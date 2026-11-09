using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Effects.AutoTune;
using ModularAudience.Audio.Effects;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class AutoTuneProcessorTests
{
    private static AutoTuneSettings Fast => new()
    {
        MinimumFrequencyHz = 100, MaximumFrequencyHz = 1000, MinimumConfidence = 0.4,
        RetuneMilliseconds = 0, ReleaseMilliseconds = 0, PitchSmoothingMilliseconds = 0,
        VibratoPreservationPercent = 0, MinimumNoteLength = "1/128", TempoBpm = 400,
        VoicingFadeMilliseconds = 0, DeadZoneCents = 0, BoundaryFadeMilliseconds = 0,
        FftSize = 1024, Overlap = 8, ProtectPeak = false
    };

    [TestMethod]
    [DataRow(100d, 440d)]
    [DataRow(50d, 445d)]
    public void DetunedTone_IsCorrectedWithoutChangingDuration(double intensity, double expected)
    {
        float[] input = Tone(450);
        float[] before = (float[])input.Clone();

        AutoTuneResult result = AutoTuneProcessor.Process(input, 8000, 1, Fast with { IntensityPercent = intensity });

        double actual = Estimate(result.Samples, 8000, 430, 460);
        Console.WriteLine($"Intensity={intensity}; peak frequency={actual:F2}; voiced={result.VoicedFrames}; corrected={result.CorrectedFrames}");
        Assert.AreEqual(expected, actual, 0.75);
        Assert.IsTrue(result.CorrectedFrames > 0);
        Assert.AreEqual(input.Length, result.Samples.Length);
        CollectionAssert.AreEqual(before, input);
        Assert.IsTrue(result.Samples.All(float.IsFinite));
    }

    [TestMethod]
    public void LinkedStereo_PreservesAntiphaseAndRelativeAmplitude()
    {
        float[] mono = Tone(450), stereo = new float[mono.Length * 2];
        for (int i = 0; i < mono.Length; i++) { stereo[2 * i] = mono[i]; stereo[2 * i + 1] = -0.5f * mono[i]; }

        AutoTuneResult result = AutoTuneProcessor.Process(stereo, 8000, 2, Fast);

        Assert.IsTrue(result.CorrectedFrames > 0, "Strongest-channel detection must not cancel antiphase stereo.");
        for (int i = 0; i < mono.Length; i++) Assert.AreEqual(-0.5f * result.Samples[2 * i], result.Samples[2 * i + 1], 0.0001f);
    }

    [TestMethod]
    public void SilenceAndDisabledCorrection_AreBitExact()
    {
        float[] input = Tone(450);
        CollectionAssert.AreEqual(input, AutoTuneProcessor.Process(input, 8000, 1, Fast with { IntensityPercent = 0 }).Samples);
        CollectionAssert.AreEqual(input, AutoTuneProcessor.Process(input, 8000, 1, Fast with { WetPercent = 0 }).Samples);
        CollectionAssert.AreEqual(input, AutoTuneProcessor.Process(input, 8000, 1, Fast with { MaximumCorrectionSemitones = 0 }).Samples);
        float[] silence = new float[1600];
        CollectionAssert.AreEqual(silence, AutoTuneProcessor.Process(silence, 8000, 1, Fast).Samples);
    }

    [TestMethod]
    public void NotesShorterThanConfiguredDuration_AreNotCorrected()
    {
        float[] input = Tone(450);

        AutoTuneResult result = AutoTuneProcessor.Process(input, 8000, 1, Fast with { MinimumNoteLength = "4", TempoBpm = 20 });

        Assert.AreEqual(0, result.CorrectedFrames);
        CollectionAssert.AreEqual(input, result.Samples);
    }

    [TestMethod]
    public void BoundaryFade_KeepsExactEdgeSamples()
    {
        float[] input = Tone(450);

        AutoTuneResult result = AutoTuneProcessor.Process(input, 8000, 1, Fast with { BoundaryFadeMilliseconds = 10 });

        Assert.AreEqual(input[0], result.Samples[0]);
        Assert.AreEqual(input[^1], result.Samples[^1]);
    }

    [TestMethod]
    public void FormantCompensationAndPeakProtection_ProduceFiniteBoundedOutput()
    {
        float[] input = Tone(450).Select(value => value * 3).ToArray();

        AutoTuneResult result = AutoTuneProcessor.Process(input, 8000, 1, Fast with
        {
            FormantPreservationPercent = 100, OutputGainDb = 12, ProtectPeak = true
        });

        Assert.IsTrue(result.CorrectedFrames > 0);
        Assert.IsTrue(result.Samples.All(float.IsFinite));
        Assert.IsTrue(result.Samples.Max(Math.Abs) <= 1.00001f);
        Assert.IsTrue(result.PeakProtectionGain < 1);
    }

    [TestMethod]
    public void CustomScaleAndCorrectionLimit_AreRespected()
    {
        Assert.AreEqual(69d, AutoTunePitchMap.Nearest(69.8, 1 << 9));
        Assert.AreEqual(71d, AutoTunePitchMap.Nearest(70.8, AutoTunePitchMap.AllowedMask(new() { Scale = AutoTuneScale.Major })));
        float[] input = Tone(450);
        AutoTuneResult result = AutoTuneProcessor.Process(input, 8000, 1, Fast with
        {
            Scale = AutoTuneScale.Custom, CustomNotesMask = 1, MaximumCorrectionSemitones = 0.1
        });
        CollectionAssert.AreEqual(input, result.Samples);
    }

    [TestMethod]
    public void NoteLengthUsesWholeNotesAtSelectedTempo()
    {
        Assert.AreEqual(0.5, (new AutoTuneSettings { MinimumNoteLength = "1/4", TempoBpm = 120 }).MinimumNoteSeconds, 1e-12);
        Assert.AreEqual(8, (new AutoTuneSettings { MinimumNoteLength = "4", TempoBpm = 120 }).MinimumNoteSeconds, 1e-12);
    }

    [TestMethod]
    public void InvalidParametersAndSamples_AreRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => (Fast with { Scale = AutoTuneScale.Custom, CustomNotesMask = 0 }).Validate());
        Assert.ThrowsExactly<ArgumentException>(() => (Fast with { MaximumFrequencyHz = 4000 }).Validate(8000));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => (Fast with { IntensityPercent = double.NaN }).Validate());
        Assert.ThrowsExactly<ArgumentException>(() => AutoTuneProcessor.Process(new float[] { float.NaN }, 8000, 1, Fast));
    }

    [TestMethod]
    public void Cancellation_IsHonoredBeforeProcessing()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(() => AutoTuneProcessor.ProcessAsync(Tone(450), 8000, 1, Fast, cancellationToken: cancellation.Token)).GetAwaiter().GetResult();
    }

    [TestMethod]
    public void ExecutionBudget_DefaultsToHalfCpuAndRejectsUnsupportedThreadCounts()
    {
        Assert.AreEqual(Math.Max(1, Environment.ProcessorCount / 2), new AudioEffectExecutionOptions().MaximumThreads);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AudioEffectExecutionOptions { MaximumThreads = 0 }.Validate());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AudioEffectExecutionOptions { MaximumThreads = Environment.ProcessorCount + 1 }.Validate());
    }

    [TestMethod]
    public void ParallelAnalysisAndStereoSynthesis_MatchSingleWorkerOutput()
    {
        float[] mono = Tone(450), stereo = new float[mono.Length * 2];
        for (int i = 0; i < mono.Length; i++) { stereo[2 * i] = mono[i]; stereo[2 * i + 1] = -0.5f * mono[i]; }
        AutoTuneResult serial = AutoTuneProcessor.Process(stereo, 8000, 2, Fast,
            execution: new AudioEffectExecutionOptions { MaximumThreads = 1 });

        AutoTuneResult parallel = AutoTuneProcessor.Process(stereo, 8000, 2, Fast,
            execution: new AudioEffectExecutionOptions { MaximumThreads = Math.Min(2, Environment.ProcessorCount) });

        CollectionAssert.AreEqual(serial.Samples, parallel.Samples);
    }

    private static float[] Tone(double hz) => Enumerable.Range(0, 8000).Select(i => (float)(0.25 * Math.Sin(2 * Math.PI * hz * i / 8000))).ToArray();

    private static double Estimate(float[] samples, int rate, double min, double max)
    {
        int start = samples.Length / 3, end = samples.Length * 5 / 6;
        double best = min, peak = 0;
        for (double hz = min; hz <= max; hz += 0.2)
        {
            double real = 0, imaginary = 0;
            for (int i = start; i < end; i++)
            {
                double phase = 2 * Math.PI * hz * i / rate;
                real += samples[i] * Math.Cos(phase); imaginary += samples[i] * Math.Sin(phase);
            }
            double amplitude = real * real + imaginary * imaginary;
            if (amplitude > peak) { peak = amplitude; best = hz; }
        }
        return best;
    }
}
