using System;
using System.Drawing;
using System.Windows.Forms;

namespace ModularAudience.Forms.Modules.Dialogs
{
    /// <summary>
    /// Randomization parameters for Generate in the Breakbeat Pattern Editor.
    /// Loads persisted values, saves them back on OK.
    /// All stretch checkboxes OFF = only plain normal green notes.
    /// </summary>
    internal sealed class BreakbeatPatternGenerateDialog : Form
    {
        private readonly NumericUpDown numeric_density = new();
        private readonly NumericUpDown numeric_amen = new();
        private readonly NumericUpDown numeric_motifLength = new();
        private readonly NumericUpDown numeric_motifRepeat = new();
        private readonly NumericUpDown numeric_maxLength = new();
        private readonly NumericUpDown numeric_minLength = new();
        private readonly NumericUpDown numeric_pitchChance = new();
        private readonly NumericUpDown numeric_pitchRange = new();
        private readonly CheckBox check_pitched = new();
        private readonly NumericUpDown numeric_volumeMin = new();
        private readonly NumericUpDown numeric_volumeMax = new();
        private readonly NumericUpDown numeric_seed = new();
        private readonly CheckBox check_timeStretch = new();
        private readonly CheckBox check_varispeed = new();
        private readonly CheckBox check_truncated = new();
        private readonly CheckBox check_interleaved = new();

        public BreakbeatPatternGenerateDialog(BreakbeatGenerateOptions options)
        {
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.ClientSize = new Size(360, 534);
            this.MinimizeBox = false;
            this.MaximizeBox = false;
            this.ShowInTaskbar = false;
            this.Text = "Generate: Random Pattern";

            TableLayoutPanel grid = new()
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                Padding = new Padding(12, 12, 12, 0)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38f));
            this.Controls.Add(grid);

            int row = 0;
            this.toolTip = new ToolTip();
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

            AddNumeric("Density (%)", this.numeric_density, options.DensityPercent, 0, 100, 5, 0,
                "Probability per enclosed cell to place a note.");
            AddNumeric("Amen break factor (%)", this.numeric_amen, options.AmenFactorPercent, 0, 100, 5, 0,
                "Share of placements on offbeat/syncopated positions (odd steps).");
            AddNumeric("Sub-pattern length (steps)", this.numeric_motifLength, options.MotifLengthSteps, 1, 64, 1, 0,
                "Motif length in grid steps for repeats across the selection width.");
            AddNumeric("Sub-pattern repeat (%)", this.numeric_motifRepeat, options.MotifRepeatPercent, 0, 100, 5, 0,
                "Chance to repeat the first motif instead of rolling a new one.");
            AddNumeric("Min note length (steps)", this.numeric_minLength, options.MinNoteLengthSteps, 1, 16, 1, 0,
                "Minimum note length in grid steps (1 = short hits allowed too).");
            AddNumeric("Max note length (steps)", this.numeric_maxLength, options.MaxNoteLengthSteps, 1, 16, 1, 0,
                "Maximum note length in grid steps (1 = short hits only).");
            AddNumeric("Pitch chance (%)", this.numeric_pitchChance, options.PitchChancePercent, 0, 100, 5, 0,
                "Probability that a generated note gets pitched.");
            AddNumeric("Pitch range (±st)", this.numeric_pitchRange, (decimal)options.PitchRangeSemitones, 0, 12, 0.25m, 2,
                "Pitch range in semitones (quarter-tone steps).");
            AddNumeric("Volume min (%)", this.numeric_volumeMin, (decimal)options.VolumeMinPercent, 0, 250, 5, 0,
                "Minimum volume of generated notes.");
            AddNumeric("Volume max (%)", this.numeric_volumeMax, (decimal)options.VolumeMaxPercent, 0, 250, 5, 0,
                "Maximum volume of generated notes.");
            AddNumeric("Seed (0 = random)", this.numeric_seed, options.Seed, 0, 999999, 1, 0,
                "0 rolls fresh every time, a fixed value makes results reproducible.");

