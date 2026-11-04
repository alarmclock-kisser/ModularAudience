using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Midi;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class MainMelodyConversionTests
{
    [TestMethod]
    public async Task HarmonicLeadDoesNotJumpToItsLouderOvertones()
    {
        int[] pitches = [60, 62, 64, 65, 67, 64];
        const int rate = 16000;
        float[] data = new float[rate * 3];
        for (int i = 0; i < data.Length; i++)
        {
            double t = i / (double)rate;
            double f = 440 * Math.Pow(2, (pitches[Math.Min(5, (int)(t * 2))] - 69) / 12.0);
            double phase = 2 * Math.PI * f * t;
            data[i] = (float)(0.12 * Math.Sin(phase) + 0.28 * Math.Sin(2 * phase) + 0.18 * Math.Sin(3 * phase));
        }
        using var audio = new AudioObj { Data = data, SampleRate = rate, Channels = 1 };
        var midi = await MidiFileData.ConvertAsync(audio, preset: MidiConversionPreset.Synth);
        CollectionAssert.AreEqual(pitches, midi.Tracks[0].Notes.Select(n => n.NoteNumber).ToArray(), MidiConversionTests.Describe(midi));
    }

    [TestMethod]
    public async Task SingleMelodyMidiExportsWithOneVoiceAndCorrectTiming()
    {
        using var audio = new AudioObj { Data = AudioTestData.Track(16000, 1200,
            (100, AudioTestData.Tone(16000, 350, 440, 5, 5), 1f),
            (600, AudioTestData.Tone(16000, 350, 493.883, 5, 5), 1f)), SampleRate = 16000, Channels = 1, Bpm = 95 };
        var midi = await MidiFileData.ConvertAsync(audio, preset: MidiConversionPreset.Synth);
        string path = Path.Combine(Path.GetTempPath(), "main-melody-" + Guid.NewGuid().ToString("N") + ".mid");
        try
        {
            Assert.AreEqual(path, await midi.ExportAsync(path));
            var loaded = MidiFileData.Load(path);
            Assert.HasCount(1, loaded.Tracks);
            CollectionAssert.AreEqual(new[] { 69, 71 }, loaded.Tracks[0].Notes.Select(n => n.NoteNumber).ToArray());
            Assert.AreEqual(95, loaded.DefaultBpm, 0.001);
            Assert.AreEqual(0.1, MidiConversionTests.Seconds(loaded, loaded.Tracks[0].Notes[0].StartTick), 0.05);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    [DataRow(16000)]
    [DataRow(44100)]
    [DataRow(16000, 0.6)]
    public async Task FollowsLeadMelodyThroughBassChordsPercussionAndVibrato(int rate, double leadGain = 1)
    {
        int[] melody = [69, 71, 72, 76, 74, 72, 71, 69];
        float[] signal = new float[rate * 4];
        var random = new Random(41);
        for (int i = 0; i < signal.Length; i++)
        {
            double time = i / (double)rate;
            int step = Math.Min(7, (int)(time / 0.5));
            double local = time - step * 0.5;
            double envelope = Math.Min(1, local / 0.015) * Math.Min(1, (0.5 - local) / 0.02);
            double frequency = 440 * Math.Pow(2, (melody[step] - 69) / 12.0);
            double phase = 2 * Math.PI * frequency * time + 0.035 * Math.Sin(2 * Math.PI * 5 * time);
            double lead = envelope * (0.25 * Math.Sin(phase) + 0.10 * Math.Sin(2 * phase) + 0.06 * Math.Sin(3 * phase));
            double bass = 0.18 * Math.Sin(2 * Math.PI * 110 * time);
            double chords = new[] { 48, 52, 55 }.Sum(p => 0.045 * Math.Sin(2 * Math.PI * 440 * Math.Pow(2, (p - 69) / 12.0) * time));
            double beat = time % 0.25;
            double drums = (random.NextDouble() - 0.5) * 0.25 * Math.Exp(-beat * 80);
            signal[i] = (float)(leadGain * lead + bass + chords + drums);
        }
        using var audio = new AudioObj { Data = signal, SampleRate = rate, Channels = 1, Bpm = 120 };
        var midi = await MidiFileData.ConvertAsync(audio, preset: MidiConversionPreset.Synth);
        var notes = midi.Tracks.Single().Notes;
        Console.WriteLine(MidiConversionTests.Describe(midi));
        CollectionAssert.AreEqual(melody, notes.Select(n => n.NoteNumber).ToArray());
        for (int i = 0; i < melody.Length; i++)
        {
            Assert.AreEqual(i * 0.5, MidiConversionTests.Seconds(midi, notes[i].StartTick), 0.08);
            Assert.IsTrue(MidiConversionTests.Seconds(midi, notes[i].DurationTicks) > 0.35);
            if (i > 0) Assert.IsTrue(notes[i - 1].StartTick + notes[i - 1].DurationTicks <= notes[i].StartTick);
        }
    }
}
