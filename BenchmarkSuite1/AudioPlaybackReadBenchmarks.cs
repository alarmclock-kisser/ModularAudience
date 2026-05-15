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
        source = new float[96000];
        destination = new float[2048];
        for (int i = 0; i < source.Length; i++)
        {
            source[i] = (float)System.Math.Sin(i * 0.01);
        }

        var assembly = typeof(ModularAudience.Audio.AudioPlaybackService).Assembly;
        var providerType = assembly.GetType("ModularAudience.Audio.ArraySampleProvider", throwOnError: true)!;
        constructor = providerType.GetConstructor(new[] { typeof(float[]), typeof(int), typeof(int), typeof(long) })!;
        readMethod = providerType.GetMethod("Read", new[] { typeof(float[]), typeof(int), typeof(int) })!;
    }

    [IterationSetup]
    public void IterationSetup()
    {
        provider = constructor.Invoke(new object[] { source, 48000, 2, 0L });
    }

    [Benchmark]
    public int Read()
    {
        return (int)readMethod.Invoke(provider, new object[] { destination, 0, destination.Length })!;
    }
}