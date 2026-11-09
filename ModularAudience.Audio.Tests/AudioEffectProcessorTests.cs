using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Effects;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class AudioEffectProcessorTests
{
    public static IEnumerable<object[]> Effects => AudioEffectCatalog.All.Where(effect => effect.Kind != AudioEffectKind.AutoTune).Select(effect => new object[] { effect.Kind });
    public static IEnumerable<object[]> FactoryProfiles => AudioEffectCatalog.All.Where(effect => effect.Kind != AudioEffectKind.AutoTune)
        .SelectMany(effect => effect.BuiltInPresets.Select(preset => new object[] { effect.Kind, preset.Id }));

    [TestMethod]
    [DynamicData(nameof(FactoryProfiles))]
    public async Task EveryFactoryProfile_RendersStableFiniteAudio(AudioEffectKind effect, string id)
    {
        AudioEffectSettings settings = AudioEffectCatalog.Get(effect).BuiltInPresets.Single(preset => preset.Id == id).Parameters.Copy();
        if (settings.Parameters.ContainsKey("TailSeconds")) settings = settings.With(("TailSeconds", 0.05));
        AudioEffectResult result = await AudioEffectProcessor.ProcessAsync(StereoTone(8000, 0.15, 433), 8000, 2, settings);
        Assert.IsTrue(result.Samples.All(float.IsFinite), effect + "/" + id);
        Assert.IsTrue(result.Samples.Any(sample => Math.Abs(sample) > 1e-7), effect + "/" + id);
    }

    [TestMethod]
    public void Catalog_HasEveryRequestedEffectAndValidDistinctFactoryProfiles()
    {
        Assert.AreEqual(37, AudioEffectCatalog.All.Count);
        Assert.AreEqual(9, AudioEffectCatalog.All.Select(effect => effect.Group).Distinct().Count());
        CollectionAssert.AreEquivalent(Enum.GetValues<AudioEffectKind>(), AudioEffectCatalog.All.Select(effect => effect.Kind).ToArray());
        foreach (AudioEffectDefinition definition in AudioEffectCatalog.All.Where(effect => effect.Kind != AudioEffectKind.AutoTune))
        {
            definition.CreateDefaults().Validate();
            if (!definition.OpensOptions) continue;
            Assert.IsGreaterThanOrEqualTo(3, definition.BuiltInPresets.Count, definition.Name);
            Assert.AreEqual(definition.BuiltInPresets.Count, definition.BuiltInPresets.Select(preset => preset.Id).Distinct().Count());
            foreach (EffectPresetDefinition preset in definition.BuiltInPresets)
            {
                preset.Parameters.Validate();
                AudioEffectSettings copy = preset.Parameters.Copy();
                Assert.AreNotSame(copy.Parameters, preset.Parameters.Parameters);
            }
            for (int i = 0; i < definition.BuiltInPresets.Count; i++)
                for (int j = i + 1; j < definition.BuiltInPresets.Count; j++)
                    Assert.IsFalse(definition.BuiltInPresets[i].Parameters.SoundEquals(definition.BuiltInPresets[j].Parameters), definition.Name);
        }
    }

    [TestMethod]
    [DynamicData(nameof(Effects))]
    public async Task EveryEffect_HasRealFiniteFrameAlignedDspWithoutMutatingInput(AudioEffectKind effect)
    {
        float[] input = StereoTone(8000, 0.12, 330); input[0] = 0.8f;
        float[] before = (float[])input.Clone(); AudioEffectSettings settings = Defaults(effect);
        if (settings.Parameters.ContainsKey("TailSeconds")) settings = settings.With(("TailSeconds", 0.2));
        if (effect == AudioEffectKind.Limiter) settings = settings.With(("InputGainDb", 12));
        if (effect == AudioEffectKind.Expander) settings = settings.With(("ThresholdDb", -6));
        if (effect == AudioEffectKind.SampleRateReducer) settings = settings.With(("RateHz", 4000));

        AudioEffectResult result = await AudioEffectProcessor.ProcessAsync(input, 8000, 2, settings);

        CollectionAssert.AreEqual(before, input);
        Assert.IsGreaterThan(0, result.Samples.Length);
        Assert.AreEqual(0, result.Samples.Length % result.Channels);
        Assert.IsTrue(result.Samples.All(float.IsFinite), effect.ToString());
        Assert.AreEqual(effect == AudioEffectKind.ConvertToMono ? 1 : 2, result.Channels);
        if (effect is not (AudioEffectKind.PitchShift or AudioEffectKind.Gain))
            Assert.IsFalse(before.SequenceEqual(result.Samples), "Missing audible DSP route: " + effect);
    }

    [TestMethod]
    [DynamicData(nameof(Effects))]
    public async Task EveryEffect_ProcessesDigitalSilenceWithoutInventingAudio(AudioEffectKind effect)
    {
        AudioEffectSettings settings = Defaults(effect);
        if (settings.Parameters.ContainsKey("TailSeconds")) settings = settings.With(("TailSeconds", 0.01));
        AudioEffectResult result = await AudioEffectProcessor.ProcessAsync(new float[1024], 8000, 2, settings);
        Assert.IsTrue(result.Samples.All(sample => sample == 0), effect.ToString());
    }

    [TestMethod]
    [DynamicData(nameof(Effects))]
    public async Task EveryEffect_HonorsCancellationBeforeRendering(AudioEffectKind effect)
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => AudioEffectProcessor.ProcessAsync(new float[1024], 8000, 2,
            Defaults(effect), cancellationToken: cancellation.Token));
    }

    [TestMethod]
    public async Task UtilityDsp_PreservesChannelsAndHasExactExpectedResults()
    {
        float[] source = [1, 3, 2, 4, 3, 5];
        AudioEffectResult reverse = await Process(source, AudioEffectKind.Reverse);
        CollectionAssert.AreEqual(new float[] { 3, 5, 2, 4, 1, 3 }, reverse.Samples);
        AudioEffectResult mono = await Process(source, AudioEffectKind.ConvertToMono);
        CollectionAssert.AreEqual(new float[] { 2, 3, 4 }, mono.Samples);
        AudioEffectResult gain = await Process(source, AudioEffectKind.Gain, ("GainDb", -6.020599913279624));
        CollectionAssert.AreEqual(source.Select(value => value * 0.5f).ToArray(), gain.Samples);
        AudioEffectResult normalized = await Process(source, AudioEffectKind.Normalize, ("TargetDb", 0));
        Assert.AreEqual(1f, normalized.Samples.Max()); Assert.AreEqual(0.2f, normalized.Samples[0], 1e-6f);
        AudioEffectResult fadeIn = await Process(source, AudioEffectKind.FadeIn);
        CollectionAssert.AreEqual(new float[] { 0, 0, 1, 2, 3, 5 }, fadeIn.Samples);
        AudioEffectResult fadeOut = await Process(source, AudioEffectKind.FadeOut);
        CollectionAssert.AreEqual(new float[] { 1, 3, 1, 2, 0, 0 }, fadeOut.Samples);
        AudioEffectResult width = await Process(source, AudioEffectKind.StereoWidth, ("WidthPercent", 0));
        CollectionAssert.AreEqual(new float[] { 2, 2, 3, 3, 4, 4 }, width.Samples);
    }

    [TestMethod]
    [DataRow(-12d, 0)]
    [DataRow(12d, 0)]
    [DataRow(-12d, 1)]
    [DataRow(12d, 1)]
    public async Task PitchShift_UsesExistingCorrectAlgorithmAndLength(double semitones, int method)
    {
        float[] source = StereoTone(16000, 0.5, 440);
        AudioEffectResult result = await AudioEffectProcessor.ProcessAsync(source, 16000, 2,
            Defaults(AudioEffectKind.PitchShift).With(("Semitones", semitones), ("Method", method)));
        Assert.AreEqual(method == 0 ? source.Length : (int)(source.Length / Math.Pow(2, semitones / 12)), result.Samples.Length);
        Assert.IsGreaterThan(0.10, Amplitude(result.Samples, 16000, 440 * Math.Pow(2, semitones / 12)));
        for (int i = 0; i < result.Samples.Length; i += 2) Assert.AreEqual(-0.5f * result.Samples[i], result.Samples[i + 1], 0.0001f);
    }

    [TestMethod]
    [DataRow(0.8)]
    [DataRow(1.5)]
    [DataRow(2d)]
    public async Task TimeStretch_PreservesPitchStereoAndRequestedFrameCount(double factor)
    {
        float[] source = StereoTone(16000, 0.5, 440);
        AudioEffectResult result = await AudioEffectProcessor.ProcessAsync(source, 16000, 2, Defaults(AudioEffectKind.TimeStretch).With(("Factor", factor)));
        Assert.AreEqual((int)(source.Length * factor), result.Samples.Length);
        Assert.IsGreaterThan(0.1, Amplitude(result.Samples, 16000, 440));
        for (int i = 0; i < result.Samples.Length; i += 2) Assert.AreEqual(-0.5f * result.Samples[i], result.Samples[i + 1], 0.0001f);
    }

    [TestMethod]
    public async Task PingPongFeedback_CrossesStereoInsteadOfSwappingChannelOrder()
    {
        float[] impulse = new float[8000 * 2]; impulse[0] = 1;
        AudioEffectResult result = await Process(impulse, AudioEffectKind.PingPongDelay,
            ("DelayMs", 10), ("FeedbackPercent", 50), ("WetPercent", 100), ("TailSeconds", 0));
        Assert.IsGreaterThan(0.1f, result.Samples[80 * 2]);
        Assert.IsGreaterThan(0.05f, result.Samples[160 * 2 + 1]);
        Assert.AreEqual(0f, result.Samples[80 * 2 + 1]);
    }

    [TestMethod]
    public async Task ReverbModels_HaveDistinctImpulseResponsesAndCanAppendOrCropTails()
    {
        float[] impulse = new float[800]; impulse[0] = 1;
        var responses = new List<float[]>();
        foreach (AudioEffectKind kind in new[] { AudioEffectKind.RoomReverb, AudioEffectKind.HallReverb, AudioEffectKind.PlateReverb,
                     AudioEffectKind.SpringReverb, AudioEffectKind.ChamberReverb, AudioEffectKind.ShimmerReverb })
        {
            AudioEffectSettings settings = Defaults(kind).With(("TailSeconds", 0.4), ("WetPercent", 100));
            AudioEffectResult result = await Process(impulse, settings);
            Assert.AreEqual(7200, result.Samples.Length);
            Assert.IsTrue(result.Samples.Skip(impulse.Length).Any(sample => Math.Abs(sample) > 1e-6), kind.ToString());
            foreach (float[] previous in responses) Assert.IsFalse(previous.SequenceEqual(result.Samples), kind.ToString());
            responses.Add(result.Samples);
            Assert.AreEqual(impulse.Length, (await Process(impulse, settings.With(("TailSeconds", 0)))).Samples.Length);
        }
    }

    [TestMethod]
    public async Task Limiter_EnforcesCeilingWithoutDurationOrStereoBalanceChanges()
    {
        float[] source = StereoTone(8000, 0.4, 440).Select(sample => sample * 12).ToArray();
        AudioEffectResult result = await Process(source, AudioEffectKind.Limiter, ("CeilingDb", -3));
        Assert.AreEqual(source.Length, result.Samples.Length);
        Assert.IsTrue(result.Samples.All(sample => Math.Abs(sample) <= Math.Pow(10, -3d / 20) + 1e-6));
        for (int i = 0; i < result.Samples.Length; i += 2) Assert.AreEqual(-0.5f * result.Samples[i], result.Samples[i + 1], 1e-6f);
    }

    [TestMethod]
    public async Task CompressorGateAndExpander_ActuallyReduceTheAppropriateLevels()
    {
        float[] loud = StereoTone(8000, 1, 440);
        AudioEffectResult compressed = await Process(loud, AudioEffectKind.Compressor, ("ThresholdDb", -30), ("Ratio", 8), ("MakeupDb", 0));
        Assert.IsLessThan(Rms(loud) * 0.5, Rms(compressed.Samples));
        float[] quiet = loud.Select(sample => sample * 0.005f).ToArray();
        foreach (AudioEffectKind kind in new[] { AudioEffectKind.Gate, AudioEffectKind.Expander })
        {
            AudioEffectResult result = await Process(quiet, kind);
            Assert.IsLessThan(Rms(quiet) * 0.2, Rms(result.Samples));
        }
    }

    [TestMethod]
    public async Task DeEsser_ReducesSibilanceMoreThanLowVoiceTone()
    {
        async Task<double> Reduction(double frequency)
        {
            float[] tone = StereoTone(24000, 0.5, frequency);
            var result = await AudioEffectProcessor.ProcessAsync(tone, 24000, 2,
                Defaults(AudioEffectKind.DeEsser).With(("FrequencyHz", 6000), ("ThresholdDb", -40), ("Ratio", 10), ("MaximumReductionDb", 24)));
            return Rms(result.Samples) / Rms(tone);
        }
        Assert.IsLessThan(0.5, await Reduction(6000)); Assert.IsGreaterThan(0.85, await Reduction(300));
    }

    [TestMethod]
    public async Task Bitcrusher_OffersDistinctQuantizersAndRateReducerReallyHoldsSamples()
    {
        float[] source = StereoTone(8000, 0.1, 433); var variants = new List<float[]>();
        for (int mode = 0; mode < 4; mode++)
        {
            AudioEffectSettings settings = Defaults(AudioEffectKind.Bitcrusher).With(("Bits", 4), ("Mode", mode), ("WetPercent", 100));
            AudioEffectResult result = await Process(source, settings);
            foreach (float[] previous in variants) Assert.IsFalse(previous.SequenceEqual(result.Samples));
            CollectionAssert.AreEqual(result.Samples, (await Process(source, settings)).Samples); variants.Add(result.Samples);
        }
        AudioEffectResult held = await Process(source, AudioEffectKind.SampleRateReducer, ("RateHz", 4000), ("WetPercent", 100));
        for (int i = 0; i + 3 < held.Samples.Length; i += 4)
        { Assert.AreEqual(held.Samples[i], held.Samples[i + 2]); Assert.AreEqual(held.Samples[i + 1], held.Samples[i + 3]); }
    }

    [TestMethod]
    public async Task HumRemoval_AttenuatesMainsButRetainsUnrelatedTone()
    {
        float[] source = StereoTone(8000, 3, 50), tone = StereoTone(8000, 3, 1000);
        for (int i = 0; i < source.Length; i++) source[i] += tone[i];
        AudioEffectResult result = await Process(source, AudioEffectKind.HumRemoval);
        Assert.IsLessThan(0.01, Amplitude(result.Samples, 8000, 50));
        Assert.IsGreaterThan(0.20, Amplitude(result.Samples, 8000, 1000));
    }

    [TestMethod]
    public async Task SpectralNoiseReduction_LowersStationaryNoiseAndBypassesBitExactly()
    {
        var random = new Random(42);
        float[] noise = Enumerable.Range(0, 16000).Select(_ => (float)((random.NextDouble() * 2 - 1) * 0.1)).ToArray();
        AudioEffectResult result = await Process(noise, AudioEffectKind.NoiseReduction, ("Profile", 1), ("ProfileMs", 400), ("ReductionDb", 30));
        Assert.IsLessThan(Rms(noise) * 0.8, Rms(result.Samples));
        AudioEffectResult bypass = await Process(noise, AudioEffectKind.NoiseReduction, ("AmountPercent", 0));
        CollectionAssert.AreEqual(noise, bypass.Samples);
    }

    [TestMethod]
    public async Task WetZero_BypassesIncludingTailAndNonFiniteInputsAreRejected()
    {
        float[] source = StereoTone(8000, 0.1, 440);
        AudioEffectResult result = await Process(source, AudioEffectKind.HallReverb, ("WetPercent", 0));
        CollectionAssert.AreEqual(source, result.Samples);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => AudioEffectProcessor.ProcessAsync(new[] { float.NaN }, 8000, 1, Defaults(AudioEffectKind.Gain)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Defaults(AudioEffectKind.Bitcrusher).With(("Bits", 4.5)));
        Assert.ThrowsExactly<ArgumentException>(() => Defaults(AudioEffectKind.Phaser).With(("MinimumHz", 4000), ("MaximumHz", 2000)));
    }

    private static AudioEffectSettings Defaults(AudioEffectKind kind) => AudioEffectCatalog.Get(kind).CreateDefaults();
    private static Task<AudioEffectResult> Process(float[] source, AudioEffectKind kind, params (string Key, double Value)[] changes) => Process(source, Defaults(kind).With(changes));
    private static Task<AudioEffectResult> Process(float[] source, AudioEffectSettings settings) => AudioEffectProcessor.ProcessAsync(source, 8000, 2, settings);
    private static float[] StereoTone(int rate, double seconds, double frequency)
    {
        float[] result = new float[(int)(rate * seconds) * 2];
        for (int frame = 0; frame < result.Length / 2; frame++)
        { result[frame * 2] = (float)(0.25 * Math.Sin(2 * Math.PI * frequency * frame / rate)); result[frame * 2 + 1] = -result[frame * 2] * 0.5f; }
        return result;
    }
    private static double Rms(float[] samples) => Math.Sqrt(samples.Skip(samples.Length / 3).Average(sample => (double)sample * sample));
    private static double Amplitude(float[] samples, int rate, double frequency)
    {
        int frames = samples.Length / 2, start = frames / 3, end = frames * 5 / 6; double real = 0, imaginary = 0;
        for (int frame = start; frame < end; frame++)
        {
            double phase = frame * 2 * Math.PI * frequency / rate;
            real += samples[frame * 2] * Math.Cos(phase); imaginary += samples[frame * 2] * Math.Sin(phase);
        }
        return 2 * Math.Sqrt(real * real + imaginary * imaginary) / (end - start);
    }
}
