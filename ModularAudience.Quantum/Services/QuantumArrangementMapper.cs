using ModularAudience.Quantum.Models;

namespace ModularAudience.Quantum.Services;

/// <summary>One shared weighted measurement per step preserves measured cross-track correlations.</summary>
public sealed class QuantumArrangementMapper
{
    public QuantumBeatSequence Map(string circuitQasm, QuantumJobResponse response, int qubits,
        QuantumBeatOptions options, IReadOnlyList<QuantumTrackMapping> tracks, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(circuitQasm);
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate(qubits, tracks);
        if (response.ExecutionLatency < TimeSpan.Zero) throw new ArgumentException("Execution latency cannot be negative.");
        var counts = QuantumCounts.Snapshot(response.Counts, qubits);
        var trackSnapshot = Array.AsReadOnly(tracks.ToArray());
        var notes = new List<QuantumBeatNote>();
        cancellationToken.ThrowIfCancellationRequested();
        if (counts.Count == 0) return new(circuitQasm, notes.AsReadOnly(), counts, response.ExecutionLatency, options, trackSnapshot, qubits);
        var states = counts.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
        long total = states.Sum(state => (long)state.Value);
        var cdf = new long[states.Length];
        for (int i = 0; i < states.Length; i++) cdf[i] = states[i].Value + (i == 0 ? 0 : cdf[i - 1]);
        var random = new Random(options.Seed);
        int stepCount = options.Bars * options.StepsPerBar;
        int phraseLength = options.Arrangement switch
        {
            QuantumBarArrangement.RepeatFirstBar => options.StepsPerBar,
            QuantumBarArrangement.AlternateTwoBars => Math.Min(stepCount, options.StepsPerBar * 2),
            _ => stepCount
        };
        var phrase = new int[phraseLength];
        for (int step = 0; step < phrase.Length; step++)
        {
            double draw = random.NextDouble() * total;
            int low = 0, high = cdf.Length - 1;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (draw < cdf[middle]) high = middle; else low = middle + 1;
            }
            phrase[step] = low;
        }

        for (int step = 0; step < stepCount; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int phraseStep = step % phraseLength;
            var state = states[phrase[phraseStep]];
            int bits = Convert.ToInt32(state.Key, 2);
            var stepNotes = new List<QuantumBeatNote>();
            for (int trackIndex = 0; trackIndex < tracks.Count; trackIndex++)
            {
                QuantumTrackMapping track = tracks[trackIndex];
                if (!track.Enabled || track.Gain == 0) continue;
                bool measured = ((bits & (1 << track.TriggerQubit)) != 0) ^ track.InvertTrigger;
                var laneRandom = new Random(unchecked(options.Seed * 397 ^ phraseStep * 7919 ^ trackIndex * 104729));
                bool ghost = !measured && options.GhostNotes && laneRandom.NextDouble() < options.GhostChance;
                if ((!measured && !ghost) || laneRandom.NextDouble() >= Math.Min(1, options.Density * track.DensityScale)) continue;
                double velocity = options.QuantumVelocity
                    ? Lerp(options.MinimumVelocity, options.MaximumVelocity, state.Value / (double)total) : options.MaximumVelocity;
                velocity *= track.Gain * (ghost ? options.GhostVelocityScale : 1);
                double start = ((step + track.StepOffset) % stepCount + stepCount) % stepCount;
                if (((int)start & 1) != 0) start += options.Swing;
                double pitch = options.PitchEnabled ? Lerp(options.MinimumPitchCents, options.MaximumPitchCents, Code(0)) : 0;
                pitch = Math.Round(pitch / options.PitchStepCents, MidpointRounding.AwayFromZero) * options.PitchStepCents;
                pitch = Math.Clamp(pitch, options.MinimumPitchCents, options.MaximumPitchCents);
                stepNotes.Add(new(trackIndex, start,
                    options.VaryDuration ? Lerp(options.MinimumDurationSteps, options.MaximumDurationSteps, Code(2)) : options.MinimumDurationSteps,
                    velocity, (options.PitchEnabled ? pitch : 0) + track.PitchOffsetCents,
                    options.TimeStretchEnabled ? Lerp(options.MinimumStretch, options.MaximumStretch, Code(4)) : 1,
                    options.VarispeedEnabled ? Lerp(options.MinimumSpeed, options.MaximumSpeed, Code(6)) : 1,
                    options.CutEnabled ? options.MaximumCutOffsetMs * Code(8) : 0,
                    options.ReverseEnabled && (bits & (1 << ((options.ModifierQubit + 10) % qubits))) != 0, ghost, state.Key));

                double Code(int offset)
                {
                    int first = (options.ModifierQubit + offset) % qubits, second = (first + 1) % qubits;
                    return (((bits >> first) & 1) | (((bits >> second) & 1) << 1)) / 3.0;
                }
            }
            notes.AddRange(stepNotes);
        }
        // Resolve collisions after lane offsets/swing, not before moving notes to their actual onsets.
        IEnumerable<QuantumBeatNote> arranged = options.Interleaved
            ? notes.GroupBy(note => note.StartStep).Select(group => group.OrderByDescending(note => note.Velocity).ThenBy(note => note.TrackIndex).First()) : notes;
        return new(circuitQasm, arranged.OrderBy(note => note.StartStep).ThenBy(note => note.TrackIndex).ToList().AsReadOnly(),
            counts, response.ExecutionLatency, options, trackSnapshot, qubits);
    }

    private static double Lerp(double minimum, double maximum, double amount) => minimum + (maximum - minimum) * amount;
}
