using ModularAudience.Audio;
using ModularAudience.Audio.Arrangement;

namespace ModularAudience.Forms.Modules;

public sealed partial class MixTrackView
{
    private void SchedulePreparation()
    {
        if (closing || released) return;
        string key = MixPreparationCache.AudioKey(Arrangement);
        if (cachedMixState == key && cachedMix != null) return;
        if (requestedPreparation == key && !liveUpdateWork.IsCompleted) return;
        if (preparationCache.CanMix(Arrangement))
        {
            liveUpdateCancellation?.Cancel(); requestedPreparation = key;
            liveUpdateWork = this.RefreshPreparedMixerAsync(); return;
        }
        liveUpdateCancellation?.Cancel();
        var source = new CancellationTokenSource(); liveUpdateCancellation = source;
        requestedPreparation = key;
        var snapshot = new MixArrangement(); snapshot.Restore(Arrangement.Capture());
        foreach (var asset in Arrangement.Sources) snapshot.Sources.Add(asset.Key, asset.Value);
        Task previous = liveUpdateWork;
        status.Text = "Preparing changed audio in background…";
        liveUpdateWork = PrepareAsync();
        async Task PrepareAsync()
        {
            try
            {
                // One preparation pipeline per view, preventing cancellation storms from
                // overlapping DSP jobs. Unchanged lane transforms stay cached.
                await previous;
                source.Token.ThrowIfCancellationRequested();
                await preparationCache.PrepareAsync(snapshot, source.Token);
                source.Token.ThrowIfCancellationRequested();
                if (!released && !closing && requestedPreparation == key) await this.RefreshPreparedMixerAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!released && !closing) { status.Text = "Preparation failed: " + error.Message; LogManager.Log(error); } }
            finally
            {
                snapshot.Dispose();
                if (ReferenceEquals(liveUpdateCancellation, source)) liveUpdateCancellation = null;
                source.Dispose();
            }
        }
    }

    private void RefreshPreparedMixer() => _ = this.RefreshPreparedMixerAsync();

    private void InstallPreparedMixer(float[] data, string key)
    {
        if (released || closing) return;
        var replacement = new AudioObj { Name = Arrangement.Name, Data = data, Channels = 2, SampleRate = 44100,
            Length = data.Length, Duration = TimeSpan.FromSeconds(data.Length / 88200.0), Bpm = (float)Arrangement.Bpm };
        if (playback != null && !ReferenceEquals(playback.Data, data))
        {
            long position = playback.Position * 2;
            playback.SwapPlaybackData(data, 44100, 2, position, loopWholeBuffer: false);
            playback.Data = data; playback.Length = data.Length; playback.Duration = replacement.Duration;
            ApplyPlaybackLoop(playback);
        }
        cachedMix?.Dispose(); cachedMix = replacement; cachedMixState = key;
        status.Text = playback != null ? "Playing — live mixer updated." : "Ready — audio prepared. Play starts from cache.";
    }
}
