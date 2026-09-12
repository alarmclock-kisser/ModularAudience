using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Midi;

namespace ModularAudience.Audio.Tests
{
    [TestClass]
    public sealed class MidiConversionTests
    {
        public TestContext TestContext { get; set; } = null!;

        [TestMethod]
        [DataRow(16000, 45)]
        [DataRow(16000, 57)]
        [DataRow(44100, 69)]
        [DataRow(48000, 81)]
        [DataRow(48000, 93)]
        [DataRow(96000, 33)]
        public void RecognizesSustainedPitchWithoutSemitoneBias(int sampleRate, int noteNumber)
        {
            using AudioTestScope scope = new();
            double frequency = 440 * Math.Pow(2, (noteNumber - 69) / 12.0);
            AudioObj audio = scope.Create(AudioTestData.Tone(sampleRate, 650, frequency, 5, 5), sampleRate);
            MidiFileData midi = MidiFileData.Convert(audio);
            Assert.AreEqual(1, midi.Tracks[0].Notes.Count, Describe(midi));
            MidiNoteData note = midi.Tracks[0].Notes[0];
            Assert.AreEqual(noteNumber, note.NoteNumber, Describe(midi));
            Assert.IsTrue(Seconds(midi, note.DurationTicks) > 0.50, Describe(midi));
            Assert.IsTrue(Seconds(midi, note.StartTick) < 0.05, Describe(midi));
            Assert.IsTrue(Seconds(midi, note.StartTick + note.DurationTicks) <= 0.651, Describe(midi));
        }

        [TestMethod]
        [DataRow(0f, 0f, 120.0)]
        [DataRow(0f, 95f, 95.0)]
        [DataRow(123f, 95f, 123.0)]
        [DataRow(float.NaN, 95f, 95.0)]
        public async Task UsesValidTempoOrNeutralFallback(float taggedBpm, float scannedBpm, double expected)
        {
            using AudioTestScope scope = new();
            AudioObj audio = scope.Create(AudioTestData.Tone(16000, 350, 440, 5, 5));
            audio.Bpm = taggedBpm;
            audio.ScannedBpm = scannedBpm;
            Assert.AreEqual(expected, MidiFileData.Convert(audio).DefaultBpm);
            Assert.AreEqual(expected, (await MidiFileData.ConvertAsync(audio, 2)).DefaultBpm);
        }

        [TestMethod]
        public void KeepsHarmonicToneAndBriefLevelDropTogether()
        {
            using AudioTestScope scope = new();
            float[] signal = new float[16000];
            for (int i = 0; i < signal.Length; i++)
            {
                double phase = 2 * Math.PI * 220 * i / 16000;
                double gain = i >= 7200 && i < 7680 ? 0.03 : 1;
                signal[i] = (float) (gain * (0.25 * Math.Sin(phase) + 0.5 * Math.Sin(2 * phase) + 0.15 * Math.Sin(3 * phase)));
            }
            MidiFileData midi = MidiFileData.Convert(scope.Create(signal));
            Assert.AreEqual(1, midi.Tracks[0].Notes.Count, Describe(midi));
            Assert.AreEqual(57, midi.Tracks[0].Notes[0].NoteNumber, Describe(midi));
            Assert.IsTrue(Seconds(midi, midi.Tracks[0].Notes[0].DurationTicks) > 0.90, Describe(midi));
        }

        [TestMethod]
        public void PreservesMelodyJumpsAndRepeatedNotesAcrossRests()
        {
            using AudioTestScope scope = new();
            float[] signal = AudioTestData.Track(16000, 1500,
                (100, AudioTestData.Tone(16000, 250, 220, 5, 5), 1f),
                (450, AudioTestData.Tone(16000, 250, 440, 5, 5), 1f),
                (800, AudioTestData.Tone(16000, 250, 440, 5, 5), 0.12f),
                (1150, AudioTestData.Tone(16000, 250, 293.6648, 5, 5), 1f));
            MidiFileData midi = MidiFileData.Convert(scope.Create(signal));
            CollectionAssert.AreEqual(new[] { 57, 69, 69, 62 }, midi.Tracks[0].Notes.Select(note => note.NoteNumber).ToArray(), Describe(midi));
            double[] starts = [0.1, 0.45, 0.8, 1.15];
            for (int i = 0; i < starts.Length; i++)
            {
                Assert.AreEqual(starts[i], Seconds(midi, midi.Tracks[0].Notes[i].StartTick), 0.05, Describe(midi));
                Assert.AreEqual(0.25, Seconds(midi, midi.Tracks[0].Notes[i].DurationTicks), 0.07, Describe(midi));
            }
            Assert.IsTrue(midi.Tracks[0].Notes[2].Velocity < midi.Tracks[0].Notes[1].Velocity, "Quieter notes must not all saturate to velocity 127.");
        }

