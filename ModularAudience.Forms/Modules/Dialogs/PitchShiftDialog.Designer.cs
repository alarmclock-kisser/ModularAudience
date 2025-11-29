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
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_range).BeginInit();
            this.SuspendLayout();
            // 
            // listBox_samples
            // 
            this.listBox_samples.FormattingEnabled = true;
            this.listBox_samples.Location = new Point(12, 12);
            this.listBox_samples.Name = "listBox_samples";
            this.listBox_samples.Size = new Size(160, 199);
            this.listBox_samples.TabIndex = 0;
            // 
            // numericUpDown_range
            // 
            this.numericUpDown_range.Location = new Point(12, 286);
            this.numericUpDown_range.Maximum = new decimal(new int[] { 128, 0, 0, 0 });
            this.numericUpDown_range.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_range.Name = "numericUpDown_range";
            this.numericUpDown_range.Size = new Size(60, 23);
            this.numericUpDown_range.TabIndex = 1;
            this.numericUpDown_range.Value = new decimal(new int[] { 8, 0, 0, 0 });
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
            this.button_create.Text = "Create";
            this.button_create.UseVisualStyleBackColor = false;
            this.button_create.Click += this.button_create_Click;
            // 
            // PitchShiftDialog
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(464, 321);
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
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private ListBox listBox_samples;
        private NumericUpDown numericUpDown_range;
        private Label label_info_range;
        private Button button_create;
    }
}