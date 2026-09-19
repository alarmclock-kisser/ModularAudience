namespace ModularAudience.Forms
{
    partial class AnalogSeparationDialog
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.panel_bands = new Panel();
            this.flowLayoutPanel_bands = new FlowLayoutPanel();
            this.button_addBand = new Button();
            this.button_autoBands = new Button();
            this.button_fromSelection = new Button();
            this.groupBox_options = new GroupBox();
            this.numeric_windowSize = new NumericUpDown();
            this.label_windowSize = new Label();
            this.numeric_overlap = new NumericUpDown();
            this.label_overlap = new Label();
            this.numeric_sharpness = new NumericUpDown();
            this.label_sharpness = new Label();
            this.numeric_threads = new NumericUpDown();
            this.label_threads = new Label();
            this.checkBox_subtract = new CheckBox();
            this.checkBox_timbreAware = new CheckBox();
            this.checkBox_harmonicAware = new CheckBox();
            this.button_run = new Button();
            this.progressBar_separating = new ProgressBar();
            this.label_status = new Label();
            this.panel_bands.SuspendLayout();
            this.flowLayoutPanel_bands.SuspendLayout();
            this.groupBox_options.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize) this.numeric_windowSize).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numeric_overlap).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numeric_sharpness).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numeric_threads).BeginInit();
            this.SuspendLayout();
            //
            // panel_bands
            //
            this.panel_bands.Controls.Add(this.flowLayoutPanel_bands);
            this.panel_bands.Location = new Point(12, 12);
            this.panel_bands.Name = "panel_bands";
            this.panel_bands.Size = new Size(460, 180);
            this.panel_bands.TabIndex = 0;
            //
            // flowLayoutPanel_bands
            //
            this.flowLayoutPanel_bands.AutoScroll = true;
            this.flowLayoutPanel_bands.FlowDirection = FlowDirection.TopDown;
            this.flowLayoutPanel_bands.Location = new Point(3, 3);
            this.flowLayoutPanel_bands.Name = "flowLayoutPanel_bands";
            this.flowLayoutPanel_bands.Size = new Size(454, 174);
            this.flowLayoutPanel_bands.TabIndex = 0;
            this.flowLayoutPanel_bands.WrapContents = false;
            //
            // button_addBand
            //
            this.button_addBand.Location = new Point(12, 198);
            this.button_addBand.Name = "button_addBand";
            this.button_addBand.Size = new Size(110, 23);
            this.button_addBand.TabIndex = 1;
            this.button_addBand.Text = "+ Add Band";
            this.button_addBand.UseVisualStyleBackColor = true;
            this.button_addBand.Click += this.button_AddBand_Click;
            //
            // button_autoBands
            //
            this.button_autoBands.Location = new Point(128, 198);
            this.button_autoBands.Name = "button_autoBands";
            this.button_autoBands.Size = new Size(110, 23);
            this.button_autoBands.TabIndex = 2;
            this.button_autoBands.Text = "Auto Bands";
            this.button_autoBands.UseVisualStyleBackColor = true;
            this.button_autoBands.Click += this.button_AutoBands_Click;
            //
            // button_fromSelection
            //
            this.button_fromSelection.Enabled = false;
            this.button_fromSelection.Location = new Point(246, 198);
            this.button_fromSelection.Name = "button_fromSelection";
            this.button_fromSelection.Size = new Size(120, 23);
            this.button_fromSelection.TabIndex = 3;
            this.button_fromSelection.Text = "From Selection";
            this.button_fromSelection.UseVisualStyleBackColor = true;
            this.button_fromSelection.Click += this.button_FromSelection_Click;
            //
            // groupBox_options
            //
            this.groupBox_options.Controls.Add(this.numeric_windowSize);
            this.groupBox_options.Controls.Add(this.label_windowSize);
            this.groupBox_options.Controls.Add(this.numeric_overlap);
            this.groupBox_options.Controls.Add(this.label_overlap);
            this.groupBox_options.Controls.Add(this.numeric_sharpness);
            this.groupBox_options.Controls.Add(this.label_sharpness);
            this.groupBox_options.Controls.Add(this.numeric_threads);
            this.groupBox_options.Controls.Add(this.label_threads);
            this.groupBox_options.Controls.Add(this.checkBox_subtract);
            this.groupBox_options.Controls.Add(this.checkBox_timbreAware);
            this.groupBox_options.Controls.Add(this.checkBox_harmonicAware);
            this.groupBox_options.Location = new Point(12, 228);
            this.groupBox_options.Name = "groupBox_options";
            this.groupBox_options.Size = new Size(460, 128);
            this.groupBox_options.TabIndex = 3;
            this.groupBox_options.TabStop = false;
            this.groupBox_options.Text = "Separation Options";
            //
            // numeric_windowSize
            //
            this.numeric_windowSize.Location = new Point(140, 26);
            this.numeric_windowSize.Maximum = new decimal(new int[] { 65536, 0, 0, 0 });
            this.numeric_windowSize.Minimum = new decimal(new int[] { 256, 0, 0, 0 });
            this.numeric_windowSize.Name = "numeric_windowSize";
            this.numeric_windowSize.Size = new Size(70, 23);
            this.numeric_windowSize.TabIndex = 0;
            this.numeric_windowSize.Value = new decimal(new int[] { 4096, 0, 0, 0 });
            //
            // label_windowSize
            //
            this.label_windowSize.AutoSize = true;
            this.label_windowSize.Location = new Point(12, 28);
            this.label_windowSize.Name = "label_windowSize";
            this.label_windowSize.Size = new Size(120, 15);
            this.label_windowSize.TabIndex = 1;
            this.label_windowSize.Text = "FFT window size";
            //
            // numeric_overlap
            //
            this.numeric_overlap.Location = new Point(300, 26);
            this.numeric_overlap.Maximum = new decimal(new int[] { 90, 0, 0, 0 });
            this.numeric_overlap.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            this.numeric_overlap.Name = "numeric_overlap";
            this.numeric_overlap.Size = new Size(70, 23);
            this.numeric_overlap.TabIndex = 2;
            this.numeric_overlap.Value = new decimal(new int[] { 50, 0, 0, 0 });
            //
            // label_overlap
            //
            this.label_overlap.AutoSize = true;
            this.label_overlap.Location = new Point(220, 28);
            this.label_overlap.Name = "label_overlap";
            this.label_overlap.Size = new Size(75, 15);
            this.label_overlap.TabIndex = 3;
            this.label_overlap.Text = "Overlap (%)";
            //
            // numeric_sharpness
            //
            this.numeric_sharpness.Location = new Point(140, 54);
            this.numeric_sharpness.Maximum = new decimal(new int[] { 16, 0, 0, 0 });
            this.numeric_sharpness.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numeric_sharpness.Name = "numeric_sharpness";
            this.numeric_sharpness.Size = new Size(70, 23);
            this.numeric_sharpness.TabIndex = 4;
            this.numeric_sharpness.Value = new decimal(new int[] { 4, 0, 0, 0 });
            //
            // label_sharpness
            //
            this.label_sharpness.AutoSize = true;
            this.label_sharpness.Location = new Point(12, 56);
            this.label_sharpness.Name = "label_sharpness";
            this.label_sharpness.Size = new Size(120, 15);
            this.label_sharpness.TabIndex = 5;
            this.label_sharpness.Text = "Band sharpness";
            //
            // numeric_threads
            //
            this.numeric_threads.Location = new Point(300, 54);
            this.numeric_threads.Maximum = new decimal(new int[] { Environment.ProcessorCount, 0, 0, 0 });
            this.numeric_threads.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numeric_threads.Name = "numeric_threads";
            this.numeric_threads.Size = new Size(70, 23);
            this.numeric_threads.TabIndex = 7;
            //
            // label_threads
            //
            this.label_threads.AutoSize = true;
            this.label_threads.Location = new Point(250, 56);
            this.label_threads.Name = "label_threads";
            this.label_threads.Size = new Size(45, 15);
            this.label_threads.TabIndex = 8;
            this.label_threads.Text = "Threads";
            //
            // checkBox_subtract
            //
            this.checkBox_subtract.AutoSize = true;
            this.checkBox_subtract.Location = new Point(12, 84);
            this.checkBox_subtract.Name = "checkBox_subtract";
            this.checkBox_subtract.Size = new Size(150, 19);
            this.checkBox_subtract.TabIndex = 6;
            this.checkBox_subtract.Text = "Subtract from original";
            this.checkBox_subtract.UseVisualStyleBackColor = true;
            //
            // checkBox_timbreAware
            //
            this.checkBox_timbreAware.AutoSize = true;
            this.checkBox_timbreAware.Checked = true;
            this.checkBox_timbreAware.CheckState = CheckState.Checked;
            this.checkBox_timbreAware.Location = new Point(185, 84);
            this.checkBox_timbreAware.Name = "checkBox_timbreAware";
            this.checkBox_timbreAware.Size = new Size(150, 19);
            this.checkBox_timbreAware.TabIndex = 9;
            this.checkBox_timbreAware.Text = "Timbre-aware peaks";
            this.checkBox_timbreAware.UseVisualStyleBackColor = true;
            //
            // checkBox_harmonicAware
            //
            this.checkBox_harmonicAware.AutoSize = true;
            this.checkBox_harmonicAware.Checked = true;
            this.checkBox_harmonicAware.CheckState = CheckState.Checked;
            this.checkBox_harmonicAware.Location = new Point(340, 84);
            this.checkBox_harmonicAware.Name = "checkBox_harmonicAware";
            this.checkBox_harmonicAware.Size = new Size(110, 19);
            this.checkBox_harmonicAware.TabIndex = 10;
            this.checkBox_harmonicAware.Text = "Harmonic-aware";
            this.checkBox_harmonicAware.UseVisualStyleBackColor = true;
            //
            // button_run
            //
            this.button_run.Location = new Point(397, 364);
            this.button_run.Name = "button_run";
            this.button_run.Size = new Size(75, 23);
            this.button_run.TabIndex = 4;
            this.button_run.Text = "Separate";
            this.button_run.UseVisualStyleBackColor = true;
            this.button_run.Click += this.button_Run_Click;
            //
            // progressBar_separating
            //
            this.progressBar_separating.Location = new Point(12, 364);
            this.progressBar_separating.Name = "progressBar_separating";
            this.progressBar_separating.Size = new Size(379, 23);
            this.progressBar_separating.TabIndex = 5;
            //
            // label_status
            //
            this.label_status.AutoSize = true;
            this.label_status.Location = new Point(12, 338);
            this.label_status.Name = "label_status";
            this.label_status.Size = new Size(120, 15);
            this.label_status.TabIndex = 6;
            this.label_status.Text = "Ready.";
            //
            // AnalogSeparationDialog
            //
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(484, 399);
            this.Controls.Add(this.label_status);
            this.Controls.Add(this.progressBar_separating);
            this.Controls.Add(this.button_run);
            this.Controls.Add(this.groupBox_options);
            this.Controls.Add(this.button_fromSelection);
            this.Controls.Add(this.button_autoBands);
            this.Controls.Add(this.button_addBand);
            this.Controls.Add(this.panel_bands);
            this.MaximumSize = new Size(500, 438);
            this.MinimumSize = new Size(500, 438);
            this.Name = "AnalogSeparationDialog";
            this.Text = "Analog Separation";
            this.panel_bands.ResumeLayout(false);
            this.flowLayoutPanel_bands.ResumeLayout(false);
            this.groupBox_options.ResumeLayout(false);
            this.groupBox_options.PerformLayout();
            ((System.ComponentModel.ISupportInitialize) this.numeric_windowSize).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numeric_overlap).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numeric_sharpness).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numeric_threads).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private Panel panel_bands;
        private FlowLayoutPanel flowLayoutPanel_bands;
        private Button button_addBand;
        private Button button_autoBands;
        private Button button_fromSelection;
        private GroupBox groupBox_options;
        private NumericUpDown numeric_windowSize;
        private Label label_windowSize;
        private NumericUpDown numeric_overlap;
        private Label label_overlap;
        private NumericUpDown numeric_sharpness;
        private Label label_sharpness;
        private NumericUpDown numeric_threads;
        private Label label_threads;
        private CheckBox checkBox_subtract;
        private CheckBox checkBox_timbreAware;
        private CheckBox checkBox_harmonicAware;
        private Button button_run;
        private ProgressBar progressBar_separating;
        private Label label_status;
    }
}