        [TestMethod]
        public void DoesNotCancelOppositePhaseStereo()
        {
            using AudioTestScope scope = new();
            float[] mono = AudioTestData.Tone(16000, 600, 440, 5, 5);
            MidiFileData midi = MidiFileData.Convert(scope.Create(AudioTestData.Stereo(mono, true), channels: 2));
            Assert.AreEqual(1, midi.Tracks[0].Notes.Count, Describe(midi));
            Assert.AreEqual(69, midi.Tracks[0].Notes[0].NoteNumber, Describe(midi));
        }

        [TestMethod]
        public void RejectsSilenceAndUnpitchedNoise()
        {
            using AudioTestScope scope = new();
            Assert.AreEqual(0, MidiFileData.Convert(scope.Create(new float[8000])).Tracks[0].Notes.Count);
            Random random = new(42);
            float[] noise = Enumerable.Range(0, 8000).Select(_ => (float) (random.NextDouble() - 0.5)).ToArray();
            MidiFileData midi = MidiFileData.Convert(scope.Create(noise));
            Assert.AreEqual(0, midi.Tracks[0].Notes.Count, Describe(midi));
        }

        [TestMethod]
        public async Task ParallelAndSynchronousConversionAgree()
        {
            using AudioTestScope scope = new();
            AudioObj audio = scope.Create(AudioTestData.Track(16000, 1100,
                (0, AudioTestData.Tone(16000, 400, 220, 5, 5), 1f),
                (500, AudioTestData.Tone(16000, 400, 440, 5, 5), 1f)));
            MidiFileData expected = MidiFileData.Convert(audio);
            foreach (int workers in new[] { 1, 2, 4 })
            {
                MidiFileData actual = await MidiFileData.ConvertAsync(audio, workers);
                Assert.AreEqual(Describe(expected), Describe(actual), $"Worker count: {workers}");
            }
        }

        [TestMethod]
        public async Task HonorsCancellation()
        {
            using AudioTestScope scope = new();
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => MidiFileData.ConvertAsync(
                scope.Create(AudioTestData.Tone(16000, 500, 440, 5, 5)), cancellationToken: cancellation.Token));
        }

