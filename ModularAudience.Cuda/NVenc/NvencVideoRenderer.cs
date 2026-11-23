#nullable enable
using ModularAudience.Audio;
using ModularAudience.Audio.Processing;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ModularAudience.Cuda
{
    public static partial class NvencVideoRenderer
    {
        public sealed record NvencOptions(
            string VideoCodec,            // z.B. "h264_nvenc", "hevc_nvenc", "libx264", "libx265", "libsvtav1"
            string Preset,                // NVENC: p1..p7, x264/x265: ultrafast..veryslow, svt-av1: 0..13 (string passt)
            int? VideoBitrateKbps,         // null => Qualitätsmodus (CQ/CRF) oder ffmpeg default
            int? MaxBitrateKbps,           // optional
            int? BufferSizeKbps,           // optional
            int? ConstantQuality,          // NVENC CQ (h264 0..51, hevc 0..63)
            int? Crf,                      // CPU CRF (x264/x265/av1 typ. 0..51, kleiner = besser)
            string? Profile,               // z.B. "high" (x264) / "main" (x265) etc.
            bool FastStart                 // -movflags +faststart
)
        {
            public static NvencOptions H264Default => new(
                VideoCodec: "h264_nvenc",
                Preset: "p2",         // p1 oder p2 für Speed! p5 ist zu langsam.
                VideoBitrateKbps: null,
                MaxBitrateKbps: null,
                BufferSizeKbps: null,
                ConstantQuality: 23,  // 23 ist guter Standard, 20 ist overkill für Visualizer
                Crf: null,
                Profile: "main",      // "main" ist sicherer und schneller als "high"
                FastStart: true
);

            public static NvencOptions HevcDefault => new(
                VideoCodec: "hevc_nvenc",
                Preset: "p2",         // auch hier Speed
                VideoBitrateKbps: null,
                MaxBitrateKbps: null,
                BufferSizeKbps: null,
                ConstantQuality: 25,  // HEVC ist effizienter, höherer CQ ist ok
                Crf: null,
                Profile: "main",      // WICHTIG: HEVC hasst oft "high". "main" ist Standard.
                FastStart: true
            );
        }


        public static readonly string[] CommonResolutions =
        [
            "3840x2160",
            "2560x1440",
            "1920x1080",
            "1280x720",
            "854x480",
            "640x360",
            "426x240",
            "256x144"
        ];

        private static string ResolveExternalToolPath(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                return toolName;
            }

            if (Path.IsPathRooted(toolName)
                || toolName.Contains(Path.DirectorySeparatorChar)
                || toolName.Contains(Path.AltDirectorySeparatorChar))
            {
                return toolName;
            }

            string fileName = Path.HasExtension(toolName) ? toolName : toolName + ".exe";

            foreach (string dir in EnumerateExternalToolSearchDirectories())
            {
                try
                {
                    string candidate = Path.Combine(dir, fileName);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch
                {
                }
            }

            return toolName;
        }

        private static IEnumerable<string> EnumerateExternalToolSearchDirectories()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            static IEnumerable<string> Expand(string? baseDir)
            {
                if (string.IsNullOrWhiteSpace(baseDir))
                {
                    yield break;
                }

                yield return baseDir;
                yield return Path.Combine(baseDir, "bin");
                yield return Path.Combine(baseDir, "ffmpeg");
                yield return Path.Combine(baseDir, "ffmpeg", "bin");
            }

            string[] baseDirs =
            [
                AppContext.BaseDirectory,
                Directory.GetCurrentDirectory(),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetEnvironmentVariable("ProgramW6432") ?? string.Empty,
                @"C:\Programme"
            ];

            foreach (string baseDir in baseDirs)
            {
                foreach (string candidate in Expand(baseDir))
                {
                    if (seen.Add(candidate))
                    {
                        yield return candidate;
                    }
                }
            }
        }

        // ------------------------------------------------------------
        // Public: Image[] + optional Audio-File
        // ------------------------------------------------------------

        public static Task<string?> NvencRenderVideoAsync(
            Image[] frames,
            int width = 0,
            int height = 0,
            float frameRate = 20.0f,
            string? audioFilePath = null,
            string? outputFilePath = null,
            IProgress<double>? progress = null,
            CancellationToken? ct = null)
            => NvencRenderVideoAsync(
                frames: frames,
                width: width,
                height: height,
                frameRate: frameRate,
                audioFilePath: audioFilePath,
                outputFilePath: outputFilePath,
                options: NvencOptions.H264Default,
                progress: progress,
                ct: ct);

        public static async Task<string?> NvencRenderVideoAsync(
            Image[] frames,
            int width,
            int height,
            float frameRate,
            string? audioFilePath,
            string? outputFilePath,
            NvencOptions options,
            IProgress<double>? progress = null,
            CancellationToken? ct = null)
        {
            if (frames is null || frames.Length == 0)
            {
                throw new ArgumentException("frames darf nicht null/leer sein.", nameof(frames));
            }

            var token = ct ?? CancellationToken.None;

            // 1) Dimensionen bestimmen
            if (width <= 0 || height <= 0)
            {
                GetMaxDimensions(frames, out var maxW, out var maxH);
                if (width <= 0)
                {
                    width = maxW;
                }

                if (height <= 0)
                {
                    height = maxH;
                }
            }

            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException("width/height müssen > 0 sein (oder aus Frames ableitbar).");
            }

            // 2) Audio prüfen + ggf. frameRate aus Audiolänge setzen
            TimeSpan? audioDuration = null;
            if (!string.IsNullOrWhiteSpace(audioFilePath))
            {
                audioFilePath = Path.GetFullPath(audioFilePath);
                if (!File.Exists(audioFilePath))
                {
                    throw new FileNotFoundException("audioFilePath existiert nicht.", audioFilePath);
                }

                try
                {
                    audioDuration = null;
                }
                catch
                {
                    audioDuration = null;
                }

                if (frameRate <= 0 && audioDuration.HasValue && audioDuration.Value.TotalSeconds > 0.001)
                {
                    frameRate = (float) (frames.Length / audioDuration.Value.TotalSeconds);
                    if (frameRate < 1e-3f)
                    {
                        frameRate = 20.0f;
                    }
                }
            }

            if (frameRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(frameRate),
                    "frameRate muss > 0 sein, außer Audio-Dauer konnte ermittelt werden.");
            }

            // 3) Output-Pfad bestimmen
            var resolvedOutputPath = ResolveOutputPath(outputFilePath, options);
            Directory.CreateDirectory(Path.GetDirectoryName(resolvedOutputPath)!);

            // 4) Gesamtdauer für Progress
            var totalVideoSeconds = frames.Length / (double) frameRate;
            var totalSeconds = audioDuration.HasValue && audioDuration.Value.TotalSeconds > 0.001
                ? Math.Max(totalVideoSeconds, audioDuration.Value.TotalSeconds)
                : totalVideoSeconds;

            progress?.Report(0.0);

            // 5) FFmpeg args (Video stdin raw BGRA)
            var args = BuildFfmpegArgsFast(
                width, height, frameRate,
                audioFilePath,
                resolvedOutputPath,
                options);

            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true, // progress kommt hier rein -> MUSS gelesen werden
                RedirectStandardError = true,  // MUSS gelesen werden
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var stderrSb = new StringBuilder();

            try
            {
                if (!proc.Start())
                {
                    throw new InvalidOperationException("FFmpeg konnte nicht gestartet werden.");
                }

                // stderr drain (sonst deadlock möglich)
                var stderrTask = Task.Run(async () =>
                {
                    try
                    {
                        string? line;
                        while ((line = await proc.StandardError.ReadLineAsync().ConfigureAwait(false)) != null)
                        {
                            stderrSb.AppendLine(line);
                        }
                    }
                    catch { }
                }, token);

                // progress drain (sonst deadlock wegen -progress pipe:1)
                var progressTask = Task.Run(async () =>
                {
                    try
                    {
                        string? line;
                        double lastReported = 0.0;

                        while ((line = await proc.StandardOutput.ReadLineAsync().ConfigureAwait(false)) != null)
                        {
                            if (line.StartsWith("out_time_ms=", StringComparison.OrdinalIgnoreCase))
                            {
                                var val = line["out_time_ms=".Length..].Trim();
                                if (long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var outMs))
                                {
                                    var sec = outMs / 1_000_000.0;
                                    var p = totalSeconds > 0 ? sec / totalSeconds : 0.0;
                                    p = Math.Clamp(p, 0.0, 1.0);

                                    if (p >= lastReported + 0.002 || p >= 1.0)
                                    {
                                        lastReported = p;
                                        progress?.Report(p);
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }, token);

                await WriteFramesToStdinAsync(proc, frames, width, height, token).ConfigureAwait(false);

                try { proc.StandardInput.Close(); } catch { }

                await proc.WaitForExitAsync(token).ConfigureAwait(false);

                try { await Task.WhenAll(stderrTask, progressTask).ConfigureAwait(false); } catch { }

                if (proc.ExitCode != 0)
                {
                    SafeDelete(resolvedOutputPath);
                    throw new InvalidOperationException($"FFmpeg exit code {proc.ExitCode}. Details:\n{stderrSb}");
                }

                progress?.Report(1.0);
                return resolvedOutputPath;
            }
            catch (OperationCanceledException)
            {
                TryKill(proc);
                SafeDelete(resolvedOutputPath);
                throw;
            }
            catch
            {
                TryKill(proc);
                SafeDelete(resolvedOutputPath);
                throw;
            }
        }

        // ------------------------------------------------------------
        // Public: Image[] + AudioObj (RAM -> NamedPipe raw PCM)
        // ------------------------------------------------------------

        public static Task<string?> NvencRenderVideoAsync(
            Image[] frames,
            int width,
            int height,
            float frameRate,
            AudioObj audio,
            string? outputFilePath = null,
            NvencOptions? options = null,
            IProgress<double>? progress = null,
            CancellationToken? ct = null)
        {
            if (audio is null)
            {
                throw new ArgumentNullException(nameof(audio));
            }

            if (audio.Data is null)
            {
                throw new ArgumentException("audio.Data ist null.", nameof(audio));
            }

            return NvencRenderVideoWithRawAudioAsync(
                frames,
                width,
                height,
                frameRate,
                audio.Data,
                audio.SampleRate,
                audio.Channels,
                outputFilePath,
                options ?? NvencOptions.H264Default,
                progress,
                ct);
        }

        public static Task<string?> NvencRenderVideoAsync(
            Image[] frames,
            int width,
            int height,
            float frameRate,
            float[] audioData,
            int audioSampleRate,
            int audioChannels,
            string? outputFilePath = null,
            NvencOptions? options = null,
            IProgress<double>? progress = null,
            CancellationToken? ct = null)
        {
            if (audioData is null)
            {
                throw new ArgumentNullException(nameof(audioData));
            }

            if (audioSampleRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(audioSampleRate));
            }

            if (audioChannels <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(audioChannels));
            }

            return NvencRenderVideoWithRawAudioAsync(
                frames,
                width,
                height,
                frameRate,
                audioData,
                audioSampleRate,
                audioChannels,
                outputFilePath,
                options ?? NvencOptions.H264Default,
                progress,
                ct);
        }

        // ------------------------------------------------------------
        // Public: FramesDir + AudioObj (lazy disk -> stdin + audio pipe)
        // ------------------------------------------------------------

        public static async Task<string?> NvencRenderVideoAsync(
            string framesDir,
            int width,
            int height,
            float frameRate,
            AudioObj audio,
            string searchPattern = "*.png",
            string? outputFilePath = null,
            NvencOptions? options = null,
            IProgress<double>? progress = null,
            CancellationToken? ct = null)
        {
            if (string.IsNullOrWhiteSpace(framesDir))
            {
                throw new ArgumentNullException(nameof(framesDir));
            }

            if (!Directory.Exists(framesDir))
            {
                throw new DirectoryNotFoundException(framesDir);
            }

            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            if (audio is null)
            {
                throw new ArgumentNullException(nameof(audio));
            }

            if (audio.Data is null)
            {
                throw new ArgumentException("audio.Data ist null.", nameof(audio));
            }

            var token = ct ?? CancellationToken.None;

            var files = Directory.EnumerateFiles(framesDir, searchPattern, SearchOption.TopDirectoryOnly)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (files.Length == 0)
            {
                throw new InvalidOperationException($"Keine Frames gefunden in '{framesDir}' (Pattern '{searchPattern}').");
            }

            return await NvencRenderVideoFromFileListAsync(
                files, width, height, frameRate, audio,
                outputFilePath, options ?? NvencOptions.H264Default,
                progress, token).ConfigureAwait(false);
        }

        // ------------------------------------------------------------
        // Public: ChannelReader (Producer→Consumer Pipeline) + AudioObj
        // ------------------------------------------------------------

        public static Task<string?> NvencRenderVideoAsync(
            ChannelReader<AudioVisualizerProcessor.FramePacket> frames,
            int frameCount,
            int width,
            int height,
            float frameRate,
            AudioObj audio,
            string? outputFilePath = null,
            NvencOptions? options = null,
            IProgress<double>? progress = null,
            CancellationToken? ct = null)
        {
            if (frames is null)
            {
                throw new ArgumentNullException(nameof(frames));
            }

            if (audio is null)
            {
                throw new ArgumentNullException(nameof(audio));
            }

            if (audio.Data is null)
            {
                throw new ArgumentException("audio.Data ist null.", nameof(audio));
            }

            if (frameCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(frameCount));
            }

            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            if (frameRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(frameRate));
            }

            return NvencRenderVideoFromChannelAsync(
                frames, frameCount, width, height, frameRate,
                audio,
                outputFilePath,
                options ?? NvencOptions.H264Default,
                progress,
                ct ?? CancellationToken.None);
        }

        // ------------------------------------------------------------
        // Core: Channel -> stdin (ordered) + raw audio pipe
        // ------------------------------------------------------------

        private static async Task<string?> NvencRenderVideoFromChannelAsync(
            ChannelReader<AudioVisualizerProcessor.FramePacket> reader,
            int frameCount,
            int width,
            int height,
            float frameRate,
            AudioObj audio,
            string? outputFilePath,
            NvencOptions options,
            IProgress<double>? progress,
            CancellationToken token)
        {
            string output = ResolveOutputPath(outputFilePath, options);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);

            int expectedLen = checked(width * height * 4);

            float[] sourceAudio = audio.Data;
            int audioSampleRate = audio.SampleRate;
            int audioChannels = audio.Channels;

            double audioSeconds = (sourceAudio.Length > 0 && audioSampleRate > 0 && audioChannels > 0)
                ? (sourceAudio.Length / (double) audioChannels) / audioSampleRate
                : 0.0;

            double totalVideoSeconds = frameCount / (double) frameRate;
            double totalSeconds = audioSeconds > 0.001 ? Math.Max(totalVideoSeconds, audioSeconds) : totalVideoSeconds;

            progress?.Report(0.0);

            string pipeName = "modularaudience_nvenc_audio_" + Guid.NewGuid().ToString("N");
            string pipePath = $@"\\.\pipe\{pipeName}";

            string args = BuildFfmpegArgsWithRawAudioPipeFast(
                width, height, frameRate,
                pipePath, audioSampleRate, audioChannels,
                output,
                options);

            var psi = new ProcessStartInfo
            {
                FileName = ResolveExternalToolPath("ffmpeg"),
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true, // progress pipe:1 -> MUSS gelesen werden!
                RedirectStandardError = true,  // MUSS gelesen werden!
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var stderrSb = new StringBuilder();

            using var audioPipe = new System.IO.Pipes.NamedPipeServerStream(
                pipeName,
                System.IO.Pipes.PipeDirection.Out,
                1,
                System.IO.Pipes.PipeTransmissionMode.Byte,
                System.IO.Pipes.PipeOptions.Asynchronous);

            try
            {
                if (!proc.Start())
                {
                    throw new InvalidOperationException("FFmpeg konnte nicht gestartet werden.");
                }

                var stderrTask = Task.Run(async () =>
                {
                    try
                    {
                        string? line;
                        while ((line = await proc.StandardError.ReadLineAsync().ConfigureAwait(false)) != null)
                        {
                            stderrSb.AppendLine(line);
                        }
                    }
                    catch { }
                }, token);

                var progressTask = Task.Run(async () =>
                {
                    try
                    {
                        string? line;
                        double last = 0.0;
                        while ((line = await proc.StandardOutput.ReadLineAsync().ConfigureAwait(false)) != null)
                        {
                            if (line.StartsWith("out_time_ms=", StringComparison.OrdinalIgnoreCase))
                            {
                                var val = line["out_time_ms=".Length..].Trim();
                                if (long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var outMs))
                                {
                                    var sec = outMs / 1_000_000.0;
                                    var p = totalSeconds > 0 ? sec / totalSeconds : 0.0;
                                    p = Math.Clamp(p, 0.0, 1.0);

                                    if (p >= last + 0.002 || p >= 1.0)
                                    {
                                        last = p;
                                        progress?.Report(p);
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }, token);

                var audioTask = Task.Run(async () =>
                {
                    token.ThrowIfCancellationRequested();
                    await audioPipe.WaitForConnectionAsync(token).ConfigureAwait(false);

                    const int floatsPerChunk = 8192;
                    var byteBuf = ArrayPool<byte>.Shared.Rent(floatsPerChunk * 2);

                    try
                    {
                        int i = 0;
                        while (i < sourceAudio.Length)
                        {
                            token.ThrowIfCancellationRequested();

                            int count = Math.Min(floatsPerChunk, sourceAudio.Length - i);
                            int bi = 0;

                            for (int k = 0; k < count; k++)
                            {
                                float f = sourceAudio[i + k];
                                if (f > 1f)
                                {
                                    f = 1f;
                                }
                                else if (f < -1f)
                                {
                                    f = -1f;
                                }

                                short s = (short) Math.Round(f * 32767f);
                                byteBuf[bi++] = (byte) (s & 0xFF);
                                byteBuf[bi++] = (byte) ((s >> 8) & 0xFF);
                            }

                            await audioPipe.WriteAsync(byteBuf.AsMemory(0, bi), token).ConfigureAwait(false);
                            i += count;
                        }

                        await audioPipe.FlushAsync(token).ConfigureAwait(false);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(byteBuf);
                        try { audioPipe.Dispose(); } catch { }
                    }
                }, token);

                await WriteFramePacketsToStdinOrderedAsync(
                    proc.StandardInput.BaseStream,
                    reader,
                    frameCount,
                    expectedLen,
                    token).ConfigureAwait(false);

                try { proc.StandardInput.Close(); } catch { }

                await proc.WaitForExitAsync(token).ConfigureAwait(false);

                try { await Task.WhenAll(stderrTask, progressTask, audioTask).ConfigureAwait(false); } catch { }

                if (proc.ExitCode != 0)
                {
                    SafeDelete(output);
                    throw new InvalidOperationException($"FFmpeg exit code {proc.ExitCode}. Details:\n{stderrSb}");
                }

                progress?.Report(1.0);
                return output;
            }
            catch (OperationCanceledException)
            {
                TryKill(proc);
                SafeDelete(output);
                throw;
            }
            catch
            {
                TryKill(proc);
                SafeDelete(output);
                throw;
            }
        }

        private static async Task WriteFramePacketsToStdinOrderedAsync(
            Stream stdin,
            ChannelReader<AudioVisualizerProcessor.FramePacket> reader,
            int frameCount,
            int expectedLen,
            CancellationToken token)
        {
            var pending = new Dictionary<int, AudioVisualizerProcessor.FramePacket>(capacity: Math.Min(frameCount, 1024));
            int next = 0;

            try
            {
                await foreach (var pkt in reader.ReadAllAsync(token).ConfigureAwait(false))
                {
                    if (pkt.Length != expectedLen)
                    {
                        ArrayPool<byte>.Shared.Return(pkt.Buffer);
                        throw new InvalidOperationException($"Frame size mismatch: got {pkt.Length}, expected {expectedLen}");
                    }

                    pending[pkt.Index] = pkt;

                    while (pending.TryGetValue(next, out var cur))
                    {
                        pending.Remove(next);

                        await stdin.WriteAsync(cur.Buffer.AsMemory(0, cur.Length), token).ConfigureAwait(false);
                        ArrayPool<byte>.Shared.Return(cur.Buffer);

                        next++;
                        if (next >= frameCount)
                        {
                            return; // fertig sobald frameCount geschrieben
                        }
                    }
                }

                if (next < frameCount)
                {
                    throw new InvalidOperationException($"Channel completed early. Missing frames: {frameCount - next}");
                }
            }
            finally
            {
                foreach (var kv in pending)
                {
                    try { ArrayPool<byte>.Shared.Return(kv.Value.Buffer); } catch { }
                }
                pending.Clear();
            }
        }

        // ------------------------------------------------------------
        // Core: Image[] -> stdin (raw BGRA)
        // ------------------------------------------------------------

        private static async Task WriteFramesToStdinAsync(
            Process proc,
            Image[] frames,
            int targetW,
            int targetH,
            CancellationToken token)
        {
            int frameBytes = checked(targetW * targetH * 4);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(frameBytes);

            try
            {
                using var stdin = proc.StandardInput.BaseStream;
                using var canvas = new Bitmap(targetW, targetH, PixelFormat.Format32bppArgb);

                for (int i = 0; i < frames.Length; i++)
                {
                    token.ThrowIfCancellationRequested();

                    var frame = frames[i];
                    if (frame == null)
                    {
                        continue;
                    }

                    using (var g = Graphics.FromImage(canvas))
                    {
                        g.Clear(Color.Black);
                        g.DrawImage(frame, 0, 0, frame.Width, frame.Height);
                    }

                    CopyBitmap32bppArgbToBgraBuffer(canvas, buffer, frameBytes);
                    await stdin.WriteAsync(buffer.AsMemory(0, frameBytes), token).ConfigureAwait(false);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        // ------------------------------------------------------------
        // Core: Raw-Audio Pipe + Image[] Frames
        // ------------------------------------------------------------

        private static async Task<string?> NvencRenderVideoWithRawAudioAsync(
            Image[] frames,
            int width,
            int height,
            float frameRate,
            float[] audioData,
            int audioSampleRate,
            int audioChannels,
            string? outputFilePath,
            NvencOptions options,
            IProgress<double>? progress,
            CancellationToken? ct)
        {
            if (frames is null || frames.Length == 0)
            {
                throw new ArgumentException("frames darf nicht null/leer sein.", nameof(frames));
            }

            var token = ct ?? CancellationToken.None;

            // Dimensionen bestimmen
            if (width <= 0 || height <= 0)
            {
                GetMaxDimensions(frames, out var maxW, out var maxH);
                if (width <= 0)
                {
                    width = maxW;
                }

                if (height <= 0)
                {
                    height = maxH;
                }
            }
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException("width/height müssen > 0 sein (oder aus Frames ableitbar).");
            }

            // Audio Dauer (RAM)
            double audioSeconds = 0;
            if (audioData.Length > 0 && audioSampleRate > 0 && audioChannels > 0)
            {
                audioSeconds = (audioData.Length / (double) audioChannels) / audioSampleRate;
            }

            // frameRate <= 0 => an Audio anpassen
            if (frameRate <= 0 && audioSeconds > 0.001)
            {
                frameRate = (float) (frames.Length / audioSeconds);
                if (frameRate < 1e-3f)
                {
                    frameRate = 20.0f;
                }
            }
            if (frameRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(frameRate), "frameRate muss > 0 sein (oder Audio muss gültig sein).");
            }

            var resolvedOutputPath = ResolveOutputPath(outputFilePath, options);
            Directory.CreateDirectory(Path.GetDirectoryName(resolvedOutputPath)!);

            var totalVideoSeconds = frames.Length / (double) frameRate;
            var totalSeconds = audioSeconds > 0.001 ? Math.Max(totalVideoSeconds, audioSeconds) : totalVideoSeconds;
            progress?.Report(0.0);

            string pipeName = "modularaudience_nvenc_audio_" + Guid.NewGuid().ToString("N");
            string pipePath = $@"\\.\pipe\{pipeName}";

            var args = BuildFfmpegArgsWithRawAudioPipeFast(
                width, height, frameRate,
                pipePath, audioSampleRate, audioChannels,
                resolvedOutputPath,
                options);

            var psi = new ProcessStartInfo
            {
                FileName = ResolveExternalToolPath("ffmpeg"),
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true, // progress pipe:1
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var stderrSb = new StringBuilder();

            using var audioPipe = new System.IO.Pipes.NamedPipeServerStream(
                pipeName,
                System.IO.Pipes.PipeDirection.Out,
                1,
                System.IO.Pipes.PipeTransmissionMode.Byte,
                System.IO.Pipes.PipeOptions.Asynchronous);

            try
            {
                if (!proc.Start())
                {
                    throw new InvalidOperationException("FFmpeg konnte nicht gestartet werden.");
                }

                var stderrTask = Task.Run(async () =>
                {
                    try
                    {
                        string? line;
                        while ((line = await proc.StandardError.ReadLineAsync().ConfigureAwait(false)) != null)
                        {
                            stderrSb.AppendLine(line);
                        }
                    }
                    catch { }
                }, token);

                var progressTask = Task.Run(async () =>
                {
                    try
                    {
                        string? line;
                        double lastReported = 0.0;

                        while ((line = await proc.StandardOutput.ReadLineAsync().ConfigureAwait(false)) != null)
                        {
                            if (line.StartsWith("out_time_ms=", StringComparison.OrdinalIgnoreCase))
                            {
                                var val = line["out_time_ms=".Length..].Trim();
                                if (long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var outMs))
                                {
                                    var sec = outMs / 1_000_000.0;
                                    var p = totalSeconds > 0 ? sec / totalSeconds : 0.0;
                                    p = Math.Clamp(p, 0.0, 1.0);

                                    if (p >= lastReported + 0.002 || p >= 1.0)
                                    {
                                        lastReported = p;
                                        progress?.Report(p);
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }, token);

                var audioTask = Task.Run(async () =>
                {
                    token.ThrowIfCancellationRequested();
                    await audioPipe.WaitForConnectionAsync(token).ConfigureAwait(false);

                    const int floatsPerChunk = 8192;
                    var byteBuf = ArrayPool<byte>.Shared.Rent(floatsPerChunk * 2);

                    try
                    {
                        int i = 0;
                        while (i < audioData.Length)
                        {
                            token.ThrowIfCancellationRequested();

                            int count = Math.Min(floatsPerChunk, audioData.Length - i);
                            int bi = 0;

                            for (int k = 0; k < count; k++)
                            {
                                float f = audioData[i + k];
                                if (f > 1f)
                                {
                                    f = 1f;
                                }
                                else if (f < -1f)
                                {
                                    f = -1f;
                                }

                                short s = (short) Math.Round(f * 32767f);
                                byteBuf[bi++] = (byte) (s & 0xFF);
                                byteBuf[bi++] = (byte) ((s >> 8) & 0xFF);
                            }

                            await audioPipe.WriteAsync(byteBuf.AsMemory(0, bi), token).ConfigureAwait(false);
                            i += count;
                        }

                        await audioPipe.FlushAsync(token).ConfigureAwait(false);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(byteBuf);
                        try { audioPipe.Dispose(); } catch { }
                    }
                }, token);

                await WriteFramesToStdinAsync(proc, frames, width, height, token).ConfigureAwait(false);

                try { proc.StandardInput.Close(); } catch { }

                await proc.WaitForExitAsync(token).ConfigureAwait(false);

                try { await Task.WhenAll(stderrTask, progressTask, audioTask).ConfigureAwait(false); } catch { }

                if (proc.ExitCode != 0)
                {
                    SafeDelete(resolvedOutputPath);
                    throw new InvalidOperationException($"FFmpeg exit code {proc.ExitCode}. Details:\n{stderrSb}");
                }

                progress?.Report(1.0);
                return resolvedOutputPath;
            }
            catch (OperationCanceledException)
            {
                TryKill(proc);
                SafeDelete(resolvedOutputPath);
                throw;
            }
            catch
            {
                TryKill(proc);
                SafeDelete(resolvedOutputPath);
                throw;
            }
        }

        // ------------------------------------------------------------
        // Core: FramesDir file list -> stdin + raw audio pipe
        // ------------------------------------------------------------

        private static async Task<string?> NvencRenderVideoFromFileListAsync(
            string[] frameFiles,
            int width,
            int height,
            float frameRate,
            AudioObj audio,
            string? outputFilePath,
            NvencOptions options,
            IProgress<double>? progress,
            CancellationToken token)
        {
            float[] sourceAudio = audio.Data;
            int audioSampleRate = audio.SampleRate;
            int audioChannels = audio.Channels;

            double audioSeconds = (sourceAudio.Length > 0 && audioSampleRate > 0 && audioChannels > 0)
                ? (sourceAudio.Length / (double) audioChannels) / audioSampleRate
                : 0.0;

            if (frameRate <= 0 && audioSeconds > 0.001)
            {
                frameRate = (float) (frameFiles.Length / audioSeconds);
            }

            if (frameRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(frameRate), "frameRate muss > 0 sein (oder Audio muss gültig sein).");
            }

            var resolvedOutputPath = ResolveOutputPath(outputFilePath, options);
            Directory.CreateDirectory(Path.GetDirectoryName(resolvedOutputPath)!);

            var totalVideoSeconds = frameFiles.Length / (double) frameRate;
            var totalSeconds = audioSeconds > 0.001 ? Math.Max(totalVideoSeconds, audioSeconds) : totalVideoSeconds;

            progress?.Report(0.0);

            string pipeName = "modularaudience_nvenc_audio_" + Guid.NewGuid().ToString("N");
            string pipePath = $@"\\.\pipe\{pipeName}";

            var args = BuildFfmpegArgsWithRawAudioPipeFast(
                width, height, frameRate,
                pipePath, audioSampleRate, audioChannels,
                resolvedOutputPath,
                options);

            var psi = new ProcessStartInfo
            {
                FileName = ResolveExternalToolPath("ffmpeg"),
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var stderrSb = new StringBuilder();

            using var audioPipe = new System.IO.Pipes.NamedPipeServerStream(
                pipeName,
                System.IO.Pipes.PipeDirection.Out,
                1,
                System.IO.Pipes.PipeTransmissionMode.Byte,
                System.IO.Pipes.PipeOptions.Asynchronous);

            try
            {
                if (!proc.Start())
                {
                    throw new InvalidOperationException("FFmpeg konnte nicht gestartet werden.");
                }

                var stderrTask = Task.Run(async () =>
                {
                    try
                    {
                        string? line;
                        while ((line = await proc.StandardError.ReadLineAsync().ConfigureAwait(false)) != null)
                        {
                            stderrSb.AppendLine(line);
                        }
                    }
                    catch { }
                }, token);

                var progressTask = Task.Run(async () =>
                {
                    try
                    {
                        string? line;
                        double last = 0.0;
                        while ((line = await proc.StandardOutput.ReadLineAsync().ConfigureAwait(false)) != null)
                        {
                            if (line.StartsWith("out_time_ms=", StringComparison.OrdinalIgnoreCase))
                            {
                                var val = line["out_time_ms=".Length..].Trim();
                                if (long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var outMs))
                                {
                                    var sec = outMs / 1_000_000.0;
                                    var p = totalSeconds > 0 ? sec / totalSeconds : 0.0;
                                    p = Math.Clamp(p, 0.0, 1.0);
                                    if (p >= last + 0.002 || p >= 1.0)
                                    {
                                        last = p;
                                        progress?.Report(p);
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }, token);

                var audioTask = Task.Run(async () =>
                {
                    token.ThrowIfCancellationRequested();
                    await audioPipe.WaitForConnectionAsync(token).ConfigureAwait(false);

                    const int floatsPerChunk = 8192;
                    var byteBuf = ArrayPool<byte>.Shared.Rent(floatsPerChunk * 2);

                    try
                    {
                        int i = 0;
                        while (i < sourceAudio.Length)
                        {
                            token.ThrowIfCancellationRequested();

                            int count = Math.Min(floatsPerChunk, sourceAudio.Length - i);
                            int bi = 0;

                            for (int k = 0; k < count; k++)
                            {
                                float f = sourceAudio[i + k];
                                if (f > 1f)
                                {
                                    f = 1f;
                                }
                                else if (f < -1f)
                                {
                                    f = -1f;
                                }

                                short s = (short) Math.Round(f * 32767f);
                                byteBuf[bi++] = (byte) (s & 0xFF);
                                byteBuf[bi++] = (byte) ((s >> 8) & 0xFF);
                            }

                            await audioPipe.WriteAsync(byteBuf.AsMemory(0, bi), token).ConfigureAwait(false);
                            i += count;
                        }

                        await audioPipe.FlushAsync(token).ConfigureAwait(false);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(byteBuf);
                    }
                }, token);

                await WriteFrameFilesToStdinAsync(proc, frameFiles, width, height, token).ConfigureAwait(false);

                try { proc.StandardInput.Close(); } catch { }

                await proc.WaitForExitAsync(token).ConfigureAwait(false);

                try { await Task.WhenAll(stderrTask, progressTask, audioTask).ConfigureAwait(false); } catch { }

                if (proc.ExitCode != 0)
                {
                    SafeDelete(resolvedOutputPath);
                    throw new InvalidOperationException($"FFmpeg exit code {proc.ExitCode}. Details:\n{stderrSb}");
                }

                progress?.Report(1.0);
                return resolvedOutputPath;
            }
            catch (OperationCanceledException)
            {
                TryKill(proc);
                SafeDelete(resolvedOutputPath);
                throw;
            }
            catch
            {
                TryKill(proc);
                SafeDelete(resolvedOutputPath);
                throw;
            }
        }

        private static async Task WriteFrameFilesToStdinAsync(
            Process proc,
            string[] frameFiles,
            int targetW,
            int targetH,
            CancellationToken token)
        {
            int frameBytes = checked(targetW * targetH * 4);
            var buffer = ArrayPool<byte>.Shared.Rent(frameBytes);

            try
            {
                using var stdin = proc.StandardInput.BaseStream;
                using var canvas = new Bitmap(targetW, targetH, PixelFormat.Format32bppArgb);

                for (int i = 0; i < frameFiles.Length; i++)
                {
                    token.ThrowIfCancellationRequested();

                    using var fs = new FileStream(frameFiles[i], FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var tmp = Image.FromStream(fs, useEmbeddedColorManagement: false, validateImageData: false);
                    using var img = (Image) tmp.Clone();

                    using (var g = Graphics.FromImage(canvas))
                    {
                        g.Clear(Color.Black);
                        g.DrawImage(img, 0, 0, img.Width, img.Height);
                    }

                    CopyBitmap32bppArgbToBgraBuffer(canvas, buffer, frameBytes);
                    await stdin.WriteAsync(buffer.AsMemory(0, frameBytes), token).ConfigureAwait(false);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        // ------------------------------------------------------------
        // Utilities
        // ------------------------------------------------------------

        private static void CopyBitmap32bppArgbToBgraBuffer(Bitmap bmp, byte[] dst, int expectedBytes)
        {
            if (bmp.PixelFormat != PixelFormat.Format32bppArgb)
            {
                throw new ArgumentException("Bitmap muss Format32bppArgb sein.");
            }

            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                int rowBytes = bmp.Width * 4;

                if (rowBytes * bmp.Height != expectedBytes)
                {
                    throw new InvalidOperationException("Frame byte size mismatch.");
                }

                unsafe
                {
                    byte* srcBase = (byte*) data.Scan0;
                    int di = 0;

                    fixed (byte* dstBase = dst)
                    {
                        for (int y = 0; y < bmp.Height; y++)
                        {
                            Buffer.MemoryCopy(
                                source: srcBase + (y * stride),
                                destination: dstBase + di,
                                destinationSizeInBytes: rowBytes,
                                sourceBytesToCopy: rowBytes);
                            di += rowBytes;
                        }
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        private static string BuildFfmpegArgs(
            int width,
            int height,
            float frameRate,
            string? audioFilePath,
            string outputPath,
            NvencOptions options)
        {
            static bool IsNvenc(string codec) => codec.Contains("_nvenc", StringComparison.OrdinalIgnoreCase);

            static bool SupportsPreset(string codec)
                => IsNvenc(codec)
                   || codec.Equals("libx264", StringComparison.OrdinalIgnoreCase)
                   || codec.Equals("libx265", StringComparison.OrdinalIgnoreCase)
                   || codec.Equals("libsvtav1", StringComparison.OrdinalIgnoreCase);

            static bool NeedsBv0ForCrf(string codec)
                => codec.Contains("av1", StringComparison.OrdinalIgnoreCase)
                   || codec.StartsWith("libvpx", StringComparison.OrdinalIgnoreCase);

            var sb = new StringBuilder();

            sb.Append("-hide_banner -nostats ");
            sb.Append("-loglevel error ");
            sb.Append("-progress pipe:1 ");

            sb.Append(CultureInfo.InvariantCulture,
                $"-f rawvideo -pix_fmt bgra -s {width}x{height} -r {frameRate:0.########} -i pipe:0 ");

            if (!string.IsNullOrWhiteSpace(audioFilePath))
            {
                sb.Append($"-i \"{audioFilePath}\" ");
            }

            sb.Append($"-c:v {options.VideoCodec} ");

            if (!string.IsNullOrWhiteSpace(options.Preset) && SupportsPreset(options.VideoCodec))
            {
                sb.Append($"-preset {options.Preset} ");
            }

            // Qualitäts-/Bitrate-Modus
            if (IsNvenc(options.VideoCodec))
            {
                if (options.ConstantQuality.HasValue)
                {
                    sb.Append(CultureInfo.InvariantCulture, $"-cq:v {options.ConstantQuality.Value} ");
                }
                else if (options.VideoBitrateKbps.HasValue)
                {
                    sb.Append(CultureInfo.InvariantCulture, $"-b:v {options.VideoBitrateKbps.Value}k ");
                    if (options.MaxBitrateKbps.HasValue)
                    {
                        sb.Append(CultureInfo.InvariantCulture, $"-maxrate {options.MaxBitrateKbps.Value}k ");
                    }
                    if (options.BufferSizeKbps.HasValue)
                    {
                        sb.Append(CultureInfo.InvariantCulture, $"-bufsize {options.BufferSizeKbps.Value}k ");
                    }
                }
            }
            else
            {
                if (options.Crf.HasValue)
                {
                    sb.Append(CultureInfo.InvariantCulture, $"-crf {options.Crf.Value} ");

                    // Viele CRF-Encoder erwarten b:v 0 (v.a. av1/vp9), sonst meckert ffmpeg oder macht weird ratecontrol.
                    if (!options.VideoBitrateKbps.HasValue && NeedsBv0ForCrf(options.VideoCodec))
                    {
                        sb.Append("-b:v 0 ");
                    }
                }
                else if (options.VideoBitrateKbps.HasValue)
                {
                    sb.Append(CultureInfo.InvariantCulture, $"-b:v {options.VideoBitrateKbps.Value}k ");
                    if (options.MaxBitrateKbps.HasValue)
                    {
                        sb.Append(CultureInfo.InvariantCulture, $"-maxrate {options.MaxBitrateKbps.Value}k ");
                    }
                    if (options.BufferSizeKbps.HasValue)
                    {
                        sb.Append(CultureInfo.InvariantCulture, $"-bufsize {options.BufferSizeKbps.Value}k ");
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(options.Profile))
            {
                sb.Append($"-profile:v {options.Profile} ");
            }

            sb.Append("-pix_fmt yuv420p ");

            if (!string.IsNullOrWhiteSpace(audioFilePath))
            {
                sb.Append("-c:a aac -b:a 192k -shortest ");
            }

            if (options.FastStart)
            {
                sb.Append("-movflags +faststart ");
            }

            sb.Append("-y ");
            sb.Append($"\"{outputPath}\"");

            return sb.ToString();
        }


        private static string BuildFfmpegArgsWithRawAudioPipe(
            int width,
            int height,
            float frameRate,
            string audioPipePath,
            int audioSampleRate,
            int audioChannels,
            string outputPath,
            NvencOptions options)
        {
            static bool IsNvenc(string codec) => codec.Contains("_nvenc", StringComparison.OrdinalIgnoreCase);

            static bool SupportsPreset(string codec)
                => IsNvenc(codec)
                   || codec.Equals("libx264", StringComparison.OrdinalIgnoreCase)
                   || codec.Equals("libx265", StringComparison.OrdinalIgnoreCase)
                   || codec.Equals("libsvtav1", StringComparison.OrdinalIgnoreCase);

            static bool NeedsBv0ForCrf(string codec)
                => codec.Contains("av1", StringComparison.OrdinalIgnoreCase)
                   || codec.StartsWith("libvpx", StringComparison.OrdinalIgnoreCase);

            var sb = new StringBuilder();

            sb.Append("-hide_banner -nostats ");
            sb.Append("-loglevel error ");
            sb.Append("-progress pipe:1 ");

            sb.Append(CultureInfo.InvariantCulture,
                $"-f rawvideo -pix_fmt bgra -s {width}x{height} -r {frameRate:0.########} -i pipe:0 ");

            sb.Append(CultureInfo.InvariantCulture,
                $"-f s16le -ar {audioSampleRate} -ac {audioChannels} -i \"{audioPipePath}\" ");

            sb.Append($"-c:v {options.VideoCodec} ");

            if (!string.IsNullOrWhiteSpace(options.Preset) && SupportsPreset(options.VideoCodec))
            {
                sb.Append($"-preset {options.Preset} ");
            }

            // Qualitäts-/Bitrate-Modus
            if (IsNvenc(options.VideoCodec))
            {
                if (options.ConstantQuality.HasValue)
                {
                    sb.Append(CultureInfo.InvariantCulture, $"-cq:v {options.ConstantQuality.Value} ");
                }
                else if (options.VideoBitrateKbps.HasValue)
                {
                    sb.Append(CultureInfo.InvariantCulture, $"-b:v {options.VideoBitrateKbps.Value}k ");
                    if (options.MaxBitrateKbps.HasValue)
                    {
                        sb.Append(CultureInfo.InvariantCulture, $"-maxrate {options.MaxBitrateKbps.Value}k ");
                    }
                    if (options.BufferSizeKbps.HasValue)
                    {
                        sb.Append(CultureInfo.InvariantCulture, $"-bufsize {options.BufferSizeKbps.Value}k ");
                    }
                }
            }
            else
            {
                if (options.Crf.HasValue)
                {
                    sb.Append(CultureInfo.InvariantCulture, $"-crf {options.Crf.Value} ");
                    if (!options.VideoBitrateKbps.HasValue && NeedsBv0ForCrf(options.VideoCodec))
                    {
                        sb.Append("-b:v 0 ");
                    }
                }
                else if (options.VideoBitrateKbps.HasValue)
                {
                    sb.Append(CultureInfo.InvariantCulture, $"-b:v {options.VideoBitrateKbps.Value}k ");
                    if (options.MaxBitrateKbps.HasValue)
                    {
                        sb.Append(CultureInfo.InvariantCulture, $"-maxrate {options.MaxBitrateKbps.Value}k ");
                    }
                    if (options.BufferSizeKbps.HasValue)
                    {
                        sb.Append(CultureInfo.InvariantCulture, $"-bufsize {options.BufferSizeKbps.Value}k ");
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(options.Profile))
            {
                sb.Append($"-profile:v {options.Profile} ");
            }

            sb.Append("-pix_fmt yuv420p ");
            sb.Append("-c:a aac -b:a 192k -shortest ");

            if (options.FastStart)
            {
                sb.Append("-movflags +faststart ");
            }

            sb.Append("-y ");
            sb.Append($"\"{outputPath}\"");

            return sb.ToString();
        }

        private static string BuildFfmpegArgsFast(
            int width,
            int height,
            float frameRate,
            string? audioFilePath,
            string outputPath,
            NvencOptions options)
        {
            static bool IsNvenc(string codec) => codec.Contains("_nvenc", StringComparison.OrdinalIgnoreCase);
            static bool NeedsBv0ForCrf(string codec) => codec.Contains("av1", StringComparison.OrdinalIgnoreCase) || codec.StartsWith("libvpx", StringComparison.OrdinalIgnoreCase);

            var sb = new StringBuilder();

            // Basis-Flags
            sb.Append("-hide_banner -nostats -loglevel error -progress pipe:1 ");

            // Thread Queue für Input-Pipes erhöhen (verhindert Stottern)
            sb.Append("-thread_queue_size 512 ");

            // Input definieren
            sb.Append(CultureInfo.InvariantCulture,
                $"-f rawvideo -pix_fmt bgra -s {width}x{height} -r {frameRate:0.########} -i pipe:0 ");

            if (!string.IsNullOrWhiteSpace(audioFilePath))
            {
                sb.Append($"-i \"{audioFilePath}\" ");
            }

            // Codec
            sb.Append($"-c:v {options.VideoCodec} ");

            // Preset & Tuning (Hier lag wahrscheinlich der Fehler)
            if (!string.IsNullOrWhiteSpace(options.Preset))
            {
                // Wenn User p5 nutzt, überschreiben wir es hier NICHT hart, 
                // sondern vertrauen auf die options (siehe unten bei "Anpassung Options")
                sb.Append($"-preset {options.Preset} ");
            }

            // NVENC Spezifische Speed-Optimierungen (SAFE MODE)
            if (IsNvenc(options.VideoCodec))
            {
                // B-Frames aus = Massive Speed-Steigerung & weniger VRAM Last
                sb.Append("-bf 0 ");

                // GOP auf 2 Sekunden fixieren (statt automatisch)
                sb.Append(CultureInfo.InvariantCulture, $"-g {(int) (frameRate * 2)} ");
            }

            // Bitrate / Qualität
            if (IsNvenc(options.VideoCodec))
            {
                if (options.ConstantQuality.HasValue)
                {
                    sb.Append(CultureInfo.InvariantCulture, $"-cq:v {options.ConstantQuality.Value} ");
                }
                else if (options.VideoBitrateKbps.HasValue)
                {
                    sb.Append(CultureInfo.InvariantCulture, $"-b:v {options.VideoBitrateKbps.Value}k ");
                    var maxRate = options.MaxBitrateKbps ?? options.VideoBitrateKbps.Value;
                    var bufSize = options.BufferSizeKbps ?? options.VideoBitrateKbps.Value;
                    sb.Append(CultureInfo.InvariantCulture, $"-maxrate {maxRate}k -bufsize {bufSize}k ");
                }
            }
            else
            {
                // CPU Fallback
                if (options.Crf.HasValue)
                {
                    sb.Append(CultureInfo.InvariantCulture, $"-crf {options.Crf.Value} ");
                    if (!options.VideoBitrateKbps.HasValue && NeedsBv0ForCrf(options.VideoCodec))
                    {
                        sb.Append("-b:v 0 ");
                    }
                }
                else if (options.VideoBitrateKbps.HasValue)
                {
                    sb.Append(CultureInfo.InvariantCulture, $"-b:v {options.VideoBitrateKbps.Value}k ");
                }
            }

            // Profil (Nur setzen, wenn es nicht null ist UND Sinn ergibt)
            // HEVC unterstützt "high" oft nicht (sondern "main"), daher hier Vorsicht.
            if (!string.IsNullOrWhiteSpace(options.Profile))
            {
                // Kleiner Schutz: Falls HEVC und "high" eingestellt ist, ignorieren wir es, um Absturz zu vermeiden.
                bool isHevc = options.VideoCodec.Contains("hevc", StringComparison.OrdinalIgnoreCase);
                bool isHigh = options.Profile.Equals("high", StringComparison.OrdinalIgnoreCase);

                if (!(isHevc && isHigh))
                {
                    sb.Append($"-profile:v {options.Profile} ");
                }
            }

            // Output Format (Pixel Format Conversion)
            // Wir nutzen explizit -pix_fmt yuv420p für Kompatibilität.
            // Entfernt: -sws_flags (verursacht manchmal Syntax-Fehler wenn kein Filter-Graph da ist)
            sb.Append("-pix_fmt yuv420p ");

            // Audio Settings
            if (!string.IsNullOrWhiteSpace(audioFilePath))
            {
                sb.Append("-c:a aac -b:a 192k -shortest ");
            }

            if (options.FastStart)
            {
                sb.Append("-movflags +faststart ");
            }

            sb.Append("-y ");
            sb.Append($"\"{outputPath}\"");

            return sb.ToString();
        }

        // Dieselbe Logik für die RawAudioPipe-Variante:
        private static string BuildFfmpegArgsWithRawAudioPipeFast(
            int width, int height, float frameRate,
            string audioPipePath, int audioSampleRate, int audioChannels,
            string outputPath, NvencOptions options)
        {
            static bool IsNvenc(string codec) => codec.Contains("_nvenc", StringComparison.OrdinalIgnoreCase);
            static bool NeedsBv0ForCrf(string codec) => codec.Contains("av1", StringComparison.OrdinalIgnoreCase) || codec.StartsWith("libvpx", StringComparison.OrdinalIgnoreCase);

            var sb = new StringBuilder();

            sb.Append("-hide_banner -nostats -loglevel error -progress pipe:1 ");

            // Thread Queue
            sb.Append("-thread_queue_size 512 ");
            sb.Append(CultureInfo.InvariantCulture,
                $"-f rawvideo -pix_fmt bgra -s {width}x{height} -r {frameRate:0.########} -i pipe:0 ");

            sb.Append("-thread_queue_size 512 ");
            sb.Append(CultureInfo.InvariantCulture,
                $"-f s16le -ar {audioSampleRate} -ac {audioChannels} -i \"{audioPipePath}\" ");

            // Codec
            sb.Append($"-c:v {options.VideoCodec} ");

            if (!string.IsNullOrWhiteSpace(options.Preset))
            {
                sb.Append($"-preset {options.Preset} ");
            }

            // NVENC Safe Speed Tuning
            if (IsNvenc(options.VideoCodec))
            {
                sb.Append("-bf 0 "); // Keine B-Frames = Speed
                sb.Append(CultureInfo.InvariantCulture, $"-g {(int) (frameRate * 2)} ");
            }

            // Bitrate / Quali
            if (IsNvenc(options.VideoCodec))
            {
                if (options.ConstantQuality.HasValue)
                {
                    sb.Append(CultureInfo.InvariantCulture, $"-cq:v {options.ConstantQuality.Value} ");
                }
                else if (options.VideoBitrateKbps.HasValue)
                {
                    sb.Append(CultureInfo.InvariantCulture, $"-b:v {options.VideoBitrateKbps.Value}k ");
                    var maxRate = options.MaxBitrateKbps ?? options.VideoBitrateKbps.Value;
                    var bufSize = options.BufferSizeKbps ?? options.VideoBitrateKbps.Value;
                    sb.Append(CultureInfo.InvariantCulture, $"-maxrate {maxRate}k -bufsize {bufSize}k ");
                }
            }
            else
            {
                if (options.Crf.HasValue)
                {
                    sb.Append(CultureInfo.InvariantCulture, $"-crf {options.Crf.Value} ");
                    if (!options.VideoBitrateKbps.HasValue && NeedsBv0ForCrf(options.VideoCodec))
                    {
                        sb.Append("-b:v 0 ");
                    }
                }
                else if (options.VideoBitrateKbps.HasValue)
                {
                    sb.Append(CultureInfo.InvariantCulture, $"-b:v {options.VideoBitrateKbps.Value}k ");
                }
            }

            if (!string.IsNullOrWhiteSpace(options.Profile))
            {
                // Safe check für HEVC Profile
                bool isHevc = options.VideoCodec.Contains("hevc", StringComparison.OrdinalIgnoreCase);
                bool isHigh = options.Profile.Equals("high", StringComparison.OrdinalIgnoreCase);

                if (!(isHevc && isHigh))
                {
                    sb.Append($"-profile:v {options.Profile} ");
                }
            }

            sb.Append("-pix_fmt yuv420p ");
            sb.Append("-c:a aac -b:a 192k -shortest ");

            if (options.FastStart)
            {
                sb.Append("-movflags +faststart ");
            }

            sb.Append("-y ");
            sb.Append($"\"{outputPath}\"");

            return sb.ToString();
        }


        private static void GetMaxDimensions(Image[] frames, out int maxW, out int maxH)
        {
            maxW = 0; maxH = 0;
            for (int i = 0; i < frames.Length; i++)
            {
                if (frames[i] is null)
                {
                    continue;
                }

                if (frames[i].Width > maxW)
                {
                    maxW = frames[i].Width;
                }

                if (frames[i].Height > maxH)
                {
                    maxH = frames[i].Height;
                }
            }
        }

        private static string ResolveOutputPath(string? outputFilePath, NvencOptions options, bool addCodecToName = true)
        {
            string baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                "NVenc_Output");

            Directory.CreateDirectory(baseDir);

            string ext = GetPreferredContainerExtension(options);
            string defaultFileName = "NVENC_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + (addCodecToName ? options.GetType().Name.Split('.').LastOrDefault() : "") + ext;

            // 1) Null/leer → Default
            if (string.IsNullOrWhiteSpace(outputFilePath))
            {
                return Path.Combine(baseDir, defaultFileName);
            }

            outputFilePath = outputFilePath.Trim();

            // 2) NUR Dateiname (kein Verzeichnisanteil)
            if (!outputFilePath.Contains(Path.DirectorySeparatorChar) &&
                !outputFilePath.Contains(Path.AltDirectorySeparatorChar))
            {
                string fileName = EnsurePreferredExtension(outputFilePath, ext);
                return Path.Combine(baseDir, fileName);
            }

            // Ab hier: Pfad vorhanden
            outputFilePath = Path.GetFullPath(outputFilePath);

            // 3) Existierendes Verzeichnis
            if (Directory.Exists(outputFilePath))
            {
                return Path.Combine(outputFilePath, defaultFileName);
            }

            // 4) Endet auf Slash → explizites Verzeichnis
            if (outputFilePath.EndsWith(Path.DirectorySeparatorChar) ||
                outputFilePath.EndsWith(Path.AltDirectorySeparatorChar))
            {
                Directory.CreateDirectory(outputFilePath);
                return Path.Combine(outputFilePath, defaultFileName);
            }

            // 5) Pfad + Dateiname
            string? dir = Path.GetDirectoryName(outputFilePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            return EnsurePreferredExtension(outputFilePath, ext);
        }

        // optional: alte Signatur behalten (falls irgendwo intern noch benutzt)
        private static string ResolveOutputPath(string? outputFilePath)
            => ResolveOutputPath(outputFilePath, NvencOptions.H264Default);

        private static string GetPreferredContainerExtension(NvencOptions options)
        {
            var c = (options.VideoCodec ?? "").Trim().ToLowerInvariant();

            // Editing/Intermediate Codecs -> MOV
            if (c is "prores_ks" or "prores_aw" or "dnxhd" or "dnxhr" or "cfhd" or "qtrle")
            {
                return ".mov";
            }

            // VP8/VP9 -> WebM (typisch)
            if (c.StartsWith("libvpx"))
            {
                return ".webm";
            }

            // AV1 -> Matroska ist sehr robust (mp4 geht auch, aber mkv macht weniger Stress)
            if (c.Contains("av1"))
            {
                return ".mkv";
            }

            // Default “delivery”
            return ".mp4";
        }

        private static string EnsurePreferredExtension(string pathOrFileName, string preferredExt)
        {
            // Wenn gar keine Extension: anhängen
            string ext = Path.GetExtension(pathOrFileName);
            if (string.IsNullOrWhiteSpace(ext))
            {
                return pathOrFileName + preferredExt;
            }

            // Wenn “Container-Extension” aber falsch: ersetzen
            // (damit "out.mp4" bei ProRes automatisch "out.mov" wird)
            if (ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".mov", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".mkv", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".webm", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".avi", StringComparison.OrdinalIgnoreCase))
            {
                if (!ext.Equals(preferredExt, StringComparison.OrdinalIgnoreCase))
                {
                    return Path.ChangeExtension(pathOrFileName, preferredExt);
                }
            }

            // Sonst: user hat bewusst was anderes gewählt -> lassen
            return pathOrFileName;
        }


        public static string SanitizeFileName(string? name, string replacement = "_")
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            // Verbotene Zeichen für Dateinamen (Windows)
            var invalidChars = Path.GetInvalidFileNameChars();

            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (invalidChars.Contains(c))
                {
                    sb.Append(replacement);
                }
                else
                {
                    sb.Append(c);
                }
            }

            // Zusätzliche kosmetische Fixes
            string result = sb.ToString().Trim();

            // Mehrere Ersatzzeichen zusammenfassen
            while (result.Contains(replacement + replacement))
            {
                result = result.Replace(replacement + replacement, replacement);
            }

            // Windows mag keine Namen, die mit Punkt/Leerzeichen enden
            result = result.TrimEnd('.', ' ');

            return result;
        }


        private static void TryKill(Process p)
        {
            try
            {
                if (!p.HasExited)
                {
                    p.Kill(entireProcessTree: true);
                }
            }
            catch { }
        }

        private static void SafeDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch { }
        }

        // ------------------------------------------------------------
        // Extra helpers you already had (BuildImageFromBytes + LoadImagesParallel)
        // (unverändert, amplitude-unrelated)
        // ------------------------------------------------------------

        public static Task<Image?> BuildImageFromBytesAsync(byte[] data, int width, int height,
            string channelCode = "BGRA", CancellationToken ct = default)
        {
            if (data is null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            if (string.IsNullOrWhiteSpace(channelCode))
            {
                throw new ArgumentException("channelCode darf nicht leer sein.", nameof(channelCode));
            }

            channelCode = channelCode.Trim().ToUpperInvariant();

            return Task.Run<Image?>(() =>
            {
                ct.ThrowIfCancellationRequested();

                int channels = channelCode.Length;
                if (channels is not (1 or 3 or 4))
                {
                    return null;
                }

                long expected = (long) width * height * channels;
                if (data.LongLength != expected)
                {
                    return null;
                }

                PixelFormat pf = channels switch
                {
                    4 => PixelFormat.Format32bppArgb,
                    3 => PixelFormat.Format24bppRgb,
                    1 => PixelFormat.Format8bppIndexed,
                    _ => throw new InvalidOperationException()
                };

                Bitmap bmp = new(width, height, pf);

                try
                {
                    if (channels == 1)
                    {
                        var pal = bmp.Palette;
                        for (int i = 0; i < 256; i++)
                        {
                            pal.Entries[i] = Color.FromArgb(i, i, i);
                        }

                        bmp.Palette = pal;
                    }

                    var rect = new Rectangle(0, 0, width, height);
                    var bd = bmp.LockBits(rect, ImageLockMode.WriteOnly, pf);

                    try
                    {
                        ct.ThrowIfCancellationRequested();

                        int dstStride = bd.Stride;
                        int srcStride = width * channels;

                        unsafe
                        {
                            byte* dstBase = (byte*) bd.Scan0;

                            fixed (byte* srcBase0 = data)
                            {
                                for (int y = 0; y < height; y++)
                                {
                                    ct.ThrowIfCancellationRequested();

                                    byte* dstRow = dstBase + (y * dstStride);
                                    byte* srcRow = srcBase0 + (y * srcStride);

                                    if (channels == 4)
                                    {
                                        for (int x = 0; x < width; x++)
                                        {
                                            byte c0 = srcRow[x * 4 + 0];
                                            byte c1 = srcRow[x * 4 + 1];
                                            byte c2 = srcRow[x * 4 + 2];
                                            byte c3 = srcRow[x * 4 + 3];

                                            (byte b, byte g, byte r, byte a) = Map4(channelCode, c0, c1, c2, c3);

                                            int di = x * 4;
                                            dstRow[di + 0] = b;
                                            dstRow[di + 1] = g;
                                            dstRow[di + 2] = r;
                                            dstRow[di + 3] = a;
                                        }
                                    }
                                    else if (channels == 3)
                                    {
                                        for (int x = 0; x < width; x++)
                                        {
                                            byte c0 = srcRow[x * 3 + 0];
                                            byte c1 = srcRow[x * 3 + 1];
                                            byte c2 = srcRow[x * 3 + 2];

                                            (byte b, byte g, byte r) = Map3(channelCode, c0, c1, c2);

                                            int di = x * 3;
                                            dstRow[di + 0] = b;
                                            dstRow[di + 1] = g;
                                            dstRow[di + 2] = r;
                                        }
                                    }
                                    else
                                    {
                                        Marshal.Copy(data, y * srcStride, (IntPtr) dstRow, srcStride);
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        bmp.UnlockBits(bd);
                    }

                    return bmp;
                }
                catch
                {
                    bmp.Dispose();
                    return null;
                }

            }, ct);

            static (byte b, byte g, byte r, byte a) Map4(string code, byte c0, byte c1, byte c2, byte c3) => code switch
            {
                "BGRA" => (c0, c1, c2, c3),
                "RGBA" => (c2, c1, c0, c3),
                "ARGB" => (c3, c2, c1, c0),
                "ABGR" => (c1, c2, c3, c0),
                _ => (c0, c1, c2, c3)
            };

            static (byte b, byte g, byte r) Map3(string code, byte c0, byte c1, byte c2) => code switch
            {
                "BGR" => (c0, c1, c2),
                "RGB" => (c2, c1, c0),
                _ => (c0, c1, c2)
            };
        }

        public static async Task<Image[]> LoadImagesParallelAsync(
            string[] imageFilePaths,
            string? imagesDir = null,
            int maxWorkers = 0,
            IProgress<double>? progress = null,
            CancellationToken ct = default)
        {
            if (imageFilePaths is null)
            {
                throw new ArgumentNullException(nameof(imageFilePaths));
            }

            if (imageFilePaths.Length == 0)
            {
                return [];
            }

            maxWorkers = maxWorkers <= 0
                ? Environment.ProcessorCount
                : Math.Clamp(maxWorkers, 1, Environment.ProcessorCount);

            string? baseDir = string.IsNullOrWhiteSpace(imagesDir) ? null : Path.GetFullPath(imagesDir);

            var paths = imageFilePaths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p =>
                {
                    var trimmed = p.Trim();
                    if (Path.IsPathRooted(trimmed))
                    {
                        return Path.GetFullPath(trimmed);
                    }

                    if (baseDir != null)
                    {
                        return Path.GetFullPath(Path.Combine(baseDir, trimmed));
                    }

                    return Path.GetFullPath(trimmed);
                })
                .ToArray();

            int total = paths.Length;
            if (total == 0)
            {
                return [];
            }

            progress?.Report(0);

            var gate = new SemaphoreSlim(maxWorkers, maxWorkers);
            var results = new Image?[total];
            int done = 0;

            var tasks = new Task[total];

            for (int i = 0; i < total; i++)
            {
                int idx = i;
                string path = paths[idx];

                tasks[idx] = Task.Run(async () =>
                {
                    await gate.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        ct.ThrowIfCancellationRequested();

                        if (!File.Exists(path))
                        {
                            return;
                        }

                        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                        using var tmp = Image.FromStream(fs, useEmbeddedColorManagement: false, validateImageData: false);
                        results[idx] = (Image) tmp.Clone();
                    }
                    catch { }
                    finally
                    {
                        gate.Release();
                        int now = Interlocked.Increment(ref done);
                        progress?.Report(now / (double) total);
                    }
                }, ct);
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            return results.Where(img => img != null).Cast<Image>().ToArray();
        }
    }
}
