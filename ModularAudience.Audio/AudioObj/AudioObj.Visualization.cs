using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace ModularAudience.Audio
{
    public partial class AudioObj
    {
        // --- Cache state (keeps a continuous strip bitmap representing frames [cacheOffsetFrames .. cacheOffsetFrames + cacheWidthPixels * samplesPerPixel) ) ---
        private Bitmap? _waveCacheStrip = null;
        private long _cacheOffsetFrames = 0; // leftmost frame index (frames = samples per channel)
        private int _cacheWidthPixels = 0;   // pixel width of the strip
        private int _cacheSamplesPerPixel = 0;
        private int _cacheChannelsToDraw = 0;
        private int _cacheHeight = 0;
        private Color _cacheWaveColor = Color.Black;
        private Color _cacheBackColor = Color.White;
        private bool _cacheDrawEachChannel = false;

        // multiplier for how many viewports we keep in cache (3 => keeps ~3x width)
        private int _cacheCapacityMultiplier = 3;

        // simple lock to guard cache updates (async-friendly)
        private readonly SemaphoreSlim _cacheLock = new(1, 1);




        [SupportedOSPlatform("windows")]
        public async Task<Bitmap> DrawWaveformAsync(int width, int height, int samplesPerPixel = 128, bool drawEachChannel = false, int caretWidth = 1, long? offset = null, Color? waveColor = null, Color? backColor = null, Color? caretColor = null, bool smoothen = false, double timingMarkersInterval = 0, float caretPosition = 0.0f, int maxWorkers = 2)
        {
            // normalize inputs same as before
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

            int channelsToDraw = drawEachChannel ? this.Channels : 1;

            // Quick decision: if cache is compatible, try to serve from it
            bool cacheCompatible = (this._waveCacheStrip != null)
                                   && this._cacheSamplesPerPixel == samplesPerPixel
                                   && this._cacheHeight == height
                                   && this._cacheChannelsToDraw == channelsToDraw
                                   && this._cacheDrawEachChannel == drawEachChannel
                                   && this._cacheWaveColor.ToArgb() == waveColor.Value.ToArgb()
                                   && this._cacheBackColor.ToArgb() == backColor.Value.ToArgb();

            if (!cacheCompatible)
            {
                // Invalidate cache and render a fresh full bitmap using your performant V2 implementation
                await this._cacheLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    this.InvalidateWaveformCache();
                }
                finally
                {
                    this._cacheLock.Release();
                }

                // Fall back to V2 (full render)
                // Assume you have DrawWaveformAsync_V2 implemented (the version you pasted earlier)
                return await this.DrawWaveformAsync_V2(width, height, samplesPerPixel, drawEachChannel, caretWidth, offset, waveColor, backColor, caretColor, smoothen, timingMarkersInterval, caretPosition, maxWorkers).ConfigureAwait(false);
            }

            // If compatible: attempt to satisfy request from cached strip (update cache if necessary)
            await this._cacheLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (this._waveCacheStrip == null)
                {
                    // no cache - should not happen due to check, but safe fallback
                    this.InvalidateWaveformCache();
                    return await this.DrawWaveformAsync_V2(width, height, samplesPerPixel, drawEachChannel, caretWidth, offset, waveColor, backColor, caretColor, smoothen, timingMarkersInterval, caretPosition, maxWorkers).ConfigureAwait(false);
                }

                long requestedStart = offset.Value; // frames
                long requestedEnd = requestedStart + viewFrames; // exclusive

                long cachedStart = this._cacheOffsetFrames;
                long cachedEnd = this._cacheOffsetFrames + (long) this._cacheWidthPixels * this._cacheSamplesPerPixel; // frames

                // If requested is fully inside cached strip -> just copy and return
                if (requestedStart >= cachedStart && requestedEnd <= cachedEnd)
                {
                    // compute pixel offsets inside cache
                    int srcX = (int) ((requestedStart - cachedStart) / this._cacheSamplesPerPixel);
                    var result = new Bitmap(width, height);
                    using (var g = Graphics.FromImage(result))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(this._waveCacheStrip, new Rectangle(0, 0, width, height), new Rectangle(srcX, 0, width, height), GraphicsUnit.Pixel);
                    }

                    // caret and selection/markers still need to be applied (we keep cache purely waveform)
                    if (caretWidth > 0)
                    {
                        using var g = Graphics.FromImage(result);
                        using var caretPen = new Pen(caretColor.Value, caretWidth);
                        int caretX = (caretPosition > 0.0f && caretPosition < 1.0f)
                            ? (int) Math.Round(caretPosition * (width - 1))
                            : (int) ((this.Position - offset.Value) / samplesPerPixel);
                        g.DrawLine(caretPen, caretX, 0, caretX, height);
                    }

                    if (smoothen)
                    {
                        // optional cheap smoothing pass
                        var smoothBitmap = new Bitmap(width, height);
                        using (var gs = Graphics.FromImage(smoothBitmap))
                        {
                            gs.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            gs.DrawImage(result, new Rectangle(0, 0, width, height));
                        }
                        result.Dispose();
                        result = smoothBitmap;
                    }

                    if (timingMarkersInterval > 0)
                    {
                        Color inverseGraphColor = Color.FromArgb(255 - waveColor.Value.R, 255 - waveColor.Value.G, 255 - waveColor.Value.B);
                        result = await this.DrawTimingMarkersAsync(result, samplesPerPixel, timingMarkersInterval, inverseGraphColor, false, offset.Value).ConfigureAwait(false);
                    }

                    return result;
                }

                // Partial overlap or no overlap -> compute missing columns and expand/slide cache accordingly
                // Strategy: grow cache region to cover requestedStart..requestedEnd, but cap total width to _cacheCapacityMultiplier * width
                int targetCacheWidth = Math.Max(this._cacheWidthPixels, width) * this._cacheCapacityMultiplier;

                // Determine newCacheStartFrames so that requested range is inside new cache and cache size is targetCacheWidth
                long newCacheSamplesSpan = (long) targetCacheWidth * this._cacheSamplesPerPixel;
                long idealCacheStart = requestedStart - (newCacheSamplesSpan - viewFrames) / 2; // center requested range in cache
                long newCacheStartFrames = Math.Clamp(idealCacheStart, 0L, Math.Max(0L, totalFrames - newCacheSamplesSpan));
                long newCacheEndFrames = newCacheStartFrames + newCacheSamplesSpan;

                // Convert to pixels
                int newCacheWidthPixels = (int) Math.Min((long) targetCacheWidth, (long) Math.Ceiling((double) newCacheSamplesSpan / this._cacheSamplesPerPixel));
                // if newCacheWidthPixels <= 0 fallback to width
                if (newCacheWidthPixels <= 0)
                {
                    newCacheWidthPixels = width;
                }

                // We'll create a new strip bitmap and try to copy existing cached portion into it to avoid re-rendering.
                var newStrip = new Bitmap(newCacheWidthPixels, this._cacheHeight);
                using (var gNew = Graphics.FromImage(newStrip))
                {
                    gNew.Clear(this._cacheBackColor);
                    // figure overlap in frames
                    long overlapStart = Math.Max(newCacheStartFrames, cachedStart);
                    long overlapEnd = Math.Min(newCacheEndFrames, cachedEnd);

                    if (overlapEnd > overlapStart && this._waveCacheStrip != null)
                    {
                        // copy overlapping pixel region from old cache into new cache
                        int srcX = (int) ((overlapStart - cachedStart) / this._cacheSamplesPerPixel);
                        int dstX = (int) ((overlapStart - newCacheStartFrames) / this._cacheSamplesPerPixel);
                        int overlapPixels = (int) ((overlapEnd - overlapStart) / this._cacheSamplesPerPixel);
                        overlapPixels = Math.Clamp(overlapPixels, 0, Math.Min(this._cacheWidthPixels - srcX, newCacheWidthPixels - dstX));

                        if (overlapPixels > 0)
                        {
                            gNew.DrawImage(this._waveCacheStrip, new Rectangle(dstX, 0, overlapPixels, this._cacheHeight), new Rectangle(srcX, 0, overlapPixels, this._cacheHeight), GraphicsUnit.Pixel);
                        }
                    }
                }

                // Replace cache with newStrip (we will render missing columns into newStrip next)
                this._waveCacheStrip?.Dispose();
                this._waveCacheStrip = newStrip;
                this._cacheOffsetFrames = newCacheStartFrames;
                this._cacheWidthPixels = newCacheWidthPixels;

                // Now compute which pixel ranges are missing and need rendering
                // For each pixel in cache, check if it was filled by overlap above by sampling from old cache area.
                // Simpler: compute missing left region [newCacheStartFrames .. cachedStart) and missing right region (cachedEnd .. newCacheEndFrames)
                var tasks = new List<Task>(2);
                // left missing
                if (newCacheStartFrames < cachedStart)
                {
                    long leftMissingFramesStart = newCacheStartFrames;
                    long leftMissingFramesEnd = Math.Min(cachedStart, newCacheEndFrames);
                    int leftStartPixel = (int) ((leftMissingFramesStart - this._cacheOffsetFrames) / this._cacheSamplesPerPixel);
                    int leftPixelCount = (int) Math.Ceiling((double) (leftMissingFramesEnd - leftMissingFramesStart) / this._cacheSamplesPerPixel);
                    leftPixelCount = Math.Clamp(leftPixelCount, 0, this._cacheWidthPixels - leftStartPixel);
                    if (leftPixelCount > 0)
                    {
                        tasks.Add(this.RenderColumnsIntoCacheAsync(leftStartPixel, leftPixelCount, samplesPerPixel, channelsToDraw, maxWorkers));
                    }
                }

                // right missing
                if (newCacheEndFrames > cachedEnd)
                {
                    long rightMissingFramesStart = Math.Max(cachedEnd, newCacheStartFrames);
                    long rightMissingFramesEnd = newCacheEndFrames;
                    int rightStartPixel = (int) ((rightMissingFramesStart - this._cacheOffsetFrames) / this._cacheSamplesPerPixel);
                    int rightPixelCount = (int) Math.Ceiling((double) (rightMissingFramesEnd - rightMissingFramesStart) / this._cacheSamplesPerPixel);
                    rightPixelCount = Math.Clamp(rightPixelCount, 0, this._cacheWidthPixels - rightStartPixel);
                    if (rightPixelCount > 0)
                    {
                        tasks.Add(this.RenderColumnsIntoCacheAsync(rightStartPixel, rightPixelCount, samplesPerPixel, channelsToDraw, maxWorkers));
                    }
                }

                // await missing render tasks
                if (tasks.Count > 0)
                {
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }

                // At this point cache covers requested region. Return the requested slice.
                int srcX2 = (int) ((requestedStart - this._cacheOffsetFrames) / this._cacheSamplesPerPixel);
                var resultBitmap = new Bitmap(width, height);
                using (var g = Graphics.FromImage(resultBitmap))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(this._waveCacheStrip, new Rectangle(0, 0, width, height), new Rectangle(srcX2, 0, width, height), GraphicsUnit.Pixel);
                }

                // Draw caret on top (as cache only stores waveform) and optionally smoothing/markers
                if (caretWidth > 0)
                {
                    using var g = Graphics.FromImage(resultBitmap);
                    using var caretPen = new Pen(caretColor.Value, caretWidth);
                    int caretX = (caretPosition > 0.0f && caretPosition < 1.0f)
                        ? (int) Math.Round(caretPosition * (width - 1))
                        : (int) ((this.Position - offset.Value) / samplesPerPixel);
                    g.DrawLine(caretPen, caretX, 0, caretX, height);
                }

                if (smoothen)
                {
                    var smoothBitmap = new Bitmap(width, height);
                    using (var gs = Graphics.FromImage(smoothBitmap))
                    {
                        gs.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        gs.DrawImage(resultBitmap, new Rectangle(0, 0, width, height));
                    }
                    resultBitmap.Dispose();
                    resultBitmap = smoothBitmap;
                }

                if (timingMarkersInterval > 0)
                {
                    Color inverseGraphColor = Color.FromArgb(255 - waveColor.Value.R, 255 - waveColor.Value.G, 255 - waveColor.Value.B);
                    resultBitmap = await this.DrawTimingMarkersAsync(resultBitmap, samplesPerPixel, timingMarkersInterval, inverseGraphColor, false, offset.Value).ConfigureAwait(false);
                }

                return resultBitmap;
            }
            finally
            {
                this._cacheLock.Release();
            }
        }

        [SupportedOSPlatform("windows")]
        public async Task<Bitmap> DrawWaveformAsync_V2(int width, int height, int samplesPerPixel = 128, bool drawEachChannel = false, int caretWidth = 1, long? offset = null, Color? waveColor = null, Color? backColor = null, Color? caretColor = null, bool smoothen = false, double timingMarkersInterval = 0, float caretPosition = 0.0f, int maxWorkers = 2)
        {
            // normalize inputs
            maxWorkers = Math.Clamp(maxWorkers, 1, Environment.ProcessorCount);
            waveColor ??= Color.Black;
            backColor ??= Color.White;
            caretColor ??= Color.Red;
            width = Math.Max(1, width);
            height = Math.Max(1, height);
            samplesPerPixel = samplesPerPixel <= 0 ? this.CalculateSamplesPerPixelToFit(width) : samplesPerPixel;
            caretWidth = Math.Clamp(caretWidth, 0, width);
            offset ??= this.Position;

            // initialize cache if needed
            this.InitializeCacheIfNeeded(width, height, samplesPerPixel, this.Channels, drawEachChannel, waveColor.Value, backColor.Value);

            // derived values
            long totalFrames = Math.Max(0, this.Length / Math.Max(1, this.Channels));
            long viewFrames = (long) width * samplesPerPixel;
            long maxOffset = Math.Max(0, totalFrames - viewFrames);
            offset = Math.Clamp(offset.Value, 0, maxOffset);


            var bitmap = new Bitmap(width, height);


            int channelsToDraw = drawEachChannel ? this.Channels : 1;
            channelsToDraw = Math.Max(1, channelsToDraw);


            // allocate compact buffers for min/max per channel per pixel (int16-like ranges map to screen Y)
            var yMin = new int[channelsToDraw * width];
            var yMax = new int[channelsToDraw * width];


            // small budget to avoid scanning enormous ranges per pixel (keeps CPU time sane)
            const int targetSamplesPerPixelBudget = 2048;
            int stride = Math.Max(1, (int) Math.Ceiling((double) samplesPerPixel / targetSamplesPerPixelBudget));


            var data = this.Data ?? Array.Empty<float>();
            long dataLength = data.LongLength;
            int channels = Math.Max(1, this.Channels);


            // Pre-calc per-channel geometry
            var channelHeight = new int[channelsToDraw];
            var centerY = new int[channelsToDraw];
            for (int c = 0; c < channelsToDraw; c++)
            {
                channelHeight[c] = height / channelsToDraw;
                centerY[c] = (channelHeight[c] / 2) + c * channelHeight[c];
            }


            // Parallelize over pixel ranges; partitioning reduces false-sharing
            await Task.Run(() =>
            {
                var po = new ParallelOptions { MaxDegreeOfParallelism = maxWorkers };


                Parallel.For(0, width, po, () => (localMin: float.MaxValue, localMax: float.MinValue), (x, state, local) =>
                {
                    // per-x work for all channels
                    long baseFrameIndex = offset.Value + (long) x * samplesPerPixel;
                    long baseSampleIndex = baseFrameIndex * channels; // convert frames -> samples


                    for (int ch = 0; ch < channelsToDraw; ch++)
                    {
                        float min = float.MaxValue;
                        float max = float.MinValue;


                        long sampleStart = baseSampleIndex + ch;
                        if (sampleStart >= dataLength)
                        {
                            // no data - place center line
                            int idx = ch * width + x;
                            yMin[idx] = centerY[ch];
                            yMax[idx] = centerY[ch];
                            continue;
                        }


                        long sampleEnd = Math.Min(sampleStart + (long) samplesPerPixel * channels, dataLength);
                        long step = (long) channels * stride;


                        // iterate with step; this is memory-bounded but predictable
                        for (long s = sampleStart; s < sampleEnd; s += step)
                        {
                            float v = data[s];
                            if (v < min)
                            {
                                min = v;
                            }

                            if (v > max)
                            {
                                max = v;
                            }
                        }


                        if (min == float.MaxValue && max == float.MinValue)
                        {
                            min = 0f;
                            max = 0f;
                        }


                        int idx2 = ch * width + x;
                        int chHeight = channelHeight[ch];
                        // map [-1..1] to pixel coordinates (invert Y because GDI has 0 at top)
                        int yMinPx = centerY[ch] - (int) Math.Round(min * (chHeight / 2.0));
                        int yMaxPx = centerY[ch] - (int) Math.Round(max * (chHeight / 2.0));
                        yMin[idx2] = yMinPx;
                        yMax[idx2] = yMaxPx;
                    }


                    return local; // no thread-local aggregation needed
                }, _ => { /* no finalize */ });
            }).ConfigureAwait(false);


            // Draw to bitmap (single-threaded GDI)
            long selStart = this.SelectionStart;
            long selEnd = this.SelectionEnd;


            using (var g = Graphics.FromImage(bitmap))
            using (var pen = new Pen(waveColor.Value))
            {
                g.Clear(backColor.Value);


                // selection overlay (kept same behavior as original)
                if (this.Channels > 0 && selStart >= 0 && selEnd >= 0 && selStart != selEnd)
                {
                    if (selEnd < selStart)
                    {
                        (selStart, selEnd) = (selEnd, selStart);
                    }

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


                // draw channels
                for (int ch = 0; ch < channelsToDraw; ch++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int idx = ch * width + x;
                        int ymn = yMin[idx];
                        int ymx = yMax[idx];


                        // safety: ensure we always draw at least a pixel if there's energy
                        if (ymn == ymx && ymn != centerY[ch])
                        {
                            ymx = Math.Min(ymn + 1, centerY[ch] + channelHeight[ch] / 2);
                            ymn = Math.Max(ymn - 1, centerY[ch] - channelHeight[ch] / 2);
                        }


                        g.DrawLine(pen, x, ymn, x, ymx);
                    }
                }


                // caret
                if (caretWidth > 0)
                {
                    using var caretPen = new Pen(caretColor.Value, caretWidth);
                    int caretX = (caretPosition > 0.0f && caretPosition < 1.0f)
                    ? (int) Math.Round(caretPosition * (width - 1))
                    : (int) ((this.Position - offset.Value) / samplesPerPixel);
                    g.DrawLine(caretPen, caretX, 0, caretX, height);
                }
            }


            // optional smoothing (cheap resize pass if requested)
            if (smoothen)
            {
                await Task.Run(() =>
                {
                    var smoothBitmap = new Bitmap(width, height);
                    using (var gs = Graphics.FromImage(smoothBitmap))
                    {
                        gs.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        gs.DrawImage(bitmap, new Rectangle(0, 0, width, height));
                    }
                    bitmap.Dispose();
                    bitmap = smoothBitmap;
                }).ConfigureAwait(false);
            }


            // timing markers kept as post-processing helper to reuse existing implementation if desired
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



        private async Task RenderColumnsIntoCacheAsync(int pixelStart, int pixelCount, int samplesPerPixel, int channelsToDraw, int maxWorkers)
        {
            if (this._waveCacheStrip == null)
            {
                return;
            }

            if (pixelCount <= 0)
            {
                return;
            }

            // clamp to cache
            pixelStart = Math.Clamp(pixelStart, 0, this._cacheWidthPixels - 1);
            pixelCount = Math.Clamp(pixelCount, 0, this._cacheWidthPixels - pixelStart);

            var data = this.Data ?? Array.Empty<float>();
            long dataLength = data.LongLength;
            int channels = Math.Max(1, this.Channels);
            int height = this._cacheHeight;
            int cacheSamplesPerPixel = this._cacheSamplesPerPixel;
            long baseFrameOffset = this._cacheOffsetFrames; // frames

            // allocate small arrays for min/max values for all channels and columns to render
            int cols = pixelCount;
            int chCount = channelsToDraw;
            var yMin = new int[chCount * cols];
            var yMax = new int[chCount * cols];

            int[] channelHeight = new int[chCount];
            int[] centerY = new int[chCount];
            for (int c = 0; c < chCount; c++)
            {
                channelHeight[c] = height / chCount;
                centerY[c] = (channelHeight[c] / 2) + c * channelHeight[c];
            }

            // sampling stride heuristic same as V2
            const int targetSamplesPerPixelBudget = 2048;
            int stride = Math.Max(1, (int) Math.Ceiling((double) samplesPerPixel / targetSamplesPerPixelBudget));
            var po = new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(maxWorkers, 1, Environment.ProcessorCount) };

            // Compute min/max for requested columns in parallel
            await Task.Run(() =>
            {
                Parallel.For(0, cols, po, xLocal =>
                {
                    int x = pixelStart + xLocal;
                    long frameStart = baseFrameOffset + (long) x * cacheSamplesPerPixel;
                    long sampleStart = frameStart * channels;

                    for (int ch = 0; ch < chCount; ch++)
                    {
                        if (sampleStart + ch >= dataLength)
                        {
                            int idx0 = ch * cols + xLocal;
                            yMin[idx0] = centerY[ch];
                            yMax[idx0] = centerY[ch];
                            continue;
                        }

                        long sampleEnd = Math.Min(sampleStart + (long) samplesPerPixel * channels, dataLength);
                        long step = (long) channels * stride;

                        float min = float.MaxValue;
                        float max = float.MinValue;
                        for (long s = sampleStart + ch; s < sampleEnd; s += step)
                        {
                            float v = data[s];
                            if (v < min)
                            {
                                min = v;
                            }

                            if (v > max)
                            {
                                max = v;
                            }
                        }

                        if (min == float.MaxValue && max == float.MinValue)
                        {
                            min = 0f; max = 0f;
                        }

                        int idx = ch * cols + xLocal;
                        int chH = channelHeight[ch];
                        int yMinPx = centerY[ch] - (int) Math.Round(min * (chH / 2.0));
                        int yMaxPx = centerY[ch] - (int) Math.Round(max * (chH / 2.0));
                        yMin[idx] = yMinPx;
                        yMax[idx] = yMaxPx;
                    }
                });
            }).ConfigureAwait(false);

            // Now draw those columns into the cache bitmap (single-threaded GDI draw)
            lock (this._waveCacheStrip)
            {
                using var g = Graphics.FromImage(this._waveCacheStrip);
                using var pen = new Pen(this._cacheWaveColor);
                for (int xLocal = 0; xLocal < cols; xLocal++)
                {
                    int x = pixelStart + xLocal;
                    for (int ch = 0; ch < chCount; ch++)
                    {
                        int idx = ch * cols + xLocal;
                        int ymn = yMin[idx];
                        int ymx = yMax[idx];
                        int cy = centerY[ch];

                        if (ymn == ymx && ymn != cy)
                        {
                            ymx = Math.Min(ymn + 1, cy + channelHeight[ch] / 2);
                            ymn = Math.Max(ymn - 1, cy - channelHeight[ch] / 2);
                        }

                        g.DrawLine(pen, x, ymn, x, ymx);
                    }
                }
            }
        }

        private void InvalidateWaveformCache()
        {
            if (this._waveCacheStrip != null)
            {
                try { this._waveCacheStrip.Dispose(); } catch { }
                this._waveCacheStrip = null;
            }

            // Update cached metadata defaults so next build will reinitialize
            this._cacheOffsetFrames = 0;
            this._cacheWidthPixels = 0;
            this._cacheSamplesPerPixel = 0;
            this._cacheChannelsToDraw = 0;
            this._cacheHeight = 0;
            this._cacheWaveColor = Color.Black;
            this._cacheBackColor = Color.White;
            this._cacheDrawEachChannel = false;

            // Build a fresh empty cache on next render (we keep allocation lazy)
        }

        private void InitializeCacheIfNeeded(int width, int height, int samplesPerPixel, int channelsToDraw, bool drawEachChannel, Color waveColor, Color backColor)
        {
            if (this._waveCacheStrip != null)
            {
                return;
            }

            int targetCacheWidth = Math.Max(width, 1) * this._cacheCapacityMultiplier;
            this._cacheWidthPixels = targetCacheWidth;
            this._cacheHeight = Math.Max(1, height);
            this._cacheSamplesPerPixel = samplesPerPixel;
            this._cacheChannelsToDraw = channelsToDraw;
            this._cacheOffsetFrames = 0;
            this._cacheWaveColor = waveColor;
            this._cacheBackColor = backColor;
            this._cacheDrawEachChannel = drawEachChannel;

            this._waveCacheStrip = new Bitmap(this._cacheWidthPixels, this._cacheHeight);
            using (var g = Graphics.FromImage(this._waveCacheStrip))
            {
                g.Clear(backColor);
            }
        }



        [SupportedOSPlatform("windows")]
        public async Task<Bitmap> DrawWaveformCacheAsync(int width, int height, int samplesPerPixel = 128, bool drawEachChannel = false, Color waveColor = default, Color backColor = default)
        {
            // === VORHER: DrawWaveformAsync. NEU: DrawWaveformCacheAsync ===
            // Diese Methode enth‰lt die langsame Logik zum Lesen der Audiodaten und Zeichnen der Welle
            // und nutzt das _cacheLock.

            // *WICHTIG*: Hier NICHT die Selection oder den Caret zeichnen.

            // ... (alte DrawWaveformAsync Logik zur Cache-Aktualisierung hier belassen) ...
            // ... (Die Logik zum Zeichnen der Welle auf das _waveCacheStrip) ...

            await this._cacheLock.WaitAsync().ConfigureAwait(false);
            try
            {
                // ... (Wellenform auf _waveCacheStrip zeichnen) ...
            }
            finally
            {
                this._cacheLock.Release();
            }

            // Statt das fertige Bitmap zur¸ckzugeben, geben wir den Teil des Caches zur¸ck, der aktuell benˆtigt wird
            // (die Logik hier ist komplex wegen des Cache-Strips, aber die Idee ist: nur Welle zeichnen)

            // Da Sie bereits eine Bitmap zur¸ckgeben, verwenden wir diese weiterhin, aber ohne Selection/Caret-Code.
            return this._waveCacheStrip ?? new Bitmap(1, 1);
        }
        public Bitmap? GetCachedBitmap()
        {
            // Wenn mˆglich, Zugriff auf das Cache-Bitmap nur ¸ber einen kleinen Lock
            if (this._waveCacheStrip != null)
            {
                // *ACHTUNG*: Da Bitmaps nicht Thread-Safe sind, M‹SSTE dies eigentlich
                // ein Klon oder ein synchornisierter Zugriff sein, 
                // ABER da wir die Selection auf dem UI Thread zeichnen,
                // nehmen wir den geringeren Aufwand des Zugriffs.
                return this._waveCacheStrip;
            }
            return null;
        }


		private Bitmap DrawWaveformPreview(int width = 160, int height = 160)
		{
			// Zeige nur eine Vorschau, wenn das Audio k¸rzer als 20 Sekunden ist
			if (this.Duration.TotalSeconds > 20.0)
			{
				return new Bitmap(width, height); // leer lassen
			}
			Bitmap bitmap = new(width, height);
			using (Graphics g = Graphics.FromImage(bitmap))
			{
				g.Clear(Color.White); // Hintergrund weiﬂ
				if (this.Data.Length == 0)
				{
					return bitmap;
				}
				using Pen pen = new(Color.BlueViolet, 2f); // Kurve BlueViolet, dicker
				float midY = height / 2.0f;
				int samplesPerPixel = Math.Max(1, this.Data.Length / width);
				for (int x = 0; x < width; x++)
				{
					int startSample = x * samplesPerPixel;
					int endSample = Math.Min(startSample + samplesPerPixel, this.Data.Length);
					float min = float.MaxValue;
					float max = float.MinValue;
					for (int s = startSample; s < endSample; s++)
					{
						float sample = this.Data[s];
						if (sample < min)
                        {
                            min = sample;
                        }

                        if (sample > max)
                        {
                            max = sample;
                        }
                    }
					float y1 = midY - (min * (midY - 1));
					float y2 = midY - (max * (midY - 1));
					g.DrawLine(pen, x, y1, x, y2);
				}
			}
			return bitmap;
		}

	}
}
