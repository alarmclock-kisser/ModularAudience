using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Processors_V1;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class PitchShifterQualityTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OctaveShiftPreservesStereoToneAndExpectedDuration(bool phaseVocoder)
    {
        using var source = Tone(48000, 1, 440);
        using var shifted = await PitchShifter.CreatePitchShiftAsync(source, 12, phaseVocoder);
        Assert.AreEqual(phaseVocoder ? source.Data.Length : source.Data.Length / 2, shifted.Data.Length);
        double amplitude = Amplitude(shifted.Data, 48000, 880);
        Console.WriteLine($"PV={phaseVocoder}: 880 Hz amplitude {amplitude}");
        Assert.IsTrue(amplitude > 0.1 && amplitude < 0.4, $"Unexpected shifted amplitude: {amplitude}");
        Assert.IsTrue(Amplitude(shifted.Data, 48000, 440) < 0.01);
        for (int i = 0; i < shifted.Data.Length; i += 2)
        {
            Assert.IsTrue(float.IsFinite(shifted.Data[i]));
            Assert.AreEqual(-0.5f * shifted.Data[i], shifted.Data[i + 1], 0.0001f);
        }
    }

    [TestMethod]
    public async Task UpwardResamplingRejectsAboveNyquistInsteadOfAliasing()
    {
        using var source = Tone(48000, 0.5, 18000);
        using var shifted = await PitchShifter.CreatePitchShiftAsync(source, 12, false);
        double alias = Amplitude(shifted.Data, 48000, 12000);
        Assert.IsTrue(alias < 0.001, $"Aliased tone amplitude: {alias}");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ZeroShiftIsBitExact(bool phaseVocoder)
    {
        using var source = Tone(48000, 0.1, 440);
        using var shifted = await PitchShifter.CreatePitchShiftAsync(source, 0, phaseVocoder);
        CollectionAssert.AreEqual(source.Data, shifted.Data);
    }

    [TestMethod]
    public async Task FractionalResamplingPreservesDcGain()
    {
        using var source = new AudioObj { Data = Enumerable.Repeat(0.25f, 9600).ToArray(), Channels = 2, SampleRate = 48000 };
        using var shifted = await PitchShifter.CreatePitchShiftAsync(source, 3.5f, false);
        foreach (float sample in shifted.Data) Assert.AreEqual(0.25f, sample, 0.00001f);
    }

    [TestMethod]
    public async Task PhaseVocoderKeepsEnergyAtBothEndsOfClip()
    {
        using var source = Tone(48000, 0.5, 440);
        using var shifted = await PitchShifter.CreatePitchShiftAsync(source, -5, true);
        double Rms(int start, int count) => Math.Sqrt(Enumerable.Range(start, count)
            .Sum(i => (double)shifted.Data[2 * i] * shifted.Data[2 * i]) / count);
        Console.WriteLine($"Boundary RMS: {Rms(0, 1000)} / {Rms(23000, 1000)}");
        Assert.IsTrue(Rms(0, 1000) > 0.05);
        Assert.IsTrue(Rms(23000, 1000) > 0.05);
        Assert.IsTrue(shifted.Data.Max(Math.Abs) < 0.6);
    }

    [TestMethod]
    [DataRow(-3.5f)]
    [DataRow(5.5f)]
    public async Task FractionalPhaseVocoderShiftMaintainsRequestedFrequency(float semitones)
    {
        using var source = Tone(48000, 1, 440);
        using var shifted = await PitchShifter.CreatePitchShiftAsync(source, semitones, true);
        double target = 440 * Math.Pow(2, semitones / 12.0);
        double amplitude = Amplitude(shifted.Data, 48000, target);
        Assert.IsTrue(amplitude > 0.15, $"Expected {target:F2} Hz tone, amplitude was {amplitude:F4}.");
        Assert.AreEqual(source.Data.Length, shifted.Data.Length);
    }

    private static AudioObj Tone(int rate, double seconds, double frequency)
    {
        int frames = (int)(rate * seconds);
        float[] data = new float[2 * frames];
        for (int i = 0; i < frames; i++)
        {
            data[2 * i] = (float)(0.25 * Math.Sin(2 * Math.PI * frequency * i / rate));
            data[2 * i + 1] = -0.5f * data[2 * i];
        }
        return new AudioObj { Data = data, Channels = 2, SampleRate = rate };
    }

    private static double Amplitude(float[] data, int sampleRate, double frequency)
    {
        int frames = data.Length / 2, start = frames / 4, end = frames * 3 / 4;
        double real = 0, imaginary = 0;
        for (int i = start; i < end; i++)
        {
            double phase = 2 * Math.PI * frequency * i / sampleRate;
            real += data[2 * i] * Math.Cos(phase);
            imaginary += data[2 * i] * Math.Sin(phase);
        }
        return 2 * Math.Sqrt(real * real + imaginary * imaginary) / (end - start);
    }
}
