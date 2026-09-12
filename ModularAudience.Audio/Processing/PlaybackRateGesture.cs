namespace ModularAudience.Audio.Processing
{
    public sealed class PlaybackRateGesture
    {
        public const int StationaryTimeoutMilliseconds = 60;
        private int lastPosition;
        private long lastMovementMilliseconds;
        public float Factor { get; private set; } = 1f;

        public float Update(int position, bool isPlaying, long nowMilliseconds)
        {
            position = Math.Clamp(position, -500, 500);
            if (!isPlaying)
            {
                this.Reset(position);
                return this.Factor;
            }
            if (position != this.lastPosition)
            {
                this.lastPosition = position;
                this.lastMovementMilliseconds = nowMilliseconds;
                this.Factor = MapFactor(position);
            }
            return this.Factor;
        }

        public bool Expire(bool isPlaying, long nowMilliseconds)
        {
            if (this.Factor == 1f || (isPlaying && nowMilliseconds - this.lastMovementMilliseconds < StationaryTimeoutMilliseconds))
            {
                return false;
            }
            this.Factor = 1f;
            return true;
        }

        public void Reset(int position)
        {
            this.lastPosition = Math.Clamp(position, -500, 500);
            this.Factor = 1f;
        }

        public static float MapFactor(int position) => (float) Math.Pow(2.0, Math.Clamp(position / 500.0, -1.0, 1.0));
    }
}
