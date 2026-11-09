using ModularAudience.Audio.Effects.Curves;

namespace ModularAudience.Audio.Effects;

/// <summary>Per-run resource budget, deliberately separate from effect sound/preset parameters.</summary>
public sealed record AudioEffectExecutionOptions
{
    public static int DefaultMaximumThreads => Math.Max(1, Environment.ProcessorCount / 2);
    public int MaximumThreads { get; init; } = DefaultMaximumThreads;
    public EffectCurveRenderScope? CurveScope { get; init; }

    public void Validate()
    {
        if (this.MaximumThreads < 1 || this.MaximumThreads > Environment.ProcessorCount)
            throw new ArgumentOutOfRangeException(nameof(this.MaximumThreads), $"Max threads must be between 1 and {Environment.ProcessorCount}.");
    }
}
