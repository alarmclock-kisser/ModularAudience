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

    public void ExtendLengthTo(long lengthTicks)
    {
        this.LengthTicks = Math.Max(this.LengthTicks, Math.Max(0, lengthTicks));
    }
}

public sealed class MidiEditSelection
{
    public required MidiFileData MidiFile { get; init; }
    public MidiFileData? SourceMidiFile { get; init; }
    public int TrackIndex { get; init; }
    public int StartNoteIndex { get; init; }
    public int EndNoteIndex { get; init; }
    public long StartTick { get; init; }
    public long EndTick { get; init; }
    public string LowestNoteName { get; init; } = "C2";
    public string HighestNoteName { get; init; } = "C6";
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

    public static MidiEditSelection CreateEditSelection(MidiFileData source, int trackIndex, long startTick, long endTick, int lowestNote, int highestNote)
    {
        ArgumentNullException.ThrowIfNull(source);
        MidiTrackData sourceTrack = source.Tracks.First(track => track.Index == trackIndex);
        startTick = Math.Clamp(Math.Min(startTick, endTick), 0, Math.Max(0, sourceTrack.LengthTicks));
        endTick = Math.Clamp(Math.Max(startTick, endTick), startTick + 1, Math.Max(startTick + 1, sourceTrack.LengthTicks));
        lowestNote = Math.Clamp(Math.Min(lowestNote, highestNote), 0, 127);
        highestNote = Math.Clamp(Math.Max(lowestNote, highestNote), lowestNote, 127);

        MidiTrackData selectedTrack = new()
        {
            Index = sourceTrack.Index,
            Name = sourceTrack.Name,
            LengthTicks = endTick - startTick
        };
        foreach (MidiNoteData note in sourceTrack.Notes)
        {
            long noteEnd = note.StartTick + note.DurationTicks;
            if (note.NoteNumber < lowestNote || note.NoteNumber > highestNote || noteEnd <= startTick || note.StartTick >= endTick)
            {
                continue;
            }

            long clippedStart = Math.Max(note.StartTick, startTick);
            long clippedEnd = Math.Min(noteEnd, endTick);
            selectedTrack.Notes.Add(new MidiNoteData
            {
                NoteNumber = note.NoteNumber,
                Channel = note.Channel,
                Velocity = note.Velocity,
                StartTick = clippedStart - startTick,
                DurationTicks = Math.Max(1, clippedEnd - clippedStart)
            });
        }

        MidiFileData selectedFile = new(source.FilePath, source.TicksPerQuarterNote, source.DefaultBpm, [selectedTrack]);
        List<MidiNoteData> orderedNotes = sourceTrack.Notes.OrderBy(note => note.StartTick).ToList();
        return new MidiEditSelection
        {
            MidiFile = selectedFile,
            SourceMidiFile = source,
            TrackIndex = sourceTrack.Index,
            StartNoteIndex = orderedNotes.FindIndex(note => note.StartTick >= startTick),
            EndNoteIndex = orderedNotes.FindLastIndex(note => note.StartTick < endTick),
            StartTick = startTick,
            EndTick = endTick,
            LowestNoteName = MidiNoteName(lowestNote),
            HighestNoteName = MidiNoteName(highestNote)
        };
    }

    public MidiFileData ReplaceSelection(MidiEditSelection selection, MidiFileData editedFile)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(editedFile);
        List<MidiTrackData> replacementTracks = this.Tracks.Select(CloneTrack).ToList();
        MidiTrackData target = replacementTracks.First(track => track.Index == selection.TrackIndex);
        MidiTrackData editedTrack = editedFile.Tracks.FirstOrDefault(track => track.Index == selection.TrackIndex) ?? editedFile.Tracks.First();
        long lastEditedNoteEnd = editedTrack.Notes.Count == 0
            ? 0
            : editedTrack.Notes.Max(note => note.StartTick + note.DurationTicks);
        long replacementEndTick = Math.Max(selection.EndTick, selection.StartTick + lastEditedNoteEnd);
        target.Notes.RemoveAll(note => note.StartTick < replacementEndTick && note.StartTick + note.DurationTicks > selection.StartTick);

