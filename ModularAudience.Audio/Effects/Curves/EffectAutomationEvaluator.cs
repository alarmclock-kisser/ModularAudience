namespace ModularAudience.Audio.Effects.Curves;

/// <summary>Render-owned compiled curves and a single source-time clock. Never mutates a preset or DSP state.</summary>
internal sealed class EffectAutomationEvaluator
{
    private sealed record Entry(EffectParameterCurve Binding, CompiledEffectCurve Curve, EffectCurveTarget Target);
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly Func<string, double> readValue;
    private readonly EffectCurveClock? clock;

    internal EffectAutomationEvaluator(EffectAutomationSettings? settings, IReadOnlyList<EffectCurveTarget> targets,
        Func<string, double> readValue, ReadOnlySpan<float> source, int channels, int rate,
        EffectCurveRenderScope? scope, CancellationToken token)
    {
        this.readValue = readValue;
        if (settings is null || !settings.HasEnabledCurves) return;
        this.clock = new(source, channels, rate, settings.Timing, scope, token);
        foreach (var (key, binding) in settings.Parameters.Where(pair => pair.Value.Enabled))
            this.entries.Add(key, new(binding, binding.Curve.Compile(), targets.Single(target => target.Key == key)));
    }
    internal bool Has(string key) => this.entries.ContainsKey(key);
    internal bool IsConstant(string key, double value)
    {
        if (!this.entries.TryGetValue(key, out Entry? entry)) return this.readValue(key) == value;
        return (entry.Curve.ConstantValue is not null || entry.Binding.StartValue == (entry.Binding.EndValue ?? this.readValue(key))) && this.Value(key, 0) == value;
    }
    internal double Value(string key, double frame)
    {
        if (!this.entries.TryGetValue(key, out Entry? entry)) return this.readValue(key);
        double t = entry.Curve.At(this.clock!.PositionAt(frame)), start = entry.Binding.StartValue, end = entry.Binding.EndValue ?? this.readValue(key);
        double value = entry.Target.InterpolateSpeed ? 1 / (1 / start + (1 / end - 1 / start) * t) :
            entry.Binding.Scale == EffectCurveValueScale.Logarithmic ? Math.Exp(Math.Log(start) + (Math.Log(end) - Math.Log(start)) * t) : start + (end - start) * t;
        return Math.Clamp(entry.Target.Integral ? Math.Round(value) : value, entry.Target.Minimum, entry.Target.Maximum);
    }
    internal double Maximum(string key) => this.entries.TryGetValue(key, out Entry? entry)
        ? Math.Max(entry.Binding.StartValue, entry.Binding.EndValue ?? this.readValue(key)) : this.readValue(key);
    internal double Minimum(string key) => this.entries.TryGetValue(key, out Entry? entry)
        ? Math.Min(entry.Binding.StartValue, entry.Binding.EndValue ?? this.readValue(key)) : this.readValue(key);
}
