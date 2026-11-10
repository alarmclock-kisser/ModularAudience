using System.Security;
using System.Text.Json;
using ModularAudience.Audio;
using ModularAudience.Audio.Effects;
using ModularAudience.Forms.Effects;

namespace ModularAudience.Forms.Modules.Dialogs;

internal sealed record EffectDialogContext(AudioEffectEditSession Session, string TrackName, AudioEffectSettings Defaults,
    Func<AudioEffectResult, CancellationToken, Task> ApplyAsync, Func<Task>? BeforePreviewAsync = null,
    Func<(double Bpm, double ScannedBpm)>? ReadTrackTempo = null,
    string? ScopeDescription = null,
    Func<AudioEffectSettings, AudioEffectExecutionOptions, IProgress<double>, CancellationToken, Task<AudioEffectResult>>? RenderAsync = null,
    Action<Form, Form>? ShowChildDialog = null);

/// <summary>Instance-wide (in-RAM) last-used effect settings, keyed by effect type. Shared across TrackView and MixTrackView.</summary>
internal static class EffectSettingsStore
{
    private static readonly Dictionary<AudioEffectKind, AudioEffectSettings> _store = new();
    public static AudioEffectSettings? Get(AudioEffectKind kind) => _store.TryGetValue(kind, out var value) ? value : null;
    public static void Set(AudioEffectKind kind, AudioEffectSettings settings) => _store[kind] = settings;
}

internal sealed class EffectOptionsDialog : Form
{
    internal EffectOptionsEditor Editor { get; }
    internal EffectOptionsDialog(EffectDialogContext context) => this.Editor = EffectDialogHost.Attach(this, context);
}

internal static class EffectDialogHost
{
    internal static Form Create(EffectDialogContext context) => context.Defaults.Effect switch
    {
        AudioEffectKind.PitchShift => new PitchShiftDialog(context),
        AudioEffectKind.TimeStretch => new TimeStretchEffectDialog(context),
        AudioEffectKind.VarispeedShift => new VarispeedShiftDialog(context),
        _ => new EffectOptionsDialog(context)
    };

    internal static EffectOptionsEditor Attach(Form form, EffectDialogContext context, EffectPresetStore? store = null, IEffectPreviewPlayer? preview = null)
    {
        // Restore the last used settings for this effect type (in-RAM, instance-wide) if available.
        AudioEffectSettings? stored = EffectSettingsStore.Get(context.Defaults.Effect);
        if (stored is not null) context = context with { Defaults = stored };
        EffectOptionsEditor? editor = null;
        var accepted = () =>
        {
            if (editor is not null) EffectSettingsStore.Set(context.Defaults.Effect, editor.CurrentSettings);
            form.DialogResult = DialogResult.OK; form.Close();
        };
        editor = new EffectOptionsEditor(context, accepted, form.Close, store, preview);
        form.Name = context.Defaults.Effect + "Dialog";
        form.Text = AudioEffectCatalog.Get(context.Defaults.Effect).Name + " — " + context.TrackName;
        form.ClientSize = new Size(1000, 730); form.MinimumSize = new Size(720, 460);
        form.StartPosition = FormStartPosition.CenterParent;
        form.Controls.Add(editor); form.CancelButton = editor.CancelButton;
        // Enter must not auto-confirm/render effect settings; editing a NumericUpDown and pressing Enter
        // should only commit the field, never start rendering. Apply stays an explicit button click.
        form.AcceptButton = null;
        form.FormClosing += (_, _) => editor.CancelWork();
        return editor;
    }
}

/// <summary>
/// Shared modeless editor hosted in a per-effect window (including PitchShiftDialog
/// and the dedicated TimeStretchEffectDialog/VarispeedShiftDialog). Rendering/preview/presets never mutate the editing target.
/// </summary>
internal sealed class EffectOptionsEditor : UserControl
{
    private readonly EffectDialogContext context;
    private readonly EffectPresetStore store;
    private readonly IEffectPreviewPlayer preview;
    private readonly EffectParameterEditor parameters;
    private readonly Func<AudioEffectSettings, IProgress<double>, CancellationToken, Task<AudioEffectResult>> render;
    private readonly Action accepted;
    private readonly EffectPresetBar presets = new();
    private readonly Button prehear = new() { Name = "prehear", Text = "Prehear", AutoSize = true };
    internal Button ApplyButton { get; } = new() { Name = "acceptEffect", Text = "OK / Apply", AutoSize = true };
    internal Button CancelButton { get; } = new() { Name = "cancelEffect", Text = "Cancel", AutoSize = true };
    private readonly CheckBox dry = new() { Name = "bypassPreview", Text = "Prehear original (bypass)", AutoSize = true };
    private readonly CheckBox loop = new() { Name = "loopPreview", Text = "Loop prehear", AutoSize = true };
    private readonly NumericUpDown maxThreads = new() { Name = "maxThreads", Minimum = 1, Maximum = Environment.ProcessorCount,
        Value = AudioEffectExecutionOptions.DefaultMaximumThreads, Width = 75 };
    private readonly ProgressBar progress = new() { Name = "effectProgress", Width = 180, Maximum = 1000 };
    private readonly Label status = new() { Name = "effectStatus", Dock = DockStyle.Fill, AutoEllipsis = true, Text = "Ready. Preview does not change the audio." };
    private readonly System.Windows.Forms.Timer playbackTimer = new() { Interval = 60 };
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? active;
    private AudioEffectSettings? renderedSettings;
    private AudioEffectResult? rendered;
    private int revision;
    private bool refreshing, committing, disposed;

