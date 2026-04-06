using ModularAudience.Audio.Processors_V1;
using ModularAudience.Audio.Processors_V2;
using ModularAudience.Audio;
using ModularAudience.Audio.Processors_V3;
using ModularAudience.Forms.Helpers;

namespace ModularAudience.Forms
{
    public partial class WindowMain
    {
        private async void button_scanBpm_Click(object sender, EventArgs e)
        {
            if (LastSelectedTrackView == null)
            {
                return;
            }

            double scannedBpm = await BeatScanner.ScanBpmAsync(LastSelectedTrackView.OriginalAudio);
            this.textBox_scanBpmResult.Text = scannedBpm.ToString("F3") + " BPM";
            LastSelectedTrackView.OriginalAudio.ScannedBpm = (float)scannedBpm;
        }

        private async void button_scanTiming_Click(object sender, EventArgs e)
        {
            if (LastSelectedTrackView == null)
            {
                return;
            }

            float scannedTiming = await BeatScanner_V2.ScanTimingAsync(LastSelectedTrackView.OriginalAudio);
            this.textBox_scanTimingResult.Text = WindowMainFormatHelpers.GetTimingString(scannedTiming);
            LastSelectedTrackView.OriginalAudio.ScannedTiming = scannedTiming;
        }

        private async void button_scanKey_Click(object sender, EventArgs e)
        {
            if (LastSelectedTrackView == null)
            {
                return;
            }

            string scannedKey = await BeatScanner_V2.ScanKeyAsync(LastSelectedTrackView.OriginalAudio);
            this.textBox_scanKeyResult.Text = scannedKey;
            LastSelectedTrackView.OriginalAudio.ScannedKey = scannedKey;
        }

        private void textBox_scanBpmResult_DoubleClick(object? sender, EventArgs e)
        {
            if (LastSelectedTrackView == null)
            {
                return;
            }

            this.textBox_scanBpmResult.ReadOnly = false;
            this.textBox_scanBpmResult.Text = LastSelectedTrackView.OriginalAudio.ScannedBpm > 0
                ? LastSelectedTrackView.OriginalAudio.ScannedBpm.ToString("0.###")
                : "";
            this.textBox_scanBpmResult.Focus();
            this.textBox_scanBpmResult.SelectAll();

            this.textBox_scanBpmResult.Leave -= this.TextBox_scanBpmResult_LeaveOrEndEdit;
            this.textBox_scanBpmResult.KeyDown -= this.TextBox_scanBpmResult_KeyDown;
            this.textBox_scanBpmResult.Leave += this.TextBox_scanBpmResult_LeaveOrEndEdit;
            this.textBox_scanBpmResult.KeyDown += this.TextBox_scanBpmResult_KeyDown;
            this.textBox_scanBpmResult.TabStop = false;
            this.textBox_scanBpmResult.ReadOnly = true;
        }

        private void TextBox_scanBpmResult_LeaveOrEndEdit(object? sender, EventArgs e)
        {
            this.ApplyBpmEditAndReset();
        }

