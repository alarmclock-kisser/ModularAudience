namespace ModularAudience.Forms
{
	partial class VisualizerDialog
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
            this.button_render = new Button();
            this.progressBar_rendering = new ProgressBar();
            this.comboBox_resolution = new ComboBox();
            this.numericUpDown_frameRate = new NumericUpDown();
            this.label_info_frameRate = new Label();
            this.numericUpDown_startSeconds = new NumericUpDown();
            this.label_info_startSeconds = new Label();
            this.label_info_endSeconds = new Label();
            this.numericUpDown_endSeconds = new NumericUpDown();
            this.label_sizeApprox = new Label();
            this.checkBox_copyPath = new CheckBox();
            this.numericUpDown_threads = new NumericUpDown();
            this.label_info_threads = new Label();
            this.label_cuda = new Label();
            this.label_info_gpuDevice = new Label();
            this.comboBox_gpuDevice = new ComboBox();
            this.numericUpDown_volume = new NumericUpDown();
            this.label_info_volume = new Label();
            this.label_amplification = new Label();
            this.numericUpDown_amplification = new NumericUpDown();
            this.label_time = new Label();
            this.label_framesPerSec = new Label();
            this.comboBox_codecPreset = new ComboBox();
            this.button_codecInfo = new Button();
            this.label_percentage = new Label();
            this.comboBox_mode = new ComboBox();
            this.comboBox_visPreset = new ComboBox();
            this.label1 = new Label();
            this.button_preview = new Button();
            this.numericUpDown_previewDuration = new NumericUpDown();
            this.label_info_previewDuration = new Label();
            this.button_backColor = new Button();
            this.button_colorGraph = new Button();
            this.label_info_thickness = new Label();
            this.numericUpDown_thickness = new NumericUpDown();
            this.label_info_threshold = new Label();
            this.numericUpDown_threshold = new NumericUpDown();
            this.button_addSubtitlesTxt = new Button();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_frameRate).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_startSeconds).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_endSeconds).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_threads).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_volume).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_amplification).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_previewDuration).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_thickness).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_threshold).BeginInit();
            this.SuspendLayout();
            // 
            // button_render
            // 
            this.button_render.Location = new Point(360, 406);
            this.button_render.Name = "button_render";
            this.button_render.Size = new Size(92, 23);
            this.button_render.TabIndex = 0;
            this.button_render.Text = "Render";
            this.button_render.UseVisualStyleBackColor = true;
            this.button_render.Click += this.button_render_Click;
            // 
            // progressBar_rendering
            // 
            this.progressBar_rendering.Location = new Point(12, 406);
            this.progressBar_rendering.Maximum = 1000;
            this.progressBar_rendering.Name = "progressBar_rendering";
            this.progressBar_rendering.Size = new Size(342, 23);
            this.progressBar_rendering.TabIndex = 1;
            // 
            // comboBox_resolution
            // 
            this.comboBox_resolution.FormattingEnabled = true;
            this.comboBox_resolution.Location = new Point(12, 27);
            this.comboBox_resolution.Name = "comboBox_resolution";
            this.comboBox_resolution.Size = new Size(240, 23);
            this.comboBox_resolution.TabIndex = 2;
            this.comboBox_resolution.Text = "Select Resolution...";
            this.comboBox_resolution.SelectedIndexChanged += this.comboBox_resolution_SelectedIndexChanged;
            this.comboBox_resolution.Click += this.comboBox_resolution_Click;
            // 
            // numericUpDown_frameRate
            // 
            this.numericUpDown_frameRate.DecimalPlaces = 2;
            this.numericUpDown_frameRate.Location = new Point(258, 27);
            this.numericUpDown_frameRate.Maximum = new decimal(new int[] { 144, 0, 0, 0 });
            this.numericUpDown_frameRate.Minimum = new decimal(new int[] { 5, 0, 0, 65536 });
            this.numericUpDown_frameRate.Name = "numericUpDown_frameRate";
            this.numericUpDown_frameRate.Size = new Size(70, 23);
            this.numericUpDown_frameRate.TabIndex = 3;
            this.numericUpDown_frameRate.Value = new decimal(new int[] { 24, 0, 0, 0 });
            this.numericUpDown_frameRate.ValueChanged += this.numericUpDown_frameRate_ValueChanged;
            // 
            // label_info_frameRate
            // 
            this.label_info_frameRate.AutoSize = true;
            this.label_info_frameRate.Location = new Point(258, 9);
            this.label_info_frameRate.Name = "label_info_frameRate";
            this.label_info_frameRate.Size = new Size(66, 15);
            this.label_info_frameRate.TabIndex = 4;
            this.label_info_frameRate.Text = "Frame Rate";
            // 
            // numericUpDown_startSeconds
            // 
            this.numericUpDown_startSeconds.DecimalPlaces = 2;
            this.numericUpDown_startSeconds.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            this.numericUpDown_startSeconds.Location = new Point(12, 317);
            this.numericUpDown_startSeconds.Maximum = new decimal(new int[] { 999999, 0, 0, 0 });
            this.numericUpDown_startSeconds.Name = "numericUpDown_startSeconds";
            this.numericUpDown_startSeconds.Size = new Size(70, 23);
            this.numericUpDown_startSeconds.TabIndex = 5;
            this.numericUpDown_startSeconds.ValueChanged += this.numericUpDown_startSeconds_ValueChanged;
            // 
            // label_info_startSeconds
            // 
            this.label_info_startSeconds.AutoSize = true;
            this.label_info_startSeconds.Location = new Point(12, 299);
            this.label_info_startSeconds.Name = "label_info_startSeconds";
            this.label_info_startSeconds.Size = new Size(54, 15);
            this.label_info_startSeconds.TabIndex = 6;
            this.label_info_startSeconds.Text = "Start sec.";
            // 
            // label_info_endSeconds
            // 
            this.label_info_endSeconds.AutoSize = true;
            this.label_info_endSeconds.Location = new Point(88, 299);
            this.label_info_endSeconds.Name = "label_info_endSeconds";
            this.label_info_endSeconds.Size = new Size(50, 15);
            this.label_info_endSeconds.TabIndex = 8;
            this.label_info_endSeconds.Text = "End sec.";
            // 
            // numericUpDown_endSeconds
            // 
            this.numericUpDown_endSeconds.DecimalPlaces = 2;
            this.numericUpDown_endSeconds.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            this.numericUpDown_endSeconds.Location = new Point(88, 317);
            this.numericUpDown_endSeconds.Maximum = new decimal(new int[] { 999999, 0, 0, 0 });
            this.numericUpDown_endSeconds.Name = "numericUpDown_endSeconds";
            this.numericUpDown_endSeconds.Size = new Size(70, 23);
            this.numericUpDown_endSeconds.TabIndex = 7;
            this.numericUpDown_endSeconds.ValueChanged += this.numericUpDown_endSeconds_ValueChanged;
            // 
            // label_sizeApprox
            // 
            this.label_sizeApprox.AutoSize = true;
            this.label_sizeApprox.Location = new Point(12, 82);
            this.label_sizeApprox.Name = "label_sizeApprox";
            this.label_sizeApprox.Size = new Size(88, 15);
            this.label_sizeApprox.TabIndex = 9;
            this.label_sizeApprox.Text = "- frames (- MB)";
            // 
            // checkBox_copyPath
            // 
            this.checkBox_copyPath.AutoSize = true;
            this.checkBox_copyPath.Checked = true;
            this.checkBox_copyPath.CheckState = CheckState.Checked;
            this.checkBox_copyPath.Location = new Point(368, 381);
            this.checkBox_copyPath.Name = "checkBox_copyPath";
            this.checkBox_copyPath.Size = new Size(81, 19);
            this.checkBox_copyPath.TabIndex = 10;
            this.checkBox_copyPath.Text = "Copy path";
            this.checkBox_copyPath.UseVisualStyleBackColor = true;
            // 
            // numericUpDown_threads
            // 
            this.numericUpDown_threads.Location = new Point(400, 27);
            this.numericUpDown_threads.Maximum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_threads.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_threads.Name = "numericUpDown_threads";
            this.numericUpDown_threads.Size = new Size(52, 23);
            this.numericUpDown_threads.TabIndex = 11;
            this.numericUpDown_threads.Value = new decimal(new int[] { 1, 0, 0, 0 });
            // 
            // label_info_threads
            // 
            this.label_info_threads.AutoSize = true;
            this.label_info_threads.Location = new Point(400, 9);
            this.label_info_threads.Name = "label_info_threads";
            this.label_info_threads.Size = new Size(49, 15);
            this.label_info_threads.TabIndex = 12;
            this.label_info_threads.Text = "Threads";
            // 
            // label_cuda
            // 
            this.label_cuda.AutoSize = true;
            this.label_cuda.Location = new Point(12, 9);
            this.label_cuda.Name = "label_cuda";
            this.label_cuda.Size = new Size(97, 15);
            this.label_cuda.TabIndex = 13;
            this.label_cuda.Text = "CUDA: <Offline>";
            // 
            // label_info_gpuDevice
            // 
            this.label_info_gpuDevice.AutoSize = true;
            this.label_info_gpuDevice.Location = new Point(336, 9);
            this.label_info_gpuDevice.Name = "label_info_gpuDevice";
            this.label_info_gpuDevice.Size = new Size(58, 15);
            this.label_info_gpuDevice.TabIndex = 36;
            this.label_info_gpuDevice.Text = "GPU Device";
            // 
            // comboBox_gpuDevice
            // 
            this.comboBox_gpuDevice.DropDownStyle = ComboBoxStyle.DropDownList;
            this.comboBox_gpuDevice.FormattingEnabled = true;
            this.comboBox_gpuDevice.Location = new Point(336, 27);
            this.comboBox_gpuDevice.Name = "comboBox_gpuDevice";
            this.comboBox_gpuDevice.Size = new Size(116, 23);
            this.comboBox_gpuDevice.TabIndex = 37;
            this.comboBox_gpuDevice.Text = "<CPU / no GPU>";
            // 
            // numericUpDown_volume
            // 
            this.numericUpDown_volume.DecimalPlaces = 1;
            this.numericUpDown_volume.ForeColor = Color.Gray;
            this.numericUpDown_volume.Location = new Point(164, 317);
            this.numericUpDown_volume.Maximum = new decimal(new int[] { 150, 0, 0, 0 });
            this.numericUpDown_volume.Name = "numericUpDown_volume";
            this.numericUpDown_volume.Size = new Size(70, 23);
            this.numericUpDown_volume.TabIndex = 14;
            this.numericUpDown_volume.Value = new decimal(new int[] { 100, 0, 0, 0 });
            this.numericUpDown_volume.ValueChanged += this.numericUpDown_volume_ValueChanged;
            // 
            // label_info_volume
            // 
            this.label_info_volume.AutoSize = true;
            this.label_info_volume.Location = new Point(164, 299);
            this.label_info_volume.Name = "label_info_volume";
            this.label_info_volume.Size = new Size(60, 15);
            this.label_info_volume.TabIndex = 15;
            this.label_info_volume.Text = "Volume %";
            // 
            // label_amplification
            // 
            this.label_amplification.AutoSize = true;
            this.label_amplification.Location = new Point(372, 270);
            this.label_amplification.Name = "label_amplification";
            this.label_amplification.Size = new Size(62, 15);
            this.label_amplification.TabIndex = 17;
            this.label_amplification.Text = "Amplify %";
            // 
            // numericUpDown_amplification
            // 
            this.numericUpDown_amplification.DecimalPlaces = 1;
            this.numericUpDown_amplification.Increment = new decimal(new int[] { 5, 0, 0, 65536 });
            this.numericUpDown_amplification.Location = new Point(372, 288);
            this.numericUpDown_amplification.Minimum = new decimal(new int[] { 5, 0, 0, 65536 });
            this.numericUpDown_amplification.Name = "numericUpDown_amplification";
            this.numericUpDown_amplification.Size = new Size(80, 23);
            this.numericUpDown_amplification.TabIndex = 16;
            this.numericUpDown_amplification.Value = new decimal(new int[] { 541, 0, 0, 65536 });
            // 
            // label_time
            // 
            this.label_time.AutoSize = true;
            this.label_time.Location = new Point(119, 388);
            this.label_time.Name = "label_time";
            this.label_time.Size = new Size(78, 15);
            this.label_time.TabIndex = 18;
            this.label_time.Text = "0.00s / ~0.00s";
            // 
            // label_framesPerSec
            // 
            this.label_framesPerSec.AutoSize = true;
            this.label_framesPerSec.Location = new Point(12, 388);
            this.label_framesPerSec.Name = "label_framesPerSec";
            this.label_framesPerSec.Size = new Size(58, 15);
            this.label_framesPerSec.TabIndex = 19;
            this.label_framesPerSec.Text = "~ 0.00 fps";
            // 
            // comboBox_codecPreset
            // 
            this.comboBox_codecPreset.FormattingEnabled = true;
            this.comboBox_codecPreset.Location = new Point(12, 56);
            this.comboBox_codecPreset.Name = "comboBox_codecPreset";
            this.comboBox_codecPreset.Size = new Size(211, 23);
            this.comboBox_codecPreset.TabIndex = 20;
            this.comboBox_codecPreset.Text = "Select Codec Preset...";
            this.comboBox_codecPreset.Click += this.comboBox_codecPreset_Click;
            // 
            // button_codecInfo
            // 
            this.button_codecInfo.Font = new Font("Bahnschrift", 9F, FontStyle.Bold, GraphicsUnit.Point,  0);
            this.button_codecInfo.Location = new Point(229, 56);
            this.button_codecInfo.Name = "button_codecInfo";
            this.button_codecInfo.Size = new Size(23, 23);
            this.button_codecInfo.TabIndex = 21;
            this.button_codecInfo.Text = "i";
            this.button_codecInfo.UseVisualStyleBackColor = true;
            this.button_codecInfo.Click += this.button_codecInfo_Click;
            // 
            // label_percentage
            // 
            this.label_percentage.AutoSize = true;
            this.label_percentage.Location = new Point(303, 388);
            this.label_percentage.Name = "label_percentage";
            this.label_percentage.Size = new Size(41, 15);
            this.label_percentage.TabIndex = 22;
            this.label_percentage.Text = "0.00 %";
            // 
            // comboBox_mode
            // 
            this.comboBox_mode.FormattingEnabled = true;
            this.comboBox_mode.Location = new Point(303, 141);
            this.comboBox_mode.Name = "comboBox_mode";
            this.comboBox_mode.Size = new Size(149, 23);
            this.comboBox_mode.TabIndex = 23;
            this.comboBox_mode.Text = "Select Mode...";
            // 
            // comboBox_visPreset
            // 
            this.comboBox_visPreset.FormattingEnabled = true;
            this.comboBox_visPreset.Location = new Point(303, 170);
            this.comboBox_visPreset.Name = "comboBox_visPreset";
            this.comboBox_visPreset.Size = new Size(149, 23);
            this.comboBox_visPreset.TabIndex = 24;
            this.comboBox_visPreset.Text = "Select Preset...";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new Point(303, 123);
            this.label1.Name = "label1";
            this.label1.Size = new Size(101, 15);
            this.label1.TabIndex = 25;
            this.label1.Text = "Visualizer Settings";
            // 
            // button_preview
            // 
            this.button_preview.Location = new Point(372, 317);
            this.button_preview.Name = "button_preview";
            this.button_preview.Size = new Size(80, 23);
            this.button_preview.TabIndex = 26;
            this.button_preview.Text = "Preview";
            this.button_preview.UseVisualStyleBackColor = true;
            this.button_preview.Click += this.button_preview_Click;
            // 
            // numericUpDown_previewDuration
            // 
            this.numericUpDown_previewDuration.Location = new Point(303, 317);
            this.numericUpDown_previewDuration.Maximum = new decimal(new int[] { 60, 0, 0, 0 });
            this.numericUpDown_previewDuration.Minimum = new decimal(new int[] { 3, 0, 0, 0 });
            this.numericUpDown_previewDuration.Name = "numericUpDown_previewDuration";
            this.numericUpDown_previewDuration.Size = new Size(63, 23);
            this.numericUpDown_previewDuration.TabIndex = 27;
            this.numericUpDown_previewDuration.Value = new decimal(new int[] { 8, 0, 0, 0 });
            // 
            // label_info_previewDuration
            // 
            this.label_info_previewDuration.AutoSize = true;
            this.label_info_previewDuration.Location = new Point(303, 299);
            this.label_info_previewDuration.Name = "label_info_previewDuration";
            this.label_info_previewDuration.Size = new Size(53, 15);
            this.label_info_previewDuration.TabIndex = 28;
            this.label_info_previewDuration.Text = "Duration";
            // 
            // button_backColor
            // 
            this.button_backColor.BackColor = Color.Black;
            this.button_backColor.Font = new Font("Bahnschrift Condensed", 9F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.button_backColor.ForeColor = Color.White;
            this.button_backColor.Location = new Point(240, 317);
            this.button_backColor.Name = "button_backColor";
            this.button_backColor.Size = new Size(57, 23);
            this.button_backColor.TabIndex = 29;
            this.button_backColor.Text = "Back";
            this.button_backColor.UseVisualStyleBackColor = false;
            this.button_backColor.Click += this.button_backColor_Click;
            // 
            // button_colorGraph
            // 
            this.button_colorGraph.BackColor = Color.White;
            this.button_colorGraph.Font = new Font("Bahnschrift Condensed", 9F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.button_colorGraph.ForeColor = SystemColors.ControlText;
            this.button_colorGraph.Location = new Point(240, 288);
            this.button_colorGraph.Name = "button_colorGraph";
            this.button_colorGraph.Size = new Size(57, 23);
            this.button_colorGraph.TabIndex = 30;
            this.button_colorGraph.Text = "Graph";
            this.button_colorGraph.UseVisualStyleBackColor = false;
            this.button_colorGraph.Click += this.button_colorGraph_Click;
            // 
            // label_info_thickness
            // 
            this.label_info_thickness.AutoSize = true;
            this.label_info_thickness.Location = new Point(12, 244);
            this.label_info_thickness.Name = "label_info_thickness";
            this.label_info_thickness.Size = new Size(59, 15);
            this.label_info_thickness.TabIndex = 32;
            this.label_info_thickness.Text = "Thickness";
            // 
            // numericUpDown_thickness
            // 
            this.numericUpDown_thickness.Location = new Point(12, 262);
            this.numericUpDown_thickness.Maximum = new decimal(new int[] { 64, 0, 0, 0 });
            this.numericUpDown_thickness.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_thickness.Name = "numericUpDown_thickness";
            this.numericUpDown_thickness.Size = new Size(63, 23);
            this.numericUpDown_thickness.TabIndex = 31;
            this.numericUpDown_thickness.Value = new decimal(new int[] { 1, 0, 0, 0 });
            // 
            // label_info_threshold
            // 
            this.label_info_threshold.AutoSize = true;
            this.label_info_threshold.Location = new Point(88, 244);
            this.label_info_threshold.Name = "label_info_threshold";
            this.label_info_threshold.Size = new Size(60, 15);
            this.label_info_threshold.TabIndex = 34;
            this.label_info_threshold.Text = "Threshold";
            // 
            // numericUpDown_threshold
            // 
            this.numericUpDown_threshold.DecimalPlaces = 5;
            this.numericUpDown_threshold.Increment = new decimal(new int[] { 50, 0, 0, 327680 });
            this.numericUpDown_threshold.Location = new Point(88, 262);
            this.numericUpDown_threshold.Maximum = new decimal(new int[] { 9999, 0, 0, 262144 });
            this.numericUpDown_threshold.Name = "numericUpDown_threshold";
            this.numericUpDown_threshold.Size = new Size(63, 23);
            this.numericUpDown_threshold.TabIndex = 33;
            this.numericUpDown_threshold.Value = new decimal(new int[] { 50, 0, 0, 196608 });
            // 
            // button_addSubtitlesTxt
            // 
            this.button_addSubtitlesTxt.Location = new Point(303, 199);
            this.button_addSubtitlesTxt.Name = "button_addSubtitlesTxt";
            this.button_addSubtitlesTxt.Size = new Size(149, 23);
            this.button_addSubtitlesTxt.TabIndex = 35;
            this.button_addSubtitlesTxt.Text = "Add Subtitles TXT";
            this.button_addSubtitlesTxt.UseVisualStyleBackColor = true;
            this.button_addSubtitlesTxt.Click += this.button_addSubtitlesTxt_Click;
            // 
            // VisualizerDialog
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(464, 441);
            this.Controls.Add(this.comboBox_gpuDevice);
            this.Controls.Add(this.label_info_gpuDevice);
            this.Controls.Add(this.button_addSubtitlesTxt);
            this.Controls.Add(this.label_info_threshold);
            this.Controls.Add(this.numericUpDown_threshold);
            this.Controls.Add(this.label_info_thickness);
            this.Controls.Add(this.numericUpDown_thickness);
            this.Controls.Add(this.button_colorGraph);
            this.Controls.Add(this.button_backColor);
            this.Controls.Add(this.label_info_previewDuration);
            this.Controls.Add(this.numericUpDown_previewDuration);
            this.Controls.Add(this.button_preview);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.comboBox_visPreset);
            this.Controls.Add(this.comboBox_mode);
            this.Controls.Add(this.label_percentage);
            this.Controls.Add(this.button_codecInfo);
            this.Controls.Add(this.comboBox_codecPreset);
            this.Controls.Add(this.label_framesPerSec);
            this.Controls.Add(this.label_time);
            this.Controls.Add(this.label_amplification);
            this.Controls.Add(this.numericUpDown_amplification);
            this.Controls.Add(this.label_info_volume);
            this.Controls.Add(this.numericUpDown_volume);
            this.Controls.Add(this.label_cuda);
            this.Controls.Add(this.label_info_threads);
            this.Controls.Add(this.numericUpDown_threads);
            this.Controls.Add(this.checkBox_copyPath);
            this.Controls.Add(this.label_sizeApprox);
            this.Controls.Add(this.label_info_endSeconds);
            this.Controls.Add(this.numericUpDown_endSeconds);
            this.Controls.Add(this.label_info_startSeconds);
            this.Controls.Add(this.numericUpDown_startSeconds);
            this.Controls.Add(this.label_info_frameRate);
            this.Controls.Add(this.numericUpDown_frameRate);
            this.Controls.Add(this.comboBox_resolution);
            this.Controls.Add(this.progressBar_rendering);
            this.Controls.Add(this.button_render);
            this.MaximumSize = new Size(480, 480);
            this.MinimumSize = new Size(480, 480);
            this.Name = "VisualizerDialog";
            this.Text = "VisualizerDialog";
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_frameRate).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_startSeconds).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_endSeconds).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_threads).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_volume).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_amplification).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_previewDuration).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_thickness).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_threshold).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private Button button_render;
		private ProgressBar progressBar_rendering;
		private ComboBox comboBox_resolution;
		private NumericUpDown numericUpDown_frameRate;
		private Label label_info_frameRate;
		private NumericUpDown numericUpDown_startSeconds;
		private Label label_info_startSeconds;
		private Label label_info_endSeconds;
		private NumericUpDown numericUpDown_endSeconds;
		private Label label_sizeApprox;
		private CheckBox checkBox_copyPath;
		private NumericUpDown numericUpDown_threads;
		private Label label_info_threads;
		private Label label_cuda;
		private Label label_info_gpuDevice;
		private ComboBox comboBox_gpuDevice;
		private NumericUpDown numericUpDown_volume;
		private Label label_info_volume;
        private Label label_amplification;
        private NumericUpDown numericUpDown_amplification;
        private Label label_time;
        private Label label_framesPerSec;
        private ComboBox comboBox_codecPreset;
        private Button button_codecInfo;
        private Label label_percentage;
        private ComboBox comboBox_mode;
        private ComboBox comboBox_visPreset;
        private Label label1;
        private Button button_preview;
        private NumericUpDown numericUpDown_previewDuration;
        private Label label_info_previewDuration;
        private Button button_backColor;
		private Button button_colorGraph;
		private Label label_info_thickness;
		private NumericUpDown numericUpDown_thickness;
		private Label label_info_threshold;
		private NumericUpDown numericUpDown_threshold;
        private Button button_addSubtitlesTxt;
    }
}