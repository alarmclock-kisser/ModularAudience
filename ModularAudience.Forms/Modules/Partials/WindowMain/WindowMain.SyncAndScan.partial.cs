using ModularAudience.Audio.Processors_V1;
using ModularAudience.Audio.Processors_V2;
using ModularAudience.Audio;
using ModularAudience.Audio.Processors_V3;
using ModularAudience.Forms.Helpers;
using ModularAudience.Forms.Hotkeys;
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
                LogManager.Log("No audio selected for BPM scanning.");
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
                LogManager.Log("No audio selected for BPM scanning.");
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
                LogManager.Log("No audio selected for BPM scanning.");
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
                LogManager.Log($"Recording button error: {ex.Message}");
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
                LogManager.Log($"Recording pre-roll error: {ex.Message}");
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
                if (Hotkeys.HotkeyManager.Instance?.IsCapturing == true)
                {
                    return false;
                }

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
            HotkeyManager? manager = HotkeyManager.Instance;
            HotkeyCombo pausingHotkey = manager?.PausingSyncerHotkey ?? new HotkeyCombo { Key = Keys.ShiftKey, Modifiers = HotkeyModifiers.None };
            HotkeyCombo nudgingHotkey = manager?.NudgingSyncerHotkey ?? new HotkeyCombo { Key = Keys.CapsLock, Modifiers = HotkeyModifiers.None };

            // The nudging syncer is a toggle: pressing the hotkey switches it on, pressing it
            // again switches it off. The pausing syncer is a hold: it stays active for as long
            // as the hotkey is held down.
            //
            // On key up only the key itself is compared, because the modifiers may already have
            // been released. Otherwise a hold would never be terminated.
            bool isNudgingKey = isDown
                ? MatchesHotkey(key, nudgingHotkey)
                : MatchesHotkeyKey(key, nudgingHotkey);
            bool isPausingKey = isDown
                ? MatchesHotkey(key, pausingHotkey)
                : MatchesHotkeyKey(key, pausingHotkey);

            if (isNudgingKey)
            {
                this.HandleNudgingSyncerHotkey(key, isDown);
            }
            else if (isPausingKey)
            {
                this.HandlePausingSyncerHotkey(key, isDown, pausingHotkey);
            }

            // Alt + C => prompt for a log comment. Both left and right Alt should work.
            // If Right-Alt was held, pause playback (using pausing syncer) while the dialog is shown.
            if (!isPausingKey && key == Keys.C && isDown)
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
                            timePrefix = "[" + timestamp.ToString(LogManager.Logger.Settings.LogTimestampFormat) + "]";
                        }
                        catch
                        {
                            timePrefix = string.Empty;
                        }
                        string prompt = $"Enter comment to add to log at {timePrefix}";
                        // Provide history from LogManager.Logs (newest first)
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
                                        LogManager.AddComment(timestamp, null, input);
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
                LogManager.Log($"CapsLock reset during shutdown failed: {ex.Message}");
            }
        }

        /// <summary>
        /// CapsLock is only reset when it is actually the hotkey of the nudging syncer. Otherwise
        /// the caps lock state of the user would be changed behind their back.
        /// </summary>
        private void ResetCapsLockStateIfNudgingHotkey()
        {
            HotkeyCombo nudgingHotkey = HotkeyManager.Instance?.NudgingSyncerHotkey
                ?? new HotkeyCombo { Key = Keys.CapsLock, Modifiers = HotkeyModifiers.None };

            if (HotkeyFormatter.NormalizeKey(nudgingHotkey.Key) == Keys.CapsLock)
            {
                ResetCapsLockState();
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
                LogManager.Log("SYNCER : ON (no-op, need >=2 playing tracks)");
                this.ResetCapsLockStateIfNudgingHotkey();
                return;
            }

            LogManager.Log($"SYNCER : tracks => {string.Join(" | ", playingTracks.Select(a => $"{a.Name}<{a.Id.ToString("N")[..6]}>"))}");

            this._syncerCts = new CancellationTokenSource();
            this._syncer = new NudgingPlaybackSyncer(playingTracks, this._syncerCts.Token, checkInterval: 0.1, maxNudgeFactor: 0.05);
            this._nudgingActive = true;
            LogManager.Log($"SYNCER : ON ({playingTracks.Count} tracks)");
        }

        private void StopSyncer()
        {
            try { this._syncerCts?.Cancel(); } catch { }
            try { this._syncerCts?.Dispose(); } catch { }
            this._syncerCts = null;
            this._syncer = null;
            if (this._nudgingActive)
            {
                LogManager.Log("SYNCER : OFF");
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
                LogManager.Log("SYNCER (pause) : ON (no-op, need >=2 playing tracks)");
                return;
            }

            LogManager.Log($"SYNCER (pause) : tracks => {string.Join(" | ", playingTracks.Select(a => $"{a.Name}<{a.Id.ToString("N")[..6]}>"))}");

            this._pausingCts = new CancellationTokenSource();
            this._pausingSyncer = new PausingPlaybackSyncer(playingTracks, this._pausingCts.Token, frequency: 0.1, grain: 10);
            this._pausingActive = true;
            LogManager.Log($"SYNCER (pause) : ON ({playingTracks.Count} tracks)");
        }

        private void StopPausingSyncer()
        {
            try { this._pausingCts?.Cancel(); } catch { }
            try { this._pausingCts?.Dispose(); } catch { }
            this._pausingCts = null;
            this._pausingSyncer = null;
            if (this._pausingActive)
            {
                LogManager.Log("SYNCER (pause) : OFF");
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

        private static bool MatchesHotkey(Keys key, HotkeyCombo combo)
        {
            if (combo.IsEmpty)
            {
                return false;
            }

            if (!MatchesHotkeyKey(key, combo))
            {
                return false;
            }

            HotkeyModifiers currentModifiers = HotkeyActionResolver.GetCurrentModifiers();

            // A hotkey that consists of a modifier key (e.g. "Shift") is necessarily held down
            // while the key is pressed, so that modifier must not be part of the comparison.
            currentModifiers &= ~HotkeyFormatter.GetInherentModifier(combo.Key);

            return currentModifiers == combo.Modifiers;
        }

        private static bool MatchesHotkeyKey(Keys key, HotkeyCombo combo)
        {
            if (combo.IsEmpty)
            {
                return false;
            }

            return HotkeyFormatter.NormalizeKey(key) == HotkeyFormatter.NormalizeKey(combo.Key);
        }

        /// <summary>
        /// The nudging syncer is a toggle: pressing the hotkey switches it on, pressing it
        /// again switches it off. The default hotkey is CapsLock, which is a lock key, so the
        /// lock state decides whether the syncer ends up running. Every other key toggles
        /// directly on the key down.
        /// </summary>
        private void HandleNudgingSyncerHotkey(Keys key, bool isDown)
        {
            if (HotkeyFormatter.NormalizeKey(key) == Keys.CapsLock)
            {
                // The lock state is only final once the key is released.
                if (isDown)
                {
                    return;
                }

                if (IsKeyLocked(Keys.CapsLock))
                {
                    this.StartSyncer();
                }
                else
                {
                    this.StopSyncer();
                }

                return;
            }

            if (!isDown)
            {
                return;
            }

            if (this._nudgingActive)
            {
                this.StopSyncer();
            }
            else
            {
                this.StartSyncer();
            }
        }

        /// <summary>
        /// The pausing syncer is a hold: it is started when the hotkey is pressed and stopped
        /// as soon as the hotkey is released again.
        /// </summary>
        private void HandlePausingSyncerHotkey(Keys key, bool isDown, HotkeyCombo combo)
        {
            if (!isDown)
            {
                // The release must always be processed, otherwise the syncer would stay
                // active when the hotkey is released while the comment dialog is open.
                if (this._pausingHotkeyPressed)
                {
                    this._pausingHotkeyPressed = false;
                    this.StopPausingSyncer();
                }

                return;
            }

            // Do not react while the comment dialog is open to avoid accidental pausing
            if (Instance != null && Instance.IsCommentDialogOpen)
            {
                return;
            }

            // Shift+click on the loop control must not start the pausing syncer.
            bool usesShift = HotkeyFormatter.GetInherentModifier(combo.Key) == HotkeyModifiers.Shift;
            if (usesShift && IsMouseOverLoopControl)
            {
                this.SuppressPausingSyncerForLoopControl();
                return;
            }

            if (!this._pausingHotkeyPressed)
            {
                this._pausingHotkeyPressed = true;
                this.StartPausingSyncer();
            }
        }
    }
}


