namespace ModularAudience.Audio.Vocal;

public static class VocalSynth
{
    internal const int MaxOutputSamples = 30_000_000;

    public static AudioObj Render(VocalSynthSettings settings, CancellationToken cancellationToken = default)
    {
        settings.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        var s = settings.Clone();
        if (s.UseWindowsSpeech) return VocalWindowsSpeech.Render(s, cancellationToken);
        var tokens = VocalTextParser.Parse(s.Language, s.Text, s.EmphasisSyntax, cancellationToken);
        var plan = CreateRenderPlan(tokens, s, cancellationToken, out int sampleCount);
        var pcm = new List<float>(sampleCount);
        var random = new Random(StableSeed(s));
        var state = new VoiceState();

        for (int i = 0; i < plan.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var unit = plan[i];
            if (unit.Token is VocalPauseToken)
            {
                AppendSilence(pcm, unit.Frames, s.Channels, cancellationToken);
                state.Frame += unit.Frames;
            }
            else if (unit.Token is VocalPhonemeToken token)
            {
                var phoneme = VocalPhonemeInventory.Get(token.Phoneme);
                if (phoneme.Symbol == "sil")
                {
                    AppendSilence(pcm, unit.Frames, s.Channels, cancellationToken);
                    state.Frame += unit.Frames;
                    continue;
                }
                var previous = GetNeighbor(plan, i - 1);
                var next = GetNeighbor(plan, i + 1);
                RenderPhoneme(pcm, phoneme, token, previous, next, unit.Frames, s, state, random, cancellationToken);
            }
        }

        return CreateAudio(pcm.ToArray(), s, cancellationToken);
    }

    internal static AudioObj CreateAudio(float[] data, VocalSynthSettings s, CancellationToken cancellationToken)
    {
        float peak = 0;
        for (int i = 0; i < data.Length; i++)
        {
            if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (!float.IsFinite(data[i]))
                throw new InvalidOperationException("Vocal synthesis produced an invalid audio sample.");
            peak = Math.Max(peak, Math.Abs(data[i]));
        }
        if (peak > 0.95f)
        {
            float scale = 0.95f / peak;
            for (int i = 0; i < data.Length; i++)
            {
                if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                data[i] *= scale;
            }
        }

        var result = new AudioObj
        {
            Data = data,
            SampleRate = s.SampleRate,
            Channels = s.Channels,
            BitDepth = 32,
            Length = data.Length,
            Duration = TimeSpan.FromSeconds(data.Length / (double)(s.SampleRate * s.Channels)),
            Bpm = 120,
            Volume = 100,
        };
        result.Rename($"{s.VoiceName} \u00b7 {s.Language.DisplayName()} \u00b7 Vocal");
        return result;
    }

    private static List<RenderUnit> CreateRenderPlan(
        IReadOnlyList<VocalToken> tokens, VocalSynthSettings settings, CancellationToken cancellationToken, out int sampleCount)
    {
        var plan = new List<RenderUnit>(tokens.Count);
        long samples = 0;
        foreach (var token in tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double milliseconds;
            int minimumFrames;
            if (token is VocalPauseToken pause)
            {
                milliseconds = pause.Milliseconds;
                minimumFrames = 1;
            }
            else if (token is VocalPhonemeToken phoneme)
            {
                if (!VocalPhonemeInventory.Contains(phoneme.Phoneme))
                    throw new InvalidOperationException($"Unknown vocal phoneme: {phoneme.Phoneme}");
                milliseconds = VocalPhonemeInventory.Get(phoneme.Phoneme).DurationMs * DurationMultiplier(phoneme);
                minimumFrames = 4;
            }
            else throw new InvalidOperationException("Unknown vocal synthesis token.");

            if (!double.IsFinite(milliseconds) || milliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(tokens), "A vocal token has an invalid duration.");
            double frames = milliseconds * settings.SampleRate / (1000 * settings.Speed);
            if (!double.IsFinite(frames) || frames > MaxOutputSamples / settings.Channels)
                throw new InvalidOperationException("The vocal synthesis output would exceed the supported size.");
            int frameCount = Math.Max(minimumFrames, (int)Math.Round(frames));
            samples += (long)frameCount * settings.Channels;
            if (samples > MaxOutputSamples)
                throw new InvalidOperationException("The vocal synthesis output would exceed the supported size.");
            plan.Add(new RenderUnit(token, frameCount));
        }
        if (plan.Count == 0)
            throw new ArgumentException("Text contains no pronounceable characters or speech markers.", nameof(settings));
        sampleCount = (int)samples;
        return plan;
    }

