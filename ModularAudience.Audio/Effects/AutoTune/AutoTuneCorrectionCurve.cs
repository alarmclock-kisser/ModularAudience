using ModularAudience.Audio.Processors_V4;
using ModularAudience.Audio.Effects.Curves;

namespace ModularAudience.Audio.Effects.AutoTune;

internal sealed record AutoTuneCorrectionCurve(double[] Semitones, float[] WetEnvelope, int HopSize, int VoicedFrames, int CorrectedFrames)
{
    internal double ShiftAt(int frame) => Interpolate(this.Semitones, frame / (double)this.HopSize);
    internal double WetAt(int frame)
    {
        double position = Math.Clamp(frame / (double)this.HopSize, 0, this.WetEnvelope.Length - 1);
        int left = (int)position, right = Math.Min(left + 1, this.WetEnvelope.Length - 1);
        return this.WetEnvelope[left] + (this.WetEnvelope[right] - this.WetEnvelope[left]) * (position - left);
    }

    private static double Interpolate(double[] values, double position)
    {
        position = Math.Clamp(position, 0, values.Length - 1);
        int left = (int)position, right = Math.Min(left + 1, values.Length - 1);
        return values[left] + (values[right] - values[left]) * (position - left);
    }
}

internal static class AutoTuneCurveBuilder
{
    internal static AutoTuneCorrectionCurve Build(float[] mono, PyinPitchFrame[] pitches, int sampleRate, int hop,
        AutoTuneSettings settings, CancellationToken token, EffectAutomationEvaluator? automation = null)
    {
        double V(string key, int frame) => automation?.Value(key, frame) ?? AutoTuneCurveTargets.BaseValue(settings, key);
        double[] raw = Enumerable.Repeat(double.NaN, pitches.Length).ToArray();
        double[] targets = Enumerable.Repeat(double.NaN, pitches.Length).ToArray();
        double[] targetNotes = Enumerable.Repeat(double.NaN, pitches.Length).ToArray();
        bool[] eligible = new bool[pitches.Length];
        double[] power = new double[mono.Length + 1];
        for (int i = 0; i < mono.Length; i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            power[i + 1] = power[i] + (double)mono[i] * mono[i];
        }
        int mask = AutoTunePitchMap.AllowedMask(settings), voiced = 0;
        double smooth = double.NaN, currentNote = double.NaN;
        double hopSeconds = hop / (double)sampleRate;
        int radius = Math.Max(1, sampleRate / 100);
        for (int i = 0; i < pitches.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            int center = Math.Min(mono.Length, i * hop);
            double noteAlpha = Alpha(V(nameof(settings.PitchSmoothingMilliseconds), center), hopSeconds);
            int start = Math.Max(0, center - radius), end = Math.Min(mono.Length, center + radius);
            double rms = Math.Sqrt(Math.Max(0, (power[end] - power[start]) / Math.Max(1, end - start)));
            if (pitches[i].FrequencyHz <= 0 || pitches[i].VoicedProbability < V(nameof(settings.MinimumConfidence), center) ||
                20 * Math.Log10(Math.Max(1e-15, rms)) < V(nameof(settings.SilenceThresholdDb), center))
            {
                smooth = currentNote = double.NaN;
                continue;
            }
            voiced++;
            double frequency = AutoTunePitchRefiner.Refine(mono, center, sampleRate, pitches[i].FrequencyHz, token);
            if (frequency < settings.MinimumFrequencyHz || frequency > settings.MaximumFrequencyHz) frequency = pitches[i].FrequencyHz;
            raw[i] = 69 + 12 * Math.Log2(frequency / V(nameof(settings.ReferenceA4Hz), center));
            smooth = double.IsNaN(smooth) ? raw[i] : smooth + noteAlpha * (raw[i] - smooth);
            double fineTune = V(nameof(settings.FineTuneCents), center) / 100;
            double candidate = AutoTunePitchMap.Nearest(smooth - fineTune, mask);
            if (double.IsNaN(currentNote) || Math.Abs(smooth - (currentNote + fineTune)) - Math.Abs(smooth - (candidate + fineTune)) > 2 * V(nameof(settings.HysteresisCents), center) / 100)
                currentNote = candidate;
            targets[i] = currentNote + fineTune; targetNotes[i] = currentNote;
        }

        for (int start = 0; start < targets.Length;)
        {
            token.ThrowIfCancellationRequested();
            if (double.IsNaN(targets[start])) { start++; continue; }
            int end = start + 1;
            while (end < targets.Length && targetNotes[end] == targetNotes[start]) end++;
            double firstSample = Math.Max(0, start * (double)hop - hop / 2d);
            double lastSample = Math.Min(mono.Length, end * (double)hop - hop / 2d);
            if ((lastSample - firstSample) / sampleRate + 1e-9 >= settings.MinimumNoteSeconds)
                Array.Fill(eligible, true, start, end - start);
            start = end;
        }

        double[] shifts = new double[pitches.Length];
        float[] wet = new float[pitches.Length];
        double previousShift = 0, previousWet = 0, vibratoCenter = 0, lastTarget = double.NaN;
        int corrected = 0;
        for (int i = 0; i < shifts.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            int center = Math.Min(mono.Length, i * hop);
            double vibratoAlpha = Alpha(V(nameof(settings.VibratoWindowMilliseconds), center), hopSeconds);
            bool active = eligible[i];
            double desired = 0;
            if (active)
            {
                vibratoCenter = lastTarget != targetNotes[i] ? raw[i] : vibratoCenter + vibratoAlpha * (raw[i] - vibratoCenter);
                lastTarget = targetNotes[i];
                desired = targets[i] - raw[i] + V(nameof(settings.VibratoPreservationPercent), center) / 100 * (raw[i] - vibratoCenter);
                if (Math.Abs(targets[i] - raw[i]) <= V(nameof(settings.DeadZoneCents), center) / 100) desired = 0;
                if (Math.Abs(desired) > V(nameof(settings.MaximumCorrectionSemitones), center)) { desired = 0; active = false; }
                desired *= V(nameof(settings.IntensityPercent), center) / 100;
                if (active && Math.Abs(desired) > 1e-7) corrected++;
            }
            else lastTarget = double.NaN;
            previousShift += Alpha(V(active ? nameof(settings.RetuneMilliseconds) : nameof(settings.ReleaseMilliseconds), center), hopSeconds) * (desired - previousShift);
            previousWet += Alpha(V(nameof(settings.VoicingFadeMilliseconds), center), hopSeconds) * ((active ? 1 : 0) - previousWet);
            shifts[i] = previousShift;
            wet[i] = (float)previousWet;
        }
        return new(shifts, wet, hop, voiced, corrected);
    }

    private static double Alpha(double milliseconds, double hopSeconds) => milliseconds <= 0 ? 1 : 1 - Math.Exp(-hopSeconds * 1000 / milliseconds);
}
