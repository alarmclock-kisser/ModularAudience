using ModularAudience.Audio;
using ModularAudience.Audio.Arrangement;

namespace ModularAudience.Forms.Modules;

public sealed partial class MixTrackView
{
    private Task mixerUpdateWork = Task.CompletedTask;
    private CancellationTokenSource? mixerUpdateCancellation;
    private int mixerRevision;
    private bool mixerRequested;

    private void InitializeLiveMixer()
    {
        this.liveUpdateTimer.Interval = 25;
        this.liveUpdateTimer.Tick += async (_, _) => { this.liveUpdateTimer.Stop(); await this.FlushLiveGainMixAsync(); };
    }
    private void RequestLiveGainMix()
    {
        this.mixerRevision++; this.mixerRequested = true; this.mixerUpdateCancellation?.Cancel();
        if (!this.liveUpdateTimer.Enabled) this.liveUpdateTimer.Start();
    }
    private Task RefreshPreparedMixerAsync()
    {
        if (released || closing) return Task.CompletedTask;
        this.mixerRevision++; this.mixerRequested = true; this.mixerUpdateCancellation?.Cancel();
        if (this.mixerUpdateWork.IsCompleted) this.mixerUpdateWork = this.MixPendingAsync();
        return this.mixerUpdateWork;
    }
    private async Task FlushLiveGainMixAsync()
    {
        this.liveUpdateTimer.Stop();
        if (released || closing) return;
        if (this.mixerRequested && this.mixerUpdateWork.IsCompleted) this.mixerUpdateWork = this.MixPendingAsync();
        await this.mixerUpdateWork;
    }
    private async Task MixPendingAsync()
    {
        while (this.mixerRequested && !released && !closing)
        {
            if (!preparationCache.CanMix(Arrangement)) return; // Preparation will pick up the latest gain when ready.
            this.mixerRequested = false; int generation = this.mixerRevision;
            using var snapshot = this.RenderSnapshot(); string key = MixPreparationCache.AudioKey(snapshot);
            using var cancellation = new CancellationTokenSource(); this.mixerUpdateCancellation = cancellation;
            try
            {
                // Never modify the currently playing array in place. A cancelled
                // older mouse position must not leave a half-updated audible buffer.
                float[] data = await Task.Run(() => preparationCache.Mix(snapshot, token: cancellation.Token), cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (!released && !closing && generation == this.mixerRevision && key == MixPreparationCache.AudioKey(Arrangement))
                    this.InstallPreparedMixer(data, key);
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (!released && !closing) { status.Text = "Mixer update failed: " + error.Message; LogManager.Log(error); }
            }
            finally { if (ReferenceEquals(this.mixerUpdateCancellation, cancellation)) this.mixerUpdateCancellation = null; }
        }
    }
    private void CancelLiveGainMix()
    {
        this.mixerRequested = false; this.mixerRevision++; this.mixerUpdateCancellation?.Cancel();
    }
}
