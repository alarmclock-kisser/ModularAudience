using System.Security;
using System.Text.Json;
using ModularAudience.Audio;
using ModularAudience.Audio.Effects;
using ModularAudience.Audio.Effects.AutoTune;
using ModularAudience.Forms.Effects;

namespace ModularAudience.Forms.Modules.Dialogs;

internal sealed record AutoTuneDialogContext(AudioEffectEditSession Session, string TrackName, AutoTuneSettings Defaults,
    Func<float[], CancellationToken, Task> ApplyAsync, Func<Task>? BeforePreviewAsync = null);

/// <summary>Modeless parameter/preset editor. Preview and rendering never mutate the editing target.</summary>
internal sealed class AutoTuneDialog : Form
{
    private readonly AutoTuneDialogContext context;
    private readonly AutoTunePresetStore store;
    private readonly IEffectPreviewPlayer preview;
    private readonly Func<AutoTuneSettings, IProgress<double>, CancellationToken, Task<AutoTuneResult>> render;
    private readonly AutoTuneParameterEditor parameters = new() { Dock = DockStyle.Fill };
    private readonly ComboBox presets = new() { Name = "autoTunePresets", Width = 210, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox presetName = new() { Name = "presetName", Width = 170, PlaceholderText = "Preset name (optional)" };
    private readonly Button save = new() { Name = "savePreset", Text = "Save", AutoSize = true };
    private readonly Button delete = new() { Name = "deletePreset", Text = "Delete", AutoSize = true };
    private readonly Button prehear = new() { Name = "prehear", Text = "Prehear", AutoSize = true };
    private readonly Button accept = new() { Name = "acceptEffect", Text = "OK / Apply", AutoSize = true };
    private readonly CheckBox dry = new() { Text = "Prehear original (bypass)", AutoSize = true };
    private readonly CheckBox loop = new() { Text = "Loop prehear", AutoSize = true };
    private readonly NumericUpDown maxThreads = new()
    {
        Name = "maxThreads", Minimum = 1, Maximum = Environment.ProcessorCount,
        Value = AudioEffectExecutionOptions.DefaultMaximumThreads, Width = 75
    };
    private readonly ProgressBar progress = new() { Width = 180, Maximum = 1000 };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, Text = "Ready. Prehear does not change the track." };
    private readonly FlowLayoutPanel presetBar = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
    private readonly System.Windows.Forms.Timer playbackTimer = new() { Interval = 60 };
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? active;
    private AutoTuneSettings? renderedSettings;
    private AutoTuneResult? rendered;
    private int revision;
    private bool refreshingPresets, committing, disposed;

