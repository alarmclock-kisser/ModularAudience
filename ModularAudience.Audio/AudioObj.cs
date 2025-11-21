using ModularAudience.Audio;
using System.ComponentModel;
using System.Globalization;

namespace NAudience.Core
{
    /// <summary>
    /// Partial forward declaration for <see cref="AudioObj"/>. Implementation details live in <c>AudioObj/</c>.
    /// </summary>
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
        public BindingList<AudioObj> PreviousSteps { get; } = [];
        public BindingList<AudioObj> NextSteps { get; } = [];

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
        }

        public AudioObj(string filePath, bool load = false)
        {
            this.FilePath = filePath;
            if (load && !this.LoadAudioFile())
            {
                this.Dispose();
            }
        }

        public static AudioObj? FromFile(string filePath)
        {
            var obj = new AudioObj(filePath);
            return obj.LoadAudioFile() ? obj : null;
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
                Id = Guid.NewGuid(),
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
            [
                $"{(this.SampleRate / 1000.0f):F1} Hz, {this.Channels} ch., {this.BitDepth} bits",
                $"Duration: {this.Duration:h\\:mm\\:ss\\.fff}",
                $"({this.Length} f32 ≙ {(this.SizeInKb / 1024.0f):F2} MB)",
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
    }
}