using ModularAudience.Audio.Midi;
using System.Runtime.ExceptionServices;

namespace ModularAudience.Audio.Vocal;

public static partial class VocalSynth
{
    private const int MidiAudioChunkSize = 16_384;

    public static VocalMidiSyllableFillResult FillMissingMidiSyllables(
        VocalSynthSettings settings, string existingText, CancellationToken cancellationToken = default) =>
        VocalMidiSyllableFiller.FillMissing(settings, existingText, cancellationToken);

    private static AudioObj RenderMidi(
        VocalSynthSettings settings, IReadOnlyList<VocalToken> tokens, CancellationToken cancellationToken)
    {
        VocalMidiSingingPlan plan = VocalMidiSinging.CreatePlan(settings, tokens, cancellationToken);
        MidiFileData midi = settings.SingingMidiFile!;
        int channels = settings.Channels;
        int sampleCount = checked(plan.OutputFrames * channels);
        var output = new float[sampleCount];
        int maxParallelNotes = GetMidiMaxDegreeOfParallelism();
        var voices = new float[Math.Min(plan.Notes.Count, maxParallelNotes)][];

        for (int batchStart = 0; batchStart < plan.Notes.Count; batchStart += voices.Length)
        {
            int batchCount = Math.Min(voices.Length, plan.Notes.Count - batchStart);
            ParallelForMidi(0, batchCount, cancellationToken, batchIndex =>
                voices[batchIndex] = RenderMidiNote(
                    plan, plan.Notes[batchStart + batchIndex], midi, settings, cancellationToken));

            for (int batchIndex = 0; batchIndex < batchCount; batchIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                MixMidiVoice(output, voices[batchIndex], plan.Notes[batchStart + batchIndex],
                    channels, cancellationToken);
                voices[batchIndex] = Array.Empty<float>();
            }
        }

        AudioObj result = CreateAudio(output, settings, cancellationToken);
        result.Bpm = (float)(midi.DefaultBpm * settings.Speed);
        return result;
    }

    private static float[] RenderMidiNote(
        VocalMidiSingingPlan plan, VocalMidiScheduledNote note, MidiFileData midi,
        VocalSynthSettings settings, CancellationToken cancellationToken)
    {
        VocalSingingSyllable syllable = plan.Syllables[note.SyllableIndex];
        double frequency = VocalMidiSinging.GetFrequency(
            midi, note.NoteNumber, settings.SingingMidiSemitoneOffset);
        if (!double.IsFinite(frequency) || frequency <= 0 || frequency >= settings.SampleRate * 0.45)
            throw new InvalidDataException($"MIDI note {note.NoteNumber} is outside the supported vocal frequency range.");
        double semitoneOffset = VocalMidiSinging.GetSemitoneOffset(frequency, settings.BasePitchHz);
        List<RenderUnit> units = CreateMidiSyllablePlan(syllable, settings, semitoneOffset, cancellationToken);
        FitMidiSyllableToDuration(units, note.DurationFrames, cancellationToken);
        return RenderMidiSyllable(units, note, settings, cancellationToken);
    }

    private static int GetMidiMaxDegreeOfParallelism() =>
        Math.Max(1, Math.Min(8, Environment.ProcessorCount / 2));

