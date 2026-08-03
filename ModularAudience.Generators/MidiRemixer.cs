using ModularAudience.Audio.Midi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ModularAudience.Generators
{
    public static class MidiRemixer
    {
        public static async Task<MidiFileData> RemixAsync(MidiFileData midiFileData, MidiRemixSettings settings, int? trackIndex = 0)
        {
            ArgumentNullException.ThrowIfNull(midiFileData);
            ArgumentNullException.ThrowIfNull(settings);
            MidiTrackData sourceTrack = FindTrack(midiFileData, trackIndex);
            MidiRemixSettings normalized = NormalizeSettings(settings);
            return await Task.Run(() => RemixTrack(midiFileData, sourceTrack, normalized)).ConfigureAwait(false);
        }

        private static MidiTrackData FindTrack(MidiFileData midi, int? trackIndex)
        {
            if (midi.Tracks.Count == 0)
            {
                throw new InvalidOperationException("The MIDI file does not contain any tracks.");
            }

            return trackIndex.HasValue
                ? midi.Tracks.FirstOrDefault(track => track.Index == trackIndex.Value)
                    ?? throw new ArgumentOutOfRangeException(nameof(trackIndex), "The requested MIDI track ID was not found.")
                : midi.Tracks[0];
        }

        private static MidiRemixSettings NormalizeSettings(MidiRemixSettings source) => new()
        {
            DenoiseFactor = Math.Clamp(source.DenoiseFactor, 0f, 1f),
            FrequencyShift = Math.Clamp(source.FrequencyShift, -4f, 4f),
            TempoShift = Math.Clamp(source.TempoShift, -0.95f, 4f),
            PatternDerivationFactor = Math.Clamp(source.PatternDerivationFactor, 0f, 1f),
            PatternRearrangementFactor = Math.Clamp(source.PatternRearrangementFactor, 0f, 1f),
            PatternMinLength = Math.Clamp(source.PatternMinLength, 1, 128),
            PatternMaxLength = Math.Clamp(Math.Max(source.PatternMinLength, source.PatternMaxLength), 1, 256),
            DerivedPatternsPoolSize = Math.Clamp(source.DerivedPatternsPoolSize, 1, 128)
        };

        private static MidiFileData RemixTrack(MidiFileData source, MidiTrackData track, MidiRemixSettings settings)
        {
            List<MidiNoteData> notes = track.Notes.OrderBy(note => note.StartTick).ToList();
            if (notes.Count == 0)
            {
                return MidiFileData.CreateGenerated([new MidiTrackData { Index = track.Index, Name = track.Name }], source.TicksPerQuarterNote, source.DefaultBpm * (1f + settings.TempoShift), pitchFrequency: ShiftFrequency(source.PitchFrequency, settings.FrequencyShift));
            }

            Random random = new(StableSeed(notes, settings));
            List<Pattern> patterns = DetectPatterns(notes, settings.PatternMinLength, settings.PatternMaxLength);
            List<Pattern> pool = patterns.SelectMany(pattern => DerivePatterns(pattern, settings, random)).ToList();
            List<Pattern> sequence = ArrangePatterns(patterns, pool, settings, random);
            List<MidiNoteData> remixedNotes = RenderPatterns(sequence, settings, random);
            MidiTrackData resultTrack = new() { Index = track.Index, Name = string.IsNullOrWhiteSpace(track.Name) ? "Remixed" : $"{track.Name} - Remix" };
            resultTrack.Notes.AddRange(remixedNotes);
            resultTrack.ExtendLengthTo(remixedNotes.Max(note => note.StartTick + note.DurationTicks));
            double tempo = Math.Clamp(source.DefaultBpm * (1.0 + settings.TempoShift), 20.0, 400.0);
            return MidiFileData.CreateGenerated([resultTrack], source.TicksPerQuarterNote, tempo, source.FilePath, ShiftFrequency(source.PitchFrequency, settings.FrequencyShift));
        }

        private static List<Pattern> DetectPatterns(List<MidiNoteData> notes, int minLength, int maxLength)
        {
            List<Pattern> result = [];
            int index = 0;
            while (index < notes.Count)
            {
                int length = Math.Min(maxLength, notes.Count - index);
                if (length < minLength)
                {
                result.Add(CreatePattern(notes[index..], index));
                    break;
                }

                int candidateLength = FindRepeatingLength(notes, index, length, minLength);
                result.Add(CreatePattern(notes.GetRange(index, candidateLength), index));
                index += candidateLength;
            }

            return result;
        }

        private static int FindRepeatingLength(List<MidiNoteData> notes, int start, int maxLength, int minLength)
        {
            for (int length = minLength; length <= maxLength / 2; length++)
            {
                if (start + length * 2 > notes.Count)
                {
                    break;
                }

                bool same = true;
                for (int offset = 0; offset < length; offset++)
                {
                    MidiNoteData first = notes[start + offset];
                    MidiNoteData second = notes[start + length + offset];
                    if (first.NoteNumber - notes[start].NoteNumber != second.NoteNumber - notes[start + length].NoteNumber || first.DurationTicks != second.DurationTicks)
                    {
                        same = false;
                        break;
                    }
                }

                if (same)
                {
                    return length;
                }
            }

            return maxLength;
        }

        private static List<Pattern> DerivePatterns(Pattern source, MidiRemixSettings settings, Random random)
        {
            List<Pattern> result = [source];
            int count = Math.Max(0, (int) Math.Round(settings.DerivedPatternsPoolSize * settings.PatternDerivationFactor));
            for (int index = 0; index < count; index++)
            {
                int transpose = random.Next(-7, 8);
                List<MidiNoteData> notes = source.Notes.Select(note => CloneNote(note, note.NoteNumber + transpose, note.StartTick, note.DurationTicks, note.Velocity)).ToList();
                if (random.NextDouble() < settings.PatternDerivationFactor)
                {
                    notes.Reverse();
                    notes = RebasePattern(notes);
                }

                Pattern derived = new(notes, source.SourceIndex);
                if (!result.Any(existing => PatternEquals(existing, derived)))
                {
                    result.Add(derived);
                }
            }

            return result;
        }

        private static List<Pattern> ArrangePatterns(List<Pattern> original, List<Pattern> pool, MidiRemixSettings settings, Random random)
        {
            List<Pattern> result = [];
            foreach (Pattern pattern in original)
            {
                Pattern selected = random.NextDouble() < settings.DenoiseFactor * settings.PatternDerivationFactor
                    ? pool.Where(candidate => candidate.Notes.Count == pattern.Notes.Count).OrderBy(_ => random.Next()).FirstOrDefault() ?? pattern
                    : pattern;
                result.Add(selected);
            }

            if (settings.PatternRearrangementFactor > 0)
            {
                int swaps = (int) Math.Round(result.Count * settings.PatternRearrangementFactor);
                for (int index = 0; index < swaps; index++)
                {
                    int first = random.Next(result.Count);
                    int second = random.Next(result.Count);
                    (result[first], result[second]) = (result[second], result[first]);
                }
            }

            return result;
        }

        private static List<MidiNoteData> RenderPatterns(List<Pattern> patterns, MidiRemixSettings settings, Random random)
        {
            List<MidiNoteData> result = [];
            long cursor = 0;
            foreach (Pattern pattern in patterns)
            {
                long patternLength = pattern.Notes.Max(note => note.StartTick + note.DurationTicks);
                double timeScale = 1.0 / (1.0 + settings.TempoShift);
                foreach (MidiNoteData note in pattern.Notes)
                {
                    bool preserve = random.NextDouble() > settings.DenoiseFactor;
                    int velocity = preserve ? note.Velocity : Math.Clamp(note.Velocity + random.Next(-12, 13), 1, 127);
                    result.Add(CloneNote(note, note.NoteNumber, cursor + (long) Math.Round(note.StartTick * timeScale), Math.Max(1, (long) Math.Round(note.DurationTicks * timeScale)), velocity));
                }

                cursor += Math.Max(1, (long) Math.Round(patternLength * timeScale));
            }

            return result;
        }

        private static Pattern CreatePattern(IEnumerable<MidiNoteData> notes, int sourceIndex)
        {
            List<MidiNoteData> ordered = notes.OrderBy(note => note.StartTick).ToList();
            return new Pattern(RebasePattern(ordered), sourceIndex);
        }

        private static MidiNoteData CloneNote(MidiNoteData source, int noteNumber, long startTick, long duration, int velocity) => new()
        {
            NoteNumber = Math.Clamp(noteNumber, 0, 127), Channel = source.Channel, Velocity = Math.Clamp(velocity, 1, 127), StartTick = Math.Max(0, startTick), DurationTicks = Math.Max(1, duration)
        };

        private static List<MidiNoteData> RebasePattern(List<MidiNoteData> notes)
        {
            long start = notes.Min(note => note.StartTick);
            return notes.Select(note => CloneNote(note, note.NoteNumber, note.StartTick - start, note.DurationTicks, note.Velocity)).ToList();
        }

        private static bool PatternEquals(Pattern left, Pattern right) => left.Notes.Count == right.Notes.Count && left.Notes.Zip(right.Notes).All(pair => pair.First.NoteNumber == pair.Second.NoteNumber && pair.First.DurationTicks == pair.Second.DurationTicks);

        private static double ShiftFrequency(double frequency, float shift) => Math.Clamp(frequency * Math.Pow(2.0, shift), 1.0, 1000.0);
        private static int StableSeed(List<MidiNoteData> notes, MidiRemixSettings settings) => HashCode.Combine(notes.Count, settings.PatternMinLength, settings.PatternMaxLength, settings.DerivedPatternsPoolSize);

        private sealed record Pattern(List<MidiNoteData> Notes, int SourceIndex);

    }



    public class MidiRemixSettings
    {
        /// <summary>
        /// Determines how much of the original MIDI file's content is preserved in the remix. A value of 0.0 means the remix will closely follow the original, while a value of 1.0 means the remix will be completely different.
        /// </summary>
        public float DenoiseFactor { get; set; } = 0.8f;

        /// <summary>
        /// Determines the amount of frequency shift applied to the remix. A value of 0.0 means no frequency shift, while a value of 1.0 means double frequency scaling, can also be negative.
        /// </summary>
        public float FrequencyShift { get; set; } = 0.0f;

        /// <summary>
        /// Determines the amount of tempo shift applied to the remix. A value of 0.0 means no tempo shift, while a value of 1.0 means double tempo scaling, can also be negative.
        /// </summary>
        public float TempoShift { get; set; } = 0.0f;

        /// <summary>
        /// Determines the amount of pattern derivation applied to the remix. 0.0 means recognised patterns will be preserved, while 1.0 means patterns will be derived strongly.
        /// </summary>
        public float PatternDerivationFactor { get; set; } = 0.5f;

        /// <summary>
        /// Determines the amount of pattern rearrangement applied to the remix. 0.0 means recognised pattern orders will be preserved, while 1.0 means patterns will be rearranged in a completely different order.
        /// </summary>
        public float PatternRearrangementFactor { get; set; } = 0.25f;

        /// <summary>
        /// Determines the minimum length of patterns to be considered.
        /// </summary>
        public int PatternMinLength { get; set; } = 4;

        /// <summary>
        /// Determines the maximum length of patterns to be considered.
        /// </summary>
        public int PatternMaxLength { get; set; } = 16;

        /// <summary>
        /// Determines the size of the pool of derived patterns to be used in the remix. Higher values mean more possible patterns will be created and chosen randomly from.
        /// </summary>
        public int DerivedPatternsPoolSize { get; set; } = 16;
    }
}