        foreach (MidiNoteData note in editedTrack.Notes)
        {
            long startTick = selection.StartTick + note.StartTick;
            long endTick = Math.Min(replacementEndTick, startTick + note.DurationTicks);
            if (startTick < replacementEndTick && endTick > selection.StartTick)
            {
                target.Notes.Add(new MidiNoteData
                {
                    NoteNumber = note.NoteNumber,
                    Channel = note.Channel,
                    Velocity = note.Velocity,
                    StartTick = startTick,
                    DurationTicks = Math.Max(1, endTick - startTick)
                });
            }
        }

        target.LengthTicks = target.Notes.Count == 0 ? 0 : target.Notes.Max(note => note.StartTick + note.DurationTicks);
        return new MidiFileData(this.FilePath, this.TicksPerQuarterNote, this.DefaultBpm, replacementTracks);
    }

    private static MidiTrackData CloneTrack(MidiTrackData source)
    {
        MidiTrackData clone = new() { Index = source.Index, Name = source.Name, LengthTicks = source.LengthTicks };
        clone.Notes.AddRange(source.Notes);
        return clone;
    }

    private static string MidiNoteName(int note)
    {
        string[] names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
        return names[note % 12] + (note / 12 - 1);
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

    public static MidiFileData Convert(AudioObj audioObj)
    {
        if (audioObj == null || audioObj.Data.Length <= 0)
        {
            throw new ArgumentNullException(nameof(audioObj));
        }

        if (audioObj.SampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(audioObj), "The audio sample rate must be positive.");
        }

        int channels = Math.Max(1, audioObj.Channels);
        int frameCount = audioObj.Data.Length / channels;
        if (frameCount < 2)
        {
            throw new InvalidDataException("The audio object does not contain complete audio frames.");
        }

        const int ticksPerQuarterNote = 960;
        double bpm = audioObj.Bpm > 0 ? audioObj.Bpm : audioObj.ScannedBpm;
        bpm = Math.Clamp(double.IsFinite(bpm) ? bpm : 120.0, 20.0, 400.0);

        float[] mono = CreateMonoSignal(audioObj.Data, channels, frameCount);
        RemoveDcOffset(mono);

        int windowSize = Math.Min(4096, HighestPowerOfTwo(Math.Max(256, frameCount)));
        windowSize = Math.Max(256, windowSize);
        int hopSize = Math.Max(64, windowSize / 4);
        List<PitchFrame> frames = AnalysePitch(mono, audioObj.SampleRate, windowSize, hopSize);
        ApplyPitchStabilityFilter(frames);

        float maximumRms = frames.Count == 0 ? 0f : frames.Max(frame => frame.Rms);
        float noiseFloor = maximumRms * 0.015f;
        List<MidiNoteData> notes = CreateNotes(frames, audioObj.SampleRate, bpm, ticksPerQuarterNote, noiseFloor);

        MidiTrackData track = new()
        {
            Index = 0,
            Name = string.IsNullOrWhiteSpace(audioObj.Name) ? "Audio conversion" : audioObj.Name,
            LengthTicks = Math.Max(1, (long)Math.Ceiling(frameCount / (double)audioObj.SampleRate * bpm / 60.0 * ticksPerQuarterNote))
        };
        track.Notes.AddRange(notes);
        if (track.Notes.Count > 0)
        {
            track.LengthTicks = Math.Max(track.LengthTicks, track.Notes.Max(note => note.StartTick + note.DurationTicks));
        }

        string filePath = string.IsNullOrWhiteSpace(audioObj.FilePath) ? audioObj.Name : audioObj.FilePath;
        return new MidiFileData(filePath ?? string.Empty, ticksPerQuarterNote, bpm, [track]);
    }

    public static async Task<MidiFileData> ConvertAsync(AudioObj audioObj, int maxWorkers = 0, CancellationToken cancellationToken = default)
    {
        ValidateAudio(audioObj, out int channels, out int frameCount, out double bpm);
        maxWorkers = Math.Clamp(maxWorkers <= 0 ? Environment.ProcessorCount : maxWorkers, 1, Math.Min(Environment.ProcessorCount, frameCount));
        cancellationToken.ThrowIfCancellationRequested();

        List<PitchFrame> frames = await Task.Run(() =>
        {
            float[] signal = CreateMonoSignal(audioObj.Data, channels, frameCount);
            RemoveDcOffset(signal);
            int windowSize = Math.Max(256, Math.Min(4096, HighestPowerOfTwo(Math.Max(256, frameCount))));
            int hopSize = Math.Max(64, windowSize / 4);
            return AnalysePitchParallel(signal, audioObj.SampleRate, windowSize, hopSize, maxWorkers, cancellationToken);
        }, cancellationToken).ConfigureAwait(false);

        ApplyPitchStabilityFilter(frames);
        float maximumRms = frames.Count == 0 ? 0f : frames.Max(frame => frame.Rms);
        float noiseFloor = maximumRms * 0.015f;
        List<MidiNoteData> notes = CreateNotes(frames, audioObj.SampleRate, bpm, 960, noiseFloor);
        MidiTrackData track = new()
        {
            Index = 0,
            Name = string.IsNullOrWhiteSpace(audioObj.Name) ? "Audio conversion" : audioObj.Name,
            LengthTicks = Math.Max(1, (long)Math.Ceiling(frameCount / (double)audioObj.SampleRate * bpm / 60.0 * 960))
        };
        track.Notes.AddRange(notes);
        if (track.Notes.Count > 0)
        {
            track.LengthTicks = Math.Max(track.LengthTicks, track.Notes.Max(note => note.StartTick + note.DurationTicks));
        }

        string filePath = string.IsNullOrWhiteSpace(audioObj.FilePath) ? audioObj.Name : audioObj.FilePath;
        return new MidiFileData(filePath ?? string.Empty, 960, bpm, [track]);
    }

    private static void ValidateAudio(AudioObj audioObj, out int channels, out int frameCount, out double bpm)
    {
        if (audioObj == null || audioObj.Data.Length <= 0)
        {
            throw new ArgumentNullException(nameof(audioObj));
        }
        if (audioObj.SampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(audioObj), "The audio sample rate must be positive.");
        }
        channels = Math.Max(1, audioObj.Channels);
        frameCount = audioObj.Data.Length / channels;
        if (frameCount < 2)
        {
            throw new InvalidDataException("The audio object does not contain complete audio frames.");
        }
        bpm = audioObj.Bpm > 0 ? audioObj.Bpm : audioObj.ScannedBpm;
        bpm = Math.Clamp(double.IsFinite(bpm) ? bpm : 120.0, 20.0, 400.0);
    }

    private readonly record struct PitchFrame(double CenterSample, double Frequency, float Rms, double Confidence);

    private static float[] CreateMonoSignal(float[] interleaved, int channels, int frameCount)
    {
        float[] mono = new float[frameCount];
        for (int frame = 0; frame < frameCount; frame++)
        {
            double sum = 0;
            int offset = frame * channels;
            for (int channel = 0; channel < channels; channel++)
            {
                float value = interleaved[offset + channel];
                if (float.IsFinite(value))
                {
                    sum += value;
                }
            }
            mono[frame] = (float)(sum / channels);
        }
        return mono;
    }

    private static void RemoveDcOffset(float[] signal)
    {
        double mean = 0;
        for (int index = 0; index < signal.Length; index++)
        {
            mean += signal[index];
        }
        mean /= signal.Length;
        for (int index = 0; index < signal.Length; index++)
        {
            signal[index] = (float)(signal[index] - mean);
        }
    }

    private static List<PitchFrame> AnalysePitch(float[] signal, int sampleRate, int windowSize, int hopSize)
    {
        int frameTotal = Math.Max(1, (signal.Length - 1 + hopSize - 1) / hopSize);
        PitchFrame[] result = new PitchFrame[frameTotal];
        AnalysePitchRange(signal, sampleRate, windowSize, hopSize, 0, frameTotal, result, CancellationToken.None);
        return [.. result];
    }

    private static List<PitchFrame> AnalysePitchParallel(float[] signal, int sampleRate, int windowSize, int hopSize, int maxWorkers, CancellationToken cancellationToken)
    {
        int frameTotal = Math.Max(1, (signal.Length - 1 + hopSize - 1) / hopSize);
        PitchFrame[] result = new PitchFrame[frameTotal];
        int workerCount = Math.Min(maxWorkers, frameTotal);
        int framesPerWorker = (frameTotal + workerCount - 1) / workerCount;
        Task[] workers = new Task[workerCount];
        for (int worker = 0; worker < workerCount; worker++)
        {
            int startFrame = worker * framesPerWorker;
            int endFrame = Math.Min(frameTotal, startFrame + framesPerWorker);
            workers[worker] = Task.Run(() => AnalysePitchRange(signal, sampleRate, windowSize, hopSize, startFrame, endFrame, result, cancellationToken), cancellationToken);
        }
        Task.WaitAll(workers);
        return [.. result];
    }

    private static void AnalysePitchRange(float[] signal, int sampleRate, int windowSize, int hopSize, int startFrame, int endFrame, PitchFrame[] result, CancellationToken cancellationToken)
    {
        float[] window = new float[windowSize];
        int minimumLag = Math.Max(2, (int)Math.Floor(sampleRate / 1200.0));
        int maximumLag = Math.Min(windowSize / 2 - 2, (int)Math.Ceiling(sampleRate / 27.5));
        double[] difference = new double[maximumLag + 1];
        double[] cumulative = new double[maximumLag + 1];

        for (int frameIndex = startFrame; frameIndex < endFrame; frameIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int start = frameIndex * hopSize;
            double sumSquares = 0;
            for (int index = 0; index < windowSize; index++)
            {
                int sourceIndex = start + index;
                float sample = sourceIndex < signal.Length ? signal[sourceIndex] : 0f;
                double hann = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * index / Math.Max(1, windowSize - 1));
                window[index] = (float)(sample * hann);
                sumSquares += sample * sample;
            }

            float rms = (float)Math.Sqrt(sumSquares / windowSize);
            if (rms < 1e-7f || maximumLag <= minimumLag)
            {
                result[frameIndex] = new PitchFrame(start + windowSize / 2.0, 0, rms, 0);
                continue;
            }

            for (int lag = 1; lag <= maximumLag; lag++)
            {
                double sum = 0;
                int limit = windowSize - lag;
                for (int index = 0; index < limit; index++)
                {
                    double delta = window[index] - window[index + lag];
                    sum += delta * delta;
                }
                difference[lag] = sum;
            }

            double running = 0;
            int bestLag = minimumLag;
            double bestValue = double.MaxValue;
            for (int lag = 1; lag <= maximumLag; lag++)
            {
                running += difference[lag];
                cumulative[lag] = running;
                double normalized = running <= 1e-20 ? 1 : difference[lag] * lag / running;
                if (lag >= minimumLag && normalized < bestValue)
                {
                    bestValue = normalized;
                    bestLag = lag;
                }
                if (lag >= minimumLag && normalized < 0.12)
                {
                    bestLag = lag;
                    bestValue = normalized;
                    break;
                }
            }

            double refinedLag = bestLag;
            if (bestLag > minimumLag && bestLag < maximumLag)
            {
                double left = NormalizedDifference(difference, cumulative, bestLag - 1);
                double center = NormalizedDifference(difference, cumulative, bestLag);
                double right = NormalizedDifference(difference, cumulative, bestLag + 1);
                double denominator = left - 2 * center + right;
                if (Math.Abs(denominator) > 1e-12)
                {
                    refinedLag += Math.Clamp(0.5 * (left - right) / denominator, -0.5, 0.5);
                }
            }

            double frequency = sampleRate / refinedLag;
            double confidence = Math.Clamp(1.0 - bestValue, 0, 1);
            if (frequency < 27.5 || frequency > 4186.0 || confidence < 0.55)
            {
                frequency = 0;
            }
            result[frameIndex] = new PitchFrame(start + windowSize / 2.0, frequency, rms, confidence);
        }
    }

    private static double NormalizedDifference(double[] difference, double[] cumulative, int lag)
    {
        return cumulative[lag] <= 1e-20 ? 1 : difference[lag] * lag / cumulative[lag];
    }

    private static void ApplyPitchStabilityFilter(List<PitchFrame> frames)
    {
        if (frames.Count < 3)
        {
            return;
        }
        for (int index = 1; index < frames.Count - 1; index++)
        {
            PitchFrame previous = frames[index - 1];
            PitchFrame current = frames[index];
            PitchFrame next = frames[index + 1];
            if (current.Frequency > 0 && previous.Frequency > 0 && next.Frequency > 0)
            {
                double median = new[] { previous.Frequency, current.Frequency, next.Frequency }.OrderBy(value => value).ElementAt(1);
                if (Math.Abs(12 * Math.Log2(current.Frequency / median)) > 0.7)
                {
                    frames[index] = current with { Frequency = median, Confidence = Math.Min(current.Confidence, previous.Confidence) };
                }
            }
        }
    }

    private static List<MidiNoteData> CreateNotes(List<PitchFrame> frames, int sampleRate, double bpm, int ppq, float noiseFloor)
    {
        List<MidiNoteData> notes = [];
        if (frames.Count == 0)
        {
            return notes;
        }

        double frameStep = frames.Count > 1
            ? frames.Zip(frames.Skip(1), (left, right) => right.CenterSample - left.CenterSample).Where(step => step > 0).DefaultIfEmpty(sampleRate * 0.01).Average()
            : sampleRate * 0.01;
        int start = -1;
        int currentNote = -1;
        for (int index = 0; index <= frames.Count; index++)
        {
            int note = index < frames.Count && frames[index].Rms >= noiseFloor && frames[index].Frequency > 0
                ? Math.Clamp((int)Math.Round(69 + 12 * Math.Log2(frames[index].Frequency / 440.0), MidpointRounding.AwayFromZero), 0, 127)
                : -1;
            if (note == currentNote && note >= 0)
            {
                continue;
            }
            if (currentNote >= 0 && start >= 0)
            {
                PitchFrame first = frames[start];
                PitchFrame last = frames[index - 1];
                double startSeconds = Math.Max(0, first.CenterSample - frameStep / 2) / sampleRate;
                double endSeconds = (last.CenterSample + frameStep / 2) / sampleRate;
                long startTick = Math.Max(0, (long)Math.Round(startSeconds * bpm / 60 * ppq));
                long endTick = Math.Max(startTick + 1, (long)Math.Round(endSeconds * bpm / 60 * ppq));
                if (endSeconds - startSeconds >= 0.045)
                {
                    float rms = frames.Skip(start).Take(index - start).Average(frame => frame.Rms);
                    int velocity = Math.Clamp((int)Math.Round(127 * Math.Sqrt(Math.Clamp(rms / Math.Max(noiseFloor * 4, 1e-5f), 0, 1))), 1, 127);
                    notes.Add(new MidiNoteData { NoteNumber = currentNote, Channel = 0, Velocity = velocity, StartTick = startTick, DurationTicks = endTick - startTick });
                }
            }
            if (note >= 0)
            {
                start = index;
                currentNote = note;
            }
            else
            {
                start = -1;
                currentNote = -1;
            }
        }
        return notes;
    }

    private static int HighestPowerOfTwo(int value)
    {
        int result = 1;
        while (result <= value / 2)
        {
            result <<= 1;
        }
        return result;
    }

    private static string FindTrackName(IList<MidiEvent> events, int trackIndex)
    {
        TextEvent? textEvent = events.OfType<TextEvent>().FirstOrDefault(eventItem => eventItem.MetaEventType == MetaEventType.SequenceTrackName);
        return string.IsNullOrWhiteSpace(textEvent?.Text) ? $"Track {trackIndex + 1}" : textEvent.Text;
    }
}
