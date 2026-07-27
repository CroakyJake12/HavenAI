/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/KeybindingService.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns KeybindingService. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;

namespace Haven.Application;

/// <summary>
/// Represents keybinding service and keeps its related state and behavior together.
/// </summary>
public sealed class KeybindingService
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IAppPaths _paths;
    /// <summary>
    /// Stores bindings locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private Dictionary<string, string> _bindings = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Stores defaults locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly Dictionary<string, string> Defaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NewChat"] = "Ctrl+N",
        ["SwitchToChat"] = "Ctrl+1",
        ["SwitchToTeach"] = "Ctrl+2",
        ["SwitchToDo"] = "Ctrl+3",
        ["SwitchToStudio"] = "Ctrl+4",
        ["SwitchToBrowse"] = "Ctrl+5",
        ["SwitchToPlan"] = "Ctrl+6",
        ["ToggleSidebar"] = "Ctrl+B",
        ["OpenCommandPalette"] = "Ctrl+K",
        ["OpenSettings"] = "Ctrl+,",
        ["Send"] = "Enter",
        ["Stop"] = "Escape",
        ["NewTab"] = "Ctrl+T",
        ["CloseTab"] = "Ctrl+W",
        ["NextTab"] = "Ctrl+Tab",
        ["PreviousTab"] = "Ctrl+Shift+Tab",
        ["ToggleTemporary"] = "Ctrl+Shift+T",
        ["BranchChat"] = "Ctrl+Shift+B",
        ["CompactContext"] = "Ctrl+Shift+C",
        ["PinChat"] = "Ctrl+Shift+P",
        ["Dictate"] = "Ctrl+Shift+D"
    };

    public KeybindingService(IAppPaths paths)
    {
        _paths = paths;
    }

    /// <summary>
    /// Performs load asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(_paths.DataDirectory, "keybindings.json");
        if (File.Exists(path))
        {
            try
            {
                var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                _bindings = JsonSerializer.Deserialize<Dictionary<string, string>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                _bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>
    /// Performs save asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(_paths.DataDirectory, "keybindings.json");
        var json = JsonSerializer.Serialize(_bindings, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves binding for the current operation.
    /// </summary>
    public string GetBinding(string action) =>
        _bindings.TryGetValue(action, out var binding) ? binding :
        Defaults.TryGetValue(action, out var def) ? def : string.Empty;

    /// <summary>
    /// Performs the set binding step owned by this component.
    /// </summary>
    public void SetBinding(string action, string binding)
    {
        if (string.IsNullOrWhiteSpace(binding))
            _bindings.Remove(action);
        else
            _bindings[action] = binding;
    }

    /// <summary>
    /// Retrieves all bindings for the current operation.
    /// </summary>
    public IReadOnlyDictionary<string, string> GetAllBindings()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in Defaults)
            result[kvp.Key] = GetBinding(kvp.Key);
        return result;
    }

    /// <summary>
    /// Retrieves defaults for the current operation.
    /// </summary>
    public IReadOnlyDictionary<string, string> GetDefaults() => Defaults;
}
