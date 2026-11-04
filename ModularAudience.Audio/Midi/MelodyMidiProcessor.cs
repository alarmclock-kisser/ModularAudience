namespace ModularAudience.Audio.Midi;

/// <summary>Predominant, monophonic melody tracking in a mixture. No instrument classifier is implied.</summary>
internal static class MelodyMidiProcessor
{
    private const int Rest = 128;
    private const int States = 129;

    internal static MidiFileData Convert(AudioObj audio, double bpm, int workers, CancellationToken token, IProgress<double>? progress)
    {
        progress?.Report(0);
        var analysis = PolyphonicPitchAnalyzer.Analyze(audio, workers, token, progress);
        var notes = Extract(analysis, bpm, token);
        var track = new MidiTrackData { Index = 0, Name = $"{audio.Name} - Main melody" };
        track.ExtendLengthTo((long)Math.Ceiling(analysis.SampleCount / (double)analysis.SampleRate * bpm / 60 * 960));
        track.Notes.AddRange(notes);
        progress?.Report(1);
        return MidiFileData.CreateGenerated([track], 960, bpm, string.IsNullOrWhiteSpace(audio.FilePath) ? audio.Name : audio.FilePath);
    }

    internal static List<MidiNoteData> Extract(PolyphonicAnalysis analysis, double bpm, CancellationToken token)
    {
        int frames = analysis.Strengths.Length;
        if (frames == 0) return [];
        // A bass fundamental must not erase a moving lead whenever they happen to meet
        // at an octave/fifth. Restore such upper candidates only when the recording also
        // contains a melodic neighborhood; an isolated harmonic tone has none.
        if (analysis.RawStrengths is { } raw)
        {
            double[] activity = Enumerable.Range(0, 128).Select(p => raw.Sum(f => (double)f[p])).ToArray();
            for (int p = 48; p <= PolyphonicPitchAnalyzer.HighestNote; p++)
            {
                token.ThrowIfCancellationRequested();
                int neighbors = Enumerable.Range(Math.Max(48, p - 7), Math.Min(PolyphonicPitchAnalyzer.HighestNote, p + 7) - Math.Max(48, p - 7) + 1)
                    .Count(q => Math.Abs(q - p) is >= 1 and <= 7 && activity[q] > activity[p] * 0.18);
                if (neighbors < 2) continue;
                for (int t = 0; t < frames; t++)
                    if (raw[t][p] > 0 && analysis.Strengths[t][p] == 0)
                    {
                        // Only recover a lead above a longer-held bass. A moving lower
                        // fundamental with matching upper harmonics is one instrument.
                        bool heldBass = false;
                        foreach (int interval in new[] { 12, 19, 24, 28, 31, 34, 36 })
                        {
                            int lower = p - interval;
                            if (lower < PolyphonicPitchAnalyzer.LowestNote || raw[t][lower] <= 0) continue;
                            foreach (int probe in new[] { t - 60, t + 60 })
                                if (probe >= 0 && probe < frames && raw[probe][lower] >= raw[t][lower] * 0.25f
                                    && raw[probe][p] < raw[t][p] * 0.1f) heldBass = true;
                        }
                        if (heldBass) analysis.Strengths[t][p] = raw[t][p] * 0.9f;
                    }
            }
        }
        float global = analysis.Strengths.Max(f => f.Max());
        if (global < 1e-6f) return [];
        double rmsFloor = Math.Max(1e-7, analysis.Rms.Max() * 0.006);
        // The soft upper-register preference cannot force an isolated bass/high instrument
        // into a vocal range; only supported spectral candidates enter the path.
        var previous = Enumerable.Repeat(double.NegativeInfinity, States).ToArray();
        previous[Rest] = 0;
        var back = new byte[checked(frames * States)];
        for (int t = 0; t < frames; t++)
        {
            token.ThrowIfCancellationRequested();
            float[] strength = analysis.Strengths[t];
            float peak = strength.Max();
            bool voiced = analysis.Rms[t] > rmsFloor && peak > global * 0.005;
            var current = Enumerable.Repeat(double.NegativeInfinity, States).ToArray();
            for (int p = PolyphonicPitchAnalyzer.LowestNote; p <= Rest; p++)
            {
                if (p < Rest && (p > PolyphonicPitchAnalyzer.HighestNote || !voiced || strength[p] < Math.Max(global * 0.003f, peak * 0.055f))) continue;
                double emission = p == Rest ? (voiced ? -0.45 : 0.3)
                    : 0.8 + 0.55 * Math.Log(Math.Max(1e-9, strength[p] / peak))
                        + 0.65 * Math.Clamp((p - 48) / 24.0, 0, 1);
                double best = double.NegativeInfinity;
                int predecessor = Rest;
                for (int q = PolyphonicPitchAnalyzer.LowestNote; q <= Rest; q++)
                {
                    double transition = p == q ? 0 : p == Rest || q == Rest ? 0.6
                        : 1.25 + 0.09 * Math.Abs(p - q) + (Math.Abs(p - q) >= 12 ? 0.6 : 0);
                    double score = previous[q] - transition;
                    if (score > best) { best = score; predecessor = q; }
                }
                current[p] = best + emission;
                back[t * States + p] = (byte)predecessor;
            }
            previous = current;
        }
        int state = Array.IndexOf(previous, previous.Max());
        int[] path = new int[frames];
        for (int t = frames - 1; t >= 0; t--)
        {
            path[t] = state == Rest ? -1 : state;
            state = back[t * States + state];
        }
        // Bridge only tiny pitch-detection gaps, never genuine silent rests.
        for (int start = 1; start < frames - 1; start++)
        {
            if (path[start - 1] < 0 || path[start] == path[start - 1]) continue;
            int end = start;
            while (end < frames && end - start < 4 && path[end] != path[start - 1] && analysis.Rms[end] > rmsFloor) end++;
            if (end < frames && path[end] == path[start - 1]) Array.Fill(path, path[start - 1], start, end - start);
        }
        List<MidiNoteData> notes = [];
        float maxRms = analysis.Rms.Max();
        for (int start = 0; start < frames;)
        {
            token.ThrowIfCancellationRequested();
            int pitch = path[start], end = start + 1;
            while (end < frames && path[end] == pitch) end++;
            double seconds = (end - start) * analysis.HopSize / (double)analysis.SampleRate;
            if (pitch >= 0 && seconds >= 0.065)
            {
                double beginSeconds = start * analysis.HopSize / (double)analysis.SampleRate;
                double endSeconds = Math.Min(analysis.SampleCount, (long)end * analysis.HopSize) / (double)analysis.SampleRate;
                double rms = analysis.Rms.Skip(start).Take(end - start).Average();
                long begin = (long)Math.Round(beginSeconds * bpm / 60 * 960);
                long finish = (long)Math.Round(endSeconds * bpm / 60 * 960);
                notes.Add(new MidiNoteData { NoteNumber = pitch, Channel = 1, StartTick = begin, DurationTicks = Math.Max(1, finish - begin),
                    Velocity = Math.Clamp((int)Math.Round(110 * Math.Sqrt(rms / Math.Max(1e-7f, maxRms))), 30, 120) });
            }
            start = end;
        }
        return notes;
    }
}
