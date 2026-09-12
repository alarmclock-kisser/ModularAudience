namespace ModularAudience.Forms.Modules
{
    public partial class TrackView
    {
        private bool ApplyPlaybackRateFactor(float factor, bool fireAndForget)
        {
            bool changed = this.OriginalAudio.ManualSampleRateFactor != factor;
            this.OriginalAudio.ManualSampleRateFactor = factor;
            this.label_info_rate.Text = $"Rate: {factor * 100f:F1}%";
            if (fireAndForget && changed)
            {
                _ = this.ApplyPlaybackRateAsync();
            }
            else if (!fireAndForget)
            {
                this.OriginalAudio.SampleRateFactor = Math.Clamp(factor * this.OriginalAudio.SyncNudgeSampleRateFactor, 0.5, 2.0);
            }
            return changed;
        }
    }
}
