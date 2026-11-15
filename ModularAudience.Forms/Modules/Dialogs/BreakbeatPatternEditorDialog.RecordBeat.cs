using System.Diagnostics;
using ModularAudience.Audio;
using ModularAudience.Audio.Processors_V1;
using ModularAudience.Generators;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class BreakbeatPatternEditorDialog
{
    private async void RecordBeatAsync()
    {
        IReadOnlyList<AudioInputDevice> devices;
        try
        {
            devices = MicrophoneAudioCapture.GetInputDevices();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, "Could not enumerate microphone devices: " + exception.Message,
                "Record Beat", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (devices.Count == 0)
        {
            MessageBox.Show(this, "No microphone input device is available. Connect a microphone and try again.",
                "Record Beat", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        BreakbeatRecordBeatSettings settings = BreakbeatRecordBeatSettings.Load();
        using BreakbeatBeatRecordingDialog recordingDialog = new(devices, settings);
        if (recordingDialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        string recordingPath = recordingDialog.RecordingFilePath;
        if (string.IsNullOrWhiteSpace(recordingPath) || !File.Exists(recordingPath))
        {
            MessageBox.Show(this, "The microphone recording is unavailable.",
                "Record Beat", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        AudioObj? recording = null;
        try
        {
            recording = await AudioObj.FromFileAsync(recordingPath);
            if (recording is null || recording.Data.Length == 0)
            {
                MessageBox.Show(this, "The microphone recording is empty.",
                    "Record Beat", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            BreakbeatTapSequence sequence = await Task.Run(() =>
                BreakbeatTapSequenceAnalyzer.Analyze(recording.Data, recording.SampleRate, recording.Channels));
            if (sequence.HitCount == 0)
            {
                MessageBox.Show(this, "No taps were detected. Check the microphone level and try again.",
                    "Record Beat", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using BreakbeatBeatPatternDialog patternDialog = new(sequence, this.samples.Count);
            if (patternDialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            await this.StopWholePatternPreviewAsync();
            await this.StopNotePreviewAsync();
            this.ApplyRecordedBeatSequence(recording, sequence, patternDialog);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, "Could not analyze or apply the recorded beat: " + exception.Message,
                "Record Beat", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            recording?.Dispose();
            DeleteTemporaryRecording(recordingPath);
        }
    }

    private void ApplyRecordedBeatSequence(
        AudioObj recording,
        BreakbeatTapSequence sequence,
        BreakbeatBeatPatternDialog options)
    {
        IReadOnlyList<BreakbeatTapHit> hits = options.GroupedHits;
        if (hits.Count == 0)
        {
            return;
        }

        int existingTrackCount = this.samples.Count;
        int drumElementCount = hits.Max(hit => hit.GroupIndex) + 1;
        this.CommitHistoryAction();
        if (options.CreateNewLines)
        {
            for (int group = existingTrackCount; group < drumElementCount; group++)
            {
                using AudioObj sample = CreateRecordedHitSample(recording, group, hits);
                this.InsertSampleTrack(sample, this.samples.Count, this.sourceSampleOrder.Count);
            }
        }

        BreakbeatPatternNote[] previousNotes = this.notes.ToArray();
        this.numericUpDown_bpm.Value = Math.Clamp(options.Bpm, this.numericUpDown_bpm.Minimum, this.numericUpDown_bpm.Maximum);
        this.numericUpDown_resolution.Value = Math.Clamp(
            options.StepsPerBar,
            (int)this.numericUpDown_resolution.Minimum,
            (int)this.numericUpDown_resolution.Maximum);
        int resolution = this.currentResolution;
        int ticksPerBar = BreakbeatGenerator_V2.PatternTicksPerBar;
        int stepTicks = Math.Max(1, ticksPerBar / resolution);
        double bpm = (double)this.numericUpDown_bpm.Value;
        int firstFrame = hits.Min(hit => hit.FrameIndex);
        Dictionary<int, double> averageRmsByGroup = hits
            .GroupBy(hit => hit.GroupIndex)
            .ToDictionary(group => group.Key, group => Math.Max(1e-8, group.Average(hit => hit.Rms)));
        List<BreakbeatPatternNote> notes = [];
        HashSet<(int Track, int StartTick)> occupiedSteps = [];

        foreach (BreakbeatTapHit hit in hits)
        {
            int row = hit.GroupIndex;
            if (row < 0 || row >= this.samples.Count)
            {
                continue;
            }

            double elapsedSeconds = Math.Max(0, hit.FrameIndex - firstFrame) / (double)recording.SampleRate;
            double rawTicks = elapsedSeconds * bpm * ticksPerBar / 240d;
            int startTick = Math.Max(0,
                (int)Math.Round(rawTicks / stepTicks, MidpointRounding.AwayFromZero) * stepTicks);
            if (!occupiedSteps.Add((row, startTick)))
            {
                continue;
            }

            int originalDurationTicks = Math.Max(1, BreakbeatGenerator_V2.GetMinimumNoteDurationTicks(
                this.samples,
                row,
                (float)bpm,
                resolution));
            float volumePercent = (float)Math.Clamp(
                hit.Rms / averageRmsByGroup[hit.GroupIndex] * 100,
                25,
                200);
            notes.Add(new BreakbeatPatternNote(
                row,
                startTick,
                stepTicks,
                ManuallyResized: true,
                OriginalDurationTicks: originalDurationTicks,
                VolumePercent: volumePercent,
                Truncated: stepTicks < originalDurationTicks,
                HardResized: true));
        }

        int maximumTick = notes.Count == 0
            ? ticksPerBar
            : notes.Max(note => note.StartTick + note.DurationTicks);
        this.bars = Math.Max(1, (maximumTick + ticksPerBar - 1) / ticksPerBar);
        int patternColumns = Math.Max(1, this.bars * resolution);
        this.pattern.Clear();
        for (int row = 0; row < this.samples.Count; row++)
        {
            this.pattern.Add(new bool[patternColumns]);
        }

        foreach (BreakbeatPatternNote note in notes)
        {
            int column = Math.Clamp(note.StartTick / stepTicks, 0, patternColumns - 1);
            this.pattern[note.TrackIndex][column] = true;
        }

        this.notes.Clear();
        this.notes.AddRange(notes);
        this.selectedNotes.Clear();
        this.deadGroupNotes.Clear();
        this.lastGenerate = null;
        this.DisableAutoRerollForManualNotes(notes);
        this.timer_historyDebounce.Stop();
        this.ConfigurePatternScrollBar();
        if (this.hScrollBar_pattern.Visible)
        {
            this.hScrollBar_pattern.Value = this.hScrollBar_pattern.Minimum;
        }

        this.RegisterPatternNotesChanged(previousNotes
            .Concat(notes)
            .Select(note => (BreakbeatPatternNote?)note)
            .ToArray());
        this.pictureBox_pattern.Invalidate();
        this.CommitHistoryAction();

        int skippedHits = hits.Count(hit => hit.GroupIndex >= this.samples.Count);
        if (skippedHits > 0)
        {
            MessageBox.Show(
                this,
                $"{skippedHits} hit(s) from drum groups without a matching track were skipped. Enable 'Create new lines to fit drum count' to add them on the next recording.",
                "Record Beat",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    private static AudioObj CreateRecordedHitSample(
        AudioObj recording,
        int groupIndex,
        IReadOnlyList<BreakbeatTapHit> hits)
    {
        BreakbeatTapHit[] groupHits = hits.Where(hit => hit.GroupIndex == groupIndex).ToArray();
        if (groupHits.Length == 0)
        {
            throw new InvalidOperationException($"No recorded tap is available for drum group {groupIndex + 1}.");
        }

        float medianRms = groupHits.Select(hit => hit.Rms).Order().ElementAt(groupHits.Length / 2);
        BreakbeatTapHit representative = groupHits
            .OrderBy(hit => Math.Abs(hit.Rms - medianRms))
            .First();
        int channels = Math.Max(1, recording.Channels);
        int frameCount = recording.Data.Length / channels;
        int preRoll = Math.Max(1, recording.SampleRate / 200);
        int startFrame = Math.Max(0, representative.FrameIndex - preRoll);
        int maximumFrames = Math.Max(1, recording.SampleRate * 300 / 1000);
        int endFrame = Math.Min(frameCount, startFrame + maximumFrames);
        int nextHitFrame = hits
            .Where(hit => hit.FrameIndex > representative.FrameIndex)
            .Select(hit => hit.FrameIndex - Math.Max(1, recording.SampleRate / 200))
            .DefaultIfEmpty(frameCount)
            .Min();
        endFrame = Math.Min(endFrame, Math.Max(startFrame + 1, nextHitFrame));
        int selectedFrames = Math.Max(1, endFrame - startFrame);
        float[] data = new float[checked(selectedFrames * channels)];
        Array.Copy(recording.Data, checked(startFrame * channels), data, 0, data.Length);

        float peak = data.Max(value => float.IsFinite(value) ? Math.Abs(value) : 0f);
        if (peak > 0.00001f)
        {
            float gain = Math.Min(8, 0.8f / peak);
            for (int index = 0; index < data.Length; index++)
            {
                data[index] = float.IsFinite(data[index]) ? Math.Clamp(data[index] * gain, -0.95f, 0.95f) : 0f;
            }
        }

        string name = $"Recorded Drum {groupIndex + 1}";
        AudioObj sample = new()
        {
            Name = name,
            Data = data,
            SampleRate = recording.SampleRate,
            Channels = channels,
            BitDepth = 32,
            Length = data.LongLength,
            Duration = TimeSpan.FromSeconds(selectedFrames / (double)recording.SampleRate),
            Bpm = recording.Bpm,
            Volume = 100
        };
        sample.Rename(name);
        return sample;
    }

    private static void DeleteTemporaryRecording(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Unable to delete temporary Record Beat file: {exception.Message}");
        }
    }
}
