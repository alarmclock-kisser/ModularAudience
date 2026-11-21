using ModularAudience.Audio;
using ModularAudience.Audio.Arrangement;
using ModularAudience.Audio.Processing;
using ModularAudience.Audio.Processors_V4;
using ModularAudience.Forms.Modules.Dialogs;
using ModularAudience.Generators;

namespace ModularAudience.Forms.Modules;

public sealed partial class MixTrackView
{
    private BreakbeatPatternEditorDialog? mixPatternEditor;
    private MixPatternEditorSession? mixPatternEditorSession;

    private async Task OpenSelectionInBreakbeatPatternEditorAsync()
    {
        if (busy || closing || released || SelectedParts().Count == 0)
        {
            return;
        }

        if (this.mixPatternEditor is { IsDisposed: false })
        {
            this.mixPatternEditor.Activate();
            return;
        }

        await this.RunAsync(async token =>
        {
            List<ClipboardPart> selected = this.SelectedParts();
            if (selected.Count == 0)
            {
                return;
            }

            string arrangementKey = MixAudioHistory.Key(this.Arrangement);
            double start = selected.Min(part => part.Clip.Start);
            double end = selected.Max(part => part.Clip.Start
                + this.Arrangement.ClipDuration(part.Clip));
            int insertionRow = selected.Min(part => part.Row);
            bool fullSelection = this.fullSelectedClips.Count > 0
                || this.selectionFirstRow < 0
                || this.selectionEnd <= this.selectionStart;
            List<MixPatternSource> rendered = await this.RenderSelectedPatternPartsAsync(
                selected,
                token);
            token.ThrowIfCancellationRequested();

            LoopAtomizerSettings detectionSettings = CreateMixPatternDetectionSettings();
            var dryRuns = new List<LoopAtomizerResult>(rendered.Count);
            bool atomizeRequired = false;
            try
            {
                foreach (MixPatternSource part in rendered)
                {
                    LoopAtomizerResult result = await LoopAtomizer_V4.AtomizeAsync(
                        part.Audio,
                        detectionSettings,
                        cancellationToken: token);
                    dryRuns.Add(result);
                    atomizeRequired |= result.Atomics.Count != 1;
                }

                if (atomizeRequired)
                {
                    DisposeAtomizerResults(dryRuns);
                    dryRuns.Clear();
                    DialogResult answer;
                    LoopAtomizerSettings settings;
                    using (var dialog = new MixPatternAtomizeDialog())
                    {
                        answer = await this.ShowMixDialogAsync(dialog);
                        settings = dialog.Settings;
                    }
                    if (answer != DialogResult.OK)
                    {
                        DisposeRenderedSources(rendered);
                        return;
                    }

                    foreach (MixPatternSource part in rendered)
                    {
                        dryRuns.Add(await LoopAtomizer_V4.AtomizeAsync(
                            part.Audio,
                            settings,
                            cancellationToken: token));
                    }
                }
                else
                {
                    DisposeAtomizerResults(dryRuns);
                    dryRuns.Clear();
                }

                List<PatternSourceAtomic> atomics = atomizeRequired
                    ? this.ExtractConfiguredAtomics(rendered, dryRuns, start)
                    : rendered.Select(part => new PatternSourceAtomic(
                        part.Audio,
                        part.ClipStart - start,
                        part.Audio.Duration.TotalSeconds,
                        this.GetMixPatternSourceName(part))).ToList();
                this.OpenBreakbeatPatternEditor(
                    atomics,
                    rendered,
                    start,
                    end,
                    insertionRow,
                    fullSelection,
                    selected.Select(part => part.Clip.Id).ToHashSet(),
                    arrangementKey,
                    atomizeRequired
                        ? atomics.Select(atomic => atomic.Audio)
                            .Concat(rendered.Select(part => part.Audio)).ToList()
                        : rendered.Select(part => part.Audio).ToList());
                if (atomizeRequired)
                {
                    dryRuns.Clear();
                }
            }
            catch
            {
                DisposeAtomizerResults(dryRuns);
                DisposeRenderedSources(rendered);
                throw;
            }
        });
    }

