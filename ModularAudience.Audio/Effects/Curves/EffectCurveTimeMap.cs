namespace ModularAudience.Audio.Effects.Curves;

/// <summary>Monotonic integral of local duration factors in source time; bounded O(inputFrames / 32) storage.</summary>
internal sealed class EffectCurveTimeMap
{
    private const int Step = 32;
    private readonly double[] times;
    private readonly int inputFrames;
    private readonly double firstFactor, lastFactor;
    internal double OutputFrames => this.times[^1];
    internal bool IsIdentity { get; }
    internal double? ConstantFactor { get; }

    internal EffectCurveTimeMap(int frames, int channels, Func<double, double> factorAt, CancellationToken token)
    {
        if (frames <= 0 || frames > 134_217_728 || channels is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(frames));
        token.ThrowIfCancellationRequested();
        this.inputFrames = frames; this.times = new double[(frames + Step - 1) / Step + 1];
        double previous = this.firstFactor = factorAt(0), elapsed = 0;
        if (!double.IsFinite(previous) || previous <= 0) throw new ArgumentException("A time curve must have a finite, positive speed everywhere.");
        bool identity = Math.Abs(previous - 1) < 1e-12;
        bool constant = true;
        for (int frame = 1; frame <= frames; frame++)
        {
            if ((frame & 8191) == 0) token.ThrowIfCancellationRequested();
            double next = factorAt(Math.Min(frame, frames - 1));
            if (!double.IsFinite(next) || next <= 0) throw new ArgumentException("A time curve must have a finite, positive speed everywhere.");
            elapsed += (previous + next) * 0.5;
            if (elapsed * channels > 134_217_728) throw new ArgumentOutOfRangeException(nameof(frames), "Curved audio would exceed the 512 MiB per-effect output limit.");
            identity &= Math.Abs(next - 1) < 1e-12;
            constant &= Math.Abs(next - this.firstFactor) <= 1e-12 * Math.Max(1, this.firstFactor);
            previous = next;
            if (frame % Step == 0 || frame == frames) this.times[(frame + Step - 1) / Step] = elapsed;
        }
        this.lastFactor = previous; this.IsIdentity = identity; this.ConstantFactor = constant ? this.firstFactor : null;
    }

    internal double SourceAt(double outputFrame)
    {
        if (outputFrame < 0) return outputFrame / this.firstFactor;
        if (outputFrame >= this.OutputFrames) return this.inputFrames + (outputFrame - this.OutputFrames) / this.lastFactor;
        int low = 0, high = this.times.Length - 1;
        while (low + 1 < high) { int mid = (low + high) / 2; if (this.times[mid] <= outputFrame) low = mid; else high = mid; }
        int start = low * Step, end = Math.Min(this.inputFrames, high * Step);
        return start + (end - start) * (outputFrame - this.times[low]) / (this.times[high] - this.times[low]);
    }
}
