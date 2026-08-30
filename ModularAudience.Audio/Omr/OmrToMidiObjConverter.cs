using ModularAudience.Audio.Midi;

namespace ModularAudience.Audio.Omr;

public static class OmrToMidiObjConverter
{
    public static MidiFileData Convert(
        OmrObj omr,
        int ticksPerQuarterNote = 960,
        double bpm = 120.0,
        int velocity = 80,
        int channel = 0,
        double minimumConfidence = 0.45,
        int beatsPerMeasure = 4,
        bool includeCandidates = true,
        string? filePath = null,
        double pitchFrequency = 440.0)
    {
        ArgumentNullException.ThrowIfNull(omr);
        if (ticksPerQuarterNote <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ticksPerQuarterNote));
        }

        if (bpm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bpm));
        }

        if (beatsPerMeasure <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(beatsPerMeasure));
        }

        if (pitchFrequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pitchFrequency));
        }

        velocity = Math.Clamp(velocity, 1, 127);
        channel = Math.Clamp(channel, 0, 15);
        minimumConfidence = Math.Clamp(minimumConfidence, 0.0, 1.0);

        int trackCount = Math.Max(1, omr.Pages
            .SelectMany(page => page.StaffSystems)
            .Select(system => system.Staves.Count)
            .DefaultIfEmpty(1)
            .Max());
        var tracks = Enumerable.Range(0, trackCount)
            .Select(index => new MidiTrackData
            {
                Index = index,
                Name = $"OMR Staff {index + 1}"
            })
            .ToArray();
        long measureTicks = checked((long)ticksPerQuarterNote * beatsPerMeasure);
        long currentTick = 0;

        foreach (OmrPage page in omr.Pages.OrderBy(page => page.PageIndex))
        {
            foreach (OmrStaffSystem system in page.StaffSystems.OrderBy(system => system.Bounds.Y))
            {
                foreach (OmrMeasure measure in system.Measures.OrderBy(measure => measure.Number ?? int.MaxValue).ThenBy(measure => measure.Bounds.X))
                {
                    foreach (OmrNoteEvent note in measure.Events)
                    {
                        if (!CanConvert(note, includeCandidates, minimumConfidence) || note.StaffIndex is not int staffIndex || staffIndex < 0 || staffIndex >= tracks.Length)
                        {
                            continue;
                        }

                        int noteNumber = ToMidiNoteNumber(note);
                        long startTick = currentTick + ToStartOffset(note.Bounds, measure.Bounds, measureTicks);
                        long durationTicks = ToDurationTicks(note.Duration, note.IsDotted, ticksPerQuarterNote, measureTicks);
                        tracks[staffIndex].Notes.Add(new MidiNoteData
                        {
                            NoteNumber = noteNumber,
                            Channel = channel,
                            Velocity = velocity,
                            StartTick = startTick,
                            DurationTicks = durationTicks
                        });
                    }

                    currentTick += measureTicks;
                }
            }
        }

        foreach (MidiTrackData track in tracks)
        {
            track.Notes.Sort((left, right) => left.StartTick.CompareTo(right.StartTick));
            track.ExtendLengthTo(currentTick);
        }

        return MidiFileData.CreateGenerated(
            tracks,
            ticksPerQuarterNote,
            bpm,
            filePath ?? omr.SourceFilePath,
            pitchFrequency);
    }

    private static bool CanConvert(OmrNoteEvent note, bool includeCandidates, double minimumConfidence)
    {
        return !note.IsRest &&
               (!note.IsCandidate || includeCandidates) &&
               note.Confidence >= minimumConfidence &&
               note.Step is not null &&
               note.Octave is not null &&
               note.Duration is not null;
    }

    private static int ToMidiNoteNumber(OmrNoteEvent note)
    {
        int semitone = note.Step switch
        {
            "C" => 0,
            "D" => 2,
            "E" => 4,
            "F" => 5,
            "G" => 7,
            "A" => 9,
            "B" => 11,
            _ => throw new InvalidOperationException($"Unsupported note step: {note.Step}")
        };

        return Math.Clamp((note.Octave!.Value + 1) * 12 + semitone + (note.Alter ?? 0), 0, 127);
    }

    private static long ToStartOffset(OmrRect noteBounds, OmrRect measureBounds, long measureTicks)
    {
        if (measureBounds.Width <= 0)
        {
            return 0;
        }

        double position = Math.Clamp((noteBounds.X - measureBounds.X) / (double)measureBounds.Width, 0.0, 1.0);
        return (long)Math.Round(position * measureTicks);
    }

    private static long ToDurationTicks(string? duration, bool isDotted, int ticksPerQuarterNote, long measureTicks)
    {
        long ticks = duration switch
        {
            "whole" => ticksPerQuarterNote * 4L,
            "half" => ticksPerQuarterNote * 2L,
            "quarter" => ticksPerQuarterNote,
            "eighth" => ticksPerQuarterNote / 2L,
            "sixteenth" => ticksPerQuarterNote / 4L,
            _ => ticksPerQuarterNote
        };

        if (isDotted)
        {
            ticks = checked(ticks * 3 / 2);
        }

        return Math.Clamp(ticks, 1, measureTicks);
    }
}
