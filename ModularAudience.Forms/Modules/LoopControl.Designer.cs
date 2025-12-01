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
            this.panel_buttons.SuspendLayout();
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
            // LoopControl
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(484, 111);
            this.Controls.Add(this.panel_buttons);
            this.MaximizeBox = false;
            this.MaximumSize = new Size(500, 150);
            this.MinimizeBox = false;
            this.MinimumSize = new Size(500, 150);
            this.Name = "LoopControl";
            this.Text = "Loop Control";
            this.panel_buttons.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        private Panel panel_buttons;
        private Button button_loop;
    }
}