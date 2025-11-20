using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace ModularAudience.Forms.Modules
{
    public partial class TrackViewSettings : Form
    {
        private readonly TrackView Track;

        internal Color ColorWave => this.button_colorWave.BackColor;
        internal Color ColorBack => this.button_colorBack.BackColor;
        internal Color ColorCaret => this.button_colorCaret.BackColor;
        internal Color ColorSelection => this.button_colorSelection.BackColor;
        internal bool SmoothWaveform => this.checkBox_smoothen.Checked;
        internal bool DrawChannelsSeparately => this.checkBox_drawEachChannel.Checked;
        internal bool ShowTimeMarkers => this.checkBox_timeMarkers.Checked;
        internal double TimeMarkersInterval => (double) this.numericUpDown_timeMarkers.Value;
        internal int CaretWidth => (int) this.numericUpDown_caretWidth.Value;
        internal float CaretPosition => (float) (this.hScrollBar_caretPosition.Value - this.hScrollBar_caretPosition.Maximum / 2) / (this.hScrollBar_caretPosition.Maximum / 2);
        internal float FrameRate => (float) this.numericUpDown_frameRate.Value;
        internal bool HueEnabled => this.checkBox_hue.Checked;
        internal float HueIncrement => (float) this.numericUpDown_hue.Value;



        public TrackViewSettings(TrackView track)
        {
            this.InitializeComponent();
            this.Track = track;

            this.numericUpDown_frameRate.Value = (decimal) WindowsScreenHelper.GetScreenRefreshRate();
        }



        private void button_colorWave_Click(object sender, EventArgs e)
        {
            using ColorDialog colorDialog = new();
            if (colorDialog.ShowDialog() == DialogResult.OK)
            {
                this.button_colorWave.BackColor = colorDialog.Color;
                this.button_colorWave.ForeColor = this.ColorWave.GetBrightness() < 0.6f ? Color.White : Color.Black;
            }
        }

        private void button_colorBack_Click(object sender, EventArgs e)
        {
            // If Right AND NOT left click, toggle to negative color
            if (Control.MouseButtons.HasFlag(MouseButtons.Right) && !Control.MouseButtons.HasFlag(MouseButtons.Left))
            {
                this.BackColor = this.GetNegativeColor(this.BackColor);
            }
            else
            {
                using ColorDialog colorDialog = new();
                if (colorDialog.ShowDialog() == DialogResult.OK)
                {
                    this.button_colorBack.BackColor = colorDialog.Color;
                }
            }

            this.button_colorBack.ForeColor = this.ColorBack.GetBrightness() < 0.6f ? Color.White : Color.Black;
        }

        private void button_colorCaret_Click(object sender, EventArgs e)
        {
            using ColorDialog colorDialog = new();
            if (colorDialog.ShowDialog() == DialogResult.OK)
            {
                this.button_colorCaret.BackColor = colorDialog.Color;
            }
        }

        private void button_colorSelection_Click(object sender, EventArgs e)
        {

            using ColorDialog colorDialog = new();
            if (colorDialog.ShowDialog() == DialogResult.OK)
            {
                // Set to dialog color but with alpha 40%
                this.button_colorSelection.BackColor = Color.FromArgb(96, colorDialog.Color);
                this.button_colorSelection.ForeColor = this.ColorSelection.GetBrightness() < 0.6f ? Color.White : Color.Black;
            }

        }





        // Helpers
        private Color GetNegativeColor(Color backColor)
        {
            return Color.FromArgb(backColor.A, 255 - backColor.R, 255 - backColor.G, 255 - backColor.B);
        }
    }
}
