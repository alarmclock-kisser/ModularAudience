using ModularAudience.Audio.Processors_V1;
using ModularAudience.Audio.Processors_V2;
using ModularAudience.Audio;
using ModularAudience.Audio.Processors_V3;
using ModularAudience.Forms.Helpers;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ModularAudience.Forms
{
    public partial class WindowMain
    {
        private async void button_scanBpm_Click(object sender, EventArgs e)
        {
            AudioObj? audio = this.GetSelectedAudioForCommands();

            if (audio == null)
            {
                LogCollection.Log("No audio selected for BPM scanning.");
                return;
            }

            double scannedBpm = await BeatScanner.ScanBpmAsync(audio);
            this.textBox_scanBpmResult.Text = scannedBpm.ToString("F3") + " BPM";
            audio.ScannedBpm = (float)scannedBpm;
        }

        private async void button_scanTiming_Click(object sender, EventArgs e)
        {
            AudioObj? audio = this.GetSelectedAudioForCommands();

            if (audio == null)
            {
                LogCollection.Log("No audio selected for BPM scanning.");
                return;
            }

            float scannedTiming = await BeatScanner_V2.ScanTimingAsync(audio);
            this.textBox_scanTimingResult.Text = WindowMainFormatHelpers.GetTimingString(scannedTiming);
            audio.ScannedTiming = scannedTiming;
        }

        private async void button_scanKey_Click(object sender, EventArgs e)
        {
            AudioObj? audio = this.GetSelectedAudioForCommands();

            if (audio == null)
            {
                LogCollection.Log("No audio selected for BPM scanning.");
                return;
            }

            string scannedKey = await BeatScanner_V2.ScanKeyAsync(audio);
            this.textBox_scanKeyResult.Text = scannedKey;
            audio.ScannedKey = scannedKey;
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
                var dlg = new Modules.Dialogs.TimeStretchDialog(LastSelectedTrackView);
                dlg.FormClosed += (_, _) =>
                {
                    this.UpdateTrackDependentUI();
                    this.RefreshAllCollectionViews();
                };
                dlg.Show(this);
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
                    await this.BeginRecordingAsync(TimeSpan.Zero);
                }
                else if (!ModifierKeys.HasFlag(Keys.Control))
                {
                    this.label_stopRecordInfo.Visible = true;
                    this._infoCtrlToStopAppeared = DateTime.Now;
                }
                else
                {
                    this.button_record.Enabled = false;
                    await AudioRecorder.StopRecordingAsync(normalizeOutput: true);
                    // Finalise the playlist track-log for this recording
                    this.FinaliseTrackLog();
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

        private async Task BeginRecordingAsync(TimeSpan preRoll)
        {
            this.recordingTimer = new System.Windows.Forms.Timer { Interval = 500 };
            this.recordingTimer.Tick += async (s, ev) => await this.RecordingTimer_TickAsync();
            this.recordingTimer.Start();

            string recordDir = this.AudioC.RecordPath;
            try { Directory.CreateDirectory(recordDir); } catch { }

            string fileName = "Recording" + DateTime.Now.ToString("_yyyyMMdd_HHmmss") + ".wav";
            string fullPath = Path.Combine(recordDir, fileName);
            await AudioRecorder.StartRecording(fullPath, preRoll: preRoll);
            if (!AudioRecorder.IsRecording)
            {
                throw new InvalidOperationException("The recording capture could not be started.");
            }

            this.StartTrackLog(fullPath);
            this.button_record.ForeColor = Color.Red;
            this.label_stopRecordInfo.Visible = false;
            this._infoCtrlToStopAppeared = DateTime.MinValue;
            this.button_record.Enabled = true;
        }

        private async void recordPreRollMenuItem_Click(object? sender, EventArgs e)
        {
            if (AudioRecorder.IsRecording || sender is not ToolStripMenuItem { Tag: int minutes })
            {
                return;
            }

            try
            {
                await this.BeginRecordingAsync(TimeSpan.FromMinutes(minutes));
            }
            catch (Exception ex)
            {
                LogCollection.Log($"Recording pre-roll error: {ex.Message}");
                try { this.recordingTimer?.Stop(); this.recordingTimer?.Dispose(); } catch { }
                this.recordingTimer = null;
                this.button_record.ForeColor = Color.Black;
                this.button_record.Enabled = true;
            }
        }

        private void contextMenuStrip_record_Opening(object? sender, CancelEventArgs e)
        {
            e.Cancel = AudioRecorder.IsRecording;
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

                // Finalise the playlist track-log for this recording
                this.FinaliseTrackLog();

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
                    bool capsOn = IsKeyLocked(Keys.CapsLock);
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
                // Do not react to Shift while comment dialog is open to avoid accidental pausing
                if (Instance != null && Instance.IsCommentDialogOpen)
                {
                    return;
                }

                if (isDown && IsMouseOverLoopControl)
                {
                    this.SuppressPausingSyncerForLoopControl();
                    return;
                }

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

            // Alt + C => prompt for a log comment. Both left and right Alt should work.
            // If Right-Alt was held, pause playback (using pausing syncer) while the dialog is shown.
            if (key == Keys.C && isDown)
            {
                if ((ModifierKeys & Keys.Alt) == Keys.Alt)
                {
                    DateTime timestamp = DateTime.Now;

                    bool rightAltDown = IsKeyDown(VK_RMENU);
                    bool startedPausingForComment = false;
                    bool wasPausingBefore = this._pausingActive;

                    if (rightAltDown)
                    {
                        // Start pausing syncer if not already active. Remember if we actually started it so we only stop our own start.
                        this.StartPausingSyncer();
                        if (!wasPausingBefore && this._pausingActive)
                        {
                            startedPausingForComment = true;
                        }
                    }

                    try
                    {
                        string timePrefix;
                        try
                        {
                            timePrefix = "[" + timestamp.ToString(LogCollection.TimeFormat) + "]";
                        }
                        catch
                        {
                            timePrefix = string.Empty;
                        }
                        string prompt = $"Enter comment to add to log at {timePrefix}";
                        // Provide history from LogCollection.Logs (newest first)
                        // Provide only user comment history (newest first)
                        List<string> history;
                        try { history = CommentHistory.ToList(); } catch { history = []; }

                        // Provide newest-first ordering
                        history = history.ToList();

                        // supply draft
                        string draft = CommentDraft ?? string.Empty;

                        Instance?.IsCommentDialogOpen = true;
                        try
                        {
                            using (var dlg = new Modules.Dialogs.CommentInputDialog(history, prompt, draft))
                            {
                                var dr = dlg.ShowDialog(Instance ?? this);
                                if (dr == DialogResult.OK)
                                {
                                    string input = dlg.ResultText?.Trim() ?? string.Empty;
                                    if (!string.IsNullOrWhiteSpace(input))
                                    {
                                        LogCollection.PostComment(timestamp, input);
                                        try { CommentHistory.Insert(0, input); } catch { }
                                        try { CommentDraft = string.Empty; } catch { }
                                    }
                                }
                                else
                                {
                                    // Save draft back
                                    try { CommentDraft = dlg.ResultText ?? string.Empty; } catch { }
                                }
                            }
                        }
                        finally
                        {
                            Instance?.IsCommentDialogOpen = false;
                        }
                    }
                    finally
                    {
                        if (startedPausingForComment)
                        {
                            this.StopPausingSyncer();
                        }
                    }
                }

                return;
            }
        }

        // P/Invoke to detect right-alt state (we need to distinguish left vs right Alt)
        private const int VK_RMENU = 0xA5;
        private const byte VK_CAPITAL = 0x14;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

        private static bool IsKeyDown(int vKey)
        {
            try
            {
                return (GetAsyncKeyState(vKey) & 0x8000) != 0;
            }
            catch
            {
                return false;
            }
        }

        private static void ResetCapsLockState()
        {
            try
            {
                if (IsKeyLocked(Keys.CapsLock))
                {
                    keybd_event(VK_CAPITAL, 0x45, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
                    keybd_event(VK_CAPITAL, 0x45, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
                }
            }
            catch (Exception ex)
            {
                LogCollection.Log($"CapsLock reset during shutdown failed: {ex.Message}");
            }
        }

        private void StartSyncer()
        {
            try { this._syncerCts?.Cancel(); } catch { }
            try { this._syncerCts?.Dispose(); } catch { }
            this._syncer = null;
            this._nudgingActive = false;

            var playingTracks = this.CollectActiveSyncTracks(includePaused: false);

            if (playingTracks.Count < 2)
            {
                LogCollection.Log("SYNCER : ON (no-op, need >=2 playing tracks)");
                ResetCapsLockState();
                return;
            }

            LogCollection.Log($"SYNCER : tracks => {string.Join(" | ", playingTracks.Select(a => $"{a.Name}<{a.Id.ToString("N")[..6]}>"))}");

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

        internal void RefreshActiveSyncerTracks()
        {
            if (!this._nudgingActive || this._syncer == null)
            {
                return;
            }

            this._syncer.UpdateTracks(this.CollectActiveSyncTracks(includePaused: false));
        }

        private void StartPausingSyncer()
        {
            try { this._pausingCts?.Cancel(); } catch { }
            try { this._pausingCts?.Dispose(); } catch { }
            this._pausingSyncer = null;
            this._pausingActive = false;

            var playingTracks = this.CollectActiveSyncTracks(includePaused: true);

            if (playingTracks.Count < 2)
            {
                LogCollection.Log("SYNCER (pause) : ON (no-op, need >=2 playing tracks)");
                return;
            }

            LogCollection.Log($"SYNCER (pause) : tracks => {string.Join(" | ", playingTracks.Select(a => $"{a.Name}<{a.Id.ToString("N")[..6]}>"))}");

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

        private List<AudioObj> CollectActiveSyncTracks(bool includePaused)
        {
            IEnumerable<AudioObj> trackViewAudios = TrackViews
                .Where(tv => tv != null && !tv.IsDisposed)
                .Select(tv => tv.OriginalAudio);

            IEnumerable<AudioObj> playlistAudios = this.GetActivePlaylistAudios();

            return trackViewAudios
                .Concat(playlistAudios)
                .Where(audio => audio != null && (audio.PlayerPlaying || (includePaused && audio.Paused)))
                .DistinctBy(audio => audio.Id)
                .ToList();
        }
    }
}
