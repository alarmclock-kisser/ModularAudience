namespace ModularAudience.Audio.Processors_V4
{
    /// <summary>Call-local acoustic emissions and the strongest refined candidate in each pitch bin.</summary>
    internal sealed class PyinObservations
    {
        private readonly int pitchCount;

        internal PyinObservations(PyinSettings settings)
        {
            this.pitchCount = settings.PitchCount;
            int cells = checked(settings.FrameCount * settings.PitchCount);
            this.CandidateMass = new double[cells];
            this.RefinedFrequency = new double[cells];
            this.VoicedMass = new double[settings.FrameCount];
        }

        internal double[] CandidateMass { get; }
        internal double[] RefinedFrequency { get; }
        internal double[] VoicedMass { get; }

        internal void CompleteFrame(int frame, CancellationToken token)
        {
            int offset = frame * this.pitchCount;
            double total = 0;
            for (int pitch = 0; pitch < this.pitchCount; pitch++)
            {
                if ((pitch & 1023) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                total += this.CandidateMass[offset + pitch];
            }
            this.VoicedMass[frame] = Math.Clamp(total, 0, 1);
            if (total > 1)
            {
                for (int pitch = 0; pitch < this.pitchCount; pitch++)
                {
                    this.CandidateMass[offset + pitch] /= total;
                }
            }
        }
    }
}