    private async Task<List<MixPatternSource>> RenderSelectedPatternPartsAsync(
        IReadOnlyList<ClipboardPart> parts,
        CancellationToken cancellationToken)
    {
        var rendered = new List<MixPatternSource>(parts.Count);
        try
        {
            foreach (ClipboardPart part in parts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                MixLane lane = this.Arrangement.Lanes[part.Row];
                var partSnapshot = this.RenderSnapshot();
                try
                {
                    var partLane = CloneLaneForPatternImport(lane, part.Clip);
                    partSnapshot.Lanes = [partLane];
                    using AudioObj audio = await MixRenderer.RenderAsync(
                        partSnapshot,
                        cancellationToken);
                    AudioObj owned = audio.Clone();
                    owned.Rename(this.Arrangement.Sources.TryGetValue(
                        part.Clip.SourceId,
                        out MixSource? source)
                        ? source.Name
                        : part.Clip.Name);
                    rendered.Add(new MixPatternSource(
                        owned,
                        part.Row,
                        part.Clip.Start,
                        this.GetLanePatternLabel(lane, part.Clip)));
                }
                finally
                {
                    partSnapshot.Dispose();
                }
            }

            return rendered;
        }
        catch
        {
            DisposeRenderedSources(rendered);
            throw;
        }
    }

    private static MixLane CloneLaneForPatternImport(MixLane source, MixClip clip)
    {
        MixClip copy = CloneClip(clip);
        copy.Start = 0;
        return new MixLane
        {
            Name = source.Name,
            Gain = source.Gain,
            AutoCrossfade = false,
            Mute = false,
            Solo = false,
            Clips = [copy]
        };
    }

    private static LoopAtomizerSettings CreateMixPatternDetectionSettings() =>
        LoopAtomizerSettings.Default with
        {
            MinSliceMs = 60,
            TailPaddingMs = 30,
            MinimumRmsLevel = 0.001f,
            MinimumAtomicDurationMs = 20,
            EnableDeduplication = false
        };

    private List<PatternSourceAtomic> ExtractConfiguredAtomics(
        IReadOnlyList<MixPatternSource> rendered,
        IReadOnlyList<LoopAtomizerResult> results,
        double selectionStart)
    {
        var atomics = new List<PatternSourceAtomic>();
        for (int index = 0; index < rendered.Count; index++)
        {
            MixPatternSource part = rendered[index];
            foreach (AudioObj atomic in results[index].Atomics)
            {
                double offset = 0;
                if (AudioAtomicTiming.TryGet(atomic, out long sourceFrame, out _)
                    && atomic.SampleRate > 0)
                {
                    offset = sourceFrame / (double)atomic.SampleRate;
                }

                atomics.Add(new PatternSourceAtomic(
                    atomic,
                    part.ClipStart - selectionStart + offset,
                    atomic.Duration.TotalSeconds,
                    this.GetMixPatternSourceName(part)));
            }
        }

        return atomics;
    }

    private void OpenBreakbeatPatternEditor(
        IReadOnlyList<PatternSourceAtomic> atomics,
        IReadOnlyList<MixPatternSource> rendered,
        double selectionStart,
        double selectionEnd,
        int insertionRow,
        bool fullSelection,
        HashSet<Guid> selectedIds,
        string arrangementKey,
        IReadOnlyList<AudioObj> ownedSources)
    {
        if (atomics.Count == 0)
        {
            throw new InvalidOperationException("No usable atomics were produced for the selected mix range.");
        }

        double bpm = this.Arrangement.Bpm;
        double secondsPerBar = 240d / bpm;
        double duration = Math.Max(atomics.Max(atomic => atomic.RelativeStart + atomic.Duration), 0);
        duration = Math.Max(duration, selectionEnd - selectionStart);
        int bars = Math.Max(1, (int)Math.Ceiling(duration / secondsPerBar - 1e-9));
        int resolution = EstimatePatternResolution(atomics, secondsPerBar);
        var samples = atomics.Select(atomic => atomic.Audio).ToList();
        var labels = atomics.Select((atomic, index) =>
            $"{atomic.SourceName} #{index + 1}").ToList();
        var pattern = samples.Select(_ => new bool[bars * resolution]).ToList();
        var notes = atomics.Select((atomic, track) => new BreakbeatPatternNote(
            track,
            (int)Math.Round(atomic.RelativeStart / secondsPerBar
                * BreakbeatGenerator_V2.PatternTicksPerBar),
            Math.Max(1, (int)Math.Round(atomic.Duration / secondsPerBar
                * BreakbeatGenerator_V2.PatternTicksPerBar)),
            OriginalDurationTicks: Math.Max(1, (int)Math.Round(atomic.Duration / secondsPerBar
                * BreakbeatGenerator_V2.PatternTicksPerBar)))).ToList();

        var editor = new BreakbeatPatternEditorDialog(
            pattern,
            samples,
            labels,
            bars,
            resolution,
            0,
            (decimal)bpm,
            notes,
            resolution);
        this.mixPatternEditor = editor;
        this.mixPatternEditorSession = new MixPatternEditorSession(
            selectionStart,
            selectionEnd,
            insertionRow,
            fullSelection,
            selectedIds,
            arrangementKey,
            [],
            ownedSources.ToList(),
            editor);
        editor.FormClosed += this.MixPatternEditor_FormClosed;
        editor.SaveRequested += this.MixPatternEditor_SaveRequestedAsync;
        editor.Show();
        this.status.Text = $"Pattern Editor opened with {atomics.Count} atomic track(s).";
    }

