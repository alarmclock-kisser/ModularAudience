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

        private sealed class LiveRateScrollBar : HScrollBar
        {
            protected override void WndProc(ref Message message)
            {
                const int LeftButtonDown = 0x0201;
                if (message.Msg == LeftButtonDown && (ModifierKeys & Keys.Control) != 0)
                {
                    this.Value = 0;
                    this.OnScroll(new ScrollEventArgs(ScrollEventType.ThumbPosition, 0));
                    return;
                }
                base.WndProc(ref message);
            }
        }
    }
}
