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

            string[] supportedExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".pdf"];
            string ext = Path.GetExtension(filePath);
            if (!supportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Unsupported file extension. Supported extensions are: " + string.Join(", ", supportedExtensions), nameof(filePath));
            }

            try
            {
                if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] pdfBytes = await File.ReadAllBytesAsync(filePath, ct);
                    int pageCount = PDFtoImage.Conversion.GetPageCount(pdfBytes, password: null);
                    var renderOptions = new PDFtoImage.RenderOptions(
                        Dpi: 300,
                        Width: null,
                        Height: null,
                        WithAnnotations: true,
                        WithFormFill: false,
                        WithAspectRatio: false,
                        Rotation: default,
                        AntiAliasing: default,
                        BackgroundColor: null,
                        Bounds: null,
                        UseTiling: false,
                        DpiRelativeToBounds: false,
                        Grayscale: false);
                    var frames = new List<Bitmap>(pageCount);

                    try
                    {
                        for (int i = 0; i < pageCount; i++)
                        {
                            ct.ThrowIfCancellationRequested();
                            using var pngStream = new MemoryStream();
                            PDFtoImage.Conversion.SavePng(pngStream, pdfBytes, new Index(i), options: renderOptions);
                            pngStream.Position = 0;
                            frames.Add(LoadBitmap(pngStream));
                        }
                    }
                    catch
                    {
                        foreach (Bitmap frame in frames)
                        {
                            frame.Dispose();
                        }

                        throw;
                    }

                    return new ImageObj(frames.ToArray());
                }
                else
                {
                    // Load image file
                    ct.ThrowIfCancellationRequested();
                    Bitmap image = await Task.Run(() => LoadBitmap(filePath), ct);
                    return new ImageObj([image]);
                }
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
