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
            this.button_scanBpm = new Button();
            this.textBox_scanBpmResult = new TextBox();
            this.textBox_scanTimingResult = new TextBox();
            this.button_scanTiming = new Button();
            this.textBox_scanKeyResult = new TextBox();
            this.button_scanKey = new Button();
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
            // button_scanBpm
            // 
            this.button_scanBpm.Location = new Point(511, 286);
            this.button_scanBpm.Name = "button_scanBpm";
            this.button_scanBpm.Size = new Size(75, 23);
            this.button_scanBpm.TabIndex = 3;
            this.button_scanBpm.TabStop = false;
            this.button_scanBpm.Text = "Scan BPM";
            this.button_scanBpm.UseVisualStyleBackColor = true;
            this.button_scanBpm.Click += this.button_scanBpm_Click;
            // 
            // textBox_scanBpmResult
            // 
            this.textBox_scanBpmResult.Location = new Point(592, 286);
            this.textBox_scanBpmResult.Name = "textBox_scanBpmResult";
            this.textBox_scanBpmResult.PlaceholderText = "0.000 BPM";
            this.textBox_scanBpmResult.ReadOnly = true;
            this.textBox_scanBpmResult.Size = new Size(100, 23);
            this.textBox_scanBpmResult.TabIndex = 4;
            this.textBox_scanBpmResult.TabStop = false;
            // 
            // textBox_scanTimingResult
            // 
            this.textBox_scanTimingResult.Location = new Point(592, 257);
            this.textBox_scanTimingResult.Name = "textBox_scanTimingResult";
            this.textBox_scanTimingResult.PlaceholderText = "1 / 1 Timing";
            this.textBox_scanTimingResult.ReadOnly = true;
            this.textBox_scanTimingResult.Size = new Size(100, 23);
            this.textBox_scanTimingResult.TabIndex = 6;
            this.textBox_scanTimingResult.TabStop = false;
            // 
            // button_scanTiming
            // 
            this.button_scanTiming.Location = new Point(511, 257);
            this.button_scanTiming.Name = "button_scanTiming";
            this.button_scanTiming.Size = new Size(75, 23);
            this.button_scanTiming.TabIndex = 5;
            this.button_scanTiming.TabStop = false;
            this.button_scanTiming.Text = "Scan Time";
            this.button_scanTiming.UseVisualStyleBackColor = true;
            this.button_scanTiming.Click += this.button_scanTiming_Click;
            // 
            // textBox_scanKeyResult
            // 
            this.textBox_scanKeyResult.Location = new Point(592, 228);
            this.textBox_scanKeyResult.Name = "textBox_scanKeyResult";
            this.textBox_scanKeyResult.PlaceholderText = "No key scanned";
            this.textBox_scanKeyResult.ReadOnly = true;
            this.textBox_scanKeyResult.Size = new Size(100, 23);
            this.textBox_scanKeyResult.TabIndex = 8;
            this.textBox_scanKeyResult.TabStop = false;
            // 
            // button_scanKey
            // 
            this.button_scanKey.Location = new Point(511, 228);
            this.button_scanKey.Name = "button_scanKey";
            this.button_scanKey.Size = new Size(75, 23);
            this.button_scanKey.TabIndex = 7;
            this.button_scanKey.TabStop = false;
            this.button_scanKey.Text = "Scan Key";
            this.button_scanKey.UseVisualStyleBackColor = true;
            this.button_scanKey.Click += this.button_scanKey_Click;
            // 
            // WindowMain
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(704, 321);
            this.Controls.Add(this.textBox_scanKeyResult);
            this.Controls.Add(this.button_scanKey);
            this.Controls.Add(this.textBox_scanTimingResult);
            this.Controls.Add(this.button_scanTiming);
            this.Controls.Add(this.textBox_scanBpmResult);
            this.Controls.Add(this.button_scanBpm);
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
        private Button button_scanBpm;
        private TextBox textBox_scanBpmResult;
        private TextBox textBox_scanTimingResult;
        private Button button_scanTiming;
        private TextBox textBox_scanKeyResult;
        private Button button_scanKey;
    }
}
