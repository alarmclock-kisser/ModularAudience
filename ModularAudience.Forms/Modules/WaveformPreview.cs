using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace ModularAudience.Forms.Modules
{
    public partial class WaveformPreview : Form
    {
        public WaveformPreview()
        {
            this.InitializeComponent();
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.StartPosition = FormStartPosition.Manual;
            this.BackColor = Color.White; // Hintergrund weiß
            this.Opacity = 0.97;
            this.pictureBox_waveform.SizeMode = PictureBoxSizeMode.StretchImage; // Immer ausfüllen
            this.Deactivate += (s, e) => this.Hide();
        }

        public void ShowWaveform(Bitmap bmp, Point location)
        {
            this.Size = new Size(160, 160);
            this.pictureBox_waveform.Size = new Size(160, 160);
            this.pictureBox_waveform.Image = bmp;
            this.Location = location;
            this.Show();
            this.BringToFront();
        }
    }
}
