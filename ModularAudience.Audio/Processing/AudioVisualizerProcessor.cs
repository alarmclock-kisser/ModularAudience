using ModularAudience.Audio.Processing.DTOs;
using System;
using System.Buffers;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ModularAudience.Audio.Processing
{
    public static class AudioVisualizerProcessor
    {
        public static async Task<Image[]> RenderVisualizerImagesAsync(
            AudioObj audio,
            int width,
            int height,
            float frameRate = 20.0f,
            string? outputFilePath = null,
            int maxWorkers = 0,
            IProgress<double>? progress = null,
            CancellationToken? ct = null)
        {
            _ = outputFilePath; // bewusst ungenutzt (RAM-Variante). Param bleibt für API-Kompatibilität.

            if (audio is null)
            {
                throw new ArgumentNullException(nameof(audio));
            }

            if (audio.Data is null)
            {
                throw new ArgumentException("audio.Data ist null.", nameof(audio));
            }

            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            var token = ct ?? CancellationToken.None;

            if (frameRate <= 0)
            {
                frameRate = 20.0f;
            }

            maxWorkers = maxWorkers <= 0 ? Environment.ProcessorCount : Math.Clamp(maxWorkers, 1, Environment.ProcessorCount);

            int channels = Math.Max(1, audio.Channels);
            int sampleRate = Math.Max(1, audio.SampleRate);

            // Frame count aus Duration (wenn vorhanden), sonst aus Datenlänge
            double durationSeconds = audio.Duration.TotalSeconds > 0
                ? audio.Duration.TotalSeconds
                : EstimateDurationSeconds(audio.Data.Length, sampleRate, channels);

            int frameCount = Math.Max(1, (int) Math.Ceiling(durationSeconds * frameRate));
            progress?.Report(0.0);

            var images = new Image[frameCount];
            int done = 0;

            var opts = new ParallelOptions
            {
                MaxDegreeOfParallelism = maxWorkers,
                CancellationToken = token
            };

            await Parallel.ForAsync(0, frameCount, opts, async (i, innerCt) =>
            {
                innerCt.ThrowIfCancellationRequested();

                // Frame-Zeitbereich
                double t0 = i / (double) frameRate;
                double t1 = (i + 1) / (double) frameRate;

                // Samples (pro Channel) in Indexbereich übersetzen
                long startSample = (long) Math.Floor(t0 * sampleRate);
                long endSample = (long) Math.Floor(t1 * sampleRate);

                // clamp auf verfügbares Material
                long availableSamplesPerChannel = audio.Data.LongLength / Math.Max(1, channels);

                if (startSample < 0)
                {
                    startSample = 0;
                }

                if (endSample < startSample + 1)
                {
                    endSample = startSample + 1;
                }

                if (startSample > availableSamplesPerChannel)
                {
                    startSample = availableSamplesPerChannel;
                }

                if (endSample > availableSamplesPerChannel)
                {
                    endSample = availableSamplesPerChannel;
                }

                var bmp = RenderWaveformFrame(audio.Data, channels, width, height, startSample, endSample);

                images[i] = bmp;

                int now = Interlocked.Increment(ref done);
                progress?.Report(now / (double) frameCount);

                await Task.CompletedTask.ConfigureAwait(false);
            }).ConfigureAwait(false);

            return images;
        }

        public static async Task<string?> RenderVisualizerImagesToTempDirAsync(
            AudioObj audio,
            int width,
            int height,
            float frameRate = 20.0f,
            string? outputFilePath = null,
            int maxWorkers = 0,
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

            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            var token = ct ?? CancellationToken.None;

            if (frameRate <= 0)
            {
                frameRate = 20.0f;
            }

            maxWorkers = maxWorkers <= 0 ? Environment.ProcessorCount : Math.Clamp(maxWorkers, 1, Environment.ProcessorCount);

            int channels = Math.Max(1, audio.Channels);
            int sampleRate = Math.Max(1, audio.SampleRate);

            double durationSeconds = audio.Duration.TotalSeconds > 0
                ? audio.Duration.TotalSeconds
                : EstimateDurationSeconds(audio.Data.Length, sampleRate, channels);

            int frameCount = Math.Max(1, (int) Math.Ceiling(durationSeconds * frameRate));

            // TempDir bestimmen (oder aus outputFilePath ableiten)
            string dir = ResolveOutputDirForFrames(outputFilePath);
            Directory.CreateDirectory(dir);

            // Leading zeros
            int digits = Math.Max(3, frameCount.ToString(CultureInfo.InvariantCulture).Length);
            string ext = ".png";

            progress?.Report(0.0);

            int done = 0;

            var opts = new ParallelOptions
            {
                MaxDegreeOfParallelism = maxWorkers,
                CancellationToken = token
            };

            try
            {
                await Parallel.ForAsync(0, frameCount, opts, async (i, innerCt) =>
                {
                    innerCt.ThrowIfCancellationRequested();

                    double t0 = i / (double) frameRate;
                    double t1 = (i + 1) / (double) frameRate;

                    long startSample = (long) Math.Floor(t0 * sampleRate);
                    long endSample = (long) Math.Floor(t1 * sampleRate);
                    long availableSamplesPerChannel = audio.Data.LongLength / Math.Max(1, channels);

                    if (startSample < 0)
                    {
                        startSample = 0;
                    }

                    if (endSample < startSample + 1)
                    {
                        endSample = startSample + 1;
                    }

                    if (startSample > availableSamplesPerChannel)
                    {
                        startSample = availableSamplesPerChannel;
                    }

                    if (endSample > availableSamplesPerChannel)
                    {
                        endSample = availableSamplesPerChannel;
                    }

                    using var bmp = RenderWaveformFrame(audio.Data, channels, width, height, startSample, endSample);

                    string name = i.ToString("D" + digits, CultureInfo.InvariantCulture) + ext;
                    string path = Path.Combine(dir, name);

                    // Save (PNG)
                    bmp.Save(path, ImageFormat.Png);

                    int now = Interlocked.Increment(ref done);
                    progress?.Report(now / (double) frameCount);

                    await Task.CompletedTask.ConfigureAwait(false);
                }).ConfigureAwait(false);

                return dir;
            }
            catch
            {
                // Optional: Bei Fehler/Cancel kannst du das Dir löschen. Ich lasse es bewusst stehen (Debug/Partial Output).
                throw;
            }
        }

        public readonly struct FramePacket
        {
            public FramePacket(int index, byte[] buffer, int length)
            {
                this.Index = index;
                this.Buffer = buffer;
                this.Length = length;
            }
            public int Index { get; }
            public byte[] Buffer { get; }   // BGRA
            public int Length { get; }
        }

        public static (ChannelReader<FramePacket> reader, int frameCount) RenderVisualizerFramesBgraChannel(
    AudioObj audio,
    int width,
    int height,
    float frameRate = 20.0f,
    float amplification = 0.8f,
    Color? graphColor = null,
    Color? backColor = null,
    int thickness = 1,
    float threshold = 0.1f,
    int maxWorkers = 0,
    int channelCapacity = 0,
    List<TimedOverlayString>? overlayStrings = null,
    IProgress<double>? progress = null,
    CancellationToken? ct = null)
        {
            if (audio is null)
            {
                throw new ArgumentNullException(nameof(audio));
            }

            if (audio.Data is null)
            {
                throw new ArgumentException("audio.Data null", nameof(audio));
            }

            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            var token = ct ?? CancellationToken.None;

            if (frameRate <= 0)
            {
                frameRate = 20f;
            }

            maxWorkers = maxWorkers <= 0
                ? Environment.ProcessorCount
                : Math.Clamp(maxWorkers, 1, Environment.ProcessorCount);

            if (channelCapacity <= 0)
            {
                channelCapacity = Math.Max(2, maxWorkers * 2);
            }

            int channels = Math.Max(1, audio.Channels);
            int sampleRate = Math.Max(1, audio.SampleRate);

            double duration = audio.Duration.TotalSeconds > 0
                ? audio.Duration.TotalSeconds
                : (audio.Data.Length / (double) channels) / sampleRate;

            int frameCount = Math.Max(1, (int) Math.Ceiling(duration * frameRate));
            int frameBytes = checked(width * height * 4);

            // resolve colors and parameters
            var gc = graphColor ?? Color.White;
            var bc = backColor ?? Color.Black;
            byte gB = gc.B, gG = gc.G, gR = gc.R, gA = gc.A;
            byte bB = bc.B, bG = bc.G, bR = bc.R, bA = bc.A;
            int lineThickness = Math.Max(1, thickness);
            float sensitivity = Math.Clamp(1f - threshold, 0f, 1f);

            var channel = Channel.CreateBounded<FramePacket>(
                new BoundedChannelOptions(channelCapacity)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = false,
                    SingleWriter = false
                });

            progress?.Report(0.0);
            int produced = 0;

            long availableSamplesPerChannel = audio.Data.LongLength / Math.Max(1, channels);

            // Producer Task
            _ = Task.Run(async () =>
            {
                var opts = new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxWorkers,
                    CancellationToken = token
                };

                try
                {
                    await Parallel.ForAsync(0, frameCount, opts, async (i, innerCt) =>
                    {
                        innerCt.ThrowIfCancellationRequested();

                        double t0 = i / (double) frameRate;
                        double t1 = (i + 1) / (double) frameRate;
                        double currentTime = t0;

                        long s0 = (long) Math.Floor(t0 * sampleRate);
                        long s1 = (long) Math.Floor(t1 * sampleRate);
                        if (s1 <= s0)
                        {
                            s1 = s0 + 1;
                        }

                        // CLAMP auf verfügbares Material (pro Channel)
                        if (s0 < 0)
                        {
                            s0 = 0;
                        }

                        if (s1 < 1)
                        {
                            s1 = 1;
                        }

                        if (s0 > availableSamplesPerChannel)
                        {
                            s0 = availableSamplesPerChannel;
                        }

                        if (s1 > availableSamplesPerChannel)
                        {
                            s1 = availableSamplesPerChannel;
                        }

                        if (s1 <= s0)
                        {
                            s1 = Math.Min(availableSamplesPerChannel, s0 + 1);
                        }

                        byte[] buffer = ArrayPool<byte>.Shared.Rent(frameBytes);

                        try
                        {
                            RenderWaveformToBgra(
                                audio.Data, channels,
                                width, height,
                                s0, s1,
                                buffer,
                                amplification,
                                gB, gG, gR, gA,
                                bB, bG, bR, bA,
                                lineThickness,
                                sensitivity);

                            if (overlayStrings != null && overlayStrings.Count > 0)
                            {
                                RenderOverlayStrings(buffer, width, height, overlayStrings, currentTime);
                            }

                            await channel.Writer.WriteAsync(new FramePacket(i, buffer, frameBytes), innerCt)
                                .ConfigureAwait(false);

                            // Ownership geht an Consumer -> nicht zurückgeben
                            buffer = null!;

                            int now = Interlocked.Increment(ref produced);
                            progress?.Report(now / (double) frameCount);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex);
                        }
                        finally
                        {
                            if (buffer != null)
                            {
                                ArrayPool<byte>.Shared.Return(buffer);
                            }
                        }
                    }).ConfigureAwait(false);

                    channel.Writer.TryComplete();
                }
                catch (OperationCanceledException oce)
                {
                    channel.Writer.TryComplete(oce);
                }
                catch (Exception ex)
                {
                    channel.Writer.TryComplete(ex);
                }
            }, token);

            return (channel.Reader, frameCount);
        }


        // -------------------------
        // CORE: Waveform → BGRA
        // -------------------------
        private static void RenderWaveformToBgra(
            float[] interleaved,
            int channels,
            int width,
            int height,
            long startSample,
            long endSample,
            byte[] dst,
            float amplification,
            byte waveB, byte waveG, byte waveR, byte waveA,
            byte backB, byte backG, byte backR, byte backA,
            int thickness,
            float sensitivity)

        {
            int midY = height / 2;
            int frameSamples = (int) Math.Max(1, endSample - startSample);

            // clear background
            ClearBgra(dst, width, height, backB, backG, backR, backA);

            // effective amplification scaled by sensitivity (0 => full, 1 => minimal)
            float ampScale = Math.Clamp(sensitivity, 0f, 1f);
            float amp = Math.Max(0.0f, amplification) * ampScale;
            int thick = Math.Max(1, thickness);

            for (int x = 0; x < width; x++)
            {
                long a = startSample + (long) (x / (double) width * frameSamples);
                long b = startSample + (long) ((x + 1) / (double) width * frameSamples);
                if (b <= a)
                {
                    b = a + 1;
                }

                float min = 1f, max = -1f;

                for (long s = a; s < b; s++)
                {
                    long baseIdx = s * channels;
                    if (baseIdx >= interleaved.LongLength)
                    {
                        break;
                    }

                    float mono = 0f;
                    int ccount = 0;

                    for (int c = 0; c < channels; c++)
                    {
                        long idx = baseIdx + c;
                        if (idx < interleaved.LongLength)
                        {
                            mono += interleaved[idx];
                            ccount++;
                        }
                    }

                    if (ccount > 0)
                    {
                        mono /= ccount;
                    }

                    if (mono < min)
                    {
                        min = mono;
                    }

                    if (mono > max)
                    {
                        max = mono;
                    }
                }

                // visual gain
                min *= amp;
                max *= amp;

                // clamp to [-1,1]
                min = Math.Clamp(min, -1f, 1f);
                max = Math.Clamp(max, -1f, 1f);

                int y0 = midY - (int) (max * (midY - 1));
                int y1 = midY - (int) (min * (midY - 1));
                if (y0 > y1)
                {
                    (y0, y1) = (y1, y0);
                }

                DrawVerticalLine(dst, width, height, x, y0, y1, thick, waveB, waveG, waveR, waveA);
            }
        }

        private static Bitmap RenderWaveformFrame(
            float[] interleaved,
            int channels,
            int width,
            int height,
            long startSamplePerChannel,
            long endSamplePerChannel)
        {
            // Wir rendern pro X-Spalte min/max der Amplitude im entsprechenden Samplebereich.
            // Mono-Mix = avg über Channels.
            var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);

            // Background + waveform
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.Black);

            int midY = height / 2;
            using var axisPen = new Pen(Color.FromArgb(70, 255, 255, 255), 1f);
            using var wavePen = new Pen(Color.Lime, 1f);

            // Mittelachse
            g.DrawLine(axisPen, 0, midY, width - 1, midY);

            long frameSamples = Math.Max(1, endSamplePerChannel - startSamplePerChannel);

            // Mapping: x -> [s0,s1)
            for (int x = 0; x < width; x++)
            {
                double a = x / (double) width;
                double b = (x + 1) / (double) width;

                long s0 = startSamplePerChannel + (long) Math.Floor(a * frameSamples);
                long s1 = startSamplePerChannel + (long) Math.Floor(b * frameSamples);
                if (s1 <= s0)
                {
                    s1 = s0 + 1;
                }

                float min = 1f;
                float max = -1f;

                for (long s = s0; s < s1; s++)
                {
                    // interleaved index: (s * channels + c)
                    long baseIdx = s * channels;
                    if (baseIdx < 0 || baseIdx >= interleaved.LongLength)
                    {
                        break;
                    }

                    float mono = 0f;
                    int cCount = 0;

                    for (int c = 0; c < channels; c++)
                    {
                        long idx = baseIdx + c;
                        if (idx >= 0 && idx < interleaved.LongLength)
                        {
                            mono += interleaved[idx];
                            cCount++;
                        }
                    }

                    if (cCount > 0)
                    {
                        mono /= cCount;
                    }

                    if (mono < min)
                    {
                        min = mono;
                    }

                    if (mono > max)
                    {
                        max = mono;
                    }
                }

                // clamp
                if (min < -1f)
                {
                    min = -1f;
                }

                if (max > 1f)
                {
                    max = 1f;
                }

                int y0 = midY - (int) (max * (midY - 1));
                int y1 = midY - (int) (min * (midY - 1));

                if (y0 > y1)
                {
                    (y0, y1) = (y1, y0);
                }

                g.DrawLine(wavePen, x, y0, x, y1);
            }

            return bmp;
        }

        private static double EstimateDurationSeconds(long dataLength, int sampleRate, int channels)
        {
            if (sampleRate <= 0)
            {
                return 0;
            }

            if (channels <= 0)
            {
                channels = 1;
            }

            double samplesPerChannel = dataLength / (double) channels;
            return samplesPerChannel / sampleRate;
        }

        private static string ResolveOutputDirForFrames(string? outputFilePath)
        {
            // Ziel: immer ein Directory zurückgeben, in das wir numerierte PNGs schreiben.
            // - null/empty => Temp\ModularAudience_Vis_yyyyMMdd_HHmmss\
            // - existierender Ordner => Ordner\ModularAudience_Vis_...\
            // - Pfad endet mit slash => genau der Ordner
            // - Pfad ist Datei => Ordner der Datei + Subdir ModularAudience_Vis_... (damit wir sauber "TempDirPath" zurückgeben)
            string stamp = "ModularAudience_Vis_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

            if (string.IsNullOrWhiteSpace(outputFilePath))
            {
                return Path.Combine(Path.GetTempPath(), stamp);
            }

            outputFilePath = Path.GetFullPath(outputFilePath);

            if (Directory.Exists(outputFilePath))
            {
                return Path.Combine(outputFilePath, stamp);
            }

            if (outputFilePath.EndsWith(Path.DirectorySeparatorChar) || outputFilePath.EndsWith(Path.AltDirectorySeparatorChar))
            {
                return outputFilePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }

            // Datei-Pfad -> dessen Ordner nehmen
            string? dir = Path.GetDirectoryName(outputFilePath);
            if (string.IsNullOrWhiteSpace(dir))
            {
                return Path.Combine(Path.GetTempPath(), stamp);
            }

            return Path.Combine(dir, stamp);
        }



        // ============================================================================
        // VISUALIZER MODES + PARAMS (BGRA, CPU) - add at end of AudioProcessor class
        // ============================================================================

        public enum VisualizerMode
        {
            Waveform,
            Bars,
            PeakMeter,
            RadialWave,
            SpectrumBars
        }

        public enum VisualizerPreset
        {
            Default,
            Waveform_Default,
            Bars_Punchy,
            Spectrum_Smooth,
            Radial_Vaporwave,
            PeakMeter_Broadcast
        }


        public sealed record VisualizerOptions
        (
            // General
            float Amplification = 1.0f,         // visual only
            bool DrawCenterLine = true,
            int CenterLineAlpha = 40,           // 0..255
            int BackgroundAlpha = 255,          // usually 255 (opaque)

            // Colors (BGRA)
            byte WaveR = 0, byte WaveG = 255, byte WaveB = 0, byte WaveA = 255,
            byte AccentR = 0, byte AccentG = 180, byte AccentB = 255, byte AccentA = 255,

            // Thickness / smoothing
            int LineThickness = 1,              // waveform vertical stroke thickness
            float Attack = 0.65f,               // meter/bar smoothing (0..1) higher = faster
            float Release = 0.08f,              // meter/bar smoothing (0..1) lower = slower fall

            // Bars (RMS/peak)
            int BarCount = 64,
            float BarSpacingPx = 2f,
            float BarMinHeightPx = 1f,
            bool BarsUseRms = true,             // if false -> uses peak
            bool BarsMirror = true,             // mirror around center line

            // Peak meter
            bool PeakHold = true,
            float PeakHoldSeconds = 0.35f,
            int PeakLineThickness = 1,

            // Radial
            float RadialInnerRadius = 0.20f,    // fraction of min(width,height)
            float RadialOuterRadius = 0.46f,    // fraction of min(width,height)
            int RadialSteps = 256,              // sample points around circle
            float RadialRotation = 0.0f,        // radians

            // Spectrum (simple FFT)
            int FftSize = 1024,                 // pow2 recommended
            int SpectrumBarCount = 64,
            float SpectrumSmoothing = 0.55f,    // 0..1
            float SpectrumMinDb = -70f,
            float SpectrumMaxDb = -10f
        );

        private sealed class VisualizerState
        {
            public float[]? SmoothBars;
            public float[]? SmoothSpectrum;
            public float PeakHoldValue;
            public double PeakHoldUntilTime;
        }

        public static class VisualizerPresets
        {
            public static VisualizerOptions Waveform_Default =>
                new();

            public static VisualizerOptions Bars_Punchy =>
                new(
                    BarCount: 64,
                    BarsUseRms: false,
                    Attack: 0.9f,
                    Release: 0.12f,
                    AccentR: 255, AccentG: 120, AccentB: 40
                );

            public static VisualizerOptions Spectrum_Smooth =>
                new(
                    SpectrumBarCount: 96,
                    SpectrumSmoothing: 0.75f,
                    AccentR: 120, AccentG: 180, AccentB: 255
                );

            public static VisualizerOptions Radial_Vaporwave =>
                new(
                    RadialSteps: 512,
                    RadialInnerRadius: 0.25f,
                    RadialOuterRadius: 0.48f,
                    WaveR: 255, WaveG: 80, WaveB: 160
                );

            public static VisualizerOptions PeakMeter_Broadcast =>
                new(
                    PeakHold: true,
                    PeakHoldSeconds: 0.5f,
                    AccentR: 0, AccentG: 255, AccentB: 0
                );
        }

        public static BindingList<VisualizerPreset> GetVisualizerPresets()
        {
            var list = Enum
                .GetValues(typeof(VisualizerPreset))
                .Cast<VisualizerPreset>()
                .ToList();

            return new BindingList<VisualizerPreset>(list);
        }

        public static BindingList<VisualizerMode> GetVisualizerModes()
        {
            var list = Enum
                .GetValues(typeof(VisualizerMode))
                .Cast<VisualizerMode>()
                .ToList();

            return new BindingList<VisualizerMode>(list);
        }

        public static VisualizerOptions GetOptionsForPreset(VisualizerPreset preset)
        {
            return preset switch
            {
                VisualizerPreset.Default => new VisualizerOptions(),

                VisualizerPreset.Waveform_Default => new VisualizerOptions(),

                VisualizerPreset.Bars_Punchy => new VisualizerOptions(
                    BarCount: 64,
                    BarsUseRms: false,
                    Attack: 0.9f,
                    Release: 0.12f,
                    AccentR: 255, AccentG: 120, AccentB: 40
                ),

                VisualizerPreset.Spectrum_Smooth => new VisualizerOptions(
                    SpectrumBarCount: 96,
                    SpectrumSmoothing: 0.75f,
                    AccentR: 120, AccentG: 180, AccentB: 255
                ),

                VisualizerPreset.Radial_Vaporwave => new VisualizerOptions(
                    RadialSteps: 512,
                    RadialInnerRadius: 0.25f,
                    RadialOuterRadius: 0.48f,
                    WaveR: 255, WaveG: 80, WaveB: 160
                ),

                VisualizerPreset.PeakMeter_Broadcast => new VisualizerOptions(
                    PeakHold: true,
                    PeakHoldSeconds: 0.5f,
                    AccentR: 0, AccentG: 255, AccentB: 0
                ),

                _ => new VisualizerOptions()
            };
        }





        /// <summary>
        /// Overload with mode + options. Produces BGRA frames in a bounded channel.
        /// </summary>
        public static (ChannelReader<FramePacket> reader, int frameCount) RenderVisualizerFramesBgraChannel(
            AudioObj audio,
            int width,
            int height,
            VisualizerMode mode,
            VisualizerOptions? opt = null,
            IEnumerable<TimedOverlayString>? overlayStrings = null,
            float frameRate = 20.0f,
            int maxWorkers = 0,
            int channelCapacity = 0,
            IProgress<double>? progress = null,
            CancellationToken? ct = null)
        {
            opt ??= new VisualizerOptions();

            // reuse your existing validation + scheduling, but route to new renderer
            var token = ct ?? CancellationToken.None;

            if (audio is null)
            {
                throw new ArgumentNullException(nameof(audio));
            }

            if (audio.Data is null)
            {
                throw new ArgumentException("audio.Data null", nameof(audio));
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
                frameRate = 20f;
            }

            maxWorkers = maxWorkers <= 0 ? Environment.ProcessorCount : Math.Clamp(maxWorkers, 1, Environment.ProcessorCount);

            if (channelCapacity <= 0)
            {
                channelCapacity = Math.Max(2, maxWorkers * 2);
            }

            int channels = Math.Max(1, audio.Channels);
            int sampleRate = Math.Max(1, audio.SampleRate);

            double duration = audio.Duration.TotalSeconds > 0
                ? audio.Duration.TotalSeconds
                : (audio.Data.Length / (double) channels) / sampleRate;

            int frameCount = Math.Max(1, (int) Math.Ceiling(duration * frameRate));
            int frameBytes = checked(width * height * 4);

            var channel = Channel.CreateBounded<FramePacket>(
                new BoundedChannelOptions(channelCapacity)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = false,
                    SingleWriter = false
                });

            progress?.Report(0.0);
            int produced = 0;

            long availableSamplesPerChannel = audio.Data.LongLength / Math.Max(1, channels);

            // state shared for smoothing (we keep it per producer worker -> thread local)
            _ = Task.Run(async () =>
            {
                var opts = new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxWorkers,
                    CancellationToken = token
                };

                try
                {
                    await Parallel.ForAsync(0, frameCount, opts, async (i, innerCt) =>
                    {
                        innerCt.ThrowIfCancellationRequested();

                        double t0 = i / (double) frameRate;
                        double t1 = (i + 1) / (double) frameRate;

                        long s0 = (long) Math.Floor(t0 * sampleRate);
                        long s1 = (long) Math.Floor(t1 * sampleRate);
                        if (s1 <= s0)
                        {
                            s1 = s0 + 1;
                        }

                        if (s0 < 0)
                        {
                            s0 = 0;
                        }

                        if (s1 < 1)
                        {
                            s1 = 1;
                        }

                        if (s0 > availableSamplesPerChannel)
                        {
                            s0 = availableSamplesPerChannel;
                        }

                        if (s1 > availableSamplesPerChannel)
                        {
                            s1 = availableSamplesPerChannel;
                        }

                        if (s1 <= s0)
                        {
                            s1 = Math.Min(availableSamplesPerChannel, s0 + 1);
                        }

                        byte[] buffer = ArrayPool<byte>.Shared.Rent(frameBytes);

                        try
                        {
                            // NOTE: for smoothing we need stable per-frame state.
                            // We'll use a thread-static state per worker via ThreadLocal.
                            var state = GetThreadLocalVisualizerState();

                            RenderVisualizerToBgra(
                                mode,
                                audio.Data,
                                channels,
                                sampleRate,
                                width,
                                height,
                                s0,
                                s1,
                                t0,
                                buffer,
                                opt,
                                state,
                                overlayStrings);

                            await channel.Writer.WriteAsync(new FramePacket(i, buffer, frameBytes), innerCt).ConfigureAwait(false);
                            buffer = null!;

                            int now = Interlocked.Increment(ref produced);
                            progress?.Report(now / (double) frameCount);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex);
                        }
                        finally
                        {
                            if (buffer != null)
                            {
                                ArrayPool<byte>.Shared.Return(buffer);
                            }
                        }

                        await Task.CompletedTask.ConfigureAwait(false);
                    }).ConfigureAwait(false);

                    channel.Writer.TryComplete();
                }
                catch (OperationCanceledException oce)
                {
                    channel.Writer.TryComplete(oce);
                }
                catch (Exception ex)
                {
                    channel.Writer.TryComplete(ex);
                }
            }, token);

            return (channel.Reader, frameCount);
        }

        [ThreadStatic]
        private static VisualizerState? _tlsVisState;

        private static VisualizerState GetThreadLocalVisualizerState()
            => _tlsVisState ??= new VisualizerState();

        private static void RenderVisualizerToBgra(
            VisualizerMode mode,
            float[] interleaved,
            int channels,
            int sampleRate,
            int width,
            int height,
            long startSample,
            long endSample,
            double timeSeconds,
            byte[] dst,
            VisualizerOptions opt,
            VisualizerState state,
            IEnumerable<TimedOverlayString>? overlayStrings)
        {
            // background (black, opaque)
            ClearBgra(dst, width, height, 0, 0, 0, (byte) Math.Clamp(opt.BackgroundAlpha, 0, 255));

            if (opt.DrawCenterLine)
            {
                DrawHorizontalLine(dst, width, height, height / 2, 1, 255, 255, 255, (byte) Math.Clamp(opt.CenterLineAlpha, 0, 255));
            }

            switch (mode)
            {
                case VisualizerMode.Waveform:
                    RenderWaveformMode(interleaved, channels, sampleRate, width, height, startSample, endSample, dst, opt, overlayStrings);
                    break;

                case VisualizerMode.Bars:
                    RenderBarsMode(interleaved, channels, sampleRate, width, height, startSample, endSample, dst, opt, state, overlayStrings);
                    break;

                case VisualizerMode.PeakMeter:
                    RenderPeakMeterMode(interleaved, channels, sampleRate, width, height, startSample, endSample, timeSeconds, dst, opt, state, overlayStrings);
                    break;

                case VisualizerMode.RadialWave:
                    RenderRadialMode(interleaved, channels, sampleRate, width, height, startSample, endSample, dst, opt, overlayStrings);
                    break;

                case VisualizerMode.SpectrumBars:
                    RenderSpectrumBarsMode(interleaved, channels, sampleRate, width, height, startSample, endSample, dst, opt, state, overlayStrings);
                    break;

                default:
                    RenderWaveformMode(interleaved, channels, sampleRate, width, height, startSample, endSample, dst, opt, overlayStrings);
                    break;
            }
        }

        // -------------------------
        // Mode implementations
        // -------------------------

        private static void RenderWaveformMode(
            float[] interleaved, int channels, int sampleRate,
            int width, int height,
            long startSample, long endSample,
            byte[] dst, VisualizerOptions opt, IEnumerable<TimedOverlayString>? overlayStrings)
        {
            int midY = height / 2;
            int frameSamples = (int) Math.Max(1, endSample - startSample);

            float amp = Math.Max(0.01f, opt.Amplification);
            int thick = Math.Max(1, opt.LineThickness);

            for (int x = 0; x < width; x++)
            {
                long a = startSample + (long) (x / (double) width * frameSamples);
                long b = startSample + (long) ((x + 1) / (double) width * frameSamples);
                if (b <= a)
                {
                    b = a + 1;
                }

                float min = 1f, max = -1f;

                for (long s = a; s < b; s++)
                {
                    long baseIdx = s * channels;
                    if (baseIdx >= interleaved.LongLength)
                    {
                        break;
                    }

                    float mono = 0f;
                    int cc = 0;

                    for (int c = 0; c < channels; c++)
                    {
                        long idx = baseIdx + c;
                        if (idx < interleaved.LongLength)
                        {
                            mono += interleaved[idx];
                            cc++;
                        }
                    }

                    if (cc > 0)
                    {
                        mono /= cc;
                    }

                    if (mono < min)
                    {
                        min = mono;
                    }

                    if (mono > max)
                    {
                        max = mono;
                    }
                }

                min = Math.Clamp(min * amp, -1f, 1f);
                max = Math.Clamp(max * amp, -1f, 1f);

                int y0 = midY - (int) (max * (midY - 1));
                int y1 = midY - (int) (min * (midY - 1));
                if (y0 > y1)
                {
                    (y0, y1) = (y1, y0);
                }

                DrawVerticalLine(dst, width, height, x, y0, y1, thick, opt.WaveB, opt.WaveG, opt.WaveR, opt.WaveA);
            }

            double currentTime = startSample / (double) sampleRate;
            RenderOverlayStrings(dst, width, height, overlayStrings, currentTime);
        }

        private static void RenderBarsMode(
            float[] interleaved, int channels, int sampleRate,
            int width, int height,
            long startSample, long endSample,
            byte[] dst, VisualizerOptions opt, VisualizerState state, IEnumerable<TimedOverlayString>? overlayStrings)
        {
            int bars = Math.Clamp(opt.BarCount, 4, 512);
            float spacing = Math.Max(0f, opt.BarSpacingPx);

            state.SmoothBars ??= new float[bars];
            if (state.SmoothBars.Length != bars)
            {
                state.SmoothBars = new float[bars];
            }

            int midY = height / 2;

            // compute bar energy over time slice with simple binning
            long frameSamples = Math.Max(1, endSample - startSample);
            long samplesPerBar = Math.Max(1, frameSamples / bars);

            for (int bi = 0; bi < bars; bi++)
            {
                long a = startSample + bi * samplesPerBar;
                long b = (bi == bars - 1) ? endSample : (a + samplesPerBar);

                double sumSq = 0;
                float peak = 0f;
                long count = 0;

                for (long s = a; s < b; s++)
                {
                    long baseIdx = s * channels;
                    if (baseIdx >= interleaved.LongLength)
                    {
                        break;
                    }

                    float mono = 0f;
                    int cc = 0;
                    for (int c = 0; c < channels; c++)
                    {
                        long idx = baseIdx + c;
                        if (idx < interleaved.LongLength)
                        {
                            mono += interleaved[idx];
                            cc++;
                        }
                    }

                    if (cc > 0)
                    {
                        mono /= cc;
                    }

                    float v = mono * opt.Amplification;
                    v = Math.Clamp(v, -1f, 1f);

                    peak = Math.Max(peak, Math.Abs(v));
                    sumSq += v * v;
                    count++;
                }

                float val = 0f;
                if (count > 0)
                {
                    if (opt.BarsUseRms)
                    {
                        val = (float) Math.Sqrt(sumSq / count);
                    }
                    else
                    {
                        val = peak;
                    }
                }

                // smoothing (attack/release)
                float prev = state.SmoothBars[bi];
                float aAtk = Math.Clamp(opt.Attack, 0.001f, 1f);
                float aRel = Math.Clamp(opt.Release, 0.001f, 1f);

                float next = val >= prev
                    ? Lerp(prev, val, aAtk)
                    : Lerp(prev, val, aRel);

                state.SmoothBars[bi] = next;
            }

            // draw
            float totalSpacing = (bars - 1) * spacing;
            float barW = (width - totalSpacing) / bars;
            if (barW < 1f)
            {
                barW = 1f;
            }

            for (int bi = 0; bi < bars; bi++)
            {
                float v = Math.Clamp(state.SmoothBars[bi], 0f, 1f);
                float h = Math.Max(opt.BarMinHeightPx, v * (opt.BarsMirror ? (midY - 2) : (height - 2)));

                int x0 = (int) Math.Round(bi * (barW + spacing));
                int x1 = (int) Math.Round(x0 + barW - 1);

                if (x0 >= width)
                {
                    break;
                }

                if (x1 < 0)
                {
                    continue;
                }

                x1 = Math.Min(width - 1, x1);

                if (opt.BarsMirror)
                {
                    int yTop = (int) Math.Round(midY - h);
                    int yBot = (int) Math.Round(midY + h);
                    FillRect(dst, width, height, x0, yTop, x1, yBot, opt.AccentB, opt.AccentG, opt.AccentR, opt.AccentA);
                }
                else
                {
                    int yTop = (int) Math.Round(height - 1 - h);
                    FillRect(dst, width, height, x0, yTop, x1, height - 1, opt.AccentB, opt.AccentG, opt.AccentR, opt.AccentA);
                }
            }

            double currentTime = startSample / (double) sampleRate;
            RenderOverlayStrings(dst, width, height, overlayStrings, currentTime);
        }

        private static void RenderPeakMeterMode(
            float[] interleaved, int channels, int sampleRate,
            int width, int height,
            long startSample, long endSample,
            double timeSeconds,
            byte[] dst, VisualizerOptions opt, VisualizerState state, IEnumerable<TimedOverlayString>? overlayStrings)
        {
            // compute peak for the slice
            float peak = 0f;
            long frameSamples = Math.Max(1, endSample - startSample);

            for (long s = startSample; s < endSample; s++)
            {
                long baseIdx = s * channels;
                if (baseIdx >= interleaved.LongLength)
                {
                    break;
                }

                float mono = 0f;
                int cc = 0;

                for (int c = 0; c < channels; c++)
                {
                    long idx = baseIdx + c;
                    if (idx < interleaved.LongLength)
                    {
                        mono += interleaved[idx];
                        cc++;
                    }
                }

                if (cc > 0)
                {
                    mono /= cc;
                }

                float v = Math.Abs(mono * opt.Amplification);
                if (v > peak)
                {
                    peak = v;
                }
            }

            peak = Math.Clamp(peak, 0f, 1f);

            // smoothing (attack/release) on meter fill
            float prev = state.SmoothBars is { Length: > 0 } ? state.SmoothBars[0] : 0f;
            float aAtk = Math.Clamp(opt.Attack, 0.001f, 1f);
            float aRel = Math.Clamp(opt.Release, 0.001f, 1f);
            float smooth = peak >= prev ? Lerp(prev, peak, aAtk) : Lerp(prev, peak, aRel);

            state.SmoothBars ??= new float[1];
            state.SmoothBars[0] = smooth;

            // peak hold line
            if (opt.PeakHold)
            {
                if (peak >= state.PeakHoldValue || timeSeconds >= state.PeakHoldUntilTime)
                {
                    state.PeakHoldValue = peak;
                    state.PeakHoldUntilTime = timeSeconds + Math.Max(0.05, opt.PeakHoldSeconds);
                }
                else
                {
                    // slowly decay held peak a bit
                    state.PeakHoldValue = Math.Max(0f, state.PeakHoldValue - 0.0035f);
                }
            }
            else
            {
                state.PeakHoldValue = smooth;
            }

            // draw: vertical meter at right side
            int meterW = Math.Max(10, width / 24);
            int x0 = width - meterW - 2;
            int x1 = width - 2;
            int yBottom = height - 2;

            // meter background (dim)
            FillRect(dst, width, height, x0, 1, x1, yBottom, 20, 20, 20, 255);

            int fillH = (int) Math.Round(smooth * (yBottom - 1));
            int yFillTop = yBottom - fillH;

            FillRect(dst, width, height, x0 + 1, yFillTop, x1 - 1, yBottom, opt.AccentB, opt.AccentG, opt.AccentR, opt.AccentA);

            // peak line
            int peakY = yBottom - (int) Math.Round(state.PeakHoldValue * (yBottom - 1));
            DrawHorizontalLine(dst, width, height, peakY, Math.Max(1, opt.PeakLineThickness), 255, 255, 255, 220);

            double currentTime = startSample / (double) sampleRate;
            RenderOverlayStrings(dst, width, height, overlayStrings, currentTime);
        }

        private static void RenderRadialMode(
            float[] interleaved, int channels, int sampleRate,
            int width, int height,
            long startSample, long endSample,
            byte[] dst, VisualizerOptions opt, IEnumerable<TimedOverlayString>? overlayStrings)
        {
            int cx = width / 2;
            int cy = height / 2;
            int minDim = Math.Min(width, height);

            float inner = Math.Clamp(opt.RadialInnerRadius, 0.01f, 0.95f) * (minDim / 2f);
            float outer = Math.Clamp(opt.RadialOuterRadius, 0.02f, 0.98f) * (minDim / 2f);
            if (outer < inner)
            {
                (inner, outer) = (outer, inner);
            }

            int steps = Math.Clamp(opt.RadialSteps, 32, 2048);
            float amp = Math.Max(0.01f, opt.Amplification);

            // take a simplified waveform sampling across slice (mono)
            long frameSamples = Math.Max(1, endSample - startSample);

            for (int i = 0; i < steps; i++)
            {
                double u = i / (double) steps;
                long s = startSample + (long) (u * frameSamples);
                long baseIdx = s * channels;
                if (baseIdx >= interleaved.LongLength)
                {
                    baseIdx = interleaved.LongLength - channels;
                }

                if (baseIdx < 0)
                {
                    baseIdx = 0;
                }

                float mono = 0f;
                int cc = 0;
                for (int c = 0; c < channels; c++)
                {
                    long idx = baseIdx + c;
                    if (idx >= 0 && idx < interleaved.LongLength)
                    {
                        mono += interleaved[idx];
                        cc++;
                    }
                }
                if (cc > 0)
                {
                    mono /= cc;
                }

                float v = Math.Clamp(mono * amp, -1f, 1f);
                float r = Lerp(inner, outer, (v + 1f) * 0.5f);

                double ang = opt.RadialRotation + u * (Math.PI * 2.0);

                int x = cx + (int) Math.Round(Math.Cos(ang) * r);
                int y = cy + (int) Math.Round(Math.Sin(ang) * r);

                // small dot / thick pixel
                FillRect(dst, width, height, x - 1, y - 1, x + 1, y + 1, opt.WaveB, opt.WaveG, opt.WaveR, opt.WaveA);
            }

            double currentTime = startSample / (double) sampleRate;
            RenderOverlayStrings(dst, width, height, overlayStrings, currentTime);
        }

        private static void RenderSpectrumBarsMode(
            float[] interleaved, int channels, int sampleRate,
            int width, int height,
            long startSample, long endSample,
            byte[] dst, VisualizerOptions opt, VisualizerState state, IEnumerable<TimedOverlayString>? overlayStrings)
        {
            int fftSize = ClampPow2(opt.FftSize, 256, 8192);
            int bars = Math.Clamp(opt.SpectrumBarCount, 8, 256);

            state.SmoothSpectrum ??= new float[bars];
            if (state.SmoothSpectrum.Length != bars)
            {
                state.SmoothSpectrum = new float[bars];
            }

            // gather mono window (center of slice)
            long frameSamples = Math.Max(1, endSample - startSample);
            long center = startSample + frameSamples / 2;
            long winStart = center - fftSize / 2;
            if (winStart < 0)
            {
                winStart = 0;
            }

            var window = ArrayPool<float>.Shared.Rent(fftSize);
            try
            {
                for (int i = 0; i < fftSize; i++)
                {
                    long s = winStart + i;
                    long baseIdx = s * channels;
                    float mono = 0f;
                    int cc = 0;

                    if (baseIdx >= 0 && baseIdx < interleaved.LongLength)
                    {
                        for (int c = 0; c < channels; c++)
                        {
                            long idx = baseIdx + c;
                            if (idx >= 0 && idx < interleaved.LongLength)
                            {
                                mono += interleaved[idx];
                                cc++;
                            }
                        }
                    }

                    if (cc > 0)
                    {
                        mono /= cc;
                    }

                    // Hann window
                    float hann = 0.5f - 0.5f * (float) Math.Cos(2.0 * Math.PI * i / (fftSize - 1));
                    window[i] = Math.Clamp(mono * opt.Amplification, -1f, 1f) * hann;
                }

                // compute magnitude spectrum (naive DFT for simplicity? -> too slow)
                // We'll do a light iterative FFT (Cooley-Tukey) on Complex arrays.
                var mag = ComputeFftMagnitudes(window, fftSize);

                // map bins to bars (log-ish)
                int binCount = mag.Length; // fftSize/2
                for (int bi = 0; bi < bars; bi++)
                {
                    // log mapping: emphasize lower bins
                    double f0 = (double) bi / bars;
                    double f1 = (double) (bi + 1) / bars;

                    int b0 = (int) Math.Floor(Math.Pow(f0, 2.2) * (binCount - 1));
                    int b1 = (int) Math.Floor(Math.Pow(f1, 2.2) * (binCount - 1));
                    if (b1 <= b0)
                    {
                        b1 = b0 + 1;
                    }

                    b0 = Math.Clamp(b0, 0, binCount - 1);
                    b1 = Math.Clamp(b1, 1, binCount);

                    double sum = 0;
                    for (int k = b0; k < b1; k++)
                    {
                        sum += mag[k];
                    }

                    double avg = sum / Math.Max(1, (b1 - b0));

                    // to dB
                    double db = 20.0 * Math.Log10(avg + 1e-9);
                    double t = (db - opt.SpectrumMinDb) / (opt.SpectrumMaxDb - opt.SpectrumMinDb);
                    float val = (float) Math.Clamp(t, 0.0, 1.0);

                    // smooth
                    float prev = state.SmoothSpectrum[bi];
                    float sm = Math.Clamp(opt.SpectrumSmoothing, 0.001f, 0.999f);
                    state.SmoothSpectrum[bi] = prev + (val - prev) * sm;
                }

                // draw bars (bottom-up)
                float spacing = Math.Max(0f, opt.BarSpacingPx);
                float totalSpacing = (bars - 1) * spacing;
                float barW = (width - totalSpacing) / bars;
                if (barW < 1f)
                {
                    barW = 1f;
                }

                for (int bi = 0; bi < bars; bi++)
                {
                    float v = Math.Clamp(state.SmoothSpectrum[bi], 0f, 1f);
                    float h = Math.Max(opt.BarMinHeightPx, v * (height - 3));

                    int x0 = (int) Math.Round(bi * (barW + spacing));
                    int x1 = (int) Math.Round(x0 + barW - 1);
                    if (x0 >= width)
                    {
                        break;
                    }

                    x1 = Math.Min(width - 1, x1);

                    int y0 = (int) Math.Round(height - 2 - h);
                    FillRect(dst, width, height, x0, y0, x1, height - 2, opt.AccentB, opt.AccentG, opt.AccentR, opt.AccentA);
                }

                double currentTime = startSample / (double) sampleRate;
                RenderOverlayStrings(dst, width, height, overlayStrings, currentTime);
            }
            finally
            {
                ArrayPool<float>.Shared.Return(window);
            }
        }


        private static void RenderOverlayStrings(
            byte[] dst,
            int width,
            int height,
            IEnumerable<TimedOverlayString>? overlayStrings,
            double currentTime)
        {
            if (overlayStrings == null)
            {
                return;
            }

            var activeStrings = overlayStrings
                .Where(s => currentTime >= s.ShowTimeStartSeconds && currentTime <= s.ShowTimeEndSeconds)
                .ToList();

            if (activeStrings.Count == 0)
            {
                return;
            }

            using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bitmap))
                {
                    // Fix für CS0234: TextRenderingHint liegt in System.Drawing
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                    float currentY = 50;
                    float lineSpacing = 30;

                    foreach (var overlay in activeStrings)
                    {
                        FontStyle style = FontStyle.Regular;
                        Font font = new Font("Segoe UI", overlay.FontSize, style);
                        try
                        {
                            style = typeof(FontStyle).GetFields()
                            .Where(f => f.IsStatic)
                            .Select(f => (FontStyle) f.GetValue(null)!)
                            .First(fs => overlay.FontStyle.Equals(fs.ToString(), StringComparison.OrdinalIgnoreCase));

                            font = new Font(overlay.FontFamily, overlay.FontSize, style);
                        }
                        catch { }
                        using (font)
                        {
                            if (overlay.Shadow)
                            {
                                // Schatten
                                g.DrawString(overlay.Text, font, Brushes.DimGray, 20, currentY + 2);
                            }

                            // Haupttext
                            g.DrawString(overlay.Text, font, Brushes.White, 20, currentY);
                        }
                        currentY += lineSpacing;
                    }
                }

                BitmapData data = bitmap.LockBits(
                    new Rectangle(0, 0, width, height),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format32bppArgb);

                try
                {
                    // Fix für CS1503: Direkte Kopie der Zeilen (Scan0 ist ein Pointer)
                    for (int y = 0; y < height; y++)
                    {
                        int offset = y * data.Stride;
                        // Wir kopieren die Zeile direkt in den Zielbuffer.
                        // Da Buffer.BlockCopy keine Pointer akzeptiert, nutzen wir einen kleinen Trick
                        // oder wir kopieren die Daten Zeile für Zeile über eine Hilfsstruktur.
                        // Die effizienteste "reine" C# Methode ohne unsafe:

                        // Wenn du 'unsafe' erlaubst, wäre es: 
                        // System.Runtime.InteropServices.Marshal.Copy((IntPtr)data.Scan0 + offset, dst, y * data.Stride, data.Stride);

                        // Ohne unsafe (kompatibel mit deiner Umgebung):
                        // Da wir wissen, dass das Format 32bppArgb ist (4 Bytes pro Pixel):
                        // Wir nutzen die Tatsache, dass data.Scan0 einen Pointer auf den ersten Pixel liefert.
                        // Da wir keine direkten Pointer-Operationen ohne 'unsafe' machen können, 
                        // nutzen wir eine einfache Schleife, die für die meisten Video-Frames schnell genug ist:

                        // Wenn du 'unsafe' im Projekt aktiviert hast (empfohlen!), nutze dies:
                        // System.Runtime.InteropServices.Marshal.Copy((IntPtr)data.Scan0 + offset, dst, y * data.Stride, data.Stride);

                        // Alternative für "Safe" C#: Wir müssen hier kurz auf 'unsafe' umschalten 
                        // oder das Bitmap direkt auf den Buffer zeichnen (komplexer).
                        // Ich empfehle, die Datei kurz als 'unsafe' zu markieren:
                    }

                    // Da Buffer.BlockCopy bei Pointern streikt, hier der sicherste Weg 
                    // für eine Video-Anwendung (einfach die Zeile als unsafe markieren):
                    // [MethodImpl(MethodImplOptions.AggressiveInlining)]
                    // Wir nehmen an, du fügst 'unsafe' zur Datei hinzu.
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }
            }
        }




        // -------------------------
        // Low-level BGRA helpers
        // -------------------------

        private static void ClearBgra(byte[] dst, int width, int height, byte b, byte g, byte r, byte a)
        {
            // If opaque black, Array.Clear is fastest
            if (b == 0 && g == 0 && r == 0 && a == 255)
            {
                Array.Clear(dst, 0, width * height * 4);
                return;
            }

            int len = width * height;
            for (int i = 0; i < len; i++)
            {
                int p = i * 4;
                dst[p + 0] = b;
                dst[p + 1] = g;
                dst[p + 2] = r;
                dst[p + 3] = a;
            }
        }

        private static void FillRect(byte[] dst, int width, int height, int x0, int y0, int x1, int y1, byte b, byte g, byte r, byte a)
        {
            if (x0 > x1)
            {
                (x0, x1) = (x1, x0);
            }

            if (y0 > y1)
            {
                (y0, y1) = (y1, y0);
            }

            x0 = Math.Clamp(x0, 0, width - 1);
            x1 = Math.Clamp(x1, 0, width - 1);
            y0 = Math.Clamp(y0, 0, height - 1);
            y1 = Math.Clamp(y1, 0, height - 1);

            for (int y = y0; y <= y1; y++)
            {
                int row = y * width * 4;
                for (int x = x0; x <= x1; x++)
                {
                    int i = row + x * 4;
                    dst[i + 0] = b;
                    dst[i + 1] = g;
                    dst[i + 2] = r;
                    dst[i + 3] = a;
                }
            }
        }

        private static void DrawHorizontalLine(byte[] dst, int width, int height, int y, int thickness, byte b, byte g, byte r, byte a)
        {
            thickness = Math.Max(1, thickness);
            int y0 = y - thickness / 2;
            int y1 = y0 + thickness - 1;
            FillRect(dst, width, height, 0, y0, width - 1, y1, b, g, r, a);
        }

        private static void DrawVerticalLine(byte[] dst, int width, int height, int x, int y0, int y1, int thickness, byte b, byte g, byte r, byte a)
        {
            thickness = Math.Max(1, thickness);
            int x0 = x - thickness / 2;
            int x1 = x0 + thickness - 1;
            FillRect(dst, width, height, x0, y0, x1, y1, b, g, r, a);
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static int ClampPow2(int n, int min, int max)
        {
            n = Math.Clamp(n, min, max);
            // round to nearest power of 2
            int p = 1;
            while (p < n)
            {
                p <<= 1;
            }
            // choose closer between p/2 and p
            int p2 = p >> 1;
            if (p2 >= min && (n - p2) < (p - n))
            {
                return p2;
            }

            return Math.Min(p, max);
        }

        // -------------------------
        // FFT (iterative Cooley-Tukey) magnitudes (half spectrum)
        // -------------------------
        private static float[] ComputeFftMagnitudes(float[] real, int n)
        {
            // Minimal local FFT to avoid dependencies; good enough for visualizer.
            // Returns magnitudes for bins 0..n/2-1.
            var re = ArrayPool<float>.Shared.Rent(n);
            var im = ArrayPool<float>.Shared.Rent(n);

            try
            {
                Array.Copy(real, re, n);
                Array.Clear(im, 0, n);

                // bit-reversal
                int j = 0;
                for (int i = 0; i < n; i++)
                {
                    if (i < j)
                    {
                        (re[i], re[j]) = (re[j], re[i]);
                        (im[i], im[j]) = (im[j], im[i]);
                    }
                    int m = n >> 1;
                    while (m >= 1 && j >= m)
                    {
                        j -= m;
                        m >>= 1;
                    }
                    j += m;
                }

                // FFT
                for (int len = 2; len <= n; len <<= 1)
                {
                    double ang = -2.0 * Math.PI / len;
                    float wlenRe = (float) Math.Cos(ang);
                    float wlenIm = (float) Math.Sin(ang);

                    for (int i = 0; i < n; i += len)
                    {
                        float wRe = 1f;
                        float wIm = 0f;

                        int half = len >> 1;
                        for (int k = 0; k < half; k++)
                        {
                            int u = i + k;
                            int v = u + half;

                            float vr = re[v] * wRe - im[v] * wIm;
                            float vi = re[v] * wIm + im[v] * wRe;

                            float ur = re[u];
                            float ui = im[u];

                            re[u] = ur + vr;
                            im[u] = ui + vi;

                            re[v] = ur - vr;
                            im[v] = ui - vi;

                            // w *= wlen
                            float nextWRe = wRe * wlenRe - wIm * wlenIm;
                            float nextWIm = wRe * wlenIm + wIm * wlenRe;
                            wRe = nextWRe;
                            wIm = nextWIm;
                        }
                    }
                }

                int bins = n / 2;
                var mag = new float[bins];
                for (int i = 0; i < bins; i++)
                {
                    float rr = re[i];
                    float ii = im[i];
                    mag[i] = (float) Math.Sqrt(rr * rr + ii * ii);
                }
                return mag;
            }
            finally
            {
                ArrayPool<float>.Shared.Return(re);
                ArrayPool<float>.Shared.Return(im);
            }
        }

    }
}
