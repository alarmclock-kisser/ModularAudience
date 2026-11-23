using NAudio.Midi;

namespace ModularAudience.Audio.Vocal;

public interface IVocalMidiNote
{
    int NoteNumber { get; }
    int Channel { get; }
    int Velocity { get; }
    long StartTick { get; }
    long DurationTicks { get; }
}

public interface IVocalMidiTrack
{
    int Index { get; }
    string Name { get; }
    IReadOnlyList<IVocalMidiNote> Notes { get; }
    long LengthTicks { get; }
}

public interface IVocalMidiFile
{
    string FilePath { get; }
    int TicksPerQuarterNote { get; }
    double DefaultBpm { get; }
    double PitchFrequency { get; }
    int TimeSignatureNumerator { get; }
    int TimeSignatureDenominator { get; }
    long TicksPerBar { get; }
    long LengthTicks { get; }
    double LengthBeats { get; }
    IReadOnlyList<IVocalMidiTrack> Tracks { get; }
}

public sealed class VocalMidiNoteData : IVocalMidiNote
{
    public int NoteNumber { get; init; }
    public int Channel { get; init; }
    public int Velocity { get; init; }
    public long StartTick { get; init; }
    public long DurationTicks { get; init; }
}

public sealed class VocalMidiTrackData : IVocalMidiTrack
{
    private readonly List<VocalMidiNoteData> notes = [];

    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<VocalMidiNoteData> MutableNotes => notes;
    public IReadOnlyList<IVocalMidiNote> Notes => notes;
    public long LengthTicks { get; private set; }

    public void AddNote(VocalMidiNoteData note)
    {
        ArgumentNullException.ThrowIfNull(note);
        notes.Add(note);
        LengthTicks = Math.Max(LengthTicks, note.StartTick + Math.Max(1, note.DurationTicks));
    }

    public void ExtendLengthTo(long lengthTicks) =>
        LengthTicks = Math.Max(LengthTicks, Math.Max(0, lengthTicks));
}

public sealed class VocalMidiFileData : IVocalMidiFile
{
    private readonly IReadOnlyList<VocalMidiTrackData> tracks;
    private readonly IReadOnlyList<IVocalMidiTrack> vocalTracks;

    public string FilePath { get; }
    public int TicksPerQuarterNote { get; }
    public double DefaultBpm { get; }
    public double PitchFrequency { get; }
    public int TimeSignatureNumerator { get; }
    public int TimeSignatureDenominator { get; }
    public long TicksPerBar => Math.Max(
        1L, (long)TicksPerQuarterNote * TimeSignatureNumerator * 4 / TimeSignatureDenominator);
    public long LengthTicks => tracks.Count == 0 ? 0 : tracks.Max(track => track.LengthTicks);
    public double LengthBeats => LengthTicks / (double)Math.Max(1, TicksPerQuarterNote);
    public IReadOnlyList<VocalMidiTrackData> VocalTracks => tracks;
    public IReadOnlyList<IVocalMidiTrack> Tracks => vocalTracks;

