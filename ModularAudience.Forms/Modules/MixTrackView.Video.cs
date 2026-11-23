using ModularAudience.Audio;
using ModularAudience.Audio.Arrangement;
using ModularAudience.Audio.Processing;
using ModularAudience.Forms.Modules.Dialogs;
using ModularAudience.Shared;
using ModularAudience.Video;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace ModularAudience.Forms.Modules;

public sealed partial class MixTrackView
{
    // ------------------------------------------------------------------
    // Video track — toolbar, import, lane management
    // ------------------------------------------------------------------

    private void AddVideoTrack()
    {
        if (busy || PlaybackLocksEdits)
        {
            return;
        }

        string initialDir = _lastVideoFolder ?? string.Empty;
        if (string.IsNullOrWhiteSpace(initialDir) || !Directory.Exists(initialDir))
        {
            initialDir = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        }

        using var dialog = new OpenFileDialog
        {
            InitialDirectory = initialDir,
            Filter = VideoFormat.Filter,
            Multiselect = false,
            Title = "Import video for mix track",
            RestoreDirectory = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try { _lastVideoFolder = Path.GetDirectoryName(dialog.FileName) ?? _lastVideoFolder; } catch { }

        string videoPath = dialog.FileName;
        AddVideoTrackFromPath(videoPath);
    }

    private void AddVideoTrackFromPath(string videoPath)
    {
        if (busy || PlaybackLocksEdits)
        {
            return;
        }

        if (!File.Exists(videoPath))
        {
            _ = ErrorAsync(new FileNotFoundException("The selected video file no longer exists.", videoPath));
            return;
        }

        _ = RunAsync(async token =>
        {
            string? ffmpegPath = await EnsureFfmpegAvailableAsync(token);
            if (ffmpegPath == null)
            {
                return;
            }

            status.Text = $"Probing video: {Path.GetFileName(videoPath)}...";
            using var video = new VideoObj(videoPath, ffmpegPath);
            await video.ProbeAsync(token);
            if (!video.HasVideo)
            {
                throw new InvalidDataException("The selected file does not contain a usable video stream.");
            }

            string before = CaptureVideoUndoState();
            MixLane videoLane = _videoLane ?? new MixLane { Name = "Video #1", IsVideo = true, Gain = 1 };
            if (_videoLane == null)
            {
                Arrangement.Lanes.Add(videoLane);
                _videoLane = videoLane;
            }

            var videoSource = new MixSource
            {
                Name = video.Name,
                IsVideo = true,
                VideoFilePath = videoPath,
                VideoDuration = video.Duration.TotalSeconds,
                VideoWidth = video.Width,
                VideoHeight = video.Height,
                VideoFrameRate = video.FrameRate,
                SampleRate = 44100,
                Channels = 2,
                Data = []
            };
            Arrangement.Sources.Add(videoSource.Id, videoSource);

            double start = videoLane.Clips.Count == 0 ? 0
                : videoLane.Clips.Max(clip => clip.Start + Arrangement.ClipDuration(clip));
            var videoClip = Arrangement.AddClip(videoLane, videoSource, start, 0, video.Duration.TotalSeconds);
            videoClip.IsVideo = true;
            videoClip.Name = video.Name;

            selectedLane = videoLane.Id;
            selectedClip = videoClip.Id;
            CommitVideoEdit(before);
            ChangedVideo();
            status.Text = $"Video imported: {video.Name} ({video.Width}x{video.Height}, {video.Duration.TotalSeconds:F1}s).";
        });
    }

    private static void EnsureVideoAudioFitsMemory(double durationSeconds)
    {
        double sampleCount = durationSeconds * 44100 * 2;
        if (!double.IsFinite(sampleCount) || sampleCount > Array.MaxLength)
        {
            throw new InvalidOperationException("The video's extracted audio is too large to load as one in-memory AudioObj.");
        }
    }

    private async Task<string?> EnsureFfmpegAvailableAsync(CancellationToken cancellationToken)
    {
        var manager = new FfmpegBinaryManager();
        string? existing = manager.FindInstalledFfmpeg();
        if (existing != null)
        {
            return existing;
        }

        DialogResult answer = await this.windowGroup.AskAsync(
            "FFmpeg is required to import and render video. Download FFmpeg now? This downloads FFmpeg only; it does not install or update yt-dlp.",
            "FFmpeg is missing", MessageBoxButtons.YesNo);
        if (answer != DialogResult.Yes)
        {
            return null;
        }

        using var installCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var progressDialog = new ProgressDialog("Downloading FFmpeg...", windowCloseDelay: 0,
            ct: installCancellation.Token, cancellationSource: installCancellation);
        progressDialog.Show(this);
        try
        {
            var installProgress = new Progress<string>(message =>
            {
                progressDialog.SetStatus(message);
                Match match = System.Text.RegularExpressions.Regex.Match(message, @"(?<percent>\d+(?:\.\d+)?)%");
                if (match.Success)
                {
                    if (double.TryParse(match.Groups["percent"].Value,
                        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double percent))
                    {
                        progressDialog.Report(percent / 100);
                    }
                }
            });
            string ffmpegPath = await manager.InstallFfmpegAsync(installProgress, installCancellation.Token);
            progressDialog.Complete();
            return ffmpegPath;
        }
        catch (OperationCanceledException)
        {
            progressDialog.Close();
            return null;
        }
        catch (Exception error)
        {
            progressDialog.Close();
            await ErrorAsync(error);
            return null;
        }
    }

    private void PruneVideoThumbnailSamples()
    {
        HashSet<Guid> activeClips = Arrangement.Lanes.Where(lane => lane.IsVideo)
            .SelectMany(lane => lane.Clips).Select(clip => clip.Id).ToHashSet();
        foreach (Guid clipId in _videoThumbSampleFractions.Keys.Where(id => !activeClips.Contains(id)).ToArray())
        {
            _videoThumbSampleFractions.Remove(clipId);
        }
    }

    private async Task<string?> ExtractAudioWithProgressAsync(VideoObj video, CancellationToken cancellationToken)
    {
        using var extractionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var progressDialog = new ProgressDialog("Extracting video audio...", ct: extractionCancellation.Token,
            cancellationSource: extractionCancellation);
        progressDialog.Show(this);
        try
        {
            var progress = new Progress<double>(progressDialog.Report);
            string? path = await video.ExtractAudioToTempWavAsync(progress, extractionCancellation.Token);
            progressDialog.Complete();
            return path;
        }
        catch
        {
            progressDialog.Close();
            throw;
        }
    }

    private double FindVideoDropStart(MixLane lane, double desiredStart, double duration, Guid? movingClipId = null)
    {
        MixClip[] obstacles = lane.Clips.Where(clip => clip.Id != movingClipId).OrderBy(clip => clip.Start).ToArray();
        if (obstacles.Length == 0)
        {
            return desiredStart;
        }

        var candidates = new List<double> { obstacles[0].Start - duration, obstacles[^1].Start + Arrangement.ClipDuration(obstacles[^1]) };
        for (int index = 1; index < obstacles.Length; index++)
        {
            double gapStart = obstacles[index - 1].Start + Arrangement.ClipDuration(obstacles[index - 1]);
            double gapEnd = obstacles[index].Start - duration;
            if (gapStart <= gapEnd)
            {
                candidates.Add(Math.Clamp(desiredStart, gapStart, gapEnd));
            }
        }
        return candidates.MinBy(candidate => Math.Abs(candidate - desiredStart));
    }

    private void SyncLinkedVideoAudio(MixClip videoClip, double originalVideoOffset, double originalVideoDuration,
        double originalVideoStart, double originalAudioOffset, double originalAudioDuration, double originalAudioStart)
    {
        if (videoClip.LinkedClipId is not Guid audioId || originalVideoDuration <= 0)
        {
            return;
        }

        MixClip? audioClip = Arrangement.Lanes.SelectMany(lane => lane.Clips).FirstOrDefault(clip => clip.Id == audioId);
        if (audioClip == null || !Arrangement.Sources.TryGetValue(audioClip.SourceId, out MixSource? audioSource))
        {
            return;
        }

        double sourceRatio = originalAudioDuration / originalVideoDuration;
        audioClip.Start = originalAudioStart + videoClip.Start - originalVideoStart;
        audioClip.SourceOffset = Math.Clamp(originalAudioOffset
            + (videoClip.SourceOffset - originalVideoOffset) * sourceRatio, 0, audioSource.Duration);
        audioClip.SourceDuration = Math.Clamp(videoClip.SourceDuration * sourceRatio, 0.001,
            audioSource.Duration - audioClip.SourceOffset);
    }

    // ------------------------------------------------------------------
    // Video clip painting
    // ------------------------------------------------------------------

    private void DrawVideoClip(Graphics g, int row, MixClip clip, Rectangle rect, bool selected)
    {
        if (!Arrangement.Sources.TryGetValue(clip.SourceId, out var source) || !source.IsVideo)
        {
            return;
        }

        // Fill background with video clip color.
        Color clipColor = ClipColor(selected ? Arrangement.Visual.SelectedClip : Arrangement.Visual.Clip, row);
        using var brush = new SolidBrush(clipColor);
        g.FillRectangle(brush, rect);

        // Draw frame thumbnails across the clip (lazy async extraction).
        double duration = Arrangement.ClipDuration(clip);
        if (duration <= 0)
        {
            return;
        }

        double aspect = source.VideoWidth / (double)source.VideoHeight;
        int availableHeight = Math.Min(150, Math.Max(1, rect.Height - 6));
        int aspectWidth = Math.Max(1, (int)Math.Round(availableHeight * aspect));
        int desiredCount = Math.Clamp((rect.Width - 4) / Math.Max(18, aspectWidth + 2), 1, 32);
        double[] sampleFractions = GetVideoThumbnailSampleFractions(clip.Id, desiredCount);
        int cellWidth = Math.Max(1, (rect.Width - 4) / desiredCount);
        int thumbWidth = Math.Min(aspectWidth, cellWidth - 2);
        int thumbHeight = thumbWidth > 0 ? Math.Min(availableHeight, Math.Max(1, (int)Math.Round(thumbWidth / aspect))) : 0;
        if (thumbWidth >= 16 && thumbHeight >= 9)
        {
            GraphicsState state = g.Save();
            g.SetClip(rect, CombineMode.Intersect);
            try
            {
                double[] visibleSamples = SelectVideoThumbnailSamples(sampleFractions, desiredCount);
                foreach (double fraction in visibleSamples)
                {
                    int x = rect.X + 2 + (int)Math.Round(fraction * (rect.Width - 4)) - thumbWidth / 2;
                    if (x + thumbWidth < Header || x >= canvas.Width)
                    {
                        continue;
                    }

                    double time = fraction * duration;
                    double sourceTime = clip.SourceOffset + time * Arrangement.Rate(clip);
                    var thumbKey = VideoThumbKey(source.Id, sourceTime);
                    int y = rect.Y + 3 + (availableHeight - thumbHeight) / 2;
                    var thumbRect = new Rectangle(x, y, thumbWidth, thumbHeight);

                    bool frameCached;
                    lock (_videoThumbCache)
                    {
                        frameCached = _videoThumbCache.TryGetValue(thumbKey, out var thumbBmp);
                        if (frameCached)
                        {
                            _videoThumbCacheOrder.Remove(thumbKey);
                            _videoThumbCacheOrder.Add(thumbKey);
                            g.DrawImage(thumbBmp!, thumbRect);
                        }
                    }
                    if (frameCached)
                    {
                        continue;
                    }

                    using var filmBrush = new SolidBrush(Color.FromArgb(60, 40, 40));
                    g.FillRectangle(filmBrush, thumbRect);
                    using var filmPen = new Pen(Color.FromArgb(100, 80, 80));
                    g.DrawRectangle(filmPen, thumbRect);
                    _ = ExtractAndCacheVideoThumbAsync(source.Id, source.VideoFilePath!, sourceTime, _videoWorkCancellation.Token);
                }
            }
            finally { g.Restore(state); }
        }

        // Clip name label.
        int labelX = Math.Max(Header + 4, rect.X + 4);
        System.Windows.Forms.TextRenderer.DrawText(g, clip.Name, Font, new Point(labelX, rect.Y + 2),
            VisualColor(Arrangement.Visual.Text), System.Windows.Forms.TextFormatFlags.EndEllipsis);

        // Duration label at right end.
        string durationText = $"{duration:F1}s";
        using var durFont = new Font(Font.FontFamily, 7, FontStyle.Regular);
        System.Windows.Forms.TextRenderer.DrawText(g, durationText, durFont, new Point(rect.Right - 30, rect.Bottom - 14),
            Color.FromArgb(180, 180, 180), System.Windows.Forms.TextFormatFlags.EndEllipsis | System.Windows.Forms.TextFormatFlags.Right);

        // Selection border.
        using var edge = new Pen(selected ? Color.White : Color.FromArgb(110, 150, 165));
        g.DrawRectangle(edge, rect);
        using var handleBrush = new SolidBrush(Color.FromArgb(65, Color.Black));
        g.FillRectangle(handleBrush, rect.X, rect.Y, rect.Width, Math.Min(21, rect.Height));
    }

    private double[] GetVideoThumbnailSampleFractions(Guid clipId, int desiredCount)
    {
        if (!_videoThumbSampleFractions.TryGetValue(clipId, out List<double>? samples))
        {
            _videoThumbSampleFractions.Add(clipId, samples = []);
        }

        if (samples.Count == 0)
        {
            for (int index = 0; index < desiredCount; index++)
            {
                samples.Add((index + 0.5) / desiredCount);
            }
        }
        while (samples.Count < desiredCount)
        {
            double[] ordered = samples.Order().Prepend(0).Append(1).ToArray();
            double fraction = ordered.Zip(ordered.Skip(1), (left, right) => (Left: left, Right: right))
                .OrderByDescending(gap => gap.Right - gap.Left)
                .ThenBy(gap => gap.Left)
                .Select(gap => (gap.Left + gap.Right) / 2)
                .First();
            samples.Add(fraction);
        }
        samples.Sort();
        return samples.ToArray();
    }

    private static double[] SelectVideoThumbnailSamples(IReadOnlyList<double> samples, int desiredCount)
    {
        if (samples.Count <= desiredCount)
        {
            return samples.ToArray();
        }

        if (desiredCount == 1)
        {
            return [samples[samples.Count / 2]];
        }

        return Enumerable.Range(0, desiredCount)
            .Select(index => samples[(int)Math.Round(index * (samples.Count - 1d) / (desiredCount - 1))])
            .Distinct().ToArray();
    }

    private void DrawVideoHoverPreview(Graphics g, MixClip clip, double timeWithinClip)
    {
        if (!Arrangement.Sources.TryGetValue(clip.SourceId, out MixSource? source) || !source.IsVideo)
        {
            return;
        }

        var previewRect = GetVideoHoverPreviewRect(_videoHoverPoint, source);
        using var bgBrush = new SolidBrush(Color.FromArgb(230, 20, 20, 20));
        g.FillRectangle(bgBrush, previewRect);

        double sourceTime = clip.SourceOffset + timeWithinClip * Arrangement.Rate(clip);
        var thumbKey = VideoThumbKey(source.Id, sourceTime);
        bool frameCached;
        lock (_videoThumbCache)
        {
            frameCached = _videoThumbCache.TryGetValue(thumbKey, out var thumbBmp);
            if (frameCached)
            {
                _videoThumbCacheOrder.Remove(thumbKey);
                _videoThumbCacheOrder.Add(thumbKey);
                g.DrawImage(thumbBmp!, previewRect);
            }
        }
        if (!frameCached)
        {
            using var filmBrush = new SolidBrush(Color.FromArgb(40, 30, 30));
            g.FillRectangle(filmBrush, previewRect);
        }
        using var borderPen = new Pen(Color.FromArgb(255, 255, 0), 2);
        g.DrawRectangle(borderPen, previewRect);
    }

    private Rectangle GetVideoHoverPreviewRect(Point pointer, MixSource source)
    {
        double scale = Math.Min(240d / source.VideoWidth, 160d / source.VideoHeight);
        int width = Math.Max(1, (int)Math.Round(source.VideoWidth * scale));
        int height = Math.Max(1, (int)Math.Round(source.VideoHeight * scale));
        int x = pointer.X + 16;
        if (x + width > canvas.Width - 4)
        {
            x = pointer.X - width - 16;
        }

        x = Math.Clamp(x, Header + 2, Math.Max(Header + 2, canvas.Width - width - 2));
        int y = pointer.Y - height - 16;
        if (y < Ruler + 2)
        {
            y = pointer.Y + 18;
        }

        y = Math.Clamp(y, Ruler + 2, Math.Max(Ruler + 2, canvas.Height - height - 2));
        return new Rectangle(x, y, width, height);
    }

    private void UpdateVideoHoverPreview(MixClip clip, Point pointer)
    {
        Rectangle previousBounds = Rectangle.Empty;
        (Guid SourceId, int FrameBucket)? previousKey = null;
        if (_hoveredVideoClip is { } previous
            && Arrangement.Sources.TryGetValue(previous.SourceId, out MixSource? previousSource))
        {
            previousBounds = GetVideoHoverPreviewRect(_videoHoverPoint, previousSource);
            previousKey = VideoThumbKey(previousSource.Id,
                previous.SourceOffset + _hoveredVideoTime * Arrangement.Rate(previous));
        }

        if (!Arrangement.Sources.TryGetValue(clip.SourceId, out MixSource? source) || !source.IsVideo)
        {
            return;
        }

        int row = Arrangement.Lanes.FindIndex(lane => lane.Clips.Contains(clip));
        if (row < 0)
        {
            return;
        }

        Rectangle clipBounds = ClipRect(row, clip);
        Rectangle currentBounds = GetVideoHoverPreviewRect(pointer, source);
        double duration = Arrangement.ClipDuration(clip);
        double time = Math.Clamp((pointer.X - clipBounds.Left) / (double)Math.Max(1, clipBounds.Width) * duration, 0, duration);
        var currentKey = VideoThumbKey(source.Id, clip.SourceOffset + time * Arrangement.Rate(clip));

        _hoveredVideoClip = clip;
        _hoveredVideoTime = time;
        _videoHoverPoint = pointer;
        if (previousKey != currentKey)
        {
            _videoHoverCancellation?.Cancel();
            _videoHoverCancellation?.Dispose();
            _videoHoverCancellation = CancellationTokenSource.CreateLinkedTokenSource(_videoWorkCancellation.Token);
            _ = ExtractAndCacheVideoThumbAsync(source.Id, source.VideoFilePath ?? string.Empty,
                clip.SourceOffset + time * Arrangement.Rate(clip), _videoHoverCancellation.Token);
        }

        canvas.Invalidate(previousBounds.IsEmpty ? currentBounds : Rectangle.Union(previousBounds, currentBounds));
    }

    private void ClearVideoHoverPreview()
    {
        Rectangle previousBounds = Rectangle.Empty;
        if (_hoveredVideoClip is { } previous
            && Arrangement.Sources.TryGetValue(previous.SourceId, out MixSource? source))
        {
            previousBounds = GetVideoHoverPreviewRect(_videoHoverPoint, source);
        }

        _hoveredVideoClip = null;
        _videoHoverCancellation?.Cancel();
        _videoHoverCancellation?.Dispose();
        _videoHoverCancellation = null;
        if (!previousBounds.IsEmpty)
        {
            canvas.Invalidate(previousBounds);
        }
    }

    // ------------------------------------------------------------------
    // Async video frame thumbnail extraction with caching
    // ------------------------------------------------------------------

    private static (Guid SourceId, int FrameBucket) VideoThumbKey(Guid sourceId, double sourceTime)
    {
        int bucket = (int)Math.Round(Math.Max(0, sourceTime) / VideoThumbFrameStep, MidpointRounding.AwayFromZero);
        return (sourceId, bucket);
    }

    private async Task ExtractAndCacheVideoThumbAsync(Guid sourceId, string videoPath, double sourceTime, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
        {
            return;
        }

        var thumbKey = VideoThumbKey(sourceId, sourceTime);
        lock (_videoThumbCache)
        {
            if (_videoThumbCache.ContainsKey(thumbKey) || _videoThumbPending.Count >= 128
                || !_videoThumbPending.Add(thumbKey))
            {
                return;
            }
        }

        bool gateAcquired = false;
        try
        {
            await _videoThumbGate.WaitAsync(token).ConfigureAwait(false);
            gateAcquired = true;
            double decodeTime = thumbKey.FrameBucket * VideoThumbFrameStep;
            Bitmap? bitmap = await GetVideoDecoder(sourceId, videoPath)
                .ExtractFrameAsync(decodeTime, maxWidth: VideoThumbDecodeWidth, token).ConfigureAwait(false);
            if (bitmap == null)
            {
                return;
            }

            if (token.IsCancellationRequested)
            {
                bitmap.Dispose();
                return;
            }

            lock (_videoThumbCache)
            {
                if (token.IsCancellationRequested)
                {
                    bitmap.Dispose();
                    return;
                }
                if (_videoThumbCache.Count >= MaxVideoThumbCache)
                {
                    var oldest = _videoThumbCacheOrder[0];
                    _videoThumbCacheOrder.RemoveAt(0);
                    if (_videoThumbCache.Remove(oldest, out var oldBitmap))
                    {
                        oldBitmap.Dispose();
                    }
                }
                _videoThumbCache[thumbKey] = bitmap;
                _videoThumbCacheOrder.Add(thumbKey);
            }

            if (!IsDisposed && !Disposing && IsHandleCreated)
            {
                try { BeginInvoke((Action)(() => InvalidateVideoThumbnail(sourceId, thumbKey))); }
                catch (InvalidOperationException) when (IsDisposed || Disposing || !IsHandleCreated) { }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { LogManager.Log("Video thumbnail extraction failed.", error); }
        finally
        {
            lock (_videoThumbCache)
            {
                _videoThumbPending.Remove(thumbKey);
            }

            if (gateAcquired)
            {
                _videoThumbGate.Release();
            }

            if (token.IsCancellationRequested && !_videoWorkCancellation.IsCancellationRequested
                && !IsDisposed && !Disposing && IsHandleCreated)
            {
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        if (_hoveredVideoClip is not { } hover || hover.SourceId != sourceId
                            || _videoHoverCancellation is not { } hoverCancellation || hoverCancellation.IsCancellationRequested
                            || !Arrangement.Sources.TryGetValue(sourceId, out MixSource? currentSource)
                            || VideoThumbKey(sourceId, hover.SourceOffset + _hoveredVideoTime * Arrangement.Rate(hover)) != thumbKey)
                        {
                            return;
                        }

                        double currentTime = hover.SourceOffset + _hoveredVideoTime * Arrangement.Rate(hover);
                        _ = ExtractAndCacheVideoThumbAsync(sourceId, currentSource.VideoFilePath ?? string.Empty,
                            currentTime, hoverCancellation.Token);
                    }));
                }
                catch (InvalidOperationException) when (IsDisposed || Disposing || !IsHandleCreated) { }
            }
        }
    }

    private VideoObj GetVideoDecoder(Guid sourceId, string videoPath)
    {
        string fullPath = Path.GetFullPath(videoPath);
        lock (_videoDecoderLock)
        {
            if (_videoDecoders.TryGetValue(sourceId, out VideoObj? decoder)
                && string.Equals(decoder.FilePath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                return decoder;
            }

            decoder?.Dispose();
            decoder = new VideoObj(fullPath);
            _videoDecoders[sourceId] = decoder;
            return decoder;
        }
    }

    private void InvalidateVideoThumbnail(Guid sourceId, (Guid SourceId, int FrameBucket) thumbKey)
    {
        int firstVisibleRow = Math.Max(0, vertical.Value);
        int visibleRows = Math.Max(1, (canvas.Height - Ruler + LaneHeight - 1) / LaneHeight);
        for (int row = firstVisibleRow; row < Math.Min(Arrangement.Lanes.Count, firstVisibleRow + visibleRows); row++)
        {
            foreach (MixClip clip in Arrangement.Lanes[row].Clips.Where(clip => clip.SourceId == sourceId))
            {
                Rectangle bounds = Rectangle.Intersect(ClipRect(row, clip), new Rectangle(Header, Ruler,
                    Math.Max(0, canvas.Width - Header), Math.Max(0, canvas.Height - Ruler)));
                if (!bounds.IsEmpty)
                {
                    canvas.Invalidate(bounds);
                }
            }
        }

        if (_hoveredVideoClip is { } hover && Arrangement.Sources.TryGetValue(hover.SourceId, out MixSource? source)
            && VideoThumbKey(source.Id, hover.SourceOffset + _hoveredVideoTime * Arrangement.Rate(hover)) == thumbKey)
        {
            canvas.Invalidate(GetVideoHoverPreviewRect(_videoHoverPoint, source));
        }
    }

    private void DisposeVideoResources()
    {
        _videoHoverCancellation?.Cancel();
        _videoHoverCancellation?.Dispose();
        _videoHoverCancellation = null;
        _videoWorkCancellation.Cancel();
        ClearVideoThumbnailCache();
        lock (_videoDecoderLock)
        {
            foreach (VideoObj decoder in _videoDecoders.Values)
            {
                decoder.Dispose();
            }

            _videoDecoders.Clear();
        }
        _videoWorkCancellation.Dispose();
    }

    private void ClearVideoThumbnailCache()
    {
        lock (_videoThumbCache)
        {
            foreach (Bitmap bitmap in _videoThumbCache.Values)
            {
                bitmap.Dispose();
            }

            _videoThumbCache.Clear();
            _videoThumbCacheOrder.Clear();
            _videoThumbSampleFractions.Clear();
        }
    }

    // ------------------------------------------------------------------
    // Video undo /redo (separate stack, max 16)
    // ------------------------------------------------------------------

    private string CaptureVideoUndoState()
    {
        MixLane[] videoLanes = Arrangement.Lanes.Where(lane => lane.IsVideo).ToArray();
        HashSet<Guid> videoClipIds = videoLanes.SelectMany(lane => lane.Clips).Select(clip => clip.Id).ToHashSet();
        MixLane[] linkedAudioLanes = Arrangement.Lanes.Where(lane => !lane.IsVideo
            && lane.Clips.Any(clip => clip.LinkedClipId is Guid linkedId && videoClipIds.Contains(linkedId))).ToArray();
        MixLane[] affectedLanes = videoLanes.Concat(linkedAudioLanes).ToArray();
        return JsonSerializer.Serialize(new VideoUndoState
        {
            Lanes = affectedLanes.Select(lane => new VideoUndoState.LaneDto
            {
                Id = lane.Id,
                Index = Arrangement.Lanes.IndexOf(lane),
                Name = lane.Name,
                Gain = lane.Gain,
                Mute = lane.Mute,
                Solo = lane.Solo,
                IsVideo = lane.IsVideo,
                Clips = (lane.IsVideo ? lane.Clips : lane.Clips.Where(clip => clip.LinkedClipId is Guid linkedId && videoClipIds.Contains(linkedId)))
                    .Select(clip => new VideoUndoState.ClipDto
                    {
                        Id = clip.Id, SourceId = clip.SourceId, Name = clip.Name, Start = clip.Start,
                        SourceOffset = clip.SourceOffset, SourceDuration = clip.SourceDuration, Gain = clip.Gain,
                        FadeIn = clip.FadeIn, FadeOut = clip.FadeOut, TempoMode = clip.TempoMode,
                        IsVideo = clip.IsVideo, LinkedClipId = clip.LinkedClipId
                    }).ToList()
            }).ToList(),
            Sources = videoLanes.SelectMany(lane => lane.Clips).Select(clip => clip.SourceId).Distinct()
                .Where(id => Arrangement.Sources.ContainsKey(id)).Select(id => Arrangement.Sources[id])
                .Where(source => source.IsVideo).Select(source => new VideoUndoState.SourceDto
                {
                    Id = source.Id, Name = source.Name, VideoFilePath = source.VideoFilePath,
                    VideoDuration = source.VideoDuration, VideoWidth = source.VideoWidth,
                    VideoHeight = source.VideoHeight, VideoFrameRate = source.VideoFrameRate
                }).ToList()
        });
    }

    private void CommitVideoEdit(string beforeState)
    {
        string afterState = CaptureVideoUndoState();
        if (beforeState == afterState)
        {
            return;
        }

        PushVideoUndo(beforeState, afterState);
    }

    private void PushVideoUndo(string beforeState, string afterState)
    {
        _videoUndo.Push(JsonSerializer.Serialize(new VideoUndoEntry { Before = beforeState, After = afterState }));
        TrimVideoHistory(_videoUndo);
        _videoRedo.Clear();
    }

    private static void TrimVideoHistory(Stack<string> history)
    {
        if (history.Count <= MaxVideoUndo)
        {
            return;
        }

        string[] newestFirst = history.Take(MaxVideoUndo).ToArray();
        history.Clear();
        for (int index = newestFirst.Length - 1; index >= 0; index--)
        {
            history.Push(newestFirst[index]);
        }
    }

    private void UndoVideo()
    {
        if (busy || PlaybackLocksEdits)
        {
            return;
        }

        if (_videoUndo.Count == 0)
        {
            return;
        }

        string entry = _videoUndo.Pop();
        var data = JsonSerializer.Deserialize<VideoUndoEntry>(entry)
            ?? throw new InvalidDataException("Invalid video undo entry.");
        ApplyVideoUndoState(JsonSerializer.Deserialize<VideoUndoState>(data.Before)
            ?? throw new InvalidDataException("Invalid video undo state."));
        _videoRedo.Push(entry);
        ChangedVideo();
        status.Text = "Video edit undone. Ctrl+Y to redo.";
    }

    private void RedoVideo()
    {
        if (busy || PlaybackLocksEdits)
        {
            return;
        }

        if (_videoRedo.Count == 0)
        {
            return;
        }

        string entry = _videoRedo.Pop();
        var data = JsonSerializer.Deserialize<VideoUndoEntry>(entry)
            ?? throw new InvalidDataException("Invalid video redo entry.");
        ApplyVideoUndoState(JsonSerializer.Deserialize<VideoUndoState>(data.After)
            ?? throw new InvalidDataException("Invalid video redo state."));
        _videoUndo.Push(entry);
        TrimVideoHistory(_videoUndo);
        ChangedVideo();
        status.Text = "Video edit redone. Ctrl+Z to undo.";
    }

    private void ApplyVideoUndoState(VideoUndoState target)
    {
        HashSet<Guid> currentVideoClipIds = Arrangement.Lanes.Where(lane => lane.IsVideo)
            .SelectMany(lane => lane.Clips).Select(clip => clip.Id).ToHashSet();
        HashSet<Guid> linkedAudioIds = Arrangement.Lanes.Where(lane => !lane.IsVideo)
            .SelectMany(lane => lane.Clips).Where(clip => clip.LinkedClipId is Guid id && currentVideoClipIds.Contains(id))
            .Select(clip => clip.Id).ToHashSet();
        HashSet<Guid> linkedAudioLaneIds = Arrangement.Lanes.Where(lane => lane.Clips.Any(clip => linkedAudioIds.Contains(clip.Id)))
            .Select(lane => lane.Id).ToHashSet();

        foreach (MixLane lane in Arrangement.Lanes)
        {
            lane.Clips.RemoveAll(clip => linkedAudioIds.Contains(clip.Id));
        }

        Arrangement.Lanes.RemoveAll(lane => lane.IsVideo);
        Arrangement.Lanes.RemoveAll(lane => linkedAudioLaneIds.Contains(lane.Id) && lane.Clips.Count == 0);
        _videoLane = null;

        foreach (VideoUndoState.SourceDto sourceData in target.Sources)
        {
            if (!Arrangement.Sources.TryGetValue(sourceData.Id, out MixSource? source))
            {
                source = SourceDtoToMixSource(sourceData);
                Arrangement.Sources.Add(source.Id, source);
            }
            source.Name = sourceData.Name;
            source.IsVideo = true;
            source.VideoFilePath = sourceData.VideoFilePath;
            source.VideoDuration = sourceData.VideoDuration;
            source.VideoWidth = sourceData.VideoWidth;
            source.VideoHeight = sourceData.VideoHeight;
            source.VideoFrameRate = sourceData.VideoFrameRate;
        }

        foreach (VideoUndoState.LaneDto laneData in target.Lanes.OrderBy(lane => lane.Index))
        {
            MixLane? lane = Arrangement.Lanes.FirstOrDefault(existing => existing.Id == laneData.Id);
            if (laneData.IsVideo)
            {
                lane = new MixLane { Id = laneData.Id, Name = laneData.Name, Gain = laneData.Gain,
                    Mute = laneData.Mute, Solo = laneData.Solo, IsVideo = true };
                Arrangement.Lanes.Insert(Math.Clamp(laneData.Index, 0, Arrangement.Lanes.Count), lane);
                _videoLane = lane;
            }
            else if (lane == null)
            {
                lane = new MixLane { Id = laneData.Id, Name = laneData.Name, Gain = laneData.Gain,
                    Mute = laneData.Mute, Solo = laneData.Solo };
                Arrangement.Lanes.Insert(Math.Clamp(laneData.Index, 0, Arrangement.Lanes.Count), lane);
            }
            foreach (VideoUndoState.ClipDto clipData in laneData.Clips)
            {
                if (!Arrangement.Lanes.SelectMany(existing => existing.Clips).Any(existing => existing.Id == clipData.Id))
                {
                    lane.Clips.Add(CloneClipDto(clipData));
                }
            }
        }
    }

    private static MixSource SourceDtoToMixSource(VideoUndoState.SourceDto dto) => new()
    {
        Id = dto.Id, Name = dto.Name, IsVideo = dto.IsVideo, VideoFilePath = dto.VideoFilePath,
        VideoDuration = dto.VideoDuration, VideoWidth = dto.VideoWidth, VideoHeight = dto.VideoHeight,
        VideoFrameRate = dto.VideoFrameRate, SampleRate = 44100, Channels = 2, Bpm = 0, Data = []
    };

    private static MixClip CloneClipDto(VideoUndoState.ClipDto dto) => new()
    {
        Id = dto.Id, SourceId = dto.SourceId, Name = dto.Name, Start = dto.Start,
        SourceOffset = dto.SourceOffset, SourceDuration = dto.SourceDuration,
        Gain = dto.Gain, FadeIn = dto.FadeIn, FadeOut = dto.FadeOut,
        TempoMode = dto.TempoMode, IsVideo = dto.IsVideo, LinkedClipId = dto.LinkedClipId
    };

    private sealed class VideoUndoEntry
    {
        public string Before { get; set; } = "";
        public string After { get; set; } = "";
    }

    private sealed class VideoUndoState
    {
        public List<LaneDto> Lanes { get; set; } = [];
        public List<SourceDto> Sources { get; set; } = [];

        public sealed class LaneDto
        {
            public Guid Id { get; set; }
            public int Index { get; set; }
            public string Name { get; set; } = "";
            public double Gain { get; set; }
            public bool Mute { get; set; }
            public bool Solo { get; set; }
            public bool IsVideo { get; set; }
            public List<ClipDto> Clips { get; set; } = [];
        }

        public sealed class ClipDto
        {
            public Guid Id { get; set; }
            public Guid SourceId { get; set; }
            public string Name { get; set; } = "";
            public double Start { get; set; }
            public double SourceOffset { get; set; }
            public double SourceDuration { get; set; }
            public double Gain { get; set; }
            public double FadeIn { get; set; }
            public double FadeOut { get; set; }
            public ClipTempoMode TempoMode { get; set; }
            public bool IsVideo { get; set; }
            public Guid? LinkedClipId { get; set; }
        }

        public sealed class SourceDto
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = "";
            public bool IsVideo { get; set; }
            public string? VideoFilePath { get; set; }
            public double VideoDuration { get; set; }
            public int VideoWidth { get; set; }
            public int VideoHeight { get; set; }
            public double VideoFrameRate { get; set; }
        }
    }

    // ------------------------------------------------------------------
    // Video context menu
    // ------------------------------------------------------------------

    private void ShowVideoContextMenu(Point point, MixClip clip, MixLane lane)
    {
        var menu = new ContextMenuStrip { ShowItemToolTips = true };

        void Item(string text, Action action, bool enabled = true, string? toolTip = null)
        {
            var item = new ToolStripMenuItem(text) { Enabled = enabled };
            if (toolTip != null)
            {
                item.ToolTipText = toolTip;
            }

            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        Item("Cut here", () => SplitVideoClipAt(clip, lane, XTime(point.X)),
            toolTip: "Split the video segment at the right-click position.");
        menu.Items.Add(new ToolStripSeparator());
        Item("Mute video audio", () => ToggleVideoAudioMute(clip), clip.LinkedClipId != null,
            "Mute the audio clip linked to this video segment.");
        Item("Remove video audio", () => RemoveVideoAudio(clip), clip.LinkedClipId != null,
            "Delete the audio clip linked to this video segment (video continues without audio).");
        Item("Extract audio to new track", () => ExtractVideoAudioToTrack(clip), clip.LinkedClipId == null,
            "Extract the video's audio track as a separate audio segment on a new lane.");
        menu.Items.Add(new ToolStripSeparator());
        Item("Undo (Ctrl+Z)", UndoVideo, _videoUndo.Count > 0);
        Item("Redo (Ctrl+Y)", RedoVideo, _videoRedo.Count > 0);
        Item("Delete video segment", () => DeleteVideoClip(clip));

        menu.Closed += (_, _) =>
        {
            if (!IsDisposed && !Disposing && IsHandleCreated)
            {
                BeginInvoke((Action)menu.Dispose);
            }
            else
            {
                menu.Dispose();
            }
        };
        menu.Show(canvas, point);
    }

    private void SplitVideoClipAt(MixClip clip, MixLane lane, double split)
    {
        if (busy || PlaybackLocksEdits)
        {
            return;
        }

        double originalVideoDuration = clip.SourceDuration;
        double left = (split - clip.Start) * Arrangement.Rate(clip);
        if (left <= 0.001 || left >= clip.SourceDuration - 0.001)
        {
            return;
        }

        string before = CaptureVideoUndoState();

        var copy = CloneClip(clip);
        copy.Id = Guid.NewGuid();
        copy.LinkedClipId = null;
        copy.Start = split;
        copy.SourceOffset += left;
        copy.SourceDuration -= left;
        copy.FadeIn = 0;
        clip.SourceDuration = left;
        clip.FadeOut = 0;
        lane.Clips.Add(copy);
        selectedClip = copy.Id;

        // Also split linked audio if present.
        if (clip.LinkedClipId != null)
        {
            var audioLane = Arrangement.Lanes.FirstOrDefault(l => l.Clips.Any(c => c.Id == clip.LinkedClipId));
            var audioClip = audioLane?.Clips.FirstOrDefault(c => c.Id == clip.LinkedClipId);
            if (audioLane != null && audioClip != null)
            {
                double audioLeft = left * audioClip.SourceDuration / originalVideoDuration;
                if (audioLeft > 0.001 && audioLeft < audioClip.SourceDuration - 0.001)
                {
                    var audioCopy = CloneClip(audioClip);
                    audioCopy.Id = Guid.NewGuid();
                    audioCopy.LinkedClipId = copy.Id;
                    audioCopy.Start = split;
                    audioCopy.SourceOffset += audioLeft;
                    audioCopy.SourceDuration -= audioLeft;
                    audioCopy.FadeIn = 0;
                    audioClip.SourceDuration = audioLeft;
                    audioClip.FadeOut = 0;
                    copy.LinkedClipId = audioCopy.Id;
                    audioLane.Clips.Add(audioCopy);
                }
            }
        }

        CommitVideoEdit(before);
        ChangedVideo();
        status.Text = "Video segment split.";
    }

    private void ToggleVideoAudioMute(MixClip clip)
    {
        if (clip.LinkedClipId == null)
        {
            return;
        }

        var lane = Arrangement.Lanes.FirstOrDefault(l => l.Clips.Any(c => c.Id == clip.LinkedClipId));
        var audioClip = lane?.Clips.FirstOrDefault(c => c.Id == clip.LinkedClipId);
        if (audioClip == null)
        {
            return;
        }

        string before = CaptureVideoUndoState();
        audioClip.Gain = audioClip.Gain > 0 ? 0 : 1;
        CommitVideoEdit(before);
        ChangedVideo();
        status.Text = audioClip.Gain > 0 ? "Video audio unmuted." : "Video audio muted.";
    }

    private void RemoveVideoAudio(MixClip clip)
    {
        if (clip.LinkedClipId == null)
        {
            return;
        }

        var lane = Arrangement.Lanes.FirstOrDefault(l => l.Clips.Any(c => c.Id == clip.LinkedClipId));
        if (lane == null)
        {
            return;
        }

        string before = CaptureVideoUndoState();
        lane.Clips.RemoveAll(c => c.Id == clip.LinkedClipId);
        clip.LinkedClipId = null;
        CommitVideoEdit(before);
        ChangedVideo();
        status.Text = "Video audio removed.";
    }

    private void ExtractVideoAudioToTrack(MixClip clip)
    {
        if (busy || PlaybackLocksEdits || clip == null)
        {
            return;
        }

        if (!Arrangement.Sources.TryGetValue(clip.SourceId, out var source) || !source.IsVideo)
        {
            return;
        }

        string? videoPath = source.VideoFilePath;
        if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
        {
            return;
        }

        _ = RunAsync(async token =>
        {
            status.Text = "Extracting audio from video...";
            string? ffmpegPath = await EnsureFfmpegAvailableAsync(token);
            if (ffmpegPath == null)
            {
                return;
            }

            using var video = new VideoObj(videoPath, ffmpegPath);
            await video.ProbeAsync(token);
            if (!video.HasAudio)
            {
                await ErrorAsync(new InvalidOperationException("The selected video does not contain an extractable audio track."));
                return;
            }
            EnsureVideoAudioFitsMemory(video.Duration.TotalSeconds);
            string? tempWav = await ExtractAudioWithProgressAsync(video, token);
            if (string.IsNullOrEmpty(tempWav) || !File.Exists(tempWav))
            {
                await ErrorAsync(new InvalidOperationException("The selected video does not contain an extractable audio track."));
                return;
            }

            try
            {
                using var audioObj = new AudioObj(tempWav, load: true);
                if (audioObj.IsDisposed || audioObj.Data.Length == 0)
                {
                    throw new InvalidDataException("The video's audio track could not be loaded.");
                }

                if (!Arrangement.Lanes.SelectMany(value => value.Clips).Any(value => value.Id == clip.Id))
                {
                    return;
                }

                string before = CaptureVideoUndoState();

                var audioSource = new MixSource
                {
                    Name = $"{video.Name} (extracted audio)",
                    Data = (float[])audioObj.Data.Clone(),
                    SampleRate = audioObj.SampleRate,
                    Channels = audioObj.Channels,
                    Bpm = audioObj.Bpm > 0 ? audioObj.Bpm : audioObj.ScannedBpm
                };
                double sourceRatio = audioSource.Duration / source.VideoDuration;
                double audioOffset = Math.Clamp(clip.SourceOffset * sourceRatio, 0, audioSource.Duration);
                double audioDuration = Math.Clamp(clip.SourceDuration * sourceRatio, 0, audioSource.Duration - audioOffset);
                if (audioDuration <= 0.001)
                {
                    throw new InvalidDataException("The selected video segment does not overlap its audio track.");
                }

                Arrangement.Sources.Add(audioSource.Id, audioSource);

                var audioLane = new MixLane { Name = $"{video.Name} Audio", Gain = 1 };
                int videoLaneIndex = Arrangement.Lanes.FindIndex(lane => lane.IsVideo && lane.Clips.Contains(clip));
                Arrangement.Lanes.Insert(videoLaneIndex + 1, audioLane);

                var audioClip = Arrangement.AddClip(audioLane, audioSource, clip.Start, audioOffset, audioDuration);
                audioClip.IsVideo = false;
                audioClip.Name = $"{video.Name} (extracted audio)";
                audioClip.LinkedClipId = clip.Id;
                clip.LinkedClipId = audioClip.Id;

                CommitVideoEdit(before);
                ChangedVideo();
                status.Text = "Audio extracted to new track.";
            }
            finally
            {
                if (File.Exists(tempWav))
                {
                    File.Delete(tempWav);
                }
            }
        });
    }

    private void DeleteVideoClip(MixClip clip)
    {
        if (clip == null)
        {
            return;
        }

        DeleteVideoClips([clip]);
        status.Text = "Video segment deleted.";
    }

    private void DeleteVideoClips(IEnumerable<MixClip> clips)
    {
        MixClip[] victims = clips.DistinctBy(clip => clip.Id)
            .Where(clip => _videoLane?.Clips.Contains(clip) == true).ToArray();
        if (victims.Length == 0)
        {
            return;
        }

        string before = CaptureVideoUndoState();
        HashSet<Guid> audioClipIds = victims.Where(clip => clip.LinkedClipId.HasValue)
            .Select(clip => clip.LinkedClipId!.Value).ToHashSet();
        foreach (MixLane lane in Arrangement.Lanes)
        {
            lane.Clips.RemoveAll(clip => audioClipIds.Contains(clip.Id));
        }

        foreach (MixClip victim in victims)
        {
            _videoLane!.Clips.Remove(victim);
            selectedClips.Remove(victim.Id);
            fullSelectedClips.Remove(victim.Id);
            if (selectedClip == victim.Id)
            {
                selectedClip = null;
            }
        }
        CommitVideoEdit(before);
        ChangedVideo();
    }

    // ------------------------------------------------------------------
    // Video clipboard (Ctrl+C/V/X)
    // ------------------------------------------------------------------

    private void CopyVideoClip()
    {
        if (Clip is not { } clip || !clip.IsVideo)
        {
            return;
        }

        var source = Arrangement.Sources[clip.SourceId];
        var data = new
        {
            clip.Id,
            clip.SourceId,
            clip.Name,
            clip.Start,
            clip.SourceOffset,
            clip.SourceDuration,
            clip.Gain,
            clip.FadeIn,
            clip.FadeOut,
            clip.TempoMode,
            clip.IsVideo,
            clip.LinkedClipId,
            SourceIsVideo = source.IsVideo,
            source.VideoFilePath,
            source.VideoDuration,
            source.VideoWidth,
            source.VideoHeight,
            source.VideoFrameRate
        };
        string json = JsonSerializer.Serialize(data);
        try { Clipboard.SetText(json); } catch { }
        status.Text = "Video segment copied to clipboard.";
    }

    private void PasteVideoClip()
    {
        if (busy || PlaybackLocksEdits)
        {
            return;
        }

        string json;
        try { json = Clipboard.GetText(); } catch { return; }
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            var data = JsonSerializer.Deserialize<VideoClipboardData>(json)
                ?? throw new InvalidDataException("The clipboard does not contain a video segment.");
            if (!data.IsVideo || string.IsNullOrWhiteSpace(data.VideoFilePath) || !File.Exists(data.VideoFilePath))
            {
                throw new InvalidDataException("The copied video file is no longer available.");
            }

            string before = CaptureVideoUndoState();
            MixLane lane = _videoLane ?? new MixLane { Name = "Video #1", IsVideo = true, Gain = 1 };
            if (_videoLane == null) { Arrangement.Lanes.Add(lane); _videoLane = lane; }

            // Re-register the video source.
            var source = new MixSource
            {
                Name = Path.GetFileNameWithoutExtension(data.VideoFilePath),
                IsVideo = true,
                VideoFilePath = data.VideoFilePath,
                VideoDuration = data.VideoDuration,
                VideoWidth = data.VideoWidth,
                VideoHeight = data.VideoHeight,
                VideoFrameRate = data.VideoFrameRate,
                SampleRate = 44100,
                Channels = 2,
                Bpm = 0,
                Data = []
            };
            Arrangement.Sources.Add(source.Id, source);

            double time = FindVideoDropStart(lane, Arrangement.SnapTime(XTime(lastPointer.X), ModifierKeys.HasFlag(Keys.Shift)),
                Math.Clamp(data.SourceDuration, 0.001, source.VideoDuration));
            var clip = Arrangement.AddClip(lane, source, time, 0, data.SourceDuration);
            clip.Name = data.Name;
            clip.SourceOffset = data.SourceOffset;
            clip.SourceDuration = data.SourceDuration;
            clip.Gain = data.Gain;
            clip.FadeIn = data.FadeIn;
            clip.FadeOut = data.FadeOut;
            clip.TempoMode = data.TempoMode;
            clip.IsVideo = true;
            clip.LinkedClipId = null;
            selectedClip = clip.Id;
            selectedLane = lane.Id;

            CommitVideoEdit(before);
            ChangedVideo();
            status.Text = "Video segment pasted.";
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or IOException or InvalidOperationException)
        {
            _ = ErrorAsync(error);
        }
    }

    private sealed class VideoClipboardData
    {
        public Guid Id { get; set; }
        public Guid SourceId { get; set; }
        public string Name { get; set; } = "";
        public double Start { get; set; }
        public double SourceOffset { get; set; }
        public double SourceDuration { get; set; }
        public double Gain { get; set; }
        public double FadeIn { get; set; }
        public double FadeOut { get; set; }
        public ClipTempoMode TempoMode { get; set; }
        public bool IsVideo { get; set; }
        public Guid? LinkedClipId { get; set; }
        public bool SourceIsVideo { get; set; }
        public string? VideoFilePath { get; set; }
        public double VideoDuration { get; set; }
        public int VideoWidth { get; set; }
        public int VideoHeight { get; set; }
        public double VideoFrameRate { get; set; }
    }

    // ------------------------------------------------------------------
    // Render Video menu (sub-items under Render ... button)
    // ------------------------------------------------------------------

    private string? _renderResolution = "keep original";
    private double _renderFramerate = 30;
    private bool _renderFramerateCustomized;
    private string _renderFormat = "mp4";

    private void ShowRenderMenuWithVideo(Control button, Point point)
    {
        var menu = new ContextMenuStrip { ShowItemToolTips = true };
        menu.Closing += (_, e) => { if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked) { e.Cancel = true; } };
        var mixItem = new ToolStripMenuItem("Mix (one track)");
        mixItem.Click += async (_, _) => { menu.Close(); await ExportAsync(); };
        menu.Items.Add(mixItem);
        var tracksItem = new ToolStripMenuItem("Separate (per track)");
        tracksItem.Click += async (_, _) => { menu.Close(); await ExportTracksAsync(null); };
        menu.Items.Add(tracksItem);

        MixClip[] videoClips = _videoLane?.Clips.OrderBy(clip => clip.Start).ToArray() ?? [];
        double maximumFrameRate = videoClips.Select(clip => Arrangement.Sources.TryGetValue(clip.SourceId, out MixSource? source)
                ? source.VideoFrameRate : 0)
            .Where(rate => double.IsFinite(rate) && rate > 0).DefaultIfEmpty(30).Max();
        if (!_renderFramerateCustomized)
        {
            _renderFramerate = Math.Clamp(maximumFrameRate, 1, 120);
        }

        var resolutionItem = new ToolStripMenuItem("Resolution");
        var resolutionCombo = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        resolutionCombo.Items.AddRange(new object[]
        {
            "keep original",
            "1920x1080 (1080p)",
            "1280x720 (720p)",
            "854x480 (480p)",
            "640x360 (360p)"
        });
        resolutionCombo.SelectedItem = _renderResolution;
        resolutionCombo.SelectedIndexChanged += (_, _) =>
        {
            if (resolutionCombo.SelectedItem is string sel)
            {
                _renderResolution = sel;
            }
        };
        resolutionItem.DropDownItems.Add(resolutionCombo);
        menu.Items.Add(resolutionItem);

        var framerateItem = new ToolStripMenuItem("Framerate");
        var framerateControl = new NumericUpDown
        {
            Minimum = 1, Maximum = 120, DecimalPlaces = 2, Increment = 0.1m,
            Value = (decimal)_renderFramerate, Width = 120
        };
        framerateControl.ValueChanged += (_, _) =>
        {
            _renderFramerate = (double)framerateControl.Value;
            _renderFramerateCustomized = true;
        };
        framerateItem.DropDownItems.Add(new ToolStripControlHost(framerateControl) { Width = 140 });
        menu.Items.Add(framerateItem);

        var formatItem = new ToolStripMenuItem("Format");
        var formatCombo = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        formatCombo.Items.AddRange(new object[] { "mp4", "avi" });
        formatCombo.SelectedItem = _renderFormat;
        formatCombo.SelectedIndexChanged += (_, _) =>
        {
            if (formatCombo.SelectedItem is string sel)
            {
                _renderFormat = sel;
            }
        };
        formatItem.DropDownItems.Add(formatCombo);
        menu.Items.Add(formatItem);

        menu.Items.Add(new ToolStripSeparator());

        bool hasVideo = videoClips.Length > 0;
        var renderVideoItem = new ToolStripMenuItem("Render Video")
        {
            Enabled = hasVideo,
            ToolTipText = "Render the video track together with its audio tracks to a video file. Requires ffmpeg."
        };
        renderVideoItem.Click += async (_, _) => { menu.Close(); await RenderVideoAsync(); };
        menu.Items.Add(renderVideoItem);
        menu.Closed += (_, _) =>
        {
            if (!IsDisposed && !Disposing && IsHandleCreated)
            {
                BeginInvoke((Action)menu.Dispose);
            }
            else
            {
                menu.Dispose();
            }
        };

        menu.Show(button, point);
    }

    private async Task RenderVideoAsync()
    {
        if (busy || _videoLane == null || _videoLane.Clips.Count == 0)
        {
            return;
        }

        _ = RunAsync(async token =>
        {
            MixClip[] clips = _videoLane.Clips.OrderBy(clip => clip.Start).ToArray();
            Arrangement.Validate();
            double renderStart = clips[0].Start;
            double renderEnd = clips.Max(clip => clip.Start + Arrangement.ClipDuration(clip));
            double renderDuration = renderEnd - renderStart;
            if (!double.IsFinite(renderDuration) || renderDuration <= 0)
            {
                throw new InvalidDataException("The video track has no renderable duration.");
            }

            string extension = _renderFormat == "avi" ? ".avi" : ".mp4";
            string baseName = string.Concat(Arrangement.Name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
            using var saveDialog = new SaveFileDialog
            {
                Filter = _renderFormat == "avi" ? "AVI Video (*.avi)|*.avi" : "MP4 Video (*.mp4)|*.mp4",
                FileName = $"{baseName}_video{extension}",
                DefaultExt = extension,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                AddExtension = true
            };
            if (await this.ShowMixFileDialogAsync(saveDialog, "Save rendered video") != DialogResult.OK)
            {
                return;
            }

            string outputPath = saveDialog.FileName;
            string? ffmpegPath = await EnsureFfmpegAvailableAsync(token);
            if (ffmpegPath == null)
            {
                return;
            }

            int width, height;
            if (_renderResolution == "keep original")
            {
                MixSource firstSource = Arrangement.Sources[clips[0].SourceId];
                width = firstSource.VideoWidth;
                height = firstSource.VideoHeight;
            }
            else
            {
                Match resolution = Regex.Match(_renderResolution ?? "", @"^(\d+)x(\d+)");
                if (!resolution.Success || !int.TryParse(resolution.Groups[1].Value, out width)
                    || !int.TryParse(resolution.Groups[2].Value, out height))
                {
                    throw new InvalidOperationException("Select a valid output resolution.");
                }
            }
            width &= ~1;
            height &= ~1;
            if (width < 2 || height < 2 || width > 7680 || height > 4320)
            {
                throw new InvalidDataException("The selected video resolution is outside the supported render limits.");
            }

            double frameRate = Math.Clamp(_renderFramerate, 1, 120);
            string frameRateText = frameRate.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            string durationText = renderDuration.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
            string outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath))!;
            Directory.CreateDirectory(outputDirectory);
            string partialPath = Path.Combine(outputDirectory,
                $".{Path.GetFileNameWithoutExtension(outputPath)}.{Guid.NewGuid():N}.partial{extension}");
            string? audioPath = null;
            using var renderCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            using var progressDialog = new ProgressDialog("Rendering video...", progress: new Progress<double>(),
                ct: renderCancellation.Token, cancellationSource: renderCancellation);
            progressDialog.Show(this);

            try
            {
                string? audioError = await RenderArrangementAudioForVideoAsync(renderStart, renderEnd,
                    path => audioPath = path, fraction => progressDialog.Report(fraction * 0.15), token);
                if (audioError != null)
                {
                    throw new InvalidOperationException(audioError);
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardErrorEncoding = Encoding.UTF8,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                AddVideoFilterInputs(startInfo, clips, renderStart, renderEnd, width, height, frameRateText,
                    out string filterComplex, out int inputCount);
                int audioInputIndex = -1;
                if (audioPath != null)
                {
                    audioInputIndex = inputCount;
                    AddVideoArguments(startInfo, "-i", audioPath);
                    filterComplex += $";[{audioInputIndex}:a:0]aresample=44100,apad,atrim=duration={durationText},asetpts=PTS-STARTPTS[aout]";
                }
                AddVideoArguments(startInfo, "-filter_complex_threads", Math.Max(1, Environment.ProcessorCount - 2).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "-filter_complex", filterComplex, "-map", "[vout]");
                if (audioInputIndex >= 0)
                {
                    AddVideoArguments(startInfo, "-map", "[aout]");
                }

                AddVideoArguments(startInfo, "-r", frameRateText, "-t", durationText);
                if (_renderFormat == "avi")
                {
                    AddVideoArguments(startInfo, "-c:v", "mpeg4", "-q:v", "3", "-c:a", "mp3", "-b:a", "192k");
                }
                else
                {
                    AddVideoArguments(startInfo, "-c:v", "libx264", "-preset", "fast", "-crf", "23", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k");
                }

                AddVideoArguments(startInfo, "-threads", Math.Max(1, Environment.ProcessorCount - 2).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "-progress", "pipe:1", "-nostats", "-y", partialPath);

                progressDialog.SetStatus("Encoding video and mixed audio...");
                using var process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Unable to start FFmpeg video rendering.");
                Task<string> errorTask = process.StandardError.ReadToEndAsync();
                try
                {
                    string? line;
                    while ((line = await process.StandardOutput.ReadLineAsync(renderCancellation.Token).ConfigureAwait(false)) != null)
                    {
                        if (!line.StartsWith("out_time_ms=", StringComparison.Ordinal)
                            || !long.TryParse(line.AsSpan("out_time_ms=".Length), out long microseconds))
                        {
                            continue;
                        }

                        double fraction = Math.Clamp(microseconds / 1_000_000d / renderDuration, 0, 1);
                        progressDialog.Report(0.15 + fraction * 0.85);
                    }
                    await process.WaitForExitAsync(renderCancellation.Token).ConfigureAwait(false);
                }
                catch { TryKillVideoProcess(process); throw; }

                string diagnostics = await errorTask.ConfigureAwait(false);
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"FFmpeg video rendering failed (exit code {process.ExitCode}):{Environment.NewLine}{diagnostics.Trim()}");
                }

                if (!File.Exists(partialPath) || new FileInfo(partialPath).Length == 0)
                {
                    throw new InvalidDataException("FFmpeg completed without producing a video file.");
                }

                renderCancellation.Token.ThrowIfCancellationRequested();
                File.Move(partialPath, outputPath, overwrite: true);
                progressDialog.Complete();
                status.Text = $"Video rendered: {Path.GetFileName(outputPath)}.";
            }
            catch (OperationCanceledException)
            {
                progressDialog.Close();
                status.Text = "Video render cancelled.";
            }
            catch
            {
                progressDialog.Close();
                throw;
            }
            finally
            {
                TryDeleteVideoTemp(partialPath);
                if (audioPath != null)
                {
                    TryDeleteVideoTemp(audioPath);
                }
            }
        });
    }

    private void AddVideoFilterInputs(ProcessStartInfo startInfo, IReadOnlyList<MixClip> clips,
        double renderStart, double renderEnd, int width, int height, string frameRate, out string filterComplex, out int inputCount)
    {
        var filters = new List<string>();
        var labels = new List<string>();
        int nextInput = 0;
        int segmentIndex = 0;
        double cursor = renderStart;

        foreach (MixClip clip in clips)
        {
            double clipEnd = clip.Start + Arrangement.ClipDuration(clip);
            if (clip.Start > cursor + 0.000001)
            {
                double gap = clip.Start - cursor;
                string gapText = gap.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
                AddVideoArguments(startInfo, "-f", "lavfi", "-t", gapText, "-i", $"color=c=black:s={width}x{height}:r={frameRate}");
                string label = $"v{segmentIndex++}";
                filters.Add($"[{nextInput}:v:0]trim=duration={gapText},setpts=PTS-STARTPTS,format=yuv420p[{label}]");
                labels.Add($"[{label}]");
                nextInput++;
            }

            if (!Arrangement.Sources.TryGetValue(clip.SourceId, out MixSource? source)
                || !source.IsVideo || string.IsNullOrWhiteSpace(source.VideoFilePath) || !File.Exists(source.VideoFilePath))
            {
                throw new FileNotFoundException("A video segment source is unavailable.", source?.VideoFilePath);
            }

            AddVideoArguments(startInfo, "-i", source.VideoFilePath);
            string videoDuration = clip.SourceDuration.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
            string offset = clip.SourceOffset.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
            string rate = Arrangement.Rate(clip).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
            string videoLabel = $"v{segmentIndex++}";
            filters.Add($"[{nextInput}:v:0]trim=start={offset}:duration={videoDuration},setpts=(PTS-STARTPTS)/{rate},"
                + $"scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:black,"
                + $"fps={frameRate},setsar=1,format=yuv420p[{videoLabel}]");
            labels.Add($"[{videoLabel}]");
            nextInput++;
            cursor = clipEnd;
        }

        if (cursor < renderEnd - 0.000001)
        {
            double gap = renderEnd - cursor;
            string gapText = gap.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
            AddVideoArguments(startInfo, "-f", "lavfi", "-t", gapText, "-i", $"color=c=black:s={width}x{height}:r={frameRate}");
            string label = $"v{segmentIndex++}";
            filters.Add($"[{nextInput}:v:0]trim=duration={gapText},setpts=PTS-STARTPTS,format=yuv420p[{label}]");
            labels.Add($"[{label}]");
            nextInput++;
        }

        filters.Add($"{string.Join(string.Empty, labels)}concat=n={labels.Count}:v=1:a=0[vout]");
        filterComplex = string.Join(';', filters);
        inputCount = nextInput;
    }

    private static void AddVideoArguments(ProcessStartInfo startInfo, params string[] arguments)
    {
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
    }

    private async Task<string?> RenderArrangementAudioForVideoAsync(double start, double end,
        Action<string> setOutputPath, Action<double> report, CancellationToken cancellationToken)
    {
        var snapshot = new MixArrangement();
        snapshot.Restore(Arrangement.Capture());
        snapshot.Lanes = Arrangement.Lanes.Select(lane =>
        {
            MixLane copy = JsonSerializer.Deserialize<MixLane>(JsonSerializer.Serialize(lane))!;
            copy.Clips = lane.Clips.Select(clip => (Original: clip, Copy: CloneClip(clip)))
                .Where(part => part.Original.Start + Arrangement.ClipDuration(part.Original) > start
                    && part.Original.Start < end)
                .Select(part =>
                {
                    double visibleStart = Math.Max(start, part.Original.Start);
                    double visibleEnd = Math.Min(end, part.Original.Start + Arrangement.ClipDuration(part.Original));
                    double factor = Arrangement.Rate(part.Original);
                    part.Copy.Start = visibleStart - start;
                    part.Copy.SourceOffset += (visibleStart - part.Original.Start) * factor;
                    part.Copy.SourceDuration = (visibleEnd - visibleStart) * factor;
                    if (visibleStart > part.Original.Start)
                    {
                        part.Copy.FadeIn = 0;
                    }

                    if (visibleEnd < part.Original.Start + Arrangement.ClipDuration(part.Original))
                    {
                        part.Copy.FadeOut = 0;
                    }

                    return part.Copy;
                }).ToList();
            return copy;
        }).ToList();
        foreach (var source in Arrangement.Sources)
        {
            snapshot.Sources.Add(source.Key, source.Value);
        }

        if (!snapshot.Lanes.Any(lane => lane.Clips.Count > 0))
        {
            return null;
        }

        using AudioObj rendered = await MixRenderer.RenderAsync(snapshot, cancellationToken,
            new Progress<double>(fraction => report(fraction))).ConfigureAwait(false);
        int frames = checked((int)Math.Ceiling((end - start) * rendered.SampleRate));
        int sampleCount = checked(frames * rendered.Channels);
        float[] exactLength = new float[sampleCount];
        Array.Copy(rendered.Data, exactLength, Math.Min(rendered.Data.Length, exactLength.Length));
        using var normalized = new AudioObj
        {
            Name = $"{Arrangement.Name} Video Audio",
            Data = exactLength,
            SampleRate = rendered.SampleRate,
            Channels = rendered.Channels,
            Length = exactLength.Length,
            Duration = TimeSpan.FromSeconds(frames / (double)rendered.SampleRate),
            BitDepth = 32
        };
        string path = Path.Combine(Path.GetTempPath(), $"ModularAudienceVideoMix_{Guid.NewGuid():N}.wav");
        setOutputPath(path);
        await WriteFloatWavAsync(path, normalized, cancellationToken).ConfigureAwait(false);
        return path;
    }

    private static Task WriteFloatWavAsync(string path, AudioObj audio, CancellationToken cancellationToken) => Task.Run(() =>
    {
        using var writer = new NAudio.Wave.WaveFileWriter(path,
            NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(audio.SampleRate, audio.Channels));
        const int ChunkSamples = 65536;
        for (int offset = 0; offset < audio.Data.Length; offset += ChunkSamples)
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.WriteSamples(audio.Data, offset, Math.Min(ChunkSamples, audio.Data.Length - offset));
        }
    }, cancellationToken);

    private static void TryKillVideoProcess(Process process)
    {
        try { if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException) { }
    }

    private static void TryDeleteVideoTemp(string path)
    {
        try { if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { LogManager.Log($"Unable to remove temporary video render file '{path}'.", error); }
    }
}
