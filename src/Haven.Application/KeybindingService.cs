using System.Text.Json;

namespace Haven.Application;

public sealed class KeybindingService
{
    private readonly IAppPaths _paths;
    private Dictionary<string, string> _bindings = new(StringComparer.OrdinalIgnoreCase);
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

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(_paths.DataDirectory, "keybindings.json");
        var json = JsonSerializer.Serialize(_bindings, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
    }

    public string GetBinding(string action) =>
        _bindings.TryGetValue(action, out var binding) ? binding :
        Defaults.TryGetValue(action, out var def) ? def : string.Empty;

    public void SetBinding(string action, string binding)
    {
        if (string.IsNullOrWhiteSpace(binding))
            _bindings.Remove(action);
        else
            _bindings[action] = binding;
    }

    public IReadOnlyDictionary<string, string> GetAllBindings()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in Defaults)
            result[kvp.Key] = GetBinding(kvp.Key);
        return result;
    }

    public IReadOnlyDictionary<string, string> GetDefaults() => Defaults;
}
