using ModularAudience.Audio;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace ModularAudience.Forms.Modules
{
    public partial class LoopControl : Form
    {
        private sealed class PlaylistTargetItem
        {
            public required AudioObj Audio { get; init; }
            public required string DisplayText { get; init; }

            public override string ToString() => this.DisplayText;
        }

        private AudioCollectionView? CollectionView = null;
        private readonly HashSet<Guid> selectedPlaylistTrackIds = [];
        private readonly HashSet<Guid> knownPlaylistTrackIds = [];
        private readonly System.Windows.Forms.Timer playlistTargetsTimer = new() { Interval = 250 };
        private bool suppressPlaylistChecklistEvents;

        private TrackView? CurrentTrackView => WindowMain.LastSelectedTrackView;
        private AudioObj? SelectedTrackAudio => this.CurrentTrackView?.OriginalAudio;
        private IReadOnlyList<AudioObj> PlaylistAudios => WindowMain.Instance?.GetActivePlaylistAudios() ?? [];
        private IReadOnlyList<AudioObj> SelectedPlaylistAudios => this.checkedListBox_playlistTracks.CheckedItems
            .OfType<PlaylistTargetItem>()
            .Select(item => item.Audio)
            .Where(audio => audio != null)
            .DistinctBy(audio => audio.Id)
            .ToList();
        private IReadOnlyList<AudioObj> TargetAudios => (this.SelectedTrackAudio != null
                ? new[] { this.SelectedTrackAudio }
                : Enumerable.Empty<AudioObj?>())
            .Concat(this.SelectedPlaylistAudios)
            .Where(audio => audio != null)
            .Cast<AudioObj>()
            .DistinctBy(audio => audio.Id)
            .ToList();
        private AudioObj? PlaylistAudio => this.SelectedPlaylistAudios.FirstOrDefault() ?? this.PlaylistAudios.FirstOrDefault();
        private AudioObj? OriginalAudio => this.TargetAudios.FirstOrDefault() ?? this.PlaylistAudio;
        private float Bpm => this.OriginalAudio?.Bpm > 0 ? this.OriginalAudio.Bpm : this.OriginalAudio?.ScannedBpm > 0 ? this.OriginalAudio.ScannedBpm : 120f;
        private int SampleRangePerBeat => (this.OriginalAudio != null ? (int) (this.OriginalAudio.SampleRate * 60f / this.Bpm * 2f) : 88200) * this.Multiplier;
        private int Multiplier => (int) this.numericUpDown_multiplier.Value;
        private int JumpMs => (int) this.numericUpDown_jump.Value;
        private int JumpSamples => (this.OriginalAudio != null ? (int) (this.OriginalAudio.SampleRate * this.JumpMs / 1000f) : 44100);

		private float CurrentLoopFraction
        {
            get
            {
                // Efficient single lookup (avoids multiple enumerations / First calls)
                var btn = this.panel_buttons.Controls.OfType<Button>().FirstOrDefault(b => b.BackColor == Color.LightBlue);
                if (btn == null)
                {
                    return 0f;
                }

                string tag = btn.Tag?.ToString() ?? "0";
                // Try invariant parse first, fallback to current culture
                if (float.TryParse(tag, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float val))
                {
                    return val;
                }
                if (float.TryParse(tag, out val))
                {
                    return val;
                }
                return 0f;
            }
        }

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

        // Track last applied loop to support relative scaling when switching buttons
        private long lastLoopStartSamples = -1;
        private long lastLoopEndSamples = -1;
        private float lastAppliedLoopFraction = 0f; // value from button (can be negative)
        private bool lastActionWasMultiplierChange = false;
		private float lastJumpMs = 1;



		public LoopControl()
        {
            this.InitializeComponent();

            this.StartPosition = FormStartPosition.Manual;
            this.Location = WindowsScreenHelper.GetCenterStartingPoint(this);
            this.TopMost = true;

            this.BuildLoopControlButtons();
            this.EnableAutoRefocusForContainer(this);
            this.checkedListBox_playlistTracks.ItemCheck += this.checkedListBox_playlistTracks_ItemCheck;
            this.playlistTargetsTimer.Tick += (_, _) => this.RefreshPlaylistTargets();
            this.playlistTargetsTimer.Start();

            this.numericUpDown_jump.Click += this.numericUpDown_jump_Click;

            this.RefreshPlaylistTargets();

			this.UpdateLoopButtonsState();

            this.FormClosing += (s, e) =>
            {
                // Hide instead of close
                WindowMain.LoopControlWindow = null;
                this.playlistTargetsTimer.Stop();
                this.Hide();
            };

        }

        private void checkedListBox_playlistTracks_ItemCheck(object? sender, ItemCheckEventArgs e)
        {
            if (this.suppressPlaylistChecklistEvents)
            {
                return;
            }

            try
            {
                this.BeginInvoke((Action) (() =>
                {
                    this.SyncSelectedPlaylistTrackIdsFromUi();
                    this.UpdateLoopButtonsState();
                }));
            }
            catch
            {
            }
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
            string name = !string.IsNullOrWhiteSpace(audio.OriginalName)
                ? audio.OriginalName
                : !string.IsNullOrWhiteSpace(audio.Name)
                    ? audio.Name
                    : Path.GetFileNameWithoutExtension(audio.FilePath);
            string bpm = audio.Bpm > 0 ? $" [{audio.Bpm:F0}]" : string.Empty;
            string state = audio.PlayerPlaying ? "▶" : audio.Paused ? "||" : "■";
            string shortId = audio.Id.ToString("N")[..6];
            return $"{state} {name}{bpm} · {shortId}";
        }

        private void RefreshPlaylistTargets()
        {
            List<AudioObj> activePlaylistAudios = this.PlaylistAudios
                .Where(audio => audio != null)
                .DistinctBy(audio => audio.Id)
                .ToList();

            HashSet<Guid> activeIds = activePlaylistAudios.Select(audio => audio.Id).ToHashSet();

            foreach (AudioObj audio in activePlaylistAudios.Where(audio => !this.knownPlaylistTrackIds.Contains(audio.Id)))
            {
                this.selectedPlaylistTrackIds.Add(audio.Id);
            }

            this.knownPlaylistTrackIds.Clear();
            this.knownPlaylistTrackIds.UnionWith(activeIds);
            this.selectedPlaylistTrackIds.RemoveWhere(id => !activeIds.Contains(id));

            this.suppressPlaylistChecklistEvents = true;
            try
            {
                var listBox = this.checkedListBox_playlistTracks;

                // Build incremental diff against current items to avoid Items.Clear() flicker on every tick.
                var existing = listBox.Items.OfType<PlaylistTargetItem>().ToList();
                var existingById = existing.ToDictionary(item => item.Audio.Id, item => item);

                // 1) Remove items whose audio is no longer active, in reverse order to keep indices stable.
                for (int i = listBox.Items.Count - 1; i >= 0; i--)
                {
                    if (listBox.Items[i] is PlaylistTargetItem item && !activeIds.Contains(item.Audio.Id))
                    {
                        listBox.Items.RemoveAt(i);
                    }
                }

                // 2) Reconcile order, update display text in-place, insert/append missing.
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
                        if (targetIndex >= listBox.Items.Count)
                        {
                            int addedIdx = listBox.Items.Add(newItem);
                            listBox.SetItemChecked(addedIdx, shouldBeChecked);
                        }
                        else
                        {
                            listBox.Items.Insert(targetIndex, newItem);
                            listBox.SetItemChecked(targetIndex, shouldBeChecked);
                        }
                        continue;
                    }

                    var existingItem = (PlaylistTargetItem) listBox.Items[currentIndex];
                    if (!string.Equals(existingItem.DisplayText, display, StringComparison.Ordinal))
                    {
                        // Update text in-place by replacing the item but only if text actually changed.
                        var refreshed = new PlaylistTargetItem { Audio = audio, DisplayText = display };
                        listBox.Items[currentIndex] = refreshed;
                        listBox.SetItemChecked(currentIndex, shouldBeChecked);
                    }
                    else if (listBox.GetItemChecked(currentIndex) != shouldBeChecked)
                    {
                        listBox.SetItemChecked(currentIndex, shouldBeChecked);
                    }
                }
            }
            finally
            {
                this.suppressPlaylistChecklistEvents = false;
            }

            int selectedCount = this.selectedPlaylistTrackIds.Count;
            int playlistCount = activePlaylistAudios.Count;
            bool hasTrackTarget = this.SelectedTrackAudio != null;
            bool hasPlaylistTarget = selectedCount > 0;

            if (hasTrackTarget && hasPlaylistTarget)
            {
                string trackName = this.SelectedTrackAudio?.OriginalName ?? this.SelectedTrackAudio?.Name ?? "selected";
                this.label_targetMode.Text = $"Target: track + playlist — {trackName} + {selectedCount}/{playlistCount} checked";
            }
            else if (hasTrackTarget)
            {
                string trackName = this.SelectedTrackAudio?.OriginalName ?? this.SelectedTrackAudio?.Name ?? "selected";
                this.label_targetMode.Text = $"Target: selected track — {trackName}";
            }
            else if (playlistCount == 0)
            {
                this.label_targetMode.Text = "Target: no active playlist tracks";
            }
            else
            {
                this.label_targetMode.Text = $"Target: playlist overlap selection — {selectedCount}/{playlistCount} checked";
            }

            bool hasPlaylistEntries = playlistCount > 0;
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
            Button? clickedButton = (Button?) sender;
            if (clickedButton == null)
            {
                return;
            }

            // State before toggle
            bool hadActiveBefore = this.panel_buttons.Controls.OfType<Button>().Any(b => b.BackColor == Color.LightBlue);

            // Toggle clicked
            clickedButton.BackColor = clickedButton.BackColor != Color.LightBlue ? Color.LightBlue : SystemColors.Control;

            // Untoggle all other buttons
            this.UntoggleAllOtherButtons(clickedButton);

            // Determine new state
            bool anyActiveNow = this.panel_buttons.Controls.OfType<Button>().Any(b => b.BackColor == Color.LightBlue);

            // Set loop range accordingly
            this.SetLoopRange(!anyActiveNow, hadActiveBefore && !(ModifierKeys.HasFlag(Keys.Control)));

            // Focus TrackView but also keep this Form front most
            this.CurrentTrackView?.Focus();
            this.BringToFront();
        }

        private void UntoggleAllOtherButtons(Button? sender)
        {
            var buttons = this.panel_buttons.Controls.OfType<Button>().Where(b => b != sender);
            foreach (var button in buttons)
            {
                button.BackColor = SystemColors.Control;
            }
        }

        private void SetLoopRange(bool noButtonSelectedAfterToggle = false, bool hadActiveBefore = false)
        {
            // Guard
            if (this.OriginalAudio == null || this.TargetAudios.Count == 0)
            {
                return;
            }

            float fraction = this.CurrentLoopFraction;

            // If no button is selected after toggle -> disable loop and reset tracking
            if (noButtonSelectedAfterToggle || fraction == 0f)
            {
                foreach (AudioObj targetAudio in this.TargetAudios)
                {
                    targetAudio.UpdateLoopFraction(0, 0, 0, false, true);
                    targetAudio.Metrics["loop.ui.fraction"] = 0f;
                }

                this.lastLoopStartSamples = -1;
                this.lastLoopEndSamples = -1;
                this.lastAppliedLoopFraction = 0f;

                return;
            }

            static int SignEps(float x)
            {
                const float eps = 1e-6f;
                return x > eps ? 1 : (x < -eps ? -1 : 0);
            }

            try
            {
                int channels = Math.Max(1, this.OriginalAudio.Channels);

                long framesPerBeat = Math.Max(1, this.SampleRangePerBeat);
                long totalFrames = Math.Max(0L, this.OriginalAudio.Length / Math.Max(1, channels));
                long totalSamples = Math.Max(0L, this.OriginalAudio.Length);

                // Capture current position and previous loop bounds before any change
                long currentSamplesBefore = this.OriginalAudio.Position * Math.Max(1, this.OriginalAudio.Channels);
                long prevStartSamples = this.lastLoopStartSamples;
                long prevEndSamples = this.lastLoopEndSamples;

                bool havePrevLoop = prevStartSamples >= 0 && prevEndSamples > prevStartSamples;

                int signNow = SignEps(fraction);
                int signPrev = SignEps(this.lastAppliedLoopFraction);
                bool signChanged = hadActiveBefore && havePrevLoop && signNow != 0 && signPrev != 0 && signNow != signPrev;

                bool doRelativeScale =
                    hadActiveBefore &&
                    !this.lastActionWasMultiplierChange &&
                    havePrevLoop &&
                    Math.Abs(this.lastAppliedLoopFraction) > 1e-9f;

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
                    double ratio = Math.Abs(fraction) / Math.Max(1e-9, Math.Abs(this.lastAppliedLoopFraction));
                    long newLenSamples = Math.Max(1L, (long) Math.Round(prevLenSamples * ratio));

                    if (fraction >= 0f)
                    {
                        // Normal: anchor at previous start.
                        // If we switched from negative->positive, anchor at previous END (shared boundary).
                        long anchorStartSamples = signChanged ? prevEndSamples : prevStartSamples;

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
                        long anchorEndSamples = signChanged ? prevStartSamples : prevEndSamples;

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
                        long anchorStartSamples = signChanged ? prevEndSamples : prevStartSamples;

                        long desiredEndSamples = anchorStartSamples + targetLenSamples;
                        long clampedEnd = Math.Clamp(desiredEndSamples, anchorStartSamples + 1L, Math.Max(1L, totalSamples));
                        long clampedStart = Math.Clamp(anchorStartSamples, 0L, Math.Max(0L, clampedEnd - 1));

                        startFrame = clampedStart / channels;
                        endFrame = clampedEnd / channels;
                        anchorAtStart = true;
                    }
                    else
                    {
                        long anchorEndSamples = signChanged ? prevStartSamples : prevEndSamples;

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
                    long currentFrame = this.OriginalAudio.Position;

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
                foreach (AudioObj targetAudio in this.TargetAudios)
                {
                    targetAudio.UpdateLoopFraction(baseStartSamples, baseEndSamples, fractionSamples, true, (!insideNewLoop) || forceJump);

                    // Force exact position if needed (prevents weird "same offset" feel)
                    if (insideNewLoop || forceJump)
                    {
                        try { targetAudio.JumpToSamples(desiredSamples); } catch { }
                    }

                    try
                    {
                        targetAudio.Metrics["loop.ui.fraction"] = fraction;
                    }
                    catch { }
                }

                // Track last applied loop and fraction for future relative scaling
                this.lastLoopStartSamples = baseStartSamples;
                this.lastLoopEndSamples = baseEndSamples;
                this.lastAppliedLoopFraction = fraction;
            }
            catch
            {
                // bewusst geschlickt, wie bisher, um keine UI-Glitches zu erzeugen
            }
        }




        internal void UpdateLoopButtonsState()
        {
            // Guard
            if (this.OriginalAudio == null || this.TargetAudios.Count == 0)
            {
                // Disable all buttons
                foreach (var btn in this.panel_buttons.Controls.OfType<Button>())
                {
                    btn.Enabled = false;
                }
                return;
            }

            this.numericUpDown_jump.ValueChanged -= this.numericUpDown_jump_ValueChanged;
            this.numericUpDown_jump.Value = (decimal) (60000f / this.Bpm / 4);
            this.lastJumpMs = (float) this.numericUpDown_jump.Value;
            this.numericUpDown_jump.ValueChanged += this.numericUpDown_jump_ValueChanged;

            // Enable all buttons
            foreach (var btn in this.panel_buttons.Controls.OfType<Button>())
            {
                btn.Enabled = true;
            }

            float? GetPersistedUiFraction()
            {
                try
                {
                    if (this.OriginalAudio.Metrics != null &&
                        this.OriginalAudio.Metrics.TryGetValue("loop.ui.fraction", out var raw))
                    {
                        if (raw is double d)
                        {
                            return (float) d;
                        }
                    }
                }
                catch { }
                return null;
            }

            // Verwende persistenten UI-Fraction-Wert, sonst LoopFraction
            float targetFraction = GetPersistedUiFraction() ?? this.OriginalAudio.LoopFraction;

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
            if (this.OriginalAudio == null || !this.panel_buttons.Controls.OfType<Button>().Any(b => b.BackColor == Color.LightBlue))
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
                float lf = this.OriginalAudio.LoopFraction;
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

            long? startSample = this.lastLoopStartSamples >= 0 ? this.lastLoopStartSamples : null;
            long? endSample = this.lastLoopEndSamples > this.lastLoopStartSamples ? this.lastLoopEndSamples : null;

            var copiedLoop = await this.OriginalAudio.CreateLoopAsync(startSample, endSample);

            if (copiedLoop != null)
            {
                // Override the name to match UI label (fix)
                double loopStartTime = 0.0;
                if (startSample.HasValue)
                {
                    loopStartTime = (double) startSample.Value
                                    / Math.Max(1, this.OriginalAudio.SampleRate)
                                    / Math.Max(1, this.OriginalAudio.Channels);
                }

                string uiLabel = GetUiFractionLabel();
                copiedLoop.Rename($"{this.OriginalAudio.OriginalName} (Looped {uiLabel} at {loopStartTime:F1}s)");

                if (this.CollectionView == null)
                {
                    this.CollectionView = new([]);
                    this.CollectionView.Rename("Loops - '" + this.OriginalAudio.OriginalName + "'");
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
            if (ctrl == null)
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
                        try { this.CurrentTrackView?.Focus(); } catch { }
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
                        this.AttachAutoRefocusToControl(c);

                        if (c.HasChildren)
                        {
                            // Rekursiv auf Unter-Container anwenden
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
            if (this.panel_buttons.Controls.OfType<Button>().Any(b => b.BackColor == Color.LightBlue))
            {
                this.lastActionWasMultiplierChange = true;
                this.SetLoopRange(false, true);
                this.lastActionWasMultiplierChange = false;
            }
        }

        private void button_backward_Click(object sender, EventArgs e)
        {
			// Jump backwards by JumpSamples and Update loop accordingly
			if (this.OriginalAudio == null)
            {
                return;
            }
            
            this.JumpByMilliseconds(-1);
		}

		private void button_forward_Click(object sender, EventArgs e)
        {
			// Jump forwards by JumpSamples and Update loop accordingly
            if (this.OriginalAudio == null)
            {
                return;
            }
           
            this.JumpByMilliseconds(1);
		}

		private void numericUpDown_jump_ValueChanged(object? sender, EventArgs e)
        {
            if (!ModifierKeys.HasFlag(Keys.Shift) && !ModifierKeys.HasFlag(Keys.Control))
            {
                if ((float) this.numericUpDown_jump.Value > this.lastJumpMs)
                {
                    this.lastJumpMs *= 2;
                    this.lastJumpMs = (float) Math.Clamp((decimal) this.lastJumpMs, 1m, this.numericUpDown_jump.Maximum);
                    this.numericUpDown_jump.Value = (decimal) this.lastJumpMs;
                }
                else if ((float) this.numericUpDown_jump.Value < this.lastJumpMs)
                {
                    this.lastJumpMs = Math.Max(1, this.lastJumpMs / 2);
                    this.numericUpDown_jump.Value = (decimal) this.lastJumpMs;
                }
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
        }




		private void JumpByMilliseconds(int direction)
		{
            if (this.OriginalAudio == null || this.TargetAudios.Count == 0)
			{
				return;
			}

			int channels = Math.Max(1, this.OriginalAudio.Channels);
			long totalSamples = Math.Max(0L, this.OriginalAudio.Length);

			// JumpSamples ist in Frames gerechnet (SampleRate * ms / 1000)
			long deltaFrames = this.JumpSamples * direction;
			long currentSamples = this.OriginalAudio.Position * channels;
			long deltaSamples = deltaFrames * channels;

			long targetSamples = currentSamples + deltaSamples;

			// Clamp innerhalb des Files
			targetSamples = Math.Clamp(targetSamples, 0L, Math.Max(0L, totalSamples - 1));

			// Playhead springen (immer!)
            foreach (AudioObj targetAudio in this.TargetAudios)
            {
                try
                {
                    targetAudio.JumpToSamples(targetSamples);
                }
                catch
                {
                    // Ignorieren, kein UI-Crash
                }
            }

			// UI sofort aktualisieren (Caret/Waveform neu rendern)
			try { this.CurrentTrackView?.RequestWaveformRender(); } catch { }

			// Wenn es keinen aktiven Loop gibt, sind wir fertig
			bool haveLoop = this.lastLoopStartSamples >= 0 &&
							this.lastLoopEndSamples > this.lastLoopStartSamples &&
							this.OriginalAudio.LoopFraction != 0;

			if (!haveLoop)
			{
				return;
			}

			// Aktiven Loop um dieselbe Distanz verschieben (Start & End)
			long len = this.lastLoopEndSamples - this.lastLoopStartSamples;
			if (len <= 0)
			{
				return;
			}

			long newStart = this.lastLoopStartSamples + deltaSamples;
			long newEnd = this.lastLoopEndSamples + deltaSamples;

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
            foreach (AudioObj targetAudio in this.TargetAudios)
            {
                targetAudio.UpdateLoopFraction(newStart, newEnd, fractionSamples, true, true);
            }

			// State im LoopControl aktualisieren
			this.lastLoopStartSamples = newStart;
			this.lastLoopEndSamples = newEnd;

            foreach (AudioObj targetAudio in this.TargetAudios)
            {
                try
                {
                    // Fraction bleibt gleich, wir verschieben nur räumlich
                    targetAudio.Metrics["loop.ui.fraction"] = this.lastAppliedLoopFraction;
                }
                catch
                {
                }
            }

			// Nach Loop-Verschiebung erneut UI-Refresh anstoßen
			try { this.CurrentTrackView?.RequestWaveformRender(); } catch { }
		}

	}
}
