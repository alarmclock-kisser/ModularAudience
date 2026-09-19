namespace ModularAudience.Audio.Processors_V4
{
    /// <summary>
    /// Deterministic, monophonic probabilistic YIN followed by joint pitch/voicing Viterbi decoding.
    /// Frames are centered at frameIndex * hopSize, including the final quotient frame.
    /// </summary>
    /// <remarks>
    /// Implements the ingredients of Mauch and Dixon's pYIN (2014): a beta-distributed YIN
    /// threshold, a Boltzmann trough prior, a no-trough fallback and a two-layer pitch HMM.
    /// This is an independent implementation, not a bit-exact reproduction. Adaptations include
    /// power-of-two rectangular windows, peak normalization, clipped boundary-lag interpolation,
    /// an endpoint-inclusive approximately ten-cent grid and a quantized transition radius.
    /// No pretrained model, instrument classifier or random sampling is involved.
    /// All mutable scratch belongs to this call or its workers. The caller must not mutate mono
    /// during analysis. Cancellation surrounds each bounded, non-interruptible managed FFT.
    /// </remarks>
    internal static class PyinPitchTracker
    {
        internal static PyinPitchFrame[] Track(float[] mono, int sampleRate, int hopSize,
            double minimumHz, double maximumHz, int threads, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(mono);
            PyinSettings.ValidateArguments(sampleRate, hopSize, minimumHz, maximumHz, threads);
            if (mono.Length == 0)
            {
                return Array.Empty<PyinPitchFrame>();
            }
            PyinSettings settings = PyinSettings.Create(mono.Length, sampleRate, hopSize,
                minimumHz, maximumHz, threads);
            ValidateSamples(mono, token);
            PyinObservations observations = new(settings);
            AnalyzeFrames(mono, settings, observations, token);
            return new PyinViterbi(settings, observations, token).Decode();
        }

        private static void ValidateSamples(float[] mono, CancellationToken token)
        {
            for (int i = 0; i < mono.Length; i++)
            {
                if ((i & 1023) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                if (!float.IsFinite(mono[i]))
                {
                    throw new ArgumentException($"pYIN samples must be finite; invalid value at index {i}.", nameof(mono));
                }
            }
            token.ThrowIfCancellationRequested();
        }

        private static void AnalyzeFrames(float[] mono, PyinSettings settings,
            PyinObservations observations, CancellationToken token)
        {
            ParallelOptions options = new()
            {
                CancellationToken = token,
                MaxDegreeOfParallelism = settings.WorkerCount
            };
            Parallel.For(0, settings.WorkerCount, options, worker =>
            {
                token.ThrowIfCancellationRequested();
                PyinYin yin = new(settings);
                PyinCandidates candidates = new(settings);
                for (int frame = worker; frame < settings.FrameCount; frame += settings.WorkerCount)
                {
                    token.ThrowIfCancellationRequested();
                    if (yin.Analyze(mono, (long)frame * settings.HopSize, token))
                    {
                        candidates.Write(yin.NormalizedDifference, observations, frame, token);
                    }
                }
            });
            token.ThrowIfCancellationRequested();
        }
    }

    /// <summary>
    /// FrequencyHz is zero for a decoded unvoiced frame. VoicedProbability is the acoustic
    /// candidate mass before HMM decoding, not a posterior or calibrated instrument confidence.
    /// </summary>
    internal sealed record PyinPitchFrame(double FrequencyHz, double VoicedProbability);
}
