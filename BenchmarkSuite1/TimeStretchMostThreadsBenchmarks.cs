using BenchmarkDotNet.Attributes;
using ModularAudience.Audio.Processors_V1;
using System;
using System.Threading.Tasks;
using Microsoft.VSDiagnostics;

namespace ModularAudience.Audio;
[CPUUsageDiagnoser]
public class TimeStretchMostThreadsBenchmarks
{
    private AudioObj template = null!;
    private readonly IProgress<double> progress = new Progress<double>(_ =>
    {
    });
    private int selectedWorkers;

    [GlobalSetup]
    public void Setup()
    {
        int sampleRate = 48000;
        int channels = 2;
        int seconds = 30;
        int length = sampleRate * channels * seconds;
        var data = new float[length];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (float)System.Math.Sin(i * 0.0007);
        }

        this.template = new AudioObj
        {
            Name = "BenchmarkAudio",
            SampleRate = sampleRate,
            Channels = channels,
            Data = data,
            Length = data.LongLength,
            Bpm = 128f
        };

        this.selectedWorkers = Math.Max(1, Environment.ProcessorCount - 1);
    }

    [Benchmark]
    public async Task MostThreads_WithProgress()
    {
        var audio = this.template.Clone();
        await TimeStretcher.TimeStretchAllThreadsAsync(audio, chunkSize: 8192, overlap: 0.5f, factor: 1.05, keepData: false, normalize: 0.0f, maxWorkers: this.selectedWorkers, progress: this.progress, offload: false);
    }

    [Benchmark]
    public async Task MostThreads_NoProgress()
    {
        var audio = this.template.Clone();
        await TimeStretcher.TimeStretchAllThreadsAsync(audio, chunkSize: 8192, overlap: 0.5f, factor: 1.05, keepData: false, normalize: 0.0f, maxWorkers: this.selectedWorkers, progress: null, offload: false);
    }

    [Benchmark]
    public async Task Channeled_NoProgress()
    {
        var audio = this.template.Clone();
        await TimeStretcher.TimeStretchAllThreadsAsync(audio, chunkSize: 8192, overlap: 0.5f, factor: 1.05, keepData: false, normalize: 0.0f, maxWorkers: this.selectedWorkers, progress: null, offload: false, channeled: true);
    }
}