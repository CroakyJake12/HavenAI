/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/UserPreferencesService.cs, in the Desktop composition layer, which starts and wires the Avalonia application.
 * What: This file owns HavenThemePreset, HavenPreferenceSnapshot, UserPreferencesService, Preferences. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.HavenUI.Tokens;

namespace Haven.Desktop;

/// <summary>
/// Represents haven theme preset and keeps its related state and behavior together.
/// </summary>
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

/// <summary>
/// Represents haven preference snapshot and keeps its related state and behavior together.
/// </summary>
public sealed record HavenPreferenceSnapshot(
    bool AutoSwitchCompatibleModels,
    bool ShowAgenticInChat,
    bool VerticalTabs,
    bool ConfidenceMeter,
    bool AutoCompactContext,
    int CompactAtPercent,
    bool AdaptiveHelp,
    bool BrowserSideAssistant,
    bool AutoWakeOllama,
    double Temperature,
    int ContextLimit,
    int ActionLimit,
    PermissionMode FilePermission,
    PermissionMode CommandPermission,
    PermissionMode BrowserPermission,
    PermissionMode ComputerPermission);

/// <summary>
/// Represents user preferences service and keeps its related state and behavior together.
/// </summary>
public sealed class UserPreferencesService
{
    /// <summary>
    /// Stores json options locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    /// <summary>
    /// Stores path locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _path;
    /// <summary>
    /// Stores preferences locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private Preferences _preferences;

    /// <summary>
    /// Stores legacy theme ids locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly HashSet<string> LegacyThemeIds = new(StringComparer.OrdinalIgnoreCase) { "midnight", "graphite", "ocean", "forest" };

    public UserPreferencesService(IAppPaths paths)
    {
        _path = Path.Combine(paths.DataDirectory, "preferences.json");
        var hasExistingPreferences = File.Exists(_path);
        _preferences = Load();
        const int currentAppearanceVersion = 2;
        var migrated = _preferences.HavenUiAppearanceVersion < currentAppearanceVersion;
        if (migrated)
        {
            var previousTheme = hasExistingPreferences && _preferences.HavenUiAppearanceVersion == 0
                ? BuiltInThemes.FirstOrDefault(item =>
                    item.Id.Equals(_preferences.ThemeId, StringComparison.OrdinalIgnoreCase))
                : null;
            _preferences = _preferences with
            {
                // Version 2 is the from-scratch Haven visual release. Existing
                // version-1 light-shell preferences migrate once to the deck's
                // canonical dark presentation; the four-position control remains
                // available and subsequent user choices are preserved.
                HavenUiAppearance = previousTheme is not null
                    ? previousTheme.Light ? HavenUiAppearance.Bright : HavenUiAppearance.SuperDark
                    : HavenUiAppearance.SuperDark,
                HavenUiAppearanceVersion = currentAppearanceVersion,
                ThemeId = "haven-ui"
            };
        }
        else if (LegacyThemeIds.Contains(_preferences.ThemeId))
        {
            _preferences = _preferences with { ThemeId = "haven-ui" };
        }

        ApplyAppearance(_preferences.HavenUiAppearance, save: migrated);
    }

    /// <summary>Raised after the canonical HavenUI colour appearance changes.</summary>
    public event EventHandler? AppearanceChanged;

