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
            numericUpDown_initialBpm = new NumericUpDown();
            label_info_initialBpm = new Label();
            label_info_targetBpm = new Label();
            numericUpDown_targetBpm = new NumericUpDown();
            label_info_stretchFactor = new Label();
            numericUpDown_stretchFactor = new NumericUpDown();
            button_stretch = new Button();
            button_cancel = new Button();
            label_info_chunkSize = new Label();
            numericUpDown_chunkSize = new NumericUpDown();
            label_info_overlap = new Label();
            numericUpDown_overlap = new NumericUpDown();
            progressBar_stretching = new ProgressBar();
            label_info_threads = new Label();
            numericUpDown_threads = new NumericUpDown();
            button_stretchV2 = new Button();
            label_processingTime = new Label();
            checkBox_autoChunking = new CheckBox();
            checkBox_offload = new CheckBox();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_initialBpm).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_targetBpm).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_stretchFactor).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_chunkSize).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_overlap).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_threads).BeginInit();
            SuspendLayout();
            // 
            // numericUpDown_initialBpm
            // 
            numericUpDown_initialBpm.DecimalPlaces = 4;
            numericUpDown_initialBpm.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            numericUpDown_initialBpm.Location = new Point(12, 104);
            numericUpDown_initialBpm.Maximum = new decimal(new int[] { 360, 0, 0, 0 });
            numericUpDown_initialBpm.Minimum = new decimal(new int[] { 30, 0, 0, 0 });
            numericUpDown_initialBpm.Name = "numericUpDown_initialBpm";
            numericUpDown_initialBpm.Size = new Size(75, 23);
            numericUpDown_initialBpm.TabIndex = 0;
            numericUpDown_initialBpm.Value = new decimal(new int[] { 120, 0, 0, 0 });
            numericUpDown_initialBpm.ValueChanged += numericUpDown_initialBpm_ValueChanged;
            // 
            // label_info_initialBpm
            // 
            label_info_initialBpm.AutoSize = true;
            label_info_initialBpm.Location = new Point(12, 86);
            label_info_initialBpm.Name = "label_info_initialBpm";
            label_info_initialBpm.Size = new Size(64, 15);
            label_info_initialBpm.TabIndex = 1;
            label_info_initialBpm.Text = "Initial BPM";
            // 
            // label_info_targetBpm
            // 
            label_info_targetBpm.AutoSize = true;
            label_info_targetBpm.Location = new Point(93, 86);
            label_info_targetBpm.Name = "label_info_targetBpm";
            label_info_targetBpm.Size = new Size(68, 15);
            label_info_targetBpm.TabIndex = 3;
            label_info_targetBpm.Text = "Target BPM";
            // 
            // numericUpDown_targetBpm
            // 
            numericUpDown_targetBpm.DecimalPlaces = 4;
            numericUpDown_targetBpm.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            numericUpDown_targetBpm.Location = new Point(93, 104);
            numericUpDown_targetBpm.Maximum = new decimal(new int[] { 360, 0, 0, 0 });
            numericUpDown_targetBpm.Minimum = new decimal(new int[] { 30, 0, 0, 0 });
            numericUpDown_targetBpm.Name = "numericUpDown_targetBpm";
            numericUpDown_targetBpm.Size = new Size(75, 23);
            numericUpDown_targetBpm.TabIndex = 2;
            numericUpDown_targetBpm.Value = new decimal(new int[] { 150, 0, 0, 0 });
            numericUpDown_targetBpm.ValueChanged += numericUpDown_targetBpm_ValueChanged;
            // 
            // label_info_stretchFactor
            // 
            label_info_stretchFactor.AutoSize = true;
            label_info_stretchFactor.Location = new Point(174, 86);
            label_info_stretchFactor.Name = "label_info_stretchFactor";
            label_info_stretchFactor.Size = new Size(80, 15);
            label_info_stretchFactor.TabIndex = 5;
            label_info_stretchFactor.Text = "Stretch Factor";
            // 
            // numericUpDown_stretchFactor
            // 
            numericUpDown_stretchFactor.DecimalPlaces = 18;
            numericUpDown_stretchFactor.Increment = new decimal(new int[] { 1, 0, 0, 262144 });
            numericUpDown_stretchFactor.Location = new Point(174, 104);
            numericUpDown_stretchFactor.Maximum = new decimal(new int[] { 10, 0, 0, 0 });
            numericUpDown_stretchFactor.Minimum = new decimal(new int[] { 1, 0, 0, 131072 });
            numericUpDown_stretchFactor.Name = "numericUpDown_stretchFactor";
            numericUpDown_stretchFactor.Size = new Size(150, 23);
            numericUpDown_stretchFactor.TabIndex = 4;
            numericUpDown_stretchFactor.Value = new decimal(new int[] { 1, 0, 0, 0 });
            numericUpDown_stretchFactor.ValueChanged += numericUpDown_stretchFactor_ValueChanged;
            // 
            // button_stretch
            // 
            button_stretch.BackColor = SystemColors.Info;
            button_stretch.Location = new Point(377, 166);
            button_stretch.Name = "button_stretch";
            button_stretch.Size = new Size(75, 23);
            button_stretch.TabIndex = 6;
            button_stretch.Text = "Stretch";
            button_stretch.UseVisualStyleBackColor = false;
            button_stretch.Click += button_stretch_Click;
            // 
            // button_cancel
            // 
            button_cancel.Location = new Point(316, 166);
            button_cancel.Name = "button_cancel";
            button_cancel.Size = new Size(55, 23);
            button_cancel.TabIndex = 7;
            button_cancel.Text = "Cancel";
            button_cancel.UseVisualStyleBackColor = true;
            button_cancel.Click += button_cancel_Click;
            // 
            // label_info_chunkSize
            // 
            label_info_chunkSize.AutoSize = true;
            label_info_chunkSize.Location = new Point(12, 33);
            label_info_chunkSize.Name = "label_info_chunkSize";
            label_info_chunkSize.Size = new Size(65, 15);
            label_info_chunkSize.TabIndex = 9;
            label_info_chunkSize.Text = "Chunk Size";
            // 
            // numericUpDown_chunkSize
            // 
            numericUpDown_chunkSize.Enabled = false;
            numericUpDown_chunkSize.Location = new Point(12, 51);
            numericUpDown_chunkSize.Maximum = new decimal(new int[] { 65536, 0, 0, 0 });
            numericUpDown_chunkSize.Minimum = new decimal(new int[] { 128, 0, 0, 0 });
            numericUpDown_chunkSize.Name = "numericUpDown_chunkSize";
            numericUpDown_chunkSize.Size = new Size(75, 23);
            numericUpDown_chunkSize.TabIndex = 8;
            numericUpDown_chunkSize.Value = new decimal(new int[] { 8192, 0, 0, 0 });
            numericUpDown_chunkSize.ValueChanged += numericUpDown_chunkSize_ValueChanged;
            // 
            // label_info_overlap
            // 
            label_info_overlap.AutoSize = true;
            label_info_overlap.Location = new Point(93, 33);
            label_info_overlap.Name = "label_info_overlap";
            label_info_overlap.Size = new Size(61, 15);
            label_info_overlap.TabIndex = 11;
            label_info_overlap.Text = "Overlap %";
            // 
            // numericUpDown_overlap
            // 
            numericUpDown_overlap.DecimalPlaces = 4;
            numericUpDown_overlap.Enabled = false;
            numericUpDown_overlap.Location = new Point(93, 51);
            numericUpDown_overlap.Maximum = new decimal(new int[] { 99, 0, 0, 131072 });
            numericUpDown_overlap.Name = "numericUpDown_overlap";
            numericUpDown_overlap.Size = new Size(75, 23);
            numericUpDown_overlap.TabIndex = 10;
            numericUpDown_overlap.Value = new decimal(new int[] { 5, 0, 0, 65536 });
            // 
            // progressBar_stretching
            // 
            progressBar_stretching.Location = new Point(12, 166);
            progressBar_stretching.Maximum = 1000;
            progressBar_stretching.Name = "progressBar_stretching";
            progressBar_stretching.Size = new Size(298, 23);
            progressBar_stretching.Style = ProgressBarStyle.Continuous;
            progressBar_stretching.TabIndex = 12;
            // 
            // label_info_threads
            // 
            label_info_threads.AutoSize = true;
            label_info_threads.Location = new Point(377, 9);
            label_info_threads.Name = "label_info_threads";
            label_info_threads.Size = new Size(49, 15);
            label_info_threads.TabIndex = 14;
            label_info_threads.Text = "Threads";
            // 
            // numericUpDown_threads
            // 
            numericUpDown_threads.Location = new Point(377, 27);
            numericUpDown_threads.Maximum = new decimal(new int[] { 0, 0, 0, 0 });
            numericUpDown_threads.Name = "numericUpDown_threads";
            numericUpDown_threads.Size = new Size(75, 23);
            numericUpDown_threads.TabIndex = 13;
            // 
            // button_stretchV2
            // 
            button_stretchV2.BackColor = SystemColors.Info;
            button_stretchV2.Location = new Point(377, 137);
            button_stretchV2.Name = "button_stretchV2";
            button_stretchV2.Size = new Size(75, 23);
            button_stretchV2.TabIndex = 15;
            button_stretchV2.Text = "Stretch V2";
            button_stretchV2.UseVisualStyleBackColor = false;
            button_stretchV2.Click += button_stretchV2_Click;
            // 
            // label_processingTime
            // 
            label_processingTime.AutoSize = true;
            label_processingTime.Location = new Point(240, 148);
            label_processingTime.Name = "label_processingTime";
            label_processingTime.Size = new Size(25, 15);
            label_processingTime.TabIndex = 16;
            label_processingTime.Text = "-:--";
            // 
            // checkBox_autoChunking
            // 
            checkBox_autoChunking.AutoSize = true;
            checkBox_autoChunking.Location = new Point(174, 52);
            checkBox_autoChunking.Name = "checkBox_autoChunking";
            checkBox_autoChunking.Size = new Size(52, 19);
            checkBox_autoChunking.TabIndex = 17;
            checkBox_autoChunking.Text = "Auto";
            checkBox_autoChunking.UseVisualStyleBackColor = true;
            checkBox_autoChunking.CheckedChanged += checkBox_autoChunking_CheckedChanged;
            // 
            // checkBox_offload
            // 
            checkBox_offload.AutoSize = true;
            checkBox_offload.Location = new Point(377, 112);
            checkBox_offload.Name = "checkBox_offload";
            checkBox_offload.Size = new Size(66, 19);
            checkBox_offload.TabIndex = 18;
            checkBox_offload.Text = "Offload";
            checkBox_offload.UseVisualStyleBackColor = true;
            // 
            // TimeStretchDialog
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(464, 201);
            Controls.Add(checkBox_offload);
            Controls.Add(checkBox_autoChunking);
            Controls.Add(label_processingTime);
            Controls.Add(button_stretchV2);
            Controls.Add(label_info_threads);
            Controls.Add(numericUpDown_threads);
            Controls.Add(progressBar_stretching);
            Controls.Add(label_info_overlap);
            Controls.Add(numericUpDown_overlap);
            Controls.Add(label_info_chunkSize);
            Controls.Add(numericUpDown_chunkSize);
            Controls.Add(button_cancel);
            Controls.Add(button_stretch);
            Controls.Add(label_info_stretchFactor);
            Controls.Add(numericUpDown_stretchFactor);
            Controls.Add(label_info_targetBpm);
            Controls.Add(numericUpDown_targetBpm);
            Controls.Add(label_info_initialBpm);
            Controls.Add(numericUpDown_initialBpm);
            Name = "TimeStretchDialog";
            Text = "TimeStretchDialog";
            ((System.ComponentModel.ISupportInitialize)numericUpDown_initialBpm).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_targetBpm).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_stretchFactor).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_chunkSize).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_overlap).EndInit();
            ((System.ComponentModel.ISupportInitialize)numericUpDown_threads).EndInit();
            ResumeLayout(false);
            PerformLayout();
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
    }
}