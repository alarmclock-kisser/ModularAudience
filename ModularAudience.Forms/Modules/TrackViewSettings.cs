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
        internal bool StrobeEnabled { get; private set; }
        internal Color HueColor { get; private set; } = Color.FromArgb(255, 255, 0, 0);
        internal float StoredHueValue { get; private set; } = 1.75f;
        internal float HueAdjustment { get; private set; }
        internal float DefaultHueAdjustment { get; } = 0.0f;
        internal float StrobeHueAdjustment { get; } = 155.77f;

        public TrackViewSettings(TrackView trackView)
        {
            this.InitializeComponent();
            this.StartPosition = FormStartPosition.Manual;
            this.Track = trackView;
            this.numericUpDown_frameRate.Value = (decimal) WindowsScreenHelper.GetScreenRefreshRate();
            this.InitializeHandlers();
            this.button_strobe.Text = "⚡";
            this.UpdateCaretPositionLabel();
            this.button_colorWave.ForeColor = this.ColorWave.GetBrightness() < 0.6f ? Color.White : Color.Black;
            this.button_colorBack.ForeColor = this.ColorBack.GetBrightness() < 0.6f ? Color.White : Color.Black;
            this.button_colorSelection.ForeColor = this.ColorSelection.GetBrightness() < 0.6f ? Color.White : Color.Black;
        }

        internal Color ResolveWaveColor()
        {
            if (!this.HueEnabled)
            {
                return this.ColorWave;
            }

            float increment = Math.Abs(this.HueAdjustment) > 0.0001f ? this.HueAdjustment : this.StoredHueValue;
            Color color = this.GetNextHue(increment, updateHueColor: true);
            this.button_colorWave.BackColor = color;
            this.button_colorWave.ForeColor = color.GetBrightness() < 0.6f ? Color.White : Color.Black;
            return color;
        }

        internal Color GetShadedColor(Color color, float factor = 0.67f)
        {
            factor = Math.Clamp(factor, 0.0f, 1.0f);
            return Color.FromArgb(
                color.A,
                (int) (color.R * factor),
                (int) (color.G * factor),
                (int) (color.B * factor));
        }

        private void InitializeHandlers()
        {
            this.Hide();
            this.FormClosing += this.TrackViewSettings_FormClosing;
            this.VisibleChanged += this.TrackViewSettings_VisibleChanged;

            this.button_colorWave.Click += this.button_colorWave_Click;
            this.button_colorBack.Click += this.button_colorBack_Click;
            this.button_colorBack.MouseDown += this.button_colorBack_MouseDown;
            this.button_colorCaret.Click += this.button_colorCaret_Click;
            this.button_colorSelection.Click += this.button_colorSelection_Click;
            this.checkBox_smoothen.CheckedChanged += (_, __) => this.NotifyTrackChanged();
            this.checkBox_drawEachChannel.CheckedChanged += (_, __) => this.NotifyTrackChanged();
            this.checkBox_timeMarkers.CheckedChanged += (_, __) => this.NotifyTrackChanged();
            this.numericUpDown_timeMarkers.ValueChanged += (_, __) => this.NotifyTrackChanged();
            this.numericUpDown_caretWidth.ValueChanged += (_, __) => this.NotifyTrackChanged();
            this.numericUpDown_frameRate.ValueChanged += (_, __) => this.NotifyTrackChanged();
            this.hScrollBar_caretPosition.Scroll += (_, __) => { this.UpdateCaretPositionLabel(); this.NotifyTrackChanged(); };
            this.checkBox_hue.CheckedChanged += this.checkBox_hue_CheckedChanged;
            this.button_strobe.Click += this.button_strobe_Click;
            this.numericUpDown_hue.ValueChanged += this.numericUpDown_hue_ValueChanged;
        }

        private void NotifyTrackChanged()
        {
            this.Track.HandleSettingsChanged();
        }

        private void UpdateCaretPositionLabel()
        {
            float normalized = (this.CaretPosition + 1f) * 50f;
            this.label_info_caretPosition.Text = $"Caret Position: {normalized:F1}%";
        }

        private void button_colorWave_Click(object? sender, EventArgs e)
        {
            using ColorDialog colorDialog = new()
            {
                AllowFullOpen = true,
                AnyColor = true,
                FullOpen = true,
                Color = this.ColorWave
            };

            if (colorDialog.ShowDialog(this) == DialogResult.OK)
            {
                this.button_colorWave.BackColor = colorDialog.Color;
                this.HueColor = colorDialog.Color;
                this.button_colorWave.ForeColor = this.ColorWave.GetBrightness() < 0.6f ? Color.White : Color.Black;
                this.NotifyTrackChanged();
            }
        }

        private void button_colorBack_Click(object? sender, EventArgs e)
        {
            using ColorDialog colorDialog = new()
            {
                AllowFullOpen = true,
                AnyColor = true,
                FullOpen = true,
                Color = this.ColorBack,
            };

            if (colorDialog.ShowDialog(this) == DialogResult.OK)
            {
                this.button_colorBack.BackColor = colorDialog.Color;
                this.button_colorBack.ForeColor = this.ColorBack.GetBrightness() < 0.6f ? Color.White : Color.Black;
                this.Track.ApplySettingsAppearance();
                this.NotifyTrackChanged();
            }
        }

        private void button_colorBack_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                this.button_colorBack.BackColor = GetNegativeColor(this.button_colorBack.BackColor);
                this.button_colorBack.ForeColor = this.ColorBack.GetBrightness() < 0.6f ? Color.White : Color.Black;
                this.Track.ApplySettingsAppearance();
                this.NotifyTrackChanged();
            }
        }

        private void button_colorCaret_Click(object? sender, EventArgs e)
        {
            using ColorDialog colorDialog = new()
            {
                AllowFullOpen = true,
                AnyColor = true,
                FullOpen = true,
                Color = this.ColorCaret,
            };

            if (colorDialog.ShowDialog() == DialogResult.OK)
            {
                this.button_colorCaret.BackColor = colorDialog.Color;
                this.NotifyTrackChanged();
            }
        }

        private void button_colorSelection_Click(object? sender, EventArgs e)
        {
            using ColorDialog colorDialog = new()
            {
                AllowFullOpen = true,
                AnyColor = true,
                FullOpen = true,
                Color = this.ColorSelection
            };

            if (colorDialog.ShowDialog() == DialogResult.OK)
            {
                this.button_colorSelection.BackColor = Color.FromArgb(96, colorDialog.Color);
                this.button_colorSelection.ForeColor = this.ColorSelection.GetBrightness() < 0.6f ? Color.White : Color.Black;
                this.NotifyTrackChanged();
            }
        }

        private void checkBox_hue_CheckedChanged(object? sender, EventArgs e)
        {
            if (this.checkBox_hue.Checked)
            {
                this.numericUpDown_hue.Enabled = !this.StrobeEnabled;
                if (this.numericUpDown_hue.Value <= 0)
                {
                    this.numericUpDown_hue.Value = 1.75m;
                }
                this.StoredHueValue = (float) this.numericUpDown_hue.Value;

                if (this.StrobeEnabled)
                {
                    this.HueAdjustment = this.StrobeHueAdjustment;
                    this.numericUpDown_hue.Enabled = false;
                }
                else
                {
                    this.HueAdjustment = this.DefaultHueAdjustment;
                    this.numericUpDown_hue.Enabled = true;
                }
                this.button_colorWave.BackColor = this.HueColor;
            }
            else
            {
                this.button_strobe.ForeColor = Color.Black;
                this.numericUpDown_hue.Enabled = false;
                this.StoredHueValue = 0.0f;
                this.HueAdjustment = 0.0f;
                this.button_colorWave.BackColor = this.ColorWave;
            }
            this.button_colorWave.ForeColor = this.button_colorWave.BackColor.GetBrightness() < 0.6f ? Color.White : Color.Black;
            this.NotifyTrackChanged();
        }

        private void button_strobe_Click(object? sender, EventArgs e)
        {
            this.StrobeEnabled = !this.StrobeEnabled;
            if (this.StrobeEnabled)
            {
                this.button_strobe.ForeColor = Color.Red;
                this.button_strobe.Text = "☠️";
                this.checkBox_hue.Checked = true;
                this.HueAdjustment = this.StrobeHueAdjustment;
                this.numericUpDown_hue.Enabled = false;
            }
            else
            {
                this.button_strobe.ForeColor = Color.Black;
                this.button_strobe.Text = "⚡";
                this.HueAdjustment = this.DefaultHueAdjustment;
                this.numericUpDown_hue.Enabled = true;
            }
            this.button_colorWave.BackColor = this.HueColor;
            this.button_colorWave.ForeColor = this.button_colorWave.BackColor.GetBrightness() < 0.6f ? Color.White : Color.Black;
            this.NotifyTrackChanged();
        }

        private void numericUpDown_hue_ValueChanged(object? sender, EventArgs e)
        {
            this.StoredHueValue = (float) this.numericUpDown_hue.Value;
            if (!this.StrobeEnabled && this.HueEnabled)
            {
                this.HueAdjustment = this.DefaultHueAdjustment;
            }
            this.NotifyTrackChanged();
        }

        private Color GetNextHue(float? increment = null, bool updateHueColor = true)
        {
            increment ??= this.HueAdjustment;
            float currentHue = this.HueColor.GetHue();
            float newHue = (currentHue + increment.Value) % 360f;
            if (updateHueColor)
            {
                this.HueColor = ColorFromHSV(newHue, 1.0f, 1.0f);
            }
            return ColorFromHSV(newHue, 1.0f, 1.0f);
        }

        private static Color GetNegativeColor(Color color)
        {
            return Color.FromArgb(color.A, 255 - color.R, 255 - color.G, 255 - color.B);
        }

        private void TrackViewSettings_FormClosing(object? sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }

        private void TrackViewSettings_VisibleChanged(object? sender, EventArgs e)
        {
            this.Track.SyncSettingsCheckbox(this.Visible);
        }

        internal static Color ColorFromHSV(float hue, float saturation, float value)
        {
            int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
            float f = hue / 60 - (float) Math.Floor(hue / 60);
            value = value * 255;
            int v = Convert.ToInt32(value);
            int p = Convert.ToInt32(value * (1 - saturation));
            int q = Convert.ToInt32(value * (1 - f * saturation));
            int t = Convert.ToInt32(value * (1 - (1 - f) * saturation));
            return hi switch
            {
                0 => Color.FromArgb(255, v, t, p),
                1 => Color.FromArgb(255, q, v, p),
                2 => Color.FromArgb(255, p, v, t),
                3 => Color.FromArgb(255, p, q, v),
                4 => Color.FromArgb(255, t, p, v),
                _ => Color.FromArgb(255, v, p, q),
            };
        }
    }
}
