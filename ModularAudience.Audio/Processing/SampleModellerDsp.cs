using ModularAudience.Audio.Synthesis;

namespace ModularAudience.Audio.Processing;

/// <summary>Offline, channel-aware sample processing. Source PCM is never modified.</summary>
public static class SampleModellerDsp
{
    public static AudioObj Render(AudioObj source, SampleModellerSettings settings, CancellationToken token = default)
    {
        settings.Validate();
        if (source.SampleRate <= 0 || source.Channels <= 0 || source.Data.Length < source.Channels)
            throw new ArgumentException("The sample contains no complete audio frames.");
        int channels = source.Channels, rate = source.SampleRate;
        var data = source.Data[..(source.Data.Length / channels * channels)];
        foreach (var effect in settings.Effects.Where(e => e.Enabled))
        {
            token.ThrowIfCancellationRequested();
            if (effect.MixPercent == 0) continue;
            int frames = data.Length / channels;
            int first = Math.Clamp((int)(frames * effect.StartPercent / 100), 0, frames - 1);
            int last = Math.Clamp((int)Math.Ceiling(frames * effect.EndPercent / 100), first + 1, frames);
            var wet = data[(first * channels)..(last * channels)];
            if (effect.Kind == SampleEffectKind.Trim) { data = wet; continue; }
            wet = Process(wet, channels, rate, effect, token);
            int newLength = checked(data.Length - (last - first) * channels + wet.Length);
            var merged = new float[newLength];
            Array.Copy(data, merged, first * channels);
            double mix = effect.MixPercent / 100;
            int dryFrames = last - first, wetFrames = wet.Length / channels;
            for (int i = 0; i < wetFrames; i++)
            {
                if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                double dryPosition = i * (double)dryFrames / Math.Max(1, wetFrames);
                int a = Math.Min(dryFrames - 1, (int)dryPosition), b = Math.Min(dryFrames - 1, a + 1);
                for (int c = 0; c < channels; c++)
                {
                    double dry = data[(first + a) * channels + c] * (1 - (dryPosition - a)) + data[(first + b) * channels + c] * (dryPosition - a);
                    double value = dry * (1 - mix) + wet[i * channels + c] * mix;
                    merged[(first + i) * channels + c] = double.IsFinite(value) ? (float)Math.Clamp(value, -16, 16) : 0;
                }
            }
            Array.Copy(data, last * channels, merged, first * channels + wet.Length, data.Length - last * channels);
            data = merged;
        }
        var result = source.Clone(); result.Data = data; result.Length = data.Length;
        result.Duration = TimeSpan.FromSeconds(data.Length / (double)(channels * rate));
        result.StartingOffset = 0; result.SelectionStart = result.SelectionEnd = -1;
        result.SampleRateFactor = result.ManualSampleRateFactor = result.SyncNudgeSampleRateFactor = 1;
        result.Tag = source.Tag; result.Key = source.Key; result.SampleTag = source.SampleTag;
        result.ScannedBpm = source.ScannedBpm; result.ScannedKey = source.ScannedKey;
        return result;
    }

