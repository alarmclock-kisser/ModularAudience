using System;
using System.Drawing;
using System.Windows.Forms;

namespace ModularAudience.Forms.Modules.Dialogs
{
    /// <summary>
    /// Remix parameters for the Breakbeat Pattern Editor: derives a variation
    /// from the current note multiselection. Loads persisted values, saves
    /// them back on OK. The bottom checkbox toggles between replacing the
    /// selection and copying the remix to the clipboard.
    /// </summary>
    internal sealed class BreakbeatPatternRemixDialog : Form
    {
        private readonly NumericUpDown numeric_variation = new();
        private readonly NumericUpDown numeric_minPatternLength = new();
        private readonly NumericUpDown numeric_maxPatternLength = new();
        private readonly NumericUpDown numeric_minPatternRepeats = new();
        private readonly NumericUpDown numeric_patternReuse = new();
        private readonly NumericUpDown numeric_mirror = new();
        private readonly NumericUpDown numeric_swapHalves = new();
        private readonly NumericUpDown numeric_rotateChance = new();
        private readonly NumericUpDown numeric_rotateSteps = new();
        private readonly NumericUpDown numeric_laneShiftChance = new();
        private readonly NumericUpDown numeric_laneShiftSteps = new();
        private readonly NumericUpDown numeric_humanizeChance = new();
        private readonly NumericUpDown numeric_humanizeAmount = new();
        private readonly NumericUpDown numeric_ghostChance = new();
        private readonly NumericUpDown numeric_ghostVolume = new();
        private readonly NumericUpDown numeric_rollChance = new();
        private readonly NumericUpDown numeric_rollParts = new();
        private readonly NumericUpDown numeric_mergeChance = new();
        private readonly NumericUpDown numeric_mergeGap = new();
        private readonly NumericUpDown numeric_thin = new();
        private readonly NumericUpDown numeric_trackShuffle = new();
        private readonly NumericUpDown numeric_pitchChance = new();
        private readonly NumericUpDown numeric_pitchRange = new();
        private readonly NumericUpDown numeric_volumeChance = new();
        private readonly NumericUpDown numeric_volumeRange = new();
        private readonly NumericUpDown numeric_seed = new();
        private readonly CheckBox check_noteKinds = new();
        private readonly CheckBox check_copyToClipboard = new();

        public BreakbeatPatternRemixDialog(BreakbeatRemixOptions options)
        {
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.ClientSize = new Size(420, 620);
            this.MinimizeBox = false;
            this.MaximizeBox = false;
            this.ShowInTaskbar = false;
            this.Text = "Remix: Derive Selection";

            this.toolTip = new ToolTip();

            TableLayoutPanel grid = new()
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Padding = new Padding(12, 12, 12, 0)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38f));

            Panel scroll = new()
            {
                Dock = DockStyle.Fill,
                AutoScroll = true
            };
            scroll.Controls.Add(grid);
            this.Controls.Add(scroll);

            int row = 0;
            void AddNumeric(string label, NumericUpDown numeric, decimal value, decimal min, decimal max, decimal increment, int decimalPlaces, string tooltip)
            {
                Label caption = new() { Text = label, AutoSize = true, Anchor = AnchorStyles.Left };
                numeric.Minimum = min;
                numeric.Maximum = max;
                numeric.Increment = increment;
                numeric.DecimalPlaces = decimalPlaces;
                numeric.Value = Math.Clamp(value, min, max);
                numeric.Dock = DockStyle.Fill;
                this.toolTip.SetToolTip(numeric, tooltip);
                this.toolTip.SetToolTip(caption, tooltip);
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                grid.Controls.Add(caption, 0, row);
                grid.Controls.Add(numeric, 1, row);
                row++;
            }

            void AddCheck(string label, CheckBox check, string tooltip)
            {
                check.Text = label;
                check.AutoSize = true;
                check.Anchor = AnchorStyles.Left;
                this.toolTip.SetToolTip(check, tooltip);
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                grid.Controls.Add(check, 0, row);
                grid.SetColumnSpan(check, 2);
                row++;
            }

