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
            this.checkBox_autoParameters = new CheckBox();
            this.checkBox_optionalParameters = new CheckBox();
            this.button_apply = new Button();
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
            this.comboBox_methods.SelectedIndexChanged += this.comboBox_methods_SelectedIndexChanged;
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
            this.button_run.BackColor = Color.FromArgb(  255,   192,   192);
            this.button_run.Location = new Point(258, 406);
            this.button_run.Name = "button_run";
            this.button_run.Size = new Size(75, 23);
            this.button_run.TabIndex = 2;
            this.button_run.Text = "Run";
            this.button_run.UseVisualStyleBackColor = false;
            this.button_run.Click += this.button_run_Click;
            // 
            // numericUpDown_maxProcessors
            // 
            this.numericUpDown_maxProcessors.Location = new Point(339, 406);
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
            this.label_info_threads.Location = new Point(339, 388);
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
            // checkBox_autoParameters
            // 
            this.checkBox_autoParameters.AutoSize = true;
            this.checkBox_autoParameters.Font = new Font("Bahnschrift SemiCondensed", 9F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.checkBox_autoParameters.Location = new Point(12, 327);
            this.checkBox_autoParameters.Margin = new Padding(1, 3, 0, 3);
            this.checkBox_autoParameters.Name = "checkBox_autoParameters";
            this.checkBox_autoParameters.Size = new Size(108, 18);
            this.checkBox_autoParameters.TabIndex = 40;
            this.checkBox_autoParameters.Text = "Auto Parameters";
            this.checkBox_autoParameters.UseVisualStyleBackColor = true;
            this.checkBox_autoParameters.CheckedChanged += this.checkBox_autoParameters_CheckedChanged;
            // 
            // checkBox_optionalParameters
            // 
            this.checkBox_optionalParameters.AutoSize = true;
            this.checkBox_optionalParameters.Checked = true;
            this.checkBox_optionalParameters.CheckState = CheckState.Checked;
            this.checkBox_optionalParameters.Font = new Font("Bahnschrift SemiCondensed", 9F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.checkBox_optionalParameters.Location = new Point(128, 327);
            this.checkBox_optionalParameters.Margin = new Padding(1, 3, 1, 3);
            this.checkBox_optionalParameters.Name = "checkBox_optionalParameters";
            this.checkBox_optionalParameters.Size = new Size(126, 18);
            this.checkBox_optionalParameters.TabIndex = 41;
            this.checkBox_optionalParameters.Text = "Optional Parameters";
            this.checkBox_optionalParameters.UseVisualStyleBackColor = true;
            this.checkBox_optionalParameters.CheckedChanged += this.checkBox_optionalParameters_CheckedChanged;
            // 
            // button_apply
            // 
            this.button_apply.BackColor = SystemColors.Info;
            this.button_apply.Location = new Point(537, 406);
            this.button_apply.Name = "button_apply";
            this.button_apply.Size = new Size(75, 23);
            this.button_apply.TabIndex = 42;
            this.button_apply.Text = "Apply";
            this.button_apply.UseVisualStyleBackColor = false;
            this.button_apply.Click += this.button_apply_Click;
            // 
            // DeveloperFunctions
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(624, 441);
            this.Controls.Add(this.button_apply);
            this.Controls.Add(this.checkBox_autoParameters);
            this.Controls.Add(this.checkBox_optionalParameters);
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
            this.MaximizeBox = false;
            this.MaximumSize = new Size(640, 480);
            this.MinimumSize = new Size(640, 480);
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
        private CheckBox checkBox_autoParameters;
        private CheckBox checkBox_optionalParameters;
        private Button button_apply;
    }
}