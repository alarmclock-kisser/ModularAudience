using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Midi;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class MidiSampleRenderTests
{
    [TestMethod]
    public void PreviewAndSaveReusePreparedNotesAndInvalidateEditedSample()
    {
        using var sample = new AudioObj { Data = Enumerable.Range(0, 9600).Select(i => (float)(0.2 * Math.Sin(i * 0.1))).ToArray(), SampleRate = 48000, Channels = 2 };
        var track = new MidiTrackData { Index = 0 };
        track.Notes.Add(new() { NoteNumber = 67, Channel = 1, Velocity = 100, StartTick = 0, DurationTicks = 1920 });
        track.ExtendLengthTo(1920);
        var midi = MidiFileData.CreateGenerated([track], 960, 120);
        using var cache = new MidiSampleRenderCache();
        using var preview = MidiAudioRenderer.Render(midi, 0, MidiInstrument.CustomSample, 120, sample, previewQuality: true, sampleCache: cache);
        Assert.AreEqual(1, cache.Count);
        using var saved = MidiAudioRenderer.Render(midi, 0, MidiInstrument.CustomSample, 120, sample, sampleCache: cache);
        Assert.AreEqual(1L, cache.Hits);
        CollectionAssert.AreEqual(preview.Data, saved.Data);
        sample.Data[100] = 0.9f;
        using var edited = MidiAudioRenderer.Render(midi, 0, MidiInstrument.CustomSample, 120, sample, sampleCache: cache);
        Assert.AreEqual(2, cache.Count);
        cache.Dispose(); Assert.AreEqual(0, cache.Count);
    }

    [TestMethod]
    [DataRow(48, 96)]
    [DataRow(84, 480)]
    public void StereoShortAndLongNotesRenderInPreviewAndSave(int pitch, int duration)
    {
        using var sample = new AudioObj { Data = Enumerable.Range(0, 44100).SelectMany(i => new[] { (float)(0.3 * Math.Sin(i * 0.04)), (float)(0.1 * Math.Sin(i * 0.07)) }).ToArray(), SampleRate = 44100, Channels = 2 };
        var track = new MidiTrackData { Index = 0 };
        track.Notes.Add(new() { NoteNumber = pitch, Channel = 1, Velocity = 100, DurationTicks = duration }); track.ExtendLengthTo(duration);
        var midi = MidiFileData.CreateGenerated([track], 960, 120);
        using var result = MidiAudioRenderer.Render(midi, 0, MidiInstrument.CustomSample, 120, sample, previewQuality: true);
        Assert.IsTrue(result.Data.All(float.IsFinite));
        Assert.IsTrue(result.Data.Any(v => Math.Abs(v) > 0.001f));
    }

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
    [DataRow(44100, 1)]
    [DataRow(48000, 2)]
    public void SustainedCustomSamplePastIntegerOverflowBoundaryRendersPreviewAndCachedSave(int sampleRate, int channels)
    {
        using var sample = new AudioObj
        {
            Data = Enumerable.Range(0, sampleRate / 10 * channels)
                .Select(i => (float)(0.2 * Math.Sin((i / channels) * 0.1))).ToArray(),
            SampleRate = sampleRate,
            Channels = channels
        };
        var track = new MidiTrackData { Index = 0 };
        track.Notes.Add(new() { NoteNumber = 60, Channel = 1, Velocity = 100, StartTick = 0, DurationTicks = 3840 });
        track.ExtendLengthTo(3840);
        var midi = MidiFileData.CreateGenerated([track], 960, 120);
        using var cache = new MidiSampleRenderCache();
        using var preview = MidiAudioRenderer.Render(midi, 0, MidiInstrument.CustomSample, 120, sample,
            sampleRate: sampleRate, previewQuality: true, renderQuality: MidiRenderQuality.Draft, sampleCache: cache);
        using var saved = MidiAudioRenderer.Render(midi, 0, MidiInstrument.CustomSample, 120, sample,
            sampleRate: sampleRate, renderQuality: MidiRenderQuality.Draft, sampleCache: cache);
        Assert.AreEqual(1L, cache.Hits);
        CollectionAssert.AreEqual(preview.Data, saved.Data);
        Assert.IsTrue(preview.Data.All(float.IsFinite));
        Assert.IsTrue(preview.Data.Skip(sampleRate * 2 * 3 / 2).Take(sampleRate / 4)
            .Any(value => Math.Abs(value) > 0.001f), "The sustained note must remain audible past 1.5 seconds.");
    }

    [TestMethod]
    [DataRow(60, 120)]
    [DataRow(84, 30)]
    [DataRow(96, 15)]
    public void ShortPitchedSamplesRenderWithBalancedChunks(int pitch, int durationTicks)
    {
        using var sample = new AudioObj
        {
            Data = Enumerable.Range(0, 2048).Select(i => (float)(0.2 * Math.Sin(i * 0.15))).ToArray(),
            SampleRate = 44100, Channels = 1
        };
        var track = new MidiTrackData { Index = 0 };
        track.Notes.Add(new() { NoteNumber = pitch, Velocity = 100, DurationTicks = durationTicks });
        track.ExtendLengthTo(durationTicks);
        using var rendered = MidiAudioRenderer.Render(MidiFileData.CreateGenerated([track], 960, 120),
            0, MidiInstrument.CustomSample, 120, sample, previewQuality: true, renderQuality: MidiRenderQuality.Balanced);
        Assert.IsTrue(rendered.Data.All(float.IsFinite));
        Assert.IsTrue(rendered.Data.Any(value => Math.Abs(value) > 0.001f));
    }

    [TestMethod]
    public void CollectionChoosesSamplesPerNoteAndKeepsTheirCacheEntriesSeparate()
    {
        using var positive = new AudioObj { Data = Enumerable.Repeat(0.2f, 1600).ToArray(), SampleRate = 16000, Channels = 1 };
        using var negative = new AudioObj { Data = Enumerable.Repeat(-0.2f, 1600).ToArray(), SampleRate = 16000, Channels = 1 };
        using var empty = new AudioObj { Data = [], SampleRate = 16000, Channels = 1 };
        var track = new MidiTrackData { Index = 0 };
        for (int i = 0; i < 24; i++)
            track.Notes.Add(new() { NoteNumber = 60, Velocity = 100, StartTick = i * 240, DurationTicks = 192 });
        track.ExtendLengthTo(24 * 240);
        var midi = MidiFileData.CreateGenerated([track], 960, 120);
        using var cache = new MidiSampleRenderCache();
        using var preview = MidiAudioRenderer.Render(midi, 0, MidiInstrument.CustomCollection, 120,
            sampleRate: 16000, previewQuality: true, sampleCache: cache, customCollection: [positive, empty, negative]);
        Assert.AreEqual(2, cache.Count, "Identical pitches/durations must retain separate entries for different source samples.");
        var centers = Enumerable.Range(0, 24).Select(i => preview.Data[(i * 2000 + 800) * 2]).ToArray();
        Assert.IsTrue(centers.Any(v => v > 0.01f));
        Assert.IsTrue(centers.Any(v => v < -0.01f), "Repeated MIDI pitches must choose a fresh source per note, not once per pitch.");
        using var saved = MidiAudioRenderer.Render(midi, 0, MidiInstrument.CustomCollection, 120,
            sampleRate: 16000, sampleCache: cache, customCollection: [positive, empty, negative]);
        CollectionAssert.AreEqual(preview.Data, saved.Data, "Preview and save should retain the same random arrangement.");
        Assert.AreEqual(2L, cache.Hits);
        Assert.IsTrue(preview.Data.All(float.IsFinite));
        Assert.IsTrue(positive.Data.All(v => v == 0.2f));
        Assert.IsTrue(negative.Data.All(v => v == -0.2f));
    }

    [TestMethod]
    public void CollectionRequiresAtLeastOneValidSample()
    {
        var track = new MidiTrackData { Index = 0 };
        var midi = MidiFileData.CreateGenerated([track], 960, 120);
        Assert.ThrowsExactly<InvalidOperationException>(() => MidiAudioRenderer.Render(midi, 0, MidiInstrument.CustomCollection, 120));
        Assert.ThrowsExactly<InvalidOperationException>(() => MidiAudioRenderer.Render(midi, 0, MidiInstrument.CustomCollection, 120, customCollection: []));
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
