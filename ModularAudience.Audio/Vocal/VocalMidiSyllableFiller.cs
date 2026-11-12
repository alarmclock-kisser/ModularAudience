using System.Text;
using ModularAudience.Audio.Midi;

namespace ModularAudience.Audio.Vocal;

public sealed record VocalMidiSyllableFillResult(
    string AppendedText,
    int AddedSyllables,
    int ExistingSyllables,
    int MidiSyllables);

internal static class VocalMidiSyllableFiller
{
    private const double MillisecondsPerVowelCharacter = 100;
    internal const int MaximumVowelCharacters = 8;

    private sealed record SyllableTemplate(
        string Prefix,
        string Vowel,
        string RepeatUnit,
        string Suffix,
        string PhraseEndCoda)
    {
        internal string Build(int requestedVowelCharacters, bool phraseEnd)
        {
            int vowelCharacters = Math.Clamp(requestedVowelCharacters, Vowel.Length, MaximumVowelCharacters);
            var result = new StringBuilder(Prefix, Prefix.Length + vowelCharacters + Suffix.Length + PhraseEndCoda.Length);
            result.Append(Vowel);
            for (int count = Vowel.Length; count < vowelCharacters && RepeatUnit.Length > 0; count += RepeatUnit.Length)
                result.Append(RepeatUnit);
            result.Append(Suffix);
            if (phraseEnd) result.Append(PhraseEndCoda);
            return result.ToString();
        }
    }

    private static readonly IReadOnlyDictionary<VocalLanguage, SyllableTemplate[]> Templates =
        new Dictionary<VocalLanguage, SyllableTemplate[]>
        {
            [VocalLanguage.English] =
            [
                new("d", "oo", "o", "", "l"),
                new("d", "o", "o", "w", "l"),
                new("d", "a", "a", "", "l"),
                new("d", "e", "e", "", "l"),
                new("d", "ee", "e", "", "l"),
            ],
            [VocalLanguage.German] =
            [
                new("d", "u", "u", "", "l"),
                new("d", "o", "o", "", "l"),
                new("d", "\u00f6", "\u00f6", "", "l"),
                new("d", "a", "a", "", "l"),
                new("d", "\u00e4", "\u00e4", "", "l"),
                new("d", "ee", "e", "", "l"),
                new("d", "\u00fc", "\u00fc", "", "l"),
                new("d", "i", "i", "", "l"),
            ],
            [VocalLanguage.French] =
            [
                new("d", "ou", "", "", "l"),
                new("d", "o", "o", "", "l"),
                new("d", "a", "a", "", "l"),
                new("d", "e", "e", "", "l"),
                new("d", "i", "i", "", "l"),
            ],
            [VocalLanguage.Spanish] = CreateLatinTemplates("du", "do", "da", "de", "di"),
            [VocalLanguage.Italian] = CreateLatinTemplates("du", "do", "da", "de", "di"),
            [VocalLanguage.Turkish] = CreateLatinTemplates("du", "do", "da", "de", "d\u00fc", "di"),
            [VocalLanguage.Chinese] =
            [
                new("d", "u", "", "", ""),
                new("d", "o", "", "ng", ""),
                new("d", "a", "", "", ""),
                new("d", "e", "", "", ""),
                new("d", "i", "", "", ""),
            ],
            [VocalLanguage.Japanese] =
            [
                new("", "\u3065", "\u30fc", "", ""),
                new("", "\u3069", "\u30fc", "", ""),
                new("", "\u3060", "\u30fc", "", ""),
                new("", "\u3067", "\u30fc", "", ""),
                new("", "\u3062", "\u30fc", "", ""),
            ],
            [VocalLanguage.Arabic] =
            [
                new("\u062f", "\u064f", "", "", ""),
                new("\u062f", "\u064e", "", "", ""),
                new("\u062f", "\u0650", "", "", ""),
            ],
            [VocalLanguage.Russian] =
            [
                new("\u0434", "\u0443", "\u0443", "", "\u043b"),
                new("\u0434", "\u043e", "\u043e", "", "\u043b"),
                new("\u0434", "\u0430", "\u0430", "", "\u043b"),
                new("\u0434", "\u044d", "\u044d", "", "\u043b"),
                new("\u0434", "\u0438", "\u0438", "", "\u043b"),
            ],
            [VocalLanguage.AnimalCrossing] =
            [
                new("", "u", "", "", ""),
                new("", "o", "", "", ""),
                new("", "a", "", "", ""),
                new("", "e", "", "", ""),
                new("", "i", "", "", ""),
            ],
            [VocalLanguage.Beatboxing] =
            [
                new("", "b", "", "", ""),
                new("", "t", "", "", ""),
                new("", "k", "", "", ""),
            ],
        };

