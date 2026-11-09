namespace ModularAudience.Quantum.Models;

public sealed record QuantumBeatNote(int TrackIndex, double StartStep, double DurationSteps, double Velocity,
    double PitchShiftCents, double TimeStretchFactor, double VarispeedFactor, double SliceCutOffsetMs,
    bool Reverse, bool IsGhost, string Measurement);

public sealed record QuantumBeatSequence(string CircuitQasm, IReadOnlyList<QuantumBeatNote> Notes,
    IReadOnlyDictionary<string, int> RawCounts, TimeSpan ExecutionLatency, QuantumBeatOptions Options,
    IReadOnlyList<QuantumTrackMapping> Tracks, int QubitCount);

/// <summary>Portable project state. Deliberately excludes credentials, backend tokens and audio PCM.</summary>
public sealed record QuantumExperimentDocument(int Version, QuantumCircuitDefinition Circuit,
    QuantumBeatOptions Options, IReadOnlyList<QuantumTrackMapping> Tracks, QuantumCircuitRandomization Randomization);
