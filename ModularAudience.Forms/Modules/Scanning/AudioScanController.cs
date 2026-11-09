using System.ComponentModel;
using ModularAudience.Audio;
using ModularAudience.Audio.Processors_V1;
using ModularAudience.Audio.Processors_V2;
using ModularAudience.Forms.Helpers;

namespace ModularAudience.Forms;

/// <summary>Shares the selected audio and result display between BPM, timing and key scans.</summary>
internal sealed class AudioScanController : IDisposable
{
    private readonly Control owner;
    private readonly Button bpmButton;
    private readonly Button timingButton;
    private readonly Button keyButton;
    private readonly TextBox bpmResult;
    private readonly TextBox timingResult;
    private readonly TextBox keyResult;
    private readonly Func<AudioObj, Task<double>> scanBpm;
    private readonly Func<AudioObj, Task<float>> scanTiming;
    private readonly Func<AudioObj, Task<string>> scanKey;
    private readonly Action<string>? log;
    private readonly HashSet<string> scanning = [];
    private readonly int uiThreadId = Environment.CurrentManagedThreadId;
    private AudioObj? target;
    private volatile bool disposed;

    internal AudioScanController(
        Control owner,
        Button bpmButton, TextBox bpmResult,
        Button timingButton, TextBox timingResult,
        Button keyButton, TextBox keyResult,
        Func<AudioObj, Task<double>>? scanBpm = null,
        Func<AudioObj, Task<float>>? scanTiming = null,
        Func<AudioObj, Task<string>>? scanKey = null,
        Action<string>? log = null)
    {
        this.owner = owner;
        this.bpmButton = bpmButton;
        this.timingButton = timingButton;
        this.keyButton = keyButton;
        this.bpmResult = bpmResult;
        this.timingResult = timingResult;
        this.keyResult = keyResult;
        this.scanBpm = scanBpm ?? (audio => BeatScanner.ScanBpmAsync(audio));
        this.scanTiming = scanTiming ?? (audio => BeatScanner_V2.ScanTimingAsync(audio));
        this.scanKey = scanKey ?? (audio => BeatScanner_V2.ScanKeyAsync(audio));
        this.log = log;
        this.bpmResult.ReadOnly = this.timingResult.ReadOnly = this.keyResult.ReadOnly = true;
        this.Refresh();
    }

    internal void SetTarget(AudioObj? audio)
    {
        if (this.disposed) return;
        if (!ReferenceEquals(this.target, audio))
        {
            if (this.target != null) this.target.PropertyChanged -= this.TargetPropertyChanged;
            this.target = audio;
            if (this.target != null) this.target.PropertyChanged += this.TargetPropertyChanged;
            // A focus change cancels a numeric edit belonging to the previous audio.
            this.bpmResult.ReadOnly = true;
        }
        this.Refresh();
    }

    internal Task<bool> ScanBpmAsync() => this.ScanAsync("BPM", this.scanBpm, (audio, result) => audio.ScannedBpm = (float)result);
    internal Task<bool> ScanTimingAsync() => this.ScanAsync("timing", this.scanTiming, (audio, result) => audio.ScannedTiming = result);
    internal Task<bool> ScanKeyAsync() => this.ScanAsync("key", this.scanKey, (audio, result) => audio.ScannedKey = result);

    private async Task<bool> ScanAsync<T>(string kind, Func<AudioObj, Task<T>> scanner, Action<AudioObj, T> apply)
    {
        AudioObj? audio = this.target;
        if (this.disposed || audio == null || !this.scanning.Add(kind)) return false;
        this.Refresh();
        try
        {
            // Capture the audio before awaiting. A later selection change must not
            // send this result to a different AudioObj or overwrite its result fields.
            T result = await scanner(audio).ConfigureAwait(false);
            bool applied = false;
            await this.OnUiAsync(() =>
            {
                apply(audio, result);
                applied = true;
                this.Refresh();
            }).ConfigureAwait(false);
            return applied;
        }
        catch (Exception error)
        {
            this.log?.Invoke($"Could not scan {kind}: {error.Message}");
            return false;
        }
        finally
        {
            await this.OnUiAsync(() =>
            {
                this.scanning.Remove(kind);
                this.Refresh();
            }).ConfigureAwait(false);
        }
    }

    internal void Refresh()
    {
        if (this.disposed) return;
        AudioObj? audio = this.target;
        this.bpmButton.Enabled = audio != null && !this.scanning.Contains("BPM");
        this.timingButton.Enabled = audio != null && !this.scanning.Contains("timing");
        this.keyButton.Enabled = audio != null && !this.scanning.Contains("key");
        if (this.bpmResult.ReadOnly)
            this.bpmResult.Text = audio != null && float.IsFinite(audio.ScannedBpm) && audio.ScannedBpm > 0
                ? $"{audio.ScannedBpm:F3} BPM" : string.Empty;
        this.timingResult.Text = audio != null && float.IsFinite(audio.ScannedTiming) && audio.ScannedTiming > 0
            ? WindowMainFormatHelpers.GetTimingString(audio.ScannedTiming) : string.Empty;
        this.keyResult.Text = audio?.ScannedKey ?? string.Empty;
    }

    private void TargetPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is not (null or "" or nameof(AudioObj.ScannedBpm) or nameof(AudioObj.ScannedTiming) or nameof(AudioObj.ScannedKey)))
            return;
        if (this.disposed || !ReferenceEquals(sender, this.target)) return;
        if (Environment.CurrentManagedThreadId == this.uiThreadId)
        {
            this.Refresh();
        }
        else if (this.owner.IsHandleCreated && !this.owner.IsDisposed && !this.owner.Disposing)
        {
            try
            {
                this.owner.BeginInvoke((Action)(() =>
                {
                    if (!this.disposed && ReferenceEquals(sender, this.target)) this.Refresh();
                }));
            }
            catch (InvalidOperationException) when (this.owner.IsDisposed || !this.owner.IsHandleCreated) { }
        }
    }

    private async Task OnUiAsync(Action action)
    {
        if (this.disposed || this.owner.IsDisposed || this.owner.Disposing) return;
        if (Environment.CurrentManagedThreadId == this.uiThreadId)
        {
            action();
            return;
        }
        if (!this.owner.IsHandleCreated) return;
        try
        {
            await this.owner.InvokeAsync(() =>
            {
                if (!this.disposed && !this.owner.IsDisposed && !this.owner.Disposing) action();
            }).ConfigureAwait(false);
        }
        catch (InvalidOperationException) when (this.owner.IsDisposed || !this.owner.IsHandleCreated) { }
    }

    public void Dispose()
    {
        this.disposed = true;
        if (this.target != null) this.target.PropertyChanged -= this.TargetPropertyChanged;
        this.target = null;
    }
}
