namespace ModularAudience.Forms.Modules.Dialogs
{
    partial class TimeStretchDialog
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
            this.numericUpDown_initialBpm = new NumericUpDown();
            this.label_info_initialBpm = new Label();
            this.label_info_targetBpm = new Label();
            this.numericUpDown_targetBpm = new NumericUpDown();
            this.label_info_stretchFactor = new Label();
            this.numericUpDown_stretchFactor = new NumericUpDown();
            this.button_stretch = new Button();
            this.button_cancel = new Button();
            this.label_info_chunkSize = new Label();
            this.numericUpDown_chunkSize = new NumericUpDown();
            this.label_info_overlap = new Label();
            this.numericUpDown_overlap = new NumericUpDown();
            this.progressBar_stretching = new ProgressBar();
            this.label_info_threads = new Label();
            this.numericUpDown_threads = new NumericUpDown();
            this.button_stretchV2 = new Button();
            this.label_processingTime = new Label();
            this.checkBox_autoChunking = new CheckBox();
            this.checkBox_offload = new CheckBox();
            this.checkBox_fixed = new CheckBox();
            this.checkBox_trim = new CheckBox();
            this.checkBox_channeled = new CheckBox();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_initialBpm).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_targetBpm).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_stretchFactor).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_chunkSize).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_overlap).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_threads).BeginInit();
            this.SuspendLayout();
            // 
            // numericUpDown_initialBpm
            // 
            this.numericUpDown_initialBpm.DecimalPlaces = 4;
            this.numericUpDown_initialBpm.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            this.numericUpDown_initialBpm.Location = new Point(12, 112);
            this.numericUpDown_initialBpm.Maximum = new decimal(new int[] { 360, 0, 0, 0 });
            this.numericUpDown_initialBpm.Minimum = new decimal(new int[] { 30, 0, 0, 0 });
            this.numericUpDown_initialBpm.Name = "numericUpDown_initialBpm";
            this.numericUpDown_initialBpm.Size = new Size(70, 23);
            this.numericUpDown_initialBpm.TabIndex = 0;
            this.numericUpDown_initialBpm.Value = new decimal(new int[] { 120, 0, 0, 0 });
            this.numericUpDown_initialBpm.ValueChanged += this.numericUpDown_initialBpm_ValueChanged;
            // 
            // label_info_initialBpm
            // 
            this.label_info_initialBpm.AutoSize = true;
            this.label_info_initialBpm.Location = new Point(12, 94);
            this.label_info_initialBpm.Name = "label_info_initialBpm";
            this.label_info_initialBpm.Size = new Size(64, 15);
            this.label_info_initialBpm.TabIndex = 1;
            this.label_info_initialBpm.Text = "Initial BPM";
            // 
            // label_info_targetBpm
            // 
            this.label_info_targetBpm.AutoSize = true;
            this.label_info_targetBpm.Location = new Point(88, 94);
            this.label_info_targetBpm.Name = "label_info_targetBpm";
            this.label_info_targetBpm.Size = new Size(68, 15);
            this.label_info_targetBpm.TabIndex = 3;
            this.label_info_targetBpm.Text = "Target BPM";
            // 
            // numericUpDown_targetBpm
            // 
            this.numericUpDown_targetBpm.DecimalPlaces = 4;
            this.numericUpDown_targetBpm.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            this.numericUpDown_targetBpm.Location = new Point(88, 112);
            this.numericUpDown_targetBpm.Maximum = new decimal(new int[] { 360, 0, 0, 0 });
            this.numericUpDown_targetBpm.Minimum = new decimal(new int[] { 30, 0, 0, 0 });
            this.numericUpDown_targetBpm.Name = "numericUpDown_targetBpm";
            this.numericUpDown_targetBpm.Size = new Size(70, 23);
            this.numericUpDown_targetBpm.TabIndex = 2;
            this.numericUpDown_targetBpm.Value = new decimal(new int[] { 150, 0, 0, 0 });
            this.numericUpDown_targetBpm.ValueChanged += this.numericUpDown_targetBpm_ValueChanged;
            // 
            // label_info_stretchFactor
            // 
            this.label_info_stretchFactor.AutoSize = true;
            this.label_info_stretchFactor.Location = new Point(164, 93);
            this.label_info_stretchFactor.Name = "label_info_stretchFactor";
            this.label_info_stretchFactor.Size = new Size(80, 15);
            this.label_info_stretchFactor.TabIndex = 5;
            this.label_info_stretchFactor.Text = "Stretch Factor";
            // 
            // numericUpDown_stretchFactor
            // 
            this.numericUpDown_stretchFactor.DecimalPlaces = 18;
            this.numericUpDown_stretchFactor.Increment = new decimal(new int[] { 1, 0, 0, 262144 });
            this.numericUpDown_stretchFactor.Location = new Point(164, 111);
            this.numericUpDown_stretchFactor.Maximum = new decimal(new int[] { 10, 0, 0, 0 });
            this.numericUpDown_stretchFactor.Minimum = new decimal(new int[] { 1, 0, 0, 131072 });
            this.numericUpDown_stretchFactor.Name = "numericUpDown_stretchFactor";
            this.numericUpDown_stretchFactor.Size = new Size(146, 23);
            this.numericUpDown_stretchFactor.TabIndex = 4;
            this.numericUpDown_stretchFactor.Value = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_stretchFactor.ValueChanged += this.numericUpDown_stretchFactor_ValueChanged;
            // 
            // button_stretch
            // 
            this.button_stretch.BackColor = SystemColors.Info;
            this.button_stretch.Location = new Point(377, 166);
            this.button_stretch.Name = "button_stretch";
            this.button_stretch.Size = new Size(75, 23);
            this.button_stretch.TabIndex = 6;
            this.button_stretch.Text = "Stretch";
            this.button_stretch.UseVisualStyleBackColor = false;
            this.button_stretch.Click += this.button_stretch_Click;
            // 
            // button_cancel
            // 
            this.button_cancel.Location = new Point(316, 166);
            this.button_cancel.Name = "button_cancel";
            this.button_cancel.Size = new Size(55, 23);
            this.button_cancel.TabIndex = 7;
            this.button_cancel.Text = "Cancel";
            this.button_cancel.UseVisualStyleBackColor = true;
            this.button_cancel.Click += this.button_cancel_Click;
            // 
            // label_info_chunkSize
            // 
            this.label_info_chunkSize.AutoSize = true;
            this.label_info_chunkSize.Location = new Point(11, 41);
            this.label_info_chunkSize.Name = "label_info_chunkSize";
            this.label_info_chunkSize.Size = new Size(65, 15);
            this.label_info_chunkSize.TabIndex = 9;
            this.label_info_chunkSize.Text = "Chunk Size";
            // 
            // numericUpDown_chunkSize
            // 
            this.numericUpDown_chunkSize.Location = new Point(12, 59);
            this.numericUpDown_chunkSize.Maximum = new decimal(new int[] { 65536, 0, 0, 0 });
            this.numericUpDown_chunkSize.Minimum = new decimal(new int[] { 128, 0, 0, 0 });
            this.numericUpDown_chunkSize.Name = "numericUpDown_chunkSize";
            this.numericUpDown_chunkSize.Size = new Size(70, 23);
            this.numericUpDown_chunkSize.TabIndex = 8;
            this.numericUpDown_chunkSize.Value = new decimal(new int[] { 8192, 0, 0, 0 });
            this.numericUpDown_chunkSize.ValueChanged += this.numericUpDown_chunkSize_ValueChanged;
            // 
            // label_info_overlap
            // 
            this.label_info_overlap.AutoSize = true;
            this.label_info_overlap.Location = new Point(88, 41);
            this.label_info_overlap.Name = "label_info_overlap";
            this.label_info_overlap.Size = new Size(61, 15);
            this.label_info_overlap.TabIndex = 11;
            this.label_info_overlap.Text = "Overlap %";
            // 
            // numericUpDown_overlap
            // 
            this.numericUpDown_overlap.DecimalPlaces = 4;
            this.numericUpDown_overlap.Increment = new decimal(new int[] { 5, 0, 0, 131072 });
            this.numericUpDown_overlap.Location = new Point(88, 59);
            this.numericUpDown_overlap.Maximum = new decimal(new int[] { 90, 0, 0, 131072 });
            this.numericUpDown_overlap.Name = "numericUpDown_overlap";
            this.numericUpDown_overlap.Size = new Size(70, 23);
            this.numericUpDown_overlap.TabIndex = 10;
            this.numericUpDown_overlap.Value = new decimal(new int[] { 5, 0, 0, 65536 });
            // 
            // progressBar_stretching
            // 
            this.progressBar_stretching.Location = new Point(12, 166);
            this.progressBar_stretching.Maximum = 1000;
            this.progressBar_stretching.Name = "progressBar_stretching";
            this.progressBar_stretching.Size = new Size(298, 23);
            this.progressBar_stretching.Style = ProgressBarStyle.Continuous;
            this.progressBar_stretching.TabIndex = 12;
            // 
            // label_info_threads
            // 
            this.label_info_threads.AutoSize = true;
            this.label_info_threads.Location = new Point(377, 9);
            this.label_info_threads.Name = "label_info_threads";
            this.label_info_threads.Size = new Size(49, 15);
            this.label_info_threads.TabIndex = 14;
            this.label_info_threads.Text = "Threads";
            // 
            // numericUpDown_threads
            // 
            this.numericUpDown_threads.Location = new Point(377, 27);
            this.numericUpDown_threads.Maximum = new decimal(new int[] { 0, 0, 0, 0 });
            this.numericUpDown_threads.Name = "numericUpDown_threads";
            this.numericUpDown_threads.Size = new Size(75, 23);
            this.numericUpDown_threads.TabIndex = 13;
            // 
            // button_stretchV2
            // 
            this.button_stretchV2.BackColor = SystemColors.Info;
            this.button_stretchV2.Location = new Point(377, 137);
            this.button_stretchV2.Name = "button_stretchV2";
            this.button_stretchV2.Size = new Size(75, 23);
            this.button_stretchV2.TabIndex = 15;
            this.button_stretchV2.Text = "Stretch V2";
            this.button_stretchV2.UseVisualStyleBackColor = false;
            this.button_stretchV2.Click += this.button_stretchV2_Click;
            // 
            // label_processingTime
            // 
            this.label_processingTime.AutoSize = true;
            this.label_processingTime.Location = new Point(240, 148);
            this.label_processingTime.Name = "label_processingTime";
            this.label_processingTime.Size = new Size(25, 15);
            this.label_processingTime.TabIndex = 16;
            this.label_processingTime.Text = "-:--";
            // 
            // checkBox_autoChunking
            // 
            this.checkBox_autoChunking.AutoSize = true;
            this.checkBox_autoChunking.Location = new Point(164, 60);
            this.checkBox_autoChunking.Name = "checkBox_autoChunking";
            this.checkBox_autoChunking.Size = new Size(52, 19);
            this.checkBox_autoChunking.TabIndex = 17;
            this.checkBox_autoChunking.Text = "Auto";
            this.checkBox_autoChunking.UseVisualStyleBackColor = true;
            this.checkBox_autoChunking.CheckedChanged += this.checkBox_autoChunking_CheckedChanged;
            // 
            // checkBox_offload
            // 
            this.checkBox_offload.AutoSize = true;
            this.checkBox_offload.Location = new Point(377, 112);
            this.checkBox_offload.Name = "checkBox_offload";
            this.checkBox_offload.Size = new Size(66, 19);
            this.checkBox_offload.TabIndex = 18;
            this.checkBox_offload.Text = "Offload";
            this.checkBox_offload.UseVisualStyleBackColor = true;
            // 
            // checkBox_fixed
            // 
            this.checkBox_fixed.AutoSize = true;
            this.checkBox_fixed.Location = new Point(12, 141);
            this.checkBox_fixed.Name = "checkBox_fixed";
            this.checkBox_fixed.Size = new Size(53, 19);
            this.checkBox_fixed.TabIndex = 19;
            this.checkBox_fixed.Text = "Fixed";
            this.checkBox_fixed.UseVisualStyleBackColor = true;
            // 
            // checkBox_trim
            // 
            this.checkBox_trim.AutoSize = true;
            this.checkBox_trim.Location = new Point(377, 56);
            this.checkBox_trim.Name = "checkBox_trim";
            this.checkBox_trim.Size = new Size(90, 19);
            this.checkBox_trim.TabIndex = 20;
            this.checkBox_trim.Text = "Trim Silence";
            this.checkBox_trim.UseVisualStyleBackColor = true;
            // 
            // checkBox_channeled
            // 
            this.checkBox_channeled.AutoSize = true;
            this.checkBox_channeled.Location = new Point(377, 92);
            this.checkBox_channeled.Name = "checkBox_channeled";
            this.checkBox_channeled.Size = new Size(83, 19);
            this.checkBox_channeled.TabIndex = 21;
            this.checkBox_channeled.Text = "Channeled";
            this.checkBox_channeled.UseVisualStyleBackColor = true;
            this.checkBox_channeled.CheckedChanged += this.checkBox_channeled_CheckedChanged;
            // 
            // TimeStretchDialog
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(464, 201);
            this.Controls.Add(this.checkBox_channeled);
            this.Controls.Add(this.checkBox_trim);
            this.Controls.Add(this.checkBox_fixed);
            this.Controls.Add(this.checkBox_offload);
            this.Controls.Add(this.checkBox_autoChunking);
            this.Controls.Add(this.label_processingTime);
            this.Controls.Add(this.button_stretchV2);
            this.Controls.Add(this.label_info_threads);
            this.Controls.Add(this.numericUpDown_threads);
            this.Controls.Add(this.progressBar_stretching);
            this.Controls.Add(this.label_info_overlap);
            this.Controls.Add(this.numericUpDown_overlap);
            this.Controls.Add(this.label_info_chunkSize);
            this.Controls.Add(this.numericUpDown_chunkSize);
            this.Controls.Add(this.button_cancel);
            this.Controls.Add(this.button_stretch);
            this.Controls.Add(this.label_info_stretchFactor);
            this.Controls.Add(this.numericUpDown_stretchFactor);
            this.Controls.Add(this.label_info_targetBpm);
            this.Controls.Add(this.numericUpDown_targetBpm);
            this.Controls.Add(this.label_info_initialBpm);
            this.Controls.Add(this.numericUpDown_initialBpm);
            this.Name = "TimeStretchDialog";
            this.Text = "TimeStretchDialog";
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_initialBpm).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_targetBpm).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_stretchFactor).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_chunkSize).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_overlap).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_threads).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private NumericUpDown numericUpDown_initialBpm;
        private Label label_info_initialBpm;
        private Label label_info_targetBpm;
        private NumericUpDown numericUpDown_targetBpm;
        private Label label_info_stretchFactor;
        private NumericUpDown numericUpDown_stretchFactor;
        private Button button_stretch;
        private Button button_cancel;
        private Label label_info_chunkSize;
        private NumericUpDown numericUpDown_chunkSize;
        private Label label_info_overlap;
        private NumericUpDown numericUpDown_overlap;
        private ProgressBar progressBar_stretching;
        private Label label_info_threads;
        private NumericUpDown numericUpDown_threads;
        private Button button_stretchV2;
        private Label label_processingTime;
        private CheckBox checkBox_autoChunking;
        private CheckBox checkBox_offload;
        private CheckBox checkBox_fixed;
        private CheckBox checkBox_trim;
        private CheckBox checkBox_channeled;
    }
}