    internal static VocalMidiSyllableFillResult FillMissing(
        VocalSynthSettings settings, string existingText, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(existingText);
        if (!settings.Singing)
            throw new InvalidOperationException("Fill Syllables requires the Singing voice.");
        MidiFileData midi = settings.SingingMidiFile
            ?? throw new InvalidOperationException("Load a MIDI file before filling syllables.");
        if (settings.SingingMidiTrackIndex is not int trackIndex)
            throw new InvalidOperationException("Select a MIDI track before filling syllables.");
        MidiTrackData track = midi.Tracks.FirstOrDefault(candidate => candidate.Index == trackIndex)
            ?? throw new InvalidOperationException("The selected MIDI track does not exist.");
        if (track.Notes.Count == 0)
            throw new InvalidOperationException("The selected MIDI track contains no notes.");
        if (!double.IsFinite(midi.DefaultBpm) || midi.DefaultBpm <= 0
            || midi.TicksPerQuarterNote <= 0
            || !double.IsFinite(settings.Speed) || settings.Speed <= 0)
            throw new InvalidDataException("The MIDI timing or vocal speed is invalid.");
        if (!Templates.TryGetValue(settings.Language, out SyllableTemplate[]? palette))
            throw new ArgumentOutOfRangeException(nameof(settings), settings.Language, "The selected language is not supported.");

        IReadOnlyList<VocalSyllableTextRange> existingRanges = VocalTextParser.GetSyllableTextRanges(
            settings.Language, existingText, settings.EmphasisSyntax, cancellationToken);
        MidiNoteData[][] noteGroups = VocalMidiSinging.GroupNotesByStartTick(track, cancellationToken);
        if (existingRanges.Count >= noteGroups.Length)
        {
            return new VocalMidiSyllableFillResult(
                string.Empty, 0, existingRanges.Count, noteGroups.Length);
        }

        double[] pitches = new double[noteGroups.Length];
        double lowestPitch = double.PositiveInfinity;
        double highestPitch = double.NegativeInfinity;
        for (int groupIndex = 0; groupIndex < noteGroups.Length; groupIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MidiNoteData[] group = noteGroups[groupIndex];
            double pitchSum = 0;
            foreach (MidiNoteData note in group)
            {
                cancellationToken.ThrowIfCancellationRequested();
                pitchSum += note.NoteNumber + settings.SingingMidiSemitoneOffset;
            }
            pitches[groupIndex] = pitchSum / group.Length;
            lowestPitch = Math.Min(lowestPitch, pitches[groupIndex]);
            highestPitch = Math.Max(highestPitch, pitches[groupIndex]);
        }

        var appended = new StringBuilder();
        for (int groupIndex = existingRanges.Count; groupIndex < noteGroups.Length; groupIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string separator = groupIndex == existingRanges.Count
                ? GetInitialSeparator(existingText, existingRanges.Count, groupIndex, noteGroups, midi)
                : GetMidiSeparator(noteGroups[groupIndex - 1], noteGroups[groupIndex], midi);
            appended.Append(separator);

            int paletteIndex = GetPaletteIndex(pitches[groupIndex], lowestPitch, highestPitch, palette.Length);
            SyllableTemplate template = palette[paletteIndex];
            int vowelCharacters = GetVowelCharacterCount(noteGroups[groupIndex], midi, settings.Speed, template);
            bool phraseEnd = groupIndex + 1 == noteGroups.Length
                || GetMidiSeparator(noteGroups[groupIndex], noteGroups[groupIndex + 1], midi) != " ";
            appended.Append(template.Build(vowelCharacters, phraseEnd));
        }

        string appendedText = appended.ToString();
        int completedSyllableCount = VocalTextParser.GetSyllableTextRanges(
            settings.Language, existingText + appendedText, settings.EmphasisSyntax, cancellationToken).Count;
        if (completedSyllableCount != noteGroups.Length)
            throw new InvalidDataException(
                $"Generated {settings.Language.DisplayName()} text maps to {completedSyllableCount} syllables for "
                + $"{noteGroups.Length} MIDI note groups.");

        return new VocalMidiSyllableFillResult(
            appendedText, noteGroups.Length - existingRanges.Count, existingRanges.Count, noteGroups.Length);
    }

