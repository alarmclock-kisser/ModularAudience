using ModularAudience.Audio;
using ModularAudience.Audio.Processors_V4;
using ModularAudience.Generators;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class BreakbeatPatternEditorDialog
{
    private sealed record TrackDeduplicationBucket(int[] DisplayTrackIndices, AudioObj[] Samples);

    private sealed record TrackDuplicateGroup(int RepresentativeTrackIndex, int[] TrackIndices);

    private sealed class ProgressAdapter(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    private async void button_deduplicate_Click(object? sender, EventArgs e)
    {
        TrackDeduplicationBucket[] buckets = this.CreateTrackDeduplicationBuckets();
        int candidateTrackCount = buckets.Sum(bucket => bucket.Samples.Length);
        if (candidateTrackCount < 2)
        {
            MessageBox.Show(this,
                "There are no two compatible sample tracks to compare. Instrument tracks, Auto-Reroll tracks, and tracks with different playback settings are excluded.",
                "Deduplicate Pattern Tracks",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        using BreakbeatPatternDeduplicationDialog configuration =
            new(candidateTrackCount);
        if (configuration.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        this.timer_historyDebounce.Stop();
        this.CommitHistoryAction();

        using CancellationTokenSource cancellationSource = new();
        ProgressDialog? activeProgressDialog = null;
        Progress<double> progress = new(value => activeProgressDialog?.Report(value));
        using ProgressDialog progressDialog = new(
            "Deduplicating Breakbeat pattern tracks...",
            progress,
            windowCloseDelay: 0.0d,
            ct: cancellationSource.Token,
            cancellationSource: cancellationSource);
        activeProgressDialog = progressDialog;

        bool wasEnabled = this.Enabled;
        bool previousWaitCursor = this.UseWaitCursor;
        this.Enabled = false;
        this.UseWaitCursor = true;

        TrackDuplicateGroup[] duplicateGroups = [];
        Exception? failure = null;
        bool cancelled = false;
        try
        {
            progressDialog.Show(this);
            progressDialog.BringToFront();
            duplicateGroups = await Task.Run(
                () => FindTrackDuplicateGroups(
                    buckets,
                    configuration.SimilarityThreshold,
                    progress,
                    cancellationSource.Token),
                cancellationSource.Token);
            cancellationSource.Token.ThrowIfCancellationRequested();
            if (duplicateGroups.Length > 0)
            {
                this.ApplyTrackDuplicateGroups(duplicateGroups);
            }

            progressDialog.Complete();
        }
        catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
        {
            cancelled = true;
            LogManager.Log("Breakbeat Pattern Editor track deduplication cancelled.");
        }
        catch (Exception error)
        {
            failure = error;
            LogManager.Log("Breakbeat Pattern Editor track deduplication failed.");
            LogManager.Log(error);
        }
        finally
        {
            if (!progressDialog.IsDisposed)
            {
                progressDialog.Close();
            }

            this.Enabled = wasEnabled;
            this.UseWaitCursor = previousWaitCursor;
            if (wasEnabled && !this.IsDisposed && !this.Disposing)
            {
                this.BringToFront();
                this.Activate();
            }
        }

        if (cancelled)
        {
            return;
        }

        if (failure is not null)
        {
            MessageBox.Show(this,
                failure.Message,
                "Pattern track deduplication failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        if (duplicateGroups.Length == 0)
        {
            MessageBox.Show(this,
                "No similar sample tracks were found at this threshold.",
                "Deduplicate Pattern Tracks",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        int removedTrackCount = duplicateGroups.Sum(group => group.TrackIndices.Length - 1);
        LogManager.Log(
            $"Breakbeat Pattern Editor deduplication merged {removedTrackCount} similar track(s) at threshold {configuration.SimilarityThreshold:F3}.");
        this.toolTip_pattern.Show(
            $"Merged {removedTrackCount} similar track(s). Undo with Ctrl+Z; redo with Ctrl+Y.",
            this.button_deduplicate,
            0,
            this.button_deduplicate.Height,
            2600);
    }

    private TrackDeduplicationBucket[] CreateTrackDeduplicationBuckets()
    {
        return Enumerable.Range(0, this.samples.Count)
            .Where(this.IsEligibleForTrackDeduplication)
            .GroupBy(index => this.trackSettings[index])
            .Select(group =>
            {
                int[] displayIndices = group.ToArray();
                return new TrackDeduplicationBucket(
                    displayIndices,
                    displayIndices.Select(index => this.samples[index]).ToArray());
            })
            .Where(bucket => bucket.Samples.Length > 1)
            .ToArray();
    }

    private bool IsEligibleForTrackDeduplication(int displayIndex)
    {
        AudioObj sample = this.samples[displayIndex];
        if (sample.Channels <= 0
            || sample.SampleRate <= 0
            || sample.Data.Length < sample.Channels
            || this.autoRerolls.ContainsKey(sample))
        {
            return false;
        }

        int sourceIndex = this.GetOriginalTrackIndex(displayIndex);
        return sourceIndex >= 0
            && sourceIndex < this.sourceSampleOrder.Count
            && !this.instrumentSettingsBySample.ContainsKey(this.sourceSampleOrder[sourceIndex]);
    }

    private static TrackDuplicateGroup[] FindTrackDuplicateGroups(
        IReadOnlyList<TrackDeduplicationBucket> buckets,
        float similarityThreshold,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        int totalSamples = buckets.Sum(bucket => bucket.Samples.Length);
        if (totalSamples == 0)
        {
            return [];
        }

        List<TrackDuplicateGroup> result = [];
        int completedSamples = 0;
        foreach (TrackDeduplicationBucket bucket in buckets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int bucketOffset = completedSamples;
            int bucketSize = bucket.Samples.Length;
            IProgress<double> bucketProgress = new ProgressAdapter(value =>
                progress.Report((bucketOffset + (value * bucketSize)) / totalSamples));
            IReadOnlyList<AtomicSampleSimilarityGroup> groups =
                AtomicSampleGrouping.FindDuplicateGroups(
                    bucket.Samples,
                    similarityThreshold,
                    bucketProgress,
                    cancellationToken);

            foreach (AtomicSampleSimilarityGroup group in groups)
            {
                result.Add(new TrackDuplicateGroup(
                    bucket.DisplayTrackIndices[group.RepresentativeIndex],
                    group.MemberIndices
                        .Select(index => bucket.DisplayTrackIndices[index])
                        .ToArray()));
            }

            completedSamples += bucketSize;
            progress.Report(completedSamples / (double)totalSamples);
        }

        return result.ToArray();
    }

    private int ApplyTrackDuplicateGroups(IReadOnlyList<TrackDuplicateGroup> groups)
    {
        int trackCount = this.samples.Count;
        int[] representativeForTrack = Enumerable.Range(0, trackCount).ToArray();
        HashSet<int> groupedTracks = [];
        foreach (TrackDuplicateGroup group in groups)
        {
            if (group.TrackIndices.Length < 2
                || group.RepresentativeTrackIndex < 0
                || group.RepresentativeTrackIndex >= trackCount
                || !group.TrackIndices.Contains(group.RepresentativeTrackIndex))
            {
                throw new InvalidOperationException("The audio grouping returned an invalid track group.");
            }

            foreach (int trackIndex in group.TrackIndices)
            {
                if (trackIndex < 0
                    || trackIndex >= trackCount
                    || !groupedTracks.Add(trackIndex)
                    || this.trackSettings[trackIndex] != this.trackSettings[group.RepresentativeTrackIndex])
                {
                    throw new InvalidOperationException("The audio grouping returned incompatible track indices.");
                }

                representativeForTrack[trackIndex] = group.RepresentativeTrackIndex;
            }
        }

        int[] removedRows = Enumerable.Range(0, trackCount)
            .Where(index => representativeForTrack[index] != index)
            .OrderByDescending(index => index)
            .ToArray();
        if (removedRows.Length == 0)
        {
            return 0;
        }

        this.timer_historyDebounce.Stop();
        this.CommitHistoryAction();
        PatternEditorHistoryState beforeDeduplication = this.CaptureHistoryState();

        foreach (TrackDuplicateGroup group in groups)
        {
            int representative = group.RepresentativeTrackIndex;
            bool[] combinedPattern = new bool[group.TrackIndices
                .Max(trackIndex => this.pattern[trackIndex].Length)];
            foreach (int trackIndex in group.TrackIndices)
            {
                bool[] sourcePattern = this.pattern[trackIndex];
                for (int column = 0; column < sourcePattern.Length; column++)
                {
                    combinedPattern[column] |= sourcePattern[column];
                }
            }

            this.pattern[representative] = combinedPattern;
            AudioObj representativeSample = this.samples[representative];
            bool groupIsSolo = group.TrackIndices.Any(index => this.soloTracks.Contains(this.samples[index]));
            bool groupIsMuted = group.TrackIndices.All(index => this.mutedTracks.Contains(this.samples[index]));
            this.soloTracks.Remove(representativeSample);
            this.mutedTracks.Remove(representativeSample);
            if (groupIsSolo)
            {
                this.soloTracks.Add(representativeSample);
            }

            if (groupIsMuted)
            {
                this.mutedTracks.Add(representativeSample);
            }
        }

        BreakbeatPatternNote MapToRepresentative(BreakbeatPatternNote note) =>
            note.TrackIndex >= 0 && note.TrackIndex < representativeForTrack.Length
                ? note with { TrackIndex = representativeForTrack[note.TrackIndex] }
                : note;

        ReplaceContents(this.notes, this.notes.Select(MapToRepresentative).Distinct());
        BreakbeatPatternNote[] selectedNotes = this.selectedNotes
            .Select(MapToRepresentative)
            .Distinct()
            .ToArray();
        this.selectedNotes.Clear();
        foreach (BreakbeatPatternNote note in selectedNotes)
        {
            this.selectedNotes.Add(note);
        }

        ReplaceContents(this.copiedNotes, this.copiedNotes.Select(MapToRepresentative).Distinct());
        ReplaceContents(this.pastePreviewNotes, this.pastePreviewNotes.Select(MapToRepresentative).Distinct());
        this.hoverPitchNote = null;
        this.hoverPitchTooltipVisible = false;
        this.timer_pitchTooltip.Stop();
        this.toolTip_pattern.Hide(this.pictureBox_pattern);
        this.notePreviewCancellationTokenSource?.Cancel();
        this.hearCancellationTokenSource?.Cancel();
        this.deadGroupNotes.Clear();

        foreach (int row in removedRows)
        {
            this.RemoveSampleTrack(row, recordUndo: false, notifyTrackSet: false);
        }

        this.undoHistory.Push(beforeDeduplication);
        this.redoHistory.Clear();
        this.lastHistoryState = this.CaptureHistoryState();
        this.TrackSetRestored?.Invoke(this);
        this.pictureBox_pattern.Invalidate();
        return removedRows.Length;
    }
}
