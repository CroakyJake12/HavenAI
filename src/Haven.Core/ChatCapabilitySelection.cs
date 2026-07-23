namespace Haven.Core;

/// <summary>
/// Explicit and inferred capability requirements for one chat turn.
/// Explicit user selections always win over inference.
/// </summary>
public sealed record ChatCapabilitySelection(
    IReadOnlySet<ToolCapability> Required,
    IReadOnlySet<ToolCapability> Explicit,
    bool IsGenericConversation)
{
    public bool Requires(ToolCapability capability) => Required.Contains(capability);

    public static ChatCapabilitySelection Create(
        string prompt,
        IEnumerable<ToolCapability>? explicitCapabilities = null)
    {
        var selected = explicitCapabilities?.ToHashSet() ?? [];
        var required = selected.ToHashSet();

        var generic = selected.Count == 0 && ChatCapabilityIntentClassifier.IsGenericConversation(prompt);
        if (!generic)
        {
            foreach (var inferred in ChatCapabilityIntentClassifier.Infer(prompt))
                required.Add(inferred);
        }

        required.Add(ToolCapability.Text);
        required.Add(ToolCapability.Streaming);
        return new(required, selected, generic);
    }
}

/// <summary>
/// Lightweight deterministic turn classifier used only to avoid loading unrelated tools for obvious requests.
/// Ambiguous requests remain text-only; the model can still answer or ask a clarification.
/// </summary>
public static class ChatCapabilityIntentClassifier
{
    private static readonly string[] GenericGreetings =
    [
        "hello", "hi", "hey", "good morning", "good afternoon", "good evening", "how are you"
    ];

    public static bool IsGenericConversation(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return true;
        var value = prompt.Trim();
        if (value.Length > 96) return false;
        return GenericGreetings.Any(greeting =>
            value.Equals(greeting, StringComparison.OrdinalIgnoreCase) ||
            value.Equals(greeting + "!", StringComparison.OrdinalIgnoreCase) ||
            value.Equals(greeting + ".", StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlySet<ToolCapability> Infer(string prompt)
    {
        var result = new HashSet<ToolCapability>();
        if (string.IsNullOrWhiteSpace(prompt)) return result;
        var value = prompt.ToLowerInvariant();

        if (ContainsAny(value, "search the web", "look online", "browse for", "find online"))
        {
            result.Add(ToolCapability.WebSearch);
            result.Add(ToolCapability.Browser);
        }

        if (ContainsAny(value, "open app", "launch ", "click ", "type into", "press ", "share screen"))
            result.Add(ToolCapability.ComputerUse);

        if (ContainsAny(value, "inspect code", "edit code", "edit file", "run tests", "build project", "run command"))
            result.Add(ToolCapability.Tools);

        if (ContainsAny(value, "image", "photo", "screenshot", "camera"))
            result.Add(ToolCapability.Vision);

        if (ContainsAny(value, "voice call", "microphone", "listen to", "transcribe"))
            result.Add(ToolCapability.AudioInput);

        if (ContainsAny(value, "speak", "read aloud", "voice response"))
            result.Add(ToolCapability.AudioOutput);

        return result;
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.Ordinal));
}

/// <summary>
/// Selects the closest installed model that satisfies a turn's requirements while preserving user effort level.
/// The caller is expected to restore the user's selected model after the capability-specific turn completes.
/// </summary>
public static class ChatModelFallbackSelector
{
    public static ModelDescriptor? Select(
        ModelDescriptor selected,
        IReadOnlyList<ModelDescriptor> installed,
        IReadOnlySet<ToolCapability> required)
    {
        if (SupportsAll(selected, required)) return selected;

        return installed
            .Where(model => SupportsAll(model, required))
            .OrderBy(model => FamilyDistance(selected, model))
            .ThenBy(model => SizeDistance(selected, model))
            .ThenBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static bool SupportsAll(ModelDescriptor model, IEnumerable<ToolCapability> required) =>
        required.All(model.Supports);

    private static int FamilyDistance(ModelDescriptor selected, ModelDescriptor candidate) =>
        string.Equals(selected.Family, candidate.Family, StringComparison.OrdinalIgnoreCase) ? 0 : 1;

    private static long SizeDistance(ModelDescriptor selected, ModelDescriptor candidate)
    {
        var difference = candidate.SizeBytes - selected.SizeBytes;
        return difference == long.MinValue ? long.MaxValue : Math.Abs(difference);
    }
}
