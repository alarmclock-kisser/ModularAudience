namespace ModularAudience.Audio.Vocal;

internal sealed record VocalMidiScheduledNote(
    int SyllableIndex,
    int NoteNumber,
    int Velocity,
    int StartFrame,
    int DurationFrames,
    bool IsSyllableContinuation,
    int PreSyllableSilenceFrames);

internal sealed record VocalMidiSingingPlan(
    IReadOnlyList<VocalSingingSyllable> Syllables,
    IReadOnlyList<VocalMidiScheduledNote> Notes,
    int OutputFrames,
    int UnusedMidiGroups,
    int RepeatedSyllables);

internal static class VocalMidiSinging
{
    private readonly record struct SyllableGroupingTiming(
        long NaturalDurationTicks,
        long ShortNoteLimitTicks);

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

        double[] syllableDurationsMilliseconds = syllables
            .Select(syllable => syllable.NaturalDurationMilliseconds)
            .ToArray();
        IVocalMidiNote[][] noteGroups = GroupSingingNoteGroups(
            track,
            midi.TicksPerQuarterNote,
            midi.DefaultBpm,
            settings.Speed,
            syllableDurationsMilliseconds,
            syllableDurationsMilliseconds[^1],
            cancellationToken);
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
            int onsetNoteIndex = GetSyllableOnsetNoteIndex(chord);
            IVocalMidiNote onsetNote = chord[onsetNoteIndex];
            int startFrame = repeated
                ? Math.Max(checked(midiEndFrame + accumulatedPauseFrames), checked(repeatedCursor + pauseBeforeFrames))
                : checked(FramesForTicks(groupStartTick) + accumulatedPauseFrames);
            int syllableOnsetFrame = repeated
                ? checked(startFrame + FramesForTicks(checked(onsetNote.StartTick - groupStartTick)))
                : checked(FramesForTicks(onsetNote.StartTick) + accumulatedPauseFrames);

