/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/AccessibilityService.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns AccessibilityService, AccessibilitySettings. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Represents accessibility service and keeps its related state and behavior together.
/// </summary>
public sealed class AccessibilityService
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IAppPaths _paths;
    /// <summary>
    /// Stores settings locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private AccessibilitySettings _settings = new();

    public AccessibilityService(IAppPaths paths)
    {
        _paths = paths;
    }

    /// <summary>
    /// Gets or updates current, the bindable or domain state represented by this property.
    /// </summary>
    public AccessibilitySettings Current => _settings;

    /// <summary>
    /// Performs load async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(_paths.DataDirectory, "accessibility.json");
        if (File.Exists(path))
        {
            try
            {
                var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                _settings = System.Text.Json.JsonSerializer.Deserialize<AccessibilitySettings>(json,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            }
            catch { _settings = new AccessibilitySettings(); }
        }
    }

    /// <summary>
    /// Performs save async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(_paths.DataDirectory, "accessibility.json");
        var json = System.Text.Json.JsonSerializer.Serialize(_settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs the update step owned by this component.
    /// </summary>
    public void Update(Action<AccessibilitySettings> update)
    {
        update(_settings);
    }

    /// <summary>
    /// Retrieves announcement for the current operation.
    /// </summary>
    public string GetAnnouncement(string key, params object[] args)
    {
        return key switch
        {
            "ModeSwitched" => $"Switched to {args[0]} mode.",
            "ConversationLoaded" => $"Loaded conversation: {args[0]}.",
            "MessageSent" => "Message sent.",
            "MessageReceived" => $"New message from {args[0]}.",
            "ToolExecuted" => $"Tool {args[0]} {(args[1] is true ? "succeeded" : "failed")}.",
            "TabOpened" => $"Opened tab: {args[0]}.",
            "TabClosed" => $"Closed tab: {args[0]}.",
            "SettingsChanged" => $"Setting {args[0]} changed to {args[1]}.",
            "Error" => $"Error: {args[0]}.",
            _ => key
        };
    }
}

/// <summary>
/// Represents accessibility settings and keeps its related state and behavior together.
/// </summary>
public sealed class AccessibilitySettings
{
    /// <summary>
    /// Gets or updates high contrast, the bindable or domain state represented by this property.
    /// </summary>
    public bool HighContrast { get; set; }
    /// <summary>
    /// Gets or updates reduce motion, the bindable or domain state represented by this property.
    /// </summary>
    public bool ReduceMotion { get; set; }
    /// <summary>
    /// Gets or updates screen reader optimized, the bindable or domain state represented by this property.
    /// </summary>
    public bool ScreenReaderOptimized { get; set; }
    /// <summary>
    /// Gets or updates keyboard navigation only, the bindable or domain state represented by this property.
    /// </summary>
    public bool KeyboardNavigationOnly { get; set; }
    /// <summary>
    /// Gets or updates font scale, the bindable or domain state represented by this property.
    /// </summary>
    public double FontScale { get; set; } = 1.0;
    /// <summary>
    /// Gets or updates announce tool results, the bindable or domain state represented by this property.
    /// </summary>
    public bool AnnounceToolResults { get; set; } = true;
    /// <summary>
    /// Gets or updates announce mode changes, the bindable or domain state represented by this property.
    /// </summary>
    public bool AnnounceModeChanges { get; set; } = true;
    /// <summary>
    /// Gets or updates focus trap modals, the bindable or domain state represented by this property.
    /// </summary>
    public bool FocusTrapModals { get; set; } = true;
    /// <summary>
    /// Gets or updates reduced animation duration ms, the bindable or domain state represented by this property.
    /// </summary>
    public int ReducedAnimationDurationMs { get; set; } = 100;
}
