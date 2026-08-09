namespace Haven.Core;

/// <summary>
/// Represents a model descriptor.
/// </summary>
public sealed record ModelDescriptor(
    string Name,
    long SizeBytes,
    string Family,
    string ParameterSize,
    string Quantization,
    IReadOnlySet<ToolCapability> Capabilities,
    DateTimeOffset ModifiedAt)
{
    /// <summary>
    /// Checks whether the model supports a capability.
    /// </summary>
    public bool Supports(ToolCapability capability) => Capabilities.Contains(capability);
    /// <summary>
    /// Human-readable size label.
    /// </summary>
    public string SizeLabel => FormatBytes(SizeBytes);
    /// <summary>
    /// Estimated RAM label.
    /// </summary>
    public string EstimatedRamLabel => $"Approx. {FormatBytes((long)(SizeBytes * 1.25))} RAM";

    /// <summary>
    /// Formats bytes into a human-readable string.
    /// </summary>
    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }
}

/// <summary>
/// A capability registered for discovery during one model turn. Registration
/// makes it discoverable; permission policy and runtime availability still
/// decide whether any concrete tool may execute.
/// </summary>
public sealed record ActiveCapability(
    string Key,
    string Name,
    string IconKey,
    string Instructions,
    string ImplementationKey,
    string OwnerAppKey)
{
    public static ActiveCapability FromDefinition(CapabilityDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new(
            definition.Key,
            definition.Name,
            definition.IconKey,
            definition.Instructions,
            definition.ImplementationKey,
            definition.OwnerAppKey);
    }

    /// <summary>Temporary adapter for Classic-only callers while that surface is deleted.</summary>
    public static ActiveCapability FromLegacyPlugin(PluginDefinition plugin) => new(
        LegacyKey(plugin.Name),
        plugin.Name,
        plugin.IconKey,
        plugin.Instructions,
        "legacy." + plugin.Name.ToLowerInvariant(),
        CapabilityRegistryCatalog.GeneralOwner);

    public static ActiveCapability FromLegacyPlugin(
        string name,
        string iconKey,
        string instructions = "") => new(
        LegacyKey(name),
        name,
        iconKey,
        instructions,
        "legacy." + name.ToLowerInvariant(),
        CapabilityRegistryCatalog.GeneralOwner);

    private static string LegacyKey(string name) => name switch
    {
        "BrowserUse" => "browser-use",
        "ComputerUse" => "computer-device-use",
        "WebSearch" => "web-search",
        "Automate" => "create-automation",
        "Test" => "run-tests",
        "DuoMode" => "duo",
        _ => "legacy-" + name.ToLowerInvariant()
    };
}
/// <summary>
/// Represents an active prompt.
/// </summary>
public sealed record ActivePrompt(string Name, string IconKey, bool Persists, string Instructions = "");

/// <summary>
/// Represents a capability requirement.
/// </summary>
public sealed record CapabilityRequirement(ToolCapability Capability, string Reason);

/// <summary>
/// Represents a capability preflight result.
/// </summary>
public sealed record CapabilityPreflightResult(
    bool IsCompatible,
    IReadOnlyList<CapabilityRequirement> Requirements,
    IReadOnlyList<CapabilityRequirement> Missing,
    ModelDescriptor? SuggestedModel)
{
    /// <summary>
    /// Creates a compatible result.
    /// </summary>
    public static CapabilityPreflightResult Compatible(IReadOnlyList<CapabilityRequirement> requirements) =>
        new(true, requirements, Array.Empty<CapabilityRequirement>(), null);
}

/// <summary>
/// Represents tool activity.
/// </summary>
public sealed record ToolActivity(
    Guid Id,
    string Title,
    string Detail,
    bool Succeeded,
    TimeSpan Duration,
    DateTimeOffset Timestamp,
    int LinesAdded = 0,
    int LinesRemoved = 0);
