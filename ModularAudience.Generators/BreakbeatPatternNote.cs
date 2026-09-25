namespace ModularAudience.Generators
{
    public enum BreakbeatPlaybackMode
    {
        TimeStretch,
        Varispeed
    }

    public sealed record BreakbeatTrackSettings(
        float DefaultVolumePercent = 100f,
        float DefaultPitchSemitones = 0f,
        BreakbeatPlaybackMode DefaultPlaybackMode = BreakbeatPlaybackMode.TimeStretch);

    public sealed record BreakbeatPatternNote(
        int TrackIndex,
        int StartTick,
        int DurationTicks,
        bool TimeStretch = false,
        bool Varispeed = false,
        bool ManuallyResized = false,
        int OriginalDurationTicks = 0,
        float PitchSemitones = 0f,
        float VolumePercent = 100f)
    {
        public bool IsManuallyAdjusted => this.ManuallyResized || this.TimeStretch || this.Varispeed;

        public bool IsTimeExtended => this.IsManuallyAdjusted
            && (this.OriginalDurationTicks > 0
                ? this.DurationTicks > this.OriginalDurationTicks
                : this.TimeStretch || this.Varispeed);

        public bool IsShortened => this.IsManuallyAdjusted
            && this.OriginalDurationTicks > 0
            && this.DurationTicks < this.OriginalDurationTicks;
    }
}