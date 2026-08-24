using System.Globalization;
using System.Text.RegularExpressions;

namespace Haven.UI;

public sealed record HavenClassDefinition(string Name, IReadOnlyDictionary<string, string> Properties, int Line);
public sealed record HavenAnimationKeyframe(double Percent, IReadOnlyDictionary<string, string> Properties);
public sealed record HavenAnimationDefinition(string Name, TimeSpan Duration, string Easing, IReadOnlyList<HavenAnimationKeyframe> Keyframes, int Line);
public sealed record HavenTransitionDefinition(string Name, TimeSpan Duration, string Easing, IReadOnlyList<string> Properties, int Line);

public static partial class HavenResourceParser
{
    public static IReadOnlyDictionary<string, HavenClassDefinition> ParseClasses(string source, string sourceName)
    {
        var result = new Dictionary<string, HavenClassDefinition>(StringComparer.Ordinal);
        foreach (var block in ExtractBlocks(source, "Class", sourceName))
        {
            var definition = new HavenClassDefinition(block.Name, ParseAssignments(block.Body, sourceName, block.Line), block.Line);
            if (!result.TryAdd(block.Name, definition)) throw Error(sourceName, block.Line, $"Duplicate Class '{block.Name}'.");
        }
        return result;
    }

    public static IReadOnlyDictionary<string, HavenAnimationDefinition> ParseAnimations(string source, string sourceName)
    {
        var result = new Dictionary<string, HavenAnimationDefinition>(StringComparer.Ordinal);
        foreach (var block in ExtractBlocks(source, "Animation", sourceName))
        {
            var keyframes = KeyframeRegex().Matches(block.Body).Select(match => new HavenAnimationKeyframe(double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), ParseAssignments(match.Groups[2].Value, sourceName, block.Line))).OrderBy(frame => frame.Percent).ToArray();
            if (keyframes.Length == 0) throw Error(sourceName, block.Line, $"Animation '{block.Name}' has no keyframes.");
            if (keyframes.Any(frame => frame.Percent is < 0 or > 100)) throw Error(sourceName, block.Line, $"Animation '{block.Name}' keyframes must be between 0% and 100%.");
            if (keyframes.GroupBy(frame => frame.Percent).Any(group => group.Count() > 1)) throw Error(sourceName, block.Line, $"Animation '{block.Name}' contains a duplicate keyframe percentage.");
            var header = KeyframeRegex().Replace(block.Body, string.Empty);
            var assignments = ParseAssignments(header, sourceName, block.Line);
            if (!assignments.TryGetValue("Duration", out var durationText)) throw Error(sourceName, block.Line, $"Animation '{block.Name}' requires Duration.");
            var easing = assignments.GetValueOrDefault("Easing", "Linear");
            ValidateEasing(easing, sourceName, block.Line, $"Animation '{block.Name}'");
            var definition = new HavenAnimationDefinition(block.Name, ParseDuration(durationText, sourceName, block.Line), easing, keyframes, block.Line);
            if (!result.TryAdd(block.Name, definition)) throw Error(sourceName, block.Line, $"Duplicate Animation '{block.Name}'.");
        }
        return result;
    }

    public static IReadOnlyDictionary<string, HavenTransitionDefinition> ParseTransitions(string source, string sourceName)
    {
        var result = new Dictionary<string, HavenTransitionDefinition>(StringComparer.Ordinal);
        foreach (var block in ExtractBlocks(source, "Transition", sourceName))
        {
            var assignments = ParseAssignments(block.Body, sourceName, block.Line);
            if (!assignments.TryGetValue("Duration", out var durationText)) throw Error(sourceName, block.Line, $"Transition '{block.Name}' requires Duration.");
            if (!assignments.TryGetValue("Properties", out var propertyText)) throw Error(sourceName, block.Line, $"Transition '{block.Name}' requires Properties.");
            var properties = propertyText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (properties.Length == 0) throw Error(sourceName, block.Line, $"Transition '{block.Name}' requires at least one property.");
            var easing = assignments.GetValueOrDefault("Easing", "Linear");
            ValidateEasing(easing, sourceName, block.Line, $"Transition '{block.Name}'");
            var definition = new HavenTransitionDefinition(block.Name, ParseDuration(durationText, sourceName, block.Line), easing, properties, block.Line);
            if (!result.TryAdd(block.Name, definition)) throw Error(sourceName, block.Line, $"Duplicate Transition '{block.Name}'.");
        }
        return result;
    }

    private static IReadOnlyDictionary<string, string> ParseAssignments(string body, string sourceName, int line)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var statement in body.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var equals = statement.IndexOf('=');
            if (equals <= 0 || equals == statement.Length - 1) throw Error(sourceName, line, $"Malformed resource assignment '{statement.Trim()}'.");
            var name = statement[..equals].Trim(); var value = statement[(equals + 1)..].Trim();
            if (!result.TryAdd(name, value)) throw Error(sourceName, line, $"Duplicate property '{name}'.");
        }
        return result;
    }

    private static IEnumerable<(string Name, string Body, int Line)> ExtractBlocks(string source, string keyword, string sourceName)
    {
        var pattern = new Regex($@"\b{Regex.Escape(keyword)}\s+([A-Za-z_][A-Za-z0-9_]*)\s*\{{", RegexOptions.CultureInvariant);
        var position = 0;
        while (true)
        {
            var match = pattern.Match(source, position); if (!match.Success) yield break;
            var start = match.Index + match.Length; var depth = 1; var index = start;
            for (; index < source.Length && depth > 0; index++) { if (source[index] == '{') depth++; else if (source[index] == '}') depth--; }
            var line = 1 + source.AsSpan(0, match.Index).Count('\n');
            if (depth != 0) throw Error(sourceName, line, $"Unclosed {keyword} '{match.Groups[1].Value}'.");
            yield return (match.Groups[1].Value, source[start..(index - 1)], line); position = index;
        }
    }

    private static TimeSpan ParseDuration(string value, string sourceName, int line)
    {
        if (value.EndsWith("ms", StringComparison.OrdinalIgnoreCase) && double.TryParse(value[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms)) return TimeSpan.FromMilliseconds(ms);
        if (value.EndsWith('s') && double.TryParse(value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)) return TimeSpan.FromSeconds(seconds);
        throw Error(sourceName, line, $"Invalid animation duration '{value}'. Use ms or s.");
    }

    private static void ValidateEasing(string value, string sourceName, int line, string owner)
    {
        try { HavenEasing.Validate(value); }
        catch (FormatException exception) { throw Error(sourceName, line, $"{owner}: {exception.Message}"); }
    }

    private static FormatException Error(string sourceName, int line, string message) => new($"{sourceName}:{line}: {message}");
    [GeneratedRegex(@"(\d+(?:\.\d+)?)%\s*\{([^{}]*)\}", RegexOptions.CultureInvariant | RegexOptions.Singleline)] private static partial Regex KeyframeRegex();
}

