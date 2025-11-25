namespace ModularAudience.Forms.Modules
{
    partial class TrackViewSettings
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.label_info_caretPosition = new Label();
            this.hScrollBar_caretPosition = new HScrollBar();
            this.checkBox_timeMarkers = new CheckBox();
            this.numericUpDown_timeMarkers = new NumericUpDown();
            this.label_info_caretWidth = new Label();
            this.numericUpDown_caretWidth = new NumericUpDown();
            this.label_info_frameRate = new Label();
            this.numericUpDown_frameRate = new NumericUpDown();
            this.checkBox_smoothen = new CheckBox();
            this.checkBox_drawEachChannel = new CheckBox();
            this.button_colorSelection = new Button();
            this.button_colorCaret = new Button();
            this.label_info_colors = new Label();
            this.button_colorBack = new Button();
            this.button_colorWave = new Button();
            this.button_strobe = new Button();
            this.numericUpDown_hue = new NumericUpDown();
            this.checkBox_hue = new CheckBox();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_timeMarkers).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_caretWidth).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_frameRate).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_hue).BeginInit();
            this.SuspendLayout();
            // 
            // label_info_caretPosition
            // 
            this.label_info_caretPosition.AutoSize = true;
            this.label_info_caretPosition.Location = new Point(12, 158);
            this.label_info_caretPosition.Margin = new Padding(3, 0, 3, 2);
            this.label_info_caretPosition.Name = "label_info_caretPosition";
            this.label_info_caretPosition.Size = new Size(112, 15);
            this.label_info_caretPosition.TabIndex = 40;
            this.label_info_caretPosition.Text = "Caret Position: 0.0%";
            // 
            // hScrollBar_caretPosition
            // 
            this.hScrollBar_caretPosition.Location = new Point(9, 175);
            this.hScrollBar_caretPosition.Maximum = 1000;
            this.hScrollBar_caretPosition.Name = "hScrollBar_caretPosition";
            this.hScrollBar_caretPosition.Size = new Size(171, 17);
            this.hScrollBar_caretPosition.TabIndex = 41;
            this.hScrollBar_caretPosition.Value = 500;
            // 
            // checkBox_timeMarkers
            // 
            this.checkBox_timeMarkers.AutoSize = true;
            this.checkBox_timeMarkers.Location = new Point(12, 104);
            this.checkBox_timeMarkers.Name = "checkBox_timeMarkers";
            this.checkBox_timeMarkers.Size = new Size(101, 19);
            this.checkBox_timeMarkers.TabIndex = 39;
            this.checkBox_timeMarkers.Text = "Time Markers:";
            this.checkBox_timeMarkers.UseVisualStyleBackColor = true;
            // 
            // numericUpDown_timeMarkers
            // 
            this.numericUpDown_timeMarkers.DecimalPlaces = 3;
            this.numericUpDown_timeMarkers.Increment = new decimal(new int[] { 125, 0, 0, 196608 });
            this.numericUpDown_timeMarkers.Location = new Point(119, 103);
            this.numericUpDown_timeMarkers.Maximum = new decimal(new int[] { 90, 0, 0, 0 });
            this.numericUpDown_timeMarkers.Minimum = new decimal(new int[] { 5, 0, 0, 131072 });
            this.numericUpDown_timeMarkers.Name = "numericUpDown_timeMarkers";
            this.numericUpDown_timeMarkers.Size = new Size(61, 23);
            this.numericUpDown_timeMarkers.TabIndex = 38;
            this.numericUpDown_timeMarkers.Value = new decimal(new int[] { 5, 0, 0, 65536 });
            // 
            // label_info_caretWidth
            // 
            this.label_info_caretWidth.AutoSize = true;
            this.label_info_caretWidth.Location = new Point(12, 134);
            this.label_info_caretWidth.Name = "label_info_caretWidth";
            this.label_info_caretWidth.Size = new Size(73, 15);
            this.label_info_caretWidth.TabIndex = 36;
            this.label_info_caretWidth.Text = "Caret Width:";
            // 
            // numericUpDown_caretWidth
            // 
            this.numericUpDown_caretWidth.Location = new Point(130, 132);
            this.numericUpDown_caretWidth.Maximum = new decimal(new int[] { 48, 0, 0, 0 });
            this.numericUpDown_caretWidth.Name = "numericUpDown_caretWidth";
            this.numericUpDown_caretWidth.Size = new Size(50, 23);
            this.numericUpDown_caretWidth.TabIndex = 37;
            this.numericUpDown_caretWidth.Value = new decimal(new int[] { 2, 0, 0, 0 });
            // 
            // label_info_frameRate
            // 
            this.label_info_frameRate.AutoSize = true;
            this.label_info_frameRate.Location = new Point(230, 14);
            this.label_info_frameRate.Name = "label_info_frameRate";
            this.label_info_frameRate.Size = new Size(69, 15);
            this.label_info_frameRate.TabIndex = 34;
            this.label_info_frameRate.Text = "Frame Rate:";
            // 
            // numericUpDown_frameRate
            // 
            this.numericUpDown_frameRate.DecimalPlaces = 2;
            this.numericUpDown_frameRate.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            this.numericUpDown_frameRate.Location = new Point(323, 12);
            this.numericUpDown_frameRate.Maximum = new decimal(new int[] { 144, 0, 0, 0 });
            this.numericUpDown_frameRate.Minimum = new decimal(new int[] { 5, 0, 0, 65536 });
            this.numericUpDown_frameRate.Name = "numericUpDown_frameRate";
            this.numericUpDown_frameRate.Size = new Size(60, 23);
            this.numericUpDown_frameRate.TabIndex = 35;
            this.numericUpDown_frameRate.Value = new decimal(new int[] { 60, 0, 0, 0 });
            // 
            // checkBox_smoothen
            // 
            this.checkBox_smoothen.AutoSize = true;
            this.checkBox_smoothen.Location = new Point(12, 39);
            this.checkBox_smoothen.Name = "checkBox_smoothen";
            this.checkBox_smoothen.Size = new Size(124, 19);
            this.checkBox_smoothen.TabIndex = 33;
            this.checkBox_smoothen.Text = "Smooth waveform";
            this.checkBox_smoothen.UseVisualStyleBackColor = true;
            // 
            // checkBox_drawEachChannel
            // 
            this.checkBox_drawEachChannel.AutoSize = true;
            this.checkBox_drawEachChannel.Location = new Point(12, 64);
            this.checkBox_drawEachChannel.Name = "checkBox_drawEachChannel";
            this.checkBox_drawEachChannel.Size = new Size(128, 19);
            this.checkBox_drawEachChannel.TabIndex = 32;
            this.checkBox_drawEachChannel.Text = "Draw each Channel";
            this.checkBox_drawEachChannel.UseVisualStyleBackColor = true;
            // 
            // button_colorSelection
            // 
            this.button_colorSelection.BackColor = SystemColors.AppWorkspace;
            this.button_colorSelection.Font = new Font("Bahnschrift SemiLight SemiConde", 8.25F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.button_colorSelection.Location = new Point(174, 12);
            this.button_colorSelection.Margin = new Padding(1, 3, 1, 1);
            this.button_colorSelection.Name = "button_colorSelection";
            this.button_colorSelection.Size = new Size(38, 23);
            this.button_colorSelection.TabIndex = 46;
            this.button_colorSelection.Text = "Area";
            this.button_colorSelection.UseVisualStyleBackColor = false;
            this.button_colorSelection.Click += this.button_colorSelection_Click;
            // 
            // button_colorCaret
            // 
            this.button_colorCaret.BackColor = Color.IndianRed;
            this.button_colorCaret.Font = new Font("Bahnschrift SemiLight SemiConde", 8.25F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.button_colorCaret.ForeColor = Color.Black;
            this.button_colorCaret.Location = new Point(54, 12);
            this.button_colorCaret.Margin = new Padding(1, 3, 1, 1);
            this.button_colorCaret.Name = "button_colorCaret";
            this.button_colorCaret.Size = new Size(38, 23);
            this.button_colorCaret.TabIndex = 45;
            this.button_colorCaret.Text = "Caret";
            this.button_colorCaret.UseVisualStyleBackColor = false;
            this.button_colorCaret.Click += this.button_colorCaret_Click;
            // 
            // label_info_colors
            // 
            this.label_info_colors.AutoSize = true;
            this.label_info_colors.Location = new Point(6, 16);
            this.label_info_colors.Name = "label_info_colors";
            this.label_info_colors.Size = new Size(44, 15);
            this.label_info_colors.TabIndex = 42;
            this.label_info_colors.Text = "Colors:";
            // 
            // button_colorBack
            // 
            this.button_colorBack.BackColor = Color.White;
            this.button_colorBack.Font = new Font("Bahnschrift SemiLight SemiConde", 8.25F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.button_colorBack.Location = new Point(134, 12);
            this.button_colorBack.Margin = new Padding(1, 3, 1, 1);
            this.button_colorBack.Name = "button_colorBack";
            this.button_colorBack.Size = new Size(38, 23);
            this.button_colorBack.TabIndex = 44;
            this.button_colorBack.Text = "Back";
            this.button_colorBack.UseVisualStyleBackColor = false;
            this.button_colorBack.Click += this.button_colorBack_Click;
            // 
            // button_colorWave
            // 
            this.button_colorWave.BackColor = SystemColors.ActiveCaption;
            this.button_colorWave.Font = new Font("Bahnschrift SemiLight SemiConde", 8.25F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.button_colorWave.Location = new Point(94, 12);
            this.button_colorWave.Margin = new Padding(1, 3, 1, 1);
            this.button_colorWave.Name = "button_colorWave";
            this.button_colorWave.Size = new Size(38, 23);
            this.button_colorWave.TabIndex = 43;
            this.button_colorWave.Text = "Wave";
            this.button_colorWave.UseVisualStyleBackColor = false;
            this.button_colorWave.Click += this.button_colorWave_Click;
            // 
            // button_strobe
            // 
            this.button_strobe.Font = new Font("Segoe UI", 9.75F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.button_strobe.ForeColor = Color.Black;
            this.button_strobe.Location = new Point(360, 41);
            this.button_strobe.Name = "button_strobe";
            this.button_strobe.Size = new Size(23, 23);
            this.button_strobe.TabIndex = 47;
            this.button_strobe.Text = "🕱";
            this.button_strobe.UseVisualStyleBackColor = true;
            // 
            // numericUpDown_hue
            // 
            this.numericUpDown_hue.DecimalPlaces = 3;
            this.numericUpDown_hue.Increment = new decimal(new int[] { 125, 0, 0, 196608 });
            this.numericUpDown_hue.Location = new Point(284, 42);
            this.numericUpDown_hue.Maximum = new decimal(new int[] { 720, 0, 0, 0 });
            this.numericUpDown_hue.Name = "numericUpDown_hue";
            this.numericUpDown_hue.Size = new Size(70, 23);
            this.numericUpDown_hue.TabIndex = 48;
            this.numericUpDown_hue.Value = new decimal(new int[] { 175, 0, 0, 131072 });
            // 
            // checkBox_hue
            // 
            this.checkBox_hue.AutoSize = true;
            this.checkBox_hue.Location = new Point(230, 43);
            this.checkBox_hue.Name = "checkBox_hue";
            this.checkBox_hue.Size = new Size(48, 19);
            this.checkBox_hue.TabIndex = 49;
            this.checkBox_hue.Text = "Hue";
            this.checkBox_hue.UseVisualStyleBackColor = true;
            // 
            // TrackViewSettings
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(395, 201);
            this.Controls.Add(this.button_strobe);
            this.Controls.Add(this.numericUpDown_hue);
            this.Controls.Add(this.checkBox_hue);
            this.Controls.Add(this.button_colorSelection);
            this.Controls.Add(this.button_colorCaret);
            this.Controls.Add(this.label_info_colors);
            this.Controls.Add(this.button_colorBack);
            this.Controls.Add(this.button_colorWave);
            this.Controls.Add(this.label_info_caretPosition);
            this.Controls.Add(this.hScrollBar_caretPosition);
            this.Controls.Add(this.checkBox_timeMarkers);
            this.Controls.Add(this.numericUpDown_timeMarkers);
            this.Controls.Add(this.label_info_caretWidth);
            this.Controls.Add(this.numericUpDown_caretWidth);
            this.Controls.Add(this.label_info_frameRate);
            this.Controls.Add(this.numericUpDown_frameRate);
            this.Controls.Add(this.checkBox_smoothen);
            this.Controls.Add(this.checkBox_drawEachChannel);
            this.MaximizeBox = false;
            this.MaximumSize = new Size(411, 240);
            this.MinimizeBox = false;
            this.MinimumSize = new Size(411, 240);
            this.Name = "TrackViewSettings";
            this.Text = "TrackViewSettings";
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_timeMarkers).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_caretWidth).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_frameRate).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_hue).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private Label label_info_caretPosition;
        private HScrollBar hScrollBar_caretPosition;
        private CheckBox checkBox_timeMarkers;
        private NumericUpDown numericUpDown_timeMarkers;
        private Label label_info_caretWidth;
        private NumericUpDown numericUpDown_caretWidth;
        private Label label_info_frameRate;
        private NumericUpDown numericUpDown_frameRate;
        private CheckBox checkBox_smoothen;
        private CheckBox checkBox_drawEachChannel;
        private Button button_colorSelection;
        private Button button_colorCaret;
        private Label label_info_colors;
        private Button button_colorBack;
        private Button button_colorWave;
        private Button button_strobe;
        private NumericUpDown numericUpDown_hue;
        private CheckBox checkBox_hue;
    }
}