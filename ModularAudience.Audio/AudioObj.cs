using ModularAudience.Audio;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Drawing;

namespace ModularAudience.Audio
{
    public partial class AudioObj : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        // Core identity & metadata
        public Guid Id { get; init; } = Guid.NewGuid();
        public readonly DateTime CreatedAt = DateTime.UtcNow;

        private string name = string.Empty;
        public string Name
        {
            get => this.name;
            set => this.SetContentField(ref this.name, value);
        }

        private string originalName = string.Empty;
        public string OriginalName
        {
            get => this.originalName;
            set => this.SetContentField(ref this.originalName, value);
        }

        private string filePath = string.Empty;
        public string FilePath
        {
            get => this.filePath;
            set => this.SetContentField(ref this.filePath, value);
        }

        // Audio data & format
        private float[] data = [];
        public float[] Data
        {
            get => this.data;
            set => this.SetContentField(ref this.data, value);
        }

        private int sampleRate;
        public int SampleRate
        {
            get => this.sampleRate;
            set => this.SetContentField(ref this.sampleRate, value);
        }

        private double sampleRateFactor = 1.0;
        public double SampleRateFactor
        {
            get => this.sampleRateFactor;
            set => this.SetContentField(ref this.sampleRateFactor, value);
        }

        private double manualSampleRateFactor = 1.0;
        public double ManualSampleRateFactor
        {
            get => this.manualSampleRateFactor;
            set => this.SetContentField(ref this.manualSampleRateFactor, value);
        }

        private double syncNudgeSampleRateFactor = 1.0;
        public double SyncNudgeSampleRateFactor
        {
            get => this.syncNudgeSampleRateFactor;
            set => this.SetContentField(ref this.syncNudgeSampleRateFactor, value);
        }

        public int AdjustedSampleRate => (int)(this.SampleRate * this.SampleRateFactor);

        private int channels;
        public int Channels
        {
            get => this.channels;
            set => this.SetContentField(ref this.channels, value);
        }

        private int bitDepth;
        public int BitDepth
        {
            get => this.bitDepth;
            set => this.SetContentField(ref this.bitDepth, value);
        }

        private long length;
        public long Length
        {
            get => this.length;
            set => this.SetContentField(ref this.length, value);
        }

        private TimeSpan duration = TimeSpan.Zero;
        public TimeSpan Duration
        {
            get => this.duration;
            set => this.SetContentField(ref this.duration, value);
        }

        public object? Tag { get; set; } = null;

        public Bitmap WaveformPreview => this.DrawWaveformPreview();


        // Musical metadata
        private float bpm;
        public float Bpm
        {
            get => this.bpm;
            set => this.SetContentField(ref this.bpm, value);
        }

        private float scannedBpm;
        public float ScannedBpm
        {
            get => this.scannedBpm;
            set => this.SetContentField(ref this.scannedBpm, value);
        }

        private float timing = 1.0f;
        public float Timing
        {
            get => this.timing;
            set => this.SetContentField(ref this.timing, value);
        }

        private float scannedTiming = 1.0f;
        public float ScannedTiming
        {
            get => this.scannedTiming;
            set => this.SetContentField(ref this.scannedTiming, value);
        }

        private string key = string.Empty;
        public string Key
        {
            get => this.key;
            set => this.SetContentField(ref this.key, value);
        }

        private string scannedKey = string.Empty;
        public string ScannedKey
        {
            get => this.scannedKey;
            set => this.SetContentField(ref this.scannedKey, value);
        }

        private float volume = 1.0f;
        public float Volume
        {
            get => this.volume;
            set => this.SetContentField(ref this.volume, value);
        }

        // Playback & navigation state
        public bool Playing { get; protected set; }
        public bool Paused { get; protected set; }
        public static event Action? GlobalPlayingChanged;
        public event Action? PlayingChanged;
        public int ChunkSize { get; set; }
        public int OverlapSize { get; set; }

        private double stretchFactor = 1.0;
        public double StretchFactor
        {
            get => this.stretchFactor;
            set => this.SetContentField(ref this.stretchFactor, value);
        }

        public long SkippedPositionBytes { get; protected set; }
        public long ScrollOffset { get; set; }
        public long StartingOffset { get; set; }
        public long SelectionStart { get; set; } = -1;
        public long SelectionEnd { get; set; } = -1;
        public int LastSamplesPerPixel { get; set; }

