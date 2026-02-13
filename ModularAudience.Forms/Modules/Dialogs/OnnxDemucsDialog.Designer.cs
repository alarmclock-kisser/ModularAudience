namespace ModularAudience.Forms
{
    partial class OnnxDemucsDialog
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
            this.comboBox_models = new ComboBox();
            this.progressBar_inferencing = new ProgressBar();
            this.button_run = new Button();
            this.checkedListBox_stems = new CheckedListBox();
            this.SuspendLayout();
            // 
            // comboBox_models
            // 
            this.comboBox_models.FormattingEnabled = true;
            this.comboBox_models.Location = new Point(12, 12);
            this.comboBox_models.Name = "comboBox_models";
            this.comboBox_models.Size = new Size(229, 23);
            this.comboBox_models.TabIndex = 0;
            this.comboBox_models.Text = "Select onnx demucs model";
            this.comboBox_models.SelectedIndexChanged += this.comboBox_models_SelectedIndexChanged;
            // 
            // progressBar_inferencing
            // 
            this.progressBar_inferencing.Location = new Point(12, 286);
            this.progressBar_inferencing.Name = "progressBar_inferencing";
            this.progressBar_inferencing.Size = new Size(359, 23);
            this.progressBar_inferencing.TabIndex = 1;
            // 
            // button_run
            // 
            this.button_run.Location = new Point(377, 286);
            this.button_run.Name = "button_run";
            this.button_run.Size = new Size(75, 23);
            this.button_run.TabIndex = 2;
            this.button_run.Text = "Run";
            this.button_run.UseVisualStyleBackColor = true;
            this.button_run.Click += this.button_Run_Click;
            // 
            // checkedListBox_stems
            // 
            this.checkedListBox_stems.CheckOnClick = true;
            this.checkedListBox_stems.FormattingEnabled = true;
            this.checkedListBox_stems.Location = new Point(12, 132);
            this.checkedListBox_stems.Name = "checkedListBox_stems";
            this.checkedListBox_stems.Size = new Size(140, 148);
            this.checkedListBox_stems.TabIndex = 3;
            // 
            // OnnxDemucsDialog
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(464, 321);
            this.Controls.Add(this.checkedListBox_stems);
            this.Controls.Add(this.button_run);
            this.Controls.Add(this.progressBar_inferencing);
            this.Controls.Add(this.comboBox_models);
            this.MaximumSize = new Size(480, 360);
            this.MinimumSize = new Size(480, 360);
            this.Name = "OnnxDemucsDialog";
            this.Text = "OnnxDemucsDialog";
            this.ResumeLayout(false);
        }

        #endregion

        private ComboBox comboBox_models;
        private ProgressBar progressBar_inferencing;
        private Button button_run;
        private CheckedListBox checkedListBox_stems;
    }
}