    private async Task MixPatternEditor_SaveRequestedAsync(BreakbeatPatternEditorDialog editor)
    {
        MixPatternEditorSession session = this.mixPatternEditorSession
            ?? throw new InvalidOperationException("The Mix selection editor session is no longer available.");
        if (!ReferenceEquals(session.Editor, editor))
        {
            return;
        }

        if (MixAudioHistory.Key(this.Arrangement) != session.ArrangementKey)
        {
            throw new InvalidOperationException(
                "The Mix arrangement changed while the Pattern Editor was open. Undo the later edits or reopen the selection.");
        }
        if (busy || closing || released)
        {
            throw new InvalidOperationException("Finish the current Mix operation before saving the pattern.");
        }

        IReadOnlyList<BreakbeatPatternMixTrack> tracks =
            await editor.RenderTracksForMixAsync();
        try
        {
            MixPatternEditorSession updated = this.ReplaceMixSelectionWithPattern(session, tracks);
            this.mixPatternEditorSession = session with
            {
                ArrangementKey = MixAudioHistory.Key(this.Arrangement),
                SelectedIds = updated.SelectedIds,
                GeneratedLaneIds = updated.GeneratedLaneIds,
                FullSelection = true
            };
        }
        finally
        {
            foreach (BreakbeatPatternMixTrack track in tracks)
            {
                track.Audio.Dispose();
            }
        }
    }

    private MixPatternEditorSession ReplaceMixSelectionWithPattern(
        MixPatternEditorSession session,
        IReadOnlyList<BreakbeatPatternMixTrack> tracks)
    {
        if (tracks.Count == 0)
        {
            throw new InvalidOperationException("The Pattern Editor contains no enabled notes to apply.");
        }

        if (session.GeneratedLaneIds.Count > 0)
        {
            this.Arrangement.Lanes.RemoveAll(lane =>
                session.GeneratedLaneIds.Contains(lane.Id));
        }
        else if (session.FullSelection)
        {
            foreach (MixLane lane in this.Arrangement.Lanes)
            {
                lane.Clips.RemoveAll(clip => session.SelectedIds.Contains(clip.Id));
            }
        }
        else
        {
            for (int row = 0; row < this.Arrangement.Lanes.Count; row++)
            {
                if (row < session.InsertionRow)
                {
                    continue;
                }

                this.RemovePatternSelectionRange(
                    this.Arrangement.Lanes[row],
                    session.SelectionStart,
                    session.SelectionEnd,
                    session.SelectedIds);
            }
        }

        int insertAt = Math.Clamp(session.InsertionRow, 0, this.Arrangement.Lanes.Count);
        var newClipIds = new HashSet<Guid>();
        var newLaneIds = new HashSet<Guid>();
        foreach (BreakbeatPatternMixTrack track in tracks)
        {
            MixSource source = this.Arrangement.AddSource(track.Audio);
            var lane = new MixLane
            {
                Name = track.Name
            };
            MixClip clip = this.Arrangement.AddClip(lane, source, session.SelectionStart);
            this.Arrangement.Lanes.Insert(insertAt++, lane);
            newClipIds.Add(clip.Id);
            newLaneIds.Add(lane.Id);
        }

        this.ClearAudioEffectSelection();
        this.selectedClips.UnionWith(newClipIds);
        this.fullSelectedClips.UnionWith(newClipIds);
        this.selectedLane = tracks.Count > 0
            ? this.Arrangement.Lanes[Math.Max(0, insertAt - tracks.Count)].Id
            : null;
        this.Changed();
        this.status.Text = $"Pattern applied to {tracks.Count} Mix track(s). Ctrl+Z / Ctrl+Y available.";
        return session with
        {
            SelectedIds = newClipIds,
            GeneratedLaneIds = newLaneIds,
            FullSelection = true
        };
    }

