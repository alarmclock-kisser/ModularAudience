namespace ModularAudience.Generators
{
    public sealed record BreakbeatPatternNote(
        int TrackIndex,
        int StartTick,
        int DurationTicks,
        bool TimeStretch = false,
        bool Varispeed = false,
        bool ManuallyResized = false,
        int OriginalDurationTicks = 0,
        float PitchSemitones = 0f)
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