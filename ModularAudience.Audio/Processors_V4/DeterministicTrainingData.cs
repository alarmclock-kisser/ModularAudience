namespace ModularAudience.Audio.Processors_V4
{
    internal sealed class DeterministicTrainingData
    {
        private DeterministicTrainingData(int frames, int bins)
        {
            this.BandOfBin = CreateBandMap(bins);
            int bands = this.BandOfBin[^1] + 1;
            this.Power = DeterministicSpectrogram.Allocate(frames, bins);
            this.BandPower = DeterministicSpectrogram.Allocate(frames, bands);
            this.Harmonic = DeterministicSpectrogram.Allocate(frames, bands);
            this.Percussive = DeterministicSpectrogram.Allocate(frames, bands);
            this.Pan = DeterministicSpectrogram.Allocate(frames, bands);
            this.Energy = new double[frames];
            this.FrameIndices = new int[frames];
        }

        internal double[][] Power { get; }
        internal double[][] BandPower { get; }
        internal double[][] Harmonic { get; }
        internal double[][] Percussive { get; }
        internal double[][] Pan { get; }
        internal double[] Energy { get; }
        internal int[] FrameIndices { get; }
        internal int[] BandOfBin { get; }
        internal double Floor { get; private set; }
        internal bool IsSilent { get; private set; }

        internal static DeterministicTrainingData Read(DeterministicAudioSnapshot source,
            DeterministicSeparationSettings settings, DeterministicModelProgress progress,
            CancellationToken cancellationToken)
        {
            int available = DeterministicSpectrogram.FrameCount(source, settings);
            int count = Math.Min(available, settings.AnalysisFrames);
            DeterministicTrainingData data = new(count, settings.WindowSize / 2 + 1);
            int width = Math.Min(settings.BlockFrames, Math.Min(settings.MedianFrames, Math.Max(1, count / 8)));
            int tiles = (count + width - 1) / width;
            int destination = 0;
            for (int tile = 0; tile < tiles; tile++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int length = count / tiles + (tile < count % tiles ? 1 : 0);
                int start = TileStart(available, tiles, tile, length);
                data.ReadTile(source, settings, start, length, destination, cancellationToken);
                destination += length;
                progress.Report(0.30 * destination / count, "Sampling contiguous analysis windows");
            }
            data.Normalize(cancellationToken);
            return data;
        }

        private static int TileStart(int available, int tiles, int tile, int length)
        {
            int left = (int)((long)tile * available / tiles);
            int right = (int)((long)(tile + 1) * available / tiles);
            if (tile == 0)
            {
                return 0;
            }
            return tile == tiles - 1 ? available - length : left + (right - left - length) / 2;
        }

        private void ReadTile(DeterministicAudioSnapshot source, DeterministicSeparationSettings settings,
            int start, int length, int destination, CancellationToken cancellationToken)
        {
            int halo = settings.MedianFrames / 2;
            int first = Math.Max(0, start - halo);
            int end = (int)Math.Min(DeterministicSpectrogram.FrameCount(source, settings), (long)start + length + halo);
            // HPSS is computed only on contiguous real frames, never across sampled windows.
            DeterministicSpectrogram block = DeterministicSpectrogram.Read(source, settings, first, end - first, false, cancellationToken);
            ParallelOptions options = new() { MaxDegreeOfParallelism = settings.Threads, CancellationToken = cancellationToken };
            Parallel.For(0, length, options, offset =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                this.FrameIndices[destination + offset] = start + offset;
                this.Capture(block, start - first + offset, destination + offset, cancellationToken);
            });
        }

        private void Capture(DeterministicSpectrogram block, int frame, int destination, CancellationToken token)
        {
            for (int bin = 0; bin < block.Bins; bin++)
            {
                if ((bin & 255) == 0) token.ThrowIfCancellationRequested();
                int band = this.BandOfBin[bin];
                double power = Positive(block.Power[frame][bin]);
                this.Power[destination][bin] = power;
                this.BandPower[destination][band] += power;
                this.Harmonic[destination][band] += power * Unit(block.Harmonic[frame][bin]);
                this.Percussive[destination][band] += power * Unit(block.Percussive[frame][bin]);
                this.Pan[destination][band] += power * SignedUnit(block.Pan[frame][bin]);
                this.Energy[destination] += power;
            }
            for (int band = 0; band < this.BandPower[destination].Length; band++)
            {
                double energy = this.BandPower[destination][band];
                if (energy <= 0) continue;
                this.Harmonic[destination][band] /= energy;
                this.Percussive[destination][band] /= energy;
                this.Pan[destination][band] /= energy;
            }
        }

        private void Normalize(CancellationToken token)
        {
            double maximum = Maximum(this.Power, token);
            this.IsSilent = maximum == 0;
            this.Floor = NormalizePower(this.Power, maximum, token);
            if (this.IsSilent) return;
            for (int frame = 0; frame < this.Power.Length; frame++)
            {
                token.ThrowIfCancellationRequested();
                this.Energy[frame] /= maximum;
                for (int band = 0; band < this.BandPower[frame].Length; band++)
                {
                    this.BandPower[frame][band] /= maximum;
                }
            }
        }

        internal static double NormalizePower(double[][] power, double maximum, CancellationToken token)
        {
            double sum = 0;
            for (int frame = 0; frame < power.Length; frame++)
            {
                token.ThrowIfCancellationRequested();
                for (int bin = 0; bin < power[frame].Length; bin++)
                {
                    power[frame][bin] = maximum > 0 ? Positive(power[frame][bin]) / maximum : 0;
                    sum += power[frame][bin];
                }
            }
            double mean = power.Length > 0 ? sum / power.Length / power[0].Length : 0;
            // This is relative to the global fitting-matrix maximum, not an absolute audio gate.
            return Math.Max(1e-15, mean * 1e-9);
        }

        internal static double Maximum(double[][] values, CancellationToken token)
        {
            double maximum = 0;
            foreach (double[] row in values)
            {
                token.ThrowIfCancellationRequested();
                foreach (double value in row) maximum = Math.Max(maximum, Positive(value));
            }
            return maximum;
        }

        internal static int[] CreateBandMap(int bins)
        {
            int[] map = new int[bins];
            int previous = -1;
            int compact = -1;
            for (int bin = 0; bin < bins; bin++)
            {
                int band = (int)(47 * Math.Log(1.0 + bin) / Math.Log(bins));
                if (band != previous) compact++;
                map[bin] = compact;
                previous = band;
            }
            return map;
        }

        internal static double Positive(double value) => double.IsFinite(value) && value > 0 ? value : 0;
        internal static double Unit(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
        internal static double SignedUnit(double value) => double.IsFinite(value) ? Math.Clamp(value, -1, 1) : 0;
    }

    internal sealed class DeterministicModelProgress(IProgress<DeterministicSeparationProgress>? target)
    {
        private double lastFraction = -1;
        private string lastStage = string.Empty;

        internal void Report(double fraction, string stage)
        {
            fraction = Math.Clamp(fraction, 0, 1);
            if (fraction < this.lastFraction) return;
            if (stage == this.lastStage && fraction < 1 && fraction - this.lastFraction < 0.02) return;
            this.lastFraction = fraction;
            this.lastStage = stage;
            target?.Report(new DeterministicSeparationProgress(fraction, stage));
        }
    }
}
