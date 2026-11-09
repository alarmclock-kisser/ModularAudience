using System.Text.Json.Serialization;

namespace ModularAudience.Audio.Effects.Curves;

public enum EffectCurveScope { PerSegment, AcrossSelection }
public enum EffectCurveValueScale { Linear, Logarithmic }

public sealed record EffectCurveTiming
{
    public EffectCurveScope Scope { get; init; } = EffectCurveScope.PerSegment;
    public bool IncludeGaps { get; init; } = true;
    public bool IncludeSilence { get; init; } = true;
    public double SilenceThresholdDb { get; init; } = -80;
    public double MinimumSilenceMs { get; init; } = 50;
    public void Validate()
    {
        if (!Enum.IsDefined(this.Scope) || !double.IsFinite(this.SilenceThresholdDb) || this.SilenceThresholdDb is < -120 or > -12 ||
            !double.IsFinite(this.MinimumSilenceMs) || this.MinimumSilenceMs is < 1 or > 10000)
            throw new ArgumentException("Invalid curve timing. Silence threshold: −120 to −12 dBFS; minimum silence: 1–10000 ms.");
    }
}

public sealed record EffectParameterCurve
{
    public bool Enabled { get; init; } = true;
    public EffectCurve Curve { get; init; } = new();
    public double StartValue { get; init; }
    /// <summary>Null follows the effect's current parameter knob (including the Target BPM helper).</summary>
    public double? EndValue { get; init; }
    public EffectCurveValueScale Scale { get; init; }
    public EffectParameterCurve Copy() => this with { Curve = this.Curve.Copy() };
    public bool SoundEquals(EffectParameterCurve? other) => other is not null && this.Enabled == other.Enabled &&
        this.StartValue == other.StartValue && this.EndValue == other.EndValue && this.Scale == other.Scale && this.Curve.SoundEquals(other.Curve);
}

/// <summary>Sound settings, persisted with an effect; shape-only presets intentionally exclude ranges and timing.</summary>
public sealed record EffectAutomationSettings
{
    public Dictionary<string, EffectParameterCurve> Parameters { get; init; } = new(StringComparer.Ordinal);
    public EffectCurveTiming Timing { get; init; } = new();
    [JsonIgnore] public bool HasEnabledCurves => this.Parameters.Values.Any(binding => binding.Enabled);
    public EffectAutomationSettings Copy() => this with
    {
        Parameters = this.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value.Copy(), StringComparer.Ordinal),
        Timing = this.Timing with { }
    };
    public bool SoundEquals(EffectAutomationSettings? other) => other is not null && this.Timing == other.Timing &&
        this.Parameters.Count == other.Parameters.Count && this.Parameters.All(pair =>
            other.Parameters.TryGetValue(pair.Key, out EffectParameterCurve? value) && pair.Value.SoundEquals(value));

    public void Validate(IReadOnlyList<EffectCurveTarget> targets, Func<string, double> readValue)
    {
        if (this.Parameters is null || this.Parameters.Count > 64 || this.Timing is null) throw new ArgumentException("Invalid effect automation settings.");
        this.Timing.Validate();
        foreach (var (key, binding) in this.Parameters)
        {
            EffectCurveTarget target = targets.FirstOrDefault(target => target.Key == key)
                ?? throw new ArgumentException("This option cannot be automated: " + key);
            if (binding is null || binding.Curve is null || !Enum.IsDefined(binding.Scale)) throw new ArgumentException("Invalid parameter curve: " + key);
            double end = binding.EndValue ?? readValue(key);
            if (!double.IsFinite(binding.StartValue) || !double.IsFinite(end) || binding.StartValue < target.Minimum || binding.StartValue > target.Maximum ||
                end < target.Minimum || end > target.Maximum || binding.Scale == EffectCurveValueScale.Logarithmic && (binding.StartValue <= 0 || end <= 0))
                throw new ArgumentException($"{target.Label}: curve range must be within {target.Minimum}–{target.Maximum}; logarithmic ranges must be positive.");
            if (target.InterpolateSpeed && binding.Scale != EffectCurveValueScale.Linear)
                throw new ArgumentException("Time Stretch curves interpolate speed linearly between their endpoints; use the equation to shape the transition.");
            binding.Curve.Compile();
        }
    }
}

public sealed record EffectCurveTarget(string Key, string Label, double Minimum, double Maximum, double NeutralValue,
    bool Integral = false, bool InterpolateSpeed = false);

public static class EffectCurveTargets
{
    public const string Amount = "EffectAmount";
    public const string FadeEnvelope = "FadeEnvelope";
    private static readonly HashSet<string> Structural = new(StringComparer.Ordinal)
        { "TailSeconds", "Overlap", "TapCount", "Voices", "Harmonics", "LookAheadMs", "ProfileMs", "LengthPercent" };

    public static IReadOnlyList<EffectCurveTarget> ForEffect(AudioEffectKind effect)
    {
        if (effect is AudioEffectKind.AutoTune or AudioEffectKind.Reverse or AudioEffectKind.ConvertToMono) return [];
        var targets = new List<EffectCurveTarget>();
        if (effect is not (AudioEffectKind.PitchShift or AudioEffectKind.TimeStretch or AudioEffectKind.VarispeedShift))
            targets.Add(new(Amount, "Effect amount (%)", 0, 100, 0));
        if (effect is AudioEffectKind.FadeIn or AudioEffectKind.FadeOut)
            targets.Add(new(FadeEnvelope, "Custom fade gain (replaces built-in fade)", 0, 1, effect == AudioEffectKind.FadeIn ? 0 : 1));
        foreach (EffectParameter parameter in AudioEffectCatalog.Get(effect).Parameters.Where(parameter =>
            parameter.Kind == EffectParameterKind.Number && !Structural.Contains(parameter.Key)))
        {
            double neutral = parameter.Key switch
            {
                "Semitones" or "GainDb" or "OutputGainDb" or "InputGainDb" or "MakeupDb" or "DriveDb" or "BiasPercent" or
                "WetPercent" or "DepthPercent" or "FeedbackPercent" or "ShimmerPercent" or "AmountPercent" or "ReductionDb" or "MaximumReductionDb" => 0,
                "Factor" or "Speed" or "Ratio" => 1, "Bits" => 24, "WidthPercent" => 100,
                "RateHz" when effect == AudioEffectKind.SampleRateReducer => 96000,
                _ => parameter.Minimum
            };
            targets.Add(new(parameter.Key, parameter.Key == "Factor" ? "Speed / Target BPM" : parameter.Label,
                parameter.Minimum, parameter.Maximum, Math.Clamp(neutral, parameter.Minimum, parameter.Maximum), parameter.Integral,
                InterpolateSpeed: parameter.Key == "Factor"));
        }
        return targets;
    }

    public static double BaseValue(AudioEffectSettings settings, string key) => key switch
    {
        Amount => 100, FadeEnvelope => settings.Effect == AudioEffectKind.FadeIn ? 1 : 0, _ => settings[key]
    };
}
