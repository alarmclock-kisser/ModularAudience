using System.Globalization;

namespace ModularAudience.Audio.Effects.Curves;

/// <summary>Small arithmetic parser, never a scripting engine. No reflection, I/O, assignments or executable user code.</summary>
internal sealed class EffectCurveExpression
{
    private readonly string text;
    private readonly EffectCurve curve;
    private int position, nodes, depth;
    private bool usesTime;
    private EffectCurveExpression(string text, EffectCurve curve) { this.text = text; this.curve = curve; }

    internal static Func<double, double> Parse(string equation, EffectCurve curve, out bool usesTime)
    {
        if (string.IsNullOrWhiteSpace(equation) || equation.Length > 1024) throw new ArgumentException("Enter an equation of 1–1024 characters.");
        var parser = new EffectCurveExpression(equation, curve);
        Func<double, double> result = parser.Sum(); parser.Space();
        if (parser.position != equation.Length) throw parser.Error("Unexpected input");
        usesTime = parser.usesTime;
        return result;
    }

    private Func<double, double> Sum()
    {
        if (++this.depth > 32) throw this.Error("The equation is nested too deeply");
        try
        {
            var left = this.Product();
            while (true)
            {
                if (this.Take('+')) { var a = left; var b = this.Product(); left = this.Node(x => a(x) + b(x)); }
                else if (this.Take('-')) { var a = left; var b = this.Product(); left = this.Node(x => a(x) - b(x)); }
                else return left;
            }
        }
        finally { this.depth--; }
    }

    private Func<double, double> Product()
    {
        var left = this.Unary();
        while (true)
        {
            if (this.Take('*')) { var a = left; var b = this.Unary(); left = this.Node(x => a(x) * b(x)); }
            else if (this.Take('/')) { var a = left; var b = this.Unary(); left = this.Node(x => a(x) / b(x)); }
            else if (this.Take('%')) { var a = left; var b = this.Unary(); left = this.Node(x => a(x) % b(x)); }
            else return left;
        }
    }

    private Func<double, double> Unary()
    {
        if (++this.depth > 32) throw this.Error("The equation is nested too deeply");
        try
        {
            if (this.Take('+')) return this.Unary();
            if (this.Take('-')) { var value = this.Unary(); return this.Node(x => -value(x)); }
            var left = this.Primary();
            if (this.Take('^')) { var right = this.Unary(); return this.Node(x => Math.Pow(left(x), right(x))); }
            return left;
        }
        finally { this.depth--; }
    }

    private Func<double, double> Primary()
    {
        this.Space();
        if (this.Take('(')) { var value = this.Sum(); this.Expect(')'); return value; }
        int start = this.position;
        if (this.position < this.text.Length && (char.IsAsciiDigit(this.text[this.position]) || this.text[this.position] == '.'))
        {
            while (this.position < this.text.Length && (char.IsAsciiDigit(this.text[this.position]) || this.text[this.position] == '.')) this.position++;
            if (this.position < this.text.Length && this.text[this.position] is 'e' or 'E')
            {
                this.position++;
                if (this.position < this.text.Length && this.text[this.position] is '+' or '-') this.position++;
                while (this.position < this.text.Length && char.IsAsciiDigit(this.text[this.position])) this.position++;
            }
            if (!double.TryParse(this.text[start..this.position], NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number))
                throw this.Error("Invalid number (use a decimal point)");
            return this.Node(_ => number);
        }
        while (this.position < this.text.Length && (char.IsAsciiLetter(this.text[this.position]) || char.IsAsciiDigit(this.text[this.position]))) this.position++;
        if (start == this.position) throw this.Error("Expected a number, variable or function");
        string name = this.text[start..this.position].ToLowerInvariant();
        if (!this.Take('('))
        {
            if (name is "x" or "t") { this.usesTime = true; return this.Node(x => x); }
            double constant = name switch
            {
                "pi" => Math.PI, "e" => Math.E, "f" => this.curve.Cycles, "p" => this.curve.Phase,
                "a" => this.curve.A, "b" => this.curve.B, "c" => this.curve.C, "d" => this.curve.D,
                _ => throw this.Error("Unknown variable '" + name + "'")
            };
            return this.Node(_ => constant);
        }
        var args = new List<Func<double, double>> { this.Sum() };
        while (this.Take(',')) { if (args.Count >= 3) throw this.Error("Too many function arguments"); args.Add(this.Sum()); }
        this.Expect(')');
        Func<double, double>? unary = name switch
        {
            "sin" => Math.Sin, "cos" => Math.Cos, "tan" => value => Math.Abs(Math.Cos(value)) < 1e-12 ? double.NaN : Math.Tan(value),
            "asin" => Math.Asin, "acos" => Math.Acos, "atan" => Math.Atan,
            "sinh" => Math.Sinh, "cosh" => Math.Cosh, "tanh" => Math.Tanh,
            "abs" => Math.Abs, "sqrt" => Math.Sqrt, "exp" => Math.Exp, "ln" or "log" => Math.Log,
            "log10" => Math.Log10, "floor" => Math.Floor, "ceil" => Math.Ceiling, "round" => Math.Round,
            "sign" => value => double.IsNaN(value) ? double.NaN : Math.Sign(value),
            "smoothstep" => value => { double t = Math.Clamp(value, 0, 1); return t * t * (3 - 2 * t); },
            _ => null
        };
        if (unary is not null && args.Count == 1) return this.Node(x => unary(args[0](x)));
        if (args.Count == 2)
        {
            Func<double, double, double>? binary = name switch
            {
                "min" => Math.Min, "max" => Math.Max, "pow" => Math.Pow,
                "step" => (edge, value) => value < edge ? 0 : 1, _ => null
            };
            if (binary is not null) return this.Node(x => binary(args[0](x), args[1](x)));
        }
        if (name == "clamp" && args.Count == 3) return this.Node(x =>
        {
            double value = args[0](x), low = args[1](x), high = args[2](x);
            return low <= high ? Math.Clamp(value, low, high) : double.NaN;
        });
        throw this.Error("Unknown function or wrong number of arguments: " + name);
    }

    private Func<double, double> Node(Func<double, double> node)
    {
        if (++this.nodes > 256) throw this.Error("The equation is too complex (maximum 256 operations)");
        // Do not allow an invalid subexpression to be hidden by min/max/clamp.
        return x => { double value = node(x); if (!double.IsFinite(value)) throw this.Error("Undefined arithmetic in the curve"); return value; };
    }
    private void Space() { while (this.position < this.text.Length && char.IsWhiteSpace(this.text[this.position])) this.position++; }
    private bool Take(char value) { this.Space(); if (this.position >= this.text.Length || this.text[this.position] != value) return false; this.position++; return true; }
    private void Expect(char value) { if (!this.Take(value)) throw this.Error("Expected '" + value + "'"); }
    private ArgumentException Error(string message) => new($"{message} (position {this.position + 1}).");
}
