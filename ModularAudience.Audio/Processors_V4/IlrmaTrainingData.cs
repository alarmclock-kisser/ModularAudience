using System.Numerics;

namespace ModularAudience.Audio.Processors_V4
{
    internal sealed class IlrmaTrainingData
    {
        private IlrmaTrainingData(int frames, int bins)
        {
            this.Frames = frames;
            this.Bins = bins;
            this.FrameIndices = new int[frames];
            this.FrameEnergy = new double[frames];
            this.BinEnergy = new double[bins];
        }

        internal int Frames { get; }
        internal int Bins { get; }
        internal int[] FrameIndices { get; }
        internal double[] FrameEnergy { get; }
        internal double[] BinEnergy { get; }
        internal Complex[][] Left { get; private set; } = [];
        internal Complex[][] Right { get; private set; } = [];
        internal double SamplePeak { get; private set; }
        internal long WorkingBytes { get; private set; }
        internal bool IsSilent => this.SamplePeak == 0;

        internal static IlrmaTrainingData Read(DeterministicAudioSnapshot source,
            DeterministicSeparationSettings settings, IProgress<DeterministicSeparationProgress>? progress,
            CancellationToken token)
        {
            int available = DeterministicSpectrogram.FrameCount(source, settings);
            int frames = Math.Min(available, settings.AnalysisFrames);
            IlrmaTrainingData data = new(frames, settings.WindowSize / 2 + 1);
            progress?.Report(new(0, "Checking stereo samples and global spatial rank"));
            data.Scan(source, settings, available, progress, token);
            if (data.IsSilent) return data;
            data.WorkingBytes = EstimateMemory(data.Bins, frames, settings);
            IlrmaNumerics.RequireMemory(data.WorkingBytes, "training");
            data.Left = new Complex[data.Bins][];
            data.Right = new Complex[data.Bins][];
            for (int bin = 0; bin < data.Bins; bin++)
            {
                token.ThrowIfCancellationRequested();
                data.Left[bin] = new Complex[frames];
                data.Right[bin] = new Complex[frames];
            }
            data.ReadFrames(source, settings, progress, token);
            data.Normalize(token);
            return data;
        }

        private static long EstimateMemory(int bins, int frames, DeterministicSeparationSettings settings)
        {
            // X: two complex tables. Both sources: power and variance tables, T and V.
            // Include row objects, rendering/FFT scratch and conservative managed FFT headroom.
            return checked(64L * bins * frames + 16L * settings.IlrmaComponents * (bins + frames)
                + 256L * (bins + frames) + 256L * settings.WindowSize * settings.Threads + 1048576);
        }

        private void Scan(DeterministicAudioSnapshot source, DeterministicSeparationSettings settings,
            int available, IProgress<DeterministicSeparationProgress>? progress, CancellationToken token)
        {
            int sampleFrames = source.Samples.Length / 2;
            double[] strongest = new double[this.Frames];
            double leftEnergy = 0, rightEnergy = 0, cross = 0;
            for (int frame = 0; frame < this.Frames; frame++)
                this.FrameIndices[frame] = (int)((long)frame * available / this.Frames);
            for (int sample = 0; sample < sampleFrames; sample++)
            {
                if ((sample & 65535) == 0)
                {
                    token.ThrowIfCancellationRequested();
                    progress?.Report(new(0.06 * sample / Math.Max(1, sampleFrames), "Checking stereo samples and global spatial rank"));
                }
                double left = source.Samples[2 * sample], right = source.Samples[2 * sample + 1];
                if (!double.IsFinite(left) || !double.IsFinite(right))
                    throw new ArgumentException($"ILRMA input contains a non-finite sample at stereo frame {sample}.", nameof(source));
                this.SamplePeak = Math.Max(this.SamplePeak, Math.Max(Math.Abs(left), Math.Abs(right)));
                leftEnergy += left * left;
                rightEnergy += right * right;
                cross += left * right;
                int frameIndex = sample / settings.HopSize;
                int region = Math.Min(this.Frames - 1, (int)((long)frameIndex * this.Frames / available));
                double energy = left * left + right * right;
                if (available > this.Frames && energy > strongest[region])
                {
                    strongest[region] = energy;
                    this.FrameIndices[region] = frameIndex;
                }
            }
            token.ThrowIfCancellationRequested();
            if (!this.IsSilent) ValidateRank(leftEnergy, rightEnergy, cross);
        }

