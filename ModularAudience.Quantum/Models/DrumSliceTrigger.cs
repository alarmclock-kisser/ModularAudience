namespace ModularAudience.Quantum.Models;

public record DrumSliceTrigger(
    int StepIndex,
    bool IsTriggered,
    double Velocity,
    double PitchShiftCents,
    double TimeStretchFactor,
    double SliceCutOffsetMs);