    private static float[] Process(float[] data, int ch, int rate, SampleEffect e, CancellationToken token)
    {
        int frames = data.Length / ch;
        double gain = Math.Pow(10, e.GainDb / 20);
        if (e.Kind == SampleEffectKind.Trim)
        {
            // Region selection is handled by Render; trim is handled there as a crop.
            return data;
        }
        if (e.Kind is SampleEffectKind.Varispeed or SampleEffectKind.PitchShift or SampleEffectKind.TimeStretch)
        {
            if (e.Kind == SampleEffectKind.Varispeed) return Resample(data, ch, Math.Max(1, (int)(frames / e.Factor)), token);
            if (e.Kind == SampleEffectKind.TimeStretch) return Stretch(data, ch, rate, Math.Max(1, (int)(frames * e.Factor)), token);
            var pitched = Resample(data, ch, Math.Max(1, (int)(frames / Math.Pow(2, e.Semitones / 12))), token);
            return Stretch(pitched, ch, rate, frames, token);
        }
        if (e.Kind == SampleEffectKind.Reverse)
        {
            for (int i = 0; i < frames / 2; i++)
            {
                if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                for (int c = 0; c < ch; c++) (data[i * ch + c], data[(frames - i - 1) * ch + c]) = (data[(frames - i - 1) * ch + c], data[i * ch + c]);
            }
            return data;
        }
        if (e.Kind == SampleEffectKind.RemoveDc)
        {
            for (int c = 0; c < ch; c++)
            {
                double mean = 0; for (int i = 0; i < frames; i++) mean += data[i * ch + c];
                mean /= frames; for (int i = 0; i < frames; i++) data[i * ch + c] -= (float)mean;
            }
            return data;
        }
        if (e.Kind == SampleEffectKind.Normalize)
        {
            double peak = data.Max(v => Math.Abs((double)v)); gain = peak > 1e-12 ? gain / peak : 1;
        }
        if (e.Kind is SampleEffectKind.LowPass or SampleEffectKind.HighPass or SampleEffectKind.BandPass or SampleEffectKind.Notch
            or SampleEffectKind.PeakEq or SampleEffectKind.LowShelf or SampleEffectKind.HighShelf)
        {
            var filters = Enumerable.Range(0, ch).Select(_ => new Biquad(e, rate)).ToArray();
            for (int i = 0; i < frames; i++)
            {
                if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                for (int c = 0; c < ch; c++) data[i * ch + c] = (float)filters[c].Apply(data[i * ch + c]);
            }
            return data;
        }
        float[] dry = (float[])data.Clone();
        var expression = e.Kind == SampleEffectKind.WaveShaper ? WaveExpression.Compile(e.Equation) : null;
        var points = e.Points.OrderBy(p => p.Position).ToArray();
        int pointIndex = 0;
        double envelope = 0;
        double attack = Math.Exp(-1 / Math.Max(1, rate * e.TimeMs / 1000)), release = Math.Exp(-1 / Math.Max(1, rate * e.Feedback / 1000));
        var phaseState = new double[ch, 4]; var phasePrevious = new double[ch, 4];
        int delayFrames = Math.Max(1, (int)(rate * e.TimeMs / 1000));
        double feedback = Math.Clamp(e.Feedback / 100, 0, 0.95);
        for (int i = 0; i < frames; i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            double position = i / (double)Math.Max(1, frames - 1), time = i / (double)rate;
            double lfo = Math.Sin(2 * Math.PI * e.Rate * time);
            double drawn = 1;
            if (points.Length > 0)
            {
                while (pointIndex + 1 < points.Length && points[pointIndex + 1].Position <= position) pointIndex++;
                var a = points[pointIndex]; var b = points[Math.Min(pointIndex + 1, points.Length - 1)];
                drawn = position <= points[0].Position ? points[0].Value : b.Position > a.Position
                    ? a.Value + (b.Value - a.Value) * Math.Clamp((position - a.Position) / (b.Position - a.Position), 0, 1) : a.Value;
            }
            double level = 0; for (int c = 0; c < ch; c++) level = Math.Max(level, Math.Abs(dry[i * ch + c]));
            envelope = level > envelope ? attack * envelope + (1 - attack) * level : release * envelope + (1 - release) * level;
            for (int c = 0; c < ch; c++)
            {
                int index = i * ch + c; double x = dry[index], value = x;
                switch (e.Kind)
                {
                    case SampleEffectKind.Gain: case SampleEffectKind.Normalize: value = x * gain; break;
                    case SampleEffectKind.Invert: value = -x; break;
                    case SampleEffectKind.Silence: value = 0; break;
                    case SampleEffectKind.FadeIn: value = x * Math.Pow(position, Math.Max(0.01, e.Amount)); break;
                    case SampleEffectKind.FadeOut: value = x * Math.Pow(1 - position, Math.Max(0.01, e.Amount)); break;
                    case SampleEffectKind.Compressor:
                        double db = 20 * Math.Log10(Math.Max(1e-12, envelope));
                        value = x * Math.Pow(10, -Math.Max(0, db - e.GainDb) * (1 - 1 / Math.Max(1, e.Amount)) / 20); break;
                    case SampleEffectKind.Limiter: value = Math.Clamp(x, -gain, gain); break;
                    case SampleEffectKind.NoiseGate: value = envelope < gain ? 0 : x; break;
                    case SampleEffectKind.SoftClip: value = Math.Tanh(x * e.Amount); break;
                    case SampleEffectKind.HardClip: value = Math.Clamp(x * e.Amount, -1, 1); break;
                    case SampleEffectKind.WaveFold: value = 2 / Math.PI * Math.Asin(Math.Sin(x * e.Amount * Math.PI / 2)); break;
                    case SampleEffectKind.Rectify: value = Math.Abs(x); break;
                    case SampleEffectKind.BitCrush:
                        int hold = Math.Max(1, (int)e.Rate); double steps = Math.Pow(2, Math.Clamp(e.Amount, 1, 24) - 1);
                        value = Math.Round(dry[(i / hold * hold) * ch + c] * steps) / steps; break;
                    case SampleEffectKind.WaveShaper: value = expression!(x); break;
                    case SampleEffectKind.DrawWaveform: value = points.Length > 0 && (e.Channel == 0 || e.Channel == c + 1) ? drawn : x; break;
                    case SampleEffectKind.VolumeEnvelope: value = e.Channel == 0 || e.Channel == c + 1 ? x * drawn : x; break;
                    case SampleEffectKind.Tremolo: value = x * (1 - e.Depth / 200 * (1 + lfo)); break;
                    case SampleEffectKind.RingModulation: value = x * Math.Sin(2 * Math.PI * e.FrequencyHz * time); break;
                    case SampleEffectKind.Pan: case SampleEffectKind.AutoPan:
                        double pan = e.Kind == SampleEffectKind.AutoPan ? lfo * e.Depth / 100 : e.Depth / 100;
                        value = ch >= 2 && c < 2 ? x * (c == 0 ? Math.Min(1, 1 - pan) : Math.Min(1, 1 + pan)) : x; break;
                    case SampleEffectKind.StereoWidth:
                        if (ch >= 2 && c < 2) { double mid = (dry[i * ch] + dry[i * ch + 1]) / 2d, side = (dry[i * ch] - dry[i * ch + 1]) / 2d * e.Amount; value = mid + (c == 0 ? side : -side); } break;
                    case SampleEffectKind.SwapChannels: if (ch >= 2 && c < 2) value = dry[i * ch + 1 - c]; break;
                    case SampleEffectKind.Mono: value = 0; for (int k = 0; k < ch; k++) value += dry[i * ch + k] / (double)ch; break;
                    case SampleEffectKind.Delay: case SampleEffectKind.PingPongDelay:
                        int sourceChannel = e.Kind == SampleEffectKind.PingPongDelay && ch >= 2 && c < 2 ? 1 - c : c;
                        value = i >= delayFrames ? dry[(i - delayFrames) * ch + sourceChannel]
                            + data[(i - delayFrames) * ch + sourceChannel] * feedback : 0; break;
                    case SampleEffectKind.Reverb:
                        value = x;
                        for (int tap = 0; tap < 8; tap++)
                        {
                            int offset = Math.Max(1, (int)(rate * (e.TimeMs / 1000 + (tap * 0.017 + c * 0.003))));
                            if (i >= offset) value += dry[(i - offset) * ch + c] * Math.Exp(-(tap + 1) / Math.Max(0.1, e.Amount)) * 0.4;
                        }
                        break;
                    case SampleEffectKind.Chorus: case SampleEffectKind.Flanger:
                        double modDelay = delayFrames * (1 + e.Depth / 100 * Math.Sin(2 * Math.PI * e.Rate * time + c * Math.PI / 2));
                        value = Read(dry, ch, c, i - modDelay) + (e.Kind == SampleEffectKind.Flanger && i >= delayFrames ? data[(i - delayFrames) * ch + c] * feedback : 0); break;
                    case SampleEffectKind.Phaser:
                        double frequency = Math.Clamp(e.FrequencyHz * Math.Pow(2, lfo * e.Depth / 50), 10, rate * 0.45);
                        double tan = Math.Tan(Math.PI * frequency / rate), coefficient = (tan - 1) / (tan + 1);
                        for (int stage = 0; stage < 4; stage++)
                        { double y = coefficient * value + phasePrevious[c, stage] - coefficient * phaseState[c, stage]; phasePrevious[c, stage] = value; phaseState[c, stage] = y; value = y; }
                        break;
                }
                data[index] = double.IsFinite(value) ? (float)Math.Clamp(value, -16, 16) : 0;
            }
        }
        return data;
    }

