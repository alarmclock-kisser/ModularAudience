using ModularAudience.Audio;
using ModularAudience.Audio.Processing;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using static ModularAudience.Audio.Processing.DropManager;

namespace ModularAudience.Forms.Modules
{
    public partial class LoopControl : Form
    {

        // Expose whether a playlist item is currently selected and return its path.
        public bool HasSelectedPlaylistItem()
        {
            try
            {
                int idx = this.checkedListBox_playlistTracks.SelectedIndex;
                return idx >= 0 && idx < this.checkedListBox_playlistTracks.Items.Count;
            }
            catch { return false; }
        }

        public string? GetSelectedPlaylistPath()
        {
            try
            {
                int idx = this.checkedListBox_playlistTracks.SelectedIndex;
                if (idx < 0 || idx >= this.checkedListBox_playlistTracks.Items.Count)
                {
                    return null;
                }

                if (this.checkedListBox_playlistTracks.Items[idx] is PlaylistTargetItem pti)
                {
                    return pti.Audio?.FilePath;
                }
            }
            catch { }
            return null;
        }
        private sealed class PlaylistTargetItem
        {
            public required AudioObj Audio { get; init; }
            public required string DisplayText { get; set; }

            public override string ToString() => this.DisplayText;
        }

        // Relative rate drag state: tracks cumulative log-rate position per audio
        private void checkedListBox_playlistTracks_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                WindowMain.Instance?.SuppressPausingSyncerForLoopControl();
            }

            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            this.resetPlaylistRateForAllTracks = false;
            try
            {
                int idx = this.checkedListBox_playlistTracks.IndexFromPoint(e.Location);
                if (idx >= 0 && idx < this.checkedListBox_playlistTracks.Items.Count)
                {
                    this.checkedListBox_playlistTracks.SelectedIndex = idx;
                    this.resetPlaylistRateForAllTracks = ModifierKeys.HasFlag(Keys.Control);
                    // ensure the right-clicked item is selected (not only focused)
                }
            }
            catch { }
        }

        private void contextMenuStrip_playlistItem_Opening(object? sender, CancelEventArgs e)
        {
            bool resetAll = this.resetPlaylistRateForAllTracks || ModifierKeys.HasFlag(Keys.Control);
            this.resetPlaylistRateForAllTracks = false;
            if (resetAll)
            {
                e.Cancel = true;
                int selectedIndex = this.checkedListBox_playlistTracks.SelectedIndex;
                if (selectedIndex >= 0 && selectedIndex < this.checkedListBox_playlistTracks.Items.Count)
                {
                    _ = this.ApplyPlaylistRateAsync(selectedIndex, 0, reset: true, resetAll: true);
                }
                return;
            }

            // Only allow opening when an item is under mouse / selected
            try
            {
                int idx = this.checkedListBox_playlistTracks.SelectedIndex;
                e.Cancel = idx < 0 || idx >= this.checkedListBox_playlistTracks.Items.Count;
            }
            catch { e.Cancel = true; }
        }

        private void toolStripMenuItem_removeFromEnsemble_Click(object? sender, EventArgs e)
        {
            try
            {
                int idx = this.checkedListBox_playlistTracks.SelectedIndex;
                if (idx < 0 || idx >= this.checkedListBox_playlistTracks.Items.Count)
                {
                    return;
                }

                if (this.checkedListBox_playlistTracks.Items[idx] is PlaylistTargetItem pti)
                {
                    var audio = pti.Audio;

                    // Try via WindowMain bridge first
                    bool removed = false;
                    try
                    {
                        if (WindowMain.Instance != null)
                        {
                            removed = WindowMain.Instance.TryRemoveActivePlaylistAudioById(audio.Id);
                        }
                    }
                    catch { removed = false; }

                    if (!removed)
                    {
                        // Fallback: stop and dispose audio directly if possible
                        _ = Task.Run(() =>
                        {
                            try { audio.StopAsync().GetAwaiter().GetResult(); } catch { }
                            try { audio.Dispose(); } catch { }
                        });
                    }

                    // Remove from UI immediately
                    this.checkedListBox_playlistTracks.Items.RemoveAt(idx);
                    this.selectedPlaylistTrackIds.Remove(audio.Id);
                    this.knownPlaylistTrackIds.Remove(audio.Id);
                    this.UpdateLoopButtonsState();
                }
            }
            catch { }
        }

        private AudioCollectionView? CollectionView = null;
        private readonly HashSet<Guid> selectedPlaylistTrackIds = [];
        private readonly HashSet<Guid> knownPlaylistTrackIds = [];
        private readonly System.Windows.Forms.Timer playlistTargetsTimer = new() { Interval = 250 };
        private bool resetPlaylistRateForAllTracks;
        private bool suppressPlaylistChecklistEvents;

        private float Bpm => this.OriginalAudio is AudioObj audio ? GetAudioBpm(audio) : 120f;
        private double Multiplier
        {
            get
            {
                if (this.domainUpDown_multiplier.SelectedItem is string selectedItem &&
                    TryParseMultiplier(selectedItem, out double val))
                {
                    return val;
                }
                return 1.0;
            }
        }

        // Parses the multiplier label, which may be a plain number ("2", "0.5")
        // or a fraction ("1/2", "1/4", "1/8"). double.TryParse rejects the
        // fraction form, so handle it explicitly.
        private static bool TryParseMultiplier(string item, out double value)
        {
            value = 0.0;
            string trimmed = item.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return false;
            }

            int slash = trimmed.IndexOf('/');
            if (slash >= 0)
            {
                string left = trimmed[..slash].Trim();
                string right = trimmed[(slash + 1)..].Trim();
                if (double.TryParse(left, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double numerator) &&
                    double.TryParse(right, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double denominator) &&
                    denominator != 0.0)
                {
                    value = numerator / denominator;
                    return true;
                }
                return false;
            }

            return double.TryParse(trimmed, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value);
        }
        private double JumpMs => (double)this.numericUpDown_jump.Value;

        private void button_playlistAllOn_Click(object? sender, EventArgs e)
        {
            this.SetAllPlaylistTrackChecks(true);
        }

        private void button_playlistAllOff_Click(object? sender, EventArgs e)
        {
            this.SetAllPlaylistTrackChecks(false);
        }

        private void SetAllPlaylistTrackChecks(bool isChecked)
        {
            this.suppressPlaylistChecklistEvents = true;
            try
            {
                for (int i = 0; i < this.checkedListBox_playlistTracks.Items.Count; i++)
                {
                    this.checkedListBox_playlistTracks.SetItemChecked(i, isChecked);
                }
            }
            finally
            {
                this.suppressPlaylistChecklistEvents = false;
            }

            this.SyncSelectedPlaylistTrackIdsFromUi();
            this.UpdateLoopButtonsState();
        }


        private readonly HashSet<Control> autoRefocusAttached = [];
        private readonly HashSet<Control> containerMonitored = [];

        private bool lastActionWasMultiplierChange = false;
        private bool suppressMultiplierEvents;
        private bool suppressJumpEvents;
        private CancellationTokenSource? pendingShiftLoopCts;
        private double lastJumpMs = 1;
        private double lastJumpValue = 1;
        private Guid _lastJumpAudioId = Guid.Empty;



        public LoopControl()
        {
            this.InitializeComponent();

            this.Fill_ComboBox_Drops();

            this.StartPosition = FormStartPosition.Manual;
            this.Location = WindowsScreenHelper.GetCenterStartingPoint(this, WindowMain.CurrentScreenId);
            this.TopMost = true;

            this.BuildLoopControlButtons();
            this.EnableAutoRefocusForContainer(this);
            this.playlistTargetsTimer.Tick += (_, _) => this.RefreshPlaylistTargets();
            this.playlistTargetsTimer.Start();

            this.numericUpDown_jump.Click += this.numericUpDown_jump_Click;
            this.domainUpDown_multiplier.Click += this.domainUpDown_multiplier_Click;

            this.RefreshPlaylistTargets();

            this.UpdateLoopButtonsState();

        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
            }
            base.OnFormClosing(e);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (this.Visible)
            {
                this.RefreshPlaylistTargets();
                this.playlistTargetsTimer.Start();
            }
            else
            {
                this.playlistTargetsTimer.Stop();
            }
        }

        private void checkedListBox_playlistTracks_ItemCheck(object? sender, ItemCheckEventArgs e)
        {
            if (this.suppressPlaylistChecklistEvents ||
                this.checkedListBox_playlistTracks.Items[e.Index] is not PlaylistTargetItem item)
            {
                return;
            }

            if (e.NewValue == CheckState.Checked)
            {
                this.selectedPlaylistTrackIds.Add(item.Audio.Id);
            }
            else
            {
                this.selectedPlaylistTrackIds.Remove(item.Audio.Id);
            }
            this.UpdateTargetLabel();
        }

        private void SyncSelectedPlaylistTrackIdsFromUi()
        {
            this.selectedPlaylistTrackIds.Clear();
            foreach (PlaylistTargetItem item in this.checkedListBox_playlistTracks.CheckedItems.OfType<PlaylistTargetItem>())
            {
                this.selectedPlaylistTrackIds.Add(item.Audio.Id);
            }
        }

        private static string BuildPlaylistDisplayText(AudioObj audio)
        {
            string rawName = !string.IsNullOrWhiteSpace(audio.OriginalName)
                ? audio.OriginalName
                : !string.IsNullOrWhiteSpace(audio.Name)
                    ? audio.Name
                    : Path.GetFileNameWithoutExtension(audio.FilePath) ?? "Unknown";

            // If name contains a double-underscore separator used for generated suffixes, keep only the first part.
            string name = rawName;
            if (name.Contains("__"))
            {
                var parts = name.Split(["__"], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    name = parts[0].Trim();
                }
            }
            // Also strip common generated marker like _stretched_ inside the filename if present
            if (name.IndexOf("_stretched_", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                name = name.Split(["_stretched_"], StringSplitOptions.RemoveEmptyEntries)[0].Trim();
            }
            string state = audio.PlayerPlaying ? "▶" : audio.Paused ? "||" : "■";
            string shortId = audio.Id.ToString("N")[..6];
            if (name.Length > 45)
            {
                name = name[..45] + "...";
            }
            return $"{state} {name} · {BuildPlaylistRateText(audio)} {shortId}";
        }

        private void RefreshPlaylistTargets()
        {
            if (this.checkedListBox_playlistTracks.IsInteracting || this.contextMenuStrip_playlistItem.Visible)
            {
                return;
            }
            Guid? selectedAudioId = SelectedTrackView?.OriginalAudio.Id
                ?? (this.checkedListBox_playlistTracks.SelectedItem as PlaylistTargetItem)?.Audio.Id;
            int previousIndex = this.checkedListBox_playlistTracks.SelectedIndex;
            List<AudioObj> activePlaylistAudios = this.GetActiveTargetAudios();
            HashSet<Guid> activeIds = activePlaylistAudios.Select(audio => audio.Id).ToHashSet();

            foreach (AudioObj audio in activePlaylistAudios.Where(audio => !this.knownPlaylistTrackIds.Contains(audio.Id)))
            {
                this.selectedPlaylistTrackIds.Add(audio.Id);
            }

            this.knownPlaylistTrackIds.Clear();
            this.knownPlaylistTrackIds.UnionWith(activeIds);
            this.selectedPlaylistTrackIds.RemoveWhere(id => !activeIds.Contains(id));
            HashSet<Guid> retainedLoopIds = activeIds.Concat(WindowMain.TrackViews
                .Where(view => !view.IsDisposed && !view.Disposing).Select(view => view.OriginalAudio.Id)).ToHashSet();
            foreach (Guid id in this.loopTargetStates.Keys.Where(id => !retainedLoopIds.Contains(id)).ToArray())
            {
                this.loopTargetStates.Remove(id);
            }

            this.suppressPlaylistChecklistEvents = true;
            try
            {
                var listBox = this.checkedListBox_playlistTracks;

                // Build incremental diff against current items to avoid Items.Clear() flicker on every tick.
                // 1) Remove items whose audio is no longer active, in reverse order to keep indices stable.
                for (int i = listBox.Items.Count - 1; i >= 0; i--)
                {
                    if (listBox.Items[i] is PlaylistTargetItem item && !activeIds.Contains(item.Audio.Id))
                    {
                        listBox.Items.RemoveAt(i);
                    }
                }

                // Preserve existing row order; append arrivals and update text without replacing items.
                for (int targetIndex = 0; targetIndex < activePlaylistAudios.Count; targetIndex++)
                {
                    AudioObj audio = activePlaylistAudios[targetIndex];
                    string display = BuildPlaylistDisplayText(audio);
                    bool shouldBeChecked = this.selectedPlaylistTrackIds.Contains(audio.Id);

                    int currentIndex = -1;
                    for (int j = 0; j < listBox.Items.Count; j++)
                    {
                        if (listBox.Items[j] is PlaylistTargetItem pti && pti.Audio.Id == audio.Id)
                        {
                            currentIndex = j;
                            break;
                        }
                    }

                    if (currentIndex < 0)
                    {
                        var newItem = new PlaylistTargetItem { Audio = audio, DisplayText = display };
                        int addedIdx = listBox.Items.Add(newItem);
                        listBox.SetItemChecked(addedIdx, shouldBeChecked);
                        continue;
                    }

                    var existingItem = (PlaylistTargetItem)listBox.Items[currentIndex];
                    this.RefreshPlaylistRowText(currentIndex, existingItem);
                    if (listBox.GetItemChecked(currentIndex) != shouldBeChecked)
                    {
                        listBox.SetItemChecked(currentIndex, shouldBeChecked);
                    }
                }

                this.RestorePlaylistSelection(selectedAudioId, previousIndex);
            }
            finally
            {
                this.suppressPlaylistChecklistEvents = false;
            }

            this.UpdateTargetLabel();
            bool hasPlaylistEntries = activePlaylistAudios.Count > 0;
            this.button_playlistAllOn.Enabled = hasPlaylistEntries;
            this.button_playlistAllOff.Enabled = hasPlaylistEntries;

            if (this.Visible)
            {
                this.UpdateLoopButtonsState();
            }
        }

        internal void RefreshPlaylistTargetsNow()
        {
            if (this.IsDisposed || this.Disposing)
            {
                return;
            }
            if (this.InvokeRequired)
            {
                this.BeginInvoke((Action)this.RefreshPlaylistTargetsNow);
                return;
            }
            this.RefreshPlaylistTargets();
        }

        internal void RefreshAudioTiming(AudioObj audio)
        {
            if (this.IsDisposed || this.Disposing)
            {
                return;
            }
            if (this.InvokeRequired)
            {
                this.BeginInvoke((Action)(() => this.RefreshAudioTiming(audio)));
                return;
            }

            int rowIndex = this.FindPlaylistTrackIndex(audio.Id);
            if (rowIndex >= 0 && this.checkedListBox_playlistTracks.Items[rowIndex] is PlaylistTargetItem item)
            {
                this.RefreshPlaylistRowText(rowIndex, item);
            }

            if (this.OriginalAudio?.Id == audio.Id)
            {
                this._jumpBaseMsByAudioId.Remove(audio.Id);
                this.UpdateJumpDistanceForRate(audio);
            }
        }



        private void BuildLoopControlButtons()
        {
            var template = this.button_loop;
            if (template == null || this.panel_buttons == null)
            {
                return;
            }

            template.Visible = false;

            string[] buttonLabels = ["16", "8", "4", "2", "1", "/2", "/4", "/8", "/8", "/4", "/2", "1", "2", "4", "8", "16"];
            float[] fractions =
            [
                -8f, -4f, -2f, -1f, -0.5f, -0.25f, -0.125f, -0.0625f,
                 0.0625f, 0.125f, 0.25f, 0.5f, 1f, 2f, 4f, 8f
            ];

            const int buttonCount = 16;

            this.panel_buttons.SuspendLayout();

            var toRemove = this.panel_buttons.Controls.OfType<Button>()
                .Where(b => !ReferenceEquals(b, template))
                .ToList();
            foreach (var b in toRemove)
            {
                this.panel_buttons.Controls.Remove(b);
                b.Dispose();
            }

            var created = new List<Button>(buttonCount);

            for (int i = 0; i < buttonCount; i++)
            {
                var copy = new Button
                {
                    Font = template.Font,
                    Size = template.Size,
                    BackColor = template.BackColor,
                    ForeColor = template.ForeColor,
                    FlatStyle = template.FlatStyle,
                    Image = template.Image,
                    ImageAlign = template.ImageAlign,
                    TextAlign = template.TextAlign,
                    Padding = template.Padding,
                    Margin = template.Margin,
                    UseVisualStyleBackColor = template.UseVisualStyleBackColor
                };
                copy.FlatAppearance.BorderSize = template.FlatAppearance.BorderSize;
                copy.FlatAppearance.MouseDownBackColor = template.FlatAppearance.MouseDownBackColor;
                copy.FlatAppearance.MouseOverBackColor = template.FlatAppearance.MouseOverBackColor;

                copy.Text = buttonLabels[i];
                copy.Tag = fractions[i].ToString(System.Globalization.CultureInfo.InvariantCulture);
                copy.TabStop = false;
                copy.Anchor = AnchorStyles.Top | AnchorStyles.Left;

                copy.Click += this.LoopButton_Click;

                string tooltipText = $"Loop {buttonLabels[i]}.\nShift+click: wait for the next beat-grid point before setting.\nCtrl+click: set loop fraction on all tracks.";
                this.toolTip_playlistTracks.SetToolTip(copy, tooltipText);

                this.panel_buttons.Controls.Add(copy);
                created.Add(copy);
            }

            void LayoutButtons()
            {
                if (this.panel_buttons.ClientSize.Width <= 0 || created.Count == 0)
                {
                    return;
                }

                int panelWidth = this.panel_buttons.ClientSize.Width;
                int panelHeight = this.panel_buttons.ClientSize.Height;

                int spacing = Math.Max(0, template.Margin.Right);
                int totalSpacing = spacing * (created.Count - 1);
                int availableWidth = Math.Max(1, panelWidth - totalSpacing);
                int btnWidth = Math.Max(8, availableWidth / created.Count);

                int btnHeight = template.Height;
                int contentWidth = btnWidth * created.Count + totalSpacing;
                int startX = Math.Max(0, (panelWidth - contentWidth) / 2);
                int y = Math.Max(0, (panelHeight - btnHeight) / 2);

                int x = startX;
                for (int i = 0; i < created.Count; i++)
                {
                    var btn = created[i];
                    btn.SetBounds(x, y, btnWidth, btnHeight);
                    x += btnWidth + spacing;
                }
            }

            LayoutButtons();
            this.panel_buttons.Resize += (s, e) => LayoutButtons();

            this.panel_buttons.ResumeLayout();
        }



        private async void LoopButton_Click(object? sender, EventArgs e)
        {
            if (sender is not Button clickedButton ||
                !float.TryParse(clickedButton.Tag?.ToString(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float fraction))
            {
                return;
            }

            bool shiftPressed = ModifierKeys.HasFlag(Keys.Shift);
            IReadOnlyList<AudioObj> targets = this.GetActionTargets(ModifierKeys.HasFlag(Keys.Control));
            bool turnOff = targets.Count > 0 && targets.All(audio =>
                audio.LoopEnabled && Math.Abs(GetUiLoopFraction(audio) - fraction) < 0.0001f);

            if (shiftPressed && !turnOff)
            {
                await this.ApplyShiftSnappedLoopsAsync(targets, fraction);
            }
            else
            {
                this.CancelPendingShiftLoop();
                this.ToggleTargetLoops(targets, fraction);
            }

            // Focus TrackView but also keep this Form front most
            this.CurrentTrackView?.Focus();
            this.BringToFront();

            // Sync selection back to WindowMain so TrackViews know which is selected
            if (this.CurrentTrackView != null)
            {
                WindowMain.LastSelectedTrackView = this.CurrentTrackView;
            }

            if (this.CurrentTrackView != null)
            {
                int index = this.FindPlaylistTrackIndex(this.CurrentTrackView.OriginalAudio.Id);
                if (index >= 0) this.checkedListBox_playlistTracks.SelectedIndex = index;
            }
        }

        private async Task ApplyShiftSnappedLoopsAsync(IReadOnlyList<AudioObj> targets, float fraction)
        {
            this.CancelPendingShiftLoop();
            if (targets.Count == 0)
            {
                return;
            }

            using CancellationTokenSource cts = new();
            this.pendingShiftLoopCts = cts;
            try
            {
                Task[] pendingLoops = targets
                    .Select(audio => this.ApplyShiftSnappedLoopAsync(audio, fraction, cts.Token))
                    .ToArray();
                await Task.WhenAll(pendingLoops);

                if (!cts.IsCancellationRequested)
                {
                    this.UpdateLoopButtonsState();
                }
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                LogCollection.Log(ex);
            }
            finally
            {
                if (ReferenceEquals(this.pendingShiftLoopCts, cts))
                {
                    this.pendingShiftLoopCts = null;
                }
            }
        }

        private async Task ApplyShiftSnappedLoopAsync(AudioObj audio, float fraction, CancellationToken cancellationToken)
        {
            int channels = Math.Max(1, audio.Channels);
            long currentFrame = Math.Clamp(audio.Position, 0L,
                Math.Max(0L, audio.Length / channels - 1L));
            bool waitForGrid = audio.PlayerPlaying && audio.BeatGrid is { Length: > 0 };
            long? anchorFrame = waitForGrid ? audio.GetNextBeatGridFrame(currentFrame) : currentFrame;

            if (waitForGrid)
            {
                if (!anchorFrame.HasValue)
                {
                    return;
                }

                if (!IsGridFrameReachable(audio, anchorFrame.Value, channels))
                {
                    audio.UpdateLoopFraction(0, 0, 0, false, false);
                }

                anchorFrame = await WaitForNextBeatGridFrameAsync(audio, anchorFrame.Value, cancellationToken);
                if (!anchorFrame.HasValue || cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }

            long anchorSamples = (anchorFrame ?? currentFrame) * channels;
            this.SetLoopRange(audio, fraction, false, anchorSamples);
        }

        private static async Task<long?> WaitForNextBeatGridFrameAsync(
            AudioObj audio,
            long initialAnchorFrame,
            CancellationToken cancellationToken)
        {
            long anchorFrame = initialAnchorFrame;
            long previousFrame = audio.Position;

            while (audio.PlayerPlaying)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long currentFrame = audio.Position;
                if (currentFrame >= anchorFrame)
                {
                    return anchorFrame;
                }

                if (currentFrame < previousFrame)
                {
                    long? wrappedAnchor = audio.GetNextBeatGridFrame(currentFrame);
                    if (!wrappedAnchor.HasValue || !IsGridFrameReachable(audio, wrappedAnchor.Value, Math.Max(1, audio.Channels)))
                    {
                        return null;
                    }

                    anchorFrame = wrappedAnchor.Value;
                }

                previousFrame = currentFrame;
                await Task.Delay(8, cancellationToken);
            }

            return null;
        }

        private static bool IsGridFrameReachable(AudioObj audio, long frame, int channels)
        {
            if (!audio.LoopEnabled || audio.LoopEndSamples <= audio.LoopStartSamples)
            {
                return true;
            }

            long loopEndFrame = audio.LoopEndSamples / Math.Max(1, channels);
            return frame < loopEndFrame;
        }

        private void CancelPendingShiftLoop()
        {
            try { this.pendingShiftLoopCts?.Cancel(); } catch { }
            this.pendingShiftLoopCts = null;
        }

        private void UntoggleAllOtherButtons(Button? sender)
        {
            var buttons = this.panel_buttons.Controls.OfType<Button>().Where(b => b != sender);
            foreach (var button in buttons)
            {
                button.BackColor = SystemColors.Control;
            }
        }

        private void SetLoopRange(AudioObj audio, float fraction, bool hadActiveBefore = false, long? currentSamplesOverride = null)
        {
            LoopTargetState state = this.GetLoopTargetState(audio);

            // If no button is selected after toggle -> disable loop and reset tracking
            if (fraction == 0f)
            {
                audio.UpdateLoopFraction(0, 0, 0, false, true);
                audio.Metrics["loop.ui.fraction"] = 0f;
                audio.Metrics.Remove("loop.ui.multiplier");
                this.loopTargetStates.Remove(audio.Id);
                RefreshTargetWaveform(audio);
                return;
            }
            if (audio.Length < Math.Max(1, audio.Channels))
            {
                return;
            }

            static int SignEps(float x)
            {
                const float eps = 1e-6f;
                return x > eps ? 1 : (x < -eps ? -1 : 0);
            }

            try
            {
                int channels = Math.Max(1, audio.Channels);
                long framesPerBeat = Math.Max(1L, (long)Math.Round((double)audio.SampleRate * 60.0 / GetAudioBpm(audio) * 2.0 * this.Multiplier));
                long totalFrames = Math.Max(0L, audio.Length / channels);
                long totalSamples = totalFrames * channels;

                // Capture current position and previous loop bounds before any change
                long currentSamplesBefore = currentSamplesOverride ?? audio.Position * channels;
                long prevStartSamples = state.StartSamples;
                long prevEndSamples = state.EndSamples;

                bool havePrevLoop = prevStartSamples >= 0 && prevEndSamples > prevStartSamples;

                int signNow = SignEps(fraction);
                int signPrev = SignEps(state.Fraction);
                bool signChanged = hadActiveBefore && havePrevLoop && signNow != 0 && signPrev != 0 && signNow != signPrev;

                bool doRelativeScale =
                    hadActiveBefore &&
                    !this.lastActionWasMultiplierChange &&
                    havePrevLoop &&
                    Math.Abs(state.Fraction) > 1e-9f;

                bool anchoredAbsoluteByMultiplier =
                    hadActiveBefore &&
                    this.lastActionWasMultiplierChange &&
                    havePrevLoop;

                long startFrame;
                long endFrame;
                bool anchorAtStart = fraction >= 0f; // positive -> anchor at start, negative -> anchor at end

                if (doRelativeScale)
                {
                    long prevLenSamples = Math.Max(1L, prevEndSamples - prevStartSamples);
                    double ratio = Math.Abs(fraction) / Math.Max(1e-9, Math.Abs(state.Fraction));
                    long newLenSamples = Math.Max(1L, (long)Math.Round(prevLenSamples * ratio));

                    if (fraction >= 0f)
                    {
                        // Normal: anchor at previous start.
                        // If we switched from negative->positive, anchor at previous END (shared boundary).
                        long anchorStartSamples = Math.Clamp(signChanged ? prevEndSamples : prevStartSamples, 0L, totalSamples - channels);

                        long desiredEndSamples = anchorStartSamples + newLenSamples;
                        long clampedEnd = Math.Clamp(desiredEndSamples, anchorStartSamples + 1L, Math.Max(1L, totalSamples));
                        long clampedStart = Math.Clamp(anchorStartSamples, 0L, Math.Max(0L, clampedEnd - 1));

                        startFrame = clampedStart / channels;
                        endFrame = clampedEnd / channels;
                        anchorAtStart = true;
                    }
                    else
                    {
                        // Normal: anchor at previous end.
                        // If we switched from positive->negative, anchor at previous START (shared boundary).
                        long anchorEndSamples = Math.Clamp(signChanged ? prevStartSamples : prevEndSamples, (long)channels, totalSamples);

                        long desiredStartSamples = anchorEndSamples - newLenSamples;
                        long clampedStart = Math.Clamp(desiredStartSamples, 0L, Math.Max(0L, anchorEndSamples - 1));
                        long clampedEnd = Math.Clamp(anchorEndSamples, clampedStart + 1L, Math.Max(1L, totalSamples));

                        startFrame = clampedStart / channels;
                        endFrame = clampedEnd / channels;
                        anchorAtStart = false;
                    }
                }
                else if (anchoredAbsoluteByMultiplier)
                {
                    long targetLenFrames = Math.Max(1L, (long)Math.Round(Math.Abs(fraction) * framesPerBeat));
                    long targetLenSamples = Math.Max(1L, targetLenFrames * channels);

                    if (fraction >= 0f)
                    {
                        long anchorStartSamples = Math.Clamp(signChanged ? prevEndSamples : prevStartSamples, 0L, totalSamples - channels);

                        long desiredEndSamples = anchorStartSamples + targetLenSamples;
                        long clampedEnd = Math.Clamp(desiredEndSamples, anchorStartSamples + 1L, Math.Max(1L, totalSamples));
                        long clampedStart = Math.Clamp(anchorStartSamples, 0L, Math.Max(0L, clampedEnd - 1));

                        startFrame = clampedStart / channels;
                        endFrame = clampedEnd / channels;
                        anchorAtStart = true;
                    }
                    else
                    {
                        long anchorEndSamples = Math.Clamp(signChanged ? prevStartSamples : prevEndSamples, (long)channels, totalSamples);

                        long desiredStartSamples = anchorEndSamples - targetLenSamples;
                        long clampedStart = Math.Clamp(desiredStartSamples, 0L, Math.Max(0L, anchorEndSamples - 1));
                        long clampedEnd = Math.Clamp(anchorEndSamples, clampedStart + 1L, Math.Max(1L, totalSamples));

                        startFrame = clampedStart / channels;
                        endFrame = clampedEnd / channels;
                        anchorAtStart = false;
                    }
                }
                else
                {
                    long deltaFrames = Math.Max(1L, (long)Math.Round(Math.Abs(fraction) * framesPerBeat));
                    long currentFrame = currentSamplesOverride.HasValue
                        ? currentSamplesBefore / channels
                        : audio.Position;

                    if (fraction < 0f)
                    {
                        startFrame = currentFrame - deltaFrames;
                        endFrame = currentFrame;
                        anchorAtStart = false;
                    }
                    else
                    {
                        startFrame = currentFrame;
                        endFrame = currentFrame + deltaFrames;
                        anchorAtStart = true;
                    }

                    startFrame = Math.Clamp(startFrame, 0L, Math.Max(0L, totalFrames - 1));
                    endFrame = Math.Clamp(endFrame, startFrame + 1L, Math.Max(1L, totalFrames));
                }

                startFrame = Math.Clamp(startFrame, 0L, totalFrames - 1);
                endFrame = Math.Clamp(endFrame, startFrame + 1, totalFrames);
                long baseStartSamples = startFrame * channels;
                long baseEndSamples = endFrame * channels;
                long fractionSamples = Math.Max(1L, baseEndSamples - baseStartSamples);

                bool insideNewLoop = currentSamplesBefore >= baseStartSamples && currentSamplesBefore < baseEndSamples;

                long desiredSamples = currentSamplesBefore;
                bool forceJump = false;

                // Special: when switching sign, keep "phase" around the shared boundary
                if (signChanged && havePrevLoop)
                {
                    long prevLen = Math.Max(1L, prevEndSamples - prevStartSamples);

                    // + -> -
                    if (signPrev > 0 && signNow < 0)
                    {
                        long offsetFromPrevStart = Math.Clamp(currentSamplesBefore - prevStartSamples, 0L, prevLen - 1);
                        desiredSamples = Math.Clamp(baseEndSamples - offsetFromPrevStart, baseStartSamples, baseEndSamples - 1);
                        forceJump = true;
                    }
                    // - -> +
                    else if (signPrev < 0 && signNow > 0)
                    {
                        long offsetFromPrevEnd = Math.Clamp(prevEndSamples - currentSamplesBefore, 0L, prevLen - 1);
                        desiredSamples = Math.Clamp(baseStartSamples + offsetFromPrevEnd, baseStartSamples, baseEndSamples - 1);
                        forceJump = true;
                    }
                }
                else if (havePrevLoop && insideNewLoop)
                {
                    // Existing continuity alignment
                    if (anchorAtStart)
                    {
                        long prevRel = Math.Max(0L, currentSamplesBefore - prevStartSamples);
                        desiredSamples = Math.Clamp(baseStartSamples + Math.Min(prevRel, fractionSamples - 1), baseStartSamples, baseEndSamples - 1);
                    }
                    else
                    {
                        long prevRelFromEnd = Math.Max(0L, prevEndSamples - currentSamplesBefore);
                        desiredSamples = Math.Clamp(baseEndSamples - Math.Min(prevRelFromEnd, fractionSamples - 1), baseStartSamples, baseEndSamples - 1);
                    }
                }

                // Apply loop; request adjustPosition if outside OR we deliberately want to re-anchor on sign switch
                audio.UpdateLoopFraction(baseStartSamples, baseEndSamples, fractionSamples, true, (!insideNewLoop) || forceJump);

                // Force exact position if needed (prevents weird "same offset" feel)
                if (insideNewLoop || forceJump)
                {
                    audio.JumpToSamples(desiredSamples);
                }
                audio.Metrics["loop.ui.fraction"] = fraction;
                audio.Metrics["loop.ui.multiplier"] = this.Multiplier;

                // Track last applied loop and fraction for future relative scaling
                state.StartSamples = baseStartSamples;
                state.EndSamples = baseEndSamples;
                state.Fraction = fraction;
                RefreshTargetWaveform(audio);
            }
            catch (Exception ex)
            {
                LogCollection.Log(ex);
            }
        }




        internal void UpdateLoopButtonsState()
        {
            AudioObj? audio = this.OriginalAudio;
            this.UpdateTargetLabel();
            this.SynchronizeMultiplierForAudio(audio);
            // Guard
            if (audio == null)
            {
                foreach (var btn in this.panel_buttons.Controls.OfType<Button>())
                {
                    btn.Enabled = this.selectedPlaylistTrackIds.Count > 0;
                }
                this.UntoggleAllOtherButtons(null);
                return;
            }

            if (audio.Id != this._lastJumpAudioId)
            {
                // GANZ WICHTIG: Die ID jetzt merken, damit die Bedingung beim nächsten Timer-Tick false ist!
                this._lastJumpAudioId = audio.Id;
                this.UpdateJumpDistanceForRate(audio);
            }

            // Enable all buttons
            foreach (var btn in this.panel_buttons.Controls.OfType<Button>())
            {
                btn.Enabled = true;
            }

            float targetFraction = GetUiLoopFraction(audio);

            // Button anhand des exakten Fraction-Wertes (mit Vorzeichen) finden
            Button? matchingButton = this.panel_buttons.Controls.OfType<Button>()
                .FirstOrDefault(b =>
                {
                    string tag = b.Tag?.ToString() ?? "0";
                    if (float.TryParse(tag, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float val) ||
                        float.TryParse(tag, out val))
                    {
                        return Math.Abs(val - targetFraction) < 0.0001f;
                    }
                    return false;
                });

            if (matchingButton != null)
            {
                matchingButton.BackColor = Color.LightBlue;
                this.UntoggleAllOtherButtons(matchingButton);
            }
            else
            {
                // Fallback: keine Übereinstimmung -> alles untoggeln
                this.UntoggleAllOtherButtons(null);
            }

        }

        private async void button_copy_Click(object sender, EventArgs e)
        {
            // Check if Ctrl key is held down for "copy looping tracks only" mode
            bool ctrlPressed = Control.ModifierKeys == Keys.Control;

            if (ctrlPressed)
            {
                // Ctrl+Click: Copy only looping tracks without other playbacks, at their rates
                await this.CopyLoopingTracksCtrlClickAsync();
                return;
            }

            // Normal Click: Copy looping range from all playing tracks with rate modifications
            await this.CopyLoopingTracksNormalClickAsync();
            return;

            // Original code (kept for reference, now unreachable due to early returns above):
            /*
            AudioObj? audio = this.OriginalAudio;
            if (audio == null || !audio.LoopEnabled)
            {
                return;
            }

            // Prefer the currently active button label for naming (fixes the "off by one step" naming)
            string GetUiFractionLabel()
            {
                var btn = this.panel_buttons.Controls
                    .OfType<Button>()
                    .FirstOrDefault(b => b.BackColor == Color.LightBlue);

                string txt = btn?.Text?.Trim() ?? string.Empty;

                // "/4" -> "1/4"
                if (txt.StartsWith("/", StringComparison.Ordinal))
                {
                    return "1" + txt;
                }

                // "1", "2", "4", "8", "16" -> as-is
                if (!string.IsNullOrWhiteSpace(txt))
                {
                    return txt;
                }

                // Fallback: derive from LoopFraction (best-effort)
                float lf = GetUiLoopFraction(audio);
                if (lf > 0f && lf < 1f)
                {
                    double recip = 1.0 / lf;
                    int recipInt = (int) Math.Round(recip);
                    if (Math.Abs(recip - recipInt) < 1e-3 && recipInt > 1)
                    {
                        return "1/" + recipInt.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                    return lf.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                }
                if (lf >= 1f)
                {
                    int whole = (int) Math.Round(lf);
                    return Math.Abs(lf - whole) < 1e-3
                        ? whole.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        : lf.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                }

                return "1";
            }

            LoopTargetState state = this.GetLoopTargetState(audio);
            long? startSample = state.StartSamples >= 0 ? state.StartSamples : null;
            long? endSample = state.EndSamples > state.StartSamples ? state.EndSamples : null;
            string uiLabel = GetUiFractionLabel();

            var copiedLoop = await audio.CreateLoopAsync(startSample, endSample);

            if (copiedLoop != null)
            {
                // Override the name to match UI label (fix)
                double loopStartTime = 0.0;
                if (startSample.HasValue)
                {
                    loopStartTime = (double) startSample.Value
                                    / Math.Max(1, audio.SampleRate)
                                    / Math.Max(1, audio.Channels);
                }

                copiedLoop.Rename($"{audio.OriginalName} (Looped {uiLabel} at {loopStartTime:F1}s)");

                if (this.CollectionView == null)
                {
                    this.CollectionView = new([]);
                    this.CollectionView.Rename("Loops - '" + audio.OriginalName + "'");
                    this.CollectionView.FormClosing += (s, e) =>
                    {
                        this.CollectionView = null;
                    };
                }
                this.CollectionView.AudioC.Audios.Add(copiedLoop);
            }
            */
        }

        private async Task CopyLoopingTracksNormalClickAsync()
        {
            bool ctrlPressed = Control.ModifierKeys == Keys.Control;

            // Get all currently playing track views AND playlist tracks
            var playingTrackViews = WindowMain.PlayingTrackViews.ToList();

            // Also get playing playlist tracks
            var playingPlaylistTracks = WindowMain.TrackViews
                .Where(tv => tv.OriginalAudio != null && tv.OriginalAudio.PlayerPlaying)
                .Select(tv => tv.OriginalAudio)
                .DistinctBy(audio => audio.Id)
                .ToList();

            // Combine both sources, deduplicating by ID
            var allPlayingAudios = playingTrackViews
                .Select(tv => tv.OriginalAudio)
                .Where(audio => audio != null)
                .Concat(playingPlaylistTracks)
                .DistinctBy(audio => audio.Id)
                .ToList();

            if (allPlayingAudios.Count == 0)
            {
                return;
            }

            if (ctrlPressed)
            {
                // Ctrl+Click: Copy only looping tracks without other playbacks, at their rates
                // Collect all looping audio objects from all playing tracks
                var loopingAudios = allPlayingAudios
                    .Where(audio => audio != null && audio.LoopEnabled)
                    .ToList();

                if (loopingAudios.Count == 0)
                {
                    return;
                }

                // Merge all looping tracks into a single resampled sample
                var merged = await this.MergeLoopedTracksAsync(loopingAudios);
                if (merged != null)
                {
                    this.AddMergedLoopToCollectionView(merged, loopingAudios, "Looped Tracks");
                }
            }
            else
            {
                // Normal Click: Copy looping range from all playing tracks with rate modifications
                // Collect all audio objects from all playing tracks (both looping and non-looping)
                var allPlayingAudiosFiltered = allPlayingAudios
                    .Where(audio => audio != null)
                    .ToList();

                if (allPlayingAudiosFiltered.Count == 0)
                {
                    return;
                }

                // Merge all playing tracks into a single resampled sample
                var merged = await this.MergeLoopedTracksAsync(allPlayingAudiosFiltered);
                if (merged != null)
                {
                    this.AddMergedLoopToCollectionView(merged, allPlayingAudiosFiltered, "Looped Tracks");
                }
            }
        }

        private async Task CopyLoopingTracksCtrlClickAsync()
        {
            // Collect all looping audio objects from all playing tracks
            var playingTrackViews = WindowMain.PlayingTrackViews.ToList();
            var playingPlaylistTracks = WindowMain.TrackViews
                .Where(tv => tv.OriginalAudio != null && tv.OriginalAudio.PlayerPlaying)
                .Select(tv => tv.OriginalAudio)
                .DistinctBy(a => a.Id)
                .ToList();

            var allPlayingAudios = playingTrackViews
                .Select(tv => tv.OriginalAudio)
                .Where(a => a != null)
                .Concat(playingPlaylistTracks)
                .DistinctBy(a => a.Id)
                .ToList();

            var loopingAudios = allPlayingAudios
                .Where(a => a != null && a.LoopEnabled)
                .ToList();

            if (loopingAudios.Count == 0)
            {
                return;
            }

            // Merge all looping tracks into a single resampled sample
            var merged = await this.MergeLoopedTracksAsync(loopingAudios);
            if (merged != null)
            {
                string baseName = loopingAudios[0].OriginalName;
                this.AddMergedLoopToCollectionView(merged, loopingAudios, $"Looped Track - {baseName}");
            }
        }






        // Hilfsmethode: hängt Click/MouseUp-Handler an ein Control, der danach die TrackView refokussiert
        private void AttachAutoRefocusToControl(Control ctrl)
        {
            if (ctrl == null || ctrl == this.checkedListBox_playlistTracks ||
                ctrl == this.button_playlistAllOn || ctrl == this.button_playlistAllOff)
            {
                return;
            }

            // Verhindere Mehrfach-Anhänge
            if (this.autoRefocusAttached.Contains(ctrl))
            {
                return;
            }

            this.autoRefocusAttached.Add(ctrl);

            // Handler, der nach Abschluss des aktuellen UI-Event-Dispatchs den Fokus zurücksetzt
            void RestoreFocusDeferred()
            {
                try
                {
                    // BeginInvoke stellt sicher, dass das auslösende Event komplettExecuted
                    this.BeginInvoke((Action)(() =>
                    {
                        try
                        {
                            if (!this.checkedListBox_playlistTracks.IsInteracting && !this.checkedListBox_playlistTracks.ContainsFocus)
                            {
                                this.CurrentTrackView?.Focus();
                            }
                        }
                        catch { }
                    }));
                }
                catch { }
            }

            try
            {
                // Klick / MouseUp abdecken
                ctrl.Click += (s, e) => RestoreFocusDeferred();
                ctrl.MouseUp += (s, e) => RestoreFocusDeferred();

                // Manche Controls (z.B. ToolStripItem) sind keine Controls — hier behandeln wir normale Controls.
                // Falls ein Control Kinder hat, werden diese beim initialen Rekursionslauf ohnehin angehängt.
            }
            catch { }
        }

        private void EnableAutoRefocusForContainer(Control container)
        {
            if (container == null)
            {
                return;
            }

            // Verhindere mehrfaches Registrieren desselben Containers
            if (this.containerMonitored.Contains(container))
            {
                return;
            }

            this.containerMonitored.Add(container);

            try
            {
                // Existierende Kinder anhängen (rekursiv)
                foreach (Control c in container.Controls)
                {
                    try
                    {
                        // --- NEU: NumericUpDown überspringen! ---
                        // Verhindert, dass das Klicken auf die Pfeile sofort den Fokus stiehlt
                        if (c is NumericUpDown)
                        {
                            continue;
                        }

                        this.AttachAutoRefocusToControl(c);

                        if (c.HasChildren)
                        {
                            this.EnableAutoRefocusForContainer(c);
                        }
                    }
                    catch { }
                }

                // Event, damit zukünftige hinzugefügte Controls ebenfalls automatisch abgedeckt werden
                container.ControlAdded += (s, e) =>
                {
                    try
                    {
                        if (e.Control == null)
                        {
                            return;
                        }

                        this.AttachAutoRefocusToControl(e.Control);
                        if (e.Control.HasChildren)
                        {
                            this.EnableAutoRefocusForContainer(e.Control);
                        }
                    }
                    catch { }
                };
            }
            catch { }
        }

        private void domainUpDown_multiplier_SelectedItemChanged(object? sender, EventArgs e)
        {
            if (this.suppressMultiplierEvents)
            {
                return;
            }

            // DomainUpDown selection changed - update multiplier
            this.UpdateMultiplierFromSelection();

            IReadOnlyList<AudioObj> targets = this.GetActionTargets(ModifierKeys.HasFlag(Keys.Control));
            this.lastActionWasMultiplierChange = true;
            try
            {
                foreach (AudioObj audio in targets.Where(audio => audio.LoopEnabled))
                {
                    this.SetLoopRange(audio, GetUiLoopFraction(audio), true);
                }
            }
            finally
            {
                this.lastActionWasMultiplierChange = false;
            }
            this.UpdateLoopButtonsState();
        }

        private void UpdateMultiplierFromSelection()
        {
            if (this.domainUpDown_multiplier.SelectedItem is string selectedItem &&
                double.TryParse(selectedItem, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double val))
            {
                // Value changed via selection - no snapping needed as values are discrete
            }
        }

        private void domainUpDown_multiplier_Click(object? sender, EventArgs e)
        {
            if (ModifierKeys.HasFlag(Keys.Control))
            {
                return;
            }

            // DomainUpDown click - no special handling needed as items are discrete
            this.UpdateMultiplierFromSelection();

            IReadOnlyList<AudioObj> targets = this.GetActionTargets(ModifierKeys.HasFlag(Keys.Control));
            this.lastActionWasMultiplierChange = true;
            try
            {
                foreach (AudioObj audio in targets.Where(audio => audio.LoopEnabled))
                {
                    this.SetLoopRange(audio, GetUiLoopFraction(audio), true);
                }
            }
            finally
            {
                this.lastActionWasMultiplierChange = false;
            }
            this.UpdateLoopButtonsState();
        }

        private void button_backward_Click(object sender, EventArgs e)
        {
            this.JumpByMilliseconds(-1);
        }

        private void button_forward_Click(object sender, EventArgs e)
        {
            this.JumpByMilliseconds(1);
        }

        private void numericUpDown_jump_ValueChanged(object? sender, EventArgs e)
        {
            if (this.suppressJumpEvents)
            {
                return;
            }

            if (!ModifierKeys.HasFlag(Keys.Shift) && !ModifierKeys.HasFlag(Keys.Control))
            {
                this.numericUpDown_jump.ValueChanged -= this.numericUpDown_jump_ValueChanged;

                double currentValue = (double)this.numericUpDown_jump.Value;
                AudioObj? audio = this.OriginalAudio;
                double rateFactor = audio == null ? 1.0 : GetJumpRateFactor(audio);
                double baseJumpMs = audio == null
                    ? this.lastJumpMs * rateFactor
                    : this.GetJumpBaseMs(audio);

                if (currentValue > this.lastJumpValue)
                {
                    // Moving Up
                    baseJumpMs *= 2;
                }
                else if (currentValue < this.lastJumpValue)
                {
                    // Moving Down
                    baseJumpMs /= 2;
                }
                else
                {
                    // Preserve direct edits as a new unscaled step.
                    baseJumpMs = currentValue * rateFactor;
                }

                double minimumBaseMs = (double)this.numericUpDown_jump.Minimum * rateFactor;
                double maximumBaseMs = (double)this.numericUpDown_jump.Maximum * rateFactor;
                baseJumpMs = Math.Clamp(baseJumpMs, minimumBaseMs, maximumBaseMs);
                this.SetJumpBaseMs(audio, baseJumpMs);
                double scaledJumpMs = Math.Clamp(baseJumpMs / rateFactor,
                    (double)this.numericUpDown_jump.Minimum,
                    (double)this.numericUpDown_jump.Maximum);
                this.numericUpDown_jump.Value = (decimal)scaledJumpMs;
                this.lastJumpValue = (double)this.numericUpDown_jump.Value;
                this.lastJumpMs = this.lastJumpValue;

                this.numericUpDown_jump.ValueChanged += this.numericUpDown_jump_ValueChanged;
            }
        }

        private void numericUpDown_jump_Click(object? sender, EventArgs e)
        {
            // If not ctrl is held, ignore
            if (!ModifierKeys.HasFlag(Keys.Control))
            {
                return;
            }

            AudioObj? audio = this.OriginalAudio;
            double rateFactor = audio == null ? 1.0 : GetJumpRateFactor(audio);
            double msPerBeat = 60000.0 / this.Bpm;
            double baseJumpMs = msPerBeat * rateFactor;
            this.SetJumpBaseMs(audio, baseJumpMs);
            double scaledJumpMs = Math.Clamp(baseJumpMs / rateFactor,
                (double)this.numericUpDown_jump.Minimum,
                (double)this.numericUpDown_jump.Maximum);
            this.numericUpDown_jump.Value = (decimal)scaledJumpMs;
            this.lastJumpMs = (double)this.numericUpDown_jump.Value;
            this.lastJumpValue = this.lastJumpMs;
        }

        /// <summary>
        /// Updates the jump distance to reflect the current rate of the audio.
        /// When the rate changes, the jump distance in milliseconds must be adjusted
        /// so that the same number of samples is jumped.
        /// </summary>
        private void UpdateJumpDistanceForRate(AudioObj audio)
        {
            this.UpdateJumpDistanceForRateImmediately(audio, GetJumpRateFactor(audio));
        }

        private void UpdateJumpDistanceForRateImmediately(AudioObj audio, double rateFactor)
        {
            double baseJumpMs = this.GetJumpBaseMs(audio);
            double scaledJumpMs = Math.Clamp(baseJumpMs / rateFactor,
                (double)this.numericUpDown_jump.Minimum,
                (double)this.numericUpDown_jump.Maximum);
            this.suppressJumpEvents = true;
            try
            {
                this.numericUpDown_jump.Value = (decimal)scaledJumpMs;
                this.lastJumpMs = (double)this.numericUpDown_jump.Value;
                this.lastJumpValue = this.lastJumpMs;
            }
            finally
            {
                this.suppressJumpEvents = false;
            }
        }




        private void JumpByMilliseconds(int direction)
        {
            IReadOnlyList<AudioObj> targets = this.GetActionTargets(ModifierKeys.HasFlag(Keys.Control));
            foreach (AudioObj audio in targets)
            {
                this.JumpByMilliseconds(audio, direction);
            }
        }

        private void JumpByMilliseconds(AudioObj audio, int direction)
        {
            LoopTargetState state = this.GetLoopTargetState(audio);
            int channels = Math.Max(1, audio.Channels);
            long totalSamples = Math.Max(0L, audio.Length);

            // SampleRateFactor is the rate currently applied by the live varispeed pipeline.
            double rateFactor = audio.SampleRateFactor;
            if (Math.Abs(rateFactor - 1.0) < 0.0005) rateFactor = 1.0;

            // JumpSamples ist in Frames gerechnet (SampleRate * ms / 1000),
            // skaliert mit dem Loop-Multiplier, damit die Sprungdistanz proportional
            // zur Loop-Länge ist (multi < 1 -> kürzerer Sprung, multi > 1 -> weiter).
            // JumpMs is audible playback time. Varispeed advances farther through
            // the source during that time, so source frames scale with the rate.
            long deltaFrames = (long)(audio.SampleRate * this.JumpMs / 1000f * this.Multiplier * rateFactor) * direction;
            long currentSamples = audio.Position * channels;
            long deltaSamples = deltaFrames * channels;

            // Wenn es keinen aktiven Loop gibt: Playhead einfach um die Distanz springen.
            bool haveLoop = state.StartSamples >= 0 &&
                            state.EndSamples > state.StartSamples &&
                            audio.LoopEnabled;

            if (!haveLoop)
            {
                long targetSamples = Math.Clamp(currentSamples + deltaSamples, 0L, Math.Max(0L, totalSamples - 1));
                audio.JumpToSamples(targetSamples);
                RefreshTargetWaveform(audio);
                return;
            }

            // Aktiven Loop um dieselbe Distanz verschieben (Start & End), Länge bleibt gleich.
            long len = state.EndSamples - state.StartSamples;
            if (len <= 0)
            {
                return;
            }

            long newStart = state.StartSamples + deltaSamples;
            long newEnd = state.EndSamples + deltaSamples;

            // Loop innerhalb des Files clampen, Länge bleibt gleich
            if (newStart < 0)
            {
                newStart = 0;
                newEnd = Math.Min(len, totalSamples);
            }
            else if (newEnd > totalSamples)
            {
                newEnd = totalSamples;
                newStart = Math.Max(0, newEnd - len);
            }

            long fractionSamples = Math.Max(1L, newEnd - newStart);

            // Playhead-Offset relativ zum alten Loop-Start (Phase innerhalb des Loops).
            long offsetInLoop = Math.Clamp(currentSamples - state.StartSamples, 0L, len - 1);

            // Loop an neuer Position setzen (adjustPosition=false: Playhead nicht an Loop-Start zwingen).
            audio.UpdateLoopFraction(newStart, newEnd, fractionSamples, true, false);

            // Playhead an dieselbe Phase im verschobenen Loop setzen:
            // Der Loop wird als Ganzes verschoben, statt vom aktuellen Punkt im Loop weiterzuloopen.
            long jumpTargetSamples = Math.Clamp(newStart + offsetInLoop, 0L, Math.Max(0L, totalSamples - 1));
            audio.JumpToSamples(jumpTargetSamples);

            // State im LoopControl aktualisieren
            state.StartSamples = newStart;
            state.EndSamples = newEnd;
            audio.Metrics["loop.ui.fraction"] = state.Fraction;

            // Nach Loop-Verschiebung erneut UI-Refresh anstoßen
            RefreshTargetWaveform(audio);
        }

        /// <summary>
        /// Resamples the loop audio to default rate (0% / 1.0x) and resets rate factors.
        /// The audio data is time-stretched/pitch-corrected to play at normal speed.
        /// Supports mono-to-stereo duplication when needed.
        /// </summary>
        private async Task ResampleLoopToDefaultRateAsync(AudioObj loopedAudio, AudioObj originalAudio)
        {
            // Calculate the effective sample rate of the original audio with its rate factors
            double effectiveSampleRate = originalAudio.SampleRate * originalAudio.SampleRateFactor * originalAudio.ManualSampleRateFactor;
            int targetSampleRate = (int)originalAudio.SampleRate; // Use the original sample rate as target (default 1.0x)

            // If the effective rate is already 1.0x, no resampling needed
            if (Math.Abs(effectiveSampleRate - targetSampleRate) < 1.0)
            {
                return;
            }

            // Resample the audio data using NAudio's WdlResampler
            // We need to read the loop range and resample it to the target rate
            int channels = originalAudio.Channels;
            long loopStart = loopedAudio.SelectionStart;
            long loopEnd = loopedAudio.SelectionEnd;
            long loopLengthSamples = loopEnd - loopStart;

            if (loopLengthSamples <= 0) { return; }

            // Resample the loop audio to default rate (bake in the rate factor)
            long loopStartRead = loopedAudio.SelectionStart;
            long loopEndRead = loopedAudio.SelectionEnd;

            // Read the loop audio data directly from the original audio's Data array
            if (originalAudio.Data == null || originalAudio.Data.Length == 0) { return; }

            // Calculate the start and end indices in the Data array
            long startSampleIndex = loopStartRead * channels;
            long endSampleIndex = loopEndRead * channels;

            // Ensure indices are within bounds
            startSampleIndex = Math.Max(0, Math.Min(startSampleIndex, originalAudio.Data.Length));
            endSampleIndex = Math.Max(startSampleIndex, Math.Min(endSampleIndex, originalAudio.Data.Length));

            // Extract the loop range from the Data array
            int sampleCount = (int)(endSampleIndex - startSampleIndex);
            if (sampleCount <= 0) { return; }

            float[] channelData = new float[sampleCount];
            Array.Copy(originalAudio.Data, startSampleIndex, channelData, 0, sampleCount);

            // Resample using NAudio's WdlResampler
            var resampler = new NAudio.Dsp.WdlResampler();
            resampler.SetMode(true, 0, true, sinc_size: 64, sinc_interpsize: 16);
            resampler.SetFeedMode(false);

            // SetRates(inputRate, outputRate): input is the effective rate of the source,
            // output is the target (default) rate. This correctly time-stretches to 1.0x.
            resampler.SetRates(effectiveSampleRate, targetSampleRate);

            // Prepare output buffer: output length = inputFrames * (outputRate / inputRate)
            int inputFrames = sampleCount / channels;
            int outputFrames = (int)Math.Ceiling((double)inputFrames * targetSampleRate / effectiveSampleRate);
            var outputBuffer = new float[outputFrames * channels];

            // Perform the resampling
            int needed = resampler.ResamplePrepare(outputFrames, channels, out Span<float> input);
            for (int i = 0; i < Math.Min(needed * channels, channelData.Length); i++) { input[i] = channelData[i]; }
            int supplied = channelData.Length / channels;
            int written = resampler.ResampleOut(outputBuffer, supplied, outputFrames, channels);

            // Update the looped audio with the resampled data
            loopedAudio.Data = outputBuffer;
            loopedAudio.SampleRate = targetSampleRate;
            loopedAudio.Length = outputBuffer.Length;
            loopedAudio.Duration = TimeSpan.FromSeconds((double)outputBuffer.Length / targetSampleRate / channels);

            // Reset the rate factors since the rate is now baked into the audio data
            loopedAudio.ManualSampleRateFactor = 1.0;
            loopedAudio.SampleRateFactor = 1.0;
        }

        /// <summary>
        /// Extracts a loop range from an audio object, resamples it to default rate (1.0x),
        /// and handles mono-to-stereo duplication if needed.
        /// Returns the resampled float[] data, or null if extraction failed.
        /// </summary>
        private async Task<float[]?> ResampleAndExtractAsync(AudioObj audio, long startSample, long endSample, int targetChannels)
        {
            if (audio.Data == null || audio.Data.Length == 0) return null;

            int channels = audio.Channels;
            long loopStart = startSample;
            long loopEnd = endSample;

            if (loopStart >= loopEnd) return null;

            // Clamp to data bounds
            long totalSamples = audio.Data.LongLength;
            loopStart = Math.Max(0, Math.Min(loopStart, totalSamples));
            loopEnd = Math.Max(loopStart, Math.Min(loopEnd, totalSamples));

            long loopLengthSamples = loopEnd - loopStart;
            if (loopLengthSamples <= 0) return null;

            // The loop range is stored in original sample indices. When a track plays
            // at a custom rate (e.g. +50%), the looped region sounds faster and higher
            // in pitch, but the samples are still at the original sample rate.
            // We must NOT time-stretch the extracted samples – that would change the
            // speed and pitch away from what was actually heard during the loop.
            // Simply extract the raw data at 1:1, handling mono-to-stereo conversion.
            return ExtractWithChannelConversion(audio, loopStart, loopEnd, targetChannels);
        }

        /// <summary>
        /// Extracts a sample range from audio data with mono-to-stereo channel conversion.
        /// </summary>
        private float[]? ExtractWithChannelConversion(AudioObj audio, long startSample, long endSample, int targetChannels)
        {
            int channels = audio.Channels;
            long loopLengthSamples = endSample - startSample;
            if (loopLengthSamples <= 0) return null;

            if (targetChannels == channels)
            {
                // Same channels, just extract
                long startIdx = startSample * channels;
                long endIdx = endSample * channels;
                int count = (int)(endIdx - startIdx);
                float[] result = new float[count];
                Array.Copy(audio.Data, startIdx, result, 0, count);
                return result;
            }

            // Mono to stereo: duplicate each sample
            if (channels == 1 && targetChannels == 2)
            {
                float[] result = new float[(int)loopLengthSamples * 2];
                for (int i = 0; i < (int)loopLengthSamples; i++)
                {
                    result[i * 2] = audio.Data[(int)startSample + i];
                    result[i * 2 + 1] = audio.Data[(int)startSample + i];
                }
                return result;
            }

            // Stereo to mono: average channels
            if (channels == 2 && targetChannels == 1)
            {
                float[] result = new float[(int)loopLengthSamples];
                for (int i = 0; i < (int)loopLengthSamples; i++)
                {
                    result[i] = (audio.Data[(int)startSample * 2 + i * 2] + audio.Data[(int)startSample * 2 + i * 2 + 1]) * 0.5f;
                }
                return result;
            }

            // Fallback: just extract with original channels
            long startIdx2 = startSample * channels;
            long endIdx2 = endSample * channels;
            int count2 = (int)(endIdx2 - startIdx2);
            float[] result2 = new float[count2];
            Array.Copy(audio.Data, startIdx2, result2, 0, count2);
            return result2;
        }

        /// <summary>
        /// Converts channel count of audio data (e.g., mono to stereo or vice versa).
        /// </summary>
        private float[] ConvertChannels(float[] data, int fromChannels, int toChannels)
        {
            int frames = data.Length / fromChannels;
            if (toChannels == fromChannels) return data;

            if (fromChannels == 1 && toChannels == 2)
            {
                float[] result = new float[frames * 2];
                for (int i = 0; i < frames; i++)
                {
                    result[i * 2] = data[i];
                    result[i * 2 + 1] = data[i];
                }
                return result;
            }

            if (fromChannels == 2 && toChannels == 1)
            {
                float[] result = new float[frames];
                for (int i = 0; i < frames; i++)
                {
                    result[i] = (data[i * 2] + data[i * 2 + 1]) * 0.5f;
                }
                return result;
            }

            return data;
        }

        /// <summary>
        /// Computes the range of the largest looping track across multiple audio objects.
        /// Returns (start, end) or null if no valid loop ranges found.
        /// </summary>
        private (long start, long end)? ComputeUnionRange(IReadOnlyList<AudioObj> audios)
        {
            (long start, long end)? largest = null;

            foreach (var audio in audios)
            {
                if (!audio.LoopEnabled)
                {
                    continue;
                }

                long startSamples = audio.LoopStartSamples;
                long endSamples = audio.LoopEndSamples;
                if (endSamples <= startSamples || endSamples <= 0)
                {
                    LoopTargetState state = this.GetLoopTargetState(audio);
                    if (state.StartSamples >= 0 && state.EndSamples > state.StartSamples)
                    {
                        startSamples = state.StartSamples;
                        endSamples = state.EndSamples;
                    }
                }

                if (startSamples < 0 || endSamples <= startSamples)
                {
                    continue;
                }

                long length = endSamples - startSamples;
                if (largest == null || length > largest.Value.end - largest.Value.start)
                {
                    largest = (startSamples, endSamples);
                }
            }

            return largest;
        }

        /// <summary>
        /// Merges multiple audio objects into a single sample.
        /// Each track is extracted from its current loop position (with wrap-around),
        /// resampled to the base sample rate if a custom rate is active,
        /// then all are additively merged with their respective volumes.
        /// </summary>
        private async Task<AudioObj?> MergeLoopedTracksAsync(IReadOnlyList<AudioObj> audios)
        {
            if (audios.Count == 0) return null;

            // Determine target channel count: stereo if any track is stereo, else mono
            int targetChannels = audios.Any(a => a.Channels == 2) ? 2 : 1;

            // The longest active loop defines the capture duration. Every playing
            // track contributes from its exact position at the click time.
            var trackInfos = new List<(
                AudioObj audio,
                long captureStartFrame,
                long loopStartFrame,
                long loopEndFrame,
                double rateFactor,
                bool wrapsLoop)>();
            double maxLoopDurationSeconds = 0.0;

            foreach (var audio in audios)
            {
                if (audio.Data == null || audio.Data.Length == 0)
                {
                    continue;
                }

                int channels = Math.Max(1, audio.Channels);
                long totalFrames = Math.Max(0L, audio.Data.LongLength / channels);
                if (totalFrames <= 0 || audio.SampleRate <= 0)
                {
                    continue;
                }

                // SampleRateFactor is the rate currently applied by the live
                // varispeed pipeline. Manual and sync factors are already folded into it.
                double rateFactor = Math.Clamp(audio.SampleRateFactor, 0.01, 10.0);
                if (Math.Abs(rateFactor - 1.0) < 0.0005) rateFactor = 1.0;

                long captureStartFrame = Math.Clamp(audio.Position, 0L, totalFrames - 1);
                long loopStartFrame = -1;
                long loopEndFrame = -1;
                bool wrapsLoop = false;
                if (audio.LoopEnabled)
                {
                    long startSamples = audio.LoopStartSamples;
                    long endSamples = audio.LoopEndSamples;
                    if (endSamples <= startSamples || endSamples <= 0)
                    {
                        LoopTargetState state = this.GetLoopTargetState(audio);
                        startSamples = state.StartSamples;
                        endSamples = state.EndSamples;
                    }

                    loopStartFrame = startSamples / channels;
                    loopEndFrame = endSamples / channels;
                    wrapsLoop = loopStartFrame >= 0 && loopEndFrame > loopStartFrame && loopEndFrame <= totalFrames;
                    if (wrapsLoop)
                    {
                        long loopLengthFrames = loopEndFrame - loopStartFrame;
                        double loopDurationSeconds = loopLengthFrames / (double)(audio.SampleRate * rateFactor);
                        maxLoopDurationSeconds = Math.Max(maxLoopDurationSeconds, loopDurationSeconds);
                        captureStartFrame = Math.Clamp(captureStartFrame, loopStartFrame, loopEndFrame - 1);
                    }
                }

                trackInfos.Add((audio, captureStartFrame, loopStartFrame, loopEndFrame, rateFactor, wrapsLoop));
            }

            if (trackInfos.Count == 0) return null;

            bool captureBeforeClick = maxLoopDurationSeconds <= 0.0;
            if (captureBeforeClick)
            {
                AudioObj tempoAudio = this.OriginalAudio ?? trackInfos[0].audio;
                double effectiveBpm = GetAudioBpm(tempoAudio)
                    * Math.Max(0.01, tempoAudio.StretchFactor)
                    * Math.Clamp(tempoAudio.SampleRateFactor, 0.01, 10.0);
                if (effectiveBpm > 0.0)
                {
                    maxLoopDurationSeconds = 4.0 * Math.Max(0.01, this.Multiplier) * 60.0 / effectiveBpm;
                }
            }

            if (maxLoopDurationSeconds <= 0.0) return null;

            int outputSampleRate = Math.Max(1, trackInfos[0].audio.SampleRate);
            long maxEffectiveLengthFrames = Math.Max(1L,
                (long)Math.Ceiling(maxLoopDurationSeconds * outputSampleRate));

            // Capture each track from its click-time position into the same window.
            var extractedTasks = trackInfos.Select(async info =>
            {
                return await this.ExtractLoopWithWrapAsync(info.audio, info.captureStartFrame,
                    info.loopStartFrame, info.loopEndFrame, info.rateFactor,
                    info.wrapsLoop, captureBeforeClick, maxEffectiveLengthFrames,
                    outputSampleRate, targetChannels);
            });

            var extractedData = await Task.WhenAll(extractedTasks);

            // Find the longest extracted data to determine output length
            int maxLen = extractedData.Max(d => d?.Length ?? 0);
            if (maxLen == 0) return null;

            // Add all extracted samples together (additive merge),
            // scaling each track by its playback volume (Volume is 0-100, 100 = full).
            float[] merged = new float[maxLen];
            for (int i = 0; i < maxLen; i++)
            {
                float sum = 0f;
                for (int t = 0; t < extractedData.Length; t++)
                {
                    var data = extractedData[t];
                    if (data != null && i < data.Length)
                    {
                        float volumeScale = trackInfos[t].audio.Volume / 100f;
                        sum += data[i] * volumeScale;
                    }
                }
                merged[i] = sum;
            }

            // Compute the effective BPM of the merged loop.
            // Bpm already reflects the stretch (TimeStretcher divides Bpm by the factor),
            // so only the live rate factors need to be applied on top.
            // Small differences are averaged. Integer-multiple tempos such as
            // 105/210 are also compatible, so keep the fastest compatible BPM.
            float mergedBpm = 0f;
            var effectiveBpms = trackInfos
                .Select(info => (double)GetAudioBpm(info.audio) * info.audio.StretchFactor * info.rateFactor)
                .Where(b => b > 0)
                .ToList();
            if (effectiveBpms.Count > 0)
            {
                double minBpm = effectiveBpms.Min();
                double maxBpm = effectiveBpms.Max();
                if (maxBpm - minBpm <= 0.33)
                {
                    mergedBpm = (float)effectiveBpms.Average();
                }
                else if (AreCompatibleMultipleBpms(effectiveBpms))
                {
                    mergedBpm = (float)maxBpm;
                }
            }

            // Create the merged AudioObj
            var firstAudio = trackInfos[0].audio;
            var mergedAudio = new AudioObj
            {
                Name = string.Empty,
                FilePath = string.Empty,
                Data = merged,
                SampleRate = firstAudio.SampleRate,
                SampleRateFactor = 1.0,
                ManualSampleRateFactor = 1.0,
                SyncNudgeSampleRateFactor = 1.0,
                Channels = targetChannels,
                BitDepth = firstAudio.BitDepth,
                Length = merged.Length,
                Duration = TimeSpan.FromSeconds((double)merged.Length / firstAudio.SampleRate / targetChannels),
                Bpm = mergedBpm,
                Timing = firstAudio.Timing,
                Volume = firstAudio.Volume,
                SelectionStart = 0,
                SelectionEnd = merged.Length
            };

            // Set name
            string baseName = firstAudio.Name;
            int extraCount = trackInfos.Count - 1;
            mergedAudio.Rename($"{baseName} +{extraCount} Merged");

            return mergedAudio;
        }

        private static bool AreCompatibleMultipleBpms(IReadOnlyList<double> bpms)
        {
            if (bpms.Count < 2)
            {
                return false;
            }

            double baseBpm = bpms.Min();
            foreach (double bpm in bpms)
            {
                double multiple = Math.Round(bpm / baseBpm);
                if (multiple < 1.0 || Math.Abs(bpm - baseBpm * multiple) > 0.33)
                {
                    return false;
                }
            }

            return bpms.Max() > baseBpm;
        }

        /// <summary>
        /// Extracts the loop segment from the loop start and resamples to the
        /// effective sample rate if a custom rate is active.
        /// Resampling (NOT time-stretching) changes the sample rate, which shifts
        /// the pitch up/down while keeping the same duration – exactly like a
        /// tape-speed change during live playback at the custom rate.
        /// The output length is exactly <paramref name="targetLength"/> samples.
        /// </summary>
        private async Task<float[]?> ExtractLoopWithWrapAsync(
            AudioObj audio,
            long captureStartFrame,
            long loopStartFrame,
            long loopEndFrame,
            double rateFactor,
            bool wrapsLoop,
            bool captureBeforeClick,
            long targetLengthFrames,
            int outputSampleRate,
            int targetChannels)
        {
            if (audio.Data == null || audio.Data.Length == 0 || audio.SampleRate <= 0)
            {
                return null;
            }

            int channels = Math.Max(1, audio.Channels);
            int outputFrames = checked((int)Math.Max(1L, targetLengthFrames));
            rateFactor = Math.Clamp(rateFactor, 0.01, 10.0);

            // If no custom rate, return as-is
            if (Math.Abs(rateFactor - 1.0) < 0.0005 && audio.SampleRate == outputSampleRate)
            {
                float[] source = CaptureSourceFrames(audio, captureStartFrame, loopStartFrame,
                    loopEndFrame, wrapsLoop, captureBeforeClick, outputFrames);
                if (targetChannels != channels)
                {
                    return ConvertChannels(source, channels, targetChannels);
                }
                return source;
            }

            // Resample from the effective input rate back to the file sample rate.
            // This changes duration and pitch together, just like live varispeed
            // playback, rather than applying a pitch-preserving time stretch.
            double inputRate = audio.SampleRate * rateFactor;
            var resampler = new NAudio.Dsp.WdlResampler();
            resampler.SetMode(true, 0, true, sinc_size: 64, sinc_interpsize: 16);
            resampler.SetFeedMode(false);
            resampler.SetRates(inputRate, outputSampleRate);

            int needed = resampler.ResamplePrepare(outputFrames, channels, out Span<float> input);
            float[] sourceData = CaptureSourceFrames(audio, captureStartFrame, loopStartFrame,
                loopEndFrame, wrapsLoop, captureBeforeClick, needed);
            input.Clear();
            sourceData.AsSpan().CopyTo(input);

            var outputBuffer = new float[checked(outputFrames * channels)];
            resampler.ResampleOut(outputBuffer, needed, outputFrames, channels);

            // Handle mono-to-stereo conversion if needed
            if (targetChannels != channels)
            {
                return ConvertChannels(outputBuffer, channels, targetChannels);
            }

            return outputBuffer;
        }

        private static float[] CaptureSourceFrames(
            AudioObj audio,
            long captureStartFrame,
            long loopStartFrame,
            long loopEndFrame,
            bool wrapsLoop,
            bool captureBeforeClick,
            int frameCount)
        {
            int channels = Math.Max(1, audio.Channels);
            long totalFrames = audio.Data.LongLength / channels;
            float[] result = new float[checked(frameCount * channels)];
            long loopLength = loopEndFrame - loopStartFrame;

            for (int frame = 0; frame < frameCount; frame++)
            {
                long sourceFrame = captureBeforeClick
                    ? captureStartFrame - frameCount + frame
                    : captureStartFrame + frame;
                if (wrapsLoop && loopLength > 0)
                {
                    long relative = (sourceFrame - loopStartFrame) % loopLength;
                    if (relative < 0) relative += loopLength;
                    sourceFrame = loopStartFrame + relative;
                }

                if (sourceFrame < 0 || sourceFrame >= totalFrames) continue;

                int sourceOffset = checked((int)(sourceFrame * channels));
                Array.Copy(audio.Data, sourceOffset, result, frame * channels, channels);
            }

            return result;
        }

        /// <summary>
        /// Adds a merged loop to the CollectionView (creates if null).
        /// </summary>
        private void AddMergedLoopToCollectionView(AudioObj merged, IReadOnlyList<AudioObj> audios, string defaultName)
        {
            if (this.CollectionView == null)
            {
                this.CollectionView = new([]);
                this.CollectionView.Rename(defaultName + " - " + DateTime.UtcNow.ToString("HH:mm:ss"));
                this.CollectionView.FormClosing += (s, e) =>
                {
                    this.CollectionView = null;
                };
            }

            this.CollectionView.AudioC.Audios.Add(merged);

            if (!this.CollectionView.Visible)
            {
                this.CollectionView.ShowDialog();
            }
        }

        private void Fill_ComboBox_Drops()
        {
            this.comboBox_drops.Items.Clear();
            this.comboBox_drops.Items.Add("Select a Drop");

            // Get enum values and names
            var dropValues = Enum.GetValues(typeof(DropType)).Cast<DropType>();
            foreach (var dropType in dropValues)
            {
                string name = dropType.ToString();
                this.comboBox_drops.Items.Add(name);
            }
        }

        private async void comboBox_drops_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (this.comboBox_drops.SelectedIndex <= 0)
            {
                return;
            }

            string selectedName = this.comboBox_drops.SelectedItem?.ToString() ?? string.Empty;
            DropType dropType = Enum.TryParse(selectedName, out DropType result) ? result : DropType.AlignedAll;

            Dictionary<Guid, int> timings = new();
            AudioObj[] audios = this.GetActionTargets(ModifierKeys.HasFlag(Keys.Control)).ToArray();
            if (audios.Length == 0)
            {
                return;
            }
            try
            {
                timings = await TimeManageDropsAsync(audios, dropType);
            }
            catch (Exception ex)
            {
                LogCollection.Log($"Drop timing calculation failed: {ex.Message}");
                timings = new Dictionary<Guid, int>();
            }

            // log timings for debugging
            try
            {
                foreach (var kv in timings)
                {
                    LogCollection.Log($"Drop timing: audio={kv.Key} offsetSamples={kv.Value}");
                }
            }
            catch { }

            // Start PausingPlaybackSyncer for the target audios for an auto-calculated duration
            try
            {
                // compute median bpm for selected tracks
                double? medianBpm = null;
                var bpms = audios.Where(a => a != null && a.Bpm > 0).Select(a => (double)a.Bpm).OrderBy(x => x).ToArray();
                if (bpms.Length > 0)
                {
                    medianBpm = bpms[bpms.Length / 2];
                }

                double? duration = null; // let RunForAsync compute default if null
                // start syncer async (do not block UI)
                _ = Task.Run(() => Audio.Processors_V3.PausingPlaybackSyncer.RunForAsync(audios, duration));
            }
            catch (Exception ex)
            {
                LogCollection.Log($"Failed to start PausingPlaybackSyncer: {ex.Message}");
            }

        }
    }
}
