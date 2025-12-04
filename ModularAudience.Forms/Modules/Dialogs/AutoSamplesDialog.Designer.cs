namespace ModularAudience.Forms.Modules.Dialogs
{
    partial class AutoSamplesDialog
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
            this.button_cut = new Button();
            this.button_cancel = new Button();
            this.progressBar_cutting = new ProgressBar();
            this.numericUpDown_minDuration = new NumericUpDown();
            this.label_info_minDuration = new Label();
            this.label_info_maxDuration = new Label();
            this.numericUpDown_maxDuration = new NumericUpDown();
            this.label_info_silenceDuration = new Label();
            this.numericUpDown_silenceDuration = new NumericUpDown();
            this.label_status = new Label();
            this.checkBox_arguments = new CheckBox();
            this.groupBox_options = new GroupBox();
            this.button_cutV2 = new Button();
            this.button_atomize = new Button();
            this.button_autoCut = new Button();
            this.button_split = new Button();
            this.numericUpDown_fractions = new NumericUpDown();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_minDuration).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_maxDuration).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_silenceDuration).BeginInit();
            this.groupBox_options.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_fractions).BeginInit();
            this.SuspendLayout();
            // 
            // button_cut
            // 
            this.button_cut.BackColor = SystemColors.Info;
            this.button_cut.Location = new Point(257, 166);
            this.button_cut.Name = "button_cut";
            this.button_cut.Size = new Size(75, 23);
            this.button_cut.TabIndex = 0;
            this.button_cut.Text = "Cut";
            this.button_cut.UseVisualStyleBackColor = false;
            this.button_cut.Click += this.button_cut_Click;
            // 
            // button_cancel
            // 
            this.button_cancel.Location = new Point(196, 166);
            this.button_cancel.Name = "button_cancel";
            this.button_cancel.Size = new Size(55, 23);
            this.button_cancel.TabIndex = 1;
            this.button_cancel.Text = "Cancel";
            this.button_cancel.UseVisualStyleBackColor = true;
            this.button_cancel.Click += this.button_cancel_Click;
            // 
            // progressBar_cutting
            // 
            this.progressBar_cutting.Location = new Point(12, 166);
            this.progressBar_cutting.Name = "progressBar_cutting";
            this.progressBar_cutting.Size = new Size(178, 23);
            this.progressBar_cutting.TabIndex = 2;
            // 
            // numericUpDown_minDuration
            // 
            this.numericUpDown_minDuration.Increment = new decimal(new int[] { 5, 0, 0, 0 });
            this.numericUpDown_minDuration.Location = new Point(6, 63);
            this.numericUpDown_minDuration.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            this.numericUpDown_minDuration.Minimum = new decimal(new int[] { 25, 0, 0, 0 });
            this.numericUpDown_minDuration.Name = "numericUpDown_minDuration";
            this.numericUpDown_minDuration.Size = new Size(60, 23);
            this.numericUpDown_minDuration.TabIndex = 3;
            this.numericUpDown_minDuration.Value = new decimal(new int[] { 175, 0, 0, 0 });
            // 
            // label_info_minDuration
            // 
            this.label_info_minDuration.AutoSize = true;
            this.label_info_minDuration.Location = new Point(6, 30);
            this.label_info_minDuration.Name = "label_info_minDuration";
            this.label_info_minDuration.Size = new Size(60, 30);
            this.label_info_minDuration.TabIndex = 4;
            this.label_info_minDuration.Text = "Minimum\r\nDuration";
            // 
            // label_info_maxDuration
            // 
            this.label_info_maxDuration.AutoSize = true;
            this.label_info_maxDuration.Location = new Point(72, 30);
            this.label_info_maxDuration.Name = "label_info_maxDuration";
            this.label_info_maxDuration.Size = new Size(61, 30);
            this.label_info_maxDuration.TabIndex = 6;
            this.label_info_maxDuration.Text = "Maximum\r\nDuration";
            // 
            // numericUpDown_maxDuration
            // 
            this.numericUpDown_maxDuration.Increment = new decimal(new int[] { 10, 0, 0, 0 });
            this.numericUpDown_maxDuration.Location = new Point(72, 63);
            this.numericUpDown_maxDuration.Maximum = new decimal(new int[] { 3600, 0, 0, 0 });
            this.numericUpDown_maxDuration.Minimum = new decimal(new int[] { 50, 0, 0, 0 });
            this.numericUpDown_maxDuration.Name = "numericUpDown_maxDuration";
            this.numericUpDown_maxDuration.Size = new Size(60, 23);
            this.numericUpDown_maxDuration.TabIndex = 5;
            this.numericUpDown_maxDuration.Value = new decimal(new int[] { 175, 0, 0, 0 });
            // 
            // label_info_silenceDuration
            // 
            this.label_info_silenceDuration.AutoSize = true;
            this.label_info_silenceDuration.Location = new Point(138, 30);
            this.label_info_silenceDuration.Name = "label_info_silenceDuration";
            this.label_info_silenceDuration.Size = new Size(53, 30);
            this.label_info_silenceDuration.TabIndex = 8;
            this.label_info_silenceDuration.Text = "Silence\r\nDuration";
            // 
            // numericUpDown_silenceDuration
            // 
            this.numericUpDown_silenceDuration.Increment = new decimal(new int[] { 5, 0, 0, 0 });
            this.numericUpDown_silenceDuration.Location = new Point(138, 63);
            this.numericUpDown_silenceDuration.Maximum = new decimal(new int[] { 2000, 0, 0, 0 });
            this.numericUpDown_silenceDuration.Minimum = new decimal(new int[] { 25, 0, 0, 0 });
            this.numericUpDown_silenceDuration.Name = "numericUpDown_silenceDuration";
            this.numericUpDown_silenceDuration.Size = new Size(60, 23);
            this.numericUpDown_silenceDuration.TabIndex = 7;
            this.numericUpDown_silenceDuration.Value = new decimal(new int[] { 60, 0, 0, 0 });
            // 
            // label_status
            // 
            this.label_status.AutoSize = true;
            this.label_status.Location = new Point(12, 196);
            this.label_status.Name = "label_status";
            this.label_status.Size = new Size(119, 15);
            this.label_status.TabIndex = 9;
            this.label_status.Text = "Ready to cut samples";
            // 
            // checkBox_arguments
            // 
            this.checkBox_arguments.AutoSize = true;
            this.checkBox_arguments.Checked = true;
            this.checkBox_arguments.CheckState = CheckState.Checked;
            this.checkBox_arguments.Location = new Point(12, 12);
            this.checkBox_arguments.Name = "checkBox_arguments";
            this.checkBox_arguments.Size = new Size(106, 19);
            this.checkBox_arguments.TabIndex = 10;
            this.checkBox_arguments.Text = "Enable Options";
            this.checkBox_arguments.UseVisualStyleBackColor = true;
            this.checkBox_arguments.CheckedChanged += this.checkBox_arguments_CheckedChanged;
            // 
            // groupBox_options
            // 
            this.groupBox_options.Controls.Add(this.label_info_silenceDuration);
            this.groupBox_options.Controls.Add(this.numericUpDown_silenceDuration);
            this.groupBox_options.Controls.Add(this.numericUpDown_maxDuration);
            this.groupBox_options.Controls.Add(this.label_info_minDuration);
            this.groupBox_options.Controls.Add(this.label_info_maxDuration);
            this.groupBox_options.Controls.Add(this.numericUpDown_minDuration);
            this.groupBox_options.Location = new Point(12, 37);
            this.groupBox_options.Name = "groupBox_options";
            this.groupBox_options.Size = new Size(204, 92);
            this.groupBox_options.TabIndex = 11;
            this.groupBox_options.TabStop = false;
            // 
            // button_cutV2
            // 
            this.button_cutV2.BackColor = SystemColors.Info;
            this.button_cutV2.Location = new Point(257, 137);
            this.button_cutV2.Name = "button_cutV2";
            this.button_cutV2.Size = new Size(75, 23);
            this.button_cutV2.TabIndex = 12;
            this.button_cutV2.Text = "Cut V2";
            this.button_cutV2.UseVisualStyleBackColor = false;
            this.button_cutV2.Click += this.button_cutV2_Click;
            // 
            // button_atomize
            // 
            this.button_atomize.BackColor = Color.FromArgb(  255,   224,   192);
            this.button_atomize.Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.button_atomize.Location = new Point(222, 12);
            this.button_atomize.Name = "button_atomize";
            this.button_atomize.Size = new Size(110, 23);
            this.button_atomize.TabIndex = 13;
            this.button_atomize.Text = "A T O M I Z E";
            this.button_atomize.UseVisualStyleBackColor = false;
            this.button_atomize.Click += this.button_atomize_Click;
            // 
            // button_autoCut
            // 
            this.button_autoCut.BackColor = SystemColors.Info;
            this.button_autoCut.Location = new Point(257, 108);
            this.button_autoCut.Name = "button_autoCut";
            this.button_autoCut.Size = new Size(75, 23);
            this.button_autoCut.TabIndex = 14;
            this.button_autoCut.Text = "Auto Cut";
            this.button_autoCut.UseVisualStyleBackColor = false;
            this.button_autoCut.Click += this.button_autoCut_Click;
            // 
            // button_split
            // 
            this.button_split.BackColor = SystemColors.Info;
            this.button_split.Location = new Point(12, 137);
            this.button_split.Name = "button_split";
            this.button_split.Size = new Size(75, 23);
            this.button_split.TabIndex = 15;
            this.button_split.Text = "Split";
            this.button_split.UseVisualStyleBackColor = false;
            this.button_split.Click += this.button_split_Click;
            // 
            // numericUpDown_fractions
            // 
            this.numericUpDown_fractions.DecimalPlaces = 4;
            this.numericUpDown_fractions.Location = new Point(93, 137);
            this.numericUpDown_fractions.Maximum = new decimal(new int[] { 256, 0, 0, 0 });
            this.numericUpDown_fractions.Minimum = new decimal(new int[] { 5, 0, 0, 196608 });
            this.numericUpDown_fractions.Name = "numericUpDown_fractions";
            this.numericUpDown_fractions.Size = new Size(60, 23);
            this.numericUpDown_fractions.TabIndex = 9;
            this.numericUpDown_fractions.Value = new decimal(new int[] { 4, 0, 0, 0 });
            this.numericUpDown_fractions.ValueChanged += this.numericUpDown_fractions_ValueChanged;
            // 
            // AutoSamplesDialog
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(344, 221);
            this.Controls.Add(this.numericUpDown_fractions);
            this.Controls.Add(this.button_split);
            this.Controls.Add(this.button_autoCut);
            this.Controls.Add(this.button_atomize);
            this.Controls.Add(this.button_cutV2);
            this.Controls.Add(this.groupBox_options);
            this.Controls.Add(this.checkBox_arguments);
            this.Controls.Add(this.label_status);
            this.Controls.Add(this.progressBar_cutting);
            this.Controls.Add(this.button_cancel);
            this.Controls.Add(this.button_cut);
            this.Name = "AutoSamplesDialog";
            this.Text = "AutoSamplesDialog";
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_minDuration).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_maxDuration).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_silenceDuration).EndInit();
            this.groupBox_options.ResumeLayout(false);
            this.groupBox_options.PerformLayout();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_fractions).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private Button button_cut;
        private Button button_cancel;
        private ProgressBar progressBar_cutting;
        private NumericUpDown numericUpDown_minDuration;
        private Label label_info_minDuration;
        private Label label_info_maxDuration;
        private NumericUpDown numericUpDown_maxDuration;
        private Label label_info_silenceDuration;
        private NumericUpDown numericUpDown_silenceDuration;
        private Label label_status;
        private CheckBox checkBox_arguments;
        private GroupBox groupBox_options;
        private Button button_cutV2;
        private Button button_atomize;
        private Button button_autoCut;
        private Button button_split;
        private NumericUpDown numericUpDown_fractions;
    }
}