    private void RemovePatternSelectionRange(
        MixLane lane,
        double start,
        double end,
        IReadOnlySet<Guid> selectedIds)
    {
        foreach (MixClip clip in lane.Clips.ToArray())
        {
            double clipEnd = clip.Start + this.Arrangement.ClipDuration(clip);
            if (!selectedIds.Contains(clip.Id)
                || clip.Start >= end
                || clipEnd <= start)
            {
                continue;
            }

            lane.Clips.Remove(clip);
            double rate = this.Arrangement.Rate(clip);
            if (clip.Start < start)
            {
                MixClip left = CloneClip(clip);
                left.Id = Guid.NewGuid();
                left.SourceDuration = (start - clip.Start) * rate;
                left.FadeOut = 0;
                lane.Clips.Add(left);
            }
            if (clipEnd > end)
            {
                MixClip right = CloneClip(clip);
                right.Id = Guid.NewGuid();
                right.Start = end;
                right.SourceOffset += (end - clip.Start) * rate;
                right.SourceDuration = (clipEnd - end) * rate;
                right.FadeIn = 0;
                lane.Clips.Add(right);
            }
        }
    }

    private void MixPatternEditor_FormClosed(object? sender, FormClosedEventArgs e)
    {
        if (sender is not BreakbeatPatternEditorDialog editor)
        {
            return;
        }

        editor.SaveRequested -= this.MixPatternEditor_SaveRequestedAsync;
        editor.FormClosed -= this.MixPatternEditor_FormClosed;
        if (this.mixPatternEditorSession is { } session && ReferenceEquals(session.Editor, editor))
        {
            DisposeRenderedSources(session.OwnedSources);
            this.mixPatternEditorSession = null;
        }
        if (ReferenceEquals(this.mixPatternEditor, editor))
        {
            this.mixPatternEditor = null;
        }
    }

    private string GetMixPatternSourceName(MixPatternSource source) =>
        $"{this.Arrangement.Lanes[source.Row].Name} · {source.Label}";

    private string GetLanePatternLabel(MixLane lane, MixClip clip) =>
        !string.IsNullOrWhiteSpace(clip.Name)
            ? clip.Name
            : lane.Name;

    private static int EstimatePatternResolution(
        IReadOnlyList<PatternSourceAtomic> atomics,
        double secondsPerBar)
    {
        double minimumSpacing = double.PositiveInfinity;
        double[] starts = atomics
            .Select(atomic => atomic.RelativeStart)
            .Distinct()
            .Order()
            .ToArray();
        for (int index = 1; index < starts.Length; index++)
        {
            double spacing = starts[index] - starts[index - 1];
            if (spacing > 0.0001)
            {
                minimumSpacing = Math.Min(minimumSpacing, spacing);
            }
        }
        foreach (PatternSourceAtomic atomic in atomics)
        {
            if (atomic.Duration > 0.0001)
            {
                minimumSpacing = Math.Min(minimumSpacing, atomic.Duration);
            }
        }

        int required = double.IsFinite(minimumSpacing)
            ? Math.Max(4, (int)Math.Ceiling(secondsPerBar / minimumSpacing - 1e-9))
            : 16;
        int[] divisions = [4, 8, 16, 32, 64, 128, 256];
        return divisions.FirstOrDefault(value => value >= required, 256);
    }

    private static void DisposeAtomizerResults(IEnumerable<LoopAtomizerResult> results)
    {
        foreach (LoopAtomizerResult result in results)
        {
            foreach (AudioObj atomic in result.Atomics)
            {
                atomic.Dispose();
            }
        }
    }

    private static void DisposeRenderedSources(IEnumerable<MixPatternSource> sources)
    {
        foreach (MixPatternSource source in sources)
        {
            source.Audio.Dispose();
        }
    }

    private static void DisposeRenderedSources(IEnumerable<AudioObj> sources)
    {
        foreach (AudioObj source in sources)
        {
            source.Dispose();
        }
    }

    private sealed record MixPatternSource(
        AudioObj Audio,
        int Row,
        double ClipStart,
        string Label);

    private sealed record PatternSourceAtomic(
        AudioObj Audio,
        double RelativeStart,
        double Duration,
        string SourceName);

    private sealed record MixPatternEditorSession(
        double SelectionStart,
        double SelectionEnd,
        int InsertionRow,
        bool FullSelection,
        HashSet<Guid> SelectedIds,
        string ArrangementKey,
        HashSet<Guid> GeneratedLaneIds,
        List<AudioObj> OwnedSources,
        BreakbeatPatternEditorDialog Editor);
}
