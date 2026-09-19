namespace ModularAudience.Audio.Processors_V4
{
    /// <summary>Immutable per-call geometry and explicit allocation limits; no quality downgrades.</summary>
    internal sealed class PyinSettings
    {
        private const int MaximumFrameLength = 1 << 18;
        private const long MaximumBackpointerBytes = 512L * 1024 * 1024;
        private const long MaximumWorkingBytes = 2L * 1024 * 1024 * 1024;
        internal const int BinsPerOctave = 120;

        private PyinSettings(int sampleCount, int sampleRate, int hopSize,
            double minimumHz, double maximumHz, int threads)
        {
            this.SampleRate = sampleRate;
            this.HopSize = hopSize;
            this.MinimumHz = minimumHz;
            this.MaximumHz = maximumHz;
            this.MinimumLag = (int)Math.Floor(sampleRate / maximumHz);
            this.MaximumLag = (int)Math.Ceiling(sampleRate / minimumHz);
            this.FrameLength = ResolveFrameLength(this.MaximumLag);
            double octaves = Math.Log2(maximumHz / minimumHz);
            this.PitchCount = Math.Max(2, (int)Math.Ceiling(BinsPerOctave * octaves) + 1);
            this.OctavesPerBin = octaves / (this.PitchCount - 1);
            long frames = (long)sampleCount / hopSize + 1;
            this.WorkerCount = (int)Math.Min(threads, frames);
            this.ValidateMemory(frames);
            this.FrameCount = (int)frames;
        }

        internal int SampleRate { get; }
        internal int HopSize { get; }
        internal int MinimumLag { get; }
        internal int MaximumLag { get; }
        internal int FrameLength { get; }
        internal int FrameCount { get; }
        internal int PitchCount { get; }
        internal int StateCount => 2 * this.PitchCount;
        internal int WorkerCount { get; }
        internal double MinimumHz { get; }
        internal double MaximumHz { get; }
        internal double OctavesPerBin { get; }

        internal static PyinSettings Create(int sampleCount, int sampleRate, int hopSize,
            double minimumHz, double maximumHz, int threads)
            => new(sampleCount, sampleRate, hopSize, minimumHz, maximumHz, threads);

        internal static void ValidateArguments(int sampleRate, int hopSize,
            double minimumHz, double maximumHz, int threads)
        {
            if (sampleRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate), "The sample rate must be positive.");
            }
            if (hopSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(hopSize), "The pYIN hop size must be positive.");
            }
            if (!double.IsFinite(minimumHz) || minimumHz < 20)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumHz), "The minimum frequency must be finite and at least 20 Hz.");
            }
            if (!double.IsFinite(maximumHz) || maximumHz <= minimumHz || maximumHz >= sampleRate / 2.0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumHz), "The maximum frequency must be finite, greater than minimumHz and strictly below Nyquist.");
            }
            if (threads < 1 || threads > Environment.ProcessorCount)
            {
                throw new ArgumentOutOfRangeException(nameof(threads), $"The thread count must be between 1 and {Environment.ProcessorCount}.");
            }
        }

        internal int GetPitchBin(double frequency)
        {
            double position = Math.Log2(frequency / this.MinimumHz) / this.OctavesPerBin;
            return Math.Clamp((int)Math.Round(position, MidpointRounding.AwayFromZero), 0, this.PitchCount - 1);
        }

        internal double GetFrequency(int pitch)
            => pitch == this.PitchCount - 1 ? this.MaximumHz
                : this.MinimumHz * Math.Pow(2, pitch * this.OctavesPerBin);

        private static int ResolveFrameLength(int maximumLag)
        {
            long required = 2L * (maximumLag + 2L);
            if (required > MaximumFrameLength)
            {
                throw new ArgumentException($"pYIN needs at least {required} samples per frame, exceeding the {MaximumFrameLength}-sample limit. Raise minimumHz or reduce the sample rate; resolution is not reduced automatically.");
            }
            int length = 1;
            while (length < required)
            {
                length <<= 1;
            }
            return length;
        }

        private void ValidateMemory(long frames)
        {
            long backpointerBytes = frames * this.StateCount * sizeof(int);
            if (backpointerBytes > MaximumBackpointerBytes)
            {
                throw new ArgumentException($"pYIN needs {backpointerBytes} backpointer bytes, exceeding the {MaximumBackpointerBytes}-byte limit. Reduce the source duration or disable pYIN.");
            }
            // Two dense acoustic tables, the banded decoder's backpointers, result objects,
            // per-worker FFT/candidate scratch and conservative managed FFT allocation headroom.
            long estimatedBytes = frames * (24L * this.PitchCount + 64)
                + this.WorkerCount * (128L * this.FrameLength + 16L * this.PitchCount)
                + 128L * this.PitchCount + 16384;
            if (estimatedBytes > MaximumWorkingBytes)
            {
                throw new ArgumentException($"pYIN needs an estimated {estimatedBytes} working bytes, exceeding the {MaximumWorkingBytes}-byte limit. Reduce the source duration or disable pYIN; pitch resolution is not reduced automatically.");
            }
        }
    }
}
