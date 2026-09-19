using System.Collections.Immutable;

namespace ModularAudience.Audio.Processors_V4
{
    public sealed record DeterministicSeparationSettings
    {
        public int WindowSize { get; init; } = 4096;
        public int MaxComponents { get; init; } = 16;
        public int Iterations { get; init; } = 80;
        public int AnalysisFrames { get; init; } = 1024;
        public int BlockFrames { get; init; } = 128;
        public int MedianFrames { get; init; } = 17;
        public int MedianBins { get; init; } = 17;
        public double SeparationMargin { get; init; } = 2.0;
        public double MaskFloor { get; init; } = 0.001;
        public double TransientPreservation { get; init; } = 0.75;
        public int Threads { get; init; } = Math.Max(1, Environment.ProcessorCount / 2);
        public int HopSize => this.WindowSize / 4;
        public bool UseCqtAnalysis { get; init; }
        public bool UseCqtSynthesis { get; init; }
        public bool UsePyin { get; init; }
        public bool UseIlrma { get; init; }
        public int CqtBinsPerOctave { get; init; } = 12;
        public double CqtMinimumHz { get; init; } = 27.5;
        public double PyinMinimumHz { get; init; } = 27.5;
        public double PyinMaximumHz { get; init; } = 1500;
        public int IlrmaIterations { get; init; } = 80;
        public int IlrmaComponents { get; init; } = 2;
        public InstrumentEnsembleMode EnsembleMode { get; init; }
        public ImmutableArray<InstrumentProfileId> InstrumentProfiles { get; init; } = [];

        public bool IsEquivalentTo(DeterministicSeparationSettings other)
        {
            return this.InstrumentProfiles.AsSpan().SequenceEqual(other.InstrumentProfiles.AsSpan())
                && (this with { InstrumentProfiles = [] }) == (other with { InstrumentProfiles = [] });
        }

        public void Validate()
        {
            Require(this.WindowSize is >= 256 and <= 16384 && (this.WindowSize & (this.WindowSize - 1)) == 0, nameof(this.WindowSize));
            Require(this.MaxComponents is >= 1 and <= 32, nameof(this.MaxComponents));
            Require(this.Iterations is >= 1 and <= 500, nameof(this.Iterations));
            Require(this.AnalysisFrames is >= 32 and <= 8192, nameof(this.AnalysisFrames));
            Require(this.BlockFrames is >= 8 and <= 512, nameof(this.BlockFrames));
            Require(this.MedianFrames is >= 3 and <= 65 && this.MedianFrames % 2 == 1, nameof(this.MedianFrames));
            Require(this.MedianBins is >= 3 and <= 65 && this.MedianBins % 2 == 1, nameof(this.MedianBins));
            Require(double.IsFinite(this.SeparationMargin) && this.SeparationMargin is >= 1 and <= 10, nameof(this.SeparationMargin));
            Require(double.IsFinite(this.MaskFloor) && this.MaskFloor is >= 0 and <= 0.05, nameof(this.MaskFloor));
            Require(double.IsFinite(this.TransientPreservation) && this.TransientPreservation is >= 0 and <= 1, nameof(this.TransientPreservation));
            Require(this.Threads >= 1 && this.Threads <= Environment.ProcessorCount, nameof(this.Threads));
            this.ValidateAdvanced();
        }

        private void ValidateAdvanced()
        {
            Require(this.CqtBinsPerOctave is 12 or 24 or 36, nameof(this.CqtBinsPerOctave));
            Require(double.IsFinite(this.CqtMinimumHz) && this.CqtMinimumHz is >= 20 and <= 500, nameof(this.CqtMinimumHz));
            Require(double.IsFinite(this.PyinMinimumHz) && this.PyinMinimumHz is >= 20 and <= 1000, nameof(this.PyinMinimumHz));
            Require(double.IsFinite(this.PyinMaximumHz) && this.PyinMaximumHz > this.PyinMinimumHz && this.PyinMaximumHz <= 5000, nameof(this.PyinMaximumHz));
            Require(this.IlrmaIterations is >= 1 and <= 500, nameof(this.IlrmaIterations));
            Require(this.IlrmaComponents is >= 1 and <= 8, nameof(this.IlrmaComponents));
            Require(Enum.IsDefined(this.EnsembleMode), nameof(this.EnsembleMode));
            Require(!this.InstrumentProfiles.IsDefault && this.InstrumentProfiles.All(Enum.IsDefined)
                && this.InstrumentProfiles.Distinct().Count() == this.InstrumentProfiles.Length, nameof(this.InstrumentProfiles));
            if (this.EnsembleMode != InstrumentEnsembleMode.Automatic && this.InstrumentProfiles.IsEmpty)
                throw new ArgumentException("Select at least one instrument profile for a guided ensemble.");
            if (this.UseIlrma && this.UseCqtSynthesis)
                throw new ArgumentException("Stereo ILRMA uses STFT demixing. Disable CQT synthesis or ILRMA; CQT analysis remains available.");
            if (this.UseIlrma && this.EnsembleMode != InstrumentEnsembleMode.Automatic && this.InstrumentProfiles.Length > 2)
                throw new ArgumentException("Stereo ILRMA supports at most two spatial sources and two instrument profiles.");
            if (!this.UseIlrma && this.EnsembleMode != InstrumentEnsembleMode.Automatic && this.InstrumentProfiles.Length > this.MaxComponents)
                throw new ArgumentException("Max components must be at least the number of selected instrument profiles.");
        }

        private static void Require(bool valid, string parameter)
        {
            if (!valid)
            {
                throw new ArgumentOutOfRangeException(parameter, "Invalid deterministic separation setting.");
            }
        }
    }

    public sealed record DeterministicSeparationProgress(double Fraction, string Stage);

    public sealed record DeterministicSourceDescriptor(
        int Id, string Name, string Character, double Confidence,
        double EnergyFraction, double FundamentalHz, double Pan);

    public sealed class DeterministicSeparationAnalysis
    {
        internal DeterministicSeparationAnalysis(DeterministicAudioSnapshot source,
            DeterministicSeparationSettings settings, DeterministicSourceModel model,
            IReadOnlyList<string> warnings)
        {
            this.Source = source;
            this.Settings = settings;
            this.Model = model;
            this.Warnings = warnings;
        }

        internal DeterministicAudioSnapshot Source { get; }
        internal DeterministicSourceModel Model { get; }
        public DeterministicSeparationSettings Settings { get; }
        public IReadOnlyList<DeterministicSourceDescriptor> Sources => this.Model.Sources;
        public int EstimatedSignalRank => this.Model.EstimatedRank;
        public IReadOnlyList<string> Warnings { get; }
        public string SourceName => this.Source.Name;
        public int SampleRate => this.Source.SampleRate;
        public int Channels => this.Source.Channels;
        public long SampleCount => this.Source.Samples.LongLength;
        public long EstimatedOutputBytes => checked(this.SampleCount * sizeof(float) * (this.Sources.Count + 1));
    }

    public sealed record DeterministicSeparationResult(IReadOnlyList<AudioObj> Stems, double ReconstructionError);

    internal sealed record DeterministicAudioSnapshot(
        float[] Samples, int SampleRate, int Channels, string Name, float Bpm, string Key);
}
