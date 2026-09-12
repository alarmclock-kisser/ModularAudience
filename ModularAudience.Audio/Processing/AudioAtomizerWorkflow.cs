using ModularAudience.Audio.Processors_V4;

namespace ModularAudience.Audio.Processing
{
    public sealed record AudioAtomizeResult(
        IReadOnlyList<AudioObj> Atomics,
        bool IsLikelyDrumLoop,
        string? SummaryLog);

    /// <summary>Application-independent orchestration for loop atomization.</summary>
    public static class AudioAtomizerWorkflow
    {
        public static async Task<AudioAtomizeResult> AtomizeAsync(
            AudioObj source,
            LoopAtomizerSettings settings,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            cancellationToken.ThrowIfCancellationRequested();

            LoopAtomizerResult result = await LoopAtomizer_V4
                .AtomizeAsync(source, settings, progress)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            string? summary = result.IsLikelyDrumLoop
                ? string.Join(", ", result.Atomics
                    .Where(atomic => !string.IsNullOrWhiteSpace(atomic.SampleTag))
                    .Select(atomic => $"{atomic.Name}={atomic.SampleTag}"))
                : null;

            return new AudioAtomizeResult(result.Atomics, result.IsLikelyDrumLoop, summary);
        }
    }
}