            AddNumeric("Variation master (%)", this.numeric_variation, options.VariationPercent, 0, 100, 5, 0,
                "Master strength: scales the chance of every operation below.");
            AddNumeric("Min pattern length (steps)", this.numeric_minPatternLength, options.MinPatternLengthSteps, 1, 64, 1, 0,
                "Shortest window used for detecting repeating sub-patterns in the selection.");
            AddNumeric("Max pattern length (steps)", this.numeric_maxPatternLength, options.MaxPatternLengthSteps, 1, 64, 1, 0,
                "Longest window used for detecting repeating sub-patterns in the selection.");
            AddNumeric("Min pattern repeats", this.numeric_minPatternRepeats, options.MinPatternRepeats, 2, 16, 1, 0,
                "How often a sub-pattern must occur before it counts as detected.");
            AddNumeric("Pattern reuse (%)", this.numeric_patternReuse, options.PatternReusePercent, 0, 100, 5, 0,
                "Chance to overwrite every occurrence of the most frequent detected pattern with its first occurrence (combinatorics).");
            AddNumeric("Mirror (%)", this.numeric_mirror, options.MirrorPercent, 0, 100, 5, 0,
                "Chance per lane to mirror its note positions inside the selection bounds.");
            AddNumeric("Swap halves (%)", this.numeric_swapHalves, options.SwapHalvesPercent, 0, 100, 5, 0,
                "Chance per lane to rotate its content by half of the selection width (call and response).");
            AddNumeric("Rotate chance (%)", this.numeric_rotateChance, options.RotateChancePercent, 0, 100, 5, 0,
                "Chance per lane to rotate its notes inside the selection by +/- up to the rotate steps.");
            AddNumeric("Rotate steps (±)", this.numeric_rotateSteps, options.RotateSteps, 1, 16, 1, 0,
                "Maximum rotation amount in grid steps (wraps around the selection width).");
            AddNumeric("Lane shift chance (%)", this.numeric_laneShiftChance, options.LaneShiftChancePercent, 0, 100, 5, 0,
                "Chance per lane to shift all of its notes by +/- up to the lane shift steps (no wrap).");
            AddNumeric("Lane shift steps (±)", this.numeric_laneShiftSteps, options.LaneShiftSteps, 1, 16, 1, 0,
                "Maximum lane shift amount in grid steps.");
            AddNumeric("Humanize chance (%)", this.numeric_humanizeChance, options.HumanizeChancePercent, 0, 100, 5, 0,
                "Chance per note to get a small timing offset (humanize).");
            AddNumeric("Humanize amount (% step)", this.numeric_humanizeAmount, options.HumanizeAmountPercent, 0, 100, 5, 0,
                "Maximum timing offset as a percentage of one grid step.");
            AddNumeric("Ghost chance (%)", this.numeric_ghostChance, options.GhostChancePercent, 0, 100, 5, 0,
                "Chance per note to add a quieter ghost hit right after or one step before it.");
            AddNumeric("Ghost volume (%)", this.numeric_ghostVolume, options.GhostVolumePercent, 0, 250, 5, 0,
                "Volume of ghost hits as a percentage of the source note.");
            AddNumeric("Roll chance (%)", this.numeric_rollChance, options.RollChancePercent, 0, 100, 5, 0,
                "Chance per longer note to split it into a roll / fill (multiple hits across its span).");
            AddNumeric("Roll parts max", this.numeric_rollParts, options.RollPartsMax, 2, 16, 1, 0,
                "Maximum number of hits a roll is split into.");
            AddNumeric("Merge chance (%)", this.numeric_mergeChance, options.MergeChancePercent, 0, 100, 5, 0,
                "Chance to merge two adjacent notes per lane into one longer note.");
            AddNumeric("Merge gap max (steps)", this.numeric_mergeGap, options.MergeGapSteps, 0, 16, 1, 0,
                "Maximum gap in grid steps that may be bridged when merging.");
            AddNumeric("Thin out (%)", this.numeric_thin, options.ThinChancePercent, 0, 100, 5, 0,
                "Chance per note to drop it (never empties a lane completely).");
            AddNumeric("Track shuffle (%)", this.numeric_trackShuffle, options.TrackShufflePercent, 0, 100, 5, 0,
                "Chance per note to move it to another track inside the selection.");
            AddNumeric("Pitch chance (%)", this.numeric_pitchChance, options.PitchChancePercent, 0, 100, 5, 0,
                "Chance per note to remix its pitch (quarter-tone steps).");
            AddNumeric("Pitch range (±st)", this.numeric_pitchRange, (decimal)options.PitchRangeSemitones, 0, 24, 0.25m, 2,
                "Pitch range in semitones for remixed notes.");
            AddNumeric("Volume chance (%)", this.numeric_volumeChance, options.VolumeChancePercent, 0, 100, 5, 0,
                "Chance per note to remix its volume.");
            AddNumeric("Volume range (±%)", this.numeric_volumeRange, (decimal)options.VolumeRangePercent, 0, 100, 5, 0,
                "Volume range in percent for remixed notes (clamped to 0-250%).");
            AddNumeric("Seed (0 = random)", this.numeric_seed, options.Seed, 0, 999999, 1, 0,
                "0 rolls fresh every time, a fixed value makes results reproducible.");

            AddCheck("Allow note kind change (TimeStretch / Varispeed)",
                this.check_noteKinds,
                "On: remixed notes may switch between time-stretch and varispeed playback.");
            this.check_noteKinds.Checked = options.AllowNoteKindChange;

            AddCheck("Copy remix to clipboard (OFF = replace selection)",
                this.check_copyToClipboard,
                "On: the remix replaces the clipboard instead of the selection; paste it later with Ctrl+V.");
            this.check_copyToClipboard.Checked = options.CopyToClipboard;

