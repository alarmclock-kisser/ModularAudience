using ModularAudience.Audio;
using ModularAudience.Audio.Processors_V1;
using NAudio.CoreAudioApi;
using System.IO;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class BreakbeatPatternEditorDialog
{
    private async void RecordBeatAsync()
    {
        // 1. Microphone availability check (English message).
        MMDevice[] devices = AudioRecorder.GetCaptureDevices();
        if (devices == null || devices.Length == 0)
        {
            MessageBox.Show(this, "No microphone / capture device is available.\nPlease connect a microphone and try again.",
                "Record Beat", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // 2. Start recording to a temp file.
        string tempFile = Path.Combine(Path.GetTempPath(), $"MA_RecordBeat_{Guid.NewGuid():N}.wav");
        try
        {
            await AudioRecorder.StartRecording(tempFile, devices[0]);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not start recording: " + ex.Message, "Record Beat", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // 3. Simple recording dialog with Stop button + live duration.
        using var recForm = new Form
        {
            Text = "Recording Beat…", ClientSize = new Size(320, 120),
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, TopMost = true
        };
        var label = new Label { Dock = DockStyle.Top, Height = 40, TextAlign = ContentAlignment.MiddleCenter, Text = "Recording… speak/play your knock sequence." };
        var stop = new Button { Text = "Stop", Dock = DockStyle.Bottom, Height = 32 };
        recForm.Controls.Add(label); recForm.Controls.Add(stop);
        var timer = new System.Windows.Forms.Timer { Interval = 200 };
        timer.Tick += (_, _) =>
        {
            if (AudioRecorder.RecordingTime.HasValue) label.Text = "Recording… " + AudioRecorder.RecordingTime.Value.ToString(@"mm\:ss\.ff");
        };
        timer.Start();
        var tcs = new TaskCompletionSource<bool>();
        stop.Click += (_, _) => { tcs.TrySetResult(true); };
        recForm.FormClosing += (_, e) => { if (!tcs.Task.IsCompleted) { tcs.TrySetResult(false); } };
        timer.Start();
        recForm.Show(this);
        bool stopped = await tcs.Task;
        timer.Stop();
        recForm.Close();

        AudioRecorder.StopRecording(normalizeOutput: true);
        // Wait briefly for file finalization.
        await Task.Delay(300);

        if (!stopped || string.IsNullOrEmpty(AudioRecorder.RecordedFile) || !File.Exists(AudioRecorder.RecordedFile))
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            return;
        }

        // 4. Load recorded AudioObj and estimate BPM.
        AudioObj? recorded = null;
        try
        {
            recorded = await AudioObj.FromFileAsync(AudioRecorder.RecordedFile);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not load recording: " + ex.Message, "Record Beat", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (recorded == null || recorded.Data == null || recorded.Data.Length == 0)
        {
            MessageBox.Show(this, "Recording is empty.", "Record Beat", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            recorded?.Dispose();
            return;
        }

        double estimatedBpm = 0;
        try { estimatedBpm = await BeatScanner.ScanBpmAsync(recorded); } catch { estimatedBpm = 0; }
        if (estimatedBpm <= 0 || !double.IsFinite(estimatedBpm)) estimatedBpm = 120;

        // 5. Param dialog.
        using var paramForm = new Form
        {
            Text = "Record Beat Parameters", ClientSize = new Size(360, 220),
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false
        };
        var bpmLabel = new Label { Text = "Estimated BPM:", Location = new Point(12, 12), AutoSize = true };
        var bpmNum = new NumericUpDown { Location = new Point(150, 10), Width = 120, DecimalPlaces = 2, Minimum = 40, Maximum = 240, Value = (decimal)estimatedBpm, Increment = 0.5m };
        var granLabel = new Label { Text = "Granularity:", Location = new Point(12, 48), AutoSize = true };
        var granCombo = new ComboBox { Location = new Point(150, 46), Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var g in new[] { "1/2", "1/4", "1/8", "1/16", "1/32", "1/64", "1/128", "1/256" }) granCombo.Items.Add(g);
        granCombo.SelectedIndex = 2; // 1/8
        var drumLabel = new Label { Text = "Drum elements:", Location = new Point(12, 84), AutoSize = true };
        var drumNum = new NumericUpDown { Location = new Point(150, 82), Width = 120, Minimum = 1, Maximum = 16, Value = 4, Increment = 1 };
        var okBtn = new Button { Text = "OK", Location = new Point(150, 150), Size = new Size(70, 28), DialogResult = DialogResult.OK };
        var cancelBtn = new Button { Text = "Cancel", Location = new Point(230, 150), Size = new Size(70, 28), DialogResult = DialogResult.Cancel };
        paramForm.Controls.AddRange(new Control[] { bpmLabel, bpmNum, granLabel, granCombo, drumLabel, drumNum, okBtn, cancelBtn });
        paramForm.AcceptButton = null; // Enter must not auto-confirm
        paramForm.CancelButton = cancelBtn;

        if (paramForm.ShowDialog(this) != DialogResult.OK)
        {
            recorded.Dispose();
            try { File.Delete(AudioRecorder.RecordedFile); } catch { }
            return;
        }

        double targetBpm = (double)bpmNum.Value;
        // 6. Save the recorded sample into a new Audio Collection so it can be reused/dropped.
        try
        {
            recorded.Rename("RecordedBeat");
            var collection = new AudioCollectionView(new[] { recorded });
            recorded = null; // collection owns it
            collection.Show();
            collection.Rename("Recorded Beat");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not save recorded sample: " + ex.Message, "Record Beat", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { recorded?.Dispose(); }

        // 7. Notify.
        MessageBox.Show(this, $"Recorded beat added as sample.\nEstimated/target BPM: {targetBpm:F2}\nGranularity: {granCombo.SelectedItem}\nDrum elements: {(int)drumNum.Value}",
            "Record Beat", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}