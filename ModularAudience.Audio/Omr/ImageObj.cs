using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ModularAudience.Audio.Omr
{
    public class ImageObj : IDisposable
    {
        public Bitmap[] Frames { get; private set; } = [];
        public Bitmap this[int index] => this.Frames[index];
        public Bitmap? Img => this.Frames.FirstOrDefault();

        public int[] Widths => this.Frames.Select(frame => frame.Width).ToArray();
        public int[] Heights => this.Frames.Select(frame => frame.Height).ToArray();
        public int IndexWidth => this.Widths.Length > 0 ? this.Widths.Max() : 0;
        public int IndexHeight => this.Heights.Length > 0 ? this.Heights.Max() : 0;

        public int FrameCount => this.Frames.Length;


        private ImageObj(Bitmap[] frames)
        {
            this.Frames = frames ?? throw new ArgumentNullException(nameof(frames));
        }

        public static async Task<ImageObj?> LoadAsync(string filePath, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path cannot be null or whitespace.", nameof(filePath));
            }
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"File not found: {filePath}", filePath);
            }

            string[] supportedExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff"];
            string ext = Path.GetExtension(filePath);
            if (!supportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Unsupported file extension. Supported extensions are: " + string.Join(", ", supportedExtensions), nameof(filePath));
            }

            try
            {
                ct.ThrowIfCancellationRequested();
                Bitmap image = await Task.Run(() => LoadBitmap(filePath), ct);
                return new ImageObj([image]);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading image: {ex.Message}");
                return null;
            }

        }

        private static Bitmap LoadBitmap(string filePath)
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return LoadBitmap(stream);
        }

        private static Bitmap LoadBitmap(Stream stream)
        {
            using Image source = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: false);
            var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);

            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.DrawImage(
                    source,
                    new Rectangle(0, 0, source.Width, source.Height),
                    0,
                    0,
                    source.Width,
                    source.Height,
                    GraphicsUnit.Pixel);
            }

            return bitmap;
        }

        public void Dispose()
        {
            foreach (var frame in this.Frames)
            {
                frame.Dispose();
            }
        }



    }
}