    private static double DurationMultiplier(VocalPhonemeToken token)
    {
        double duration = token.Emphasis switch
        {
            VocalEmphasis.Strong => 1.1,
            VocalEmphasis.Lengthened => 1.5,
            _ => 1.0,
        };
        return duration * (token.Stressed ? 1.12 : 1.0);
    }

    private static void AppendSilence(List<float> pcm, int frames, int channels, CancellationToken cancellationToken)
    {
        for (int i = 0; i < frames * channels; i++)
        {
            if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            pcm.Add(0f);
        }
    }

    private static VocalPhoneme? GetNeighbor(List<RenderUnit> plan, int index) =>
        index >= 0 && index < plan.Count && plan[index].Token is VocalPhonemeToken token
            ? VocalPhonemeInventory.Get(token.Phoneme)
            : null;

    private static void RenderPhoneme(
        List<float> pcm, VocalPhoneme ph, VocalPhonemeToken token, VocalPhoneme? previous, VocalPhoneme? next,
        int frames, VocalSynthSettings s, VoiceState state, Random random, CancellationToken cancellationToken)
    {
        bool animal = ph.Symbol.StartsWith("animal-", StringComparison.Ordinal);
        bool connectedBefore = CanConnect(previous, ph);
        bool connectedAfter = CanConnect(ph, next);
        int fadeIn = connectedBefore ? 0 : Math.Max(1, Math.Min(frames / 4, (int)(s.SampleRate * 0.006)));
        int fadeOut = connectedAfter ? 0 : Math.Max(1, Math.Min(frames / 4, (int)(s.SampleRate * 0.008)));
        var (pitchMultiplier, gainMultiplier, voicingMultiplier, breathExtra) = EmphasisParameters(token.Emphasis);
        if (token.Stressed) pitchMultiplier *= 1.035;
        if (animal) pitchMultiplier *= AnimalPitch(ph.Symbol);

        double maxFrequency = Math.Min(6000, s.SampleRate * 0.45);
        var weights = new double[(int)(maxFrequency / (s.BasePitchHz * 0.7)) + 3];
        var formants = new double[ph.Formants.Length];
        var breathFilters = new Resonator[ph.Formants.Length];
        var noiseFilter = CreateNoiseFilter(ph.Symbol, s.SampleRate);
        double breathiness = Math.Min(1.5, s.Breathiness + breathExtra);
        double whisperReduction = 1 - 0.75 * Math.Clamp((s.Breathiness - 0.45) * 2, 0, 1);
        int harmonicCount = 0;
        double phase = state.Phase;

        for (int i = 0; i < frames; i++)
        {
            if ((i & 127) == 0) cancellationToken.ThrowIfCancellationRequested();
            double progress = i / (double)Math.Max(1, frames - 1);
            double time = state.Frame / (double)s.SampleRate;
            double pitch = s.BasePitchHz * pitchMultiplier;
            if (animal)
                pitch *= 1.05 - 0.10 * progress;
            else
                pitch *= 1.015 - 0.03 * progress;
            if (token.Emphasis == VocalEmphasis.PitchUp)
                pitch *= 0.98 + 0.08 * progress;
            if (s.VibratoCents > 0 && token.Emphasis != VocalEmphasis.Whisper)
            {
                double vibrato = s.VibratoCents * Math.Min(1, i / (s.SampleRate * 0.2))
                    * Math.Sin(2 * Math.PI * s.VibratoHz * time);
                pitch *= Math.Pow(2, vibrato / 1200);
            }
            if (s.Roughness > 0)
                pitch *= 1 + 0.02 * s.Roughness * Math.Sin(2 * Math.PI * 17 * time);

            if ((i & 63) == 0 && ph.Voiced)
            {
                UpdateFormants(ph, previous, next, connectedBefore, connectedAfter, progress, s.FormantScale, formants);
                harmonicCount = UpdateHarmonics(weights, formants, ph.FormantAmplitudes, pitch, maxFrequency);
                for (int f = 0; f < formants.Length; f++)
                    breathFilters[f].Configure(formants[f], 110 + f * 65, s.SampleRate);
            }

            double source = 0;
            if (ph.Voiced)
            {
                for (int h = 1; h <= harmonicCount; h++)
                    source += weights[h] * Math.Sin(phase * h);
                source *= 0.7 * ph.Voicing * s.Voicing * voicingMultiplier * whisperReduction;
            }

            double white = random.NextDouble() * 2 - 1;
            double bandNoise = noiseFilter.Process(white);
            double noise = bandNoise * ph.Noise * s.Noise;
            double voiceGate = 1;
            double noiseGate = 1;
            ShapeConsonant(ph.Symbol, progress, frames / (double)s.SampleRate, ref voiceGate, ref noiseGate);
            double value = source * voiceGate + noise * noiseGate;
            if (ph.Voiced && breathiness > 0)
            {
                double breath = 0;
                for (int f = 0; f < breathFilters.Length; f++)
                    breath += breathFilters[f].Process(white) * ph.FormantAmplitudes[f];
                value += breath * breathiness * 0.85 * voiceGate;
            }
            if (ph.Symbol == "animal-dot")
                value += bandNoise * s.Noise * 0.8 * Math.Exp(-i / (s.SampleRate * 0.006));

            double envelope = 1;
            if (fadeIn > 0 && i < fadeIn) envelope *= i / (double)fadeIn;
            if (fadeOut > 0 && i >= frames - fadeOut) envelope *= (frames - 1 - i) / (double)fadeOut;
            float sample = (float)(value * envelope * gainMultiplier);
            for (int channel = 0; channel < s.Channels; channel++) pcm.Add(sample);

            phase += 2 * Math.PI * pitch / s.SampleRate;
            if (phase >= 2 * Math.PI) phase -= 2 * Math.PI * Math.Floor(phase / (2 * Math.PI));
            state.Frame++;
        }
        state.Phase = phase;
    }

