namespace ModularAudience.Forms.Modules
{
    partial class LoopControl
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
            this.panel_buttons = new Panel();
            this.button_loop = new Button();
            this.button_copy = new Button();
            this.numericUpDown_multiplier = new NumericUpDown();
            this.label_info_multiplier = new Label();
            this.numericUpDown_jump = new NumericUpDown();
            this.button_forward = new Button();
            this.button_backward = new Button();
            this.label_info_jump = new Label();
            this.panel_buttons.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_multiplier).BeginInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_jump).BeginInit();
            this.SuspendLayout();
            // 
            // panel_buttons
            // 
            this.panel_buttons.BackColor = SystemColors.ButtonFace;
            this.panel_buttons.Controls.Add(this.button_loop);
            this.panel_buttons.Location = new Point(12, 70);
            this.panel_buttons.Name = "panel_buttons";
            this.panel_buttons.Size = new Size(435, 29);
            this.panel_buttons.TabIndex = 0;
            // 
            // button_loop
            // 
            this.button_loop.Font = new Font("Bahnschrift Light Condensed", 8.25F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.button_loop.Location = new Point(3, 3);
            this.button_loop.Name = "button_loop";
            this.button_loop.Size = new Size(23, 23);
            this.button_loop.TabIndex = 1;
            this.button_loop.Text = "4";
            this.button_loop.UseVisualStyleBackColor = true;
            // 
            // button_copy
            // 
            this.button_copy.Font = new Font("Bahnschrift Light Condensed", 8.25F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.button_copy.Location = new Point(453, 73);
            this.button_copy.Name = "button_copy";
            this.button_copy.Size = new Size(23, 23);
            this.button_copy.TabIndex = 2;
            this.button_copy.TabStop = false;
            this.button_copy.Text = "⿻";
            this.button_copy.UseVisualStyleBackColor = true;
            this.button_copy.Click += this.button_copy_Click;
            // 
            // numericUpDown_multiplier
            // 
            this.numericUpDown_multiplier.Location = new Point(12, 44);
            this.numericUpDown_multiplier.Maximum = new decimal(new int[] { 32, 0, 0, 0 });
            this.numericUpDown_multiplier.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_multiplier.Name = "numericUpDown_multiplier";
            this.numericUpDown_multiplier.Size = new Size(40, 23);
            this.numericUpDown_multiplier.TabIndex = 3;
            this.numericUpDown_multiplier.Value = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_multiplier.ValueChanged += this.numericUpDown_multiplier_ValueChanged;
            // 
            // label_info_multiplier
            // 
            this.label_info_multiplier.AutoSize = true;
            this.label_info_multiplier.Location = new Point(12, 26);
            this.label_info_multiplier.Name = "label_info_multiplier";
            this.label_info_multiplier.Size = new Size(35, 15);
            this.label_info_multiplier.TabIndex = 4;
            this.label_info_multiplier.Text = "Multi";
            // 
            // numericUpDown_jump
            // 
            this.numericUpDown_jump.Location = new Point(368, 41);
            this.numericUpDown_jump.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
            this.numericUpDown_jump.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_jump.Name = "numericUpDown_jump";
            this.numericUpDown_jump.Size = new Size(50, 23);
            this.numericUpDown_jump.TabIndex = 5;
            this.numericUpDown_jump.Value = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_jump.ValueChanged += this.numericUpDown_jump_ValueChanged;
            // 
            // button_forward
            // 
            this.button_forward.Font = new Font("Bahnschrift Light Condensed", 8.25F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.button_forward.Location = new Point(424, 41);
            this.button_forward.Name = "button_forward";
            this.button_forward.Size = new Size(23, 23);
            this.button_forward.TabIndex = 6;
            this.button_forward.TabStop = false;
            this.button_forward.Text = "→";
            this.button_forward.UseVisualStyleBackColor = true;
            this.button_forward.Click += this.button_forward_Click;
            // 
            // button_backward
            // 
            this.button_backward.Font = new Font("Bahnschrift Light Condensed", 8.25F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.button_backward.Location = new Point(339, 41);
            this.button_backward.Name = "button_backward";
            this.button_backward.Size = new Size(23, 23);
            this.button_backward.TabIndex = 7;
            this.button_backward.TabStop = false;
            this.button_backward.Text = "←";
            this.button_backward.UseVisualStyleBackColor = true;
            this.button_backward.Click += this.button_backward_Click;
            // 
            // label_info_jump
            // 
            this.label_info_jump.AutoSize = true;
            this.label_info_jump.Location = new Point(368, 23);
            this.label_info_jump.Name = "label_info_jump";
            this.label_info_jump.Size = new Size(55, 15);
            this.label_info_jump.TabIndex = 8;
            this.label_info_jump.Text = "Jump ms";
            // 
            // LoopControl
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(484, 111);
            this.Controls.Add(this.label_info_jump);
            this.Controls.Add(this.button_backward);
            this.Controls.Add(this.button_forward);
            this.Controls.Add(this.numericUpDown_jump);
            this.Controls.Add(this.label_info_multiplier);
            this.Controls.Add(this.numericUpDown_multiplier);
            this.Controls.Add(this.button_copy);
            this.Controls.Add(this.panel_buttons);
            this.MaximizeBox = false;
            this.MaximumSize = new Size(500, 150);
            this.MinimizeBox = false;
            this.MinimumSize = new Size(500, 150);
            this.Name = "LoopControl";
            this.Text = "Loop Control";
            this.panel_buttons.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_multiplier).EndInit();
            ((System.ComponentModel.ISupportInitialize) this.numericUpDown_jump).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private Panel panel_buttons;
        private Button button_loop;
        private Button button_copy;
        private NumericUpDown numericUpDown_multiplier;
        private Label label_info_multiplier;
        private NumericUpDown numericUpDown_jump;
        private Button button_forward;
        private Button button_backward;
        private Label label_info_jump;
    }
}