namespace ModularAudience.Audio.Midi;

internal static class PolyphonicMidiProcessor
{
    private const int Ppq = 960;
    private const double MinimumNoteSeconds = 0.06;
    private const double MaximumGapSeconds = 0.04;

    internal static MidiFileData Convert(AudioObj audio, double bpm, int maxWorkers, CancellationToken cancellationToken, IProgress<double>? progress)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(0);
        PolyphonicAnalysis analysis = PolyphonicPitchAnalyzer.Analyze(audio, maxWorkers, cancellationToken, progress);
        List<DetectedNote> detected = TrackNotes(analysis, bpm, cancellationToken);
        progress?.Report(0.88);
        HashSet<int> melody = FindMelody(detected, bpm, cancellationToken);
        MidiTrackData[] tracks = CreateTracks(audio.Name, detected, melody, analysis, bpm);
        cancellationToken.ThrowIfCancellationRequested();
        string path = string.IsNullOrWhiteSpace(audio.FilePath) ? audio.Name : audio.FilePath;
        MidiFileData midi = MidiFileData.CreateGenerated(tracks, Ppq, bpm, path);
        LogCollection.Log($"Polyphonic MIDI: {detected.Count} notes, {tracks[0].Notes.Count} estimated melody notes, {tracks[1].Notes.Count} estimated accompaniment notes. Instrument identities are not inferred.");
        progress?.Report(1);
        return midi;
    }

    private static List<DetectedNote> TrackNotes(PolyphonicAnalysis analysis, double bpm, CancellationToken cancellationToken)
    {
        List<DetectedNote> notes = [];
        float maximum = analysis.Strengths.Max(frame => frame.Max());
        float rmsFloor = Math.Max(1e-7f, analysis.Rms.Max() * 0.003f);
        if (maximum < 1e-5f)
        {
            return notes;
        }
        for (int pitch = PolyphonicPitchAnalyzer.LowestNote; pitch <= PolyphonicPitchAnalyzer.HighestNote; pitch++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            float peak = analysis.Strengths.Max(frame => frame[pitch]);
            float onThreshold = Math.Max(maximum * 0.02f, peak * 0.15f);
            TrackPitch(analysis, pitch, onThreshold, rmsFloor, maximum, bpm, notes, cancellationToken);
        }
        return notes.OrderBy(note => note.Midi.StartTick + note.Midi.DurationTicks)
            .ThenBy(note => note.Midi.StartTick).ThenBy(note => note.Midi.NoteNumber).ToList();
    }

    private static void TrackPitch(PolyphonicAnalysis analysis, int pitch, float onThreshold, float rmsFloor,
        float maximum, double bpm, List<DetectedNote> notes, CancellationToken cancellationToken)
    {
        NoteRun run = new();
        int maximumGap = Math.Max(1, (int)Math.Round(MaximumGapSeconds * analysis.SampleRate / analysis.HopSize));
        for (int frame = 0; frame <= analysis.Strengths.Length; frame++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool audible = frame < analysis.Strengths.Length && analysis.Rms[frame] > rmsFloor;
            float strength = frame < analysis.Strengths.Length ? analysis.Strengths[frame][pitch] : 0;
            float threshold = run.Start >= 0 ? onThreshold * 0.45f : onThreshold;
            if (audible && strength >= threshold)
            {
                if (run.Start < 0)
                {
                    run.Start = frame;
                }
                run.Last = frame;
                run.VoicedFrames++;
                run.TotalStrength += strength;
            }
            else if (run.Start >= 0 && (!audible || frame - run.Last > maximumGap))
            {
                AddNote(notes, run, pitch, analysis, maximum, bpm);
                run = new NoteRun();
            }
        }
    }

    private static void AddNote(List<DetectedNote> notes, NoteRun run, int pitch, PolyphonicAnalysis analysis, float maximum, double bpm)
    {
        long startSample = (long)run.Start * analysis.HopSize;
        long endSample = Math.Min(analysis.SampleCount, (run.Last + 1L) * analysis.HopSize);
        if (endSample - startSample < Math.Ceiling(MinimumNoteSeconds * analysis.SampleRate) || run.VoicedFrames < 3)
        {
            return;
        }
        double start = startSample / (double)analysis.SampleRate;
        double end = endSample / (double)analysis.SampleRate;
        double strength = run.TotalStrength / run.VoicedFrames / maximum;
        long startTick = (long)Math.Round(start * bpm / 60 * Ppq);
        long endTick = (long)Math.Round(end * bpm / 60 * Ppq);
        MidiNoteData note = new()
        {
            NoteNumber = pitch,
            Channel = 1,
            Velocity = Math.Clamp((int)Math.Round(127 * Math.Sqrt(strength)), 1, 127),
            StartTick = startTick,
            DurationTicks = Math.Max(1, endTick - startTick)
        };
        notes.Add(new DetectedNote(note, strength));
    }

    private static HashSet<int> FindMelody(List<DetectedNote> notes, double bpm, CancellationToken cancellationToken)
    {
        double[] scores = new double[notes.Count];
        int[] previous = Enumerable.Repeat(-1, notes.Count).ToArray();
        int best = -1;
        for (int index = 0; index < notes.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double ownScore = MelodyWeight(notes[index], bpm);
            scores[index] = ownScore;
            for (int earlier = 0; earlier < index; earlier++)
            {
                if ((earlier & 255) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                MidiNoteData left = notes[earlier].Midi;
                MidiNoteData right = notes[index].Midi;
                if (left.StartTick + left.DurationTicks > right.StartTick)
                {
                    continue;
                }
                double gap = Seconds(right.StartTick - left.StartTick - left.DurationTicks, bpm);
                double transition = gap < 0.5 ? Math.Min(0.12, Math.Abs(left.NoteNumber - right.NoteNumber) * 0.006) : 0;
                double candidate = scores[earlier] + ownScore - transition;
                if (candidate > scores[index])
                {
                    scores[index] = candidate;
                    previous[index] = earlier;
                }
            }
            if (best < 0 || scores[index] > scores[best])
            {
                best = index;
            }
        }
        HashSet<int> selected = [];
        while (best >= 0)
        {
            selected.Add(best);
            best = previous[best];
        }
        return selected;
    }

    private static double MelodyWeight(DetectedNote note, double bpm)
    {
        double duration = Seconds(note.Midi.DurationTicks, bpm);
        double register = 0.7 + 0.65 * Math.Clamp((note.Midi.NoteNumber - 44) / 36.0, 0, 1);
        return duration * (1 + Math.Min(0.5, duration)) * register * (0.9 + 0.1 * note.Strength);
    }

    private static MidiTrackData[] CreateTracks(string name, List<DetectedNote> notes, HashSet<int> melody, PolyphonicAnalysis analysis, double bpm)
    {
        string prefix = string.IsNullOrWhiteSpace(name) ? "Audio" : name;
        MidiTrackData[] tracks =
        [
            new() { Index = 0, Name = $"{prefix} - Melody (estimated)" },
            new() { Index = 1, Name = $"{prefix} - Accompaniment (estimated)" }
        ];
        long length = (long)Math.Ceiling(analysis.SampleCount / (double)analysis.SampleRate * bpm / 60 * Ppq);
        foreach (MidiTrackData track in tracks)
        {
            track.ExtendLengthTo(length);
        }
        for (int index = 0; index < notes.Count; index++)
        {
            int role = melody.Contains(index) ? 0 : 1;
            MidiNoteData note = notes[index].Midi;
            tracks[role].Notes.Add(new MidiNoteData
            {
                NoteNumber = note.NoteNumber,
                Channel = role + 1,
                Velocity = note.Velocity,
                StartTick = note.StartTick,
                DurationTicks = note.DurationTicks
            });
        }
        foreach (MidiTrackData track in tracks)
        {
            track.Notes.Sort((left, right) => left.StartTick.CompareTo(right.StartTick));
        }
        return tracks;
    }

    private static double Seconds(long ticks, double bpm) => ticks * 60.0 / bpm / Ppq;

    private sealed class NoteRun
    {
        internal int Start = -1;
        internal int Last;
        internal int VoicedFrames;
        internal double TotalStrength;
    }

    private sealed record DetectedNote(MidiNoteData Midi, double Strength);
}