public sealed class HavenResourceSet
{
    private readonly IReadOnlyDictionary<string, HavenClassDefinition> _systemClasses;
    private readonly IReadOnlyDictionary<string, HavenClassDefinition> _userClasses;
    private readonly IReadOnlyDictionary<string, HavenAnimationDefinition> _systemAnimations;
    private readonly IReadOnlyDictionary<string, HavenAnimationDefinition> _userAnimations;
    private readonly IReadOnlyDictionary<string, HavenTransitionDefinition> _systemTransitions;
    private readonly IReadOnlyDictionary<string, HavenTransitionDefinition> _userTransitions;

    public HavenResourceSet(string systemClasses, string userClasses, string systemAnimations, string userAnimations)
    {
        _systemClasses = HavenResourceParser.ParseClasses(systemClasses, "SystemClasses.hui");
        _userClasses = HavenResourceParser.ParseClasses(userClasses, "UserClasses.hui");
        _systemAnimations = HavenResourceParser.ParseAnimations(systemAnimations, "SystemAnimations.hui");
        _userAnimations = HavenResourceParser.ParseAnimations(userAnimations, "UserAnimations.hui");
        _systemTransitions = HavenResourceParser.ParseTransitions(systemAnimations, "SystemAnimations.hui");
        _userTransitions = HavenResourceParser.ParseTransitions(userAnimations, "UserAnimations.hui");
    }

    public static HavenResourceSet LoadEmbedded() => new(HavenResourceCatalog.SystemClasses, HavenResourceCatalog.UserClasses, HavenResourceCatalog.SystemAnimations, HavenResourceCatalog.UserAnimations);

    public void ApplyClasses(HavenElement root)
    {
        foreach (var element in root.DescendantsAndSelf())
        foreach (var className in element.ClassTokens)
        {
            if (_systemClasses.TryGetValue(className, out var system)) Apply(element, system, HavenValueSource.SystemClass);
            if (_userClasses.TryGetValue(className, out var user)) Apply(element, user, HavenValueSource.UserClass);
        }
    }

    public HavenAnimationDefinition ResolveAnimation(string name)
    {
        if (_userAnimations.TryGetValue(name, out var user)) return user;
        if (_systemAnimations.TryGetValue(name, out var system)) return system;
        throw new KeyNotFoundException($"Animation '{name}' was not found in UserAnimations.hui or SystemAnimations.hui.");
    }

    public bool TryResolveAnimation(string? name, out HavenAnimationDefinition? definition)
    {
        definition = null;
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (_userAnimations.TryGetValue(name, out var user)) { definition = user; return true; }
        if (_systemAnimations.TryGetValue(name, out var system)) { definition = system; return true; }
        return false;
    }

    public HavenTransitionDefinition ResolveTransition(string name)
    {
        if (_userTransitions.TryGetValue(name, out var user)) return user;
        if (_systemTransitions.TryGetValue(name, out var system)) return system;
        throw new KeyNotFoundException($"Transition '{name}' was not found in UserAnimations.hui or SystemAnimations.hui.");
    }

    public bool TryResolveTransition(string? name, out HavenTransitionDefinition? definition)
    {
        definition = null;
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (_userTransitions.TryGetValue(name, out var user)) { definition = user; return true; }
        if (_systemTransitions.TryGetValue(name, out var system)) { definition = system; return true; }
        return false;
    }

    private static void Apply(HavenElement element, HavenClassDefinition definition, HavenValueSource source)
    {
        foreach (var property in definition.Properties) HavenPropertyCodec.Set(element, property.Key, property.Value, source);
    }
}
