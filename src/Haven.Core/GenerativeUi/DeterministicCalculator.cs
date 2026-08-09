using System.Globalization;

namespace Haven.Core;

/// <summary>Small deterministic expression engine; it executes no script or platform code.</summary>
public static class DeterministicCalculator
{
    public static double Evaluate(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        if (expression.Length > 256) throw new InvalidOperationException("Expression is too long.");
        var parser = new Parser(expression);
        var result = parser.Parse();
        if (!double.IsFinite(result)) throw new InvalidOperationException("Result is not finite.");
        return result;
    }

    public static string Format(double value) =>
        value.ToString("G15", CultureInfo.InvariantCulture);

    private sealed class Parser(string source)
    {
        private int _position;

        public double Parse()
        {
            var value = Expression();
            SkipWhitespace();
            if (_position != source.Length) throw Error("Unexpected input");
            return value;
        }

        private double Expression()
        {
            var value = Term();
            while (true)
            {
                SkipWhitespace();
                if (Take('+')) value += Term();
                else if (Take('-')) value -= Term();
                else return value;
            }
        }

        private double Term()
        {
            var value = Power();
            while (true)
            {
                SkipWhitespace();
                if (Take('*')) value *= Power();
                else if (Take('/'))
                {
                    var divisor = Power();
                    if (divisor == 0) throw Error("Division by zero");
                    value /= divisor;
                }
                else return value;
            }
        }

        private double Power()
        {
            var value = Unary();
            SkipWhitespace();
            return Take('^') ? Math.Pow(value, Power()) : value;
        }

        private double Unary()
        {
            SkipWhitespace();
            if (Take('+')) return Unary();
            if (Take('-')) return -Unary();
            return Primary();
        }

        private double Primary()
        {
            SkipWhitespace();
            if (Take('('))
            {
                var value = Expression();
                SkipWhitespace();
                if (!Take(')')) throw Error("Missing closing parenthesis");
                return value;
            }

            if (_position < source.Length && (char.IsAsciiLetter(source[_position]) || source[_position] == 'π'))
            {
                var name = Identifier();
                if (name.Equals("pi", StringComparison.OrdinalIgnoreCase) || name == "π") return Math.PI;
                if (name.Equals("e", StringComparison.OrdinalIgnoreCase)) return Math.E;
                SkipWhitespace();
                if (!Take('(')) throw Error($"Unknown constant '{name}'");
                var first = Expression();
                SkipWhitespace();
                double? second = null;
                if (Take(',')) second = Expression();
                SkipWhitespace();
                if (!Take(')')) throw Error("Missing closing parenthesis");
                return ApplyFunction(name, first, second);
            }

            return Number();
        }

        private double Number()
        {
            SkipWhitespace();
            var start = _position;
            var hasDot = false;
            while (_position < source.Length)
            {
                var character = source[_position];
                if (char.IsAsciiDigit(character)) { _position++; continue; }
                if (character == '.' && !hasDot) { hasDot = true; _position++; continue; }
                break;
            }
            if (start == _position || !double.TryParse(source[start.._position], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                throw Error("Expected a number");
            return value;
        }

        private string Identifier()
        {
            var start = _position;
            while (_position < source.Length && (char.IsAsciiLetter(source[_position]) || source[_position] == 'π')) _position++;
            return source[start.._position];
        }

        private static double ApplyFunction(string name, double first, double? second) => name.ToLowerInvariant() switch
        {
            "sqrt" when second is null && first >= 0 => Math.Sqrt(first),
            "abs" when second is null => Math.Abs(first),
            "sin" when second is null => Math.Sin(first),
            "cos" when second is null => Math.Cos(first),
            "tan" when second is null => Math.Tan(first),
            "min" when second is not null => Math.Min(first, second.Value),
            "max" when second is not null => Math.Max(first, second.Value),
            _ => throw new InvalidOperationException($"Unsupported function or arguments: {name}.")
        };

        private void SkipWhitespace()
        {
            while (_position < source.Length && char.IsWhiteSpace(source[_position])) _position++;
        }

        private bool Take(char expected)
        {
            if (_position >= source.Length || source[_position] != expected) return false;
            _position++;
            return true;
        }

        private InvalidOperationException Error(string message) =>
            new($"{message} at position {_position + 1}.");
    }
}