        private static void ValidateRank(double left, double right, double cross)
        {
            // det(C)/(C00*C11) is invariant to input level and separate channel gains.
            double correlation = left > 0 && right > 0 ? cross / Math.Sqrt(left) / Math.Sqrt(right) : 1;
            double normalizedDeterminant = 1 - correlation * correlation;
            if (!double.IsFinite(normalizedDeterminant) || normalizedDeterminant <= 1e-10)
                throw new ArgumentException("ILRMA requires two spatially independent channels. The global channel covariance is rank-deficient (including proportional or antiphase duplicates).");
        }

        private void ReadFrames(DeterministicAudioSnapshot source, DeterministicSeparationSettings settings,
            IProgress<DeterministicSeparationProgress>? progress, CancellationToken token)
        {
            double[] window = DeterministicSpectrogram.CreateWindow(settings.WindowSize);
            for (int frame = 0; frame < this.Frames; frame++)
            {
                token.ThrowIfCancellationRequested();
                long offset = (long)this.FrameIndices[frame] * settings.HopSize - settings.WindowSize / 2;
                Complex[] left = this.Transform(source, offset, 0, window, token);
                Complex[] right = this.Transform(source, offset, 1, window, token);
                for (int bin = 0; bin < this.Bins; bin++)
                {
                    if ((bin & 255) == 0) token.ThrowIfCancellationRequested();
                    this.Left[bin][frame] = left[bin];
                    this.Right[bin][frame] = right[bin];
                }
                if ((frame & 15) == 0 || frame == this.Frames - 1)
                    progress?.Report(new(0.06 + 0.14 * (frame + 1) / this.Frames, "Sampling distributed complex stereo STFT frames"));
            }
        }

        private Complex[] Transform(DeterministicAudioSnapshot source, long offset, int channel,
            double[] window, CancellationToken token)
        {
            Complex[] spectrum = new Complex[window.Length];
            double scale = Math.Sqrt(window.Length);
            for (int i = 0; i < spectrum.Length; i++)
            {
                if ((i & 255) == 0) token.ThrowIfCancellationRequested();
                long sample = offset + i;
                if (sample >= 0 && sample < source.Samples.Length / 2)
                    spectrum[i] = new Complex(source.Samples[2 * (int)sample + channel] / this.SamplePeak / scale * window[i], 0);
            }
            IlrmaNumerics.Transform(spectrum, false, token);
            spectrum[0] = new Complex(spectrum[0].Real, 0);
            spectrum[window.Length / 2] = new Complex(spectrum[window.Length / 2].Real, 0);
            return spectrum;
        }

        private void Normalize(CancellationToken token)
        {
            double total = 0;
            for (int bin = 0; bin < this.Bins; bin++)
            {
                token.ThrowIfCancellationRequested();
                for (int frame = 0; frame < this.Frames; frame++)
                    total += IlrmaNumerics.Power(this.Left[bin][frame]) + IlrmaNumerics.Power(this.Right[bin][frame]);
            }
            if (!(total > 0) || !double.IsFinite(total))
                throw new ArithmeticException("The nonsilent ILRMA input has no finite sampled STFT energy.");
            double scale = Math.Sqrt(2.0 * this.Bins * this.Frames / total);
            for (int bin = 0; bin < this.Bins; bin++)
            {
                token.ThrowIfCancellationRequested();
                for (int frame = 0; frame < this.Frames; frame++)
                {
                    this.Left[bin][frame] *= scale;
                    this.Right[bin][frame] *= scale;
                    double energy = IlrmaNumerics.Power(this.Left[bin][frame]) + IlrmaNumerics.Power(this.Right[bin][frame]);
                    this.BinEnergy[bin] += energy / this.Frames;
                    this.FrameEnergy[frame] += energy;
                }
            }
        }
    }
}