            for (int noteIndex = 0; noteIndex < chord.Count; noteIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IVocalMidiNote note = chord[noteIndex];
                if (note.NoteNumber is < 0 or > 127)
                    throw new InvalidDataException($"MIDI note number {note.NoteNumber} is outside the supported range.");
                int noteStartFrame = repeated
                    ? checked(startFrame + FramesForTicks(checked(note.StartTick - groupStartTick)))
                    : checked(FramesForTicks(note.StartTick) + accumulatedPauseFrames);
                int durationFrames = Math.Max(1, FramesForTicks(Math.Max(1, note.DurationTicks)));
                int preSyllableSilenceFrames = noteIndex != onsetNoteIndex
                    && note.StartTick < onsetNote.StartTick
                    && IsLessProminentLeadIn(note, onsetNote)
                        ? Math.Clamp(syllableOnsetFrame - noteStartFrame, 0, durationFrames)
                        : 0;
                scheduledNotes.Add(new VocalMidiScheduledNote(
                    syllableIndex,
                    note.NoteNumber,
                    Math.Clamp(note.Velocity, 1, 127),
                    noteStartFrame,
                    durationFrames,
                    noteIndex != onsetNoteIndex,
                    preSyllableSilenceFrames));
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

    private static int GetSyllableOnsetNoteIndex(IReadOnlyList<IVocalMidiNote> notes)
    {
        int onsetIndex = 0;
        for (int index = 1; index < notes.Count; index++)
        {
            IVocalMidiNote candidate = notes[index];
            IVocalMidiNote onset = notes[onsetIndex];
            int candidateVelocity = Math.Clamp(candidate.Velocity, 1, 127);
            int onsetVelocity = Math.Clamp(onset.Velocity, 1, 127);
            long candidateDuration = Math.Max(1, candidate.DurationTicks);
            long onsetDuration = Math.Max(1, onset.DurationTicks);
            if (candidateVelocity > onsetVelocity
                || candidateVelocity == onsetVelocity && candidateDuration > onsetDuration)
                onsetIndex = index;
        }
        return onsetIndex;
    }

    private static bool IsLessProminentLeadIn(IVocalMidiNote note, IVocalMidiNote onsetNote)
    {
        int velocity = Math.Clamp(note.Velocity, 1, 127);
        int onsetVelocity = Math.Clamp(onsetNote.Velocity, 1, 127);
        return velocity < onsetVelocity
            || velocity == onsetVelocity
                && Math.Max(1, note.DurationTicks) < Math.Max(1, onsetNote.DurationTicks);
    }

    internal static double GetFrequency(IVocalMidiFile midi, int noteNumber, double semitoneOffset = 0) =>
        midi.PitchFrequency * Math.Pow(2.0, (noteNumber + semitoneOffset - 69) / 12.0);

    internal static double GetSemitoneOffset(double frequency, double basePitchHz) =>
        12.0 * Math.Log2(frequency / basePitchHz);

    internal static IVocalMidiNote[][] GroupSingingNoteGroups(
        IVocalMidiTrack track,
        int ticksPerQuarterNote,
        double bpm,
        double speed,
        IReadOnlyList<double> syllableDurationsMilliseconds,
        double fallbackSyllableDurationMilliseconds,
        CancellationToken cancellationToken)
    {
        if (ticksPerQuarterNote <= 0 || !double.IsFinite(bpm) || bpm <= 0
            || !double.IsFinite(speed) || speed <= 0
            || !double.IsFinite(fallbackSyllableDurationMilliseconds)
            || fallbackSyllableDurationMilliseconds <= 0)
            throw new InvalidDataException("The MIDI timing is invalid for syllable grouping.");

        IVocalMidiNote[] orderedNotes = track.Notes
            .OrderBy(note => note.StartTick)
            .ThenBy(note => note.NoteNumber)
            .ToArray();
        long quarterTicks = Math.Max(1, ticksPerQuarterNote);
        List<IVocalMidiNote[]> noteGroups = [];
        List<IVocalMidiNote> currentGroup = [];
        long currentGroupEndTick = long.MinValue;
        long currentAttackStartTick = long.MinValue;
        long currentAttackDurationTicks = 0;
        bool currentGroupContainsLongNote = false;
        foreach (IVocalMidiNote note in orderedNotes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long noteDurationTicks = Math.Max(1, note.DurationTicks);
            long noteEndTick = checked(note.StartTick + noteDurationTicks);
            SyllableGroupingTiming timing = GetSyllableGroupingTiming(
                noteGroups.Count,
                syllableDurationsMilliseconds,
                fallbackSyllableDurationMilliseconds,
                bpm,
                quarterTicks,
                speed);
            long previousAttackDurationTicks = currentGroup.Count == 0
                ? 0
                : Math.Max(
                    currentAttackDurationTicks,
                    checked(currentGroupEndTick - currentAttackStartTick));
            bool continuesRapidMelisma = currentGroup.Count > 0
                && !currentGroupContainsLongNote
                && noteDurationTicks <= timing.ShortNoteLimitTicks
                && note.StartTick >= currentGroupEndTick
                && note.StartTick - currentGroupEndTick
                    <= GetMaximumArticulationGapTicks(previousAttackDurationTicks, noteDurationTicks)
                && currentGroupEndTick - currentGroup[0].StartTick < timing.NaturalDurationTicks;
            if (currentGroup.Count > 0 && note.StartTick >= currentGroupEndTick && !continuesRapidMelisma)
            {
                noteGroups.Add(currentGroup.ToArray());
                currentGroup.Clear();
                currentGroupEndTick = long.MinValue;
                currentAttackStartTick = long.MinValue;
                currentAttackDurationTicks = 0;
                currentGroupContainsLongNote = false;
                timing = GetSyllableGroupingTiming(
                    noteGroups.Count,
                    syllableDurationsMilliseconds,
                    fallbackSyllableDurationMilliseconds,
                    bpm,
                    quarterTicks,
                    speed);
            }

            currentGroup.Add(note);
            if (note.StartTick > currentAttackStartTick)
            {
                currentAttackStartTick = note.StartTick;
                currentAttackDurationTicks = noteDurationTicks;
            }
            else if (note.StartTick == currentAttackStartTick)
            {
                currentAttackDurationTicks = Math.Max(currentAttackDurationTicks, noteDurationTicks);
            }
            currentGroupContainsLongNote |= noteDurationTicks > timing.ShortNoteLimitTicks;
            currentGroupEndTick = Math.Max(currentGroupEndTick, noteEndTick);
        }

        if (currentGroup.Count > 0)
            noteGroups.Add(currentGroup.ToArray());
        return noteGroups.ToArray();
    }

    private static SyllableGroupingTiming GetSyllableGroupingTiming(
        int syllableIndex,
        IReadOnlyList<double> syllableDurationsMilliseconds,
        double fallbackSyllableDurationMilliseconds,
        double bpm,
        long quarterTicks,
        double speed)
    {
        double durationMilliseconds = syllableIndex < syllableDurationsMilliseconds.Count
            ? syllableDurationsMilliseconds[syllableIndex]
            : fallbackSyllableDurationMilliseconds;
        if (!double.IsFinite(durationMilliseconds) || durationMilliseconds <= 0)
            throw new InvalidDataException("A MIDI singing syllable has an invalid natural duration.");

        double naturalDurationTicksValue =
            durationMilliseconds * bpm * quarterTicks * speed / 60_000.0;
        if (!double.IsFinite(naturalDurationTicksValue) || naturalDurationTicksValue >= long.MaxValue)
            throw new InvalidDataException("The MIDI syllable duration exceeds the supported timing range.");
        long naturalDurationTicks = Math.Max(1, (long)Math.Ceiling(naturalDurationTicksValue));
        return new SyllableGroupingTiming(
            naturalDurationTicks,
            Math.Max(1, naturalDurationTicks / 2));
    }

    private static long GetMaximumArticulationGapTicks(long previousDurationTicks, long nextDurationTicks) =>
        Math.Max(1, Math.Min(previousDurationTicks, nextDurationTicks) / 2);
}
