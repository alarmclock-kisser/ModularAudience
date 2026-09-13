using ModularAudience.Audio;
using ModularAudience.Audio.Processing;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
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
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            try
            {
                int idx = this.checkedListBox_playlistTracks.IndexFromPoint(e.Location);
                if (idx >= 0 && idx < this.checkedListBox_playlistTracks.Items.Count)
                {
                    this.checkedListBox_playlistTracks.SelectedIndex = idx;
                    // ensure the right-clicked item is selected (not only focused)
                }
            }
            catch { }
        }

        private void contextMenuStrip_playlistItem_Opening(object? sender, CancelEventArgs e)
        {
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
        private bool suppressPlaylistChecklistEvents;

        private float Bpm => this.OriginalAudio is AudioObj audio ? GetAudioBpm(audio) : 120f;
        private int Multiplier => (int) this.numericUpDown_multiplier.Value;
        private int JumpMs => (int) this.numericUpDown_jump.Value;

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
        private float lastJumpMs = 1;
        private float lastJumpValue = 1;
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
            return $"{state} {name} · {BuildPlaylistRateText(audio)} {shortId}";
        }

        private void RefreshPlaylistTargets()
        {
            if (this.checkedListBox_playlistTracks.IsInteracting || this.contextMenuStrip_playlistItem.Visible)
            {
                return;
            }
            Guid? selectedAudioId = (this.checkedListBox_playlistTracks.SelectedItem as PlaylistTargetItem)?.Audio.Id;
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

                    var existingItem = (PlaylistTargetItem) listBox.Items[currentIndex];
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



        private void LoopButton_Click(object? sender, EventArgs e)
        {
            if (sender is not Button clickedButton ||
                !float.TryParse(clickedButton.Tag?.ToString(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float fraction))
            {
                return;
            }

            IReadOnlyList<AudioObj> targets = this.GetActionTargets(ModifierKeys.HasFlag(Keys.Control));
            this.ToggleTargetLoops(targets, fraction);

            // Focus TrackView but also keep this Form front most
            this.CurrentTrackView?.Focus();
            this.BringToFront();

            if (this.CurrentTrackView != null)
            {
                int index = this.FindPlaylistTrackIndex(this.CurrentTrackView.OriginalAudio.Id);
                if (index >= 0) this.checkedListBox_playlistTracks.SelectedIndex = index;
            }
        }

        private void UntoggleAllOtherButtons(Button? sender)
        {
            var buttons = this.panel_buttons.Controls.OfType<Button>().Where(b => b != sender);
            foreach (var button in buttons)
            {
                button.BackColor = SystemColors.Control;
            }
        }

        private void SetLoopRange(AudioObj audio, float fraction, bool hadActiveBefore = false)
        {
            LoopTargetState state = this.GetLoopTargetState(audio);

            // If no button is selected after toggle -> disable loop and reset tracking
            if (fraction == 0f)
            {
                audio.UpdateLoopFraction(0, 0, 0, false, true);
                audio.Metrics["loop.ui.fraction"] = 0f;
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
                long framesPerBeat = Math.Max(1L, (long) (audio.SampleRate * 60f / GetAudioBpm(audio) * 2f) * this.Multiplier);
                long totalFrames = Math.Max(0L, audio.Length / channels);
                long totalSamples = totalFrames * channels;

                // Capture current position and previous loop bounds before any change
                long currentSamplesBefore = audio.Position * channels;
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
                    long newLenSamples = Math.Max(1L, (long) Math.Round(prevLenSamples * ratio));

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
                        long anchorEndSamples = Math.Clamp(signChanged ? prevStartSamples : prevEndSamples, (long) channels, totalSamples);

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
                    long targetLenFrames = Math.Max(1L, (long) Math.Round(Math.Abs(fraction) * framesPerBeat));
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
                        long anchorEndSamples = Math.Clamp(signChanged ? prevStartSamples : prevEndSamples, (long) channels, totalSamples);

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
                    long deltaFrames = Math.Max(1L, (long) Math.Round(Math.Abs(fraction) * framesPerBeat));
                    long currentFrame = audio.Position;

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

                this.numericUpDown_jump.ValueChanged -= this.numericUpDown_jump_ValueChanged;

                decimal defaultJumpMs = (decimal) (60000f / GetAudioBpm(audio) / 4);
                // Optional, aber sicherheitshalber klammern, damit es bei wilden BPM nicht crasht:
                defaultJumpMs = Math.Clamp(defaultJumpMs, this.numericUpDown_jump.Minimum, this.numericUpDown_jump.Maximum);

                this.numericUpDown_jump.Value = defaultJumpMs;

                this.lastJumpMs = (float) this.numericUpDown_jump.Value;
                this.lastJumpValue = (float) this.numericUpDown_jump.Value; // <-- Das hier auch nachziehen!

                this.numericUpDown_jump.ValueChanged += this.numericUpDown_jump_ValueChanged;
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
                    this.BeginInvoke((Action) (() =>
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

        private void numericUpDown_multiplier_ValueChanged(object sender, EventArgs e)
        {
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
            if (!ModifierKeys.HasFlag(Keys.Shift) && !ModifierKeys.HasFlag(Keys.Control))
            {
                this.numericUpDown_jump.ValueChanged -= this.numericUpDown_jump_ValueChanged;

                float currentValue = (float) this.numericUpDown_jump.Value;

                if (currentValue > this.lastJumpValue)
                {
                    // Moving Up
                    this.lastJumpMs *= 2;
                    this.lastJumpMs = (float) Math.Clamp((decimal) this.lastJumpMs, 1m, this.numericUpDown_jump.Maximum);
                    this.numericUpDown_jump.Value = (decimal) this.lastJumpMs;
                }
                else if (currentValue < this.lastJumpValue)
                {
                    // Moving Down
                    this.lastJumpMs = Math.Max(1, this.lastJumpMs / 2);
                    this.numericUpDown_jump.Value = (decimal) this.lastJumpMs;
                }
                else
                {
                    // Sync lastJumpMs if value was set directly (e.g. via mouse drag)
                    this.lastJumpMs = currentValue;
                }

                this.lastJumpValue = (float) this.numericUpDown_jump.Value;
                this.lastJumpMs = this.lastJumpValue; // Ensure they stay in sync

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

            int msPerBeat = (int) Math.Round(60000f / this.Bpm);
            this.numericUpDown_jump.Value = msPerBeat;
            this.lastJumpMs = msPerBeat;
            this.lastJumpValue = msPerBeat;
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

            // JumpSamples ist in Frames gerechnet (SampleRate * ms / 1000)
            long deltaFrames = (long) (audio.SampleRate * this.JumpMs / 1000f) * direction;
            long currentSamples = audio.Position * channels;
            long deltaSamples = deltaFrames * channels;

            long targetSamples = currentSamples + deltaSamples;

            // Clamp innerhalb des Files
            targetSamples = Math.Clamp(targetSamples, 0L, Math.Max(0L, totalSamples - 1));

            // Playhead springen (immer!)
            audio.JumpToSamples(targetSamples);

            // UI sofort aktualisieren (Caret/Waveform neu rendern)
            RefreshTargetWaveform(audio);

            // Wenn es keinen aktiven Loop gibt, sind wir fertig
            bool haveLoop = state.StartSamples >= 0 &&
                            state.EndSamples > state.StartSamples &&
                            audio.LoopEnabled;

            if (!haveLoop)
            {
                return;
            }

            // Aktiven Loop um dieselbe Distanz verschieben (Start & End)
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

            // Loop an neuer Position setzen und weiterspielen
            audio.UpdateLoopFraction(newStart, newEnd, fractionSamples, true, true);

            // State im LoopControl aktualisieren
            state.StartSamples = newStart;
            state.EndSamples = newEnd;
            audio.Metrics["loop.ui.fraction"] = state.Fraction;

            // Nach Loop-Verschiebung erneut UI-Refresh anstoßen
            RefreshTargetWaveform(audio);
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
                var bpms = audios.Where(a => a != null && a.Bpm > 0).Select(a => (double) a.Bpm).OrderBy(x => x).ToArray();
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
