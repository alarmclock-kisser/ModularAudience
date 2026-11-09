using System.Globalization;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Effects;
using ModularAudience.Audio.Effects.AutoTune;
using ModularAudience.Audio.Effects.Curves;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class EffectCurveTests
{
    public static IEnumerable<object[]> BuiltIns => EffectCurvePresets.All.Select(preset => new object[] { preset.Id });

    [TestMethod]
    [DynamicData(nameof(BuiltIns))]
    public void EveryBuiltIn_IsFiniteBoundedAndIndependent(string id)
    {
        EffectCurve source = EffectCurvePresets.All.Single(preset => preset.Id == id).Curve;
        EffectCurve copy = source.Copy(); CompiledEffectCurve curve = copy.Compile();
        Assert.AreNotSame(source.Points, copy.Points); Assert.IsTrue(copy.SoundEquals(source));
        for (int i = 0; i <= 10000; i++) Assert.IsTrue(curve.At(i / 10000d) is >= 0 and <= 1, id);
        Assert.IsFalse((copy with { Equation = "1-x" }).SoundEquals(source with { Equation = "x" }));
    }

    [TestMethod]
    [DataRow("x", 0.25, 0.25)]
    [DataRow("t", 0.25, 0.25)]
    [DataRow("-2^2+4.25", 0.5, 0.25)]
    [DataRow("2^-2", 0.5, 0.25)]
    [DataRow("2^3^2/1024", 0.5, 0.5)]
    [DataRow("1e-2+0.24", 0.5, 0.25)]
    [DataRow("sin(pi*x)^2", 0.5, 1d)]
    [DataRow("log(e)/2", 0.5, 0.5)]
    [DataRow("log10(100)/4", 0.5, 0.5)]
    [DataRow("sqrt(0.25)", 0.5, 0.5)]
    [DataRow("min(0.8,max(0.2,x))", 0.9, 0.8)]
    [DataRow("clamp(2*x,0.2,0.8)", 0.1, 0.2)]
    [DataRow("smoothstep(x)", 0.5, 0.5)]
    [DataRow("step(0.5,x)", 0.49, 0d)]
    [DataRow("step(0.5,x)", 0.5, 1d)]
    [DataRow("abs(-x)+floor(0.9)+ceil(0.1)/4", 0.25, 0.5)]
    [DataRow("pow(x,2)+(5%2)/4", 0.5, 0.5)]
    [DataRow("2*x-0.5", 0d, -0.5d)]
    [DataRow("2*x-0.5", 1d, 1.5d)]
    public void Equations_RespectPrecedenceFunctionsAndClamping(string expression, double x, double expected) =>
        Assert.AreEqual(expected, new EffectCurve { Equation = expression }.Compile().At(x), 1e-10);

    [TestMethod]
    public void EquationVariables_AreSnapshottedAndUseInvariantCulture()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var shape = new EffectCurve { Equation = "0.1*a+0.1*b+0.1*c+0.1*d+0.1*f+0.1*p", A = 1, B = 2, C = 1, D = 2, Cycles = 2, Phase = 1 };
            Assert.AreEqual(0.9, shape.Compile().At(0.4), 1e-12);
            var sine = new EffectCurve { Equation = "0.5+0.5*sin(2*pi*(f*x+p))", Cycles = 2, Phase = 0.25 }.Compile();
            Assert.AreEqual(1, sine.At(0), 1e-12); Assert.AreEqual(0, sine.At(0.25), 1e-12);
            Assert.IsTrue(shape.Summary.Contains("f=2", StringComparison.Ordinal));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("1/x")]
    [DataRow("1/(x-0.5)")]
    [DataRow("log(x)")]
    [DataRow("sqrt(-x)")]
    [DataRow("tan(pi*x)")]
    [DataRow("exp(1000)")]
    [DataRow("clamp(1/0,0,1)")]
    [DataRow("min(1,sqrt(-1))")]
    [DataRow("max(0,log(-1))")]
    [DataRow("clamp(x,1,0)")]
    [DataRow("sin(x,1)")]
    [DataRow("unknown(x)")]
    [DataRow("2x")]
    [DataRow("x=1")]
    [DataRow("x; System.IO.File.Delete(\"x\")")]
    [DataRow("NaN")]
    [DataRow("Infinity")]
    [DataRow("1e999")]
    [DataRow("sin()")]
    public void InvalidOrExecutableInput_IsRejected(string equation) =>
        Assert.ThrowsExactly<ArgumentException>(() => new EffectCurve { Equation = equation }.Compile());

    [TestMethod]
    public void ComplexityAndParameterLimits_AreEnforced()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new EffectCurve { Equation = new string('x', 1025) }.Compile());
        Assert.ThrowsExactly<ArgumentException>(() => new EffectCurve { Equation = new string('(', 40) + "x" + new string(')', 40) }.Compile());
        Assert.ThrowsExactly<ArgumentException>(() => new EffectCurve { Equation = string.Join('+', Enumerable.Repeat("x", 140)) }.Compile());
        Assert.ThrowsExactly<ArgumentException>(() => new EffectCurve { Cycles = double.NaN }.Compile());
        Assert.ThrowsExactly<ArgumentException>(() => new EffectCurve { A = double.PositiveInfinity }.Compile());
        Assert.ThrowsExactly<ArgumentException>(() => new EffectCurve { Cycles = 1001 }.Compile());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new EffectCurve().Compile().At(double.NaN));
        Assert.AreEqual(0d, new EffectCurve { Equation = "0" }.Compile().ConstantValue);
        Assert.IsNull(new EffectCurve { Equation = "sin(4096*pi*x)^2" }.Compile().ConstantValue,
            "Sampling must never misclassify a fast oscillator as a constant curve.");
    }

    [TestMethod]
    public void Checkpoints_InterpolateExactlyAndSupportSmoothAndHold()
    {
        var shape = new EffectCurve { Mode = EffectCurveMode.Checkpoints, Points = [new(0, 0), new(0.5, 1), new(1, 0)] };
        Assert.AreEqual(0.5, shape.Compile().At(0.25), 1e-12);
        Assert.AreEqual(0.15625, (shape with { Interpolation = EffectCurveInterpolation.Smooth }).Compile().At(0.125), 1e-12);
        var hold = (shape with { Interpolation = EffectCurveInterpolation.Hold }).Compile();
        Assert.AreEqual(0d, hold.At(0.49)); Assert.AreEqual(1d, hold.At(0.5)); Assert.AreEqual(0d, hold.At(1));
        EffectCurvePoint[] mutable = [new(0, 0), new(1, 1)]; var compiled = (shape with { Points = mutable }).Compile(); mutable[1] = new(1, 0);
        Assert.AreEqual(1d, compiled.At(1));
        Assert.AreEqual(0d, shape.Compile().At(-1)); Assert.AreEqual(0d, shape.Compile().At(2));
    }

    [TestMethod]
    public void InvalidCheckpointTables_AreRejected()
    {
        EffectCurvePoint[][] invalid = [[], [new(0, 0)], [new(0.1, 0), new(1, 1)], [new(0, 0), new(0.9, 1)],
            [new(0, 0), new(0.5, 1), new(0.5, 0), new(1, 1)], [new(0, 0), new(0.8, 1), new(0.2, 0), new(1, 1)],
            [new(0, 0), new(1, double.NaN)], [new(0, 0), new(1, double.PositiveInfinity)]];
        foreach (var points in invalid) Assert.ThrowsExactly<ArgumentException>(() => new EffectCurve { Mode = EffectCurveMode.Checkpoints, Points = points }.Compile());
    }

    [TestMethod]
    public void Settings_DeepCopyEqualityValidationAndLegacyJsonRoundTrip()
    {
        AudioEffectSettings original = AudioEffectCatalog.Get(AudioEffectKind.PitchShift).CreateDefaults().With(("Semitones", 12)) with
        { Automation = new() { Parameters = new() { ["Semitones"] = new() { StartValue = 0, Curve = new() { Equation = "x^a", A = 2 } } } } };
        original.Validate(); AudioEffectSettings copy = original.Copy(); Assert.IsTrue(copy.SoundEquals(original));
        Assert.AreNotSame(original.Automation, copy.Automation); Assert.AreNotSame(original.Automation!.Parameters, copy.Automation!.Parameters);
        Assert.AreNotSame(original.Automation.Parameters["Semitones"].Curve.Points, copy.Automation.Parameters["Semitones"].Curve.Points);
        copy.Automation.Parameters["Semitones"] = copy.Automation.Parameters["Semitones"] with { Enabled = false }; Assert.IsFalse(copy.SoundEquals(original));
        var roundTrip = JsonSerializer.Deserialize<AudioEffectSettings>(JsonSerializer.Serialize(original))!; roundTrip.Validate(); Assert.IsTrue(original.SoundEquals(roundTrip));
        var legacy = JsonSerializer.Deserialize<AudioEffectSettings>("{\"Effect\":1,\"Parameters\":{\"Semitones\":12,\"Method\":0,\"OutputGainDb\":0}}")!;
        legacy.Validate(); Assert.IsNull(legacy.Automation);
        var typed = new AutoTuneSettings { Automation = original.Automation with { Parameters = new() { [nameof(AutoTuneSettings.IntensityPercent)] = new() { StartValue = 0 } } } };
        typed.Validate(); Assert.IsTrue(typed.SoundEquals(typed.Copy()));
        Assert.IsFalse(typed.SoundEquals(typed with { Automation = null }));
    }

    [TestMethod]
    public void UnsafeRangesAndStructuralAutomation_AreRejected()
    {
        Assert.AreEqual(35, AudioEffectCatalog.All.Count(effect => effect.Kind == AudioEffectKind.AutoTune || EffectCurveTargets.ForEffect(effect.Kind).Count > 0));
        AudioEffectSettings gain = AudioEffectCatalog.Get(AudioEffectKind.Gain).CreateDefaults();
        Assert.ThrowsExactly<ArgumentException>(() => (gain with { Automation = new() { Parameters = new() { ["GainDb"] = new() { StartValue = -100 } } } }).Validate());
        Assert.ThrowsExactly<ArgumentException>(() => (gain with { Automation = new() { Parameters = new() { ["GainDb"] = new() { StartValue = 0, Scale = EffectCurveValueScale.Logarithmic } } } }).Validate());
        AudioEffectSettings reverb = AudioEffectCatalog.Get(AudioEffectKind.RoomReverb).CreateDefaults();
        Assert.ThrowsExactly<ArgumentException>(() => (reverb with { Automation = new() { Parameters = new() { ["TailSeconds"] = new() { StartValue = 0 } } } }).Validate());
        foreach (AudioEffectKind immediate in new[] { AudioEffectKind.Reverse, AudioEffectKind.ConvertToMono })
        {
            Assert.AreEqual(0, EffectCurveTargets.ForEffect(immediate).Count);
            Assert.ThrowsExactly<ArgumentException>(() => (AudioEffectCatalog.Get(immediate).CreateDefaults() with
            { Automation = new() { Parameters = new() { [EffectCurveTargets.Amount] = new() { StartValue = 0 } } } }).Validate());
        }
        Assert.ThrowsExactly<ArgumentException>(() => new EffectCurveTiming { SilenceThresholdDb = -1 }.Validate());
        var noise = AudioEffectCatalog.Get(AudioEffectKind.NoiseReduction).CreateDefaults() with
            { Automation = new() { Parameters = new() { ["NoiseFloorDb"] = new() { StartValue = -90 } } } };
        Assert.ThrowsExactly<ArgumentException>(noise.Validate); noise.With(("Profile", 2)).Validate();
    }

    [TestMethod]
    public void ParameterMapping_IsLinearLogarithmicOrSpeedInterpolatedAndTracksTheTarget()
    {
        var settings = AudioEffectCatalog.Get(AudioEffectKind.PitchShift).CreateDefaults().With(("Semitones", 12));
        var evaluator = Evaluator(settings, "Semitones", new() { StartValue = 0 });
        Assert.AreEqual(0d, evaluator.Value("Semitones", 0)); Assert.AreEqual(6d, evaluator.Value("Semitones", 50)); Assert.AreEqual(12d, evaluator.Value("Semitones", 100));
        var frequency = AudioEffectCatalog.Get(AudioEffectKind.SingleDelay).CreateDefaults().With(("LowPassHz", 10000));
        var logarithmic = Evaluator(frequency, "LowPassHz", new() { StartValue = 100, Scale = EffectCurveValueScale.Logarithmic });
        Assert.AreEqual(1000d, logarithmic.Value("LowPassHz", 50), 1e-8);
        var stretch = AudioEffectCatalog.Get(AudioEffectKind.TimeStretch).CreateDefaults().With(("Factor", 0.5));
        var speed = Evaluator(stretch, "Factor", new() { StartValue = 1 });
        Assert.AreEqual(2d / 3, speed.Value("Factor", 50), 1e-12); // speed 1× -> 2×, midpoint 1.5×, NOT factor 0.75
        var bits = AudioEffectCatalog.Get(AudioEffectKind.Bitcrusher).CreateDefaults();
        Assert.AreEqual(16d, Evaluator(bits, "Bits", new() { StartValue = 24 }).Value("Bits", 50));
    }

    [TestMethod]
    public void Clock_RestartsPerSegmentOrSharesTimeWithOrWithoutGaps()
    {
        float[] samples = Enumerable.Repeat(0.2f, 20).ToArray(); var scope = new EffectCurveRenderScope([new(2, 6), new(12, 18)]);
        var restart = new EffectCurveClock(samples, 1, 1000, new(), scope);
        Assert.AreEqual(0d, restart.PositionAt(2)); Assert.AreEqual(1d, restart.PositionAt(5)); Assert.AreEqual(1d, restart.PositionAt(10));
        Assert.AreEqual(0d, restart.PositionAt(12)); Assert.AreEqual(1d, restart.PositionAt(17));
        var shared = new EffectCurveClock(samples, 1, 1000, new() { Scope = EffectCurveScope.AcrossSelection }, scope);
        Assert.AreEqual(12d / 19, shared.PositionAt(12), 1e-12);
        var compressed = new EffectCurveClock(samples, 1, 1000, new() { Scope = EffectCurveScope.AcrossSelection, IncludeGaps = false }, scope);
        Assert.AreEqual(4d / 9, compressed.PositionAt(12), 1e-12); Assert.AreEqual(1d, compressed.PositionAt(17));
    }

    [TestMethod]
    public void Clock_SkipsOnlySustainedLinkedSilenceAndHandlesSingleFrameOrAllSilence()
    {
        float[] samples = [0.2f, 0, 0.2f, 0, 0, 0, 0, 0.2f, 0.2f, 0.2f];
        var timing = new EffectCurveTiming { Scope = EffectCurveScope.AcrossSelection, IncludeSilence = false, MinimumSilenceMs = 3 };
        var spans = EffectCurveClock.FindAudibleSpans(samples, 1, 1000, timing);
        CollectionAssert.AreEqual(new[] { new EffectCurveSpan(0, 3), new EffectCurveSpan(7, 10) }, spans);
        var clock = new EffectCurveClock(samples, 1, 1000, timing);
        Assert.AreEqual(0.6, clock.PositionAt(4), 1e-12); Assert.AreEqual(0.6, clock.PositionAt(7), 1e-12); Assert.AreEqual(1d, clock.PositionAt(9));
        Assert.AreEqual(0d, new EffectCurveClock(new float[10], 1, 1000, timing).PositionAt(5));
        Assert.AreEqual(0d, new EffectCurveClock(new float[] { 1 }, 1, 1000, timing).PositionAt(0));
        float[] stereo = [0, 1, 0, 1, 0, 1];
        CollectionAssert.AreEqual(new[] { new EffectCurveSpan(0, 3) }, EffectCurveClock.FindAudibleSpans(stereo, 2, 1000, timing));
    }

    [TestMethod]
    public void TimeMap_IntegratesSpeedRatherThanAveragingEndpointsAndIsMonotonic()
    {
        var map = new EffectCurveTimeMap(10000, 2, frame => 1 / (1 + frame / 9999d), default);
        Assert.IsNull(map.ConstantFactor);
        Assert.AreEqual(10000 * Math.Log(2), map.OutputFrames, 0.3);
        double previous = -1;
        for (int i = 0; i < (int)map.OutputFrames; i++) { double next = map.SourceAt(i); Assert.IsGreaterThan(previous, next); previous = next; }
        Assert.AreEqual(10000d, map.SourceAt(map.OutputFrames), 1e-8);
        Assert.IsTrue(new EffectCurveTimeMap(20, 1, _ => 1, default).IsIdentity);
        Assert.AreEqual(2d, new EffectCurveTimeMap(20, 1, _ => 2, default).ConstantFactor);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new EffectCurveTimeMap(1_000_000, 64, _ => 8, default));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => new EffectCurveTimeMap(20000, 1, _ => 1, cts.Token));
    }

    private static EffectAutomationEvaluator Evaluator(AudioEffectSettings settings, string key, EffectParameterCurve binding) =>
        new(new() { Parameters = new() { [key] = binding } }, EffectCurveTargets.ForEffect(settings.Effect), name => EffectCurveTargets.BaseValue(settings, name),
            Enumerable.Repeat(0.1f, 101).ToArray(), 1, 8000, null, default);
}
