using System.Diagnostics;
using ModularAudience.Audio;

namespace ModularAudience.Forms.Modules.Dialogs;

internal sealed partial class BreakbeatBeatRecordingDialog : Form
{
    private readonly BreakbeatRecordBeatSettings settings;
    private readonly string recordingPath = Path.Combine(Path.GetTempPath(), $"MA_RecordBeat_{Guid.NewGuid():N}.wav");
    private MicrophoneAudioCapture? capture;
    private readonly Stopwatch recordingClock = new();
    private bool initializingDeviceSelection = true;
    private bool restartingCapture;
    private bool closing;
    private bool acceptRecording;

    internal BreakbeatBeatRecordingDialog(
        IReadOnlyList<AudioInputDevice> devices,
        BreakbeatRecordBeatSettings settings)
    {
        this.InitializeComponent();
        this.settings = settings;
        this.comboBox_inputDevice.Items.AddRange(devices.Cast<object>().ToArray());
        int selectedIndex = devices.ToList().FindIndex(device =>
            string.Equals(device.Id, settings.InputDeviceId, StringComparison.Ordinal));
        this.comboBox_inputDevice.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
        this.initializingDeviceSelection = false;
        this.button_stop.Enabled = false;
    }

    internal string RecordingFilePath => this.acceptRecording ? this.recordingPath : string.Empty;

    private async void BreakbeatBeatRecordingDialog_Shown(object? sender, EventArgs e)
    {
        if (this.comboBox_inputDevice.SelectedItem is AudioInputDevice device)
        {
            await this.RestartCaptureAsync(device);
        }
    }

    private async void comboBox_inputDevice_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (this.initializingDeviceSelection || this.closing
            || this.comboBox_inputDevice.SelectedItem is not AudioInputDevice device)
        {
            return;
        }

        await this.RestartCaptureAsync(device);
    }

    private async Task RestartCaptureAsync(AudioInputDevice device)
    {
        if (this.restartingCapture || this.closing)
        {
            return;
        }

        this.restartingCapture = true;
        this.timer_recording.Stop();
        this.comboBox_inputDevice.Enabled = false;
        this.button_stop.Enabled = false;
        try
        {
            await this.StopCaptureAsync();
            if (this.closing)
            {
                return;
            }

            if (File.Exists(this.recordingPath))
            {
                File.Delete(this.recordingPath);
            }

            this.progressBar_inputLevel.Value = 0;
            this.label_elapsed.Text = "Recording time: 00:00.000";
            this.label_status.Text = "Starting microphone...";
            this.recordingClock.Reset();
            this.capture = MicrophoneAudioCapture.Start(device.Id, this.recordingPath);
            this.recordingClock.Start();
            this.timer_recording.Start();
            this.label_status.Text = "Recording. Play or tap your beat.";
            this.button_stop.Enabled = true;

            this.settings.InputDeviceId = device.Id;
            try
            {
                this.settings.Save();
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, "Could not save the selected microphone: " + exception.Message,
                    "Record Beat", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        catch (Exception exception)
        {
            this.label_status.Text = "Microphone could not be started.";
            MessageBox.Show(this, "Could not start microphone recording: " + exception.Message,
                "Record Beat", MessageBoxButtons.OK, MessageBoxIcon.Error);
            await this.StopCaptureAsync();
        }
        finally
        {
            this.comboBox_inputDevice.Enabled = true;
            this.restartingCapture = false;
        }
    }

    private async void timer_recording_Tick(object? sender, EventArgs e)
    {
        MicrophoneAudioCapture? activeCapture = this.capture;
        if (activeCapture is null)
        {
            return;
        }

        if (activeCapture.Failure is Exception failure)
        {
            this.timer_recording.Stop();
            this.button_stop.Enabled = false;
            this.label_status.Text = "Microphone recording failed.";
            await this.StopCaptureAsync();
            MessageBox.Show(this, "Microphone recording failed: " + failure.Message,
                "Record Beat", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        this.label_elapsed.Text = $"Recording time: {this.recordingClock.Elapsed:mm\\:ss\\.fff}";
        float peak = activeCapture.PeakLevel;
        this.progressBar_inputLevel.Value = Math.Clamp(
            (int)Math.Round(peak * this.progressBar_inputLevel.Maximum),
            this.progressBar_inputLevel.Minimum,
            this.progressBar_inputLevel.Maximum);
        this.label_inputLevel.Text = $"Mic level {peak:P0}";
    }

    private void button_stop_Click(object? sender, EventArgs e)
    {
        this.acceptRecording = this.capture is not null;
        this.Close();
    }

    private void button_cancel_Click(object? sender, EventArgs e)
    {
        this.acceptRecording = false;
        this.Close();
    }

    private async void BreakbeatBeatRecordingDialog_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (this.closing)
        {
            return;
        }

        e.Cancel = true;
        this.closing = true;
        this.timer_recording.Stop();
        this.button_stop.Enabled = false;
        this.comboBox_inputDevice.Enabled = false;
        await this.StopCaptureAsync();

        if (!this.acceptRecording || !File.Exists(this.recordingPath) || new FileInfo(this.recordingPath).Length <= 44)
        {
            this.acceptRecording = false;
            this.DeleteRecordingFile();
            this.DialogResult = DialogResult.Cancel;
        }
        else
        {
            this.DialogResult = DialogResult.OK;
        }

        this.FormClosing -= this.BreakbeatBeatRecordingDialog_FormClosing;
        this.Close();
    }

    private async Task StopCaptureAsync()
    {
        MicrophoneAudioCapture? activeCapture = this.capture;
        this.capture = null;
        this.recordingClock.Stop();
        if (activeCapture is null)
        {
            return;
        }

        try
        {
            await activeCapture.StopAsync();
        }
        catch (Exception exception)
        {
            this.acceptRecording = false;
            this.label_status.Text = "Microphone recording could not be finalized.";
            MessageBox.Show(this, "Could not finalize microphone recording: " + exception.Message,
                "Record Beat", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DeleteRecordingFile()
    {
        try
        {
            if (File.Exists(this.recordingPath))
            {
                File.Delete(this.recordingPath);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Unable to delete temporary Record Beat file: {exception.Message}");
        }
    }
}
