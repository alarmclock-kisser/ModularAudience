using System.Globalization;
using System.Text.Json.Serialization;

namespace ModularAudience.Audio.Effects.Curves;

public enum EffectCurveMode { Equation, Checkpoints }
public enum EffectCurveInterpolation { Linear, Smooth, Hold }
public sealed record EffectCurvePoint(double X, double Y);

/// <summary>A portable, effect-independent shape. Both axes are normalized; finite overshoot is clamped to 0..1.</summary>
public sealed record EffectCurve
{
    public EffectCurveMode Mode { get; init; }
    public string Equation { get; init; } = "x";
    public double Cycles { get; init; } = 1;
    public double Phase { get; init; }
    public double A { get; init; } = 3;
    public double B { get; init; } = 1;
    public double C { get; init; }
    public double D { get; init; }
    public EffectCurveInterpolation Interpolation { get; init; }
    public IReadOnlyList<EffectCurvePoint> Points { get; init; } = [new(0, 0), new(1, 1)];

    public EffectCurve Copy() => this with { Points = this.Points.Select(point => point with { }).ToArray() };

    public bool SoundEquals(EffectCurve? other) => other is not null && this.Mode == other.Mode &&
        (this.Mode == EffectCurveMode.Equation
            ? this.Equation == other.Equation && this.Cycles == other.Cycles && this.Phase == other.Phase &&
                this.A == other.A && this.B == other.B && this.C == other.C && this.D == other.D
            : this.Interpolation == other.Interpolation && this.Points.SequenceEqual(other.Points));

    [JsonIgnore]
    public string Summary => this.Mode == EffectCurveMode.Equation
        ? $"y = {this.Equation} [f={this.Cycles.ToString("G5", CultureInfo.InvariantCulture)}, p={this.Phase.ToString("G5", CultureInfo.InvariantCulture)}, a={this.A.ToString("G5", CultureInfo.InvariantCulture)}, b={this.B.ToString("G5", CultureInfo.InvariantCulture)}, c={this.C.ToString("G5", CultureInfo.InvariantCulture)}, d={this.D.ToString("G5", CultureInfo.InvariantCulture)}]"
        : $"{this.Interpolation} checkpoints: " + string.Join("; ", this.Points.Select(point =>
            $"({point.X.ToString("G4", CultureInfo.InvariantCulture)}, {point.Y.ToString("G4", CultureInfo.InvariantCulture)})"));

    public CompiledEffectCurve Compile()
    {
        if (!Enum.IsDefined(this.Mode) || !Enum.IsDefined(this.Interpolation)) throw new ArgumentException("Invalid curve mode or interpolation.");
        if (!double.IsFinite(this.Cycles) || this.Cycles is < 0 or > 1000 || !double.IsFinite(this.Phase) || Math.Abs(this.Phase) > 1000 ||
            new[] { this.A, this.B, this.C, this.D }.Any(value => !double.IsFinite(value) || Math.Abs(value) > 10000))
            throw new ArgumentException("Curve parameters must be finite (cycles 0–1000, phase ±1000, a/b/c/d ±10000).");
        if (this.Points is null || this.Points.Count is < 2 or > 512 || this.Points.Any(point => point is null ||
                !double.IsFinite(point.X) || !double.IsFinite(point.Y) || point.X is < 0 or > 1 || Math.Abs(point.Y) > 1000000) ||
            this.Points[0].X != 0 || this.Points[^1].X != 1 || this.Points.Zip(this.Points.Skip(1)).Any(pair => pair.First.X >= pair.Second.X))
            throw new ArgumentException("Use 2–512 finite checkpoints, strictly increasing x values, starting at x=0 and ending at x=1.");
        Func<double, double> evaluate;
        bool constant;
        if (this.Mode == EffectCurveMode.Equation)
        {
            evaluate = EffectCurveExpression.Parse(this.Equation, this, out bool usesTime); constant = !usesTime;
        }
        else
        {
            EffectCurvePoint[] points = this.Points.Select(point => point with { }).ToArray();
            double first = Math.Clamp(points[0].Y, 0, 1);
            constant = points.All(point => Math.Clamp(point.Y, 0, 1) == first);
            EffectCurveInterpolation interpolation = this.Interpolation;
            evaluate = x =>
            {
                if (x >= 1) return points[^1].Y;
                int low = 0, high = points.Length - 1;
                while (low + 1 < high) { int mid = (low + high) / 2; if (points[mid].X <= x) low = mid; else high = mid; }
                double t = (x - points[low].X) / (points[high].X - points[low].X);
                t = interpolation switch { EffectCurveInterpolation.Smooth => t * t * (3 - 2 * t), EffectCurveInterpolation.Hold => 0, _ => t };
                return points[low].Y + (points[high].Y - points[low].Y) * t;
            };
        }
        var compiled = new CompiledEffectCurve(evaluate, constant);
        // Catch domain errors before enabling/saving. Every render evaluation is checked as well.
        for (int i = 0; i <= 4096; i++) compiled.At(i / 4096d);
        return compiled;
    }
}

public sealed class CompiledEffectCurve
{
    private readonly Func<double, double> evaluate;
    public double? ConstantValue { get; }
    internal CompiledEffectCurve(Func<double, double> evaluate, bool constant)
    {
        this.evaluate = evaluate; this.ConstantValue = constant ? this.At(0) : null;
    }
    public double At(double x)
    {
        if (!double.IsFinite(x)) throw new ArgumentOutOfRangeException(nameof(x));
        double value = this.evaluate(Math.Clamp(x, 0, 1));
        if (!double.IsFinite(value) || Math.Abs(value) > 1e12)
            throw new ArgumentException($"The curve is undefined or excessively large near x={x.ToString("G8", CultureInfo.InvariantCulture)}. Avoid poles, division by zero and invalid logarithms/roots.");
        return Math.Clamp(value, 0, 1);
    }
}
