namespace ModularAudience.Forms.Modules
{
	partial class CudaFunctions
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
			this.comboBox_devices = new ComboBox();
			this.button_initialize = new Button();
			this.listBox_pointers = new ListBox();
			this.label_status = new Label();
			this.button_write = new Button();
			this.button_fft = new Button();
			this.numericUpDown_chunkSize = new NumericUpDown();
			this.numericUpDown_overlap = new NumericUpDown();
			this.label_info_chunkSize = new Label();
			this.label_info_overlap = new Label();
			this.listBox_log = new ListBox();
			this.comboBox_kernels = new ComboBox();
			this.button_execute = new Button();
			this.panel_arguments = new Panel();
			((System.ComponentModel.ISupportInitialize) this.numericUpDown_chunkSize).BeginInit();
			((System.ComponentModel.ISupportInitialize) this.numericUpDown_overlap).BeginInit();
			this.SuspendLayout();
			// 
			// comboBox_devices
			// 
			this.comboBox_devices.FormattingEnabled = true;
			this.comboBox_devices.Location = new Point(12, 12);
			this.comboBox_devices.Name = "comboBox_devices";
			this.comboBox_devices.Size = new Size(240, 23);
			this.comboBox_devices.TabIndex = 0;
			this.comboBox_devices.Text = "Select CUDA device ...";
			this.comboBox_devices.SelectedIndexChanged += this.comboBox_devices_SelectedIndexChanged;
			// 
			// button_initialize
			// 
			this.button_initialize.Location = new Point(258, 12);
			this.button_initialize.Name = "button_initialize";
			this.button_initialize.Size = new Size(75, 23);
			this.button_initialize.TabIndex = 1;
			this.button_initialize.Text = "Initialize";
			this.button_initialize.UseVisualStyleBackColor = true;
			this.button_initialize.Click += this.button_initialize_Click;
			// 
			// listBox_pointers
			// 
			this.listBox_pointers.FormattingEnabled = true;
			this.listBox_pointers.Location = new Point(12, 56);
			this.listBox_pointers.Name = "listBox_pointers";
			this.listBox_pointers.Size = new Size(321, 169);
			this.listBox_pointers.TabIndex = 2;
			this.listBox_pointers.SelectedIndexChanged += this.listBox_pointers_SelectedIndexChanged;
			// 
			// label_status
			// 
			this.label_status.AutoSize = true;
			this.label_status.Location = new Point(12, 38);
			this.label_status.Name = "label_status";
			this.label_status.Size = new Size(158, 15);
			this.label_status.TabIndex = 3;
			this.label_status.Text = "No status message available.";
			// 
			// button_write
			// 
			this.button_write.BackColor = SystemColors.Info;
			this.button_write.Location = new Point(258, 231);
			this.button_write.Name = "button_write";
			this.button_write.Size = new Size(75, 23);
			this.button_write.TabIndex = 5;
			this.button_write.Text = "Write";
			this.button_write.UseVisualStyleBackColor = false;
			this.button_write.Click += this.button_write_Click;
			// 
			// button_fft
			// 
			this.button_fft.Location = new Point(339, 275);
			this.button_fft.Name = "button_fft";
			this.button_fft.Size = new Size(75, 23);
			this.button_fft.TabIndex = 6;
			this.button_fft.Text = "Fourier";
			this.button_fft.UseVisualStyleBackColor = true;
			this.button_fft.Click += this.button_fft_Click;
			// 
			// numericUpDown_chunkSize
			// 
			this.numericUpDown_chunkSize.Location = new Point(12, 231);
			this.numericUpDown_chunkSize.Maximum = new decimal(new int[] { 65536, 0, 0, 0 });
			this.numericUpDown_chunkSize.Minimum = new decimal(new int[] { 128, 0, 0, 0 });
			this.numericUpDown_chunkSize.Name = "numericUpDown_chunkSize";
			this.numericUpDown_chunkSize.Size = new Size(70, 23);
			this.numericUpDown_chunkSize.TabIndex = 7;
			this.numericUpDown_chunkSize.Tag = "2048";
			this.numericUpDown_chunkSize.Value = new decimal(new int[] { 2048, 0, 0, 0 });
			this.numericUpDown_chunkSize.ValueChanged += this.numericUpDown_chunkSize_ValueChanged;
			// 
			// numericUpDown_overlap
			// 
			this.numericUpDown_overlap.DecimalPlaces = 3;
			this.numericUpDown_overlap.Increment = new decimal(new int[] { 5, 0, 0, 196608 });
			this.numericUpDown_overlap.Location = new Point(88, 231);
			this.numericUpDown_overlap.Maximum = new decimal(new int[] { 99, 0, 0, 131072 });
			this.numericUpDown_overlap.Name = "numericUpDown_overlap";
			this.numericUpDown_overlap.Size = new Size(50, 23);
			this.numericUpDown_overlap.TabIndex = 8;
			this.numericUpDown_overlap.Value = new decimal(new int[] { 5, 0, 0, 65536 });
			// 
			// label_info_chunkSize
			// 
			this.label_info_chunkSize.AutoSize = true;
			this.label_info_chunkSize.Location = new Point(12, 257);
			this.label_info_chunkSize.Name = "label_info_chunkSize";
			this.label_info_chunkSize.Size = new Size(65, 15);
			this.label_info_chunkSize.TabIndex = 9;
			this.label_info_chunkSize.Text = "Chunk Size";
			// 
			// label_info_overlap
			// 
			this.label_info_overlap.AutoSize = true;
			this.label_info_overlap.Location = new Point(88, 257);
			this.label_info_overlap.Name = "label_info_overlap";
			this.label_info_overlap.Size = new Size(48, 15);
			this.label_info_overlap.TabIndex = 10;
			this.label_info_overlap.Text = "Overlap";
			// 
			// listBox_log
			// 
			this.listBox_log.FormattingEnabled = true;
			this.listBox_log.HorizontalScrollbar = true;
			this.listBox_log.Location = new Point(12, 275);
			this.listBox_log.Name = "listBox_log";
			this.listBox_log.Size = new Size(321, 169);
			this.listBox_log.TabIndex = 11;
			// 
			// comboBox_kernels
			// 
			this.comboBox_kernels.FormattingEnabled = true;
			this.comboBox_kernels.Location = new Point(420, 12);
			this.comboBox_kernels.Name = "comboBox_kernels";
			this.comboBox_kernels.Size = new Size(287, 23);
			this.comboBox_kernels.TabIndex = 13;
			this.comboBox_kernels.Text = "Select a CUDA audio kernel ...";
			this.comboBox_kernels.SelectedIndexChanged += this.comboBox_kernels_SelectedIndexChanged;
			// 
			// button_execute
			// 
			this.button_execute.Location = new Point(713, 12);
			this.button_execute.Name = "button_execute";
			this.button_execute.Size = new Size(75, 23);
			this.button_execute.TabIndex = 14;
			this.button_execute.Text = "Execute";
			this.button_execute.UseVisualStyleBackColor = true;
			this.button_execute.Click += this.button_execute_Click;
			// 
			// panel_arguments
			// 
			this.panel_arguments.BackColor = SystemColors.ControlLight;
			this.panel_arguments.Location = new Point(420, 41);
			this.panel_arguments.Name = "panel_arguments";
			this.panel_arguments.Size = new Size(287, 213);
			this.panel_arguments.TabIndex = 15;
			// 
			// CudaFunctions
			// 
			this.AutoScaleDimensions = new SizeF(7F, 15F);
			this.AutoScaleMode = AutoScaleMode.Font;
			this.ClientSize = new Size(800, 450);
			this.Controls.Add(this.panel_arguments);
			this.Controls.Add(this.button_execute);
			this.Controls.Add(this.comboBox_kernels);
			this.Controls.Add(this.listBox_log);
			this.Controls.Add(this.label_info_overlap);
			this.Controls.Add(this.label_info_chunkSize);
			this.Controls.Add(this.numericUpDown_overlap);
			this.Controls.Add(this.numericUpDown_chunkSize);
			this.Controls.Add(this.button_fft);
			this.Controls.Add(this.button_write);
			this.Controls.Add(this.label_status);
			this.Controls.Add(this.listBox_pointers);
			this.Controls.Add(this.button_initialize);
			this.Controls.Add(this.comboBox_devices);
			this.Name = "CudaFunctions";
			this.Text = "CUDA Functions";
			((System.ComponentModel.ISupportInitialize) this.numericUpDown_chunkSize).EndInit();
			((System.ComponentModel.ISupportInitialize) this.numericUpDown_overlap).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();
		}

		#endregion

		private ComboBox comboBox_devices;
		private Button button_initialize;
		private ListBox listBox_pointers;
		private Label label_status;
		private Button button_write;
		private Button button_fft;
		private NumericUpDown numericUpDown_chunkSize;
		private NumericUpDown numericUpDown_overlap;
		private Label label_info_chunkSize;
		private Label label_info_overlap;
		private ListBox listBox_log;
		private ComboBox comboBox_kernels;
		private Button button_execute;
		private Panel panel_arguments;
	}
}