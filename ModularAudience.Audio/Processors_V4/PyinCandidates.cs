namespace ModularAudience.Audio.Processors_V4
{
    /// <summary>
    /// Integrates multiple YIN troughs over 100 Beta(2,18) threshold bins, rather than selecting
    /// one autocorrelation peak. Earlier qualifying troughs have a Boltzmann rank prior (lambda=2).
    /// Thresholds without a qualifying trough assign 0.01 mass to the minimum trough; the rest
    /// is unvoiced. With no local trough at all, the searched global minimum receives only 0.01.
    /// </summary>
    internal sealed class PyinCandidates
    {
        private const int ThresholdCount = 100;
        private const double BoltzmannParameter = 2;
        private const double NoTroughProbability = 0.01;
        private readonly PyinSettings settings;
        private readonly int[] lags;
        private readonly double[] masses;
        private readonly double[] strongestMass;
        private readonly double[] thresholdPrior;

        internal PyinCandidates(PyinSettings settings)
        {
            this.settings = settings;
            int capacity = settings.MaximumLag - settings.MinimumLag + 1;
            this.lags = new int[capacity];
            this.masses = new double[capacity];
            this.strongestMass = new double[settings.PitchCount];
            this.thresholdPrior = CreateThresholdPrior();
        }

        internal void Write(double[] difference, PyinObservations observations, int frame,
            CancellationToken token)
        {
            Array.Clear(this.masses);
            Array.Clear(this.strongestMass);
            int count = this.FindTroughs(difference, out int globalLag, token);
            if (count == 0)
            {
                this.lags[0] = globalLag;
                this.masses[0] = NoTroughProbability;
                count = 1;
            }
            else
            {
                this.AccumulateThresholds(difference, count, token);
            }
            this.StoreCandidates(difference, count, observations, frame, token);
            observations.CompleteFrame(frame, token);
        }

        private int FindTroughs(double[] difference, out int globalLag, CancellationToken token)
        {
            int count = 0;
            globalLag = this.settings.MinimumLag;
            for (int lag = this.settings.MinimumLag; lag <= this.settings.MaximumLag; lag++)
            {
                if ((lag & 1023) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                if (difference[lag] < difference[globalLag])
                {
                    globalLag = lag;
                }
                // The asymmetric comparison deterministically selects the first point of a
                // flat-bottomed trough, including a perfectly periodic zero difference.
                if (difference[lag] < difference[lag - 1] && difference[lag] <= difference[lag + 1])
                {
                    this.lags[count++] = lag;
                }
            }
            return count;
        }

        private void AccumulateThresholds(double[] difference, int count, CancellationToken token)
        {
            int minimum = 0;
            for (int i = 1; i < count; i++)
            {
                if ((i & 1023) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                if (difference[this.lags[i]] < difference[this.lags[minimum]])
                {
                    minimum = i;
                }
            }
            for (int bin = 0; bin < ThresholdCount; bin++)
            {
                token.ThrowIfCancellationRequested();
                double threshold = (bin + 1.0) / ThresholdCount;
                int qualifying = this.CountQualifying(difference, count, threshold, token);
                if (qualifying == 0)
                {
                    this.masses[minimum] += NoTroughProbability * this.thresholdPrior[bin];
                }
                else
                {
                    this.DistributeThreshold(difference, count, qualifying, bin, token);
                }
            }
        }

        private int CountQualifying(double[] difference, int count, double threshold, CancellationToken token)
        {
            int qualifying = 0;
            for (int i = 0; i < count; i++)
            {
                if ((i & 1023) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                if (difference[this.lags[i]] < threshold)
                {
                    qualifying++;
                }
            }
            return qualifying;
        }

        private void DistributeThreshold(double[] difference, int count, int qualifying, int bin,
            CancellationToken token)
        {
            double ratio = Math.Exp(-BoltzmannParameter);
            double normalizer = (1 - Math.Exp(-BoltzmannParameter * qualifying)) / (1 - ratio);
            double mass = this.thresholdPrior[bin] / normalizer;
            double threshold = (bin + 1.0) / ThresholdCount;
            for (int i = 0; i < count; i++)
            {
                if ((i & 1023) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                if (difference[this.lags[i]] < threshold)
                {
                    this.masses[i] += mass;
                    mass *= ratio;
                }
            }
        }

        private void StoreCandidates(double[] difference, int count, PyinObservations observations,
            int frame, CancellationToken token)
        {
            int offset = frame * this.settings.PitchCount;
            for (int i = 0; i < count; i++)
            {
                if ((i & 1023) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                double mass = this.masses[i];
                if (mass <= 0)
                {
                    continue;
                }
                double frequency = this.InterpolateFrequency(difference, this.lags[i]);
                int pitch = this.settings.GetPitchBin(frequency);
                observations.CandidateMass[offset + pitch] += mass;
                if (mass > this.strongestMass[pitch])
                {
                    this.strongestMass[pitch] = mass;
                    observations.RefinedFrequency[offset + pitch] = frequency;
                }
            }
        }

        private double InterpolateFrequency(double[] difference, int lag)
        {
            double left = difference[lag - 1];
            double center = difference[lag];
            double right = difference[lag + 1];
            double curvature = left - 2 * center + right;
            double shift = curvature > 0 ? Math.Clamp(0.5 * (left - right) / curvature, -0.5, 0.5) : 0;
            double period = Math.Clamp(lag + shift, this.settings.SampleRate / this.settings.MaximumHz,
                this.settings.SampleRate / this.settings.MinimumHz);
            return Math.Clamp(this.settings.SampleRate / period, this.settings.MinimumHz, this.settings.MaximumHz);
        }

        private static double[] CreateThresholdPrior()
        {
            double[] prior = new double[ThresholdCount];
            double total = 0;
            for (int bin = 0; bin < ThresholdCount; bin++)
            {
                // Beta(2,18) survival S(x)=(1-x)^18*(1+18*x). Subtract survival
                // values instead of almost-unit CDFs to preserve the small upper-tail masses.
                double lower = bin / (double)ThresholdCount;
                double upper = (bin + 1.0) / ThresholdCount;
                prior[bin] = Math.Max(0, BetaSurvival(lower) - BetaSurvival(upper));
                total += prior[bin];
            }
            for (int bin = 0; bin < ThresholdCount; bin++)
            {
                prior[bin] /= total;
            }
            return prior;
        }

        private static double BetaSurvival(double value)
            => Math.Pow(1 - value, 18) * (1 + 18 * value);
    }
}
