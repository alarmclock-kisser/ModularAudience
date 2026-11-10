using ModularAudience.Audio.Effects.AutoTune;
using ModularAudience.Audio.Processors_V4;

namespace ModularAudience.Audio.Vocal;

internal static class VocalSinging
{
    private static readonly int[] Melody = [0, 4, 7, 5, 2, 4, 7, 0];

    internal static int Note(int index) => Melody[Math.Max(0, index) % Melody.Length];

    internal static float[] RenderWindows(float[] source, VocalSynthSettings settings, CancellationToken cancellationToken)
    {
        int rate = settings.SampleRate;
        int hop = Math.Max(1, rate / 100);
        var pitches = PyinPitchTracker.Track(source, rate, hop, 55, Math.Min(900, rate * 0.45), 1, cancellationToken);
        var shifts = new double[pitches.Length];
        var wet = new float[pitches.Length];
        double previousShift = 0, previousWet = 0;
        bool haveShift = false;
        for (int i = 0; i < pitches.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool voiced = pitches[i].FrequencyHz > 0;
            if (voiced)
            {
                double seconds = i * (double)hop / rate;
                int note = (int)(seconds * settings.Speed / 0.32);
                double target = settings.BasePitchHz * Math.Pow(2, Note(note) / 12.0);
                double desired = 12 * Math.Log2(target / pitches[i].FrequencyHz);
                previousShift = haveShift ? previousShift + 0.55 * (desired - previousShift) : desired;
                haveShift = true;
            }
            previousWet += 0.4 * ((voiced ? 1 : 0) - previousWet);
            shifts[i] = previousShift;
            wet[i] = (float)previousWet;
        }
        if (!haveShift) return source;

        var quality = new AutoTuneSettings
        {
            FftSize = rate < 12000 ? 1024 : rate >= 96000 ? 4096 : 2048,
            Overlap = 8,
            FormantPreservationPercent = 70,
        };
        var curve = new AutoTuneCorrectionCurve(shifts, wet, hop, 0, 0);
        float[] shifted = AutoTunePhaseVocoder.Render(source, rate, 1, quality, curve, 1, null, cancellationToken);
        for (int i = 0; i < source.Length; i++)
        {
            if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            double amount = curve.WetAt(i);
            shifted[i] = (float)(source[i] + (shifted[i] - source[i]) * amount);
        }
        return shifted;
    }
}
