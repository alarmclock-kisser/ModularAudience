using BenchmarkDotNet.Attributes;
using Microsoft.VSDiagnostics;
using ModularAudience.Audio;
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace BenchmarkSuite1;
[WarmupCount(3)]
[IterationCount(50)]
[InvocationCount(1)]
[CPUUsageDiagnoser]
[MemoryDiagnoser]
public class LivePlaybackRateBenchmarks
{
    private static readonly FieldInfo PipelineField = typeof(AudioPlaybackService).GetField("pipeline", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private AudioPlaybackService playback = null!;
    private object previousPipeline = null!;
    private float nextRate;
    private int requests;
    private int replacements;
    [GlobalSetup]
    public void Setup()
    {
        const int sampleRate = 48000;
        float[] samples = new float[sampleRate * 4];
        for (int frame = 0; frame < samples.Length / 2; frame++)
        {
            float sample = (float)(0.5 * Math.Sin(2 * Math.PI * 440 * frame / sampleRate));
            samples[frame * 2] = sample;
            samples[frame * 2 + 1] = sample;
        }

        this.playback = new AudioPlaybackService();
        this.playback.SetLoop(0, samples.Length);
        this.playback.InitializePlayback(samples, sampleRate, 2, desiredLatency: 20, initialVolume: 0f).GetAwaiter().GetResult();
        Thread.Sleep(100);
        if (this.playback.GetPositionBytes() <= 0)
        {
            throw new InvalidOperationException("The live-rate benchmark requires an active audio output device.");
        }
    }

    [IterationSetup]
    public void PrepareRateChange()
    {
        int step = this.requests++ % 100;
        int position = step < 50 ? -500 + step * 20 : 500 - (step - 50) * 20;
        this.nextRate = (float)Math.Pow(2.0, position / 500.0);
        this.previousPipeline = PipelineField.GetValue(this.playback)!;
    }

    [Benchmark]
    public Task ChangeRate()
    {
        return this.playback.AdjustSampleRate(this.nextRate);
    }

    [IterationCleanup]
    public void ObserveRateChange()
    {
        if (!ReferenceEquals(this.previousPipeline, PipelineField.GetValue(this.playback)))
        {
            this.replacements++;
        }

        Thread.Sleep(40);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        this.playback.Dispose();
        Console.WriteLine($"Live rate: requests={this.requests}; pipeline replacements={this.replacements}; audio buffer=20 ms x 2. The benchmark measures individual updates; iteration spacing is not UI-event timing.");
    }
}