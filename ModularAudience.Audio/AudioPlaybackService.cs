using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        // Loop config (array-based playback)
        private bool loopEnabled;
        private long loopStartSamples;
        private long loopEndSamples;
        private long loopActivationSamples; // absolute sample counter snapshot when loop was activated

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
                long bytes = this.player.GetPosition();
                this.loopActivationSamples = bytes / sizeof(float);
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
                try { currentBytes = this.player.GetPosition(); } catch { }
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
            this.waveProvider = new SampleToWaveProvider(this.volumeControl);
            this.player.Init(this.waveProvider);

            await Task.Run(() => this.player.Play());
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

            this.PlaybackRate = factor;

            // Neue Pipeline auf Basis derselben Quelle aufbauen und atomar umschalten
            ISampleProvider? currentSource;
            lock (this.graphGate)
            {
                currentSource = this.pipeline; // aktuelle Pipeline-Quelle ist die erste Stufe der Kette
            }
            if (currentSource == null)
            {
                return;
            }

            // currentSource ist bereits das Ergebnis vorheriger Resampler.
            // Baue die Pipeline neu basierend auf der ursprünglichen Quelle, wenn möglich.
            ISampleProvider baseSource = this.reader ?? currentSource;
            var newPipeline = BuildPipeline(baseSource, this.PlaybackRate, this.switching.WaveFormat);
            lock (this.graphGate)
            {
                this.pipeline = newPipeline;
                this.switching.SetCurrent(this.pipeline);
            }

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
        }

        // Graph aufbauen: Quelle -> (Resample auf R*f) -> (Resample auf DeviceRate) -> konstant D
        private static ISampleProvider BuildPipeline(ISampleProvider source, double rate, WaveFormat deviceFormat)
        {
            //1) Quelle ggf. auf "virtuelle" Abtastrate R * rate bringen (erzeugt Varispeed-Effekt)
            int sourceRate = source.WaveFormat.SampleRate;
            int channels = source.WaveFormat.Channels;

            // WdlResamplingSampleProvider erzeugt einen Provider mit neuem WaveFormat (SampleRate)
            var spedUp = new WdlResamplingSampleProvider(source, Math.Max(8000, (int) Math.Round(sourceRate * rate)));

            //2) Auf die konstante Device-Rate zurück resamplen
            var toDevice = new WdlResamplingSampleProvider(spedUp, deviceFormat.SampleRate);

            // Sicherheitscheck: Kanäle konsistent halten
            if (toDevice.WaveFormat.Channels != deviceFormat.Channels)
            {
                if (deviceFormat.Channels == 1 && channels > 1)
                {
                    toDevice = new WdlResamplingSampleProvider(
                        new StereoToMonoSampleProvider(spedUp) { LeftVolume = 0.5f, RightVolume = 0.5f },
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
            this.player.Stop();
            this.reader?.Dispose();
            this.reader = null;
            this.switching = null;
            this.volumeControl = null;
            this.waveProvider = null;
            this.pipeline = null;
            this.rawData = null;
            this.rawSampleRate = 0;
            this.rawChannels = 0;
            // keep loop configuration; caller decides whether to clear
        }

        private void RebuildLoopPipeline(bool adjustPosition)
        {
            // Only rebuild if we are in array playback mode (rawData present) and pipeline initialized
            if (this.rawData == null || this.switching == null || this.pipeline == null)
            {
                return;
            }
            // Current absolute sample index based on bytes
            long currentBytes = 0;
            try { currentBytes = this.player.GetPosition(); } catch { currentBytes = 0; }
            int ch = Math.Max(1, this.rawChannels);
            long currentSamples = currentBytes / sizeof(float); // bytes from WaveOutEvent.GetPosition are device bytes; we treat as floats for simplicity
            // If loop enabled clamp; if outside and adjustPosition jump to loop start
            long startSampleIndex = currentSamples;
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
        }

        public void Dispose()
        {
            try { this.player.Stop(); } catch { }
            this.reader?.Dispose();
            this.reader = null;
            this.switching = null;
            this.volumeControl = null;
            this.waveProvider = null;
            this.pipeline = null;
            if (this.ownsPlayer)
            {
                this.player.Dispose();
            }
        }
    }
}