    private static void ParallelForMidi(
        int fromInclusive, int toExclusive, CancellationToken cancellationToken, Action<int> body)
    {
        int maxDegreeOfParallelism = GetMidiMaxDegreeOfParallelism();
        if (maxDegreeOfParallelism == 1 || toExclusive - fromInclusive <= 1)
        {
            for (int index = fromInclusive; index < toExclusive; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                body(index);
            }
            return;
        }

        try
        {
            Parallel.For(fromInclusive, toExclusive, new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = maxDegreeOfParallelism,
            }, body);
        }
        catch (AggregateException error)
        {
            AggregateException flattened = error.Flatten();
            if (flattened.InnerExceptions.Count > 0)
                ExceptionDispatchInfo.Capture(flattened.InnerExceptions[0]).Throw();
            throw flattened;
        }
    }

    private static List<RenderUnit> CreateMidiSyllablePlan(
        VocalSingingSyllable syllable, VocalSynthSettings settings, double semitoneOffset,
        CancellationToken cancellationToken)
    {
        List<RenderUnit> plan = new(syllable.Tokens.Count);
        foreach (VocalToken token in syllable.Tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double milliseconds;
            int minimumFrames;
            if (token is VocalPauseToken pause)
            {
                milliseconds = pause.Milliseconds;
                minimumFrames = 1;
            }
            else if (token is VocalPhonemeToken phoneme)
            {
                if (!VocalPhonemeInventory.Contains(phoneme.Phoneme))
                    throw new InvalidOperationException($"Unknown vocal phoneme: {phoneme.Phoneme}");
                VocalPhoneme definition = VocalPhonemeInventory.Get(phoneme.Phoneme);
                bool beat = phoneme.Phoneme.StartsWith("beat-", StringComparison.Ordinal);
                if (phoneme.Phoneme == "unsupported")
                {
                    milliseconds = settings.BeepForUnsupportedSounds ? 150 : 0;
                    minimumFrames = settings.BeepForUnsupportedSounds ? 1 : 0;
                }
                else
                {
                    milliseconds = definition.DurationMs * (beat ? 1 : DurationMultiplier(phoneme));
                    if (definition.IsVowel)
                        milliseconds *= settings.Language == VocalLanguage.AnimalCrossing ? 1.35 : 1.75;
                    minimumFrames = 4;
                }
            }
            else
            {
                throw new InvalidOperationException("Unknown vocal synthesis token.");
            }

            if (!double.IsFinite(milliseconds) || milliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(syllable), "A vocal token has an invalid duration.");
            double frameValue = milliseconds * settings.SampleRate / (1000 * settings.Speed);
            if (!double.IsFinite(frameValue) || frameValue > MaxOutputSamples / settings.Channels)
                throw new InvalidOperationException("The MIDI-sung vocal would exceed the supported output size.");
            int frames = Math.Max(minimumFrames, (int)Math.Round(frameValue));
            plan.Add(new RenderUnit(token, frames, semitoneOffset));
        }
        SustainVowelPairs(plan, cancellationToken);
        return plan;
    }

    private static void FitMidiSyllableToDuration(
        List<RenderUnit> plan, int targetFrames, CancellationToken cancellationToken)
    {
        long naturalFrameCount = plan.Sum(unit => (long)unit.Frames);
        if (naturalFrameCount > MaxOutputSamples)
            throw new InvalidOperationException("The vocal syllable would exceed the supported render size.");
        int naturalFrames = (int)naturalFrameCount;
        if (naturalFrames <= 0)
            throw new InvalidOperationException("A MIDI note is mapped to a syllable with no renderable sound.");
        int audibleUnits = plan.Count(unit => unit.Frames > 0);
        if (targetFrames < audibleUnits)
            throw new InvalidOperationException("A MIDI note is too short to fit the mapped syllable.");
        if (targetFrames >= naturalFrames)
        {
            int holdIndex = Enumerable.Range(0, plan.Count)
                .LastOrDefault(index => plan[index].Token is VocalPhonemeToken phoneme
                    && VocalPhonemeInventory.Get(phoneme.Phoneme).IsVowel, -1);
            if (holdIndex < 0) holdIndex = plan.Count - 1;
            plan[holdIndex] = plan[holdIndex] with { Frames = plan[holdIndex].Frames + targetFrames - naturalFrames };
            return;
        }

        double scale = targetFrames / (double)naturalFrames;
        int scaledTotal = 0;
        for (int i = 0; i < plan.Count; i++)
        {
            if ((i & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (plan[i].Frames <= 0) continue;
            int frames = Math.Max(1, (int)Math.Floor(plan[i].Frames * scale));
            plan[i] = plan[i] with { Frames = frames };
            scaledTotal += frames;
        }

        int excessFrames = scaledTotal - targetFrames;
        if (excessFrames > 0)
        {
            foreach (int index in Enumerable.Range(0, plan.Count)
                .Where(index => plan[index].Frames > 1)
                .OrderByDescending(index => plan[index].Frames))
            {
                int reduction = Math.Min(excessFrames, plan[index].Frames - 1);
                plan[index] = plan[index] with { Frames = plan[index].Frames - reduction };
                excessFrames -= reduction;
                if (excessFrames == 0) break;
            }
            if (excessFrames > 0)
                throw new InvalidOperationException("A MIDI note is too short to fit the mapped syllable.");
            scaledTotal = targetFrames;
        }

        if (scaledTotal < targetFrames)
        {
            int holdIndex = Enumerable.Range(0, plan.Count)
                .LastOrDefault(index => plan[index].Token is VocalPhonemeToken phoneme
                    && VocalPhonemeInventory.Get(phoneme.Phoneme).IsVowel, -1);
            if (holdIndex < 0) holdIndex = plan.Count - 1;
            plan[holdIndex] = plan[holdIndex] with { Frames = plan[holdIndex].Frames + targetFrames - scaledTotal };
        }
    }

    private static float[] RenderMidiSyllable(
        IReadOnlyList<RenderUnit> plan, VocalMidiScheduledNote note, VocalSynthSettings settings,
        CancellationToken cancellationToken)
    {
        var pcm = new List<float>(checked(note.DurationFrames * settings.Channels));
        var state = new VoiceState { Frame = note.StartFrame };
        var random = new Random(unchecked(StableSeed(settings) ^ note.NoteNumber * 16777619 ^ note.SyllableIndex));
        for (int i = 0; i < plan.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RenderUnit unit = plan[i];
            if (unit.Token is VocalPauseToken)
            {
                AppendSilence(pcm, unit.Frames, settings.Channels, cancellationToken);
                state.Frame += unit.Frames;
                continue;
            }
            if (unit.Token is not VocalPhonemeToken token) continue;

            VocalPhoneme phoneme = VocalPhonemeInventory.Get(token.Phoneme);
            if (phoneme.Symbol == "sil")
            {
                AppendSilence(pcm, unit.Frames, settings.Channels, cancellationToken);
                state.Frame += unit.Frames;
            }
            else if (phoneme.Symbol == "unsupported")
            {
                if (settings.BeepForUnsupportedSounds)
                    RenderUnsupportedBeep(pcm, unit.Frames, settings, state, cancellationToken);
                else
                    AppendSilence(pcm, unit.Frames, settings.Channels, cancellationToken);
                state.Frame += unit.Frames;
            }
            else
            {
                VocalPhoneme? previous = GetNeighbor(plan, i - 1);
                VocalPhoneme? next = GetNeighbor(plan, i + 1);
                RenderPhoneme(pcm, phoneme, token, previous, next, unit.Frames, unit.Note,
                    settings, state, random, cancellationToken);
            }
        }
        if (pcm.Count != note.DurationFrames * settings.Channels)
            throw new InvalidOperationException("The MIDI syllable render did not match its scheduled duration.");
        return pcm.ToArray();
    }

    private static void MixMidiVoice(
        float[] output, float[] voice, VocalMidiScheduledNote note, int channels, CancellationToken cancellationToken)
    {
        int startSample = checked(note.StartFrame * channels);
        float gain = Math.Clamp(note.Velocity / 127f, 0.05f, 1f);
        for (int i = 0; i < voice.Length; i++)
        {
            if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            int outputIndex = startSample + i;
            if ((uint)outputIndex >= (uint)output.Length)
                throw new InvalidOperationException("A scheduled MIDI voice exceeds the output timeline.");
            output[outputIndex] += voice[i] * gain;
        }
    }
}
