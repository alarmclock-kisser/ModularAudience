using BenchmarkDotNet.Attributes;
using ModularAudience.Audio;
using ModularAudience.Audio.Midi;
using System;
using System.Linq;
using Microsoft.VSDiagnostics;

namespace BenchmarkSuite1;

[CPUUsageDiagnoser]
public class MidiCustomSampleRenderBenchmarks
{
    private MidiFileData midi = null!;
    private AudioObj sample = null!;
    [GlobalSetup]
    public void Setup()
    {
        this.sample = new AudioObj
        {
            Name = "PreviewSample",
            SampleRate = 44100,
            Channels = 2,
            BitDepth = 32,
            Data = Enumerable.Range(0, 44100 * 2).Select(index => (float) Math.Sin(index * 0.01)).ToArray(),
            Length = 44100L * 2,
            Duration = TimeSpan.FromSeconds(1),
            Volume = 100f
        };
        MidiTrackData track = new()
        {
            Index = 0,
            Name = "Preview"
        };
        for (int index = 0; index < 8; index++)
        {
            track.Notes.Add(new MidiNoteData { NoteNumber = 60 + index, Channel = 0, Velocity = 100, StartTick = index * 480, DurationTicks = 420 });
        }

        this.midi = MidiFileData.CreateGenerated([track], 480, 120.0);
    }

    [Benchmark]
    public AudioObj RenderCustomSample()
    {
        return MidiAudioRenderer.Render(this.midi, 0, MidiInstrument.CustomSample, 120.0, this.sample, 44100);
    }

    [Benchmark]
    public AudioObj RenderCustomSamplePreview()
    {
        return MidiAudioRenderer.Render(this.midi, 0, MidiInstrument.CustomSample, 120.0, this.sample, 44100, previewQuality: true);
    }
}