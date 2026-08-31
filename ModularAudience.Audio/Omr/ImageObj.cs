using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ModularAudience.Audio.Omr
{
    public class ImageObj : IDisposable
    {
        public Image<Rgba32>[] Frames { get; private set; } = [];
        public Image<Rgba32> this[int index] => this.Frames[index];
        public Image<Rgba32>? Img => this.Frames.FirstOrDefault();

        public int[] Widths => this.Frames.Select(frame => frame.Width).ToArray();
        public int[] Heights => this.Frames.Select(frame => frame.Height).ToArray();
        public int IndexWidth => this.Widths.Length > 0 ? this.Widths.Max() : 0;
        public int IndexHeight => this.Heights.Length > 0 ? this.Heights.Max() : 0;

        public int FrameCount => this.Frames.Length;


        private ImageObj(Image<Rgba32>[] frames)
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

            string[] supportedExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".pdf"];
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
                    var frames = new List<Image<Rgba32>>(pageCount);

                    for (int i = 0; i < pageCount; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        using var pngStream = new MemoryStream();
                        PDFtoImage.Conversion.SavePng(pngStream, pdfBytes, new Index(i), options: renderOptions);
                        pngStream.Position = 0;
                        frames.Add(await Image.LoadAsync<Rgba32>(pngStream, ct));
                    }

                    return new ImageObj(frames.ToArray());
                }
                else
                {
                    // Load image file
                    var image = await Image.LoadAsync<Rgba32>(filePath, ct);
                    return new ImageObj([image]);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading image: {ex.Message}");
                return null;
            }

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
