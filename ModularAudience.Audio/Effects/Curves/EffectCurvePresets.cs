namespace ModularAudience.Audio.Effects.Curves;

public sealed record EffectCurvePresetDefinition(string Id, string Name, EffectCurve Curve);

/// <summary>Code-defined starting points. Persistence never modifies or replaces these definitions.</summary>
public static class EffectCurvePresets
{
    private static EffectCurvePresetDefinition P(string id, string name, string equation, double a = 3, double cycles = 1) =>
        new(id, name, new() { Equation = equation, A = a, Cycles = cycles });

    public static IReadOnlyList<EffectCurvePresetDefinition> All { get; } = Array.AsReadOnly(new[]
    {
        P("linear", "Linear rise", "x"), P("fall", "Linear fall", "1-x"),
        P("log", "Logarithmic rise", "log(1+a*x)/log(1+a)", 9),
        P("exponential", "Exponential rise", "(exp(a*x)-1)/(exp(a)-1)"),
        P("power", "Power / ease in", "x^a", 2), P("ease-out", "Power / ease out", "1-(1-x)^a", 2),
        P("smooth", "Smooth S-curve", "smoothstep(x)"),
        P("sine", "Sine oscillator", "0.5+0.5*sin(2*pi*(f*x+p))", cycles: 4),
        P("cosine", "Cosine oscillator", "0.5+0.5*cos(2*pi*(f*x+p))", cycles: 4),
        P("tangent", "Tangent rise (pole-free)", "0.5+tan((x-0.5)*a)/(2*tan(a/2))", 2),
        P("reciprocal", "Reciprocal decay (safe 1/x)", "1/(1+a*x)", 9),
        P("triangle", "Triangle oscillator", "1-abs(2*((f*x+p)-floor(f*x+p))-1)", cycles: 4),
        P("saw", "Sawtooth oscillator", "(f*x+p)-floor(f*x+p)", cycles: 4),
        P("square", "Square oscillator", "step(0,sin(2*pi*(f*x+p)))", cycles: 4),
        P("damped", "Decaying sine pulses", "exp(-a*x)*(0.5+0.5*cos(2*pi*(f*x+p)))", cycles: 6),
        P("bell", "Bell / swell", "sin(pi*x)^a", 2),
        P("constant", "Constant full amount", "1")
    });
}
