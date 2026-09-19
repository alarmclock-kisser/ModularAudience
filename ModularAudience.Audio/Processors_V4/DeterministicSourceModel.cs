namespace ModularAudience.Audio.Processors_V4
{
    internal sealed class DeterministicSourceModel
    {
        private readonly double[][] dictionary;
        private readonly DeterministicSourceGroup[] groups;
        private readonly int windowSize;

        private DeterministicSourceModel(double[][] dictionary, DeterministicSourceGroup[] groups, int rank, int windowSize)
        {
            // Ownership transfers from training. Rendering only reads these arrays and uses call-local H/masks.
            this.dictionary = dictionary;
            this.groups = groups;
            this.windowSize = windowSize;
            this.EstimatedRank = rank;
            this.Sources = Array.AsReadOnly(groups.Select(group => group.Descriptor).ToArray());
        }

        internal IReadOnlyList<DeterministicSourceDescriptor> Sources { get; }
        internal int EstimatedRank { get; }

        internal static DeterministicSourceModel Train(DeterministicAudioSnapshot source,
            DeterministicSeparationSettings settings, IProgress<DeterministicSeparationProgress>? progress,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(settings);
            settings.Validate();
            ArgumentOutOfRangeException.ThrowIfLessThan(source.Channels, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(source.SampleRate, 1);
            cancellationToken.ThrowIfCancellationRequested();
            DeterministicModelProgress reporter = new(progress);
            reporter.Report(0, "Sampling contiguous analysis windows");
            DeterministicTrainingData data = DeterministicTrainingData.Read(source, settings, reporter, cancellationToken);
            reporter.Report(0.31, "Estimating effective spectral rank with MDL");
            int rank = DeterministicRankEstimator.Estimate(data, settings, cancellationToken);
            if (rank == 0)
            {
                reporter.Report(1, "Analysis complete: no modeled sources");
                return new(DeterministicSpectrogram.Allocate(settings.WindowSize / 2 + 1, 0), [], 0, settings.WindowSize);
            }
            return Learn(data, source.SampleRate, rank, settings, reporter, cancellationToken);
        }

        private static DeterministicSourceModel Learn(DeterministicTrainingData data, int sampleRate, int rank,
            DeterministicSeparationSettings settings, DeterministicModelProgress reporter, CancellationToken token)
        {
            DeterministicNmfFit fit = DeterministicNmf.Train(data, rank, settings, reporter, token);
            reporter.Report(0.88, "Measuring harmonic, percussive and stereo evidence");
            DeterministicComponentFeatures[] features = DeterministicSourceFeatures.Measure(fit, data, sampleRate, settings, token);
            reporter.Report(0.96, "Grouping redundant components conservatively");
            DeterministicSourceGroup[] groups = DeterministicSourceGrouping.Create(features, token);
            token.ThrowIfCancellationRequested();
            DeterministicSourceModel model = new(fit.Dictionary, groups, rank, settings.WindowSize);
            reporter.Report(1, "Deterministic source analysis complete");
            return model;
        }

        internal float[][][] BuildMasks(DeterministicSpectrogram block, DeterministicSeparationSettings settings,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(block);
            ArgumentNullException.ThrowIfNull(settings);
            settings.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            if (settings.WindowSize != this.windowSize || (block.Frames > 0 && block.Bins != this.dictionary.Length))
            {
                throw new ArgumentException("The rendering window must match the learned dictionary.", nameof(settings));
            }
            // One power responsibility per source/bin is applied equally to every audio channel by the processor.
            return DeterministicSourceMasks.Build(block, this.dictionary, this.groups, settings, cancellationToken);
        }
    }
}
