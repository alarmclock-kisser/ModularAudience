namespace ModularAudience.Audio.Processors_V4
{
    /// <summary>
    /// Sparse triangular pitch motion shared by voiced and unvoiced copies of the grid.
    /// The 35.92 octaves/second maximum motion is quantized upward to a bin radius (at least
    /// one bin). Unlike implementations parameterizing a full window width, this is a radius.
    /// Each SOURCE row is normalized after clipping at the frequency-range edges.
    /// </summary>
    internal sealed class PyinTransitions
    {
        private const double MaximumOctavesPerSecond = 35.92;
        internal const double SwitchProbability = 0.01;
        private readonly double[] logWeights;
        private readonly double[] logRowSums;

        internal PyinTransitions(PyinSettings settings, CancellationToken token)
        {
            double binsPerHop = MaximumOctavesPerSecond * settings.HopSize
                / settings.SampleRate / settings.OctavesPerBin;
            this.Radius = (int)Math.Min(settings.PitchCount - 1, Math.Max(1, Math.Ceiling(binsPerHop)));
            this.logWeights = new double[this.Radius + 1];
            this.logRowSums = new double[settings.PitchCount];
            for (int distance = 0; distance <= this.Radius; distance++)
            {
                if ((distance & 1023) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                this.logWeights[distance] = Math.Log(this.Radius + 1.0 - distance);
            }
            this.InitializeRowSums(settings.PitchCount, token);
        }

        internal int Radius { get; }

        internal double GetLogProbability(int source, int destination)
            => this.logWeights[Math.Abs(source - destination)] - this.logRowSums[source];

        private void InitializeRowSums(int pitchCount, CancellationToken token)
        {
            double height = this.Radius + 1.0;
            for (int source = 0; source < pitchCount; source++)
            {
                if ((source & 1023) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                int left = Math.Min(source, this.Radius);
                int right = Math.Min(pitchCount - 1 - source, this.Radius);
                double sum = height + left * height - left * (left + 1.0) / 2
                    + right * height - right * (right + 1.0) / 2;
                this.logRowSums[source] = Math.Log(sum);
            }
        }
    }
}
