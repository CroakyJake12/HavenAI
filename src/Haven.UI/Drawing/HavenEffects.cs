using System.Globalization;

namespace Haven.UI;

/// <summary>
/// Parses the small, platform-neutral shadow vocabulary used by Haven markup.
/// The five-value form is: offset-x offset-y blur spread brush-token, with an
/// optional final opacity. Named presets keep repeated effects central.
/// </summary>
public static class HavenEffects
{
    private static readonly IReadOnlyDictionary<string, HavenShadow> NamedShadows =
        new Dictionary<string, HavenShadow>(StringComparer.OrdinalIgnoreCase)
        {
            ["Card"] = new(new HavenTokenBrush("Shadow"), 34, 0, 14, 0, .40),
            ["Popup"] = new(new HavenTokenBrush("Shadow"), 70, 0, 24, 0, .66),
            ["Composer"] = new(new HavenTokenBrush("Shadow"), 34, 0, 12, 0, .44),
            ["Toolbar"] = new(new HavenTokenBrush("Shadow"), 28, 0, 10, 0, .40),
            ["Floating"] = new(new HavenTokenBrush("Shadow"), 52, 0, 18, 0, .44)
        };

    public static bool TryResolveShadow(string? value, out HavenShadow? shadow)
    {
        shadow = null;
        if (string.IsNullOrWhiteSpace(value) || value.Equals("None", StringComparison.OrdinalIgnoreCase)) return false;
        if (NamedShadows.TryGetValue(value.Trim(), out var named))
        {
            shadow = named;
            return true;
        }

        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length is not (5 or 6))
            throw new FormatException($"Shadow '{value}' must be a named Haven shadow or 'offsetX offsetY blur spread brush [opacity]'.");
        var opacity = parts.Length == 6 ? ParseNumber(parts[5], "opacity") : 1d;
        if (opacity is < 0 or > 1) throw new FormatException("Shadow opacity must be between 0 and 1.");
        shadow = new HavenShadow(
            new HavenTokenBrush(parts[4]),
            Math.Max(0, ParseNumber(parts[2], "blur")),
            ParseNumber(parts[0], "offsetX"),
            ParseNumber(parts[1], "offsetY"),
            ParseNumber(parts[3], "spread"),
            opacity);
        return true;
    }

    private static double ParseNumber(string value, string field)
    {
        var normalized = value.EndsWith("px", StringComparison.OrdinalIgnoreCase) ? value[..^2] : value;
        if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed))
            return parsed;
        throw new FormatException($"Shadow {field} '{value}' is not a finite pixel value.");
    }
}