        private string sampleTag = string.Empty;
        public string SampleTag
        {
            get => this.sampleTag;
            set => this.SetContentField(ref this.sampleTag, value);
        }

        public BindingList<AudioObj> PreviousSteps { get; } = [];
        public BindingList<AudioObj> NextSteps { get; } = [];

        // Playback infrastructure
        private readonly AudioPlaybackService playback = new();
        private bool disposed;
        public bool LoopEnabled { get; set; }
        private bool playbackLoopApplied;
        private long playbackLoopStartBytes;
        private long playbackLoopEndBytes;
        private long playbackLoopLengthBytes => Math.Max(0, this.playbackLoopEndBytes - this.playbackLoopStartBytes);
        private long loopFractionStartSamples;
        private long loopFractionEndSamples;
        public long LoopStartSamples => this.loopFractionStartSamples;
        public long LoopEndSamples => this.loopFractionEndSamples;
        private long positionOriginBytes;
        private bool resumeFromSetPosition;
        private long pausedBaselineBytes;

        // Undo / Redo infrastructure (computed)
        public bool CanUndo => this.PreviousSteps.Count > 0;
        public bool CanRedo => this.NextSteps.Count > 0;

        // Beat grid
        private bool drawBeatGrid;
        public bool DrawBeatGrid
        {
            get => this.drawBeatGrid;
            set => this.SetContentField(ref this.drawBeatGrid, value);
        }

        private bool[] beatGrid = [];
        public bool[] BeatGrid
        {
            get => this.beatGrid;
            set => this.SetContentField(ref this.beatGrid, value);
        }

        // Metrics store
        public Dictionary<string, double> Metrics { get; } = [];

        public double this[string metric]
        {
            get
            {
                if (this.Metrics.TryGetValue(metric, out double value))
                {
                    return value;
                }

                var key = this.Metrics.Keys.FirstOrDefault(k => k.Equals(metric, StringComparison.OrdinalIgnoreCase));
                return key != null ? this.Metrics[key] : 0.0;
            }
            set
            {
                if (this.Metrics.ContainsKey(metric))
                {
                    this.Metrics[metric] = value;
                }
                else
                {
                    var key = this.Metrics.Keys.FirstOrDefault(k => k.Equals(metric, StringComparison.OrdinalIgnoreCase));
                    if (key != null)
                    {
                        this.Metrics[key] = value;
                    }
                    else
                    {
                        string capitalizedMetric = metric.Length > 0
                            ? char.ToUpper(metric[0], CultureInfo.InvariantCulture) + metric[1..].ToLowerInvariant()
                            : metric;
                        this.Metrics.Add(capitalizedMetric, value);
                    }
                }

                this.NotifyContentChanged(nameof(this.Metrics));
            }
        }