            GroupBox stretchGroup = new()
            {
                Text = "Note kinds (all OFF = only plain green notes)",
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(8, 4, 8, 8)
            };
            FlowLayoutPanel stretchFlow = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
            this.check_timeStretch.Text = "Time-stretched (blue)";
            this.check_timeStretch.AutoSize = true;
            this.check_timeStretch.Checked = options.AllowTimeStretch;
            this.check_varispeed.Text = "Time-pitch-stretched / varispeed (yellow)";
            this.check_varispeed.AutoSize = true;
            this.check_varispeed.Checked = options.AllowVarispeed;
            this.check_truncated.Text = "Truncated / hard-cut (short green)";
            this.check_truncated.AutoSize = true;
            this.check_truncated.Checked = options.AllowTruncated;
            this.check_pitched.Text = "Pitched (random shift+wheel style)";
            this.check_pitched.AutoSize = true;
            this.check_pitched.Checked = options.AllowPitchedNotes;
            this.toolTip.SetToolTip(this.check_pitched,
                "On: generated notes get random pitch (chance + range above apply).");
            stretchFlow.Controls.Add(this.check_timeStretch);
            stretchFlow.Controls.Add(this.check_varispeed);
            stretchFlow.Controls.Add(this.check_truncated);
            stretchFlow.Controls.Add(this.check_pitched);
            this.check_interleaved.Text = "Interleaved (only one track sounds at a time)";
            this.check_interleaved.AutoSize = true;
            this.check_interleaved.Checked = options.Interleaved;
            this.toolTip.SetToolTip(this.check_interleaved,
                "On: generated notes never overlap in time across tracks.");
            stretchFlow.Controls.Add(this.check_interleaved);
            stretchGroup.Controls.Add(stretchFlow);
            this.Controls.Add(stretchGroup);
            stretchGroup.BringToFront();

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
            reset.Click += (_, _) => this.ApplyOptions(new BreakbeatGenerateOptions());
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            buttons.Controls.Add(reset);
            this.Controls.Add(buttons);

            this.AcceptButton = ok;
            this.CancelButton = cancel;
        }

        private readonly ToolTip toolTip;

        private void ApplyOptions(BreakbeatGenerateOptions options)
        {
            options = BreakbeatPatternGenerateSettings.Normalize(options);
            this.numeric_density.Value = options.DensityPercent;
            this.numeric_amen.Value = options.AmenFactorPercent;
            this.numeric_motifLength.Value = options.MotifLengthSteps;
            this.numeric_motifRepeat.Value = options.MotifRepeatPercent;
            this.numeric_maxLength.Value = options.MaxNoteLengthSteps;
            this.numeric_minLength.Value = options.MinNoteLengthSteps;
            this.numeric_pitchChance.Value = options.PitchChancePercent;
            this.numeric_pitchRange.Value = (decimal)options.PitchRangeSemitones;
            this.check_pitched.Checked = options.AllowPitchedNotes;
            this.numeric_volumeMin.Value = (decimal)options.VolumeMinPercent;
            this.numeric_volumeMax.Value = (decimal)options.VolumeMaxPercent;
            this.numeric_seed.Value = options.Seed;
            this.check_timeStretch.Checked = options.AllowTimeStretch;
            this.check_varispeed.Checked = options.AllowVarispeed;
            this.check_truncated.Checked = options.AllowTruncated;
            this.check_interleaved.Checked = options.Interleaved;
        }

        public BreakbeatGenerateOptions Options => BreakbeatPatternGenerateSettings.Normalize(new BreakbeatGenerateOptions
        {
            DensityPercent = (int)this.numeric_density.Value,
            AmenFactorPercent = (int)this.numeric_amen.Value,
            MotifLengthSteps = (int)this.numeric_motifLength.Value,
            MotifRepeatPercent = (int)this.numeric_motifRepeat.Value,
            MaxNoteLengthSteps = (int)this.numeric_maxLength.Value,
            MinNoteLengthSteps = (int)this.numeric_minLength.Value,
            PitchChancePercent = (int)this.numeric_pitchChance.Value,
            PitchRangeSemitones = (float)this.numeric_pitchRange.Value,
            AllowPitchedNotes = this.check_pitched.Checked,
            VolumeMinPercent = (float)this.numeric_volumeMin.Value,
            VolumeMaxPercent = (float)this.numeric_volumeMax.Value,
            AllowTimeStretch = this.check_timeStretch.Checked,
            AllowVarispeed = this.check_varispeed.Checked,
            AllowTruncated = this.check_truncated.Checked,
            Interleaved = this.check_interleaved.Checked,
            Seed = (int)this.numeric_seed.Value
        });
    }
}
