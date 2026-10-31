using ModularAudience.Audio;
using ModularAudience.Generators;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class BreakbeatPatternEditorDialog
{
    private async void WarmNoteCache(object? sender, EventArgs e)
    {
        this.cacheWarmTimer.Stop();
        if (this.IsDisposed || this.cacheLifetime.IsCancellationRequested) return;
        if (!this.cacheWarmWork.IsCompleted) { this.cacheWarmTimer.Start(); return; }
        // Warm unique transforms without allocating a complete pattern-length mix buffer.
        var notes = this.notes.Select(n => n with { StartTick = 0, VolumePercent = 100f }).Distinct().ToArray();
        var samples = this.samples.Select(CloneSampleWithMetadata).ToArray();
        var settings = this.trackSettings.ToArray();
        float bpm = (float)this.Bpm;
        int resolution = this.currentResolution;
        this.cacheWarmWork = WarmAsync();
        await this.cacheWarmWork;
        async Task WarmAsync()
        {
            try
            {
                using AudioObj? warm = await BreakbeatGenerator_V2.RenderPatternNotesAsync(notes, samples, 1, bpm,
                    resolution, 0, "CacheWarm", this.cacheLifetime.Token, settings, this.noteRenderCache);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { LogManager.Log("Pattern note cache preparation failed."); LogManager.Log(error); }
            finally { foreach (var sample in samples) sample.Dispose(); }
        }
    }
    private void DisposeNoteCache()
    {
        if (this.cacheLifetime.IsCancellationRequested) return;
        this.cacheWarmTimer.Stop(); this.cacheWarmTimer.Dispose();
        this.cacheLifetime.Cancel();
        _ = this.cacheWarmWork.ContinueWith(_ => this.cacheLifetime.Dispose(), TaskScheduler.Default);
        this.noteRenderCache.Dispose();
    }
}
