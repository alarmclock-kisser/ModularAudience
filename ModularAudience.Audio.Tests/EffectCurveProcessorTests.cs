using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Effects;
using ModularAudience.Audio.Effects.AutoTune;
using ModularAudience.Audio.Effects.Curves;
using ModularAudience.Audio.Processors_V4;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class EffectCurveProcessorTests
{
    public static IEnumerable<object[]> Effects => AudioEffectCatalog.All.Where(effect => effect.Kind != AudioEffectKind.AutoTune &&
        EffectCurveTargets.ForEffect(effect.Kind).Count > 0).Select(effect => new object[] { effect.Kind });
    public static IEnumerable<object[]> ScalarTargets => AudioEffectCatalog.All.Where(effect => effect.Kind != AudioEffectKind.AutoTune)
        .SelectMany(effect => EffectCurveTargets.ForEffect(effect.Kind).Where(target => effect.Parameters.Any(parameter => parameter.Key == target.Key) &&
            target.Key is not ("Semitones" or "Factor" or "Speed")).Select(target => new object[] { effect.Kind, target.Key }));
    public static IEnumerable<object[]> AutoTuneTargets => AutoTuneCurveTargets.All.Where(target => target.Key != EffectCurveTargets.Amount)
        .Select(target => new object[] { target.Key });
    public static IEnumerable<object[]> AmountEffects => AudioEffectCatalog.All.Where(effect =>
        EffectCurveTargets.ForEffect(effect.Kind).Any(target => target.Key == EffectCurveTargets.Amount)).Select(effect => new object[] { effect.Kind });

    [TestMethod]
    [DynamicData(nameof(ScalarTargets))]
    public async Task EveryAdvertisedScalarCurve_ActuallyReachesTheSameDspAsItsConstantParameter(AudioEffectKind effect, string key)
    {
        AudioEffectSettings settings = Defaults(effect); EffectCurveTarget target = EffectCurveTargets.ForEffect(effect).Single(target => target.Key == key);
        double value = target.Minimum + (target.Maximum - target.Minimum) * 0.3;
        if (target.Integral) value = Math.Round(value);
        if (key == "RateHz" && effect == AudioEffectKind.SampleRateReducer) value = 4000;
        if (key == "MinimumHz") settings = settings.With(("MaximumHz", 16000));
        if (key == "MaximumHz") settings = settings.With(("MinimumHz", 20));
        if (effect == AudioEffectKind.HumRemoval && key == "FrequencyHz") settings = settings.With(("Mains", 2));
        if (effect == AudioEffectKind.NoiseReduction && key == "NoiseFloorDb") settings = settings.With(("Profile", 2));
        float[] input = Signal(8000, 0.35); float[] before = (float[])input.Clone();
        AudioEffectResult expected = await AudioEffectProcessor.ProcessAsync(input, 8000, 2, settings.With((key, value)));
        AudioEffectSettings curved = WithCurve(settings, key, new() { StartValue = value, Curve = new() { Equation = "0" } });
        AudioEffectResult actual = await AudioEffectProcessor.ProcessAsync(input, 8000, 2, curved);
        Assert.AreEqual(expected.Channels, actual.Channels); Assert.AreEqual(expected.Samples.Length, actual.Samples.Length, effect + "/" + key);
        double difference = actual.Samples.Zip(expected.Samples).Max(pair => Math.Abs(pair.First - pair.Second));
        Assert.IsLessThan(0.00005, difference, $"{effect}/{key}: curve was ignored or reset DSP state (max error {difference:G6}).");
        CollectionAssert.AreEqual(before, input);
    }

    [TestMethod]
    [DynamicData(nameof(Effects))]
    public async Task EverySupportedEffect_AcceptsSimultaneousPeriodicParameterCurvesWithoutChangingInput(AudioEffectKind effect)
    {
        AudioEffectSettings settings = Defaults(effect);
        if (effect == AudioEffectKind.PitchShift) settings = settings.With(("Semitones", 12));
        if (effect == AudioEffectKind.TimeStretch) settings = settings.With(("Factor", 0.5));
        if (effect == AudioEffectKind.HumRemoval) settings = settings.With(("Mains", 2));
        if (effect == AudioEffectKind.NoiseReduction) settings = settings.With(("Profile", 2));
        var curves = EffectCurveTargets.ForEffect(effect).ToDictionary(target => target.Key, target => new EffectParameterCurve
        {
            StartValue = target.NeutralValue, Curve = new() { Equation = "0.5+0.5*sin(2*pi*(f*x+p))", Cycles = 2 }
        }, StringComparer.Ordinal);
        settings = settings with { Automation = new() { Parameters = curves } };
        float[] input = Signal(8000, 0.2); float[] before = (float[])input.Clone();
        AudioEffectResult result = await AudioEffectProcessor.ProcessAsync(input, 8000, 2, settings);
        Assert.IsGreaterThan(0, result.Samples.Length, effect.ToString()); Assert.AreEqual(0, result.Samples.Length % 2);
        Assert.IsTrue(result.Samples.All(float.IsFinite), effect.ToString()); CollectionAssert.AreEqual(before, input);
        if (effect is not (AudioEffectKind.PitchShift or AudioEffectKind.TimeStretch or AudioEffectKind.VarispeedShift)) Assert.AreEqual(input.Length + (int)(settings.Parameters.GetValueOrDefault("TailSeconds") * 8000) * 2, result.Samples.Length);
    }

    [TestMethod]
    [DynamicData(nameof(Effects))]
    public async Task DisabledCurves_AreBitExactWithLegacyRendering_AndPreCancelledCurvesDoNotRun(AudioEffectKind effect)
    {
        AudioEffectSettings settings = Defaults(effect); EffectCurveTarget target = EffectCurveTargets.ForEffect(effect).First();
        if (effect == AudioEffectKind.PitchShift) settings = settings.With(("Semitones", 7));
        if (effect == AudioEffectKind.Gain) settings = settings.With(("GainDb", 6));
        var disabled = WithCurve(settings, target.Key, new() { Enabled = false, StartValue = target.NeutralValue, Curve = new() { Equation = "1-x" } });
        float[] input = Signal(8000, 0.1);
        input[0] = BitConverter.Int32BitsToSingle(int.MinValue);
        AudioEffectResult expected = await AudioEffectProcessor.ProcessAsync(input, 8000, 2, settings);
        AudioEffectResult actual = await AudioEffectProcessor.ProcessAsync(input, 8000, 2, disabled);
        CollectionAssert.AreEqual(expected.Samples, actual.Samples, effect.ToString());
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => AudioEffectProcessor.ProcessAsync(input, 8000, 2, disabled, cancellationToken: cts.Token));
    }

    [TestMethod]
    [DynamicData(nameof(AmountEffects))]
    public async Task EffectAmount_ZeroIsDryAndFullAmountMatchesTheEffect(AudioEffectKind effect)
    {
        AudioEffectSettings settings = Defaults(effect); if (effect == AudioEffectKind.Gain) settings = settings.With(("GainDb", 6));
        float[] input = Signal(8000, 0.2);
        AudioEffectResult reference = await AudioEffectProcessor.ProcessAsync(input, 8000, 2, settings);
        AudioEffectResult dry = await AudioEffectProcessor.ProcessAsync(input, 8000, 2, WithCurve(settings, EffectCurveTargets.Amount,
            new() { StartValue = 0, Curve = new() { Equation = "0" } }));
        Assert.AreEqual(input.Length, dry.Samples.Length, "An everywhere-dry curve must not insert a silent tail or create an edit.");
        CollectionAssert.AreEqual(input, dry.Samples.Take(input.Length).ToArray()); Assert.IsTrue(dry.Samples.Skip(input.Length).All(sample => sample == 0));
        AudioEffectResult wet = await AudioEffectProcessor.ProcessAsync(input, 8000, 2, WithCurve(settings, EffectCurveTargets.Amount,
            new() { StartValue = 0, Curve = new() { Equation = "1" } }));
        CollectionAssert.AreEqual(reference.Samples, wet.Samples, effect.ToString());
    }

    [TestMethod]
    public async Task GainAndIndependentOutputCurves_HaveExactExpectedFrameLinkedValues()
    {
        float[] input = Enumerable.Range(0, 101).SelectMany(_ => new[] { 0.1f, -0.05f }).ToArray();
        var settings = WithCurve(Defaults(AudioEffectKind.Gain).With(("GainDb", 20)), "GainDb", new() { StartValue = 0 });
        var result = await AudioEffectProcessor.ProcessAsync(input, 8000, 2, settings);
        for (int frame = 0; frame <= 100; frame++)
        {
            Assert.AreEqual(0.1 * Math.Pow(10, frame / 100d), result.Samples[frame * 2], 1e-6);
            Assert.AreEqual(-0.5 * result.Samples[frame * 2], result.Samples[frame * 2 + 1], 1e-7);
        }
        var overdrive = Defaults(AudioEffectKind.Overdrive).With(("WetPercent", 0), ("OutputGainDb", 6));
        overdrive = WithCurve(overdrive, "OutputGainDb", new() { StartValue = 0 });
        result = await AudioEffectProcessor.ProcessAsync(input, 8000, 2, overdrive);
        Assert.AreEqual(input[0], result.Samples[0]); Assert.AreEqual(input[^2] * Math.Pow(10, 6d / 20), result.Samples[^2], 1e-6);
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("x-x")]
    public async Task ConstantZeroWetCurve_BypassesWithoutAppendingSilenceOrCreatingHistory(string equation)
    {
        float[] input = Signal(8000, 0.1);
        input[0] = BitConverter.Int32BitsToSingle(int.MinValue);
        var settings = WithCurve(Defaults(AudioEffectKind.HallReverb), "WetPercent", new() { StartValue = 0, Curve = new() { Equation = equation } });
        var result = await AudioEffectProcessor.ProcessAsync(input, 8000, 2, settings); CollectionAssert.AreEqual(input, result.Samples);
        using var audio = new AudioObj { Data = (float[])input.Clone(), Channels = 2, SampleRate = 8000, Length = input.Length };
        Assert.IsFalse(AudioEffectEditSession.Capture(audio).Apply(audio, result)); Assert.IsFalse(audio.CanUndo);
        settings = WithCurve(Defaults(AudioEffectKind.HallReverb), EffectCurveTargets.Amount, new() { StartValue = 0, Curve = new() { Equation = equation } });
        result = await AudioEffectProcessor.ProcessAsync(input, 8000, 2, settings); CollectionAssert.AreEqual(input, result.Samples);
    }

    [TestMethod]
    [DataRow(AudioEffectKind.FadeIn, 0d, 1d)]
    [DataRow(AudioEffectKind.FadeOut, 1d, 0d)]
    public async Task CustomFadeEnvelope_OverridesBuiltInShapeAndLength(AudioEffectKind effect, double from, double to)
    {
        float[] input = Enumerable.Repeat(1f, 101).ToArray();
        var settings = WithCurve(Defaults(effect).With(("LengthPercent", 10)), EffectCurveTargets.FadeEnvelope,
            new() { StartValue = from, EndValue = to, Curve = new() { Equation = "x^2" } });
        var result = await AudioEffectProcessor.ProcessAsync(input, 8000, 1, settings);
        for (int i = 0; i <= 100; i++) Assert.AreEqual(from + (to - from) * Math.Pow(i / 100d, 2), result.Samples[i], 1e-7);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public async Task SlidingPitch_ShiftsFrequencyOverTimeInsteadOfCrossfadingTwoFixedPitches(int method)
    {
        const int rate = 16000; const double seconds = 2;
        float[] input = Tone(rate, seconds, 220);
        var settings = WithCurve(Defaults(AudioEffectKind.PitchShift).With(("Semitones", 12), ("Method", method)), "Semitones", new() { StartValue = 0 });
        var result = await AudioEffectProcessor.ProcessAsync(input, rate, 2, settings);
        Assert.AreEqual(2, result.Channels); Assert.IsNull(result.TempoMultiplier);
        if (method == 0) Assert.AreEqual(input.Length, result.Samples.Length);
        else Assert.IsTrue(result.Samples.Length < input.Length && result.Samples.Length > input.Length / 2);
        double early = Frequency(result.Samples, rate, 0.25, 0.15), late = Frequency(result.Samples, rate, result.Samples.Length / 2d / rate - 0.35, 0.15);
        Assert.IsTrue(early > 215 && early < 280, $"Start frequency: {early}"); Assert.IsTrue(late > 350 && late < 460, $"End frequency: {late}");
        Assert.IsGreaterThan(early * 1.3, late);
        for (int i = 0; i < result.Samples.Length; i += 2) Assert.AreEqual(-0.5f * result.Samples[i], result.Samples[i + 1], 0.00001f);
    }

    [TestMethod]
    [DataRow(0.5)]
    [DataRow(2d)]
    public async Task SpeedCurve_IntegratesTargetBpmAndPreservesPitchAndStereo(double targetFactor)
    {
        const int rate = 16000, frames = 16000;
        float[] input = Tone(rate, 1, 330);
        var settings = WithCurve(Defaults(AudioEffectKind.TimeStretch).With(("Factor", targetFactor), ("FftSize", 2)), "Factor", new() { StartValue = 1 });
        var result = await AudioEffectProcessor.ProcessAsync(input, rate, 2, settings);
        double targetSpeed = 1 / targetFactor;
        double expected = frames * Math.Log(targetSpeed) / (targetSpeed - 1);
        Assert.AreEqual(Math.Round(expected), result.Samples.Length / 2d, 1);
        Assert.IsNull(result.TempoMultiplier); Assert.AreEqual(330, Frequency(result.Samples, rate, 0.15, 0.2), 8);
        for (int i = 0; i < result.Samples.Length; i += 2) Assert.AreEqual(-0.5f * result.Samples[i], result.Samples[i + 1], 0.00001f);
        Assert.IsTrue(result.Samples.All(float.IsFinite));
    }

    [TestMethod]
    [DataRow(0.5)]
    [DataRow(1d)]
    [DataRow(2d)]
    public async Task VarispeedShift_ChangesPitchTempoAndDurationTogether(double speed)
    {
        const int rate = 16000; float[] input = Tone(rate, 0.6, 330);
        var result = await AudioEffectProcessor.ProcessAsync(input, rate, 2, Defaults(AudioEffectKind.VarispeedShift).With(("Speed", speed)));
        Assert.AreEqual((int)Math.Round(input.Length / 2d / speed) * 2, result.Samples.Length); Assert.AreEqual(speed, result.TempoMultiplier);
        Assert.AreEqual(330 * speed, Frequency(result.Samples, rate, 0.05, 0.2), 3);
        for (int i = 0; i < result.Samples.Length; i += 2) Assert.AreEqual(-0.5f * result.Samples[i], result.Samples[i + 1], 0.000001f);
        if (speed == 1) CollectionAssert.AreEqual(input, result.Samples);
    }

    [TestMethod]
    public async Task VarispeedCurve_ContinuouslySweepsBothPitchAndSpeedAndIntegratesTheRate()
    {
        const int rate = 16000; float[] input = Tone(rate, 2, 220);
        var settings = WithCurve(Defaults(AudioEffectKind.VarispeedShift).With(("Speed", 2)), "Speed", new() { StartValue = 1 });
        var result = await AudioEffectProcessor.ProcessAsync(input, rate, 2, settings);
        Assert.AreEqual(Math.Round(rate * 2 * Math.Log(2)) * 2, result.Samples.Length, 2); Assert.IsNull(result.TempoMultiplier);
        double early = Frequency(result.Samples, rate, 0.15, 0.15), late = Frequency(result.Samples, rate, 1.1, 0.15);
        Assert.IsTrue(early is > 220 and < 280); Assert.IsTrue(late is > 360 and < 440);
        for (int i = 0; i < result.Samples.Length; i += 2) Assert.AreEqual(-0.5f * result.Samples[i], result.Samples[i + 1], 0.000001f);
    }

    [TestMethod]
    public async Task AConstantSpeedCurve_StillReportsItsTruthfulUniformTempoMultiplier()
    {
        var settings = WithCurve(Defaults(AudioEffectKind.VarispeedShift).With(("Speed", 2)), "Speed", new() { StartValue = 1, Curve = new() { Equation = "1" } });
        var result = await AudioEffectProcessor.ProcessAsync(Tone(8000, 0.2, 220), 8000, 2, settings);
        Assert.AreEqual(2d, result.TempoMultiplier); Assert.AreEqual(1600, result.Samples.Length);
    }

    [TestMethod]
    public async Task CurvedStretch_HonorsQualityOverlapAndProducesDeterministicResultsAcrossWorkerBudgets()
    {
        float[] input = Signal(16000, 0.3);
        var settings = WithCurve(Defaults(AudioEffectKind.TimeStretch).With(("Factor", 0.5), ("Overlap", 0.5), ("FftSize", 2)), "Factor", new() { StartValue = 1 });
        var first = await AudioEffectProcessor.ProcessAsync(input, 16000, 2, settings, execution: new() { MaximumThreads = 1 });
        var parallel = await AudioEffectProcessor.ProcessAsync(input, 16000, 2, settings, execution: new() { MaximumThreads = Environment.ProcessorCount });
        CollectionAssert.AreEqual(first.Samples, parallel.Samples);
        var denser = await AudioEffectProcessor.ProcessAsync(input, 16000, 2, settings.With(("Overlap", 0.95)));
        Assert.AreEqual(first.Samples.Length, denser.Samples.Length); Assert.IsFalse(first.Samples.SequenceEqual(denser.Samples));
        Assert.IsTrue(denser.Samples.All(float.IsFinite));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(3)]
    [DataRow(6)]
    public async Task SupportedCurveRoutes_KeepMonoOddAndMultichannelLayoutsAligned(int channels)
    {
        const int frames = 800;
        float[] input = Enumerable.Range(0, frames).SelectMany(frame => Enumerable.Range(0, channels)
            .Select(channel => (float)(0.15 * Math.Sin(2 * Math.PI * 220 * frame / 8000) * (channel + 1) / channels))).ToArray();
        foreach (object[] row in Effects)
        {
            var effect = (AudioEffectKind)row[0]; var settings = Defaults(effect); EffectCurveTarget target = EffectCurveTargets.ForEffect(effect).First();
            settings = WithCurve(settings, target.Key, new() { StartValue = target.NeutralValue, Curve = new() { Equation = "smoothstep(x)" } });
            AudioEffectResult result = await AudioEffectProcessor.ProcessAsync(input, 8000, channels, settings);
            Assert.AreEqual(channels, result.Channels, effect.ToString()); Assert.AreEqual(0, result.Samples.Length % channels); Assert.IsTrue(result.Samples.All(float.IsFinite));
            if (effect == AudioEffectKind.StereoWidth && channels % 2 != 0)
                for (int frame = 0; frame < frames; frame++) Assert.AreEqual(input[frame * channels + channels - 1], result.Samples[frame * channels + channels - 1], 1e-7f);
        }
    }

    [TestMethod]
    public async Task CurveClock_SkippingSilenceChangesOnlyTheEnvelope_NotTheRenderedDuration()
    {
        float[] input = Enumerable.Repeat(0.1f, 10).Concat(new float[20]).Concat(Enumerable.Repeat(0.1f, 10)).ToArray();
        var settings = WithCurve(Defaults(AudioEffectKind.Gain).With(("GainDb", 20)), "GainDb", new() { StartValue = 0 });
        var count = await AudioEffectProcessor.ProcessAsync(input, 1000, 1, settings);
        settings = settings with { Automation = settings.Automation! with { Timing = new() { IncludeSilence = false, MinimumSilenceMs = 5 } } };
        var skip = await AudioEffectProcessor.ProcessAsync(input, 1000, 1, settings);
        Assert.AreEqual(input.Length, skip.Samples.Length); Assert.IsTrue(skip.Samples.Skip(10).Take(20).All(sample => sample == 0));
        Assert.IsGreaterThan(count.Samples[9], skip.Samples[9]); Assert.IsLessThan(count.Samples[30], skip.Samples[30]);
        Assert.AreEqual(count.Samples[^1], skip.Samples[^1]);
    }

    [TestMethod]
    public async Task SettingsAndCurveSnapshots_AreOwnedByTheRender()
    {
        float[] input = Enumerable.Repeat(0.1f, 10000).ToArray();
        var settings = WithCurve(Defaults(AudioEffectKind.Gain).With(("GainDb", 20)), "GainDb", new() { StartValue = 0 });
        Task<AudioEffectResult> task = AudioEffectProcessor.ProcessAsync(input, 8000, 1, settings);
        settings.Automation!.Parameters["GainDb"] = new() { StartValue = -96, Curve = new() { Equation = "0" } }; settings.Parameters["GainDb"] = -96;
        var result = await task; Assert.AreEqual(1f, result.Samples[^1], 1e-6f); Assert.AreEqual(0.1f, input[^1]);
    }

    [TestMethod]
    public async Task CurveRender_HonorsMidRenderCancellationAndRejectsCrossingPhaserBounds()
    {
        using var cts = new CancellationTokenSource();
        var settings = WithCurve(Defaults(AudioEffectKind.Gain).With(("GainDb", 12)), "GainDb", new() { StartValue = 0 });
        await Assert.ThrowsAsync<OperationCanceledException>(() => AudioEffectProcessor.ProcessAsync(Signal(8000, 4), 8000, 2, settings,
            new CallbackProgress(value => { if (value > 0) cts.Cancel(); }), cts.Token));
        settings = WithCurve(Defaults(AudioEffectKind.Phaser), "MinimumHz", new() { StartValue = 6000 });
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => AudioEffectProcessor.ProcessAsync(Signal(8000, 0.1), 8000, 2, settings));
    }

    [TestMethod]
    public void AutoTuneIntensityAndFineTune_CurvesReachCorrectionBeforeSynthesisWithoutBreakingNoteGating()
    {
        const int rate = 8000, hop = 80, frames = 8000;
        float[] mono = Enumerable.Range(0, frames).Select(i => (float)(0.2 * Math.Sin(2 * Math.PI * 435 * i / rate))).ToArray();
        PyinPitchFrame[] pitches = Enumerable.Range(0, frames / hop).Select(_ => new PyinPitchFrame(435, 1)).ToArray();
        var settings = new AutoTuneSettings { RetuneMilliseconds = 0, PitchSmoothingMilliseconds = 0, HysteresisCents = 0,
            DeadZoneCents = 0, VibratoPreservationPercent = 0, MinimumNoteLength = "1/128", Automation = new() { Parameters = new()
            { [nameof(AutoTuneSettings.IntensityPercent)] = new() { StartValue = 0 } } } };
        var evaluator = new EffectAutomationEvaluator(settings.Automation, AutoTuneCurveTargets.All, key => AutoTuneCurveTargets.BaseValue(settings, key), mono, 1, rate, null, default);
        var curve = AutoTuneCurveBuilder.Build(mono, pitches, rate, hop, settings, default, evaluator);
        Assert.AreEqual(0d, curve.Semitones[0], 1e-8); Assert.IsGreaterThan(0.15, curve.Semitones[^1]);
        settings = settings with { FineTuneCents = 20, Automation = new() { Parameters = new() { [nameof(AutoTuneSettings.FineTuneCents)] = new() { StartValue = 0 } } } };
        evaluator = new(settings.Automation, AutoTuneCurveTargets.All, key => AutoTuneCurveTargets.BaseValue(settings, key), mono, 1, rate, null, default);
        curve = AutoTuneCurveBuilder.Build(mono, pitches, rate, hop, settings, default, evaluator);
        Assert.IsGreaterThan(50, curve.CorrectedFrames); Assert.IsGreaterThan(curve.Semitones[10], curve.Semitones[^1]);
    }

    [TestMethod]
    [DynamicData(nameof(AutoTuneTargets))]
    public async Task EveryAdvertisedAutoTuneCurve_ReachesItsActualAnalysisCorrectionOrOutputControl(string key)
    {
        var settings = new AutoTuneSettings { MinimumFrequencyHz = 200, MaximumFrequencyHz = 700, FftSize = 1024, Overlap = 8,
            MinimumNoteLength = "1/128", BoundaryFadeMilliseconds = 0, ProtectPeak = false };
        EffectCurveTarget target = AutoTuneCurveTargets.All.Single(target => target.Key == key);
        double value = key switch
        {
            nameof(settings.ReferenceA4Hz) => 435, nameof(settings.OutputGainDb) => -6, nameof(settings.SilenceThresholdDb) => -40,
            nameof(settings.MaximumCorrectionSemitones) => 1.5, nameof(settings.FormantSmoothingHz) => 700,
            _ => target.Minimum + (target.Maximum - target.Minimum) * 0.3
        };
        AutoTuneSettings reference = settings.Copy(); typeof(AutoTuneSettings).GetProperty(key)!.SetValue(reference, value);
        AutoTuneSettings curved = settings with { Automation = new() { Parameters = new() { [key] = new() { StartValue = value, Curve = new() { Equation = "0" } } } } };
        float[] input = Tone(8000, 0.32, 432); var execution = new AudioEffectExecutionOptions { MaximumThreads = 1 };
        AutoTuneResult expected = await AutoTuneProcessor.ProcessAsync(input, 8000, 2, reference, execution: execution);
        AutoTuneResult actual = await AutoTuneProcessor.ProcessAsync(input, 8000, 2, curved, execution: execution);
        CollectionAssert.AreEqual(expected.Samples, actual.Samples, key);
        Assert.AreEqual(expected.CorrectedFrames, actual.CorrectedFrames); Assert.IsTrue(actual.Samples.All(float.IsFinite));
    }

    [TestMethod]
    public async Task AutoTuneAmountAndOutputGainCurves_WorkInBypassWithoutChangingPitchOrLength()
    {
        float[] input = Tone(8000, 0.2, 432);
        var settings = new AutoTuneSettings { IntensityPercent = 0, BoundaryFadeMilliseconds = 0, ProtectPeak = false,
            OutputGainDb = 12, Automation = new() { Parameters = new() { [nameof(AutoTuneSettings.OutputGainDb)] = new() { StartValue = 0 } } } };
        AutoTuneResult result = await AutoTuneProcessor.ProcessAsync(input, 8000, 2, settings);
        Assert.AreEqual(input.Length, result.Samples.Length);
        for (int frame = 0; frame < input.Length / 2; frame++) Assert.AreEqual(input[frame * 2] * Math.Pow(10, 12 * frame / (double)(input.Length / 2 - 1) / 20), result.Samples[frame * 2], 1e-6);
        settings = settings with { Automation = new() { Parameters = new() { [EffectCurveTargets.Amount] = new() { StartValue = 0, Curve = new() { Equation = "0" } } } } };
        result = await AutoTuneProcessor.ProcessAsync(input, 8000, 2, settings); CollectionAssert.AreEqual(input, result.Samples);
        Assert.AreEqual(BitConverter.SingleToInt32Bits(input[1]), BitConverter.SingleToInt32Bits(result.Samples[1]), "An all-dry curve preserves signed zero without redundant history.");
    }

    private static AudioEffectSettings Defaults(AudioEffectKind effect)
    {
        AudioEffectSettings settings = AudioEffectCatalog.Get(effect).CreateDefaults();
        return settings.Parameters.ContainsKey("TailSeconds") ? settings.With(("TailSeconds", 0.05)) : settings;
    }
    private static AudioEffectSettings WithCurve(AudioEffectSettings settings, string key, EffectParameterCurve curve) => settings with
        { Automation = new() { Parameters = new() { [key] = curve } } };
    private static float[] Tone(int rate, double seconds, double frequency) => Enumerable.Range(0, (int)Math.Round(rate * seconds))
        .SelectMany(i => new[] { (float)(0.2 * Math.Sin(2 * Math.PI * frequency * i / rate)), (float)(-0.1 * Math.Sin(2 * Math.PI * frequency * i / rate)) }).ToArray();
    private static float[] Signal(int rate, double seconds)
    {
        var random = new Random(91);
        return Enumerable.Range(0, (int)Math.Round(rate * seconds)).SelectMany(i =>
        {
            double amplitude = (i / 80) % 3 == 0 ? 0.005 : 0.5;
            float value = (float)(amplitude * (0.5 * Math.Sin(2 * Math.PI * 233 * i / rate) + 0.2 * Math.Sin(2 * Math.PI * 1700 * i / rate) + 0.2 * (random.NextDouble() * 2 - 1)));
            return new[] { value, -value * 0.5f };
        }).ToArray();
    }
    private static double Frequency(float[] samples, int rate, double startSeconds, double lengthSeconds)
    {
        int first = Math.Max(1, (int)(startSeconds * rate)), last = Math.Min(samples.Length / 2, first + (int)(lengthSeconds * rate));
        var crossings = new List<double>();
        for (int frame = first; frame < last; frame++)
        {
            double previous = samples[(frame - 1) * 2], next = samples[frame * 2];
            if (previous <= 0 && next > 0) crossings.Add(frame - 1 - previous / (next - previous));
        }
        return crossings.Count < 2 ? 0 : (crossings.Count - 1) * rate / (crossings[^1] - crossings[0]);
    }
    private sealed class CallbackProgress(Action<double> report) : IProgress<double> { public void Report(double value) => report(value); }
}
