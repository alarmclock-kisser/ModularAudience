namespace ModularAudience.Forms.Modules
{
    partial class TrackView
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
            this.button_loop = new Button();
            this.button_playback = new Button();
            this.button_pause = new Button();
            this.pictureBox1 = new PictureBox();
            this.vScrollBar1 = new VScrollBar();
            this.hScrollBar1 = new HScrollBar();
            this.label_volume = new Label();
            this.textBox_time = new TextBox();
            this.checkBox_settings = new CheckBox();
            ((System.ComponentModel.ISupportInitialize) this.pictureBox1).BeginInit();
            this.SuspendLayout();
            // 
            // button_loop
            // 
            this.button_loop.Font = new Font("Segoe UI Symbol", 9F, FontStyle.Bold, GraphicsUnit.Point,  0);
            this.button_loop.Location = new Point(60, 10);
            this.button_loop.Margin = new Padding(1);
            this.button_loop.Name = "button_loop";
            this.button_loop.Size = new Size(23, 23);
            this.button_loop.TabIndex = 6;
            this.button_loop.Text = "↺";
            this.button_loop.UseVisualStyleBackColor = true;
            // 
            // button_playback
            // 
            this.button_playback.Location = new Point(10, 10);
            this.button_playback.Margin = new Padding(1);
            this.button_playback.Name = "button_playback";
            this.button_playback.Size = new Size(23, 23);
            this.button_playback.TabIndex = 4;
            this.button_playback.Tag = "■";
            this.button_playback.Text = "▶";
            this.button_playback.UseVisualStyleBackColor = true;
            // 
            // button_pause
            // 
            this.button_pause.Font = new Font("Bahnschrift", 9F, FontStyle.Regular, GraphicsUnit.Point,  0);
            this.button_pause.Location = new Point(35, 10);
            this.button_pause.Margin = new Padding(1);
            this.button_pause.Name = "button_pause";
            this.button_pause.Size = new Size(23, 23);
            this.button_pause.TabIndex = 5;
            this.button_pause.Text = "||";
            this.button_pause.UseVisualStyleBackColor = true;
            // 
            // pictureBox1
            // 
            this.pictureBox1.BackColor = Color.White;
            this.pictureBox1.BorderStyle = BorderStyle.Fixed3D;
            this.pictureBox1.Location = new Point(104, 12);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new Size(948, 160);
            this.pictureBox1.TabIndex = 7;
            this.pictureBox1.TabStop = false;
            // 
            // vScrollBar1
            // 
            this.vScrollBar1.LargeChange = 1;
            this.vScrollBar1.Location = new Point(84, 12);
            this.vScrollBar1.Maximum = 9999;
            this.vScrollBar1.Name = "vScrollBar1";
            this.vScrollBar1.Size = new Size(17, 160);
            this.vScrollBar1.TabIndex = 8;
            // 
            // hScrollBar1
            // 
            this.hScrollBar1.Location = new Point(104, 175);
            this.hScrollBar1.Name = "hScrollBar1";
            this.hScrollBar1.Size = new Size(948, 17);
            this.hScrollBar1.TabIndex = 9;
            // 
            // label_volume
            // 
            this.label_volume.AutoSize = true;
            this.label_volume.Location = new Point(60, 175);
            this.label_volume.Name = "label_volume";
            this.label_volume.Size = new Size(44, 15);
            this.label_volume.TabIndex = 10;
            this.label_volume.Text = "100.0%";
            // 
            // textBox_time
            // 
            this.textBox_time.Location = new Point(12, 149);
            this.textBox_time.MaxLength = 32;
            this.textBox_time.Name = "textBox_time";
            this.textBox_time.PlaceholderText = "0:00:00.000";
            this.textBox_time.ReadOnly = true;
            this.textBox_time.Size = new Size(69, 23);
            this.textBox_time.TabIndex = 11;
            this.textBox_time.TabStop = false;
            // 
            // checkBox_settings
            // 
            this.checkBox_settings.AutoSize = true;
            this.checkBox_settings.Location = new Point(12, 124);
            this.checkBox_settings.Name = "checkBox_settings";
            this.checkBox_settings.Size = new Size(68, 19);
            this.checkBox_settings.TabIndex = 12;
            this.checkBox_settings.Text = "Settings";
            this.checkBox_settings.UseVisualStyleBackColor = true;
            // 
            // TrackView
            // 
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.BackColor = SystemColors.ControlLight;
            this.ClientSize = new Size(1064, 201);
            this.Controls.Add(this.checkBox_settings);
            this.Controls.Add(this.textBox_time);
            this.Controls.Add(this.label_volume);
            this.Controls.Add(this.hScrollBar1);
            this.Controls.Add(this.vScrollBar1);
            this.Controls.Add(this.pictureBox1);
            this.Controls.Add(this.button_loop);
            this.Controls.Add(this.button_playback);
            this.Controls.Add(this.button_pause);
            this.Name = "TrackView";
            this.Text = "#00 - No Track Loaded";
            ((System.ComponentModel.ISupportInitialize) this.pictureBox1).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private Button button_loop;
        private Button button_playback;
        private Button button_pause;
        private PictureBox pictureBox1;
        private VScrollBar vScrollBar1;
        private HScrollBar hScrollBar1;
        private Label label_volume;
        private TextBox textBox_time;
        private CheckBox checkBox_settings;
    }
}