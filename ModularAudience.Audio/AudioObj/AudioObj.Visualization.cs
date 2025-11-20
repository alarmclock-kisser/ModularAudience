using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace NAudience.Core
{
    public partial class AudioObj
    {
        [SupportedOSPlatform("windows")]
        public async Task<Bitmap> DrawWaveformAsync(int width, int height, int samplesPerPixel = 128, bool drawEachChannel = false, int caretWidth = 1, long? offset = null, Color? waveColor = null, Color? backColor = null, Color? caretColor = null, bool smoothen = false, double timingMarkersInterval = 0, float caretPosition = 0.0f, int maxWorkers = 2)
        {
            maxWorkers = Math.Clamp(maxWorkers, 1, Environment.ProcessorCount);
            waveColor ??= Color.Black;
            backColor ??= Color.White;
            caretColor ??= Color.Red;
            width = Math.Max(1, width);
            height = Math.Max(1, height);
            samplesPerPixel = samplesPerPixel <= 0 ? this.CalculateSamplesPerPixelToFit(width) : samplesPerPixel;
            caretWidth = Math.Clamp(caretWidth, 0, width);
            offset ??= this.Position;

            long totalFrames = Math.Max(0, this.Length / Math.Max(1, this.Channels));
            long viewFrames = (long) width * samplesPerPixel;
            long maxOffset = Math.Max(0, totalFrames - viewFrames);
            offset = Math.Clamp(offset.Value, 0, maxOffset);

            var bitmap = new Bitmap(width, height);
            int channelsToDraw = drawEachChannel ? this.Channels : 1;
            var minMaxPerChannel = new (int yMin, int yMax)[channelsToDraw][];
            for (int c = 0; c < channelsToDraw; c++)
            {
                minMaxPerChannel[c] = new (int, int)[width];
            }

            const int targetSamplesPerPixelBudget = 2048;
            int stride = Math.Max(1, (int) Math.Ceiling((double) samplesPerPixel / targetSamplesPerPixelBudget));
            var data = this.Data;
            long dataLength = data?.LongLength ?? 0L;
            int channels = this.Channels;

            await Task.Run(() =>
            {
                var po = new ParallelOptions { MaxDegreeOfParallelism = maxWorkers };
                Parallel.For(0, channelsToDraw, po, channelIndex =>
                {
                    try
                    {
                        int channelHeight = height / channelsToDraw;
                        int centerY = channelHeight / 2 + channelIndex * channelHeight;
                        long baseOffsetIndices = offset.Value * channels;

                        for (int x = 0; x < width; x++)
                        {
                            long sampleStart = baseOffsetIndices + (long) x * samplesPerPixel * channels + channelIndex;
                            if (sampleStart >= dataLength)
                            {
                                for (int xr = x; xr < width; xr++)
                                {
                                    minMaxPerChannel[channelIndex][xr] = (centerY, centerY);
                                }
                                break;
                            }

                            long sampleEnd = Math.Min(sampleStart + (long) samplesPerPixel * channels, dataLength);
                            long step = (long) channels * stride;
                            float min = float.MaxValue;
                            float max = float.MinValue;

                            for (long idx = sampleStart; idx < sampleEnd; idx += step)
                            {
                                float sample = data![idx];
                                if (sample < min) { min = sample; }
                                if (sample > max) { max = sample; }
                            }

                            if (min == float.MaxValue && max == float.MinValue)
                            {
                                min = 0f;
                                max = 0f;
                            }

                            int yMin = centerY - (int) (min * (channelHeight / 2f));
                            int yMax = centerY - (int) (max * (channelHeight / 2f));
                            minMaxPerChannel[channelIndex][x] = (yMin, yMax);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Waveform generation failed for channel {channelIndex}: {ex.Message}");
                    }
                });
            }).ConfigureAwait(false);

            long selStart = this.SelectionStart;
            long selEnd = this.SelectionEnd;
            using (var g = Graphics.FromImage(bitmap))
            using (var pen = new Pen(waveColor.Value))
            {
                g.Clear(backColor.Value);

                if (this.Channels > 0 && selStart >= 0 && selEnd >= 0 && selStart != selEnd)
                {
                    if (selEnd < selStart) { (selStart, selEnd) = (selEnd, selStart); }
                    int ch = Math.Max(1, this.Channels);
                    long selStartFrames = selStart / ch;
                    long selEndFrames = selEnd / ch;
                    long viewStartFrames = offset.Value;
                    long viewEndFrames = viewStartFrames + viewFrames;
                    long highlightStartFrames = Math.Max(viewStartFrames, selStartFrames);
                    long highlightEndFrames = Math.Min(viewEndFrames, selEndFrames);
                    if (highlightEndFrames > highlightStartFrames)
                    {
                        double invSPP = 1.0 / samplesPerPixel;
                        int x1 = (int) Math.Floor((highlightStartFrames - viewStartFrames) * invSPP);
                        int x2 = (int) Math.Ceiling((highlightEndFrames - viewStartFrames) * invSPP);
                        int rectX = Math.Clamp(x1, 0, width);
                        int rectW = Math.Clamp(x2 - rectX, 0, width - rectX);
                        if (rectW > 0)
                        {
                            Color overlay = backColor.Value.GetBrightness() > 0.92f
                                ? Color.FromArgb(28, 0, 0, 0)
                                : Color.FromArgb(48, 255, 255, 255);
                            using var selBrush = new SolidBrush(overlay);
                            g.FillRectangle(selBrush, rectX, 0, rectW, height);
                        }
                    }
                }

                for (int channelIndex = 0; channelIndex < channelsToDraw; channelIndex++)
                {
                    int channelHeight = height / channelsToDraw;
                    int centerY = channelHeight / 2 + channelIndex * channelHeight;
                    for (int x = 0; x < width; x++)
                    {
                        var (yMin, yMax) = minMaxPerChannel[channelIndex][x];
                        if (yMin == 0 && yMax == 0)
                        {
                            yMin = yMax = centerY;
                        }
                        if (samplesPerPixel == 1 && yMin == yMax && yMin != centerY)
                        {
                            yMax += 1;
                            yMin -= 1;
                        }
                        g.DrawLine(pen, x, yMin, x, yMax);
                    }
                }

                if (caretWidth > 0)
                {
                    using var caretPen = new Pen(caretColor.Value, caretWidth);
                    int caretX = caretPosition is > 0.0f and < 1.0f
                        ? (int) Math.Round(caretPosition * (width - 1))
                        : (int) ((this.Position - offset.Value) / samplesPerPixel);
                    g.DrawLine(caretPen, caretX, 0, caretX, height);
                }
            }

            if (smoothen)
            {
                await Task.Run(() =>
                {
                    var smoothBitmap = new Bitmap(width, height);
                    using (var gSmooth = Graphics.FromImage(smoothBitmap))
                    {
                        gSmooth.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        gSmooth.DrawImage(bitmap, new Rectangle(0, 0, width, height));
                    }
                    bitmap.Dispose();
                    bitmap = smoothBitmap;
                }).ConfigureAwait(false);
            }

            if (timingMarkersInterval > 0)
            {
                Color inverseGraphColor = Color.FromArgb(255 - waveColor.Value.R, 255 - waveColor.Value.G, 255 - waveColor.Value.B);
                bitmap = await this.DrawTimingMarkersAsync(bitmap, samplesPerPixel, timingMarkersInterval, inverseGraphColor, false, offset.Value).ConfigureAwait(false);
            }

            return bitmap;
        }

        [SupportedOSPlatform("windows")]
        public async Task<Bitmap> DrawTimingMarkersAsync(Bitmap waveForm, int samplesPerPixel, double interval = 1, Color? color = null, bool drawTimes = false, long offsetFrames = 0)
        {
            color ??= Color.Gray;

            return await Task.Run(() =>
            {
                int width = waveForm.Width;
                int height = waveForm.Height;
                using (var g = Graphics.FromImage(waveForm))
                using (var pen = new Pen(color.Value))
                using (var font = new Font("Arial", 10))
                using (var brush = new SolidBrush(color.Value))
                {
                    double invSPP = 1.0 / Math.Max(1, samplesPerPixel);
                    long intervalFrames = (long) Math.Round(interval * Math.Max(1, this.SampleRate));
                    if (intervalFrames <= 0)
                    {
                        return waveForm;
                    }

                    double intervalInPixels = intervalFrames * invSPP;
                    long remainder = offsetFrames % intervalFrames;
                    double firstMarkerX = remainder == 0 ? 0.0 : (intervalFrames - remainder) * invSPP;
                    for (double x = firstMarkerX; x < width; x += intervalInPixels)
                    {
                        if (x >= 0 && x < width)
                        {
                            g.DrawLine(pen, (float) x, 0, (float) x, height);
                            if (drawTimes)
                            {
                                double seconds = (offsetFrames + (x * samplesPerPixel)) / (double) this.SampleRate;
                                TimeSpan time = TimeSpan.FromSeconds(seconds);
                                string timeLabel = time.ToString(@"mm\:ss");
                                g.DrawString(timeLabel, font, brush, (float) x + 2, 2);
                            }
                        }
                    }
                }

                return waveForm;
            }).ConfigureAwait(false);
        }

        private int CalculateSamplesPerPixelToFit(int width)
        {
            if (this.Data == null || this.Data.Length == 0 || width <= 0)
            {
                return 1;
            }

            int totalSamples = this.Data.Length / this.Channels;
            int samplesPerPixel = (int) Math.Ceiling((double) totalSamples / width);
            return Math.Max(1, samplesPerPixel);
        }
    }
}
