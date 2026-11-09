using System.ComponentModel;
using ModularAudience.Audio;
using ModularAudience.Audio.Arrangement;
using ModularAudience.Audio.Effects;
using ModularAudience.Audio.Effects.AutoTune;
using ModularAudience.Forms.Effects;
using ModularAudience.Forms.Modules.Dialogs;

namespace ModularAudience.Forms.Modules;

public sealed partial class MixTrackView
{
    private ContextMenuStrip? timelineContextMenu;
    private readonly Dictionary<AudioEffectKind, Form> mixEffectDialogs = [];
    private readonly CancellationTokenSource mixEffectsLifetime = new();
    private Task playbackStopWork = Task.CompletedTask;
    private bool mixEffectOpening, mixEffectWindowsClosed;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<EffectDialogContext, Form> CreateMixEffectDialog { get; set; } = EffectDialogHost.Create;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<AutoTuneDialogContext, AutoTuneDialog> CreateMixAutoTuneDialog { get; set; } = context => new AutoTuneDialog(context);

    private bool HasAudioEffectSelection => (selectionFirstRow >= 0 && selectionFirstRow < Arrangement.Lanes.Count && selectionEnd > selectionStart) ||
        Clip is not null || Arrangement.Lanes.SelectMany(lane => lane.Clips).Any(clip => selectedClips.Contains(clip.Id));

    private void ClearAudioEffectSelection()
    {
        ClearRangeSelection(); selectedClip = null; canvas.Invalidate();
    }

    internal MixEffectRegion[] CaptureEffectRegions()
    {
        if (selectionFirstRow >= 0 && selectionEnd > selectionStart && fullSelectedClips.Count == 0)
            return Arrangement.Lanes.Skip(selectionFirstRow).Take(selectionLastRow - selectionFirstRow + 1)
                .Select(lane => new MixEffectRegion(lane.Id, selectionStart, selectionEnd,
                    lane.Clips.Where(clip => clip.Start < selectionEnd && clip.Start + Arrangement.ClipDuration(clip) > selectionStart)
                        .Select(clip => clip.Id).ToHashSet())).ToArray();
        var parts = SelectedParts();
        if (parts.Count == 0) return [];
        double start = parts.Min(part => part.Clip.Start), end = parts.Max(part => part.Clip.Start + Arrangement.ClipDuration(part.Clip));
        return parts.GroupBy(part => part.Row).Select(group => new MixEffectRegion(Arrangement.Lanes[group.Key].Id, start, end,
            group.Select(part => part.Clip.Id).ToHashSet())).ToArray();
    }

    private async void OpenMixEffect(AudioEffectKind effect) => await this.OpenMixEffectAsync(effect);