        [TestMethod]
        public async Task ExportPreservesTempoChannelsAndTrailingRest()
        {
            using AudioTestScope scope = new();
            AudioObj audio = scope.Create(AudioTestData.Track(16000, 1000, (100, AudioTestData.Tone(16000, 400, 440, 5, 5), 1f)));
            audio.Bpm = 95;
            MidiFileData source = MidiFileData.Convert(audio);
            string path = Path.Combine(Path.GetTempPath(), $"midi-conversion-{Guid.NewGuid():N}.mid");
            try
            {
                Assert.AreEqual(path, await source.ExportAsync(path), "Export must succeed with valid MIDI channels.");
                MidiFileData loaded = MidiFileData.Load(path);
                Assert.AreEqual(source.DefaultBpm, loaded.DefaultBpm, 0.001);
                Assert.AreEqual(source.LengthTicks, loaded.LengthTicks);
                CollectionAssert.AreEqual(source.Tracks[0].Notes.Select(note => note.NoteNumber).ToArray(), loaded.Tracks[0].Notes.Select(note => note.NoteNumber).ToArray());
                Assert.IsTrue(loaded.Tracks[0].Notes.All(note => note.Channel is >= 1 and <= 16));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void KeepsModerateVibratoAsOneSustainedNote()
        {
            using AudioTestScope scope = new();
            float[] signal = new float[16000];
            double phase = 0;
            for (int i = 0; i < signal.Length; i++)
            {
                double pitchOffset = 0.35 * Math.Sin(2 * Math.PI * 5 * i / 16000);
                phase += 2 * Math.PI * 440 * Math.Pow(2, pitchOffset / 12) / 16000;
                signal[i] = (float) (0.5 * Math.Sin(phase));
            }
            MidiFileData midi = MidiFileData.Convert(scope.Create(signal));
            Assert.AreEqual(1, midi.Tracks[0].Notes.Count, Describe(midi));
            Assert.AreEqual(69, midi.Tracks[0].Notes[0].NoteNumber, Describe(midi));
            Assert.IsTrue(Seconds(midi, midi.Tracks[0].Notes[0].DurationTicks) > 0.9, Describe(midi));
        }

        [TestMethod]
        [DataRow(20f)]
        [DataRow(95f)]
        [DataRow(240f)]
        public void TempoDoesNotChangeDetectedPitchOrElapsedTime(float bpm)
        {
            using AudioTestScope scope = new();
            AudioObj audio = scope.Create(AudioTestData.Track(16000, 1000,
                (200, AudioTestData.Tone(16000, 500, 440, 5, 5), 1f)));
            audio.Bpm = bpm;
            MidiFileData midi = MidiFileData.Convert(audio);
            Assert.AreEqual(1, midi.Tracks[0].Notes.Count, Describe(midi));
            MidiNoteData note = midi.Tracks[0].Notes[0];
            Assert.AreEqual(69, note.NoteNumber, Describe(midi));
            Assert.AreEqual(0.2, Seconds(midi, note.StartTick), 0.05, Describe(midi));
            Assert.AreEqual(0.5, Seconds(midi, note.DurationTicks), 0.07, Describe(midi));
            Assert.AreEqual(1.0, Seconds(midi, midi.LengthTicks), 0.004, Describe(midi));
        }

        [TestMethod]
        [TestCategory("Diagnostic")]
        public void DocumentsSinglePitchOutputForOverlappingTones()
        {
            using AudioTestScope scope = new();
            float[] signal = AudioTestData.Track(16000, 800,
                (0, AudioTestData.Tone(16000, 800, 440, 5, 5), 0.5f),
                (0, AudioTestData.Tone(16000, 800, 660, 5, 5), 0.5f));
            MidiFileData midi = MidiFileData.Convert(scope.Create(signal));
            this.TestContext.WriteLine("Two simultaneous input pitches: 440 Hz (MIDI 69), 660 Hz (approximately MIDI 76).");
            this.TestContext.WriteLine("Observed monophonic output (not a transcription-accuracy assertion): " + Describe(midi));
            Assert.AreEqual(1, midi.Tracks.Count, "The current converter documents single-track, monophonic output.");
            MidiNoteData[] notes = midi.Tracks[0].Notes.OrderBy(note => note.StartTick).ToArray();
            Assert.IsTrue(notes.Length > 0, Describe(midi));
            for (int i = 1; i < notes.Length; i++)
            {
                Assert.IsTrue(notes[i - 1].StartTick + notes[i - 1].DurationTicks <= notes[i].StartTick,
                    "The current converter cannot represent simultaneous pitches.");
            }
        }

        internal static double Seconds(MidiFileData midi, long ticks) => ticks * 60.0 / midi.DefaultBpm / midi.TicksPerQuarterNote;

        internal static string Describe(MidiFileData midi) => $"BPM={midi.DefaultBpm}, length={midi.LengthTicks}: " + string.Join("; ", midi.Tracks.SelectMany(track => track.Notes)
            .Select(note => $"{note.NoteNumber}@{note.StartTick}+{note.DurationTicks} v{note.Velocity}"));
    }

    [TestClass]
    public sealed class MidiConversionFileTests
    {
        public TestContext TestContext { get; set; } = null!;

        [TestMethod]
        [TestCategory("Manual")]
        public Task EnhancedStemDiagnostic() => this.AnalyzeEnhancedStemAsync(0, null);

        [TestMethod]
        [TestCategory("Manual")]
        public Task EnhancedStemExcerptDiagnostic() => this.AnalyzeEnhancedStemAsync(10, 6);

        [TestMethod]
        [TestCategory("Manual")]
        public Task EnhancedStemPolyphonicDiagnostic() => this.AnalyzeEnhancedStemAsync(0, null, MidiConversionPreset.Polyphonic);

        [TestMethod]
        [TestCategory("Manual")]
        public Task EnhancedStemPolyphonicExcerptDiagnostic() => this.AnalyzeEnhancedStemAsync(10, 6, MidiConversionPreset.Polyphonic);

        private async Task AnalyzeEnhancedStemAsync(double startSeconds, double? durationSeconds, MidiConversionPreset preset = MidiConversionPreset.Synth)
        {
            DirectoryInfo? root = new(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "ModularAudience.slnx")))
            {
                root = root.Parent;
            }
            string? path = root == null ? null : Path.Combine(root.FullName, "ModularAudience.Audio", "Resources", "enhanced.mp3");
            if (path == null || !File.Exists(path))
            {
                Assert.Inconclusive("The optional enhanced.mp3 diagnostic input is unavailable.");
                return;
            }
            using AudioObj audio = new() { FilePath = path };
            Assert.IsTrue(audio.LoadAudioFile(), "The diagnostic MP3 must decode successfully.");
            if (durationSeconds.HasValue)
            {
                int start = (int) (startSeconds * audio.SampleRate) * audio.Channels;
                int count = Math.Min((int) (durationSeconds.Value * audio.SampleRate) * audio.Channels, audio.Data.Length - start);
                Assert.IsTrue(count > 0, "The diagnostic excerpt must contain audio.");
                audio.Data = audio.Data.AsSpan(start, count).ToArray();
                audio.Length = audio.Data.Length;
                audio.Duration = TimeSpan.FromSeconds(count / (double) (audio.SampleRate * audio.Channels));
            }
            MidiFileData midi = await MidiFileData.ConvertAsync(audio, maxWorkers: 4, preset: preset);
            MidiNoteData[] notes = midi.Tracks.SelectMany(track => track.Notes).ToArray();
            Assert.IsTrue(notes.Length > 0, "The stem must produce playable notes; this does not establish transcription accuracy.");
            if (preset == MidiConversionPreset.Polyphonic)
            {
                Assert.AreEqual(2, midi.Tracks.Count);
                Assert.IsTrue(midi.Tracks.All(track => track.Notes.Count > 0), "The mixed stem should yield notes in both estimated roles.");
            }
            string[] report =
            [
                $"Analysis start: {startSeconds:F3}s; scope: {(durationSeconds.HasValue ? "excerpt" : "full file")}",
                $"Conversion preset: {preset}; no reference transcription is available to establish musical accuracy.",
                $"Input: {audio.Duration.TotalSeconds:F3}s, {audio.SampleRate}Hz, {audio.Channels}ch, tagged BPM={audio.Bpm}, scanned BPM={audio.ScannedBpm}",
                $"Output: BPM={midi.DefaultBpm}, tracks={midi.Tracks.Count}, notes={notes.Length}, mean duration={notes.Average(note => MidiConversionTests.Seconds(midi, note.DurationTicks)):F3}s, notes under 100ms={notes.Count(note => MidiConversionTests.Seconds(midi, note.DurationTicks) < 0.1)}",
                "Pitch histogram: " + string.Join(", ", notes.GroupBy(note => note.NoteNumber).OrderBy(group => group.Key).Select(group => $"{group.Key}:{group.Count()}")),
                .. midi.Tracks.Select(track => $"{track.Name}: {track.Notes.Count} notes, summed note duration={track.Notes.Sum(note => MidiConversionTests.Seconds(midi, note.DurationTicks)):F3}s")
            ];
            foreach (string line in report)
            {
                this.TestContext.WriteLine(line);
            }
            string directory = Path.Combine(root!.FullName, "ModularAudience.Audio.Tests", "TestResults", "MidiConversion");
            Directory.CreateDirectory(directory);
            string name = durationSeconds.HasValue ? "enhanced-excerpt-diagnostic" : "enhanced-diagnostic";
            if (preset == MidiConversionPreset.Polyphonic)
            {
                name += "-polyphonic";
            }
            string reportPath = Path.Combine(directory, name + ".txt");
            File.WriteAllLines(reportPath, report);
            this.TestContext.AddResultFile(reportPath);
            string output = Path.Combine(directory, name + ".mid");
            Assert.AreEqual(output, await midi.ExportAsync(output), "The diagnostic MIDI must export successfully.");
            this.TestContext.AddResultFile(output);
        }
    }
}
