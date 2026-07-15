using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop;

public sealed record HavenThemePreset(
    string Id,
    string Name,
    string Description,
    string Background,
    string Panel,
    string Panel2,
    string Text,
    string Muted,
    string Accent,
    string Blue,
    bool Light,
    string NubColor = "#00000000",
    bool CardBorder = false);

public sealed record HavenPreferenceSnapshot(
    bool AutoSwitchCompatibleModels,
    bool ShowAgenticInChat,
    bool VerticalTabs,
    bool ConfidenceMeter,
    bool AutoCompactContext,
    int CompactAtPercent,
    bool AdaptiveHelp,
    bool BrowserSideAssistant,
    double Temperature,
    int ContextLimit,
    int ActionLimit,
    PermissionMode FilePermission,
    PermissionMode CommandPermission,
    PermissionMode BrowserPermission,
    PermissionMode ComputerPermission);

public sealed class UserPreferencesService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private Preferences _preferences;

    private static readonly HashSet<string> LegacyThemeIds = new(StringComparer.OrdinalIgnoreCase) { "midnight", "graphite", "ocean", "forest" };

    public UserPreferencesService(IAppPaths paths)
    {
        _path = Path.Combine(paths.DataDirectory, "preferences.json");
        _preferences = Load();
        // Migrate legacy themes to new Windows 11 system theme
        if (LegacyThemeIds.Contains(_preferences.ThemeId))
            _preferences = _preferences with { ThemeId = "system" };
        ApplyTheme(_preferences.ThemeId, save: false);
    }

    private static IReadOnlyList<HavenThemePreset> BuiltInThemes { get; } =
    [
        new("system", "System", "Follows your OS theme and accent color",
            "#202020", "#242424", "#2D2D2D", "#FFFFFF", "#9A9A9A", "#0078D4", "#60CDFF", false, "#0078D4", false),
        new("obsidian", "Obsidian", "Windows 11 Mica dark with teal accent",
            "#111111", "#1A1A1A", "#202020", "#F5F5F5", "#8A8A8A", "#60CDFF", "#98EBFF", false, "#60CDFF", false),
        new("sapphire", "Sapphire", "Deep navy with electric blue accent",
            "#0A1628", "#0F1D30", "#15253D", "#E8F0FE", "#7B93B5", "#4CC2FF", "#80D4FF", false, "#4CC2FF", false),
        new("amethyst", "Amethyst", "Rich purple dark with violet accent",
            "#13111C", "#1A1726", "#221E30", "#F0ECF9", "#8E82A6", "#B47EFF", "#D4A8FF", false, "#B47EFF", false),
        new("emerald", "Emerald", "Forest dark with green accent",
            "#0D1510", "#121E17", "#18281E", "#ECF5EF", "#7FA38D", "#4ECC7A", "#7AEAA3", false, "#4ECC7A", false),
        new("rose", "Rose", "Warm dark with pink accent",
            "#181114", "#22161A", "#2D1C22", "#FBF0F2", "#A38088", "#FF6B8A", "#FF9AB5", false, "#FF6B8A", false),
        new("light", "Light", "Clean Windows 11 Mica light",
            "#F3F3F3", "#FFFFFF", "#F9F9F9", "#1A1A1A", "#616161", "#005FB8", "#0078D4", true, "#005FB8", true),
        new("midnight", "Midnight", "Original deep teal theme",
            "#080B10", "#0D1118", "#111721", "#EDF2F7", "#8B98AA", "#72E0BD", "#5AA6FF", false, "#72E0BD", false)
    ];

    public IReadOnlyList<HavenThemePreset> Themes => BuiltInThemes.Concat(_preferences.CustomThemes).ToArray();
    public string ThemeId => _preferences.ThemeId;
    public string? DefaultModel => _preferences.DefaultModel;
    public EffortLevel DefaultEffort => Enum.TryParse<EffortLevel>(_preferences.DefaultEffort, true, out var effort) ? effort : EffortLevel.Medium;
    public bool AutoSwitchCompatibleModels => _preferences.AutoSwitchCompatibleModels;
    public bool ShowAgenticInChat => _preferences.ShowAgenticInChat;
    public bool VerticalTabs => _preferences.VerticalTabs;
    public bool ConfidenceMeter => _preferences.ConfidenceMeter;
    public bool AutoCompactContext => _preferences.AutoCompactContext;
    public int CompactAtPercent => Math.Clamp(_preferences.CompactAtPercent, 50, 95);
    public bool AdaptiveHelp => _preferences.AdaptiveHelp;
    public bool BrowserSideAssistant => _preferences.BrowserSideAssistant;
    public GenerationOptions GenerationOptions => new(Math.Clamp(_preferences.Temperature, 0, 2), Math.Clamp(_preferences.ContextLimit, 2048, 262144), Math.Clamp(_preferences.ActionLimit, 1, 100));
    public PermissionMode FilePermission => ParsePermission(_preferences.FilePermission);
    public PermissionMode CommandPermission => ParsePermission(_preferences.CommandPermission);
    public PermissionMode BrowserPermission => ParsePermission(_preferences.BrowserPermission);
    public PermissionMode ComputerPermission => ParsePermission(_preferences.ComputerPermission);
    public HavenPreferenceSnapshot Snapshot => new(AutoSwitchCompatibleModels, ShowAgenticInChat, VerticalTabs, ConfidenceMeter,
        AutoCompactContext, CompactAtPercent, AdaptiveHelp, BrowserSideAssistant, GenerationOptions.Temperature,
        GenerationOptions.ContextLimit, GenerationOptions.ActionLimit, FilePermission, CommandPermission, BrowserPermission, ComputerPermission);

    public void ApplyTheme(string themeId, bool save = true)
    {
        var themes = Themes;
        var theme = themes.FirstOrDefault(item => item.Id.Equals(themeId, StringComparison.OrdinalIgnoreCase)) ?? themes[0];

        // Resolve "system" theme: use OS accent color if available
        if (theme.Id == "system")
        {
            var osAccent = GetSystemAccentColor();
            if (osAccent is not null)
                theme = theme with { NubColor = $"#{osAccent.Value.R:X2}{osAccent.Value.G:X2}{osAccent.Value.B:X2}", Accent = $"#{osAccent.Value.R:X2}{osAccent.Value.G:X2}{osAccent.Value.B:X2}" };
        }

        _preferences = _preferences with { ThemeId = theme.Id };
        var application = Avalonia.Application.Current;
        if (application is not null)
        {
            application.RequestedThemeVariant = theme.Light ? ThemeVariant.Light : ThemeVariant.Dark;
            var background = Color.Parse(theme.Background);
            var panel = Color.Parse(theme.Panel);
            var panel2 = Color.Parse(theme.Panel2);
            var text = Color.Parse(theme.Text);
            var muted = Color.Parse(theme.Muted);
            var accent = Color.Parse(theme.Accent);
            var blue = Color.Parse(theme.Blue);
            var nub = Color.Parse(theme.NubColor);

            // Mica-style layered backgrounds — truly transparent, let the system Mica backdrop do the work
            Set(application, "HavenBackgroundBrush", Color.FromArgb(theme.Light ? (byte)92 : (byte)70, background.R, background.G, background.B));
            Set(application, "HavenElevatedBrush", Color.FromArgb(theme.Light ? (byte)220 : (byte)190, panel.R, panel.G, panel.B));
            Set(application, "HavenPanelBrush", Color.FromArgb(theme.Light ? (byte)176 : (byte)118, panel.R, panel.G, panel.B));
            Set(application, "HavenPanel2Brush", Color.FromArgb(theme.Light ? (byte)218 : (byte)164, panel2.R, panel2.G, panel2.B));
            Set(application, "HavenPanel3Brush", Color.FromArgb(theme.Light ? (byte)238 : (byte)198, panel2.R, panel2.G, panel2.B));
            Set(application, "HavenPanelHoverBrush", Color.FromArgb(theme.Light ? (byte)96 : (byte)72, text.R, text.G, text.B));
            Set(application, "HavenButtonBrush", Color.FromArgb(theme.Light ? (byte)198 : (byte)46, panel2.R, panel2.G, panel2.B));
            Set(application, "HavenButtonHoverBrush", Color.FromArgb(theme.Light ? (byte)230 : (byte)70, text.R, text.G, text.B));
            Set(application, "HavenButtonPressedBrush", Color.FromArgb(theme.Light ? (byte)175 : (byte)32, text.R, text.G, text.B));
            Set(application, "HavenFocusBrush", Color.FromArgb(180, accent.R, accent.G, accent.B));
            Set(application, "PrimaryBrush", accent);
            Set(application, "SurfaceCardBrush", Color.FromArgb(theme.Light ? (byte)218 : (byte)164, panel2.R, panel2.G, panel2.B));
            Set(application, "TextPrimaryBrush", text);

            // Text hierarchy
            Set(application, "HavenTextBrush", text);
            Set(application, "HavenTextSoftBrush", Mix(text, muted, .22));
            Set(application, "HavenMutedBrush", muted);
            Set(application, "HavenMuted2Brush", Mix(muted, background, .20));

            // Accent colors — semi-transparent soft brushes for Mica layering
            Set(application, "HavenAccentBrush", accent);
            Set(application, "HavenAccentInkBrush", theme.Light ? Colors.White : Color.Parse("#000000"));
            Set(application, "HavenAccentSoftBrush", Color.FromArgb(40, accent.R, accent.G, accent.B));
            Set(application, "HavenBlueBrush", blue);
            Set(application, "HavenBlueSoftBrush", Color.FromArgb(40, blue.R, blue.G, blue.B));

            // Borders — minimal Windows 11 style
            var line = Mix(panel2, text, theme.CardBorder ? .16 : .09);
            var strongLine = Mix(panel2, text, theme.CardBorder ? .25 : .17);
            Set(application, "HavenLineBrush", Color.FromArgb(theme.Light ? (byte)105 : (byte)68, line.R, line.G, line.B));
            Set(application, "HavenLineStrongBrush", Color.FromArgb(theme.Light ? (byte)145 : (byte)105, strongLine.R, strongLine.G, strongLine.B));
            Set(application, "StrokeBrush", Color.FromArgb(theme.Light ? (byte)145 : (byte)105, strongLine.R, strongLine.G, strongLine.B));
            application.Resources["HavenAcrylicTintColor"] = panel2;
            application.Resources["HavenAcrylicFallbackColor"] = Color.FromArgb(246, panel2.R, panel2.G, panel2.B);

            // Sidebar accent nub color
            Set(application, "HavenNubBrush", nub);
        }
        if (save) Save();
    }

    private static Color? GetSystemAccentColor()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Try AccentColorMenu first (Windows 10 1903+)
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var value = key?.GetValue("AccentColorMenu") as int[];
                if (value is { Length: 4 })
                {
                    // ABGR format: [0]=alpha, [1]=blue, [2]=green, [3]=red
                    byte r = (byte)((value[3] >> 0) & 0xFF);
                    byte g = (byte)((value[2] >> 0) & 0xFF);
                    byte b = (byte)((value[1] >> 0) & 0xFF);
                    if (r != 0 || g != 0 || b != 0)
                        return Color.FromArgb(255, r, g, b);
                }
                // Fallback: try DWM colorization
                using var dwmKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
                var accentValue = dwmKey?.GetValue("AccentColor") as int?;
                if (accentValue.HasValue && accentValue.Value != 0)
                {
                    uint v = (uint)accentValue.Value;
                    return Color.FromArgb(255, (byte)(v & 0xFF), (byte)((v >> 8) & 0xFF), (byte)((v >> 16) & 0xFF));
                }
            }
        }
        catch { }
        // Default Windows 11 blue
        return Color.Parse("#0078D4");
    }

    public void SetModelDefaults(string? model, EffortLevel effort)
    {
        _preferences = _preferences with { DefaultModel = string.IsNullOrWhiteSpace(model) ? null : model, DefaultEffort = effort.ToString() };
        Save();
    }

    public void SetAdvancedModelOptions(double temperature, int contextLimit, int actionLimit)
    {
        _preferences = _preferences with
        {
            Temperature = Math.Clamp(temperature, 0, 2),
            ContextLimit = Math.Clamp(contextLimit, 2048, 262144),
            ActionLimit = Math.Clamp(actionLimit, 1, 100)
        };
        Save();
    }

    public void SetFeatureOptions(bool autoSwitch, bool showAgenticInChat, bool verticalTabs, bool confidenceMeter,
        bool autoCompact, int compactAtPercent, bool adaptiveHelp, bool browserSideAssistant)
    {
        _preferences = _preferences with
        {
            AutoSwitchCompatibleModels = autoSwitch,
            ShowAgenticInChat = showAgenticInChat,
            VerticalTabs = verticalTabs,
            ConfidenceMeter = confidenceMeter,
            AutoCompactContext = autoCompact,
            CompactAtPercent = Math.Clamp(compactAtPercent, 50, 95),
            AdaptiveHelp = adaptiveHelp,
            BrowserSideAssistant = browserSideAssistant
        };
        Save();
    }

    public void SetToolPermissions(PermissionMode file, PermissionMode command, PermissionMode browser, PermissionMode computer)
    {
        _preferences = _preferences with
        {
            FilePermission = file.ToString(),
            CommandPermission = command.ToString(),
            BrowserPermission = browser.ToString(),
            ComputerPermission = computer.ToString()
        };
        Save();
    }

    public HavenThemePreset SaveCustomTheme(HavenThemePreset theme)
    {
        _ = Color.Parse(theme.Background);
        _ = Color.Parse(theme.Panel);
        _ = Color.Parse(theme.Panel2);
        _ = Color.Parse(theme.Text);
        _ = Color.Parse(theme.Muted);
        _ = Color.Parse(theme.Accent);
        _ = Color.Parse(theme.Blue);
        if (!string.IsNullOrWhiteSpace(theme.NubColor) && theme.NubColor != "#00000000")
            _ = Color.Parse(theme.NubColor);
        var id = string.IsNullOrWhiteSpace(theme.Id) || BuiltInThemes.Any(item => item.Id.Equals(theme.Id, StringComparison.OrdinalIgnoreCase))
            ? "custom-" + Guid.NewGuid().ToString("N")
            : theme.Id;
        var saved = theme with { Id = id, Name = string.IsNullOrWhiteSpace(theme.Name) ? "Custom theme" : theme.Name.Trim() };
        var custom = _preferences.CustomThemes.Where(item => !item.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).Append(saved).ToArray();
        _preferences = _preferences with { CustomThemes = custom };
        Save();
        return saved;
    }

    private Preferences Load()
    {
        try
        {
            if (!File.Exists(_path)) return Preferences.Default;
            return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(_path), JsonOptions) ?? Preferences.Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Preferences.Default;
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_preferences, JsonOptions));
            File.Move(temporary, _path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void Set(Avalonia.Application application, string key, Color value) => application.Resources[key] = new SolidColorBrush(value);

    private static Color Mix(Color first, Color second, double weight)
    {
        byte Blend(byte a, byte b) => (byte)Math.Clamp(Math.Round(a * (1 - weight) + b * weight), 0, 255);
        return Color.FromArgb(255, Blend(first.R, second.R), Blend(first.G, second.G), Blend(first.B, second.B));
    }

    private static PermissionMode ParsePermission(string? value) =>
        Enum.TryParse<PermissionMode>(value, true, out var mode) ? mode : PermissionMode.Ask;

    private sealed record Preferences
    {
        public static Preferences Default => new();
        public string ThemeId { get; init; } = "system";
        public string? DefaultModel { get; init; }
        public string DefaultEffort { get; init; } = EffortLevel.Medium.ToString();
        public bool AutoSwitchCompatibleModels { get; init; } = true;
        public bool ShowAgenticInChat { get; init; }
        public bool VerticalTabs { get; init; }
        public bool ConfidenceMeter { get; init; } = true;
        public bool AutoCompactContext { get; init; } = true;
        public int CompactAtPercent { get; init; } = 82;
        public bool AdaptiveHelp { get; init; } = true;
        public bool BrowserSideAssistant { get; init; } = true;
        public double Temperature { get; init; } = 0.7;
        public int ContextLimit { get; init; } = 32768;
        public int ActionLimit { get; init; } = 24;
        public string FilePermission { get; init; } = PermissionMode.Ask.ToString();
        public string CommandPermission { get; init; } = PermissionMode.Ask.ToString();
        public string BrowserPermission { get; init; } = PermissionMode.Ask.ToString();
        public string ComputerPermission { get; init; } = PermissionMode.Ask.ToString();
        public IReadOnlyList<HavenThemePreset> CustomThemes { get; init; } = [];
    }
}