    /// <summary>
    /// Gets or updates built in themes, the bindable or domain state represented by this property.
    /// </summary>
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
        new("new-haven", "New Haven", "Phase 1 mockup palette",
            "#F1F8E9", "#FFFFFF", "#F7F7F7", "#050505", "#646464", "#6FE9F0", "#0078D4", true, "#6FE9F0", false),
        new("light", "Light", "Clean Windows 11 Mica light",
            "#F3F3F3", "#FFFFFF", "#F9F9F9", "#1A1A1A", "#616161", "#005FB8", "#0078D4", true, "#005FB8", true),
        new("midnight", "Midnight", "Original deep teal theme",
            "#080B10", "#0D1118", "#111721", "#EDF2F7", "#8B98AA", "#72E0BD", "#5AA6FF", false, "#72E0BD", false)
    ];

    /// <summary>
    /// Gets or updates themes, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<HavenThemePreset> Themes => BuiltInThemes.Concat(_preferences.CustomThemes).ToArray();
    /// <summary>
    /// Gets or updates theme id, the bindable or domain state represented by this property.
    /// </summary>
    public string ThemeId => _preferences.ThemeId;
    /// <summary>Gets the current four-position HavenUI brightness appearance.</summary>
    public HavenUiAppearance Appearance => _preferences.HavenUiAppearance;
    /// <summary>
    /// Gets or updates default model, the bindable or domain state represented by this property.
    /// </summary>
    public string? DefaultModel => _preferences.DefaultModel;
    /// <summary>
    /// Gets or updates default effort, the bindable or domain state represented by this property.
    /// </summary>
    public EffortLevel DefaultEffort => Enum.TryParse<EffortLevel>(_preferences.DefaultEffort, true, out var effort) ? effort : EffortLevel.Medium;
    /// <summary>
    /// Gets or updates auto switch compatible models, the bindable or domain state represented by this property.
    /// </summary>
    public bool AutoSwitchCompatibleModels => _preferences.AutoSwitchCompatibleModels;
    /// <summary>
    /// Gets or updates show agentic in chat, the bindable or domain state represented by this property.
    /// </summary>
    public bool ShowAgenticInChat => _preferences.ShowAgenticInChat;
    /// <summary>
    /// Gets or updates vertical tabs, the bindable or domain state represented by this property.
    /// </summary>
    public bool VerticalTabs => _preferences.VerticalTabs;
    /// <summary>
    /// Gets or updates confidence meter, the bindable or domain state represented by this property.
    /// </summary>
    public bool ConfidenceMeter => _preferences.ConfidenceMeter;
    /// <summary>
    /// Gets or updates auto compact context, the bindable or domain state represented by this property.
    /// </summary>
    public bool AutoCompactContext => _preferences.AutoCompactContext;
    /// <summary>
    /// Gets or updates compact at percent, the bindable or domain state represented by this property.
    /// </summary>
    public int CompactAtPercent => Math.Clamp(_preferences.CompactAtPercent, 50, 95);
    /// <summary>
    /// Gets or updates adaptive help, the bindable or domain state represented by this property.
    /// </summary>
    public bool AdaptiveHelp => _preferences.AdaptiveHelp;
    /// <summary>
    /// Gets or updates browser side assistant, the bindable or domain state represented by this property.
    /// </summary>
    public bool BrowserSideAssistant => _preferences.BrowserSideAssistant;
    /// <summary>Reports whether Haven should start Ollama when a local-model send finds it offline.</summary>
    public bool AutoWakeOllama => _preferences.AutoWakeOllama;
    /// <summary>Reports whether Generative UI may replace Haven's launcher-selected base theme.</summary>
    public bool GenerativeUiEnabled => _preferences.GenerativeUiEnabled;
    /// <summary>Reports whether local models should stay loaded when Haven's main UI is closed.</summary>
    public bool AlwaysLoaded => _preferences.AlwaysLoaded;
    /// <summary>
    /// Gets or updates generation options, the bindable or domain state represented by this property.
    /// </summary>
    public GenerationOptions GenerationOptions => new(Math.Clamp(_preferences.Temperature, 0, 2), Math.Clamp(_preferences.ContextLimit, 2048, 262144), Math.Clamp(_preferences.ActionLimit, 1, 100));
    /// <summary>
    /// Gets or updates file permission, the bindable or domain state represented by this property.
    /// </summary>
    public PermissionMode FilePermission => ParsePermission(_preferences.FilePermission);
    /// <summary>
    /// Gets or updates command permission, the bindable or domain state represented by this property.
    /// </summary>
    public PermissionMode CommandPermission => ParsePermission(_preferences.CommandPermission);
    /// <summary>
    /// Gets or updates browser permission, the bindable or domain state represented by this property.
    /// </summary>
    public PermissionMode BrowserPermission => ParsePermission(_preferences.BrowserPermission);
    /// <summary>
    /// Gets or updates computer permission, the bindable or domain state represented by this property.
    /// </summary>
    public PermissionMode ComputerPermission => ParsePermission(_preferences.ComputerPermission);
    /// <summary>
    /// Gets or updates snapshot, the bindable or domain state represented by this property.
    /// </summary>
    public HavenPreferenceSnapshot Snapshot => new(AutoSwitchCompatibleModels, ShowAgenticInChat, VerticalTabs, ConfidenceMeter,
        AutoCompactContext, CompactAtPercent, AdaptiveHelp, BrowserSideAssistant, AutoWakeOllama, GenerationOptions.Temperature,
        GenerationOptions.ContextLimit, GenerationOptions.ActionLimit, FilePermission, CommandPermission, BrowserPermission, ComputerPermission);

    /// <summary>
    /// Performs the apply theme step owned by this component.
    /// </summary>
    public void ApplyTheme(string themeId, bool save = true)
    {
        var theme = Themes.FirstOrDefault(item => item.Id.Equals(themeId, StringComparison.OrdinalIgnoreCase));
        ApplyAppearance(theme?.Light == false ? HavenUiAppearance.Dark : HavenUiAppearance.Bright, save);
    }

    /// <summary>
    /// Applies one of the four canonical HavenUI colour appearances. Geometry,
    /// typography and navigation remain unchanged.
    /// </summary>
    public void ApplyAppearance(HavenUiAppearance appearance, bool save = true)
    {
        if (!Enum.IsDefined(appearance))
            throw new ArgumentOutOfRangeException(nameof(appearance), appearance, "Unknown HavenUI appearance.");

        _preferences = _preferences with
        {
            HavenUiAppearance = appearance,
            HavenUiAppearanceVersion = 2,
            ThemeId = "haven-ui"
        };

        var application = Avalonia.Application.Current;
        if (application is not null)
        {
            application.RequestedThemeVariant = appearance is HavenUiAppearance.Dark or HavenUiAppearance.SuperDark
                ? ThemeVariant.Dark
                : ThemeVariant.Light;
            application.Resources["HavenUiAppearance"] = appearance;
            HavenUiResourceApplier.Apply(SurfacePaletteCatalog.For(HavenSurface.Home, appearance));
        }

        if (save) Save();
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    // Kept temporarily for persisted-theme migration tests. The production
    // Settings and startup paths no longer call this superseded theme engine.
    private void ApplyLegacyTheme(string themeId, bool save = true)
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
            Set(application, "SurfaceCardBrush", Color.FromArgb(theme.Light ? (byte)218 : (byte)164, panel2.R, panel2.G, panel2.B));
            Set(application, "TextPrimaryBrush", text);

            // Text hierarchy
            Set(application, "HavenTextBrush", text);
            Set(application, "HavenTextSoftBrush", Mix(text, muted, .22));
            Set(application, "HavenMutedBrush", muted);
            Set(application, "HavenMuted2Brush", Mix(muted, background, .20));

            // Accent colors — semi-transparent soft brushes for Mica layering
            Set(application, "HavenAccentInkBrush", theme.Light ? Colors.White : Color.Parse("#000000"));
            Set(application, "HavenAccentSoftBrush", Color.FromArgb(40, accent.R, accent.G, accent.B));
            Set(application, "HavenBlueSoftBrush", Color.FromArgb(40, blue.R, blue.G, blue.B));

            // Persisted pre-HavenUI themes are upgraded into the same three
            // visibly non-flat gradient tiers as current page palettes.
            HavenUiResourceApplier.ApplyAccentPalette(HavenAccentPalette.FromAnchors(
                accent,
                blue,
                nub,
                theme.Light ? Colors.White : Color.Parse("#000000"),
                Color.FromArgb(40, accent.R, accent.G, accent.B),
                panel));

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

    /// <summary>
    /// Retrieves system accent color for the current operation.
    /// </summary>
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

    /// <summary>
    /// Performs the set model defaults step owned by this component.
    /// </summary>
    public void SetModelDefaults(string? model, EffortLevel effort)
    {
        _preferences = _preferences with { DefaultModel = string.IsNullOrWhiteSpace(model) ? null : model, DefaultEffort = effort.ToString() };
        Save();
    }

    /// <summary>
    /// Performs the set advanced model options step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the set feature options step owned by this component.
    /// </summary>
    public void SetFeatureOptions(bool autoSwitch, bool showAgenticInChat, bool verticalTabs, bool confidenceMeter,
        bool autoCompact, int compactAtPercent, bool adaptiveHelp, bool browserSideAssistant, bool autoWakeOllama)
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
            BrowserSideAssistant = browserSideAssistant,
            AutoWakeOllama = autoWakeOllama
        };
        Save();
    }

    /// <summary>
    /// Enables or disables Generative UI theme application at startup.
    /// </summary>
    public void SetGenerativeUiEnabled(bool enabled)
    {
        _preferences = _preferences with { GenerativeUiEnabled = enabled };
        Save();
    }

    /// <summary>
    /// Enables or disables model residency (Always Loaded).
    /// When enabled, the configured local model stays loaded after Haven's main UI closes.
    /// </summary>
    public void SetAlwaysLoaded(bool alwaysLoaded)
    {
        _preferences = _preferences with { AlwaysLoaded = alwaysLoaded };
        Save();
    }

    /// <summary>
    /// Performs the set tool permissions step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the save custom theme step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the load step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the save step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the set step owned by this component.
    /// </summary>
    private static void Set(Avalonia.Application application, string key, Color value) => application.Resources[key] = new SolidColorBrush(value);

    /// <summary>
    /// Performs the mix step owned by this component.
    /// </summary>
    private static Color Mix(Color first, Color second, double weight)
    {
        byte Blend(byte a, byte b) => (byte)Math.Clamp(Math.Round(a * (1 - weight) + b * weight), 0, 255);
        return Color.FromArgb(255, Blend(first.R, second.R), Blend(first.G, second.G), Blend(first.B, second.B));
    }

    /// <summary>
    /// Performs the parse permission step owned by this component.
    /// </summary>
    private static PermissionMode ParsePermission(string? value) =>
        Enum.TryParse<PermissionMode>(value, true, out var mode) ? mode : PermissionMode.Ask;

    /// <summary>
    /// Represents preferences and keeps its related state and behavior together.
    /// </summary>
    private sealed record Preferences
    {
        /// <summary>
        /// Gets or updates default, the bindable or domain state represented by this property.
        /// </summary>
        public static Preferences Default => new();
        /// <summary>
        /// Gets or updates theme id, the bindable or domain state represented by this property.
        /// </summary>
        public string ThemeId { get; init; } = "system";
        /// <summary>Gets the canonical four-position HavenUI appearance.</summary>
        public HavenUiAppearance HavenUiAppearance { get; init; } = HavenUiAppearance.SuperDark;
        /// <summary>Distinguishes migrated preference files from theme-only legacy files.</summary>
        public int HavenUiAppearanceVersion { get; init; }
        /// <summary>
        /// Gets or updates default model, the bindable or domain state represented by this property.
        /// </summary>
        public string? DefaultModel { get; init; }
        /// <summary>
        /// Gets or updates default effort, the bindable or domain state represented by this property.
        /// </summary>
        public string DefaultEffort { get; init; } = EffortLevel.Medium.ToString();
        /// <summary>
        /// Gets or updates auto switch compatible models, the bindable or domain state represented by this property.
        /// </summary>
        public bool AutoSwitchCompatibleModels { get; init; } = true;
        /// <summary>
        /// Gets or updates show agentic in chat, the bindable or domain state represented by this property.
        /// </summary>
        public bool ShowAgenticInChat { get; init; }
        /// <summary>
        /// Gets or updates vertical tabs, the bindable or domain state represented by this property.
        /// </summary>
        public bool VerticalTabs { get; init; }
        /// <summary>
        /// Gets or updates confidence meter, the bindable or domain state represented by this property.
        /// </summary>
        public bool ConfidenceMeter { get; init; } = true;
        /// <summary>
        /// Gets or updates auto compact context, the bindable or domain state represented by this property.
        /// </summary>
        public bool AutoCompactContext { get; init; } = true;
        /// <summary>
        /// Gets or updates compact at percent, the bindable or domain state represented by this property.
        /// </summary>
        public int CompactAtPercent { get; init; } = 82;
        /// <summary>
        /// Gets or updates adaptive help, the bindable or domain state represented by this property.
        /// </summary>
        public bool AdaptiveHelp { get; init; } = true;
        /// <summary>
        /// Gets or updates browser side assistant, the bindable or domain state represented by this property.
        /// </summary>
        public bool BrowserSideAssistant { get; init; } = true;
        /// <summary>Gets whether local-model sends may launch Ollama automatically.</summary>
        public bool AutoWakeOllama { get; init; } = true;
        /// <summary>
        /// Gets whether Generative UI is allowed to override the base Haven theme.
        /// Disabled is the safe default for existing preference files that predate this setting.
        /// </summary>
        public bool GenerativeUiEnabled { get; init; }
        /// <summary>
        /// Gets whether local models should remain loaded when Haven's main UI is closed.
        /// Enables faster reopen and background task continuity at the cost of memory.
        /// </summary>
        public bool AlwaysLoaded { get; init; }
        /// <summary>
        /// Gets or updates temperature, the bindable or domain state represented by this property.
        /// </summary>
        public double Temperature { get; init; } = 0.7;
        /// <summary>
        /// Gets or updates context limit, the bindable or domain state represented by this property.
        /// </summary>
        public int ContextLimit { get; init; } = 32768;
        /// <summary>
        /// Gets or updates action limit, the bindable or domain state represented by this property.
        /// </summary>
        public int ActionLimit { get; init; } = 24;
        /// <summary>
        /// Gets or updates file permission, the bindable or domain state represented by this property.
        /// </summary>
        public string FilePermission { get; init; } = PermissionMode.Ask.ToString();
        /// <summary>
        /// Gets or updates command permission, the bindable or domain state represented by this property.
        /// </summary>
        public string CommandPermission { get; init; } = PermissionMode.Ask.ToString();
        /// <summary>
        /// Gets or updates browser permission, the bindable or domain state represented by this property.
        /// </summary>
        public string BrowserPermission { get; init; } = PermissionMode.Ask.ToString();
        /// <summary>
        /// Gets or updates computer permission, the bindable or domain state represented by this property.
        /// </summary>
        public string ComputerPermission { get; init; } = PermissionMode.Ask.ToString();
        /// <summary>
        /// Gets or updates custom themes, the bindable or domain state represented by this property.
        /// </summary>
        public IReadOnlyList<HavenThemePreset> CustomThemes { get; init; } = [];
    }
}