    private static double Read(float[] data, int ch, int channel, double frame)
    {
        int frames = data.Length / ch;
        if (frame < 0 || frame > frames - 1) return 0;
        int a = (int)frame, b = Math.Min(frames - 1, a + 1);
        return data[a * ch + channel] + (data[b * ch + channel] - data[a * ch + channel]) * (frame - a);
    }
    private static float[] Resample(float[] data, int ch, int frames, CancellationToken token)
    {
        var result = new float[checked(frames * ch)]; int oldFrames = data.Length / ch;
        for (int i = 0; i < frames; i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            for (int c = 0; c < ch; c++) result[i * ch + c] = (float)Read(data, ch, c, Math.Min(oldFrames - 1, i * (double)oldFrames / frames));
        }
        return result;
    }
    // Windowed overlap-add time-domain stretching. Each channel uses identical grain positions.
    private static float[] Stretch(float[] data, int ch, int rate, int frames, CancellationToken token)
    {
        int oldFrames = data.Length / ch;
        if (oldFrames == frames) return data;
        int grain = Math.Min(oldFrames, Math.Max(32, rate / 25));
        if (grain < 8) return Resample(data, ch, frames, token);
        int hop = Math.Max(1, grain / 4);
        var result = new float[checked(frames * ch)]; var weights = new double[frames];
        for (int start = -grain + hop; start < frames; start += hop)
        {
            token.ThrowIfCancellationRequested();
            int source = Math.Clamp((int)(start * (double)oldFrames / frames), 0, Math.Max(0, oldFrames - grain));
            for (int j = 0; j < grain; j++)
            {
                int target = start + j; if (target < 0 || target >= frames) continue;
                double window = 0.5 - 0.5 * Math.Cos(2 * Math.PI * j / Math.Max(1, grain - 1)); weights[target] += window;
                for (int c = 0; c < ch; c++) result[target * ch + c] += (float)(data[(source + j) * ch + c] * window);
            }
        }
        for (int i = 0; i < frames; i++) for (int c = 0; c < ch; c++)
            if (weights[i] > 1e-9) result[i * ch + c] /= (float)weights[i];
        return result;
    }