    internal async Task OpenMixEffectAsync(AudioEffectKind effect)
    {
        if (busy || closing || released || this.mixEffectOpening) return;
        if (this.mixEffectDialogs.TryGetValue(effect, out Form? existing) && !existing.IsDisposed) { existing.Activate(); return; }
        MixEffectEditSession? session = null; bool ownedByDialog = false;
        this.mixEffectOpening = true;
        try
        {
            CancellationToken lifetime = this.mixEffectsLifetime.Token;
            MixEffectRegion[] regions = this.CaptureEffectRegions();
            if (regions.Length == 0) return;
            bool wasRange = selectionFirstRow >= 0 && selectionEnd > selectionStart && fullSelectedClips.Count == 0;
            this.status.Text = "Capturing selected audio and clip envelopes...";
            session = await MixEffectEditSession.CaptureAsync(Arrangement, regions, lifetime);
            lifetime.ThrowIfCancellationRequested();
            if (!session.IsCurrent(Arrangement)) throw new InvalidOperationException("The selected mix audio changed during capture. Please reopen the effect.");
            MixEffectEditSession captured = session;
            AudioEffectDefinition definition = AudioEffectCatalog.Get(effect);
            if (!definition.OpensOptions)
            {
                AudioEffectResult[] results = await this.RenderMixEffectAsync(captured, definition.CreateDefaults(), new(), null, lifetime);
                await this.CommitMixEffectAsync(captured, results, definition.Name, wasRange, lifetime);
                return;
            }
            using AudioObj originalPreview = await captured.RenderPreviewAsync(token: lifetime);
            lifetime.ThrowIfCancellationRequested();
            AudioEffectEditSession previewSession = AudioEffectEditSession.Capture(originalPreview);
            AudioEffectResult[]? renderedBatch = null;
            string scope = $"Mix selection: {captured.Start:F3}s–{captured.End:F3}s; {captured.Inputs.Count} lane(s), including silence between segments.\n" +
                "Each lane is processed separately. Clip gain/fades are rendered; track gain/mute/solo remain editable. Preview auditions the selected lanes.";
            Form dialog;
            if (effect == AudioEffectKind.AutoTune)
            {
                var context = new AutoTuneDialogContext(previewSession, Arrangement.Name + " — selection",
                    new AutoTuneSettings { TempoBpm = AutoTuneTempoResolver.Resolve(captured.TempoBpm, 0) },
                    (_, token) => this.CommitMixEffectAsync(captured, renderedBatch ?? throw new InvalidOperationException("Render the effect before applying."),
                        definition.Name, wasRange, token),
                    this.StopMixForEffectAsync, () => (Arrangement.Bpm, 0), scope,
                    async (settings, execution, progress, token) =>
                    {
                        var results = new List<AudioEffectResult>(); int analyzed = 0, voiced = 0, corrected = 0; double protection = 1;
                        for (int i = 0; i < captured.Inputs.Count; i++)
                        {
                            token.ThrowIfCancellationRequested(); MixEffectInput input = captured.Inputs[i];
                            var report = new SliceProgress(progress, i * 0.9 / captured.Inputs.Count, 0.9 / captured.Inputs.Count);
                            AutoTuneResult result = await AutoTuneProcessor.ProcessAsync(input.Samples, MixEffectEditSession.SampleRate,
                                input.Channels, settings, report, token, execution);
                            results.Add(new(result.Samples, input.Channels)); analyzed += result.AnalyzedFrames; voiced += result.VoicedFrames;
                            corrected += result.CorrectedFrames; protection = Math.Min(protection, result.PeakProtectionGain);
                        }
                        using AudioObj preview = await captured.RenderPreviewAsync(results, token);
                        token.ThrowIfCancellationRequested(); renderedBatch = results.ToArray(); progress.Report(1);
                        return new AutoTuneResult(preview.Data, analyzed, voiced, corrected, protection);
                    });
                dialog = this.CreateMixAutoTuneDialog(context);
            }
            else
            {
                var context = new EffectDialogContext(previewSession, Arrangement.Name + " — selection", definition.CreateDefaults(),
                    (_, token) => this.CommitMixEffectAsync(captured, renderedBatch ?? throw new InvalidOperationException("Render the effect before applying."),
                        definition.Name, wasRange, token),
                    this.StopMixForEffectAsync, () => (Arrangement.Bpm, 0), scope,
                    async (settings, execution, progress, token) =>
                    {
                        AudioEffectResult[] results = await this.RenderMixEffectAsync(captured, settings, execution, progress, token);
                        using AudioObj preview = await captured.RenderPreviewAsync(results, token);
                        token.ThrowIfCancellationRequested(); renderedBatch = results; progress.Report(1);
                        return new AudioEffectResult(preview.Data, preview.Channels);
                    });
                dialog = this.CreateMixEffectDialog(context);
            }
            this.mixEffectDialogs[effect] = dialog;
            dialog.FormClosed += (_, _) =>
            {
                if (this.mixEffectDialogs.GetValueOrDefault(effect) == dialog) this.mixEffectDialogs.Remove(effect);
                captured.Dispose();
            };
            dialog.Show(this); ownedByDialog = true;
            this.status.Text = definition.Name + ": selected audio captured. Preview and presets do not edit the mix.";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (EffectOptionsEditor.IsEffectError(error))
        {
            LogManager.Log("Mix effect failed.", error);
            if (!released && !closing) this.status.Text = error.Message;
        }
        finally { this.mixEffectOpening = false; if (!ownedByDialog) session?.Dispose(); }
    }

    private async Task<AudioEffectResult[]> RenderMixEffectAsync(MixEffectEditSession session, AudioEffectSettings settings,
        AudioEffectExecutionOptions execution, IProgress<double>? progress, CancellationToken token)
    {
        MixEffectInput[] inputs = session.Inputs.ToArray(); var results = new AudioEffectResult[inputs.Length];
        // Sequential lanes share one bounded worker budget, rather than multiplying
        // the dialog's Max threads by the number of selected tracks.
        for (int i = 0; i < inputs.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            IProgress<double>? report = progress is null ? null : new SliceProgress(progress, i * 0.9 / inputs.Length, 0.9 / inputs.Length);
            results[i] = await AudioEffectProcessor.ProcessAsync(inputs[i].Samples, MixEffectEditSession.SampleRate, inputs[i].Channels,
                settings, report, token, execution);
        }
        return results;
    }

    private Task StopMixForEffectAsync()
    {
        this.StopPlayback(); return this.playbackStopWork;
    }

    private async Task CommitMixEffectAsync(MixEffectEditSession session, IReadOnlyList<AudioEffectResult> results, string effectName,
        bool wasRange, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (closing || released) throw new OperationCanceledException(token);
        if (busy) throw new InvalidOperationException("Finish the current mix operation before applying this effect.");
        await this.StopMixForEffectAsync(); token.ThrowIfCancellationRequested();
        if (closing || released) throw new OperationCanceledException(token);
        MixEffectCommit edit = session.Apply(Arrangement, results, effectName);
        if (!edit.Changed) { this.status.Text = "Effect bypass/no-op: mix and undo history unchanged."; return; }
        this.liveUpdateCancellation?.Cancel();
        this.caret = session.MapTime(this.caret, edit.NewDuration); this.manualCaret = session.MapTime(this.manualCaret, edit.NewDuration);
        this.ClearAudioEffectSelection();
        this.selectedClips.UnionWith(edit.SelectedClipIds);
        this.selectedClip = edit.SelectedClipIds.FirstOrDefault(); this.selectedLane = session.LaneIds.FirstOrDefault();
        if (wasRange)
        {
            this.selectionFirstRow = Arrangement.Lanes.FindIndex(lane => session.LaneIds.Contains(lane.Id));
            this.selectionLastRow = Arrangement.Lanes.FindLastIndex(lane => session.LaneIds.Contains(lane.Id));
            this.selectionStart = session.Start; this.selectionEnd = session.Start + edit.NewDuration;
        }
        else this.fullSelectedClips.UnionWith(edit.SelectedClipIds);
        this.Changed(); // Exactly one existing arrangement-level undo/redo step.
        this.status.Text = $"{effectName} applied to {session.Inputs.Count} selected lane(s). Ctrl+Z / Ctrl+Y available.";
    }

    private void CloseMixEffectWindows()
    {
        if (this.mixEffectWindowsClosed) return;
        this.mixEffectWindowsClosed = true; this.mixEffectsLifetime.Cancel();
        foreach (Form dialog in this.mixEffectDialogs.Values.ToArray()) dialog.Close();
        this.mixEffectDialogs.Clear(); this.mixEffectsLifetime.Dispose();
    }

    private sealed class SliceProgress(IProgress<double> target, double offset, double length) : IProgress<double>
    {
        public void Report(double value) => target.Report(Math.Clamp(offset + Math.Clamp(value, 0, 1) * length, 0, 1));
    }
}
