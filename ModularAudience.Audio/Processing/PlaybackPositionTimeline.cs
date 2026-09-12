namespace ModularAudience.Audio.Processing
{
    internal sealed class PlaybackPositionTimeline
    {
        private readonly record struct Segment(long OutputStart, double SourceStart, long Frames, double Ratio);
        private readonly Segment[] segments = new Segment[4096];
        private readonly Lock gate = new();
        private int next;
        private int count;
        private long outputFrames;
        private double sourceFrames;

        public void Add(int frames, double ratio)
        {
            if (frames <= 0) { return; }
            lock (this.gate)
            {
                int previous = (this.next + this.segments.Length - 1) % this.segments.Length;
                if (this.count > 0 && this.segments[previous].Ratio == ratio)
                {
                    Segment segment = this.segments[previous];
                    this.segments[previous] = segment with { Frames = segment.Frames + frames };
                }
                else
                {
                    this.segments[this.next] = new Segment(this.outputFrames, this.sourceFrames, frames, ratio);
                    this.next = (this.next + 1) % this.segments.Length;
                    this.count = Math.Min(this.count + 1, this.segments.Length);
                }
                this.outputFrames += frames;
                this.sourceFrames += frames * ratio;
            }
        }

        public double GetSourceFramePosition(long outputFramePosition)
        {
            lock (this.gate)
            {
                if (this.count == 0 || outputFramePosition <= 0) { return 0; }
                if (outputFramePosition >= this.outputFrames) { return this.sourceFrames; }
                int first = (this.next + this.segments.Length - this.count) % this.segments.Length;
                int low = 0;
                int high = this.count - 1;
                while (low < high)
                {
                    int middle = (low + high + 1) / 2;
                    Segment candidate = this.segments[(first + middle) % this.segments.Length];
                    if (candidate.OutputStart <= outputFramePosition) { low = middle; }
                    else { high = middle - 1; }
                }
                Segment segment = this.segments[(first + low) % this.segments.Length];
                long offset = Math.Clamp(outputFramePosition - segment.OutputStart, 0, segment.Frames);
                return segment.SourceStart + offset * segment.Ratio;
            }
        }
    }
}