    private static SyllableTemplate[] CreateLatinTemplates(params string[] syllables) =>
        syllables.Select(syllable =>
        {
            string vowel = syllable[1..];
            return new SyllableTemplate("d", vowel, vowel, "", "l");
        }).ToArray();

    private static int GetPaletteIndex(
        double pitch, double lowestPitch, double highestPitch, int paletteLength)
    {
        if (paletteLength <= 1 || highestPitch <= lowestPitch) return paletteLength / 2;
        double normalized = (pitch - lowestPitch) / (highestPitch - lowestPitch);
        return Math.Clamp(
            (int)Math.Round(normalized * (paletteLength - 1), MidpointRounding.AwayFromZero),
            0,
            paletteLength - 1);
    }

    private static int GetVowelCharacterCount(
        IReadOnlyList<MidiNoteData> chord, MidiFileData midi, double speed, SyllableTemplate template)
    {
        if (template.RepeatUnit.Length == 0) return template.Vowel.Length;
        long durationTicks = chord.Max(note => Math.Max(1, note.DurationTicks));
        double durationMilliseconds = durationTicks * 60_000.0
            / (midi.DefaultBpm * midi.TicksPerQuarterNote * speed);
        if (!double.IsFinite(durationMilliseconds))
            throw new InvalidDataException("A MIDI note has an invalid duration.");
        if (durationMilliseconds >= MaximumVowelCharacters * MillisecondsPerVowelCharacter)
            return MaximumVowelCharacters;
        return Math.Clamp(
            (int)Math.Ceiling(durationMilliseconds / MillisecondsPerVowelCharacter),
            1,
            MaximumVowelCharacters);
    }

    private static string GetInitialSeparator(
        string existingText,
        int existingSyllableCount,
        int groupIndex,
        IReadOnlyList<MidiNoteData[]> noteGroups,
        MidiFileData midi)
    {
        if (existingText.Length == 0 || char.IsWhiteSpace(existingText[^1])) return string.Empty;
        if (existingSyllableCount == 0) return " ";
        string midiSeparator = GetMidiSeparator(noteGroups[groupIndex - 1], noteGroups[groupIndex], midi);
        if (midiSeparator == Environment.NewLine) return midiSeparator;
        return char.IsPunctuation(existingText[^1]) ? " " : midiSeparator;
    }

    private static string GetMidiSeparator(
        IReadOnlyList<MidiNoteData> previous, IReadOnlyList<MidiNoteData> current, MidiFileData midi)
    {
        long previousStart = previous[0].StartTick;
        long currentStart = current[0].StartTick;
        if (previousStart / midi.TicksPerBar != currentStart / midi.TicksPerBar)
            return Environment.NewLine;

        long previousEnd = previous.Max(note => note.StartTick + Math.Max(1, note.DurationTicks));
        long restTicks = Math.Max(0, currentStart - previousEnd);
        return restTicks >= Math.Max(1, midi.TicksPerQuarterNote / 2)
            ? ", "
            : " ";
    }
}
