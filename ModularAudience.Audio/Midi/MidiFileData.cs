using NAudio.Midi;

namespace ModularAudience.Audio.Midi;

public sealed class MidiNoteData
{
    public int NoteNumber { get; init; }
    public int Channel { get; init; }
    public int Velocity { get; init; }
    public long StartTick { get; init; }
    public long DurationTicks { get; init; }
    public double StartBeat => this.StartTick;
}

public sealed class MidiTrackData
{
    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;
    public List<MidiNoteData> Notes { get; } = [];
    public long LengthTicks { get; internal set; }
}

public sealed class MidiFileData
{
    public static bool IsMidiPath(string? filePath)
    {
        string extension = Path.GetExtension(filePath ?? string.Empty);
        return extension.Equals(".mid", StringComparison.OrdinalIgnoreCase) || extension.Equals(".midi", StringComparison.OrdinalIgnoreCase);
    }

    public string FilePath { get; }
    public int TicksPerQuarterNote { get; }
    public double DefaultBpm { get; }
    public IReadOnlyList<MidiTrackData> Tracks { get; }
    public long LengthTicks => this.Tracks.Count == 0 ? 0 : this.Tracks.Max(track => track.LengthTicks);
    public double LengthBeats => this.LengthTicks / (double)Math.Max(1, this.TicksPerQuarterNote);

    private MidiFileData(string filePath, int ticksPerQuarterNote, double defaultBpm, IReadOnlyList<MidiTrackData> tracks)
    {
        this.FilePath = filePath;
        this.TicksPerQuarterNote = ticksPerQuarterNote;
        this.DefaultBpm = defaultBpm;
        this.Tracks = tracks;
    }

    public static MidiFileData Load(string filePath)
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

        double bpm = 120.0;
        List<MidiTrackData> tracks = [];
        for (int trackIndex = 0; trackIndex < midiFile.Events.Count(); trackIndex++)
        {
            IList<MidiEvent> events = midiFile.Events[trackIndex];
            MidiTrackData track = new()
            {
                Index = trackIndex,
                Name = FindTrackName(events, tracks.Count + 1)
            };
            Dictionary<(int Channel, int Note), Queue<(long Tick, int Velocity)>> activeNotes = [];
            long lastTick = 0;

            foreach (MidiEvent midiEvent in events)
            {
                lastTick = Math.Max(lastTick, midiEvent.AbsoluteTime);
                if (midiEvent is TempoEvent tempoEvent && tempoEvent.MicrosecondsPerQuarterNote > 0)
                {
                    bpm = 60_000_000.0 / tempoEvent.MicrosecondsPerQuarterNote;
                }

                if (midiEvent is not NoteEvent noteEvent || noteEvent.NoteNumber < 0 || noteEvent.NoteNumber > 127)
                {
                    continue;
                }

                var key = (noteEvent.Channel, noteEvent.NoteNumber);
                if (noteEvent.CommandCode == MidiCommandCode.NoteOn && noteEvent.Velocity > 0)
                {
                    if (!activeNotes.TryGetValue(key, out Queue<(long Tick, int Velocity)>? starts))
                    {
                        starts = new Queue<(long Tick, int Velocity)>();
                        activeNotes[key] = starts;
                    }
                    starts.Enqueue((noteEvent.AbsoluteTime, noteEvent.Velocity));
                }
                else if (noteEvent.CommandCode == MidiCommandCode.NoteOff ||
                         (noteEvent.CommandCode == MidiCommandCode.NoteOn && noteEvent.Velocity == 0))
                {
                    if (activeNotes.TryGetValue(key, out Queue<(long Tick, int Velocity)>? starts) && starts.Count > 0)
                    {
                        var start = starts.Dequeue();
                        track.Notes.Add(new MidiNoteData
                        {
                            NoteNumber = noteEvent.NoteNumber,
                            Channel = noteEvent.Channel,
                            Velocity = start.Velocity,
                            StartTick = start.Tick,
                            DurationTicks = Math.Max(1, noteEvent.AbsoluteTime - start.Tick)
                        });
                    }
                }
            }

            foreach (var pending in activeNotes)
            {
                while (pending.Value.Count > 0)
                {
                    var start = pending.Value.Dequeue();
                    track.Notes.Add(new MidiNoteData
                    {
                        NoteNumber = pending.Key.Note,
                        Channel = pending.Key.Channel,
                        Velocity = start.Velocity,
                        StartTick = start.Tick,
                        DurationTicks = Math.Max(1, lastTick - start.Tick)
                    });
                }
            }

            track.LengthTicks = Math.Max(lastTick, track.Notes.Count == 0 ? 0 : track.Notes.Max(note => note.StartTick + note.DurationTicks));
            if (track.Notes.Count > 0)
            {
                tracks.Add(track);
            }
        }

        if (tracks.Count == 0)
        {
            throw new InvalidDataException("The MIDI file contains no playable notes.");
        }

        return new MidiFileData(Path.GetFullPath(filePath), ticksPerQuarterNote, Math.Clamp(bpm, 20.0, 400.0), tracks);
    }

    private static string FindTrackName(IList<MidiEvent> events, int trackIndex)
    {
        TextEvent? textEvent = events.OfType<TextEvent>().FirstOrDefault(eventItem => eventItem.MetaEventType == MetaEventType.SequenceTrackName);
        return string.IsNullOrWhiteSpace(textEvent?.Text) ? $"Track {trackIndex + 1}" : textEvent.Text;
    }
}
