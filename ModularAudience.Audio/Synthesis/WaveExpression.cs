using System.Globalization;
using System.Linq.Expressions;

namespace ModularAudience.Audio.Synthesis;

/// <summary>Small arithmetic grammar compiled to a delegate; no scripting, reflection or external code.</summary>
public static class WaveExpression
{
    public static Func<double, double> Compile(string equation) => new Parser(equation).Compile();
    private sealed class Parser
    {
        private readonly string text;
        private int position, nodes, depth;
        private readonly ParameterExpression x = Expression.Parameter(typeof(double), "x");
        internal Parser(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > 65536) throw new FormatException("Enter an equation (maximum 65536 characters).");
            this.text = text.Replace("π", "pi").Replace("τ", "tau").Replace('×', '*').Replace('·', '*').Replace('÷', '/').Replace('−', '-');
        }
        internal Func<double, double> Compile()
        {
            Expression body = Sum(); White();
            if (position != text.Length) throw Error("Unexpected character");
            return Expression.Lambda<Func<double, double>>(body, x).Compile();
        }
        private FormatException Error(string message) => new($"{message} at character {position + 1}.");
        private void White() { while (position < text.Length && char.IsWhiteSpace(text[position])) position++; }
        private bool Take(char c) { White(); if (position < text.Length && text[position] == c) { position++; return true; } return false; }
        private Expression Sum()
        {
            if (++depth > 64) throw Error("Expression nesting is too deep");
            var value = Product();
            while (true)
            {
                if (Take('+')) value = Expression.Add(value, Product());
                else if (Take('-')) value = Expression.Subtract(value, Product());
                else break;
            }
            depth--; return value;
        }
        private Expression Product()
        {
            var value = Unary();
            while (true)
            {
                if (Take('*')) value = Expression.Multiply(value, Unary());
                else if (Take('/')) value = Expression.Divide(value, Unary());
                else if (Take('%')) value = Expression.Modulo(value, Unary());
                else
                {
                    White();
                    if (position < text.Length && (text[position] == '(' || char.IsLetter(text[position])))
                        value = Expression.Multiply(value, Unary()); // 2pi, 3sin(...), 2(x+1)
                    else break;
                }
            }
            return value;
        }
        private Expression Unary()
        {
            if (++depth > 64) throw Error("Expression nesting is too deep");
            try { return UnaryCore(); }
            finally { depth--; }
        }
        private Expression UnaryCore()
        {
            if (++nodes > 8192) throw Error("Expression is too complex");
            if (Take('+')) return Unary();
            if (Take('-')) return Expression.Negate(Unary());
            var value = Atom();
            if (Take('^')) value = Call(nameof(Math.Pow), value, Unary());
            return value;
        }
        private Expression Atom()
        {
            White();
            if (Take('(')) { var expression = Sum(); if (!Take(')')) throw Error("Expected ')' "); return expression; }
            if (position >= text.Length) throw Error("Expected a value");
            if (char.IsDigit(text[position]) || text[position] == '.')
            {
                int begin = position;
                while (position < text.Length && (char.IsDigit(text[position]) || text[position] == '.')) position++;
                if (position < text.Length && text[position] is 'e' or 'E')
                {
                    int exponent = position++;
                    if (position < text.Length && text[position] is '+' or '-') position++;
                    int digits = position;
                    while (position < text.Length && char.IsDigit(text[position])) position++;
                    if (digits == position) position = exponent;
                }
                if (!double.TryParse(text[begin..position], NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
                    throw Error("Invalid number (use '.' as decimal separator)");
                return Expression.Constant(value);
            }
            int start = position;
            while (position < text.Length && (char.IsLetterOrDigit(text[position]) || text[position] == '_')) position++;
            string name = text[start..position].ToLowerInvariant();
            if (name == "x" || name == "phase") return x;
            if (name is "pi" or "tau" or "e") return Expression.Constant(name == "pi" ? Math.PI : name == "tau" ? 2 * Math.PI : Math.E);
            if (!Take('(')) throw Error($"Unknown variable '{name}'");
            var args = new List<Expression> { Sum() };
            while (Take(',')) args.Add(Sum());
            if (!Take(')')) throw Error("Expected ')'");
            string? method = name switch
            {
                "sin" => "Sin", "cos" => "Cos", "tan" => "Tan", "tanh" => "Tanh", "asin" => "Asin", "acos" => "Acos", "atan" => "Atan",
                "abs" => "Abs", "sqrt" => "Sqrt", "exp" => "Exp", "log" or "ln" => "Log", "log10" => "Log10",
                "floor" => "Floor", "ceil" => "Ceiling", "round" => "Round", "min" => "Min", "max" => "Max", "pow" => "Pow", "sign" => "Sign", "clamp" => "Clamp", _ => null
            };
            if (method == null) throw Error($"Unknown function '{name}'");
            int expected = name is "min" or "max" or "pow" ? 2 : name == "clamp" ? 3 : 1;
            if (args.Count != expected) throw Error($"{name} expects {expected} argument(s)");
            return Call(method, args.ToArray());
        }
        private static Expression Call(string name, params Expression[] args)
        {
            var method = typeof(Math).GetMethod(name, Enumerable.Repeat(typeof(double), args.Length).ToArray())
                ?? throw new FormatException($"Unsupported function: {name}");
            Expression call = Expression.Call(method, args);
            return method.ReturnType == typeof(int) ? Expression.Convert(call, typeof(double)) : call;
        }
    }
}
