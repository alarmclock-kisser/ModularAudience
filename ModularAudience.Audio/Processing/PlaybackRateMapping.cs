namespace ModularAudience.Audio.Processing
{
    public static class PlaybackRateMapping
    {
        public static float MapFactor(int position) => (float) Math.Pow(2.0, Math.Clamp(position / 500.0, -1.0, 1.0));
    }
}