        private void TextBox_scanBpmResult_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                this.ApplyBpmEditAndReset();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                this.ResetBpmEdit();
            }
        }

        private void ApplyBpmEditAndReset()
        {
            if (LastSelectedTrackView == null)
            {
                return;
            }

            string input = this.textBox_scanBpmResult.Text.Trim().Replace(',', '.');
            if (float.TryParse(input, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float bpm) && bpm > 0)
            {
                LastSelectedTrackView.OriginalAudio.ScannedBpm = bpm;
                this.textBox_scanBpmResult.Text = bpm.ToString("0.###") + " BPM";
            }
            else
            {
                float oldBpm = LastSelectedTrackView.OriginalAudio.ScannedBpm;
                this.textBox_scanBpmResult.Text = oldBpm > 0 ? oldBpm.ToString("0.###") + " BPM" : "";
            }

            this.textBox_scanBpmResult.ReadOnly = true;
            this.textBox_scanBpmResult.Leave -= this.TextBox_scanBpmResult_LeaveOrEndEdit;
            this.textBox_scanBpmResult.KeyDown -= this.TextBox_scanBpmResult_KeyDown;
        }

        private void ResetBpmEdit()
        {
            if (LastSelectedTrackView == null)
            {
                return;
            }

            float oldBpm = LastSelectedTrackView.OriginalAudio.ScannedBpm;
            this.textBox_scanBpmResult.Text = oldBpm > 0 ? oldBpm.ToString("0.###") + " BPM" : "";
            this.textBox_scanBpmResult.ReadOnly = true;
            this.textBox_scanBpmResult.Leave -= this.TextBox_scanBpmResult_LeaveOrEndEdit;
            this.textBox_scanBpmResult.KeyDown -= this.TextBox_scanBpmResult_KeyDown;
        }

        private void button_timeStretch_Click(object sender, EventArgs e)
        {
            var selectedAudios = CollectionViews
                .Where(cv => cv != null && !cv.IsDisposed)
                .SelectMany(cv => cv.SelectedAudios)
                .ToList();

            if (LastSelectedTrackView != null && !LastSelectedTrackView.IsDisposed)
            {
                using var dlg = new Modules.Dialogs.TimeStretchDialog(LastSelectedTrackView);
                dlg.ShowDialog(this);
                UpdateTrackDependentUI();
                return;
            }

            MessageBox.Show(this, "No track selected.", "Time Stretch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private async void button_record_Click(object sender, EventArgs e)
        {
            try
            {
                if (!AudioRecorder.IsRecording)
                {
                    this.recordingTimer = new System.Windows.Forms.Timer { Interval = 500 };
                    this.recordingTimer.Tick += async (s, ev) => await this.RecordingTimer_TickAsync();
                    this.recordingTimer.Start();

                    string recordDir = this.AudioC.RecordPath;
                    try { Directory.CreateDirectory(recordDir); } catch { }

                    string fileName = "Recording" + DateTime.Now.ToString("_yyyyMMdd_HHmmss") + ".wav";
                    string fullPath = Path.Combine(recordDir, fileName);
                    await AudioRecorder.StartRecording(fullPath);

                    this.button_record.ForeColor = Color.Red;
                    this.label_stopRecordInfo.Visible = false;
                    this._infoCtrlToStopAppeared = DateTime.MinValue;
                    this.button_record.Enabled = true;
                }
                else if (!ModifierKeys.HasFlag(Keys.Control))
                {
                    this.label_stopRecordInfo.Visible = true;
                    this._infoCtrlToStopAppeared = DateTime.Now;
                }
                else
                {
                    this.button_record.Enabled = false;
                    AudioRecorder.StopRecording(normalizeOutput: true);
                    this.label_stopRecordInfo.Visible = true;
                    this._infoCtrlToStopAppeared = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                LogCollection.Log($"Recording button error: {ex.Message}");
                try { this.recordingTimer?.Stop(); this.recordingTimer?.Dispose(); } catch { }
                this.recordingTimer = null;
                this.button_record.ForeColor = Color.Black;
                this.label_stopRecordInfo.Visible = false;
                this.button_record.Enabled = true;
            }
        }

        private async Task RecordingTimer_TickAsync()
        {
            if (this.label_stopRecordInfo.Visible && this._infoCtrlToStopAppeared != DateTime.MinValue)
            {
                TimeSpan elapsedSinceInfoShown = DateTime.Now - this._infoCtrlToStopAppeared;
                if (elapsedSinceInfoShown.TotalSeconds >= 4)
                {
                    this.label_stopRecordInfo.Visible = false;
                    this._infoCtrlToStopAppeared = DateTime.MinValue;
                }
            }

            if (AudioRecorder.IsRecording && AudioRecorder.RecordingTime.HasValue)
            {
                this.textBox_recordingTime.Text = AudioRecorder.RecordingTime.Value.ToString(@"hh\:mm\:ss");
            }
            else
            {
                this.textBox_recordingTime.Text = "";
                this.label_stopRecordInfo.Visible = false;

                if (this.recordingTimer != null)
                {
                    try { this.recordingTimer.Stop(); this.recordingTimer.Dispose(); } catch { }
                    this.recordingTimer = null;
                }

                this.button_record.ForeColor = Color.Black;
                this.button_record.Enabled = true;
            }

            await Task.CompletedTask;
        }

        private sealed class GlobalKeyMessageFilter : IMessageFilter
        {
            public event Action<Keys, bool>? KeyChanged;

            public bool PreFilterMessage(ref Message m)
            {
                const int WM_KEYDOWN = 0x0100;
                const int WM_KEYUP = 0x0101;
                const int WM_SYSKEYDOWN = 0x0104;
                const int WM_SYSKEYUP = 0x0105;

                if (m.Msg == WM_KEYDOWN || m.Msg == WM_SYSKEYDOWN)
                {
                    Keys key = (Keys)((int)m.WParam & 0xFFFF);
                    this.KeyChanged?.Invoke(key, true);
                }
                else if (m.Msg == WM_KEYUP || m.Msg == WM_SYSKEYUP)
                {
                    Keys key = (Keys)((int)m.WParam & 0xFFFF);
                    this.KeyChanged?.Invoke(key, false);
                }

                return false;
            }
        }

        private void GlobalKeyChanged(Keys key, bool isDown)
        {
            if (key == Keys.CapsLock)
            {
                if (!isDown)
                {
                    bool capsOn = Control.IsKeyLocked(Keys.CapsLock);
                    if (capsOn)
                    {
                        this.StartSyncer();
                    }
                    else
                    {
                        this.StopSyncer();
                    }
                }

                return;
            }

            if (key == Keys.ShiftKey || key == Keys.LShiftKey || key == Keys.RShiftKey)
            {
                if (isDown)
                {
                    if (!this._shiftPressed)
                    {
                        this._shiftPressed = true;
                        this.StartPausingSyncer();
                    }
                }
                else if (this._shiftPressed)
                {
                    this._shiftPressed = false;
                    this.StopPausingSyncer();
                }
            }
        }

        private void StartSyncer()
        {
            try { this._syncerCts?.Cancel(); } catch { }
            try { this._syncerCts?.Dispose(); } catch { }
            this._syncer = null;
            this._nudgingActive = false;

            var playingTracks = TrackViews
                .Where(tv => tv != null && !tv.IsDisposed && tv.OriginalAudio.PlayerPlaying)
                .Select(tv => tv.OriginalAudio)
                .ToList();

            if (playingTracks.Count < 2)
            {
                LogCollection.Log("SYNCER : ON (no-op, need >=2 playing tracks)");
                return;
            }

            this._syncerCts = new CancellationTokenSource();
            this._syncer = new NudgingPlaybackSyncer(playingTracks, this._syncerCts.Token, checkInterval: 0.1, maxNudgeFactor: 0.05);
            this._nudgingActive = true;
            LogCollection.Log($"SYNCER : ON ({playingTracks.Count} tracks)");
        }

        private void StopSyncer()
        {
            try { this._syncerCts?.Cancel(); } catch { }
            try { this._syncerCts?.Dispose(); } catch { }
            this._syncerCts = null;
            this._syncer = null;
            if (this._nudgingActive)
            {
                LogCollection.Log("SYNCER : OFF");
            }
            this._nudgingActive = false;
        }

        private void StartPausingSyncer()
        {
            try { this._pausingCts?.Cancel(); } catch { }
            try { this._pausingCts?.Dispose(); } catch { }
            this._pausingSyncer = null;
            this._pausingActive = false;

            var playingTracks = TrackViews
                .Where(tv => tv != null && !tv.IsDisposed && tv.OriginalAudio.PlayerPlaying)
                .Select(tv => tv.OriginalAudio)
                .ToList();

            if (playingTracks.Count < 2)
            {
                LogCollection.Log("SYNCER (pause) : ON (no-op, need >=2 playing tracks)");
                return;
            }

            this._pausingCts = new CancellationTokenSource();
            this._pausingSyncer = new PausingPlaybackSyncer(playingTracks, this._pausingCts.Token, frequency: 0.1, grain: 10);
            this._pausingActive = true;
            LogCollection.Log($"SYNCER (pause) : ON ({playingTracks.Count} tracks)");
        }

        private void StopPausingSyncer()
        {
            try { this._pausingCts?.Cancel(); } catch { }
            try { this._pausingCts?.Dispose(); } catch { }
            this._pausingCts = null;
            this._pausingSyncer = null;
            if (this._pausingActive)
            {
                LogCollection.Log("SYNCER (pause) : OFF");
            }
            this._pausingActive = false;
        }
    }
}
