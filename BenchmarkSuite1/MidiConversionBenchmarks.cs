using BenchmarkDotNet.Attributes;
using ModularAudience.Audio;
using ModularAudience.Audio.Midi;
using Microsoft.VSDiagnostics;
using System;
using System.Threading.Tasks;

namespace BenchmarkSuite1;

[CPUUsageDiagnoser]
public class MidiConversionBenchmarks
{
    private AudioObj audio = null!;
    [GlobalSetup]
    public void Setup()
    {
        const int sampleRate = 48000;
        const int seconds = 8;
        this.audio = new AudioObj
        {
            Name = "Benchmark audio",
            SampleRate = sampleRate,
            Channels = 1,
            Bpm = 120,
            Data = new float[sampleRate * seconds]
        };
        for (int index = 0; index < this.audio.Data.Length; index++)
        {
            double time = index / (double) sampleRate;
            this.audio.Data[index] = (float) (0.6 * Math.Sin(2 * Math.PI * 220 * time) + 0.2 * Math.Sin(2 * Math.PI * 440 * time));
        }
    }

    [Benchmark(Baseline = true)]
    public MidiFileData Convert()
    {
        return MidiFileData.Convert(this.audio);
    }

    [Benchmark]
    public Task<MidiFileData> ConvertAsync()
    {
        return MidiFileData.ConvertAsync(this.audio, Environment.ProcessorCount);
    }
}