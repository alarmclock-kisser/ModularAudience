namespace ModularAudience.Forms.Modules
{
    partial class DeveloperFunctions
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
            this.comboBox_methods = new ComboBox();
            this.panel_parameters = new Panel();
            this.button_run = new Button();
            this.numericUpDown_maxProcessors = new NumericUpDown();
            this.label_info_threads = new Label();
            this.progressBar_processing = new ProgressBar();
            this.label_elapsedProcessingTime = new Label();
            this.label_trackName = new Label();
            this.comboBox_track = new ComboBox();
            this.textBox_trackInfo = new TextBox();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_maxProcessors).BeginInit();
            this.SuspendLayout();
            // 
            // comboBox_methods
            // 
            this.comboBox_methods.FormattingEnabled = true;
            this.comboBox_methods.Location = new Point(12, 12);
            this.comboBox_methods.Name = "comboBox_methods";
            this.comboBox_methods.Size = new Size(240, 23);
            this.comboBox_methods.TabIndex = 0;
            this.comboBox_methods.Text = "Select a method ...";
            // 
            // panel_parameters
            // 
            this.panel_parameters.BackColor = SystemColors.ControlLight;
            this.panel_parameters.Location = new Point(12, 41);
            this.panel_parameters.Name = "panel_parameters";
            this.panel_parameters.Size = new Size(240, 280);
            this.panel_parameters.TabIndex = 1;
            // 
            // button_run
            // 
            this.button_run.BackColor = SystemColors.Info;
            this.button_run.Location = new Point(177, 327);
            this.button_run.Name = "button_run";
            this.button_run.Size = new Size(75, 23);
            this.button_run.TabIndex = 2;
            this.button_run.Text = "Run";
            this.button_run.UseVisualStyleBackColor = false;
            // 
            // numericUpDown_maxProcessors
            // 
            this.numericUpDown_maxProcessors.Location = new Point(12, 327);
            this.numericUpDown_maxProcessors.Maximum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_maxProcessors.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_maxProcessors.Name = "numericUpDown_maxProcessors";
            this.numericUpDown_maxProcessors.Size = new Size(50, 23);
            this.numericUpDown_maxProcessors.TabIndex = 3;
            this.numericUpDown_maxProcessors.Value = new decimal(new int[] { 1, 0, 0, 0 });
            // 
            // label_info_threads
            // 
            this.label_info_threads.AutoSize = true;
            this.label_info_threads.Location = new Point(12, 353);
            this.label_info_threads.Name = "label_info_threads";
            this.label_info_threads.Size = new Size(49, 15);
            this.label_info_threads.TabIndex = 4;
            this.label_info_threads.Text = "Threads";
            // 
            // progressBar_processing
            // 
            this.progressBar_processing.Location = new Point(12, 406);
            this.progressBar_processing.Name = "progressBar_processing";
            this.progressBar_processing.Size = new Size(240, 23);
            this.progressBar_processing.TabIndex = 5;
            // 
            // label_elapsedProcessingTime
            // 
            this.label_elapsedProcessingTime.AutoSize = true;
            this.label_elapsedProcessingTime.Location = new Point(211, 388);
            this.label_elapsedProcessingTime.Name = "label_elapsedProcessingTime";
            this.label_elapsedProcessingTime.Size = new Size(30, 15);
            this.label_elapsedProcessingTime.TabIndex = 6;
            this.label_elapsedProcessingTime.Text = "--:--";
            // 
            // label_trackName
            // 
            this.label_trackName.AutoSize = true;
            this.label_trackName.Location = new Point(372, 38);
            this.label_trackName.Name = "label_trackName";
            this.label_trackName.Size = new Size(151, 15);
            this.label_trackName.TabIndex = 7;
            this.label_trackName.Text = "No track currently selected.";
            // 
            // comboBox_track
            // 
            this.comboBox_track.FormattingEnabled = true;
            this.comboBox_track.Location = new Point(372, 12);
            this.comboBox_track.Name = "comboBox_track";
            this.comboBox_track.Size = new Size(240, 23);
            this.comboBox_track.TabIndex = 8;
            this.comboBox_track.Text = "Auto last focussed track";
            this.comboBox_track.SelectedIndexChanged += this.comboBox_track_SelectedIndexChanged;
            // 
            // textBox_trackInfo
            // 
            this.textBox_trackInfo.BackColor = SystemColors.ControlLight;
            this.textBox_trackInfo.Location = new Point(372, 56);
            this.textBox_trackInfo.Multiline = true;
            this.textBox_trackInfo.Name = "textBox_trackInfo";
            this.textBox_trackInfo.PlaceholderText = "Track info data here";
            this.textBox_trackInfo.ReadOnly = true;
            this.textBox_trackInfo.Size = new Size(240, 265);
            this.textBox_trackInfo.TabIndex = 9;
            this.textBox_trackInfo.TabStop = false;
            // 
            // DeveloperFunctions
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(624, 441);
            this.Controls.Add(this.textBox_trackInfo);
            this.Controls.Add(this.comboBox_track);
            this.Controls.Add(this.label_trackName);
            this.Controls.Add(this.label_elapsedProcessingTime);
            this.Controls.Add(this.progressBar_processing);
            this.Controls.Add(this.label_info_threads);
            this.Controls.Add(this.numericUpDown_maxProcessors);
            this.Controls.Add(this.button_run);
            this.Controls.Add(this.panel_parameters);
            this.Controls.Add(this.comboBox_methods);
            this.Name = "DeveloperFunctions";
            this.Text = "Developer Functions";
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_maxProcessors).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private ComboBox comboBox_methods;
        private Panel panel_parameters;
        private Button button_run;
        private NumericUpDown numericUpDown_maxProcessors;
        private Label label_info_threads;
        private ProgressBar progressBar_processing;
        private Label label_elapsedProcessingTime;
        private Label label_trackName;
        private ComboBox comboBox_track;
        private TextBox textBox_trackInfo;
    }
}