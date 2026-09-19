namespace ModularAudience.Audio.Processors_V4
{
    /// <summary>
    /// Log-domain Viterbi over N voiced plus N unvoiced pitch states. Unvoiced states retain
    /// pitch motion through gaps; their emissions are uniformly (1 - acoustic voiced mass)/N.
    /// Time is O(frames * pitches * pitch bandwidth); no dense transition matrix is allocated.
    /// Backpointers and acoustic tables are bounded by PyinSettings before any large allocation.
    /// </summary>
    internal sealed class PyinViterbi
    {
        private const double MinimumCandidateMass = 1e-300;
        private readonly PyinSettings settings;
        private readonly PyinObservations observations;
        private readonly CancellationToken token;
        private readonly PyinTransitions transitions;
        private readonly int[] backpointers;
        private readonly double[] voicedSources;
        private readonly double[] unvoicedSources;
        private readonly int[] voicedPredecessors;
        private readonly int[] unvoicedPredecessors;
        private readonly double logStay = Math.Log(1 - PyinTransitions.SwitchProbability);
        private readonly double logSwitch = Math.Log(PyinTransitions.SwitchProbability);
        private double[] previous;
        private double[] current;

        internal PyinViterbi(PyinSettings settings, PyinObservations observations, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            this.settings = settings;
            this.observations = observations;
            this.token = token;
            this.transitions = new PyinTransitions(settings, token);
            this.backpointers = new int[checked(settings.FrameCount * settings.StateCount)];
            this.previous = new double[settings.StateCount];
            this.current = new double[settings.StateCount];
            this.voicedSources = new double[settings.PitchCount];
            this.unvoicedSources = new double[settings.PitchCount];
            this.voicedPredecessors = new int[settings.PitchCount];
            this.unvoicedPredecessors = new int[settings.PitchCount];
        }

        internal PyinPitchFrame[] Decode()
        {
            this.Initialize();
            for (int frame = 1; frame < this.settings.FrameCount; frame++)
            {
                this.token.ThrowIfCancellationRequested();
                this.PrepareSources();
                this.AdvanceFrame(frame);
                this.Normalize(this.current);
                (this.previous, this.current) = (this.current, this.previous);
            }
            return this.Backtrack();
        }

        private void Initialize()
        {
            double initial = -Math.Log(this.settings.StateCount);
            double unvoiced = this.GetUnvoicedEmission(0);
            for (int pitch = 0; pitch < this.settings.PitchCount; pitch++)
            {
                if ((pitch & 255) == 0)
                {
                    this.token.ThrowIfCancellationRequested();
                }
                // Initially uniform pitch and equal voiced/unvoiced priors.
                this.previous[pitch] = initial + this.GetVoicedEmission(0, pitch);
                this.previous[pitch + this.settings.PitchCount] = initial + unvoiced;
            }
            this.Normalize(this.previous);
        }

        private void PrepareSources()
        {
            for (int pitch = 0; pitch < this.settings.PitchCount; pitch++)
            {
                if ((pitch & 255) == 0)
                {
                    this.token.ThrowIfCancellationRequested();
                }
                double voiced = this.previous[pitch];
                double unvoiced = this.previous[pitch + this.settings.PitchCount];
                this.voicedSources[pitch] = this.SelectLayer(voiced + this.logStay,
                    unvoiced + this.logSwitch, pitch, out this.voicedPredecessors[pitch]);
                this.unvoicedSources[pitch] = this.SelectLayer(voiced + this.logSwitch,
                    unvoiced + this.logStay, pitch, out this.unvoicedPredecessors[pitch]);
            }
        }

        private double SelectLayer(double voiced, double unvoiced, int pitch, out int predecessor)
        {
            // Equal scores choose the lower absolute state index, independently of workers.
            bool selectVoiced = voiced >= unvoiced;
            predecessor = selectVoiced ? pitch : pitch + this.settings.PitchCount;
            return selectVoiced ? voiced : unvoiced;
        }

        private void AdvanceFrame(int frame)
        {
            double unvoicedEmission = this.GetUnvoicedEmission(frame);
            int backOffset = frame * this.settings.StateCount;
            for (int pitch = 0; pitch < this.settings.PitchCount; pitch++)
            {
                this.token.ThrowIfCancellationRequested();
                var best = this.FindPredecessors(pitch);
                int unvoiced = pitch + this.settings.PitchCount;
                this.current[pitch] = best.VoicedScore + this.GetVoicedEmission(frame, pitch);
                this.current[unvoiced] = best.UnvoicedScore + unvoicedEmission;
                this.backpointers[backOffset + pitch] = best.VoicedState;
                this.backpointers[backOffset + unvoiced] = best.UnvoicedState;
            }
        }

        private (double VoicedScore, int VoicedState, double UnvoicedScore, int UnvoicedState)
            FindPredecessors(int destination)
        {
            double bestVoiced = double.NegativeInfinity;
            double bestUnvoiced = double.NegativeInfinity;
            int voicedState = -1;
            int unvoicedState = -1;
            int first = Math.Max(0, destination - this.transitions.Radius);
            int last = Math.Min(this.settings.PitchCount - 1, destination + this.transitions.Radius);
            for (int source = first; source <= last; source++)
            {
                if ((source & 255) == 0)
                {
                    this.token.ThrowIfCancellationRequested();
                }
                double transition = this.transitions.GetLogProbability(source, destination);
                Consider(this.voicedSources[source] + transition, this.voicedPredecessors[source],
                    ref bestVoiced, ref voicedState);
                Consider(this.unvoicedSources[source] + transition, this.unvoicedPredecessors[source],
                    ref bestUnvoiced, ref unvoicedState);
            }
            return (bestVoiced, voicedState, bestUnvoiced, unvoicedState);
        }

        private static void Consider(double score, int state, ref double bestScore, ref int bestState)
        {
            if (score > bestScore || (score == bestScore && (bestState < 0 || state < bestState)))
            {
                bestScore = score;
                bestState = state;
            }
        }

        private double GetVoicedEmission(int frame, int pitch)
        {
            if (this.observations.VoicedMass[frame] == 0)
            {
                return double.NegativeInfinity;
            }
            // A numerical floor on non-silent voiced emissions avoids an impossible lattice
            // after an abrupt pitch jump with exactly unit voiced mass. It never enables
            // voiced states for silence; it does not change the reported acoustic probability.
            double mass = this.observations.CandidateMass[frame * this.settings.PitchCount + pitch];
            return Math.Log(Math.Max(MinimumCandidateMass, mass));
        }

        private double GetUnvoicedEmission(int frame)
            => Math.Log(1 - this.observations.VoicedMass[frame]) - Math.Log(this.settings.PitchCount);

        private void Normalize(double[] scores)
        {
            double maximum = double.NegativeInfinity;
            for (int state = 0; state < scores.Length; state++)
            {
                if ((state & 1023) == 0)
                {
                    this.token.ThrowIfCancellationRequested();
                }
                maximum = Math.Max(maximum, scores[state]);
            }
            if (!double.IsFinite(maximum))
            {
                throw new ArithmeticException("The pYIN decoder has no finite path through the observation lattice.");
            }
            for (int state = 0; state < scores.Length; state++)
            {
                scores[state] -= maximum;
            }
        }

        private PyinPitchFrame[] Backtrack()
        {
            int state = 0;
            for (int candidate = 1; candidate < this.previous.Length; candidate++)
            {
                if ((candidate & 1023) == 0)
                {
                    this.token.ThrowIfCancellationRequested();
                }
                if (this.previous[candidate] > this.previous[state])
                {
                    state = candidate;
                }
            }
            PyinPitchFrame[] result = new PyinPitchFrame[this.settings.FrameCount];
            for (int frame = result.Length - 1; frame >= 0; frame--)
            {
                this.token.ThrowIfCancellationRequested();
                double frequency = this.GetDecodedFrequency(frame, state);
                result[frame] = new PyinPitchFrame(frequency, this.observations.VoicedMass[frame]);
                if (frame > 0)
                {
                    state = this.backpointers[frame * this.settings.StateCount + state];
                }
            }
            this.token.ThrowIfCancellationRequested();
            return result;
        }

        private double GetDecodedFrequency(int frame, int state)
        {
            if (state >= this.settings.PitchCount)
            {
                return 0;
            }
            // Refine only a candidate mapped to the DECODED bin, never the strongest candidate
            // of a different bin. Thus refinement stays within half a grid step of the path.
            double refined = this.observations.RefinedFrequency[frame * this.settings.PitchCount + state];
            return refined > 0 ? refined : this.settings.GetFrequency(state);
        }
    }
}
