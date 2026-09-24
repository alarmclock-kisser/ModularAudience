using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Midi;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class PolyphonicMidiConversionTests
{
    [TestMethod]
    [DataRow(8000)]
    [DataRow(16000)]
    [DataRow(44100)]
    [DataRow(48000)]
    public void DetectsBothTonesWithoutInventingTheirCommonSubharmonic(int sampleRate)
    {
        using AudioTestScope scope = new();
        AudioObj audio = scope.Create(AudioTestData.Track(sampleRate, 1000,
            (0, AudioTestData.Tone(sampleRate, 1000, 440, 5, 5), 0.5f),
            (0, AudioTestData.Tone(sampleRate, 1000, 660, 5, 5), 0.5f)), sampleRate);
        MidiFileData midi = Convert(audio);
        AssertPitches(midi, 69, 76);
        Assert.AreEqual(2, midi.Tracks.Count);
        Assert.IsTrue(midi.Tracks.All(track => track.Notes.Count > 0), Describe(midi));
        foreach (MidiNoteData note in Notes(midi))
        {
            Assert.IsTrue(Seconds(midi, note.DurationTicks) > 0.85, Describe(midi));
        }
        Assert.IsFalse(Notes(midi).Any(note => note.NoteNumber == 57), "A nonexistent 220 Hz fundamental must not be inferred.");
    }

    [TestMethod]
    [DataRow(48, 52, 55)]
    [DataRow(60, 64, 67)]
    public void RetainsAllThreeChordNotes(int first, int second, int third)
    {
        using AudioTestScope scope = new();
        float[] signal = AudioTestData.Track(16000, 1000,
            (100, Tone(first, 800), 0.4f), (100, Tone(second, 800), 0.4f), (100, Tone(third, 800), 0.4f));
        MidiFileData midi = Convert(scope.Create(signal));
        AssertPitches(midi, first, second, third);
        foreach (MidiNoteData note in Notes(midi))
        {
            Assert.AreEqual(0.1, Seconds(midi, note.StartTick), 0.06, Describe(midi));
            Assert.AreEqual(0.8, Seconds(midi, note.DurationTicks), 0.08, Describe(midi));
        }
    }

    [TestMethod]
    public void DoesNotTurnAHarmonicInstrumentIntoAChord()
    {
        using AudioTestScope scope = new();
        MidiFileData midi = Convert(scope.Create(HarmonicTone(57, 1000)));
        AssertPitches(midi, 57);
        Assert.AreEqual(1, Notes(midi).Length, Describe(midi));
        Assert.IsTrue(Seconds(midi, Notes(midi)[0].DurationTicks) > 0.9, Describe(midi));
        Assert.AreEqual(0, midi.Tracks[1].Notes.Count, "Do not manufacture accompaniment for an isolated voice.");
    }

    [TestMethod]
    public void AssignsSustainedMelodyAndShortChordsToDifferentRoles()
    {
        using AudioTestScope scope = new();
        List<(double StartMs, float[] Samples, float Gain)> parts =
        [
            (100, HarmonicTone(69, 650), 0.5f),
            (950, HarmonicTone(71, 650), 0.5f)
        ];
        foreach (int start in new[] { 200, 600, 1100, 1500 })
        {
            foreach (int pitch in new[] { 48, 52, 55 })
            {
                parts.Add((start, Tone(pitch, 120), 0.3f));
            }
        }
        MidiFileData midi = Convert(scope.Create(AudioTestData.Track(16000, 1800, parts.ToArray())));
        CollectionAssert.AreEqual(new[] { 69, 71 }, midi.Tracks[0].Notes.Select(note => note.NoteNumber).ToArray(), Describe(midi));
        CollectionAssert.AreEquivalent(new[] { 48, 52, 55 }, midi.Tracks[1].Notes.Select(note => note.NoteNumber).Distinct().ToArray(), Describe(midi));
        Assert.IsTrue(midi.Tracks[1].Notes.Count >= 9, "Short accompaniment attacks must remain distinct: " + Describe(midi));
        Assert.IsTrue(midi.Tracks[0].Notes.All(note => Seconds(midi, note.DurationTicks) > 0.55), Describe(midi));
    }

    [TestMethod]
    public void DoesNotSplitIndependentlyModulatedOvertonesIntoVoices()
    {
        using AudioTestScope scope = new();
        float[] signal = new float[24000];
        double phase = 0;
        double frequency = 440 * Math.Pow(2, (63 - 69) / 12.0);
        for (int i = 0; i < signal.Length; i++)
        {
            double time = i / 16000.0;
            phase += 2 * Math.PI * frequency * Math.Pow(2, 0.3 * Math.Sin(2 * Math.PI * 5 * time) / 12) / 16000;
            signal[i] = (float)(0.12 * Math.Sin(phase)
                + 0.32 * (0.65 + 0.35 * Math.Sin(2 * Math.PI * 7 * time)) * Math.Sin(2 * phase)
                + 0.18 * (0.65 + 0.35 * Math.Sin(2 * Math.PI * 11 * time)) * Math.Sin(3 * phase)
                + 0.1 * (0.65 + 0.35 * Math.Sin(2 * Math.PI * 9 * time)) * Math.Sin(4 * phase));
        }
        MidiFileData midi = Convert(scope.Create(signal));
        AssertPitches(midi, 63);
        Assert.AreEqual(1, Notes(midi).Length, Describe(midi));
    }

    [TestMethod]
    public void KeepsSustainedOctaveMelodyAcrossLowerPlucks()
    {
        using AudioTestScope scope = new();
        AudioObj audio = scope.Create(AudioTestData.Track(16000, 1400,
            (0, Tone(69, 1400), 0.6f),
            (200, Tone(57, 140), 0.5f),
            (650, Tone(57, 140), 0.5f),
            (1100, Tone(57, 140), 0.5f)));
        MidiFileData midi = Convert(audio);
        AssertPitches(midi, 57, 69);
        MidiNoteData melody = midi.Tracks[0].Notes.Single();
        Assert.AreEqual(69, melody.NoteNumber, Describe(midi));
        Assert.IsTrue(Seconds(midi, melody.DurationTicks) > 1.3, Describe(midi));
        Assert.AreEqual(3, midi.Tracks[1].Notes.Count, Describe(midi));
    }

    [TestMethod]
    public void TreatsShortHarmonicBurstsOverAStableFundamentalConservatively()
    {
        using AudioTestScope scope = new();
        AudioObj audio = scope.Create(AudioTestData.Track(16000, 1400,
            (0, Tone(57, 1400), 0.6f),
            (200, Tone(69, 140), 0.5f),
            (650, Tone(69, 140), 0.5f),
            (1100, Tone(69, 140), 0.5f)));
        MidiFileData midi = Convert(audio);
        AssertPitches(midi, 57);
        Assert.AreEqual(1, Notes(midi).Length, Describe(midi));
    }

    [TestMethod]
    [DataRow(0.1f)]
    [DataRow(0.2f)]
    public void RetainsAQuieterConcurrentVoice(float gain)
    {
        using AudioTestScope scope = new();
        AudioObj audio = scope.Create(AudioTestData.Track(16000, 1200,
            (0, Tone(67, 1200), 0.8f), (250, Tone(72, 700), gain)));
        MidiFileData midi = Convert(audio);
        AssertPitches(midi, 67, 72);
        MidiNoteData quiet = Notes(midi).Single(note => note.NoteNumber == 72);
        Assert.IsTrue(Seconds(midi, quiet.DurationTicks) > 0.6, Describe(midi));
        Assert.IsTrue(quiet.Velocity < Notes(midi).First(note => note.NoteNumber == 67).Velocity, Describe(midi));
    }

    [TestMethod]
    public void PreservesRepeatedNotesAndActualRests()
    {
        using AudioTestScope scope = new();
        MidiFileData midi = Convert(scope.Create(AudioTestData.Track(16000, 800,
            (50, Tone(69, 220), 1f), (400, Tone(69, 220), 1f))));
        Assert.AreEqual(2, Notes(midi).Length, Describe(midi));
        AssertPitches(midi, 69);
        MidiNoteData[] notes = Notes(midi).OrderBy(note => note.StartTick).ToArray();
        Assert.IsTrue(Seconds(midi, notes[1].StartTick - notes[0].StartTick - notes[0].DurationTicks) >= 0.08, Describe(midi));
    }

    [TestMethod]
    public void KeepsBriefLevelDropWithinASustainedNote()
    {
        using AudioTestScope scope = new();
        float[] signal = Tone(69, 1000);
        for (int i = 7200; i < 7680; i++)
        {
            signal[i] *= 0.05f;
        }
        MidiFileData midi = Convert(scope.Create(signal));
        Assert.AreEqual(1, Notes(midi).Length, Describe(midi));
        AssertPitches(midi, 69);
        Assert.IsTrue(Seconds(midi, Notes(midi)[0].DurationTicks) > 0.9, Describe(midi));
    }

    [TestMethod]
    public void PreservesOppositePhaseStereoWithoutModifyingInput()
    {
        using AudioTestScope scope = new();
        float[] stereo = AudioTestData.Stereo(Tone(69, 800), true);
        float[] original = (float[])stereo.Clone();
        MidiFileData midi = Convert(scope.Create(stereo, channels: 2));
        AssertPitches(midi, 69);
        CollectionAssert.AreEqual(original, stereo);
    }

    [TestMethod]
    public void RetainsIndependentOctaveVoicesInDifferentStereoChannels()
    {
        using AudioTestScope scope = new();
        float[] left = Tone(69, 800);
        float[] right = Tone(81, 800);
        float[] stereo = new float[left.Length * 2];
        for (int i = 0; i < left.Length; i++)
        {
            stereo[2 * i] = left[i];
            stereo[2 * i + 1] = right[i];
        }
        AssertPitches(Convert(scope.Create(stereo, channels: 2)), 69, 81);
    }

    [TestMethod]
    public void SuppressesOvertonesWithOverlappingStereoPositions()
    {
        using AudioTestScope scope = new();
        float[] fundamental = Tone(57, 1000);
        float[] second = Tone(69, 1000);
        float[] third = AudioTestData.Tone(16000, 1000, 660, 5, 5);
        float[] stereo = new float[fundamental.Length * 2];
        for (int i = 0; i < fundamental.Length; i++)
        {
            stereo[2 * i] = fundamental[i] * 0.5f + second[i] * 0.2f + third[i] * 0.15f;
            stereo[2 * i + 1] = fundamental[i] * 0.3f + second[i] * 0.5f + third[i] * 0.3f;
        }
        MidiFileData midi = Convert(scope.Create(stereo, channels: 2));
        AssertPitches(midi, 57);
        Assert.AreEqual(1, Notes(midi).Length, Describe(midi));
    }

    [TestMethod]
    public void RejectsSilenceAndUnpitchedNoise()
    {
        using AudioTestScope scope = new();
        Assert.AreEqual(0, Notes(Convert(scope.Create(new float[8000]))).Length);
        Assert.AreEqual(0, Notes(Convert(scope.Create(new float[2]))).Length);
        Random random = new(42);
        float[] signal = Enumerable.Range(0, 16000).Select(_ => (float)(random.NextDouble() - 0.5)).ToArray();
        MidiFileData midi = Convert(scope.Create(signal));
        Assert.AreEqual(0, Notes(midi).Length, Describe(midi));
    }

    [TestMethod]
    public async Task ParallelConversionIsDeterministicAndReportsMonotonicProgress()
    {
        using AudioTestScope scope = new();
        AudioObj audio = scope.Create(AudioTestData.Track(16000, 1000,
            (0, Tone(69, 1000), 0.5f), (200, Tone(76, 500), 0.4f)));
        string expected = Describe(Convert(audio));
        foreach (int workers in new[] { 1, 2, 4 })
        {
            List<double> values = [];
            MidiFileData actual = await MidiFileData.ConvertAsync(audio, workers, progress: new InlineProgress(values.Add), preset: MidiConversionPreset.Polyphonic);
            Assert.AreEqual(expected, Describe(actual));
            Assert.AreEqual(0.0, values[0]);
            Assert.AreEqual(1.0, values[^1]);
            Assert.IsTrue(values.Zip(values.Skip(1)).All(pair => pair.First <= pair.Second));
        }
    }

    [TestMethod]
    public async Task HonorsCancellationBeforeAndDuringAnalysis()
    {
        using AudioTestScope scope = new();
        AudioObj audio = scope.Create(Tone(69, 2000));
        using CancellationTokenSource before = new();
        before.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => MidiFileData.ConvertAsync(audio,
            cancellationToken: before.Token, preset: MidiConversionPreset.Polyphonic));
        using CancellationTokenSource during = new();
        InlineProgress progress = new(value => { if (value >= 0.1) during.Cancel(); });
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => MidiFileData.ConvertAsync(audio, 2,
            during.Token, progress, MidiConversionPreset.Polyphonic));
    }

    [TestMethod]
    public async Task ExportsBothTracksWithChannelsTempoAndOriginalTiming()
    {
        using AudioTestScope scope = new();
        AudioObj audio = scope.Create(AudioTestData.Track(16000, 1200,
            (100, Tone(69, 800), 0.5f), (100, Tone(76, 800), 0.5f)));
        audio.Bpm = 95;
        MidiFileData source = Convert(audio);
        string path = Path.Combine(Path.GetTempPath(), $"polyphonic-{Guid.NewGuid():N}.mid");
        try
        {
            Assert.AreEqual(path, await source.ExportAsync(path));
            MidiFileData loaded = MidiFileData.Load(path);
            Assert.AreEqual(2, loaded.Tracks.Count);
            Assert.AreEqual(95.0, loaded.DefaultBpm, 0.001);
            Assert.AreEqual(source.LengthTicks, loaded.LengthTicks);
            AssertPitches(loaded, 69, 76);
            CollectionAssert.AreEquivalent(new[] { 1, 2 }, Notes(loaded).Select(note => note.Channel).Distinct().ToArray());
            Assert.IsTrue(Notes(loaded).All(note => Seconds(loaded, note.StartTick + note.DurationTicks) <= 1.2));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static MidiFileData Convert(AudioObj audio) => MidiFileData.Convert(audio, MidiConversionPreset.Polyphonic);
    private static float[] Tone(int note, int durationMs) => AudioTestData.Tone(16000, durationMs, 440 * Math.Pow(2, (note - 69) / 12.0), 5, 5);
    private static MidiNoteData[] Notes(MidiFileData midi) => midi.Tracks.SelectMany(track => track.Notes).ToArray();
    private static double Seconds(MidiFileData midi, long ticks) => MidiConversionTests.Seconds(midi, ticks);
    private static string Describe(MidiFileData midi) => MidiConversionTests.Describe(midi);

    private static float[] HarmonicTone(int note, int durationMs)
    {
        float[] signal = Tone(note, durationMs);
        double frequency = 440 * Math.Pow(2, (note - 69) / 12.0);
        for (int i = 0; i < signal.Length; i++)
        {
            double phase = 2 * Math.PI * frequency * i / 16000;
            double envelope = Math.Min(1, i / 80.0) * Math.Min(1, (signal.Length - i) / 80.0);
            signal[i] = (float)(envelope * (0.25 * Math.Sin(phase) + 0.45 * Math.Sin(2 * phase) + 0.18 * Math.Sin(3 * phase) + 0.12 * Math.Sin(4 * phase)));
        }
        return signal;
    }

    private static void AssertPitches(MidiFileData midi, params int[] expected)
    {
        CollectionAssert.AreEquivalent(expected, Notes(midi).Select(note => note.NoteNumber).Distinct().ToArray(), Describe(midi));
    }

    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
