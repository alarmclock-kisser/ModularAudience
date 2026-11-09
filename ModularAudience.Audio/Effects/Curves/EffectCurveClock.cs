namespace ModularAudience.Audio.Effects.Curves;

/// <summary>Half-open source-frame interval. Timeline metadata, never a test for nonzero individual samples.</summary>
public readonly record struct EffectCurveSpan(int Start, int End);

/// <summary>Captured selection geometry for execution and bounce recipes. Simultaneous lanes share time rather than being concatenated.</summary>
public sealed record EffectCurveRenderScope(IReadOnlyList<EffectCurveSpan> Segments,
    IReadOnlyList<EffectCurveSpan>? SelectionSegments = null, IReadOnlyList<EffectCurveSpan>? AudibleSegments = null)
{
    public EffectCurveRenderScope Copy()
    {
        ArgumentNullException.ThrowIfNull(this.Segments);
        return new(this.Segments.ToArray(), this.SelectionSegments?.ToArray(), this.AudibleSegments?.ToArray());
    }
}

public sealed class EffectCurveClock
{
    private sealed class Clock
    {
        internal readonly EffectCurveSpan[] Spans;
        internal readonly long[] Prefix;
        internal readonly long Total;
        internal Clock(EffectCurveSpan[] spans)
        {
            this.Spans = spans; this.Prefix = new long[spans.Length];
            for (int i = 0; i < spans.Length; i++) { this.Prefix[i] = this.Total; this.Total += spans[i].End - spans[i].Start; }
        }
        internal double At(double frame)
        {
            if (this.Spans.Length == 0 || this.Total <= 1) return 0;
            int low = 0, high = this.Spans.Length;
            while (low < high) { int mid = (low + high) / 2; if (this.Spans[mid].Start <= frame) low = mid + 1; else high = mid; }
            int index = Math.Max(0, low - 1);
            double elapsed = this.Prefix[index] + Math.Clamp(frame - this.Spans[index].Start, 0, this.Spans[index].End - this.Spans[index].Start);
            return Math.Clamp(elapsed / (this.Total - 1d), 0, 1);
        }
    }
    private readonly EffectCurveSpan[] segments;
    private readonly Clock[] clocks;
    private readonly bool shared;

    public EffectCurveClock(ReadOnlySpan<float> samples, int channels, int sampleRate, EffectCurveTiming timing,
        EffectCurveRenderScope? scope = null, CancellationToken token = default)
    {
        timing.Validate();
        if (scope is not null && scope.Segments is null) throw new ArgumentException("Curve scope requires a segment list (which may be empty).");
        if (channels <= 0 || sampleRate <= 0 || samples.Length == 0 || samples.Length % channels != 0) throw new ArgumentException("Invalid curve clock audio format.");
        int frames = samples.Length / channels;
        this.segments = Normalize(scope?.Segments ?? [new(0, frames)], frames, mergeAdjacent: false);
        // An explicitly selected empty lane still has a region (and must not pin the duration of a multi-lane speed edit).
        if (this.segments.Length == 0 && timing.Scope == EffectCurveScope.PerSegment) this.segments = [new(0, frames)];
        EffectCurveSpan[]? audible = timing.IncludeSilence ? null : Normalize(scope?.AudibleSegments ??
            FindAudibleSpans(samples, channels, sampleRate, timing, token), frames);
        this.shared = timing.Scope == EffectCurveScope.AcrossSelection;
        if (this.shared)
        {
            EffectCurveSpan[] ranges = timing.IncludeGaps ? [new(0, frames)] : Normalize(scope?.SelectionSegments ?? this.segments, frames);
            this.clocks = [new(audible is null ? ranges : Intersect(ranges, audible))];
        }
        else this.clocks = this.segments.Select(segment => new Clock(audible is null ? [segment] : Intersect([segment], audible))).ToArray();
    }

    public double PositionAt(double frame)
    {
        if (!double.IsFinite(frame)) throw new ArgumentOutOfRangeException(nameof(frame));
        if (this.shared) return this.clocks[0].At(frame);
        if (this.segments.Length == 0) return 0;
        int low = 0, high = this.segments.Length;
        while (low < high) { int mid = (low + high) / 2; if (this.segments[mid].Start <= frame) low = mid + 1; else high = mid; }
        return this.clocks[Math.Max(0, low - 1)].At(frame);
    }

    public static EffectCurveSpan[] FindAudibleSpans(ReadOnlySpan<float> samples, int channels, int rate,
        EffectCurveTiming timing, CancellationToken token = default)
    {
        if (channels <= 0 || rate <= 0 || samples.Length % channels != 0) throw new ArgumentException("Invalid silence-analysis format.");
        timing.Validate();
        int frames = samples.Length / channels, minimum = Math.Max(1, (int)Math.Ceiling(timing.MinimumSilenceMs * rate / 1000));
        double threshold = Math.Pow(10, timing.SilenceThresholdDb / 20);
        var audible = new List<EffectCurveSpan>(); int quietStart = -1, activeStart = 0;
        for (int frame = 0; frame <= frames; frame++)
        {
            if ((frame & 8191) == 0) token.ThrowIfCancellationRequested();
            bool quiet = frame < frames;
            for (int channel = 0; quiet && channel < channels; channel++) quiet &= Math.Abs(samples[frame * channels + channel]) <= threshold;
            if (quiet) { if (quietStart < 0) quietStart = frame; }
            else if (quietStart >= 0)
            {
                if (frame - quietStart >= minimum)
                {
                    if (quietStart > activeStart) audible.Add(new(activeStart, quietStart));
                    activeStart = frame;
                }
                quietStart = -1;
            }
        }
        if (activeStart < frames) audible.Add(new(activeStart, frames));
        return audible.ToArray();
    }

    public static EffectCurveSpan[] Normalize(IEnumerable<EffectCurveSpan> spans, int frames, bool mergeAdjacent = true)
    {
        var result = new List<EffectCurveSpan>();
        foreach (EffectCurveSpan span in spans.OrderBy(span => span.Start).ThenBy(span => span.End))
        {
            if (span.Start < 0 || span.End > frames || span.End <= span.Start) throw new ArgumentException("Curve segments must contain complete frames within the captured region.");
            if (result.Count > 0 && (span.Start < result[^1].End || mergeAdjacent && span.Start == result[^1].End))
                result[^1] = result[^1] with { End = Math.Max(result[^1].End, span.End) };
            else result.Add(span);
        }
        return result.ToArray();
    }

    private static EffectCurveSpan[] Intersect(EffectCurveSpan[] left, EffectCurveSpan[] right)
    {
        var result = new List<EffectCurveSpan>(); int i = 0, j = 0;
        while (i < left.Length && j < right.Length)
        {
            int start = Math.Max(left[i].Start, right[j].Start), end = Math.Min(left[i].End, right[j].End);
            if (end > start) result.Add(new(start, end));
            if (left[i].End < right[j].End) i++; else j++;
        }
        return result.ToArray();
    }
}
