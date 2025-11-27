using ModularAudience.Audio;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Drawing;

namespace ModularAudience.Audio
{
    public partial class AudioObj
    {
        // Core identity & metadata
        public Guid Id { get; set; } = Guid.NewGuid();
        public readonly DateTime CreatedAt = DateTime.UtcNow;
        public string Name { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;

        // Audio data & format
        public float[] Data { get; set; } = [];
        public int SampleRate { get; set; }
        public double SampleRateFactor { get; private set; } = 1.0;
        public int AdjustedSampleRate => (int) (this.SampleRate * this.SampleRateFactor);
        public int Channels { get; set; }
        public int BitDepth { get; set; }
        public long Length { get; set; }
        public TimeSpan Duration { get; set; } = TimeSpan.Zero;

		public Bitmap WaveformPreview => this.DrawWaveformPreview();


        // Musical metadata
        public float Bpm { get; set; }
        public float ScannedBpm { get; set; }
        public float Timing { get; set; } = 1.0f;
        public float ScannedTiming { get; set; } = 1.0f;
        public string Key { get; set; } = string.Empty;
        public string ScannedKey { get; set; } = string.Empty;

        public float Volume { get; set; } = 1.0f;

        // Playback & navigation state
        public bool Playing { get; protected set; }
        public bool Paused { get; protected set; }
        public int ChunkSize { get; set; }
        public int OverlapSize { get; set; }
        public double StretchFactor { get; set; } = 1.0;
        public long SkippedPositionBytes { get; protected set; }
        public long ScrollOffset { get; set; }
        public long StartingOffset { get; set; }
        public long SelectionStart { get; set; } = -1;
        public long SelectionEnd { get; set; } = -1;
        public int LastSamplesPerPixel { get; set; }
        public string SampleTag { get; set; } = string.Empty;
        public BindingList<AudioObj> PreviousSteps { get; } = new BindingList<AudioObj>();
        public BindingList<AudioObj> NextSteps { get; } = new BindingList<AudioObj>();

        // Playback infrastructure
        private readonly AudioPlaybackService playback = new();
        public bool LoopEnabled { get; set; }
        private bool playbackLoopApplied;
        private long playbackLoopStartBytes;
        private long playbackLoopEndBytes;
        private long playbackLoopLengthBytes => Math.Max(0, this.playbackLoopEndBytes - this.playbackLoopStartBytes);
        private long loopFractionStartSamples;
        private long loopFractionEndSamples;
        private long positionOriginBytes;
        private bool resumeFromSetPosition;
        private long pausedBaselineBytes;

        // Undo / Redo infrastructure (computed)
        public bool CanUndo => this.PreviousSteps.Count > 0;
        public bool CanRedo => this.NextSteps.Count > 0;

        // Metrics store
        public Dictionary<string, double> Metrics { get; } = new Dictionary<string, double>();

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
                    return;
                }

                var key = this.Metrics.Keys.FirstOrDefault(k => k.Equals(metric, StringComparison.OrdinalIgnoreCase));
                if (key != null)
                {
                    this.Metrics[key] = value;
                    return;
                }

                string capitalizedMetric = metric.Length > 0
                    ? char.ToUpper(metric[0], CultureInfo.InvariantCulture) + metric[1..].ToLowerInvariant()
                    : metric;
                this.Metrics.Add(capitalizedMetric, value);
            }
        }

        public AudioObj()
        {
            // Do not initialize undo history here to avoid Clone recursion and unintended baseline snapshots.
        }

        public AudioObj(string filePath, bool load = false)
        {
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
                Id = this.Id,
                Name = this.Name,
                FilePath = this.FilePath,
                Data = (float[]) this.Data.Clone(),
                SampleRate = this.SampleRate,
                Channels = this.Channels,
                BitDepth = this.BitDepth,
                Length = this.Length,
                Duration = this.Duration,
                Bpm = this.Bpm,
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
            new List<string>
            {
                $"{(this.SampleRate / 1000.0f):F1} Hz, {this.Channels} ch., {this.BitDepth} bits",
                $"Duration: {this.Duration:h\\:mm\\:ss\\.fff}",
                $"({this.Length:N0} f32 ≙ {(this.SizeInKb / 1024.0f):F2} MB)",
                $"BPM-Tag: {this.Bpm:F3}",
                $"BPM Scanned: {this.ScannedBpm:F3}"
            };

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
            this.NextSteps.Clear();
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
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task CreateUndoStepAsync()
        {
            this.NextSteps.Clear();
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
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Internal helper - applies state from source snapshot to THIS instance
        private void ApplyStateFrom(AudioObj source)
        {
            // copy audio data and relevant metadata
            this.Data = (float[]) source.Data.Clone();
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

            // Copy metrics dictionary content
            this.Metrics.Clear();
            foreach (var kv in source.Metrics)
            {
                this.Metrics[kv.Key] = kv.Value;
            }

            // Reset playback state
            this.Playing = false;
            this.Paused = false;
            this.positionOriginBytes = 0;
            this.SkippedPositionBytes = 0;

            // Invalidate undo/redo as applying a new state should be considered a new baseline
            this.NextSteps.Clear();
            this.PreviousSteps.Clear();

            if (disposeSource)
            {
                try { source.Dispose(); } catch { }
            }
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
        }
    }
}
