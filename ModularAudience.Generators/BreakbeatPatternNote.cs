namespace ModularAudience.Generators
{
    public sealed record BreakbeatPatternNote(
        int TrackIndex,
        int StartTick,
        int DurationTicks,
        bool TimeStretch = false);
}