    public VocalMidiFileData(
        string filePath,
        int ticksPerQuarterNote,
        double defaultBpm,
        IEnumerable<VocalMidiTrackData> tracks,
        double pitchFrequency = 440,
        int timeSignatureNumerator = 4,
        int timeSignatureDenominator = 4)
    {
        if (ticksPerQuarterNote <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ticksPerQuarterNote));
        }

        if (!double.IsFinite(defaultBpm) || defaultBpm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultBpm));
        }

        if (timeSignatureNumerator <= 0 || timeSignatureDenominator <= 0
            || (timeSignatureDenominator & (timeSignatureDenominator - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeSignatureDenominator));
        }

        ArgumentNullException.ThrowIfNull(tracks);
        FilePath = filePath ?? string.Empty;
        TicksPerQuarterNote = ticksPerQuarterNote;
        DefaultBpm = Math.Clamp(defaultBpm, 20, 400);
        PitchFrequency = Math.Clamp(pitchFrequency, 1, 1000);
        TimeSignatureNumerator = timeSignatureNumerator;
        TimeSignatureDenominator = timeSignatureDenominator;
        this.tracks = tracks.ToArray();
        vocalTracks = this.tracks.Cast<IVocalMidiTrack>().ToArray();
    }

    public static VocalMidiFileData FromTracks(
        string filePath,
        int ticksPerQuarterNote,
        double defaultBpm,
        IEnumerable<IVocalMidiTrack> sourceTracks,
        double pitchFrequency = 440,
        int timeSignatureNumerator = 4,
        int timeSignatureDenominator = 4)
    {
        ArgumentNullException.ThrowIfNull(sourceTracks);
        VocalMidiTrackData[] tracks = sourceTracks.Select(source =>
        {
            ArgumentNullException.ThrowIfNull(source);
            VocalMidiTrackData track = new() { Index = source.Index, Name = source.Name };
            foreach (IVocalMidiNote note in source.Notes)
            {
                track.AddNote(new VocalMidiNoteData
                {
                    NoteNumber = note.NoteNumber,
                    Channel = note.Channel,
                    Velocity = note.Velocity,
                    StartTick = note.StartTick,
                    DurationTicks = note.DurationTicks,
                });
            }
            track.ExtendLengthTo(source.LengthTicks);
            return track;
        }).ToArray();
        return new VocalMidiFileData(
            filePath, ticksPerQuarterNote, defaultBpm, tracks, pitchFrequency,
            timeSignatureNumerator, timeSignatureDenominator);
    }

    public static VocalMidiFileData Load(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("The MIDI file was not found.", filePath);
        }

        MidiFile midiFile = new(filePath, strictChecking: false);
        int ticksPerQuarterNote = midiFile.DeltaTicksPerQuarterNote;
        if (ticksPerQuarterNote <= 0)
        {
            throw new InvalidDataException("The MIDI file does not use supported PPQ timing.");
        }

        double bpm = 120;
        int timeNumerator = 4;
        int timeDenominator = 4;
        bool hasTimeSignature = false;
        List<VocalMidiTrackData> tracks = [];
        for (int trackIndex = 0; trackIndex < midiFile.Events.Count(); trackIndex++)
        {
            IList<MidiEvent> events = midiFile.Events[trackIndex];
            VocalMidiTrackData track = new() { Index = trackIndex, Name = FindTrackName(events, tracks.Count + 1) };
            Dictionary<(int Channel, int Note), Queue<(long Tick, int Velocity)>> activeNotes = [];
            long lastTick = 0;
            foreach (MidiEvent midiEvent in events)
            {
                lastTick = Math.Max(lastTick, midiEvent.AbsoluteTime);
                if (midiEvent is TimeSignatureEvent signature && !hasTimeSignature)
                {
                    if (signature.Numerator <= 0 || signature.Denominator <= 0
                        || (signature.Denominator & (signature.Denominator - 1)) != 0)
                    {
                        throw new InvalidDataException("The MIDI file contains an invalid time signature.");
                    }

                    timeNumerator = signature.Numerator;
                    timeDenominator = signature.Denominator;
                    hasTimeSignature = true;
                }
                if (midiEvent is TempoEvent tempo && tempo.MicrosecondsPerQuarterNote > 0)
                {
                    bpm = 60_000_000.0 / tempo.MicrosecondsPerQuarterNote;
                }

                if (midiEvent is not NoteEvent note || note.NoteNumber is < 0 or > 127)
                {
                    continue;
                }

                var key = (note.Channel, note.NoteNumber);
                if (note.CommandCode == MidiCommandCode.NoteOn && note.Velocity > 0)
                {
                    if (!activeNotes.TryGetValue(key, out Queue<(long Tick, int Velocity)>? starts))
                    {
                        activeNotes[key] = starts = new Queue<(long Tick, int Velocity)>();
                    }

                    starts.Enqueue((note.AbsoluteTime, note.Velocity));
                }
                else if (note.CommandCode == MidiCommandCode.NoteOff
                    || note.CommandCode == MidiCommandCode.NoteOn && note.Velocity == 0)
                {
                    if (activeNotes.TryGetValue(key, out Queue<(long Tick, int Velocity)>? starts) && starts.Count > 0)
                    {
                        (long start, int velocity) = starts.Dequeue();
                        track.AddNote(new VocalMidiNoteData
                        {
                            NoteNumber = note.NoteNumber,
                            Channel = note.Channel,
                            Velocity = velocity,
                            StartTick = start,
                            DurationTicks = Math.Max(1, note.AbsoluteTime - start),
                        });
                    }
                }
            }

            foreach (var pending in activeNotes)
            {
                while (pending.Value.Count > 0)
                {
                    (long start, int velocity) = pending.Value.Dequeue();
                    track.AddNote(new VocalMidiNoteData
                    {
                        NoteNumber = pending.Key.Note,
                        Channel = pending.Key.Channel,
                        Velocity = velocity,
                        StartTick = start,
                        DurationTicks = Math.Max(1, lastTick - start),
                    });
                }
            }
            track.ExtendLengthTo(lastTick);
            if (track.MutableNotes.Count > 0)
            {
                tracks.Add(track);
            }
        }

        if (tracks.Count == 0)
        {
            throw new InvalidDataException("The MIDI file contains no playable notes.");
        }

        return new VocalMidiFileData(
            Path.GetFullPath(filePath), ticksPerQuarterNote, bpm, tracks, 440, timeNumerator, timeDenominator);
    }

    private static string FindTrackName(IList<MidiEvent> events, int ordinal)
    {
        foreach (MidiEvent midiEvent in events)
        {
            if (midiEvent is not MetaEvent meta || meta.MetaEventType != MetaEventType.SequenceTrackName)
            {
                continue;
            }

            if (meta is TextEvent text && !string.IsNullOrWhiteSpace(text.Text))
            {
                return text.Text.Trim();
            }

            break;
        }
        return $"Track {ordinal}";
    }
}
