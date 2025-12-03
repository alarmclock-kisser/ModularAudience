namespace ModularAudience.Forms.Modules.Dialogs
{
    partial class PitchShiftDialog
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
            this.listBox_samples = new ListBox();
            this.numericUpDown_range = new NumericUpDown();
            this.label_info_range = new Label();
            this.button_create = new Button();
            this.label_info_step = new Label();
            this.progressBar_processing = new ProgressBar();
            this.checkBox_fftPv = new CheckBox();
            this.domainUpDown_step = new DomainUpDown();
            this.numericUpDown_take = new NumericUpDown();
            this.label_info_take = new Label();
            this.button_shift = new Button();
            this.numericUpDown_semitones = new NumericUpDown();
            this.numericUpDown_percent = new NumericUpDown();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_range).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_take).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_semitones).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_percent).BeginInit();
            this.SuspendLayout();
            // 
            // listBox_samples
            // 
            this.listBox_samples.FormattingEnabled = true;
            this.listBox_samples.Location = new Point(12, 12);
            this.listBox_samples.Name = "listBox_samples";
            this.listBox_samples.Size = new Size(160, 199);
            this.listBox_samples.TabIndex = 0;
            this.listBox_samples.TabStop = false;
            // 
            // numericUpDown_range
            // 
            this.numericUpDown_range.Location = new Point(12, 286);
            this.numericUpDown_range.Maximum = new decimal(new int[] { 128, 0, 0, 0 });
            this.numericUpDown_range.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_range.Name = "numericUpDown_range";
            this.numericUpDown_range.Size = new Size(60, 23);
            this.numericUpDown_range.TabIndex = 1;
            this.numericUpDown_range.TabStop = false;
            this.numericUpDown_range.Value = new decimal(new int[] { 8, 0, 0, 0 });
            this.numericUpDown_range.ValueChanged += this.numericUpDown_range_ValueChanged;
            // 
            // label_info_range
            // 
            this.label_info_range.AutoSize = true;
            this.label_info_range.Location = new Point(12, 253);
            this.label_info_range.Name = "label_info_range";
            this.label_info_range.Size = new Size(47, 30);
            this.label_info_range.TabIndex = 2;
            this.label_info_range.Text = "Keys +-\r\nRange";
            // 
            // button_create
            // 
            this.button_create.BackColor = SystemColors.Info;
            this.button_create.Location = new Point(377, 286);
            this.button_create.Name = "button_create";
            this.button_create.Size = new Size(75, 23);
            this.button_create.TabIndex = 3;
            this.button_create.TabStop = false;
            this.button_create.Text = "Create";
            this.button_create.UseVisualStyleBackColor = false;
            this.button_create.Click += this.button_create_Click;
            // 
            // label_info_step
            // 
            this.label_info_step.AutoSize = true;
            this.label_info_step.Location = new Point(78, 253);
            this.label_info_step.Name = "label_info_step";
            this.label_info_step.Size = new Size(30, 30);
            this.label_info_step.TabIndex = 5;
            this.label_info_step.Text = "Key\r\nStep";
            // 
            // progressBar_processing
            // 
            this.progressBar_processing.Location = new Point(144, 286);
            this.progressBar_processing.Name = "progressBar_processing";
            this.progressBar_processing.Size = new Size(227, 23);
            this.progressBar_processing.TabIndex = 6;
            // 
            // checkBox_fftPv
            // 
            this.checkBox_fftPv.AutoSize = true;
            this.checkBox_fftPv.Checked = true;
            this.checkBox_fftPv.CheckState = CheckState.Checked;
            this.checkBox_fftPv.Location = new Point(377, 261);
            this.checkBox_fftPv.Name = "checkBox_fftPv";
            this.checkBox_fftPv.Size = new Size(64, 19);
            this.checkBox_fftPv.TabIndex = 7;
            this.checkBox_fftPv.TabStop = false;
            this.checkBox_fftPv.Text = "FFT-PV";
            this.checkBox_fftPv.UseVisualStyleBackColor = true;
            // 
            // domainUpDown_step
            // 
            this.domainUpDown_step.Location = new Point(78, 286);
            this.domainUpDown_step.Name = "domainUpDown_step";
            this.domainUpDown_step.Size = new Size(60, 23);
            this.domainUpDown_step.TabIndex = 8;
            this.domainUpDown_step.Text = "domainUpDown1";
            this.domainUpDown_step.SelectedItemChanged += this.domainUpDown_step_SelectedItemChanged;
            // 
            // numericUpDown_take
            // 
            this.numericUpDown_take.Location = new Point(144, 257);
            this.numericUpDown_take.Maximum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_take.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_take.Name = "numericUpDown_take";
            this.numericUpDown_take.Size = new Size(55, 23);
            this.numericUpDown_take.TabIndex = 9;
            this.numericUpDown_take.Value = new decimal(new int[] { 1, 0, 0, 0 });
            // 
            // label_info_take
            // 
            this.label_info_take.AutoSize = true;
            this.label_info_take.Location = new Point(144, 224);
            this.label_info_take.Name = "label_info_take";
            this.label_info_take.Size = new Size(35, 30);
            this.label_info_take.TabIndex = 10;
            this.label_info_take.Text = "Take\r\nEvery";
            // 
            // button_shift
            // 
            this.button_shift.BackColor = SystemColors.Info;
            this.button_shift.Location = new Point(377, 188);
            this.button_shift.Name = "button_shift";
            this.button_shift.Size = new Size(75, 23);
            this.button_shift.TabIndex = 11;
            this.button_shift.TabStop = false;
            this.button_shift.Text = "Shift";
            this.button_shift.UseVisualStyleBackColor = false;
            this.button_shift.Click += this.button_shift_Click;
            // 
            // numericUpDown_semitones
            // 
            this.numericUpDown_semitones.DecimalPlaces = 2;
            this.numericUpDown_semitones.Increment = new decimal(new int[] { 25, 0, 0, 131072 });
            this.numericUpDown_semitones.Location = new Point(178, 188);
            this.numericUpDown_semitones.Maximum = new decimal(new int[] { 32, 0, 0, 0 });
            this.numericUpDown_semitones.Minimum = new decimal(new int[] { 32, 0, 0, int.MinValue });
            this.numericUpDown_semitones.Name = "numericUpDown_semitones";
            this.numericUpDown_semitones.Size = new Size(60, 23);
            this.numericUpDown_semitones.TabIndex = 12;
            this.numericUpDown_semitones.Value = new decimal(new int[] { 2, 0, 0, 0 });
            this.numericUpDown_semitones.ValueChanged += this.numericUpDown_semitones_ValueChanged;
            // 
            // numericUpDown_percent
            // 
            this.numericUpDown_percent.DecimalPlaces = 10;
            this.numericUpDown_percent.Location = new Point(244, 188);
            this.numericUpDown_percent.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
            this.numericUpDown_percent.Minimum = new decimal(new int[] { 10000, 0, 0, int.MinValue });
            this.numericUpDown_percent.Name = "numericUpDown_percent";
            this.numericUpDown_percent.Size = new Size(127, 23);
            this.numericUpDown_percent.TabIndex = 13;
            this.numericUpDown_percent.ValueChanged += this.numericUpDown_percent_ValueChanged;
            // 
            // PitchShiftDialog
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(464, 321);
            this.Controls.Add(this.numericUpDown_percent);
            this.Controls.Add(this.numericUpDown_semitones);
            this.Controls.Add(this.button_shift);
            this.Controls.Add(this.label_info_take);
            this.Controls.Add(this.numericUpDown_take);
            this.Controls.Add(this.domainUpDown_step);
            this.Controls.Add(this.checkBox_fftPv);
            this.Controls.Add(this.progressBar_processing);
            this.Controls.Add(this.label_info_step);
            this.Controls.Add(this.button_create);
            this.Controls.Add(this.label_info_range);
            this.Controls.Add(this.numericUpDown_range);
            this.Controls.Add(this.listBox_samples);
            this.MaximizeBox = false;
            this.MaximumSize = new Size(480, 360);
            this.MinimumSize = new Size(480, 360);
            this.Name = "PitchShiftDialog";
            this.Text = "PitchShiftDialog";
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_range).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_take).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_semitones).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_percent).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private ListBox listBox_samples;
        private NumericUpDown numericUpDown_range;
        private Label label_info_range;
        private Button button_create;
        private Label label_info_step;
        private ProgressBar progressBar_processing;
        private CheckBox checkBox_fftPv;
		private DomainUpDown domainUpDown_step;
		private NumericUpDown numericUpDown_take;
		private Label label_info_take;
        private Button button_shift;
        private NumericUpDown numericUpDown_semitones;
        private NumericUpDown numericUpDown_percent;
    }
}