    private static (double Pitch, double Gain, double Voicing, double Breath) EmphasisParameters(VocalEmphasis emphasis) =>
        emphasis switch
        {
            VocalEmphasis.Strong => (1.08, 1.2, 1.0, 0),
            VocalEmphasis.Whisper => (1.0, 0.8, 0.12, 0.75),
            VocalEmphasis.PitchUp => (1.08, 1.0, 1.0, 0),
            VocalEmphasis.PitchDown => (0.88, 1.0, 1.0, 0),
            _ => (1.0, 1.0, 1.0, 0),
        };

    private static double AnimalPitch(string symbol) => symbol switch
    {
        "animal-a" => 1.18, "animal-e" => 1.28, "animal-i" => 1.38,
        "animal-o" => 1.12, "animal-u" => 1.22, "animal-dot" => 1.3, _ => 1.0,
    };

    private static bool CanConnect(VocalPhoneme? a, VocalPhoneme? b) =>
        a is { Voiced: true } && b is { Voiced: true }
        && !a.Symbol.StartsWith("animal-", StringComparison.Ordinal)
        && !b.Symbol.StartsWith("animal-", StringComparison.Ordinal)
        && !IsClosure(a.Symbol) && !IsClosure(b.Symbol);

    private static bool IsClosure(string symbol) => symbol is "p" or "b" or "t" or "d" or "k" or "g"
        or "ts" or "tsh" or "j" or "dz";

    private static void ShapeConsonant(string symbol, double progress, double duration, ref double voice, ref double noise)
    {
        bool stop = symbol is "p" or "b" or "t" or "d" or "k" or "g";
        bool affricate = symbol is "ts" or "tsh" or "j" or "dz";
        if (!stop && !affricate) return;
        double closure = stop ? 0.48 : 0.25;
        double releaseTime = Math.Max(0, (progress - closure) * duration);
        voice = 0.15 + 0.85 * Math.Min(1, releaseTime / 0.004);
        noise = progress < closure ? 0 : stop ? Math.Exp(-releaseTime * 70) + 0.2 * Math.Exp(-releaseTime * 16) : 1;
    }

