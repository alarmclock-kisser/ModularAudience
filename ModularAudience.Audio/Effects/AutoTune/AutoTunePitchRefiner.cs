namespace ModularAudience.Audio.Effects.AutoTune;

/// <summary>Refines pYIN's discrete pitch grid locally without changing its octave/voicing decision.</summary>
internal static class AutoTunePitchRefiner
{
    internal static double Refine(float[] mono, int center, int sampleRate, double decodedHz, CancellationToken token)
    {
        double period = sampleRate / decodedHz;
        int lag = (int)Math.Round(period);
        if (lag < 4) return decodedHz;
        int pairs = Math.Max(32, (int)Math.Ceiling(3 * period));
        int needed = pairs + lag + 4;
        if (mono.Length < needed) return decodedHz;
        int start = Math.Clamp(center - needed / 2, 0, mono.Length - needed);
        int best = lag;
        double minimum = double.PositiveInfinity;
        for (int candidate = lag - 2; candidate <= lag + 2; candidate++)
        {
            double value = Difference(mono, start, pairs, candidate, token);
            if (value < minimum) { minimum = value; best = candidate; }
        }
        double left = Difference(mono, start, pairs, best - 1, token);
        double right = Difference(mono, start, pairs, best + 1, token);
        double denominator = left - 2 * minimum + right;
        double offset = denominator > 1e-20 ? Math.Clamp(0.5 * (left - right) / denominator, -0.5, 0.5) : 0;
        double refined = sampleRate / (best + offset);
        // Never let this local interpolation override the decoded note/octave.
        return double.IsFinite(refined) && Math.Abs(12 * Math.Log2(refined / decodedHz)) <= 0.2 ? refined : decodedHz;
    }

    private static double Difference(float[] data, int start, int count, int lag, CancellationToken token)
    {
        double sum = 0;
        for (int i = 0; i < count; i++)
        {
            if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
            double delta = (double)data[start + i] - data[start + i + lag];
            sum += delta * delta;
        }
        return sum;
    }
}