    internal EffectOptionsEditor(EffectDialogContext context, Action accepted, Action cancel,
        EffectPresetStore? store = null, IEffectPreviewPlayer? preview = null,
        Func<AudioEffectSettings, IProgress<double>, CancellationToken, Task<AudioEffectResult>>? render = null)
    {
        this.context = context; this.accepted = accepted;
        this.store = store ?? new EffectPresetStore(context.Defaults.Effect);
        this.preview = preview ?? new EffectPreviewPlayer();
        AudioEffectDefinition definition = AudioEffectCatalog.Get(context.Defaults.Effect);
        this.parameters = new EffectParameterEditor(definition, context.ReadTrackTempo, context.ShowChildDialog);
        this.render = render ?? ((settings, report, token) =>
        {
            var execution = new AudioEffectExecutionOptions { MaximumThreads = (int)this.maxThreads.Value };
            return context.RenderAsync is null ? AudioEffectProcessor.ProcessAsync(context.Session.Input,
                context.Session.SampleRate, context.Session.Channels, settings, report, token, execution)
                : context.RenderAsync(settings, execution, report, token);
        });
        this.Dock = DockStyle.Fill;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), RowCount = 6, ColumnCount = 1 };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.Absolute, 48)); root.RowStyles.Add(new(SizeType.AutoSize));
        double start = context.Session.StartSample / (double)context.Session.Channels / context.Session.SampleRate;
        double seconds = context.Session.SampleCount / (double)context.Session.Channels / context.Session.SampleRate;
        string scope = context.ScopeDescription ?? $"{(context.Session.IsSelection ? "Selection" : "Whole track")}: {start:F3}s–{start + seconds:F3}s, " +
            $"{context.Session.SampleRate} Hz / {context.Session.Channels} ch (snapshot taken when opened).";
        root.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(840, 0), Text =
            scope + $"\n{definition.Description}\n" +
            "Length changes ripple subsequent audio. OK creates one undo step; Cancel leaves the audio unchanged." }, 0, 0);
        root.Controls.Add(this.presets, 0, 1); root.Controls.Add(this.parameters, 0, 2);
        var hear = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        hear.Controls.AddRange([this.prehear, this.dry, this.loop, this.progress]);
        hear.Controls.Add(new Label { Text = "Max threads (not preset)", AutoSize = true, Margin = new Padding(6, 6, 2, 0) }); hear.Controls.Add(this.maxThreads);
        root.Controls.Add(hear, 0, 3); root.Controls.Add(this.status, 0, 4);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        actions.Controls.AddRange([this.CancelButton, this.ApplyButton]); root.Controls.Add(actions, 0, 5); this.Controls.Add(root);
        this.parameters.ApplySettings(context.Defaults);
        this.parameters.SettingsChanged += (_, _) => this.InvalidateResult();
        this.maxThreads.ValueChanged += (_, _) => this.InvalidateResult();
        this.presets.Presets.SelectedIndexChanged += (_, _) =>
        {
            if (!this.refreshing) this.PresetAction(this.LoadSelectedPreset);
            this.UpdateButtons();
        };
        this.presets.Presets.DropDown += (_, _) => this.PresetAction(() => this.RefreshPresets(this.presets.Presets.SelectedItem as EffectPreset));
        this.presets.SaveButton.Click += (_, _) => this.PresetAction(this.SavePreset);
        this.presets.LoadButton.Click += (_, _) => this.PresetAction(this.LoadSelectedPreset);
        this.presets.RenameButton.Click += (_, _) => this.PresetAction(this.RenamePreset);
        this.presets.DeleteButton.Click += (_, _) => this.PresetAction(this.DeletePreset);
        this.presets.DefaultsButton.Click += (_, _) => this.parameters.ApplySettings(this.context.Defaults);
        this.prehear.Click += async (_, _) => await this.PrehearAsync();
        this.ApplyButton.Click += async (_, _) => await this.AcceptEffectAsync();
        this.CancelButton.Click += (_, _) => cancel();
        this.dry.CheckedChanged += (_, _) => this.StopPreview(); this.loop.CheckedChanged += (_, _) => this.StopPreview();
        this.playbackTimer.Tick += (_, _) => { if (!this.preview.IsPlaying) this.StopPreview(); };
        this.PresetAction(() => this.RefreshPresets(null)); this.UpdateButtons();
    }

    internal AudioEffectSettings CurrentSettings => this.parameters.CaptureSettings();
    internal void SetSettings(AudioEffectSettings settings) => this.parameters.ApplySettings(settings);

    private void InvalidateResult()
    {
        this.revision++; this.rendered = null; this.renderedSettings = null;
        this.active?.Cancel(); this.StopPreview();
        if (!this.disposed) this.status.Text = "Parameters changed. Prehear or apply to render the new settings.";
    }

    private async Task<AudioEffectResult?> RenderAsync()
    {
        if (this.active is not null || this.disposed) return null;
        AudioEffectSettings settings;
        try { this.ValidateChildren(); settings = this.CurrentSettings; }
        catch (Exception error) when (IsEffectError(error)) { this.ShowError(error); return null; }
        if (this.rendered is not null && settings.SoundEquals(this.renderedSettings)) return this.rendered;
        int generation = this.revision;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(this.lifetime.Token);
        this.active = cancellation; this.UpdateButtons(); this.progress.Value = 0;
        this.status.Text = "Rendering " + AudioEffectCatalog.Get(settings.Effect).Name + "...";
        var report = new Progress<double>(value =>
        {
            if (!this.disposed && generation == this.revision && double.IsFinite(value))
                this.progress.Value = (int)Math.Round(Math.Clamp(value, 0, 1) * this.progress.Maximum);
        });
        try
        {
            AudioEffectResult result = await this.render(settings, report, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (this.disposed || generation != this.revision) return null;
            if (result.Channels <= 0 || result.Samples.Length == 0 || result.Samples.Length % result.Channels != 0 || result.Samples.Any(sample => !float.IsFinite(sample)))
                throw new InvalidOperationException("The effect must return finite, complete audio frames.");
            this.rendered = result; this.renderedSettings = settings.Copy(); this.progress.Value = this.progress.Maximum;
            double duration = result.Samples.Length / (double)result.Channels / this.context.Session.SampleRate;
            this.status.Text = $"Ready: {duration:F3}s / {result.Channels} ch. OK creates one undo step; Cancel leaves the audio unchanged.";
            return result;
        }
        catch (OperationCanceledException) { if (!this.disposed) this.status.Text = "Rendering cancelled; audio unchanged."; return null; }
        catch (Exception error) when (IsEffectError(error)) { this.ShowError(error); return null; }
        finally { if (ReferenceEquals(this.active, cancellation)) this.active = null; this.UpdateButtons(); }
    }

    internal async Task PrehearAsync()
    {
        if (this.preview.IsPlaying) { this.StopPreview(); return; }
        AudioEffectResult? result = this.dry.Checked ? new(this.context.Session.Input.ToArray(), this.context.Session.Channels) : await this.RenderAsync();
        if (result is null || this.disposed) return;
        int generation = this.revision;
        try
        {
            if (this.context.BeforePreviewAsync is not null) await this.context.BeforePreviewAsync();
            if (this.disposed || generation != this.revision) return;
            this.preview.Play(result.Samples, this.context.Session.SampleRate, result.Channels, this.loop.Checked);
            this.prehear.Text = "Stop prehear"; this.playbackTimer.Start();
        }
        catch (Exception error) when (IsEffectError(error)) { this.ShowError(error); this.StopPreview(); }
    }

    internal async Task AcceptEffectAsync()
    {
        if (this.committing || this.disposed) return;
        this.StopPreview(); AudioEffectResult? result = await this.RenderAsync();
        if (result is null || this.disposed) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(this.lifetime.Token);
        this.active = cancellation; this.committing = true; this.UpdateButtons();
        try
        {
            await this.context.ApplyAsync(result, cancellation.Token);
            if (!this.disposed) this.accepted();
        }
        catch (OperationCanceledException) { if (!this.disposed) this.status.Text = "Apply cancelled."; }
        catch (Exception error) when (IsEffectError(error)) { this.ShowError(error); }
        finally { if (ReferenceEquals(this.active, cancellation)) this.active = null; this.committing = false; this.UpdateButtons(); }
    }

    internal void LoadSelectedPreset()
    {
        if (this.presets.Presets.SelectedItem is not EffectPreset selected) return;
        EffectPreset loaded = selected.IsBuiltIn ? selected : this.store.Load(selected.Name);
        this.presets.PresetName.Text = loaded.IsBuiltIn ? string.Empty : loaded.Name;
        this.parameters.ApplySettings(loaded.Parameters);
        this.status.Text = (loaded.IsBuiltIn ? "Default: " : "Loaded: ") + loaded.Name + (loaded.IsBuiltIn ? ". Save with a new name (or blank for Preset#N) to keep a variant." : string.Empty);
    }

    internal void SavePreset()
    {
        this.ValidateChildren(); EffectPreset saved = this.store.Save(this.presets.PresetName.Text, this.CurrentSettings);
        this.presets.PresetName.Text = saved.Name; this.RefreshPresets(saved); this.status.Text = "Saved: " + saved.Name;
    }

    internal void RenamePreset()
    {
        if (this.presets.Presets.SelectedItem is not EffectPreset { IsBuiltIn: false } selected) return;
        EffectPreset renamed = this.store.Rename(selected.Name, this.presets.PresetName.Text);
        this.presets.PresetName.Text = renamed.Name; this.RefreshPresets(renamed); this.status.Text = "Renamed: " + renamed.Name;
    }

    internal void DeletePreset()
    {
        if (this.presets.Presets.SelectedItem is not EffectPreset { IsBuiltIn: false } selected) return;
        this.store.Delete(selected.Name); this.RefreshPresets(null); this.presets.PresetName.Clear();
        this.parameters.ApplySettings(this.context.Defaults); this.status.Text = "Preset deleted; parameters reset to defaults.";
    }

    private void RefreshPresets(EffectPreset? selected)
    {
        this.refreshing = true;
        try
        {
            IReadOnlyList<EffectPreset> available;
            try { available = this.store.ListAvailable(); }
            catch (Exception error) when (IsEffectError(error))
            {
                available = this.store.FactoryPresets();
                try { available = available.Concat(this.store.List()).ToArray(); } catch (Exception readError) when (IsEffectError(readError)) { }
                this.ShowError(error);
            }
            this.presets.Presets.Items.Clear(); this.presets.Presets.Items.AddRange(available.Cast<object>().ToArray());
            this.presets.Presets.SelectedItem = selected is null ? null : available.FirstOrDefault(preset =>
                string.Equals(preset.BuiltInId, selected.BuiltInId, StringComparison.OrdinalIgnoreCase) &&
                (preset.IsBuiltIn || preset.Name.Equals(selected.Name, StringComparison.OrdinalIgnoreCase)));
        }
        finally { this.refreshing = false; }
        this.UpdateButtons();
    }

    private void PresetAction(Action action) { try { action(); } catch (Exception error) when (IsEffectError(error)) { this.ShowError(error); } }
    private void ShowError(Exception error) { if (!this.disposed) this.status.Text = error.Message; LogManager.Log("Effect failed.", error); }
    private void UpdateButtons()
    {
        if (this.disposed) return;
        bool busy = this.active is not null;
        this.ApplyButton.Enabled = this.prehear.Enabled = !busy;
        this.presets.UpdateState(busy, this.presets.Presets.SelectedItem is EffectPreset, this.presets.Presets.SelectedItem is EffectPreset { IsBuiltIn: false });
        this.parameters.Enabled = this.presets.Enabled = this.maxThreads.Enabled = !this.committing;
    }
    private void StopPreview()
    {
        this.playbackTimer.Stop();
        try { this.preview.Stop(); } catch (Exception error) when (IsEffectError(error)) { if (!this.disposed) this.status.Text = error.Message; }
        if (!this.disposed) this.prehear.Text = "Prehear";
    }
    internal void CancelWork() { this.lifetime.Cancel(); this.active?.Cancel(); this.StopPreview(); }
    internal static bool IsEffectError(Exception error) => error is ArgumentException or InvalidOperationException or ArithmeticException or
        IOException or InvalidDataException or UnauthorizedAccessException or JsonException or SecurityException or KeyNotFoundException or NotSupportedException or
        NAudio.MmException or System.ComponentModel.Win32Exception;

    protected override void Dispose(bool disposing)
    {
        if (disposing && !this.disposed)
        {
            this.disposed = true; this.CancelWork(); this.playbackTimer.Dispose();
            try { this.preview.Dispose(); } catch (Exception error) when (IsEffectError(error)) { }
            this.lifetime.Dispose();
        }
        base.Dispose(disposing);
    }
}
