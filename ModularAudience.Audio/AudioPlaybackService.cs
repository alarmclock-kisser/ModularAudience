using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static ModularAudience.Audio.SwitchingSampleProvider;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace ModularAudience.Audio
{
    internal static class NativeMethods
    {
        public const int THREAD_PRIORITY_TIME_CRITICAL = 15;

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentThread();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetThreadPriority(IntPtr hThread, int nPriority);
    }

    internal sealed class SwitchingSampleProvider : ISampleProvider
    {
        private ISampleProvider? current;
        private readonly WaveFormat outputFormat;
        private readonly Lock gate = new();

        public SwitchingSampleProvider(WaveFormat outputFormat)
        {
            this.outputFormat = outputFormat ?? throw new ArgumentNullException(nameof(outputFormat));
        }

        public WaveFormat WaveFormat => this.outputFormat;

        public void SetCurrent(ISampleProvider? provider)
        {
            lock (this.gate)
            {
                this.current = provider;
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            // Attempt to give the audio callback OS-level time-critical priority to avoid preemption
            try
            {
                // THREAD_PRIORITY_TIME_CRITICAL == 15
                NativeMethods.SetThreadPriority(NativeMethods.GetCurrentThread(), NativeMethods.THREAD_PRIORITY_TIME_CRITICAL);
            }
            catch { }

            ISampleProvider? p;
            lock (this.gate) { p = this.current; }
            if (p == null) { Array.Clear(buffer, offset, count); return count; }
            return p.Read(buffer, offset, count);
        }
    }

    internal sealed class RateAdjustedSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider source;

        public RateAdjustedSampleProvider(ISampleProvider source, double rate)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            int adjustedRate = Math.Max(8000, (int) Math.Round(this.source.WaveFormat.SampleRate * Math.Clamp(rate, 0.5, 2.0)));
            this.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(adjustedRate, this.source.WaveFormat.Channels);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            return this.source.Read(buffer, offset, count);
        }
    }

    // Einfacher Provider, der aus einem Float-Array (interleaved) liest
    internal class ArraySampleProvider : ISampleProvider
    {
        protected readonly float[] data;
        protected long position; // in Samples (floats)
        public WaveFormat WaveFormat { get; }

        public ArraySampleProvider(float[] data, int sampleRate, int channels, long startSampleIndex = 0)
        {
            this.data = data ?? throw new ArgumentNullException(nameof(data));
            if (channels <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(channels));
            }

            if (sampleRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            }

            this.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
            this.position = Math.Clamp(startSampleIndex, 0, data.LongLength);
        }

        public virtual int Read(float[] buffer, int offset, int count)
        {
            int samplesAvailable = (int) Math.Min(count, this.data.LongLength - this.position);
            if (samplesAvailable <= 0)
            {
                Array.Clear(buffer, offset, count);
                return 0;
            }
            Array.Copy(this.data, (int) this.position, buffer, offset, samplesAvailable);
            this.position += samplesAvailable;
            return samplesAvailable;
        }
    }

    // Provider, der einen Bereich [loopStart, loopEnd) endlos looped
    internal sealed class LoopingArraySampleProvider : ArraySampleProvider
    {
        private readonly long loopStart; // inclusive
        private readonly long loopEnd;   // exclusive

        public LoopingArraySampleProvider(float[] data, int sampleRate, int channels, long loopStartSampleIndex, long loopEndSampleIndex, long startSampleIndex)
            : base(data, sampleRate, channels, 0)
        {
            long len = data.LongLength;
            this.loopStart = Math.Clamp(loopStartSampleIndex, 0, len);
            this.loopEnd = Math.Clamp(loopEndSampleIndex, this.loopStart + 1, len);
            long start = Math.Clamp(startSampleIndex, this.loopStart, this.loopEnd - 1);
            this.position = start;
        }

        public override int Read(float[] buffer, int offset, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            int written = 0;
            long len = this.data.LongLength;
            if (this.loopStart < 0 || this.loopEnd <= this.loopStart || this.loopStart >= len)
            {
                return base.Read(buffer, offset, count);
            }

            while (written < count)
            {
                // Wenn Position außerhalb des Loop-Bereichs: auf Loop-Start springen
                if (this.position < this.loopStart || this.position >= this.loopEnd)
                {
                    this.position = this.loopStart;
                }
                long remainingInLoop = this.loopEnd - this.position;
                if (remainingInLoop <= 0)
                {
                    this.position = this.loopStart;
                    continue;
                }
                int toCopy = (int) Math.Min(count - written, remainingInLoop);
                Array.Copy(this.data, (int) this.position, buffer, offset + written, toCopy);
                this.position += toCopy;
                written += toCopy;
            }
            return written;
        }
    }

    public sealed class AudioPlaybackService : IDisposable
    {
        // Gate to serialize operations that touch the underlying WaveOutEvent
        private readonly object playerGate = new();

        private readonly WaveOutEvent player; // von außen injiziert oder intern erzeugt
        private readonly bool ownsPlayer;
        private AudioFileReader? reader; // float32 Quelle (Datei) optional
        private SwitchingSampleProvider? switching; // konstanter Output (Geräteformat)
        private VolumeSampleProvider? volumeControl; // per-instance volume control
        private SampleToWaveProvider? waveProvider; // für WaveOutEvent.Init
        private ISampleProvider? pipeline; // aktuelle (resampled) Pipeline
        private readonly Lock graphGate = new();
        private float[]? rawData; // store original data for seeking while paused
        private int rawSampleRate;
        private int rawChannels;
        private long positionOriginOutputSamples;
        private double positionOriginSourceSamples;

        // Loop config (array-based playback)
        private bool loopEnabled;
        private long loopStartSamples;
        private long loopEndSamples;
        private long loopActivationSamples; // absolute sample counter snapshot when loop was activated
        public static float MasterLimiter;

        public float PlaybackRate { get; private set; } = 1.0f;
        public int DeviceSampleRate { get; private set; } = 44100;
        public int Channels { get; private set; } = 2;

        public event EventHandler<StoppedEventArgs>? PlaybackStopped;

        public AudioPlaybackService()
        {
            this.player = new WaveOutEvent();
            this.ownsPlayer = true;
            this.player.PlaybackStopped += (s, e) => this.PlaybackStopped?.Invoke(this, e);
        }

        public AudioPlaybackService(WaveOutEvent player)
        {
            this.player = player ?? throw new ArgumentNullException(nameof(player));
            this.ownsPlayer = false;
            this.player.PlaybackStopped += (s, e) => this.PlaybackStopped?.Invoke(this, e);
        }

        public void SetLoop(long startSampleIndex, long endSampleIndex)
        {
            if (endSampleIndex <= startSampleIndex)
            {
                this.loopEnabled = false;
                this.RebuildLoopPipeline(adjustPosition: true);
                return;
            }
            this.loopEnabled = true;
            this.loopStartSamples = Math.Max(0, startSampleIndex);
            this.loopEndSamples = Math.Max(this.loopStartSamples + 1, endSampleIndex);

            // Snapshot absolute samples at activation for later modulo mapping
            try
            {
                lock (this.playerGate)
                {
                    long bytes = this.player.GetPosition();
                    this.loopActivationSamples = bytes / sizeof(float);
                }
            }
            catch { this.loopActivationSamples = 0; }

            this.RebuildLoopPipeline(adjustPosition: true);
        }

        public void ClearLoop(long? resumeSampleIndex = null)
        {
            long startSampleIndex;
            if (resumeSampleIndex.HasValue)
            {
                startSampleIndex = resumeSampleIndex.Value;
            }
            else if (this.rawData != null && this.switching != null)
            {
                long currentBytes = 0;
                try { lock (this.playerGate) { currentBytes = this.player.GetPosition(); } } catch { }
                long currentSamples = currentBytes / sizeof(float);

                long ls = Math.Max(0, this.loopStartSamples);
                long le = Math.Max(ls + 1, this.loopEndSamples);
                long loopLen = Math.Max(1, le - ls);

                long elapsedSinceActivation = Math.Max(0, currentSamples - this.loopActivationSamples);
                long offsetInLoop = elapsedSinceActivation % loopLen;
                startSampleIndex = ls + offsetInLoop;
            }
            else
            {
                startSampleIndex = 0;
            }

            // If loop already disabled we only honor explicit resume requests
            if (!this.loopEnabled)
            {
                if (resumeSampleIndex.HasValue)
                {
                    this.SeekSamples(Math.Max(0, startSampleIndex));
                }
                else
                {
                    this.RebuildLoopPipeline(adjustPosition: false);
                }
                return;
            }

            this.loopEnabled = false;

            if (this.rawData != null && this.switching != null)
            {
                long maxLen = this.rawData.LongLength;
                startSampleIndex = Math.Clamp(startSampleIndex, 0, maxLen);
                ISampleProvider source = this.CreateArraySource(startSampleIndex);
                var newPipeline = BuildPipeline(source, this.PlaybackRate, this.switching.WaveFormat);
                lock (this.graphGate)
                {
                    this.pipeline = newPipeline;
                    this.switching.SetCurrent(this.pipeline);
                }
            }

            this.loopStartSamples = 0;
            this.loopEndSamples = 0;
            this.loopActivationSamples = 0;

            if (resumeSampleIndex.HasValue && this.rawData == null)
            {
                this.SeekSamples(Math.Max(0, startSampleIndex));
            }
        }

        public async Task InitializePlayback(string filePath, int? deviceSampleRate = null, int desiredLatency = 50, float initialVolume = 1.0f)
        {
            this.ResetGraph();

            // Quelle laden (AudioFileReader liefert Float32, normalisiert)
            this.reader = new AudioFileReader(filePath);
            this.Channels = this.reader.WaveFormat.Channels;
            this.DeviceSampleRate = deviceSampleRate ?? this.reader.WaveFormat.SampleRate;

            // Konstantes Geräteformat
            var deviceFormat = WaveFormat.CreateIeeeFloatWaveFormat(this.DeviceSampleRate, this.Channels);

            // Umschaltbarer Provider mit konstantem Format
            this.switching = new SwitchingSampleProvider(deviceFormat);

            // Erste Pipeline (PlaybackRate =1.0)
            this.pipeline = BuildPipeline(this.reader, this.PlaybackRate, deviceFormat);
            this.switching.SetCurrent(this.pipeline);

            // Volume control wraps switching provider
            this.volumeControl = new VolumeSampleProvider(this.switching) { Volume = Math.Clamp(initialVolume, 0f, 1f) };

            // WaveOut initialisieren (falls noch nicht)
            // Increase internal buffering to reduce underruns on busy systems
            try
            {
                this.player.DesiredLatency = Math.Max(desiredLatency, 120);
                this.player.NumberOfBuffers = Math.Max(this.player.NumberOfBuffers, 4);
            }
            catch { }

            this.player.Volume = 1.0f; // keep device stream at unity, control via VolumeSampleProvider
            this.waveProvider = new SampleToWaveProvider(this.volumeControl);
            this.player.Init(this.waveProvider);

            // Start (nicht blocking)
            await Task.Run(() => this.player.Play());
            this.SetPositionMapping(0);
        }

        public async Task InitializePlayback(float[] data, int sampleRate, int channels, long startSampleIndex = 0, int? deviceSampleRate = null, int desiredLatency = 50, float initialVolume = 1.0f)
        {
            this.ResetGraph();

            this.rawData = data; // keep reference
            this.rawSampleRate = sampleRate;
            this.rawChannels = channels;

            this.Channels = channels;
            this.DeviceSampleRate = deviceSampleRate ?? sampleRate;

            // Quelle (loop-aware)
            ISampleProvider source = this.CreateArraySource(startSampleIndex);
            var deviceFormat = WaveFormat.CreateIeeeFloatWaveFormat(this.DeviceSampleRate, this.Channels);

            this.switching = new SwitchingSampleProvider(deviceFormat);
            this.pipeline = BuildPipeline(source, this.PlaybackRate, deviceFormat);
            this.switching.SetCurrent(this.pipeline);

            // Volume control wraps switching provider
            this.volumeControl = new VolumeSampleProvider(this.switching) { Volume = Math.Clamp(initialVolume, 0f, 1f) };

            try
            {
                this.player.DesiredLatency = Math.Max(desiredLatency, 120);
                this.player.NumberOfBuffers = Math.Max(this.player.NumberOfBuffers, 4);
            }
            catch { }

            this.player.Volume = 1.0f; // keep device stream at unity, control via VolumeSampleProvider

            try
            {
                if (this.volumeControl == null)
                {
                    // Defensive: ensure volumeControl exists
                    this.volumeControl = new VolumeSampleProvider(this.switching) { Volume = Math.Clamp(initialVolume, 0f, 1f) };
                }

                this.waveProvider = new SampleToWaveProvider(this.volumeControl);
                lock (this.playerGate)
                {
                    this.player.Init(this.waveProvider);
                    // Start playback (Play is non-blocking for WaveOutEvent)
                    this.player.Play();
                }
            }
            catch (Exception ex)
            {
                try { Debug.WriteLine($"Playback initialization failed: {ex.Message}"); } catch { }
                try { ModularAudience.Audio.LogCollection.Log($"AudioPlaybackService.InitializePlayback failed: {ex.Message}"); } catch { }
                // Bail out: leave Playing=false to caller and avoid crashing the thread.
                return;
            }
            this.SetPositionMapping(Math.Clamp(startSampleIndex, 0, data.LongLength));
        }

        private ISampleProvider CreateArraySource(long startSampleIndex)
        {
            if (this.rawData == null)
            {
                throw new InvalidOperationException("No raw data available for array source.");
            }
            if (this.loopEnabled)
            {
                long ls = Math.Clamp(this.loopStartSamples, 0, this.rawData.LongLength - 1);
                long le = Math.Clamp(this.loopEndSamples, ls + 1, this.rawData.LongLength);
                long start = Math.Clamp(startSampleIndex, ls, le - 1);
                return (ISampleProvider) new LoopingArraySampleProvider(this.rawData, this.rawSampleRate, this.rawChannels, ls, le, start);
            }
            else
            {
                return new ArraySampleProvider(this.rawData, this.rawSampleRate, this.rawChannels, startSampleIndex);
            }
        }

        // Nahtlose Anpassung der Geschwindigkeit (Pitch & Tempo ändern sich gemeinsam, "Varispeed")
        public async Task AdjustSampleRate(float factor)
        {
            if (this.switching == null)
            {
                return;
            }

            factor = Math.Clamp(factor, 0.5f, 2.0f);
            long currentOutputSamples = this.GetPlayerOutputSampleCount();
            long currentSourceSamples = this.GetCurrentSourceSampleIndex(currentOutputSamples);
            this.PlaybackRate = factor;

            ISampleProvider? newPipeline;
            if (this.rawData != null)
            {
                currentSourceSamples = this.ClampSourceSampleIndex(currentSourceSamples);
                ISampleProvider baseSource = this.CreateArraySource(currentSourceSamples);
                newPipeline = BuildPipeline(baseSource, this.PlaybackRate, this.switching.WaveFormat);
            }
            else
            {
                ISampleProvider? baseSource;
                lock (this.graphGate)
                {
                    baseSource = this.reader ?? this.pipeline;
                }
                if (baseSource == null)
                {
                    return;
                }
                newPipeline = BuildPipeline(baseSource, this.PlaybackRate, this.switching.WaveFormat);
            }

            lock (this.graphGate)
            {
                this.pipeline = newPipeline;
                this.switching.SetCurrent(this.pipeline);
            }
            this.SetPositionMapping(currentSourceSamples, currentOutputSamples);

            await Task.CompletedTask; // API bleibt async
        }

        // Seek in paused state without reinitializing WaveOut
        public void SeekSamples(long startSampleIndex)
        {
            if (this.switching == null || this.rawData == null || this.rawSampleRate <= 0 || this.rawChannels <= 0)
            {
                return;
            }
            // Clamp startSampleIndex
            if (this.loopEnabled)
            {
                startSampleIndex = Math.Clamp(startSampleIndex, this.loopStartSamples, Math.Max(this.loopStartSamples, this.loopEndSamples - 1));
            }
            else
            {
                startSampleIndex = Math.Clamp(startSampleIndex, 0, this.rawData.LongLength);
            }

            ISampleProvider source = this.CreateArraySource(startSampleIndex);
            var newPipeline = BuildPipeline(source, this.PlaybackRate, this.switching.WaveFormat);
            lock (this.graphGate)
            {
                this.pipeline = newPipeline;
                this.switching.SetCurrent(this.pipeline);
            }
            this.SetPositionMapping(startSampleIndex);
        }

        // Graph aufbauen: Quelle -> virtuelle Samplerate R*f -> Resample auf DeviceRate
        private static ISampleProvider BuildPipeline(ISampleProvider source, double rate, WaveFormat deviceFormat)
        {
            int channels = source.WaveFormat.Channels;
            var rateAdjusted = new RateAdjustedSampleProvider(source, rate);
            var toDevice = new WdlResamplingSampleProvider(rateAdjusted, deviceFormat.SampleRate);

            // Sicherheitscheck: Kanäle konsistent halten
            if (toDevice.WaveFormat.Channels != deviceFormat.Channels)
            {
                if (deviceFormat.Channels == 1 && channels > 1)
                {
                    toDevice = new WdlResamplingSampleProvider(
                        new StereoToMonoSampleProvider(rateAdjusted) { LeftVolume = 0.5f, RightVolume = 0.5f },
                        deviceFormat.SampleRate);
                }
                // sonst: beibehalten, typ. wandelt das Ausgabegerät im Shared-Mode
            }

            return toDevice;
        }

        public void Stop()
        {
            this.player?.Stop();
        }

        public void Pause()
        {
            if (this.player.PlaybackState == PlaybackState.Playing)
            {
                this.player.Pause();
            }
        }

        public void Resume()
        {
            if (this.player.PlaybackState == PlaybackState.Paused)
            {
                this.player.Play();
            }
        }

        public long GetPositionBytes()
        {
            try { return this.player.GetPosition(); } catch { return 0; }
        }

        public long GetSourceSamplePosition()
        {
            return this.GetCurrentSourceSampleIndex();
        }

        public void SwapRawData(float[] data, int sampleRate, int channels, long startSampleIndex)
        {
            if (data == null || data.Length == 0 || this.switching == null)
            {
                return;
            }

            this.rawData = data;
            this.rawSampleRate = sampleRate;
            this.rawChannels = channels;
            this.Channels = channels;

            startSampleIndex = Math.Clamp(startSampleIndex, 0, data.LongLength);
            ISampleProvider source = this.CreateArraySource(startSampleIndex);
            var newPipeline = BuildPipeline(source, this.PlaybackRate, this.switching.WaveFormat);
            lock (this.graphGate)
            {
                this.pipeline = newPipeline;
                this.switching.SetCurrent(this.pipeline);
            }
            this.SetPositionMapping(startSampleIndex);
        }

        public void SetVolume(float volume)
        {
            if (this.volumeControl != null)
            {
                this.volumeControl.Volume = Math.Clamp(volume, 0f, 1f);
            }
            else
            {
                // Fallback: set device stream volume if volume provider not yet created
                this.player.Volume = Math.Clamp(volume, 0f, 1f);
            }
        }

        private void ResetGraph()
        {
            lock (this.playerGate)
            {
                try { this.player.Stop(); } catch { }
                try { this.reader?.Dispose(); } catch { }
                this.reader = null;
                this.switching = null;
                this.volumeControl = null;
                this.waveProvider = null;
                this.pipeline = null;
                this.rawData = null;
                this.rawSampleRate = 0;
                this.rawChannels = 0;
                this.positionOriginOutputSamples = 0;
                this.positionOriginSourceSamples = 0;
            }
            // keep loop configuration; caller decides whether to clear
        }

        private void RebuildLoopPipeline(bool adjustPosition)
        {
            // Only rebuild if we are in array playback mode (rawData present) and pipeline initialized
            if (this.rawData == null || this.switching == null || this.pipeline == null)
            {
                return;
            }
            long currentOutputSamples = this.GetPlayerOutputSampleCount();
            long startSampleIndex = this.GetCurrentSourceSampleIndex(currentOutputSamples);
            if (this.loopEnabled)
            {
                long ls = Math.Clamp(this.loopStartSamples, 0, this.rawData.LongLength - 1);
                long le = Math.Clamp(this.loopEndSamples, ls + 1, this.rawData.LongLength);
                if (adjustPosition && (startSampleIndex < ls || startSampleIndex >= le))
                {
                    startSampleIndex = ls;
                }
                startSampleIndex = Math.Clamp(startSampleIndex, ls, le - 1);
            }
            else
            {
                startSampleIndex = Math.Clamp(startSampleIndex, 0, this.rawData.LongLength);
            }

            ISampleProvider newSource = this.CreateArraySource(startSampleIndex);
            var deviceFormat = this.switching.WaveFormat;
            var newPipeline = BuildPipeline(newSource, this.PlaybackRate, deviceFormat);
            lock (this.graphGate)
            {
                this.pipeline = newPipeline;
                this.switching.SetCurrent(this.pipeline);
            }
            this.SetPositionMapping(startSampleIndex, currentOutputSamples);
        }

        private long GetPlayerOutputSampleCount()
        {
            try { return Math.Max(0, this.player.GetPosition() / sizeof(float)); }
            catch { return Math.Max(0, this.positionOriginOutputSamples); }
        }

        private long GetCurrentSourceSampleIndex(long? outputSampleCount = null)
        {
            if (this.rawData == null)
            {
                return 0;
            }

            long currentOutputSamples = outputSampleCount ?? this.GetPlayerOutputSampleCount();
            long deltaOutputSamples = Math.Max(0, currentOutputSamples - this.positionOriginOutputSamples);
            double sourcePosition = this.positionOriginSourceSamples + (deltaOutputSamples * this.PlaybackRate);

            if (this.loopEnabled)
            {
                long ls = Math.Clamp(this.loopStartSamples, 0, this.rawData.LongLength - 1);
                long le = Math.Clamp(this.loopEndSamples, ls + 1, this.rawData.LongLength);
                long loopLen = Math.Max(1, le - ls);
                if (sourcePosition < ls)
                {
                    sourcePosition = ls;
                }
                else
                {
                    sourcePosition = ls + ((sourcePosition - ls) % loopLen);
                }
            }

            return this.ClampSourceSampleIndex((long) Math.Floor(sourcePosition));
        }

        private long ClampSourceSampleIndex(long sourceSampleIndex)
        {
            if (this.rawData == null)
            {
                return Math.Max(0, sourceSampleIndex);
            }

            if (this.loopEnabled)
            {
                long ls = Math.Clamp(this.loopStartSamples, 0, this.rawData.LongLength - 1);
                long le = Math.Clamp(this.loopEndSamples, ls + 1, this.rawData.LongLength);
                return Math.Clamp(sourceSampleIndex, ls, le - 1);
            }

            return Math.Clamp(sourceSampleIndex, 0, this.rawData.LongLength);
        }

        private void SetPositionMapping(long sourceSampleIndex, long? outputSampleCount = null)
        {
            this.positionOriginSourceSamples = Math.Max(0, sourceSampleIndex);
            this.positionOriginOutputSamples = outputSampleCount ?? this.GetPlayerOutputSampleCount();
        }

        public void Dispose()
        {
            lock (this.playerGate)
            {
                try { this.player.Stop(); } catch { }
                try { this.reader?.Dispose(); } catch { }
                this.reader = null;
                this.switching = null;
                this.volumeControl = null;
                this.waveProvider = null;
                this.pipeline = null;
                if (this.ownsPlayer)
                {
                    try { this.player.Dispose(); } catch { }
                }
            }
        }

        public static void SetMasterLimiter(float masterLimiter)
        {
            // NAudio's WaveOutEvent does not have a built-in master limiter, but we can simulate it by adjusting the volume of the output stream.
            // This is a global setting that affects all instances using the same output device.
            // Note: This is a workaround and may not be as effective as a true master limiter in preventing clipping, especially if individual track volumes are set high.
            float clampedLimiter = Math.Clamp((masterLimiter / 10f), 0f, 1f);
            try
            {
                // Set the volume for all WaveOutEvent instances (this is a simplification; in a real implementation, you might want to track instances or use a shared volume provider)
                // For demonstration purposes, we will just set the volume on the default output device.
                using (var tempPlayer = new WaveOutEvent())
                {
                    tempPlayer.Volume = clampedLimiter;
                }
            }
            catch { }

            MasterLimiter = clampedLimiter;
        }
    }
}