    private sealed class Biquad
    {
        private readonly double b0, b1, b2, a1, a2;
        private double z1, z2;
        public Biquad(SampleEffect e, int rate)
        {
            double w = 2 * Math.PI * Math.Clamp(e.FrequencyHz, 1, rate * 0.49) / rate;
            double cos = Math.Cos(w), sin = Math.Sin(w), alpha = sin / (2 * e.Q), a = Math.Pow(10, e.GainDb / 40);
            double n0 = 1, n1 = 0, n2 = 0, d0 = 1, d1 = -2 * cos, d2 = 1 - alpha;
            switch (e.Kind)
            {
                case SampleEffectKind.LowPass: n0 = (1 - cos) / 2; n1 = 1 - cos; n2 = n0; d0 = 1 + alpha; break;
                case SampleEffectKind.HighPass: n0 = (1 + cos) / 2; n1 = -1 - cos; n2 = n0; d0 = 1 + alpha; break;
                case SampleEffectKind.BandPass: n0 = alpha; n2 = -alpha; d0 = 1 + alpha; break;
                case SampleEffectKind.Notch: n0 = n2 = 1; n1 = -2 * cos; d0 = 1 + alpha; break;
                case SampleEffectKind.PeakEq: n0 = 1 + alpha * a; n1 = -2 * cos; n2 = 1 - alpha * a; d0 = 1 + alpha / a; d2 = 1 - alpha / a; break;
                case SampleEffectKind.LowShelf:
                    double low = 2 * Math.Sqrt(a) * alpha;
                    n0 = a * ((a + 1) - (a - 1) * cos + low); n1 = 2 * a * ((a - 1) - (a + 1) * cos); n2 = a * ((a + 1) - (a - 1) * cos - low);
                    d0 = (a + 1) + (a - 1) * cos + low; d1 = -2 * ((a - 1) + (a + 1) * cos); d2 = (a + 1) + (a - 1) * cos - low; break;
                case SampleEffectKind.HighShelf:
                    double high = 2 * Math.Sqrt(a) * alpha;
                    n0 = a * ((a + 1) + (a - 1) * cos + high); n1 = -2 * a * ((a - 1) + (a + 1) * cos); n2 = a * ((a + 1) + (a - 1) * cos - high);
                    d0 = (a + 1) - (a - 1) * cos + high; d1 = 2 * ((a - 1) - (a + 1) * cos); d2 = (a + 1) - (a - 1) * cos - high; break;
            }
            b0 = n0 / d0; b1 = n1 / d0; b2 = n2 / d0; a1 = d1 / d0; a2 = d2 / d0;
        }
        public double Apply(double x) { double y = b0 * x + z1; z1 = b1 * x - a1 * y + z2; z2 = b2 * x - a2 * y; return y; }
    }
}
