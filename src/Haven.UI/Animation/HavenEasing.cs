using System.Globalization;
using System.Text.RegularExpressions;

namespace Haven.UI;

/// <summary>Framework-neutral easing parser/evaluator shared by transitions and keyframes.</summary>
public static partial class HavenEasing
{
    public static void Validate(string value) => _ = Parse(value);

    public static double Evaluate(double progress, string value)
    {
        var easing = Parse(value);
        var t = Math.Clamp(progress, 0d, 1d);
        return easing.Name switch
        {
            "linear" => t,
            "easein" => t * t,
            "easeout" => 1d - Math.Pow(1d - t, 2d),
            "easeinout" => t < .5d ? 2d * t * t : 1d - Math.Pow(-2d * t + 2d, 2d) / 2d,
            "spring" => Math.Clamp(1d - Math.Exp(-7d * t) * Math.Cos(11d * t), 0d, 1.08d),
            "stepstart" => t <= 0 ? 0 : 1,
            "stepend" => t < 1 ? 0 : 1,
            "cubicbezier" => CubicBezier(t, easing.Parameters[0], easing.Parameters[1], easing.Parameters[2], easing.Parameters[3]),
            _ => throw new InvalidOperationException($"Unsupported Haven easing '{value}'.")
        };
    }

    private static ParsedEasing Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        if (normalized is "linear" or "easein" or "easeout" or "easeinout" or "spring" or "stepstart" or "stepend")
            return new ParsedEasing(normalized, []);

        var match = CubicBezierRegex().Match(normalized);
        if (!match.Success) throw new FormatException($"Unknown Haven easing '{value}'.");
        var parameters = match.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries)
            .Select(part => double.Parse(part, NumberStyles.Float, CultureInfo.InvariantCulture))
            .ToArray();
        if (parameters.Length != 4 || parameters.Any(number => !double.IsFinite(number)) || parameters[0] is < 0 or > 1 || parameters[2] is < 0 or > 1)
            throw new FormatException($"CubicBezier easing '{value}' requires four finite values with x1/x2 between 0 and 1.");
        return new ParsedEasing("cubicbezier", parameters);
    }

    private static double CubicBezier(double x, double x1, double y1, double x2, double y2)
    {
        static double Sample(double t, double a1, double a2) =>
            3d * (1d - t) * (1d - t) * t * a1 + 3d * (1d - t) * t * t * a2 + t * t * t;
        static double Slope(double t, double a1, double a2) =>
            3d * (1d - t) * (1d - t) * a1 + 6d * (1d - t) * t * (a2 - a1) + 3d * t * t * (1d - a2);

        var t = x;
        for (var iteration = 0; iteration < 8; iteration++)
        {
            var error = Sample(t, x1, x2) - x;
            var slope = Slope(t, x1, x2);
            if (Math.Abs(error) < .000001d || Math.Abs(slope) < .000001d) break;
            t = Math.Clamp(t - error / slope, 0d, 1d);
        }
        return Math.Clamp(Sample(t, y1, y2), 0d, 1d);
    }

    private sealed record ParsedEasing(string Name, IReadOnlyList<double> Parameters);

    [GeneratedRegex(@"^cubicbezier\(([^)]+)\)$", RegexOptions.CultureInvariant)]
    private static partial Regex CubicBezierRegex();
}
