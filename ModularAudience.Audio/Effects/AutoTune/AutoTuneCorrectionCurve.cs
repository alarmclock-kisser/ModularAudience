using ModularAudience.Audio.Processors_V4;

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
        AutoTuneSettings settings, CancellationToken token)
    {
        double[] raw = Enumerable.Repeat(double.NaN, pitches.Length).ToArray();
        double[] targets = Enumerable.Repeat(double.NaN, pitches.Length).ToArray();
        bool[] eligible = new bool[pitches.Length];
        double[] power = new double[mono.Length + 1];
        for (int i = 0; i < mono.Length; i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            power[i + 1] = power[i] + (double)mono[i] * mono[i];
        }
        int mask = AutoTunePitchMap.AllowedMask(settings), voiced = 0;
        double smooth = double.NaN, currentTarget = double.NaN;
        double hopSeconds = hop / (double)sampleRate;
        double noteAlpha = Alpha(settings.PitchSmoothingMilliseconds, hopSeconds);
        int radius = Math.Max(1, sampleRate / 100);
        for (int i = 0; i < pitches.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            int center = Math.Min(mono.Length, i * hop);
            int start = Math.Max(0, center - radius), end = Math.Min(mono.Length, center + radius);
            double rms = Math.Sqrt(Math.Max(0, (power[end] - power[start]) / Math.Max(1, end - start)));
            if (pitches[i].FrequencyHz <= 0 || pitches[i].VoicedProbability < settings.MinimumConfidence ||
                20 * Math.Log10(Math.Max(1e-15, rms)) < settings.SilenceThresholdDb)
            {
                smooth = currentTarget = double.NaN;
                continue;
            }
            voiced++;
            double frequency = AutoTunePitchRefiner.Refine(mono, center, sampleRate, pitches[i].FrequencyHz, token);
            if (frequency < settings.MinimumFrequencyHz || frequency > settings.MaximumFrequencyHz) frequency = pitches[i].FrequencyHz;
            raw[i] = 69 + 12 * Math.Log2(frequency / settings.ReferenceA4Hz);
            smooth = double.IsNaN(smooth) ? raw[i] : smooth + noteAlpha * (raw[i] - smooth);
            double candidate = AutoTunePitchMap.Nearest(smooth - settings.FineTuneCents / 100, mask) + settings.FineTuneCents / 100;
            if (double.IsNaN(currentTarget) || Math.Abs(smooth - currentTarget) - Math.Abs(smooth - candidate) > 2 * settings.HysteresisCents / 100)
                currentTarget = candidate;
            targets[i] = currentTarget;
        }

        for (int start = 0; start < targets.Length;)
        {
            token.ThrowIfCancellationRequested();
            if (double.IsNaN(targets[start])) { start++; continue; }
            int end = start + 1;
            while (end < targets.Length && targets[end] == targets[start]) end++;
            double firstSample = Math.Max(0, start * (double)hop - hop / 2d);
            double lastSample = Math.Min(mono.Length, end * (double)hop - hop / 2d);
            if ((lastSample - firstSample) / sampleRate + 1e-9 >= settings.MinimumNoteSeconds)
                Array.Fill(eligible, true, start, end - start);
            start = end;
        }

        double[] shifts = new double[pitches.Length];
        float[] wet = new float[pitches.Length];
        double previousShift = 0, previousWet = 0, vibratoCenter = 0, lastTarget = double.NaN;
        double vibratoAlpha = Alpha(settings.VibratoWindowMilliseconds, hopSeconds);
        int corrected = 0;
        for (int i = 0; i < shifts.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            bool active = eligible[i];
            double desired = 0;
            if (active)
            {
                vibratoCenter = lastTarget != targets[i] ? raw[i] : vibratoCenter + vibratoAlpha * (raw[i] - vibratoCenter);
                lastTarget = targets[i];
                desired = targets[i] - raw[i] + settings.VibratoPreservationPercent / 100 * (raw[i] - vibratoCenter);
                if (Math.Abs(targets[i] - raw[i]) <= settings.DeadZoneCents / 100) desired = 0;
                if (Math.Abs(desired) > settings.MaximumCorrectionSemitones) { desired = 0; active = false; }
                desired *= settings.IntensityPercent / 100;
                if (active && Math.Abs(desired) > 1e-7) corrected++;
            }
            else lastTarget = double.NaN;
            previousShift += Alpha(active ? settings.RetuneMilliseconds : settings.ReleaseMilliseconds, hopSeconds) * (desired - previousShift);
            previousWet += Alpha(settings.VoicingFadeMilliseconds, hopSeconds) * ((active ? 1 : 0) - previousWet);
            shifts[i] = previousShift;
            wet[i] = (float)previousWet;
        }
        return new(shifts, wet, hop, voiced, corrected);
    }

    private static double Alpha(double milliseconds, double hopSeconds) => milliseconds <= 0 ? 1 : 1 - Math.Exp(-hopSeconds * 1000 / milliseconds);
}
