using ModularAudience.Audio;

namespace ModularAudience.Forms.Modules
{
    public partial class TrackView
    {
        private float rateAnchorFactor = 1.0f;

        private void SetRateAnchor(float factor)
        {
            this.rateAnchorFactor = Math.Clamp(factor, 0.01f, 10.0f);
        }

        internal bool MatchesRateAudio(AudioObj audio) =>
            ReferenceEquals(this.OriginalAudio, audio)
            || this.OriginalAudio.Id == audio.Id
            || this.sourceAudioId == audio.Id;

        internal void SyncRateAnchorFromLoopControl()
        {
            if (this.IsDisposed || this.Disposing)
            {
                return;
            }

            this.SetRateAnchor((float)this.OriginalAudio.ManualSampleRateFactor);
            this.suppressRateSync = true;
            try
            {
                if (this.hScrollBar_rate.Value != 0)
                {
                    this.hScrollBar_rate.Value = 0;
                }
                this.UpdateRateLabel((float)this.OriginalAudio.ManualSampleRateFactor);
            }
            finally
            {
                this.suppressRateSync = false;
            }
        }

        private bool ApplyPlaybackRateFactor(float factor, bool fireAndForget)
        {
            double newFactor = Math.Clamp(factor, 0.01f, 10.0f);
            bool changed = this.OriginalAudio.ManualSampleRateFactor != (float)newFactor;
            this.OriginalAudio.ManualSampleRateFactor = (float)newFactor;
            this.UpdateRateLabel((float)newFactor);
            WindowMain.LoopControlWindow?.UpdateJumpDistanceFromTrackView(this.OriginalAudio);
            if (fireAndForget && changed)
            {
                _ = this.ApplyPlaybackRateAsync();
            }
            else if (!fireAndForget)
            {
                this.OriginalAudio.SampleRateFactor = Math.Clamp((float)newFactor * this.OriginalAudio.SyncNudgeSampleRateFactor, 0.01, 10.0);
            }
            return changed;
        }

        private void UpdateRateLabel(float factor)
        {
            this.label_info_rate.Text = $"Rate: {((factor - 1.0f) * 100.0f):+0.0;-0.0;+0.0}%";
        }
    }
}
