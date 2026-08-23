using System.Text.Json;

namespace Haven.Desktop;

public sealed record CanvasPenPresetPreference(
    string Id,
    string Name,
    string Tool,
    string Color,
    double Opacity,
    double Thickness,
    string Effect);

public sealed record CanvasPenPresetPreferenceState(
    int Version,
    IReadOnlyList<CanvasPenPresetPreference> CustomPresets);

public sealed partial class UserPreferencesService
{
    private const int CurrentCanvasPenPresetVersion = 1;

    private static IReadOnlyList<CanvasPenPresetPreference> BuiltInCanvasPenPresets { get; } =
    [
        new("builtin-blue", "Blue pen", "Pen", "#FF2F80ED", 1, 3, "Pressure"),
        new("builtin-black", "Black pen", "Pen", "#FF111111", 1, 3, "Pressure"),
        new("builtin-red", "Red pen", "Pen", "#FFE5484D", 1, 3, "Pressure"),
        new("builtin-highlighter", "Yellow highlighter", "Highlighter", "#FFFFC928", 0.28, 4, "Uniform")
    ];

    public int CanvasPenPresetVersion => CurrentCanvasPenPresetVersion;

    public bool CanvasPenPresetPreferencesWritable
    {
        get
        {
            var state = CanvasPresetState;
            return state.Version is > 0 and <= CurrentCanvasPenPresetVersion;
        }
    }

    public IReadOnlyList<CanvasPenPresetPreference> CanvasPenPresets
    {
        get
        {
            var state = CanvasPresetState;
            if (state.Version is <= 0 or > CurrentCanvasPenPresetVersion) return BuiltInCanvasPenPresets;
            var custom = (state.CustomPresets ?? [])
                .Select(value => NormaliseCanvasPreset(value, custom: true))
                .Where(value => value is not null)
                .Cast<CanvasPenPresetPreference>()
                .Where(value => BuiltInCanvasPenPresets.All(builtIn => !builtIn.Id.Equals(value.Id, StringComparison.OrdinalIgnoreCase)))
                .DistinctBy(value => value.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return BuiltInCanvasPenPresets.Concat(custom).ToArray();
        }
    }

    public CanvasPenPresetPreference? SaveCanvasPenPreset(CanvasPenPresetPreference preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        if (!CanvasPenPresetPreferencesWritable) return null;
        var saved = NormaliseCanvasPreset(preset, custom: true);
        if (saved is null) return null;
        var custom = CanvasPenPresets
            .Where(value => !value.Id.StartsWith("builtin-", StringComparison.OrdinalIgnoreCase))
            .Where(value => !value.Id.Equals(saved.Id, StringComparison.OrdinalIgnoreCase))
            .Append(saved)
            .ToArray();
        _preferences = _preferences with
        {
            CanvasPenPresets = JsonSerializer.SerializeToElement(new CanvasPenPresetPreferenceState(CurrentCanvasPenPresetVersion, custom), JsonOptions)
        };
        Save();
        return saved;
    }

    public bool RemoveCanvasPenPreset(string id)
    {
        if (!CanvasPenPresetPreferencesWritable || string.IsNullOrWhiteSpace(id) || id.StartsWith("builtin-", StringComparison.OrdinalIgnoreCase)) return false;
        var existing = CanvasPenPresets
            .Where(value => !value.Id.StartsWith("builtin-", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var custom = existing.Where(value => !value.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (custom.Length == existing.Length) return false;
        _preferences = _preferences with
        {
            CanvasPenPresets = JsonSerializer.SerializeToElement(new CanvasPenPresetPreferenceState(CurrentCanvasPenPresetVersion, custom), JsonOptions)
        };
        Save();
        return true;
    }

    private CanvasPenPresetPreferenceState CanvasPresetState => ReadCanvasPresetState(_preferences.CanvasPenPresets);

    private static CanvasPenPresetPreferenceState ReadCanvasPresetState(JsonElement? payload)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } value)
            return new CanvasPenPresetPreferenceState(CurrentCanvasPenPresetVersion, []);
        try
        {
            return JsonSerializer.Deserialize<CanvasPenPresetPreferenceState>(value.GetRawText(), JsonOptions)
                ?? new CanvasPenPresetPreferenceState(CurrentCanvasPenPresetVersion, []);
        }
        catch (JsonException)
        {
            return new CanvasPenPresetPreferenceState(CurrentCanvasPenPresetVersion, []);
        }
        catch (NotSupportedException)
        {
            return new CanvasPenPresetPreferenceState(CurrentCanvasPenPresetVersion, []);
        }
    }

    private static CanvasPenPresetPreference? NormaliseCanvasPreset(CanvasPenPresetPreference? value, bool custom)
    {
        if (value is null) return null;
        var tool = value.Tool.Equals("Highlighter", StringComparison.OrdinalIgnoreCase) ? "Highlighter" : "Pen";
        var effect = value.Effect.Equals("Uniform", StringComparison.OrdinalIgnoreCase) ? "Uniform"
            : value.Effect.Equals("Marker", StringComparison.OrdinalIgnoreCase) ? "Marker"
            : "Pressure";
        var color = NormaliseCanvasColour(value.Color, tool == "Highlighter" ? "#FFFFC928" : "#FF2F80ED");
        var id = value.Id?.Trim() ?? string.Empty;
        if (custom && (id.Length == 0 || id.StartsWith("builtin-", StringComparison.OrdinalIgnoreCase)))
            id = "canvas-pen-" + Guid.NewGuid().ToString("N");
        if (!custom && id.Length == 0) return null;
        var name = string.IsNullOrWhiteSpace(value.Name) ? "Custom pen" : value.Name.Trim();
        if (name.Length > 80) name = name[..80];
        return new CanvasPenPresetPreference(
            id,
            name,
            tool,
            color,
            Math.Clamp(double.IsFinite(value.Opacity) ? value.Opacity : 1, 0.05, 1),
            Math.Clamp(double.IsFinite(value.Thickness) ? value.Thickness : 3, 0.5, 64),
            effect);
    }

    private static string NormaliseCanvasColour(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var color = value.Trim();
        return color.StartsWith('#') && color.Length is 7 or 9 && color.Skip(1).All(Uri.IsHexDigit)
            ? color.ToUpperInvariant()
            : fallback;
    }

    private sealed partial record Preferences
    {
        public JsonElement? CanvasPenPresets { get; init; } =
            JsonSerializer.SerializeToElement(new CanvasPenPresetPreferenceState(CurrentCanvasPenPresetVersion, []), JsonOptions);
    }
}