        private void SetContentField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            this.NotifyContentChanged(propertyName);
        }

        private void NotifyContentChanged([CallerMemberName] string? propertyName = null)
        {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public AudioObj()
        {
            AudioPlaybackService.Register(this);
            this.Volume = AudioPlaybackService.MasterLimiter * 100f;
        }


        public AudioObj(string filePath, bool load = false)
        {
            AudioPlaybackService.Register(this);
            this.Volume = AudioPlaybackService.MasterLimiter * 100f;
            this.FilePath = filePath;
            if (load)
            {
                if (!this.LoadAudioFile())
                {
                    this.Dispose();
                }
            }
        }

        public static AudioObj? FromFile(string filePath)
        {
            var obj = new AudioObj(filePath);
            if (obj.LoadAudioFile())
            {
                return obj;
            }

            obj.Dispose();
            return null;
        }

        public static async Task<AudioObj?> FromFileAsync(string filePath)
        {
            var obj = await Task.Run(() => new AudioObj(filePath)).ConfigureAwait(false);
            if (obj.LoadAudioFile())
            {
                return obj;
            }

            obj.Dispose();
            return null;
        }

        public AudioObj Clone()
        {
            return new AudioObj
            {
                Name = this.Name,
                OriginalName = this.OriginalName,
                FilePath = this.FilePath,
                Data = (float[])this.Data.Clone(),
                SampleRate = this.SampleRate,
                SampleRateFactor = this.SampleRateFactor,
                ManualSampleRateFactor = this.ManualSampleRateFactor,
                SyncNudgeSampleRateFactor = this.SyncNudgeSampleRateFactor,
                Channels = this.Channels,
                BitDepth = this.BitDepth,
                Length = this.Length,
                Duration = this.Duration,
                Bpm = this.Bpm,
                DrawBeatGrid = this.DrawBeatGrid,
                BeatGrid = (bool[])this.BeatGrid.Clone(),
                Timing = this.Timing,
                Volume = this.Volume
            };
        }

        public Task<AudioObj> CloneAsync()
        {
            return Task.Run(this.Clone);
        }

        public async Task CreateSnapshotAsync()
        {
            this.PreviousSteps.Add(await this.CloneAsync().ConfigureAwait(false));
        }

        public string GetInfoString(bool formatted = true)
        {
            List<string> infoLines =
            [
                $"{(this.SampleRate / 1000.0f):F1} Hz, {this.Channels} ch., {this.BitDepth} bits",
                $"Duration: {this.Duration:h\\:mm\\:ss\\.fff}",
                $"({this.Length:N0} f32 ≙ {(this.SizeInKb / 1024.0f):F2} MB)",
                $"BPM-Tag: {this.Bpm:F3}",
                $"BPM Scanned: {this.ScannedBpm:F3}"
            ];

            return formatted ? string.Join(Environment.NewLine, infoLines) : string.Join(" | ", infoLines);
        }

        public string GetMetricsString(bool formatted = true)
        {
            if (this.Metrics.Count == 0)
            {
                return "No metrics available.";
            }

            List<string> metricLines = this.Metrics
                .Select(kvp => $"{kvp.Key}: {kvp.Value:F2} ms")
                .ToList();

            return formatted ? string.Join(Environment.NewLine, metricLines) : string.Join(" | ", metricLines);
        }

        public void CreateUndoStep()
        {
            // Invalidate redo stack (new action)
            ClearSteps(this.NextSteps);
            // push current snapshot to previous steps
            this.PreviousSteps.Add(this.Clone());
        }

        public bool Undo()
        {
            if (!this.CanUndo)
            {
                return false;
            }

            try
            {
                // save current state for redo
                this.NextSteps.Add(this.Clone());

                // take last snapshot
                var snapshot = this.PreviousSteps[^1];
                if (snapshot == null)
                {
                    return false;
                }

                // apply snapshot and remove it from previous steps
                this.ApplyStateFrom(snapshot);
                this.PreviousSteps.RemoveAt(this.PreviousSteps.Count - 1);
                snapshot.Dispose();

                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool Redo()
        {
            if (!this.CanRedo)
            {
                return false;
            }

            try
            {
                // save current state back into previous steps (undo stack)
                this.PreviousSteps.Add(this.Clone());

                // get redo state (last in NextSteps)
                var redoState = this.NextSteps[^1];
                this.NextSteps.RemoveAt(this.NextSteps.Count - 1);
                if (redoState == null)
                {
                    return false;
                }

                this.ApplyStateFrom(redoState);
                redoState.Dispose();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task CreateUndoStepAsync()
        {
            ClearSteps(this.NextSteps);
            this.PreviousSteps.Add(await this.CloneAsync().ConfigureAwait(false));
        }

        public async Task<bool> UndoAsync()
        {
            if (!this.CanUndo)
            {
                return false;
            }

            try
            {
                this.NextSteps.Add(await this.CloneAsync().ConfigureAwait(false));

                var snapshot = this.PreviousSteps[^1];
                if (snapshot == null)
                {
                    return false;
                }

                this.ApplyStateFrom(snapshot);
                this.PreviousSteps.RemoveAt(this.PreviousSteps.Count - 1);
                snapshot.Dispose();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> RedoAsync()
        {
            if (!this.CanRedo)
            {
                return false;
            }

            try
            {
                this.PreviousSteps.Add(await this.CloneAsync().ConfigureAwait(false));

                var redoState = this.NextSteps[^1];
                this.NextSteps.RemoveAt(this.NextSteps.Count - 1);
                if (redoState == null)
                {
                    return false;
                }

                this.ApplyStateFrom(redoState);
                redoState.Dispose();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Rename(string newName)
        {
            this.Name = newName;
            this.OriginalName = newName;
        }

        // Internal helper - applies state from source snapshot to THIS instance
        private void ApplyStateFrom(AudioObj source)
        {
            this.Name = source.Name;

            // copy audio data and relevant metadata, important, no cloning of Data, just assign ref
            this.Data = source.Data;
            this.SampleRate = source.SampleRate;
            this.Channels = source.Channels;
            this.BitDepth = source.BitDepth;
            this.Length = source.Length;
            this.Duration = source.Duration;
            this.Bpm = source.Bpm;
            this.Timing = source.Timing;
            this.Volume = source.Volume;

            // selection should be reset after applying a snapshot (like in collection)
            this.SelectionStart = -1;
            this.SelectionEnd = -1;

            // IMPORTANT: Do NOT overwrite PreviousSteps or NextSteps here.
            // They belong to the live object and should remain intact.
        }

        public void ReplaceWith(AudioObj source, bool disposeSource = false)
        {
            if (source == null)
            {
                return;
            }

            // Stop playback on this instance before replacing data
            try { this.StopAsync().GetAwaiter().GetResult(); } catch { }

            // Copy metrics and basic properties
            this.ApplyStateFrom(source);

            // Copy additional metadata fields
            this.ScannedBpm = source.ScannedBpm;
            this.ScannedTiming = source.ScannedTiming;
            this.ScannedKey = source.ScannedKey;
            this.Key = source.Key;
            this.SampleTag = source.SampleTag;
            this.ChunkSize = source.ChunkSize;
            this.OverlapSize = source.OverlapSize;
            this.StretchFactor = source.StretchFactor;
            this.ScrollOffset = source.ScrollOffset;
            this.StartingOffset = source.StartingOffset;
            this.Bpm = source.Bpm;
            this.Rename(source.OriginalName);

            // Copy metrics dictionary content
            this.Metrics.Clear();
            foreach (var kv in source.Metrics)
            {
                this.Metrics[kv.Key] = kv.Value;
            }

            // Reset playback state
            if (this.Playing) GlobalPlayingChanged?.Invoke();
            this.Playing = false;
            this.Paused = false;
            this.positionOriginBytes = 0;
            this.SkippedPositionBytes = 0;

            // Invalidate undo/redo as applying a new state should be considered a new baseline
            ClearSteps(this.NextSteps);
            ClearSteps(this.PreviousSteps);

            if (disposeSource)
            {
                try { source.Dispose(); } catch { }
            }
        }

        public void ClearRedoHistory()
        {
            ClearSteps(this.NextSteps);
        }

        private static void ClearSteps(BindingList<AudioObj> steps)
        {
            foreach (AudioObj step in steps)
            {
                try { step.Dispose(); } catch { }
            }
            steps.Clear();
        }

        public async Task InsertAudioAtFrameAsync(AudioObj clip, long insertFrame)
        {
            if (clip == null || clip.Data.Length == 0)
            {
                return;
            }

            // Ensure insertFrame is within bounds
            insertFrame = Math.Max(0, Math.Min(insertFrame, this.Length / this.Channels));

            int totalChannels = this.Channels;
            long insertIndex = insertFrame * totalChannels;
            float[] newData = new float[this.Data.Length + clip.Data.Length];
            Array.Copy(this.Data, 0, newData, 0, insertIndex);
            Array.Copy(clip.Data, 0, newData, insertIndex, clip.Data.Length);
            Array.Copy(this.Data, insertIndex, newData, insertIndex + clip.Data.Length, this.Data.Length - insertIndex);
            this.Data = newData;
            this.Length = this.Data.Length;
            this.Duration = TimeSpan.FromSeconds((double)this.Length / (this.SampleRate * this.Channels));

            await Task.CompletedTask;
        }

        public long GetSampleAtSeconds(double seconds)
        {
            // Guard
            if (this.SampleRate <= 0 || this.Data == null || this.Data.Length == 0 || this.Channels <= 0)
            {
                return 0;
            }

            if (double.IsNaN(seconds) || double.IsInfinity(seconds))
            {
                return 0;
            }

            // Use base sample rate (frames = samples per channel)
            long totalFrames = this.Data.Length / this.Channels;

            // Clamp seconds to valid range
            if (seconds <= 0.0)
            {
                return 0;
            }

            if (seconds >= this.Duration.TotalSeconds)
            {
                return totalFrames - 1;
            }

            // Compute frame index (floor to avoid stepping past requested time)
            long frameIndex = (long)Math.Floor(seconds * this.SampleRate);

            // Ensure within bounds [0, totalFrames-1]
            frameIndex = Math.Clamp(frameIndex, 0L, Math.Max(0L, totalFrames - 1));

            return frameIndex;
        }

        public long GetNearestSnapSamplePosition(long frameIndex)
        {
            if (this.BeatGrid == null || this.BeatGrid.Length == 0)
            {
                long totalFrames = this.Length / Math.Max(1, this.Channels);
                if (totalFrames <= 0)
                {
                    return 0;
                }
                return Math.Clamp(frameIndex, 0L, totalFrames - 1);
            }

            int total = this.BeatGrid.Length;
            if (total <= 0)
            {
                return 0;
            }

            long clamped = Math.Clamp(frameIndex, 0L, total - 1);
            int center = (int)clamped;

            if (this.BeatGrid[center])
            {
                return center;
            }

            int maxDistance = total - 1;
            for (int d = 1; d <= maxDistance; d++)
            {
                int left = center - d;
                if (left >= 0 && this.BeatGrid[left])
                {
                    return left;
                }

                int right = center + d;
                if (right < total && this.BeatGrid[right])
                {
                    return right;
                }

                if (left < 0 && right >= total)
                {
                    break;
                }
            }

            return center;
        }

        public long? GetNextBeatGridFrame(long frameIndex)
        {
            if (this.BeatGrid == null || this.BeatGrid.Length == 0)
            {
                return null;
            }

            long startFrame = frameIndex < long.MaxValue ? frameIndex + 1 : long.MaxValue;
            if (startFrame < 0 || startFrame >= this.BeatGrid.LongLength)
            {
                return null;
            }

            for (long candidate = startFrame; candidate < this.BeatGrid.LongLength; candidate++)
            {
                if (this.BeatGrid[candidate])
                {
                    return candidate;
                }
            }

            return null;
        }

        public async Task ResampleAsync(int sampleRate)
        {
            // Guard against invalid sample rates
            if (sampleRate <= 0)
            {
                throw new ArgumentException("Invalid sample rate.", nameof(sampleRate));
            }

            // Resample async and parallel if possible (offload to thread pool)
            await Task.Run(() =>
            {
                // Simple nearest-neighbor resampling for demonstration (replace with better algorithm if needed)
                int newLength = (int)((long)this.Data.Length * sampleRate / this.SampleRate);
                float[] newData = new float[newLength];
                for (int i = 0; i < newLength; i++)
                {
                    int oldIndex = (int)((long)i * this.SampleRate / sampleRate);
                    oldIndex = Math.Min(oldIndex, this.Data.Length - 1);
                    newData[i] = this.Data[oldIndex];
                }
                this.Data = newData;
                this.SampleRate = sampleRate;
                this.Length = newData.Length;
                this.Duration = TimeSpan.FromSeconds((double)this.Length / (this.SampleRate * this.Channels));
            }).ConfigureAwait(false);
        }

        public async Task RechannelAsync(int channels)
        {
            // Guard against invalid channel counts
            if (channels <= 0)
            {
                throw new ArgumentException("Invalid channel count.", nameof(channels));
            }

            // Rechannel async and parallel if possible (offload to thread pool)
            await Task.Run(() =>
            {
                // Simple rechanneling (replace with better algorithm if needed)
                int totalSamples = this.Data.Length / this.Channels;
                float[] newData = new float[totalSamples * channels];
                for (int i = 0; i < totalSamples; i++)
                {
                    for (int c = 0; c < channels; c++)
                    {
                        newData[i * channels + c] = this.Data[i * this.Channels + c % this.Channels];
                    }
                }
                this.Data = newData;
                this.Channels = channels;
                this.Length = newData.Length;
                this.Duration = TimeSpan.FromSeconds((double)this.Length / (this.SampleRate * this.Channels));
            }).ConfigureAwait(false);
        }
    }
}
