using BenchmarkDotNet.Attributes;
using Microsoft.VSDiagnostics;
using System;
using System.Reflection;

namespace BenchmarkSuite1;
[CPUUsageDiagnoser]
public class AudioPlaybackReadBenchmarks
{
    private float[] source = null!;
    private float[] destination = null!;
    private object provider = null!;
    private ConstructorInfo constructor = null!;
    private MethodInfo readMethod = null!;
    [GlobalSetup]
    public void Setup()
    {
        this.source = new float[96000];
        this.destination = new float[2048];
        for (int i = 0; i < this.source.Length; i++)
        {
            this.source[i] = (float)System.Math.Sin(i * 0.01);
        }

        var assembly = typeof(ModularAudience.Audio.AudioPlaybackService).Assembly;
        var providerType = assembly.GetType("ModularAudience.Audio.ArraySampleProvider", throwOnError: true)!;
        this.constructor = providerType.GetConstructor(new[] { typeof(float[]), typeof(int), typeof(int), typeof(long) })!;
        this.readMethod = providerType.GetMethod("Read", new[] { typeof(float[]), typeof(int), typeof(int) })!;
    }

    [IterationSetup]
    public void IterationSetup()
    {
        this.provider = this.constructor.Invoke(new object[] { this.source, 48000, 2, 0L });
    }

    [Benchmark]
    public int Read()
    {
        return (int) this.readMethod.Invoke(this.provider, new object[] { this.destination, 0, this.destination.Length })!;
    }
}