            FlowLayoutPanel buttons = new()
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(12),
                AutoSize = true
            };
            Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = new Size(75, 25) };
            Button ok = new() { Text = "OK", DialogResult = DialogResult.OK, Size = new Size(75, 25) };
            Button reset = new() { Text = "Defaults", Size = new Size(75, 25) };
            reset.Click += (_, _) => this.ApplyOptions(new BreakbeatRemixOptions());
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            buttons.Controls.Add(reset);
            this.Controls.Add(buttons);

            this.AcceptButton = ok;
            this.CancelButton = cancel;
        }

        private readonly ToolTip toolTip;

        private void ApplyOptions(BreakbeatRemixOptions options)
        {
            options = BreakbeatPatternRemixSettings.Normalize(options);
            this.numeric_variation.Value = options.VariationPercent;
            this.numeric_minPatternLength.Value = options.MinPatternLengthSteps;
            this.numeric_maxPatternLength.Value = options.MaxPatternLengthSteps;
            this.numeric_minPatternRepeats.Value = options.MinPatternRepeats;
            this.numeric_patternReuse.Value = options.PatternReusePercent;
            this.numeric_mirror.Value = options.MirrorPercent;
            this.numeric_swapHalves.Value = options.SwapHalvesPercent;
            this.numeric_rotateChance.Value = options.RotateChancePercent;
            this.numeric_rotateSteps.Value = options.RotateSteps;
            this.numeric_laneShiftChance.Value = options.LaneShiftChancePercent;
            this.numeric_laneShiftSteps.Value = options.LaneShiftSteps;
            this.numeric_humanizeChance.Value = options.HumanizeChancePercent;
            this.numeric_humanizeAmount.Value = options.HumanizeAmountPercent;
            this.numeric_ghostChance.Value = options.GhostChancePercent;
            this.numeric_ghostVolume.Value = options.GhostVolumePercent;
            this.numeric_rollChance.Value = options.RollChancePercent;
            this.numeric_rollParts.Value = options.RollPartsMax;
            this.numeric_mergeChance.Value = options.MergeChancePercent;
            this.numeric_mergeGap.Value = options.MergeGapSteps;
            this.numeric_thin.Value = options.ThinChancePercent;
            this.numeric_trackShuffle.Value = options.TrackShufflePercent;
            this.numeric_pitchChance.Value = options.PitchChancePercent;
            this.numeric_pitchRange.Value = (decimal)options.PitchRangeSemitones;
            this.numeric_volumeChance.Value = options.VolumeChancePercent;
            this.numeric_volumeRange.Value = (decimal)options.VolumeRangePercent;
            this.numeric_seed.Value = options.Seed;
            this.check_noteKinds.Checked = options.AllowNoteKindChange;
            this.check_copyToClipboard.Checked = options.CopyToClipboard;
        }

        public BreakbeatRemixOptions Options => BreakbeatPatternRemixSettings.Normalize(new BreakbeatRemixOptions
        {
            VariationPercent = (int)this.numeric_variation.Value,
            MinPatternLengthSteps = (int)this.numeric_minPatternLength.Value,
            MaxPatternLengthSteps = (int)this.numeric_maxPatternLength.Value,
            MinPatternRepeats = (int)this.numeric_minPatternRepeats.Value,
            PatternReusePercent = (int)this.numeric_patternReuse.Value,
            MirrorPercent = (int)this.numeric_mirror.Value,
            SwapHalvesPercent = (int)this.numeric_swapHalves.Value,
            RotateChancePercent = (int)this.numeric_rotateChance.Value,
            RotateSteps = (int)this.numeric_rotateSteps.Value,
            LaneShiftChancePercent = (int)this.numeric_laneShiftChance.Value,
            LaneShiftSteps = (int)this.numeric_laneShiftSteps.Value,
            HumanizeChancePercent = (int)this.numeric_humanizeChance.Value,
            HumanizeAmountPercent = (int)this.numeric_humanizeAmount.Value,
            GhostChancePercent = (int)this.numeric_ghostChance.Value,
            GhostVolumePercent = (int)this.numeric_ghostVolume.Value,
            RollChancePercent = (int)this.numeric_rollChance.Value,
            RollPartsMax = (int)this.numeric_rollParts.Value,
            MergeChancePercent = (int)this.numeric_mergeChance.Value,
            MergeGapSteps = (int)this.numeric_mergeGap.Value,
            ThinChancePercent = (int)this.numeric_thin.Value,
            TrackShufflePercent = (int)this.numeric_trackShuffle.Value,
            PitchChancePercent = (int)this.numeric_pitchChance.Value,
            PitchRangeSemitones = (float)this.numeric_pitchRange.Value,
            VolumeChancePercent = (int)this.numeric_volumeChance.Value,
            VolumeRangePercent = (float)this.numeric_volumeRange.Value,
            Seed = (int)this.numeric_seed.Value,
            AllowNoteKindChange = this.check_noteKinds.Checked,
            CopyToClipboard = this.check_copyToClipboard.Checked
        });
    }
}
