using System.Drawing;
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
        }

        public void ShowWaveform(Bitmap bmp, Point location)
        {
            this.ReplaceImage(bmp);
            this.Size = new Size(bmp.Width, bmp.Height);
            this.pictureBox_waveform.Size = new Size(bmp.Width, bmp.Height);
            this.Location = location;
            this.Show();
            this.BringToFront();
        }

        public void ShowConcatenatedWaveforms(IList<Bitmap> bitmaps, Point location)
        {
            if (bitmaps.Count == 0)
            {
                return;
            }

            int width = bitmaps.Max(b => b.Width);
            int totalHeight = bitmaps.Sum(b => b.Height);

            Bitmap combined = new(width, totalHeight);
            using (Graphics g = Graphics.FromImage(combined))
            {
                g.Clear(Color.White);
                int y = 0;
                foreach (Bitmap bmp in bitmaps)
                {
                    g.DrawImageUnscaled(bmp, 0, y);
                    y += bmp.Height;
                }
            }

            foreach (Bitmap bmp in bitmaps)
            {
                bmp.Dispose();
            }

            // Scale down if combined bitmap exceeds screen bounds
            Rectangle screenBounds = Screen.FromPoint(location).Bounds;
            int maxH = screenBounds.Height - 40;
            float scale = 1.0f;
            if (combined.Height > maxH)
            {
                scale = (float)maxH / combined.Height;
            }

            Bitmap displayBmp;
            if (scale < 1.0f)
            {
                int finalW = Math.Max(1, (int)(combined.Width * scale));
                int finalH = Math.Max(1, (int)(combined.Height * scale));
                displayBmp = new Bitmap(finalW, finalH);
                using Graphics g = Graphics.FromImage(displayBmp);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(combined, 0, 0, finalW, finalH);
                combined.Dispose();
            }
            else
            {
                displayBmp = combined;
            }

            this.ReplaceImage(displayBmp);
            this.Size = new Size(displayBmp.Width, displayBmp.Height);
            this.pictureBox_waveform.Size = new Size(displayBmp.Width, displayBmp.Height);

            // Clamp position to screen
            Rectangle bounds = Screen.FromPoint(location).Bounds;
            int posX = Math.Max(bounds.Left, Math.Min(location.X, bounds.Right - displayBmp.Width));
            int posY = Math.Max(bounds.Top, Math.Min(location.Y, bounds.Bottom - displayBmp.Height));

            this.Location = new Point(posX, posY);
            this.Show();
            this.BringToFront();
        }

        private void ReplaceImage(Bitmap image)
        {
            Image? previous = this.pictureBox_waveform.Image;
            this.pictureBox_waveform.Image = image;
            if (previous != null && !ReferenceEquals(previous, image))
            {
                previous.Dispose();
            }
        }

        public void ClearImage()
        {
            Image? previous = this.pictureBox_waveform.Image;
            this.pictureBox_waveform.Image = null;
            previous?.Dispose();
        }
    }
}
