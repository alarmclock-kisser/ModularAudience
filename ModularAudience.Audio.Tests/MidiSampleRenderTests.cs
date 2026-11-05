using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Midi;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class MidiSampleRenderTests
{
    [TestMethod]
    [DataRow(MidiRenderQuality.Balanced)]
    [DataRow(MidiRenderQuality.Draft)]
    public void LongSampleNotePreservesPitchAndHasBoundedLevel(MidiRenderQuality quality)
    {
        using var sample = new AudioObj { Data = new float[4800], SampleRate = 48000, Channels = 1 };
        double root = 440 * Math.Pow(2, (60 - 69) / 12.0);
        for (int i = 0; i < sample.Data.Length; i++) sample.Data[i] = (float)(0.25 * Math.Sin(2 * Math.PI * root * i / sample.SampleRate));
        var original = (float[])sample.Data.Clone();
        var track = new MidiTrackData { Index = 0, Name = "Sustained" };
        track.Notes.Add(new() { NoteNumber = 60, Channel = 1, Velocity = 100, StartTick = 0, DurationTicks = 1920 });
        track.ExtendLengthTo(1920);
        var midi = MidiFileData.CreateGenerated([track], 960, 120);
        using var audio = MidiAudioRenderer.Render(midi, 0, MidiInstrument.CustomSample, 120, sample,
            sampleRate: 44100, renderQuality: quality);
        Assert.IsTrue(audio.Data.All(float.IsFinite));
        Assert.IsTrue(audio.Data.Max(Math.Abs) <= 0.21f);
        int begin = audio.SampleRate / 5, end = audio.SampleRate * 4 / 5;
        double real = 0, imaginary = 0;
        for (int i = begin; i < end; i++)
        {
            double phase = 2 * Math.PI * root * i / audio.SampleRate;
            real += audio.Data[2 * i] * Math.Cos(phase); imaginary += audio.Data[2 * i] * Math.Sin(phase);
        }
        double amplitude = 2 * Math.Sqrt(real * real + imaginary * imaginary) / (end - begin);
        Assert.IsTrue(amplitude > 0.015, $"Sustained root tone disappeared: {amplitude}");
        CollectionAssert.AreEqual(original, sample.Data);
    }

    [TestMethod]
    public void CustomSampleChordAvoidsClippingAndPreservesVelocityOrdering()
    {
        using var sample = new AudioObj { Data = Enumerable.Range(0, 8000).Select(i => (float)(0.8 * Math.Sin(i * 0.1))).ToArray(), SampleRate = 16000, Channels = 1 };
        var track = new MidiTrackData { Index = 0 };
        for (int i = 0; i < 6; i++) track.Notes.Add(new() { NoteNumber = 60, Channel = 1, Velocity = 127, StartTick = 0, DurationTicks = 960 });
        track.ExtendLengthTo(960);
        using var audio = MidiAudioRenderer.Render(MidiFileData.CreateGenerated([track], 960, 120), 0,
            MidiInstrument.CustomSample, 120, sample, sampleRate: 16000);
        Assert.IsTrue(audio.Data.Max(Math.Abs) <= 0.951f);
        Assert.IsTrue(audio.Data.All(float.IsFinite));
    }
}
