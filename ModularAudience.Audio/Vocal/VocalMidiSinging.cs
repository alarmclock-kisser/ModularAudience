namespace ModularAudience.Audio.Vocal;

internal sealed record VocalMidiScheduledNote(
    int SyllableIndex,
    int NoteNumber,
    int Velocity,
    int StartFrame,
    int DurationFrames,
    bool IsSyllableContinuation);

internal sealed record VocalMidiSingingPlan(
    IReadOnlyList<VocalSingingSyllable> Syllables,
    IReadOnlyList<VocalMidiScheduledNote> Notes,
    int OutputFrames,
    int UnusedMidiGroups,
    int RepeatedSyllables);

internal static class VocalMidiSinging
{
    internal static VocalMidiSingingPlan CreatePlan(
        VocalSynthSettings settings, IReadOnlyList<VocalToken> tokens, CancellationToken cancellationToken)
    {
        IVocalMidiFile midi = settings.SingingMidiFile
            ?? throw new ArgumentException("A MIDI file is required for MIDI singing.", nameof(settings));
        if (settings.SingingMidiTrackIndex is not int trackIndex)
            throw new ArgumentException("Select a MIDI track for singing.", nameof(settings));
        IVocalMidiTrack track = midi.Tracks.FirstOrDefault(candidate => candidate.Index == trackIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(settings), "The selected MIDI track does not exist.");

        IReadOnlyList<VocalToken> timelineTokens = settings.IgnoreTextPausesForMidiSinging
            ? tokens.Where(token => token is not VocalPauseToken).ToArray()
            : tokens.Where(token => token is not VocalPauseToken { IsLineBreak: true }).ToArray();
        var (syllables, trailingPauseMilliseconds) =
            VocalSinging.CreateSyllables(settings.Language, timelineTokens, cancellationToken);
        if (syllables.Count == 0)
            throw new InvalidOperationException("The text contains no singable syllables.");

        IVocalMidiNote[][] noteGroups = GroupOverlappingNotes(track, cancellationToken);
        if (noteGroups.Length == 0)
            throw new InvalidOperationException("The selected MIDI page contains no notes.");

        double secondsPerTick = 60.0 / midi.DefaultBpm / Math.Max(1, midi.TicksPerQuarterNote) / settings.Speed;
        int FramesForTicks(long ticks)
        {
            double frames = Math.Round(Math.Max(0, ticks) * secondsPerTick * settings.SampleRate);
            if (!double.IsFinite(frames) || frames > VocalSynthEngine.MaxOutputSamples / settings.Channels)
                throw new InvalidOperationException("The MIDI-sung vocal would exceed the supported output size.");
            return Math.Max(0, (int)frames);
        }

        int FramesForPause(double milliseconds)
        {
            if (!double.IsFinite(milliseconds) || milliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(tokens), "A text pause has an invalid duration.");
            double frames = Math.Round(milliseconds * settings.SampleRate / (1000 * settings.Speed));
            if (!double.IsFinite(frames) || frames > VocalSynthEngine.MaxOutputSamples / settings.Channels)
                throw new InvalidOperationException("The MIDI-sung vocal would exceed the supported output size.");
            return Math.Max(0, (int)frames);
        }

        int midiEndFrame = 0;
        int repeatedDurationFrames = 0;
        if (syllables.Count > noteGroups.Length)
        {
            long midiEndTick = Math.Max(
                track.LengthTicks,
                track.Notes.Max(note => note.StartTick + Math.Max(1, note.DurationTicks)));
            midiEndFrame = FramesForTicks(midiEndTick);
            long repeatedGroupStartTick = noteGroups[^1][0].StartTick;
            long repeatedGroupEndTick = noteGroups[^1].Max(
                note => checked(note.StartTick + Math.Max(1, note.DurationTicks)));
            long repeatedDurationTicks = Math.Max(1, checked(repeatedGroupEndTick - repeatedGroupStartTick));
            repeatedDurationFrames = Math.Max(1, FramesForTicks(repeatedDurationTicks));
        }
        int accumulatedPauseFrames = 0;
        int repeatedCursor = midiEndFrame;
        List<VocalMidiScheduledNote> scheduledNotes = [];
        int latestVoiceEnd = 0;

        for (int syllableIndex = 0; syllableIndex < syllables.Count; syllableIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int pauseBeforeFrames = FramesForPause(syllables[syllableIndex].PauseBeforeMilliseconds);
            accumulatedPauseFrames = checked(accumulatedPauseFrames + pauseBeforeFrames);
            bool repeated = syllableIndex >= noteGroups.Length;
            IReadOnlyList<IVocalMidiNote> chord = repeated ? noteGroups[^1] : noteGroups[syllableIndex];
            long groupStartTick = chord[0].StartTick;
            int startFrame = repeated
                ? Math.Max(checked(midiEndFrame + accumulatedPauseFrames), checked(repeatedCursor + pauseBeforeFrames))
                : checked(FramesForTicks(groupStartTick) + accumulatedPauseFrames);

            foreach (IVocalMidiNote note in chord)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (note.NoteNumber is < 0 or > 127)
                    throw new InvalidDataException($"MIDI note number {note.NoteNumber} is outside the supported range.");
                int noteStartFrame = repeated
                    ? checked(startFrame + FramesForTicks(checked(note.StartTick - groupStartTick)))
                    : checked(FramesForTicks(note.StartTick) + accumulatedPauseFrames);
                int durationFrames = Math.Max(1, FramesForTicks(Math.Max(1, note.DurationTicks)));
                scheduledNotes.Add(new VocalMidiScheduledNote(
                    syllableIndex,
                    note.NoteNumber,
                    Math.Clamp(note.Velocity, 1, 127),
                    noteStartFrame,
                    durationFrames,
                    note.StartTick > groupStartTick));
                latestVoiceEnd = Math.Max(latestVoiceEnd, checked(noteStartFrame + durationFrames));
            }

            if (repeated)
                repeatedCursor = checked(startFrame + repeatedDurationFrames);
        }

