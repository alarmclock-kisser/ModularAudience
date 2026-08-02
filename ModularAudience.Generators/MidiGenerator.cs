using ModularAudience.Audio;
using ModularAudience.Audio.Midi;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ModularAudience.Generators
{
    public static class MidiGenerator
    {
        public static async Task<MidiFileData?> GenerateMidiFileDataAsync(MidiGenerationSettings settings, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            try
            {
                ArgumentNullException.ThrowIfNull(settings);
                return settings.Preset switch
                {
                    MidiGenerationPreset.Default => await GenerateDefaultPresetAsync(settings, progress, cancellationToken),
                    MidiGenerationPreset.Jazz => await GenerateJazzPresetAsync(settings, progress, cancellationToken),
                    MidiGenerationPreset.Rock => await GenerateRockPresetAsync(settings, progress, cancellationToken),
                    MidiGenerationPreset.Classical => await GenerateClassicalPresetAsync(settings, progress, cancellationToken),
                    MidiGenerationPreset.Electronic => await GenerateElectronicPresetAsync(settings, progress, cancellationToken),
                    MidiGenerationPreset.Breakbeat => await GenerateBreakbeatPresetAsync(settings, progress, cancellationToken),
                    MidiGenerationPreset.Glitch => await GenerateGlitchPresetAsync(settings, progress, cancellationToken),
                    MidiGenerationPreset.Chiptune => await GenerateChiptunePresetAsync(settings, progress, cancellationToken),
                    MidiGenerationPreset.Endboss => await GenerateEndbossPresetAsync(settings, progress, cancellationToken),
                    _ => throw new ArgumentOutOfRangeException(nameof(settings.Preset), settings.Preset, "Unknown MIDI generation preset.")
                };
            }
            catch (Exception ex)
            {
                LogCollection.Log($"Error generating MIDI file data: {ex.Message}");
                return null;
            }
        }

        public static async Task<MidiFileData> GenerateDefaultPresetAsync(MidiGenerationSettings settings, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
            await GeneratePresetAsync(settings, BuildDefault, progress, cancellationToken);

        public static async Task<MidiFileData> GenerateJazzPresetAsync(MidiGenerationSettings settings, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
            await GeneratePresetAsync(settings, BuildJazz, progress, cancellationToken);

        public static async Task<MidiFileData> GenerateRockPresetAsync(MidiGenerationSettings settings, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
            await GeneratePresetAsync(settings, BuildRock, progress, cancellationToken);

        public static async Task<MidiFileData> GenerateClassicalPresetAsync(MidiGenerationSettings settings, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
            await GeneratePresetAsync(settings, BuildClassical, progress, cancellationToken);

        public static async Task<MidiFileData> GenerateElectronicPresetAsync(MidiGenerationSettings settings, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
            await GeneratePresetAsync(settings, BuildElectronic, progress, cancellationToken);

        public static async Task<MidiFileData> GenerateBreakbeatPresetAsync(MidiGenerationSettings settings, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
            await GeneratePresetAsync(settings, BuildBreakbeat, progress, cancellationToken);

        public static async Task<MidiFileData> GenerateGlitchPresetAsync(MidiGenerationSettings settings, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
            await GeneratePresetAsync(settings, BuildGlitch, progress, cancellationToken);

        public static async Task<MidiFileData> GenerateChiptunePresetAsync(MidiGenerationSettings settings, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
            await GeneratePresetAsync(settings, BuildChiptune, progress, cancellationToken);

        public static async Task<MidiFileData> GenerateEndbossPresetAsync(MidiGenerationSettings settings, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
            await GeneratePresetAsync(settings, BuildEndboss, progress, cancellationToken);

        private static Task<MidiFileData> GeneratePresetAsync(MidiGenerationSettings settings, Action<List<MidiTrackData>, MidiGenerationSettings, Random, IProgress<double>?, CancellationToken> builder, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(builder);
            ValidateSettings(settings);
            return Task.Run(() =>
            {
                Random random = settings.Seed.HasValue ? new(settings.Seed.Value) : new();
                List<MidiTrackData> tracks = CreateTracks(settings);
                progress?.Report(0.0);
                builder(tracks, settings, random, progress, cancellationToken);
                ApplyIntensity(tracks, settings.Intensity);
                progress?.Report(1.0);
                return MidiFileData.CreateGenerated(tracks, settings.TicksPerQuarterNote, settings.Tempo, settings.FilePath, settings.PitchFrequency);
            }, cancellationToken);
        }

        private static void ApplyIntensity(List<MidiTrackData> tracks, double intensity)
        {
            double factor = Math.Clamp(intensity, 0.0, 100.0) / 100.0;
            if (factor <= 0.0)
            {
                foreach (MidiTrackData track in tracks)
                {
                    track.Notes.Clear();
                }
                return;
            }

            foreach (MidiTrackData track in tracks)
            {
                List<MidiNoteData> notes = track.Notes.ToList();
                track.Notes.Clear();
                foreach (MidiNoteData note in notes)
                {
                    track.Notes.Add(new MidiNoteData
                    {
                        NoteNumber = note.NoteNumber,
                        Channel = note.Channel,
                        Velocity = Math.Clamp((int) Math.Round(note.Velocity * factor), 1, 127),
                        StartTick = note.StartTick,
                        DurationTicks = note.DurationTicks
                    });
                }
            }
        }

        private static List<MidiTrackData> CreateTracks(MidiGenerationSettings settings)
        {
            List<MidiTrackData> tracks = [];
            for (int index = 0; index < settings.NumberOfTracks; index++)
            {
                tracks.Add(new MidiTrackData { Index = index, Name = $"{settings.Preset} {index + 1}" });
            }
            return tracks;
        }

        private static void ValidateSettings(MidiGenerationSettings settings)
        {
            if (settings.TicksPerQuarterNote <= 0) throw new ArgumentOutOfRangeException(nameof(settings.TicksPerQuarterNote));
            if (settings.NumberOfBars <= 0) throw new ArgumentOutOfRangeException(nameof(settings.NumberOfBars));
            if (settings.NumberOfTracks <= 0) throw new ArgumentOutOfRangeException(nameof(settings.NumberOfTracks));
            if (settings.TimeSignatureNumerator <= 0) throw new ArgumentOutOfRangeException(nameof(settings.TimeSignatureNumerator));
            if (settings.TimeSignatureDenominator <= 0 || (settings.TimeSignatureDenominator & (settings.TimeSignatureDenominator - 1)) != 0)
                throw new ArgumentOutOfRangeException(nameof(settings.TimeSignatureDenominator));
            if (!double.IsFinite(settings.Tempo) || settings.Tempo < 20.0 || settings.Tempo > 400.0)
                throw new ArgumentOutOfRangeException(nameof(settings.Tempo));
            if (!double.IsFinite(settings.PitchFrequency) || settings.PitchFrequency <= 0)
                throw new ArgumentOutOfRangeException(nameof(settings.PitchFrequency));
            if (!double.IsFinite(settings.Intensity) || settings.Intensity < 0.0 || settings.Intensity > 100.0)
                throw new ArgumentOutOfRangeException(nameof(settings.Intensity));
        }

        private static long BarTicks(MidiGenerationSettings settings) =>
            settings.TicksPerQuarterNote * settings.TimeSignatureNumerator * 4L / settings.TimeSignatureDenominator;

        private static long StepTicks(MidiGenerationSettings settings, int stepsPerBeat = 4) =>
            Math.Max(1, settings.TicksPerQuarterNote / stepsPerBeat);

        private static int ScaleNote(MidiGenerationSettings settings, int degree, int octave = 0)
        {
            int[] scale = [0, 2, 4, 5, 7, 9, 11];
            int normalizedDegree = Math.Abs(degree) % scale.Length;
            return Math.Clamp(60 + settings.KeySignature + scale[normalizedDegree] + (degree / scale.Length + octave) * 12, 0, 127);
        }

        private static void AddNote(MidiTrackData track, int noteNumber, long startTick, long durationTicks, int velocity, int channel = 0)
        {
            track.Notes.Add(new MidiNoteData
            {
                NoteNumber = Math.Clamp(noteNumber, 0, 127),
                Channel = Math.Clamp(channel, 0, 15),
                Velocity = Math.Clamp(velocity, 1, 127),
                StartTick = Math.Max(0, startTick),
                DurationTicks = Math.Max(1, durationTicks)
            });
        }

        private static void FinishTracks(List<MidiTrackData> tracks, long lengthTicks)
        {
            foreach (MidiTrackData track in tracks)
            {
                track.ExtendLengthTo(lengthTicks);
            }
        }

        private static bool Chance(Random random, double probability) => random.NextDouble() < probability;

        private static void AddDrum(MidiTrackData track, int note, long tick, long duration, int velocity) =>
            AddNote(track, note, tick, duration, velocity, 9);

        private static void CheckCancellation(CancellationToken cancellationToken, int index)
        {
            if ((index & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
        }

        private static void BuildDefault(List<MidiTrackData> tracks, MidiGenerationSettings settings, Random random, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            long step = StepTicks(settings, 2);
            for (int bar = 0; bar < settings.NumberOfBars; bar++)
            {
                for (int beat = 0; beat < settings.TimeSignatureNumerator * 2; beat++)
                {
                    CheckCancellation(cancellationToken, bar * 16 + beat);
                    AddNote(tracks[0], ScaleNote(settings, beat + bar % 4), bar * BarTicks(settings) + beat * step, step, 88);
                }
                progress?.Report((bar + 1) / (double) settings.NumberOfBars);
            }
            FinishTracks(tracks, settings.NumberOfBars * BarTicks(settings));
        }

        private static void BuildJazz(List<MidiTrackData> tracks, MidiGenerationSettings settings, Random random, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            long beat = settings.TicksPerQuarterNote;
            for (int bar = 0; bar < settings.NumberOfBars; bar++)
            {
                long start = bar * BarTicks(settings);
                for (int trackIndex = 0; trackIndex < tracks.Count; trackIndex++)
                {
                    AddNote(tracks[trackIndex], ScaleNote(settings, bar + trackIndex * 2, -1), start, beat * 2, 76, trackIndex % 9);
                    AddNote(tracks[trackIndex], ScaleNote(settings, bar + 3 + trackIndex * 2), start + beat * 2, beat * 2, 68, trackIndex % 9);
                }
                AddDrum(tracks[0], 42, start, beat / 2, 62);
                AddDrum(tracks[0], 42, start + beat * 2, beat / 2, 66);
                AddDrum(tracks[0], 38, start + beat, beat / 2, 82);
                CheckCancellation(cancellationToken, bar);
                progress?.Report((bar + 1) / (double) settings.NumberOfBars);
            }
            FinishTracks(tracks, settings.NumberOfBars * BarTicks(settings));
        }

        private static void BuildRock(List<MidiTrackData> tracks, MidiGenerationSettings settings, Random random, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            long step = StepTicks(settings);
            for (int bar = 0; bar < settings.NumberOfBars; bar++)
            {
                long start = bar * BarTicks(settings);
                for (int index = 0; index < settings.TimeSignatureNumerator * 4; index++)
                {
                    long tick = start + index * step;
                    if (index % 4 == 0 || index == 7 || index == 10) AddDrum(tracks[0], 36, tick, step, 108);
                    if (index % 8 == 4) AddDrum(tracks[0], 38, tick, step, 112);
                    AddDrum(tracks[0], 42, tick, step / 2, index % 2 == 0 ? 70 : 54);
                }
                AddNote(tracks[Math.Min(1, tracks.Count - 1)], ScaleNote(settings, bar % 4, -1), start, BarTicks(settings), 86);
                CheckCancellation(cancellationToken, bar);
                progress?.Report((bar + 1) / (double) settings.NumberOfBars);
            }
            FinishTracks(tracks, settings.NumberOfBars * BarTicks(settings));
        }

        private static void BuildClassical(List<MidiTrackData> tracks, MidiGenerationSettings settings, Random random, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            long beat = settings.TicksPerQuarterNote;
            for (int bar = 0; bar < settings.NumberOfBars; bar++)
            {
                long start = bar * BarTicks(settings);
                int root = (bar / 2) % 4;
                for (int voice = 0; voice < tracks.Count; voice++)
                {
                    for (int note = 0; note < 4; note++)
                    {
                        int degree = root + note * 2 + voice;
                        AddNote(tracks[voice], ScaleNote(settings, degree, voice), start + note * beat, beat, 72 + voice * 8, voice % 9);
                    }
                }
                CheckCancellation(cancellationToken, bar);
                progress?.Report((bar + 1) / (double) settings.NumberOfBars);
            }
            FinishTracks(tracks, settings.NumberOfBars * BarTicks(settings));
        }

        private static void BuildElectronic(List<MidiTrackData> tracks, MidiGenerationSettings settings, Random random, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            long step = StepTicks(settings);
            for (int bar = 0; bar < settings.NumberOfBars; bar++)
            {
                long start = bar * BarTicks(settings);
                for (int index = 0; index < settings.TimeSignatureNumerator * 4; index++)
                {
                    long tick = start + index * step;
                    if (index % 4 == 0) AddDrum(tracks[0], 36, tick, step, 105);
                    if (index % 8 == 4) AddDrum(tracks[0], 39, tick, step, 92);
                    if (index % 2 == 0) AddDrum(tracks[0], 42, tick, step / 2, 62);
                    if (Chance(random, 0.65)) AddNote(tracks[Math.Min(1, tracks.Count - 1)], ScaleNote(settings, index + bar, 1), tick, step / 2, 70);
                }
                CheckCancellation(cancellationToken, bar);
                progress?.Report((bar + 1) / (double) settings.NumberOfBars);
            }
            FinishTracks(tracks, settings.NumberOfBars * BarTicks(settings));
        }

        private static void BuildBreakbeat(List<MidiTrackData> tracks, MidiGenerationSettings settings, Random random, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            long step = StepTicks(settings);
            int[] kicks = [0, 7, 10, 12];
            int[] snares = [4, 12];
            for (int bar = 0; bar < settings.NumberOfBars; bar++)
            {
                long start = bar * BarTicks(settings);
                foreach (int index in kicks) AddDrum(tracks[0], 36, start + index * step, step, 105);
                foreach (int index in snares) AddDrum(tracks[0], 38, start + index * step, step, 112);
                for (int index = 0; index < 16; index++)
                {
                    if (index % 2 == 0 || Chance(random, 0.2)) AddDrum(tracks[0], 42, start + index * step, step / 2, 58 + random.Next(25));
                }
                CheckCancellation(cancellationToken, bar);
                progress?.Report((bar + 1) / (double) settings.NumberOfBars);
            }
            FinishTracks(tracks, settings.NumberOfBars * BarTicks(settings));
        }

        private static void BuildGlitch(List<MidiTrackData> tracks, MidiGenerationSettings settings, Random random, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            long step = StepTicks(settings, 8);
            int totalSteps = settings.NumberOfBars * settings.TimeSignatureNumerator * 8;
            for (int index = 0; index < totalSteps; index++)
            {
                int trackIndex = index % tracks.Count;
                long tick = index * step;
                AddNote(tracks[trackIndex], ScaleNote(settings, random.Next(7), random.Next(-1, 2)), tick, step, 45 + random.Next(80), trackIndex % 9);
                if (index % 3 == 0) AddDrum(tracks[0], 37, tick, step / 2, 70);
                CheckCancellation(cancellationToken, index);
                progress?.Report((index + 1) / (double) totalSteps);
            }
            FinishTracks(tracks, settings.NumberOfBars * BarTicks(settings));
        }

        private static void BuildChiptune(List<MidiTrackData> tracks, MidiGenerationSettings settings, Random random, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            long step = StepTicks(settings);
            for (int bar = 0; bar < settings.NumberOfBars; bar++)
            {
                long start = bar * BarTicks(settings);
                for (int index = 0; index < 16; index++)
                {
                    AddNote(tracks[0], ScaleNote(settings, (bar * 3 + index) % 7, index % 2), start + index * step, step, 82, 0);
                    if (index % 4 == 0) AddDrum(tracks[0], 36, start + index * step, step, 92);
                }
                CheckCancellation(cancellationToken, bar);
                progress?.Report((bar + 1) / (double) settings.NumberOfBars);
            }
            FinishTracks(tracks, settings.NumberOfBars * BarTicks(settings));
        }

        private static void BuildEndboss(List<MidiTrackData> tracks, MidiGenerationSettings settings, Random random, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            long step = StepTicks(settings);
            for (int bar = 0; bar < settings.NumberOfBars; bar++)
            {
                long start = bar * BarTicks(settings);
                for (int index = 0; index < 16; index++)
                {
                    if (index % 4 == 0 || index == 7 || index == 14) AddDrum(tracks[0], 36, start + index * step, step, 120);
                    if (index % 8 == 4) AddDrum(tracks[0], 38, start + index * step, step, 127);
                    AddNote(tracks[Math.Min(1, tracks.Count - 1)], ScaleNote(settings, (index + bar) % 7, -1), start + index * step, step, 100);
                    if (Chance(random, 0.35)) AddNote(tracks[^1], ScaleNote(settings, random.Next(7), 1), start + index * step, step / 2, 110);
                }
                CheckCancellation(cancellationToken, bar);
                progress?.Report((bar + 1) / (double) settings.NumberOfBars);
            }
            FinishTracks(tracks, settings.NumberOfBars * BarTicks(settings));
        }
    }



    public class MidiGenerationSettings
    {
        public double Tempo { get; set; } = 120.0;
        public double Intensity { get; set; } = 100.0;
        public int TicksPerQuarterNote { get; set; } = 960;
        public int TimeSignatureNumerator { get; set; } = 4;
        public int TimeSignatureDenominator { get; set; } = 4;
        public int KeySignature { get; set; } = 0; // C Major
        public int NumberOfBars { get; set; } = 16;
        public int NumberOfTracks { get; set; } = 1;
        public int Instrument { get; set; } = 0; // Acoustic Grand Piano
        public MidiInstrument MidiInstrument { get; set; } = MidiInstrument.Sine;
        public AudioObj? CustomSample { get; set; }
        public double PitchFrequency { get; set; } = 440.0;
        public int? Seed { get; set; }
        public string FilePath { get; set; } = string.Empty;

        public MidiGenerationPreset Preset { get; set; } = MidiGenerationPreset.Default;
    }

    public enum MidiGenerationPreset
    {
        Default,
        Jazz,
        Rock,
        Classical,
        Electronic,
        Breakbeat,
        Glitch,
        Chiptune,
        Endboss
    }
}