    internal AutoTuneDialog(AutoTuneDialogContext context, AutoTunePresetStore? store = null, IEffectPreviewPlayer? preview = null,
        Func<AutoTuneSettings, IProgress<double>, CancellationToken, Task<AutoTuneResult>>? render = null)
    {
        this.context = context;
        this.store = store ?? new AutoTunePresetStore();
        this.preview = preview ?? new EffectPreviewPlayer();
        this.render = render ?? ((settings, report, token) => AutoTuneProcessor.ProcessAsync(context.Session.Input,
            context.Session.SampleRate, context.Session.Channels, settings, report, token,
            new AudioEffectExecutionOptions { MaximumThreads = (int)this.maxThreads.Value }));
        this.Name = "AutoTuneDialog";
        this.Text = "Auto Tune — " + context.TrackName;
        this.ClientSize = new Size(860, 710);
        this.MinimumSize = new Size(730, 580);
        this.StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), RowCount = 6, ColumnCount = 1 };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.Absolute, 44)); root.RowStyles.Add(new(SizeType.AutoSize));
        double start = context.Session.StartSample / (double)(context.Session.SampleRate * context.Session.Channels);
        double seconds = context.Session.SampleCount / (double)(context.Session.SampleRate * context.Session.Channels);
        root.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(810, 0), Text =
                $"{(context.Session.IsSelection ? "Selection" : "Whole track")}: {start:F3}s–{start + seconds:F3}s, " +
                $"{context.Session.SampleRate} Hz / {context.Session.Channels} ch (snapshot taken when opened).\n" +
                "Monophonic voice/melody correction, not polyphonic separation. Preview uses the source sample rate."
        }, 0, 0);
        this.presetBar.Controls.Add(new Label { Text = "Presets", AutoSize = true, Margin = new Padding(3, 7, 3, 0) });
        this.presetBar.Controls.AddRange([this.presets, this.presetName, this.save, this.delete]);
        var defaults = new Button { Text = "Defaults", AutoSize = true };
        this.presetBar.Controls.Add(defaults);
        root.Controls.Add(this.presetBar, 0, 1);
        root.Controls.Add(this.parameters, 0, 2);
        var hearBar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        hearBar.Controls.AddRange([this.prehear, this.dry, this.loop, this.progress]);
        hearBar.Controls.Add(new Label { Text = "Max threads (not preset)", AutoSize = true, Margin = new Padding(6, 6, 2, 0) });
        hearBar.Controls.Add(this.maxThreads);
        root.Controls.Add(hearBar, 0, 3); root.Controls.Add(this.status, 0, 4);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "Cancel", AutoSize = true };
        actions.Controls.AddRange([cancel, this.accept]); root.Controls.Add(actions, 0, 5);
        this.Controls.Add(root); this.AcceptButton = this.accept; this.CancelButton = cancel;
        this.parameters.ApplySettings(context.Defaults);
        this.parameters.SettingsChanged += (_, _) => this.InvalidateResult();
        this.maxThreads.ValueChanged += (_, _) => this.InvalidateResult();
        this.save.Click += (_, _) => this.PresetAction(this.SavePreset);
        this.delete.Click += (_, _) => this.PresetAction(this.DeletePreset);
        defaults.Click += (_, _) => this.parameters.ApplySettings(this.context.Defaults);
        this.presets.SelectedIndexChanged += (_, _) =>
        {
            if (!this.refreshingPresets && this.presets.SelectedItem is string name) this.PresetAction(() =>
            {
                AutoTunePreset preset = this.store.Load(name);
                this.presetName.Text = preset.Name;
                this.parameters.ApplySettings(preset.Parameters);
                this.status.Text = "Loaded preset: " + preset.Name;
            });
            this.UpdateButtons();
        };
        this.presets.DropDown += (_, _) => this.PresetAction(() => this.RefreshPresets(this.presets.SelectedItem as string));
        this.prehear.Click += async (_, _) => await this.PrehearAsync();
        this.accept.Click += async (_, _) => await this.AcceptEffectAsync();
        cancel.Click += (_, _) => this.Close();
        this.dry.CheckedChanged += (_, _) => this.StopPreview();
        this.loop.CheckedChanged += (_, _) => this.StopPreview();
        this.playbackTimer.Tick += (_, _) => { if (!this.preview.IsPlaying) this.StopPreview(); };
        this.PresetAction(() => this.RefreshPresets(null));
        this.UpdateButtons();
    }

    internal AutoTuneSettings CurrentSettings => this.parameters.CaptureSettings();
    internal void SetSettings(AutoTuneSettings settings) => this.parameters.ApplySettings(settings);

    private void InvalidateResult()
    {
        this.revision++;
        this.rendered = null; this.renderedSettings = null;
        this.active?.Cancel(); this.StopPreview();
        if (!this.disposed) this.status.Text = "Parameters changed. Prehear or apply to render the new settings.";
    }

    private async Task<AutoTuneResult?> RenderAsync()
    {
        if (this.active is not null || this.disposed) return null;
        AutoTuneSettings settings;
        try { this.ValidateChildren(); settings = this.parameters.CaptureSettings(); settings.Validate(this.context.Session.SampleRate); }
        catch (Exception error) when (IsEffectError(error)) { this.status.Text = error.Message; return null; }
        if (this.rendered is not null && this.renderedSettings == settings) return this.rendered;
        int generation = this.revision;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(this.lifetime.Token);
        this.active = cancellation; this.UpdateButtons(); this.progress.Value = 0;
        this.status.Text = "Analyzing pitch and rendering Auto Tune...";
        var report = new Progress<double>(value =>
        {
            if (!this.disposed && generation == this.revision && double.IsFinite(value))
                this.progress.Value = (int)Math.Round(Math.Clamp(value, 0, 1) * this.progress.Maximum);
        });
        try
        {
            AutoTuneResult result = await this.render(settings, report, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (this.disposed || generation != this.revision) return null;
            if (result.Samples.Length != this.context.Session.SampleCount || result.Samples.Any(value => !float.IsFinite(value)))
                throw new InvalidOperationException("The effect must return the same number of finite samples.");
            this.rendered = result; this.renderedSettings = settings;
            this.progress.Value = this.progress.Maximum;
            this.status.Text = $"Ready. Voiced frames: {result.VoicedFrames}/{result.AnalyzedFrames}; corrected: {result.CorrectedFrames}. " +
                "OK creates one undo step; Cancel leaves the track unchanged.";
            return result;
        }
        catch (OperationCanceledException) { if (!this.disposed) this.status.Text = "Rendering cancelled; the track was not changed."; return null; }
        catch (Exception error) when (IsEffectError(error)) { this.ShowError(error); return null; }
        finally
        {
            if (ReferenceEquals(this.active, cancellation)) this.active = null;
            this.UpdateButtons();
        }
    }

    internal async Task PrehearAsync()
    {
        if (this.preview.IsPlaying) { this.StopPreview(); return; }
        AutoTuneResult? result = await this.RenderAsync();
        if (result is null || this.disposed) return;
        int generation = this.revision;
        try
        {
            if (this.context.BeforePreviewAsync is not null) await this.context.BeforePreviewAsync();
            if (this.disposed || generation != this.revision) return;
            this.preview.Play(this.dry.Checked ? this.context.Session.Input.ToArray() : result.Samples,
                this.context.Session.SampleRate, this.context.Session.Channels, this.loop.Checked);
            this.prehear.Text = "Stop prehear"; this.playbackTimer.Start();
        }
        catch (Exception error) when (IsEffectError(error)) { this.ShowError(error); this.StopPreview(); }
    }

    internal async Task AcceptEffectAsync()
    {
        this.StopPreview();
        AutoTuneResult? result = await this.RenderAsync();
        if (result is null || this.disposed) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(this.lifetime.Token);
        this.active = cancellation; this.committing = true; this.UpdateButtons();
        try
        {
            await this.context.ApplyAsync(result.Samples, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!this.disposed) { this.DialogResult = DialogResult.OK; this.Close(); }
        }
        catch (OperationCanceledException) { if (!this.disposed) this.status.Text = "Apply cancelled."; }
        catch (Exception error) when (IsEffectError(error)) { this.ShowError(error); }
        finally
        {
            if (ReferenceEquals(this.active, cancellation)) this.active = null;
            this.committing = false; this.UpdateButtons();
        }
    }

    internal void SavePreset()
    {
        this.ValidateChildren();
        AutoTunePreset saved = this.store.Save(this.presetName.Text, this.parameters.CaptureSettings());
        this.presetName.Text = saved.Name;
        this.RefreshPresets(saved.Name); this.status.Text = "Saved preset: " + saved.Name;
    }

    internal void DeletePreset()
    {
        if (this.presets.SelectedItem is not string name) return;
        this.store.Delete(name);
        this.RefreshPresets(null); this.presetName.Clear();
        this.parameters.ApplySettings(this.context.Defaults);
        this.status.Text = "Preset deleted; parameters reset to defaults.";
    }

    private void RefreshPresets(string? selected)
    {
        this.refreshingPresets = true;
        try
        {
            this.presets.Items.Clear();
            this.presets.Items.AddRange(this.store.List().Select(preset => (object)preset.Name).ToArray());
            this.presets.SelectedItem = selected;
        }
        finally { this.refreshingPresets = false; }
        this.UpdateButtons();
    }

    private void PresetAction(Action action)
    {
        try { action(); }
        catch (Exception error) when (IsEffectError(error)) { this.ShowError(error); }
    }

    private void UpdateButtons()
    {
        if (this.disposed) return;
        bool busy = this.active is not null;
        this.accept.Enabled = this.prehear.Enabled = !busy;
        this.save.Enabled = !busy;
        this.delete.Enabled = !busy && this.presets.SelectedItem is string;
        this.parameters.Enabled = this.presetBar.Enabled = !this.committing;
        this.maxThreads.Enabled = !this.committing;
    }

    private void StopPreview()
    {
        this.playbackTimer.Stop();
        try { this.preview.Stop(); }
        catch (Exception error) when (IsEffectError(error)) { if (!this.disposed) this.status.Text = error.Message; }
        if (!this.disposed) this.prehear.Text = "Prehear";
    }

    private void ShowError(Exception error)
    {
        if (this.disposed) return;
        this.status.Text = error.Message;
        LogManager.Log("Auto Tune failed.", error);
    }

    private static bool IsEffectError(Exception error) => error is ArgumentException or InvalidOperationException or ArithmeticException or
        IOException or InvalidDataException or UnauthorizedAccessException or JsonException or SecurityException or KeyNotFoundException or
        NAudio.MmException or System.ComponentModel.Win32Exception;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        this.lifetime.Cancel(); this.active?.Cancel(); this.StopPreview();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !this.disposed)
        {
            this.disposed = true;
            this.lifetime.Cancel(); this.active?.Cancel(); this.StopPreview();
            this.playbackTimer.Dispose();
            try { this.preview.Dispose(); } catch (Exception error) when (IsEffectError(error)) { }
            this.lifetime.Dispose();
        }
        base.Dispose(disposing);
    }
}
