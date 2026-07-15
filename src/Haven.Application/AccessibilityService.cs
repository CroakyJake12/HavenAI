using Haven.Core;

namespace Haven.Application;

public sealed class AccessibilityService
{
    private readonly IAppPaths _paths;
    private AccessibilitySettings _settings = new();

    public AccessibilityService(IAppPaths paths)
    {
        _paths = paths;
    }

    public AccessibilitySettings Current => _settings;

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

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(_paths.DataDirectory, "accessibility.json");
        var json = System.Text.Json.JsonSerializer.Serialize(_settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
    }

    public void Update(Action<AccessibilitySettings> update)
    {
        update(_settings);
    }

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

public sealed class AccessibilitySettings
{
    public bool HighContrast { get; set; }
    public bool ReduceMotion { get; set; }
    public bool ScreenReaderOptimized { get; set; }
    public bool KeyboardNavigationOnly { get; set; }
    public double FontScale { get; set; } = 1.0;
    public bool AnnounceToolResults { get; set; } = true;
    public bool AnnounceModeChanges { get; set; } = true;
    public bool FocusTrapModals { get; set; } = true;
    public int ReducedAnimationDurationMs { get; set; } = 100;
}
