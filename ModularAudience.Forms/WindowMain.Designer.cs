namespace ModularAudience.Forms
{
    partial class WindowMain
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.button_import = new Button();
            this.button_browse = new Button();
            this.checkBox_singleCollection = new CheckBox();
            this.SuspendLayout();
            // 
            // button_import
            // 
            this.button_import.BackColor = Color.FromArgb(  255,   255,   192);
            this.button_import.Location = new Point(12, 12);
            this.button_import.Name = "button_import";
            this.button_import.Size = new Size(75, 23);
            this.button_import.TabIndex = 0;
            this.button_import.TabStop = false;
            this.button_import.Text = "Import";
            this.button_import.UseVisualStyleBackColor = false;
            this.button_import.Click += this.button_import_Click;
            // 
            // button_browse
            // 
            this.button_browse.Font = new Font("Bahnschrift", 9F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.button_browse.Location = new Point(93, 12);
            this.button_browse.Name = "button_browse";
            this.button_browse.Size = new Size(32, 23);
            this.button_browse.TabIndex = 1;
            this.button_browse.Text = "[...]";
            this.button_browse.UseVisualStyleBackColor = true;
            this.button_browse.Click += this.button_browse_Click;
            // 
            // checkBox_singleCollection
            // 
            this.checkBox_singleCollection.AutoSize = true;
            this.checkBox_singleCollection.Location = new Point(12, 41);
            this.checkBox_singleCollection.Name = "checkBox_singleCollection";
            this.checkBox_singleCollection.Size = new Size(115, 19);
            this.checkBox_singleCollection.TabIndex = 2;
            this.checkBox_singleCollection.Text = "Single Collection";
            this.checkBox_singleCollection.UseVisualStyleBackColor = true;
            // 
            // WindowMain
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(704, 321);
            this.Controls.Add(this.checkBox_singleCollection);
            this.Controls.Add(this.button_browse);
            this.Controls.Add(this.button_import);
            this.Name = "WindowMain";
            this.Text = "ModularAudience (Main Control)";
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private Button button_import;
        private Button button_browse;
        private CheckBox checkBox_singleCollection;
    }
}