    private static void UpdateFormants(
        VocalPhoneme ph, VocalPhoneme? previous, VocalPhoneme? next, bool connectedBefore, bool connectedAfter,
        double progress, double scale, double[] formants)
    {
        double glide = Math.Clamp((progress - 0.2) / 0.65, 0, 1);
        glide = glide * glide * (3 - 2 * glide);
        for (int f = 0; f < formants.Length; f++)
        {
            double start = ph.Formants[f];
            double end = ph.EndFormants.Length > f ? ph.EndFormants[f] : start;
            double value = start + (end - start) * glide;
            if (connectedBefore && previous is not null && previous.Formants.Length > f && progress < 0.25)
            {
                double previousEnd = previous.EndFormants.Length > f ? previous.EndFormants[f] : previous.Formants[f];
                double boundary = (previousEnd + start) * 0.5;
                value = boundary + (value - boundary) * progress / 0.25;
            }
            if (connectedAfter && next is not null && next.Formants.Length > f && progress > 0.75)
            {
                double boundary = (end + next.Formants[f]) * 0.5;
                value += (boundary - value) * (progress - 0.75) / 0.25;
            }
            formants[f] = value * scale;
        }
    }

    private static int UpdateHarmonics(double[] weights, double[] formants, double[] amplitudes, double pitch, double maxFrequency)
    {
        int count = Math.Min(weights.Length - 1, (int)(maxFrequency / pitch));
        for (int h = 1; h <= count; h++)
        {
            double frequency = h * pitch;
            double response = 0.045;
            for (int f = 0; f < formants.Length; f++)
            {
                double bandwidth = 100 + 50 * f;
                double distance = (frequency - formants[f]) / bandwidth;
                response += amplitudes[f] / (1 + distance * distance);
            }
            weights[h] = response / Math.Pow(h, 1.1);
        }
        return count;
    }

    private static NoiseFilter CreateNoiseFilter(string symbol, int rate)
    {
        var (low, high) = symbol switch
        {
            "s" or "z" or "ts" or "dz" => (3500.0, 9500.0),
            "sh" or "zh" or "tsh" or "j" => (1800.0, 6500.0),
            "ich" => (2600.0, 7000.0),
            "ch" => (900.0, 4200.0),
            "t" or "d" => (2500.0, 8500.0),
            "k" or "g" => (1200.0, 5500.0),
            "p" or "b" or "animal-dot" => (400.0, 3200.0),
            _ => (1000.0, 7000.0),
        };
        return new NoiseFilter(low, high, rate);
    }

    internal static int StableSeed(VocalSynthSettings settings)
    {
        uint hash = 2166136261;
        foreach (char c in settings.VoiceName + "\0" + settings.Text)
            hash = unchecked((hash ^ c) * 16777619);
        hash = unchecked((hash ^ (uint)settings.Language) * 16777619);
        return unchecked((int)hash);
    }

    private sealed class VoiceState
    {
        public double Phase;
        public long Frame;
    }

    private readonly record struct RenderUnit(VocalToken Token, int Frames);

    private struct NoiseFilter
    {
        private readonly double lowAlpha;
        private readonly double highAlpha;
        private double low;
        private double high;

        public NoiseFilter(double lowFrequency, double highFrequency, int rate)
        {
            highFrequency = Math.Min(highFrequency, rate * 0.45);
            lowFrequency = Math.Min(lowFrequency, highFrequency * 0.7);
            lowAlpha = 1 - Math.Exp(-2 * Math.PI * lowFrequency / rate);
            highAlpha = 1 - Math.Exp(-2 * Math.PI * highFrequency / rate);
        }

        public double Process(double input)
        {
            low += lowAlpha * (input - low);
            high += highAlpha * (input - high);
            return high - low;
        }
    }

    private struct Resonator
    {
        private double b0, a1, a2, x1, x2, y1, y2;

        public void Configure(double frequency, double bandwidth, int rate)
        {
            frequency = Math.Clamp(frequency, 30, rate * 0.45);
            double omega = 2 * Math.PI * frequency / rate;
            double alpha = Math.Sin(omega) * bandwidth / (2 * frequency);
            b0 = alpha / (1 + alpha);
            a1 = -2 * Math.Cos(omega) / (1 + alpha);
            a2 = (1 - alpha) / (1 + alpha);
        }

        public double Process(double input)
        {
            double output = b0 * (input - x2) - a1 * y1 - a2 * y2;
            x2 = x1;
            x1 = input;
            y2 = y1;
            y1 = output;
            return output;
        }
    }
}