        int trailingPauseFrames = FramesForPause(trailingPauseMilliseconds);
        int outputFrames = checked(latestVoiceEnd + trailingPauseFrames);
        if ((long)outputFrames * settings.Channels > VocalSynthEngine.MaxOutputSamples)
            throw new InvalidOperationException("The MIDI-sung vocal would exceed the supported output size.");

        long renderWorkSamples = scheduledNotes.Sum(note => (long)note.DurationFrames * settings.Channels);
        if (renderWorkSamples > VocalSynthEngine.MaxOutputSamples)
            throw new InvalidOperationException("The MIDI-sung vocal layers would exceed the supported render size.");

        return new VocalMidiSingingPlan(
            syllables,
            scheduledNotes,
            outputFrames,
            Math.Max(0, noteGroups.Length - syllables.Count),
            Math.Max(0, syllables.Count - noteGroups.Length));
    }

    internal static double GetFrequency(IVocalMidiFile midi, int noteNumber, double semitoneOffset = 0) =>
        midi.PitchFrequency * Math.Pow(2.0, (noteNumber + semitoneOffset - 69) / 12.0);

    internal static double GetSemitoneOffset(double frequency, double basePitchHz) =>
        12.0 * Math.Log2(frequency / basePitchHz);

    internal static IVocalMidiNote[][] GroupOverlappingNotes(
        IVocalMidiTrack track, CancellationToken cancellationToken)
    {
        IVocalMidiNote[] orderedNotes = track.Notes
            .OrderBy(note => note.StartTick)
            .ThenBy(note => note.NoteNumber)
            .ToArray();
        List<IVocalMidiNote[]> noteGroups = [];
        List<IVocalMidiNote> currentGroup = [];
        long currentGroupEndTick = long.MinValue;
        foreach (IVocalMidiNote note in orderedNotes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (currentGroup.Count > 0 && note.StartTick >= currentGroupEndTick)
            {
                noteGroups.Add(currentGroup.ToArray());
                currentGroup.Clear();
            }

            currentGroup.Add(note);
            currentGroupEndTick = Math.Max(
                currentGroupEndTick,
                checked(note.StartTick + Math.Max(1, note.DurationTicks)));
        }

        if (currentGroup.Count > 0)
            noteGroups.Add(currentGroup.ToArray());
        return noteGroups.ToArray();
    }
}
