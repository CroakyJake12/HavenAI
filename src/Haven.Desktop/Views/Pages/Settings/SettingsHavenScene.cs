using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Tokens;
using Haven.Desktop.Services;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Settings;

/// <summary>Haven-owned Settings information architecture and interaction surface.</summary>
internal sealed partial class SettingsHavenScene : IDisposable
{
    private sealed record SectionDefinition(string Key, string Title, string Description, string[] Keywords);

    private static readonly SectionDefinition[] Definitions =
    [
        new("home", "Settings", "Search Haven settings or choose a category.", ["settings", "home"]),
        new("models", "AI & Models", "Local models, defaults, residency, installation and removal.", ["ai", "model", "models", "ollama", "install", "download", "residency", "effort", "fallback", "priority", "personality", "nickname", "override", "governance", "provider"]),
        new("appearance", "Personalisation", "Default tab, colour appearance and accessibility preferences.", ["personalisation", "default", "tab", "appearance", "theme", "dark", "bright", "font", "accent", "colour", "color", "avatar", "profile", "picture", "motion", "animation", "accessibility"]),
        new("apps", "Chat & Apps", "Chat behaviour, context management and app assistance preferences.", ["chat", "apps", "agentic", "confidence", "compact", "browser", "auto"]),
        new("permissions", "Permissions & Sandboxing", "File, command, browser and device-use permission defaults.", ["permission", "permissions", "sandbox", "file", "command", "browser", "device", "computer", "tool"]),
        new("integrations", "Integrations", "Provider connections and external model integrations.", ["integration", "provider", "connection", "api", "key", "cloud"]),
        new("extensions", "Plugins & Skills", "Browse, install and manage native Plugins, Skills, bundles and repository sources.", ["plugin", "plugins", "skill", "skills", "extension", "github", "repository", "source", "update"]),
        new("voice", "Voice", "Voice profiles and voice-related configuration.", ["voice", "speech", "microphone", "call", "profile"]),
        new("privacy", "Privacy & Memory", "Local-data boundaries, memory and background-learning status.", ["privacy", "memory", "knowledge", "background", "learning", "data"]),
        new("advanced", "Advanced", "Generation parameters and bounded tool-action limits.", ["advanced", "temperature", "context", "action", "limit", "generation"]),
        new("updates", "Updates", "Installation source, current version, update channel and update checks.", ["updates", "upgrade", "channel", "stable", "preview", "development", "check now"]),
        new("about", "About Haven", "Product and runtime information.", ["about", "version", "runtime", "haven"])
    ];

    private static readonly string[] CatalogModels =
        ["qwen3:4b", "qwen3:8b", "gemma3:4b", "llama3.2:3b", "deepseek-r1:1.5b", "qwen2.5-coder:7b"];

    private readonly Dictionary<string, Container> _sections = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<HavenButton>> _navigation = new(StringComparer.OrdinalIgnoreCase);

    public SettingsHavenScene()
    {
        Root = new Page { Name = "Settings.Root", Layout = HavenLayout.Grid, Columns = "Auto 1fr", Rows = "1fr" };
        Root.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Background, "Transparent");
        Root.SetValue(HavenProperties.Overflow, HavenOverflow.Clip);

        Sidebar = BuildSidebar();
        Sidebar.Conditions.Add(new HavenScreenRangeCondition(HavenScreenAxis.Width, HavenLength.Px(720)));
        Root.Add(Sidebar);

        Content = new Container { Name = "Settings.Content", Layout = HavenLayout.Vertical };
        Content.SetValue(HavenProperties.Column, 1);
        Content.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Content.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        Content.SetValue(HavenProperties.Padding, HavenThickness.Parse("22px 24px 36px 24px"));
        Content.SetValue(HavenProperties.Gap, HavenLength.Px(12));
        Content.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        Root.Add(Content);

        PageTitle = new HavenText { Name = "Settings.PageTitle", Level = TextLevel.H1 };
        PageTitle.SetValue(HavenProperties.FontSize, 34d);
        PageTitle.SetValue(HavenProperties.FontWeight, 800);
        Content.Add(PageTitle);

        PageDescription = Muted("Settings.PageDescription", string.Empty);
        Content.Add(PageDescription);

        CompactNavigation = BuildCompactNavigation();
        CompactNavigation.Conditions.Add(new HavenScreenRangeCondition(HavenScreenAxis.Width, maximum: HavenLength.Px(719.999)));
        Content.Add(CompactNavigation);

        var searchRow = new Container { Name = "Settings.SearchRow", Layout = HavenLayout.Grid, Columns = "1fr Auto", Rows = "48px" };
        searchRow.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        searchRow.SetValue(HavenProperties.MaxWidth, HavenLength.Px(820));
        searchRow.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        SearchInput = new Input { Name = "Settings.Search", Placeholder = "Search settings" };
        SearchInput.Accessibility.AccessibleName = "Search settings";
        SearchButton = new HavenButton { Name = "Settings.SearchButton", Content = "Find setting", Variant = ButtonVariant.Secondary };
        SearchButton.SetValue(HavenProperties.Column, 1);
        searchRow.Add(SearchInput);
        searchRow.Add(SearchButton);
        Content.Add(searchRow);

        StatusText = Muted("Settings.Status", string.Empty);
        StatusText.SetValue(HavenProperties.MinHeight, HavenLength.Px(24));
        Content.Add(StatusText);

        _sections["home"] = BuildHome();
        _sections["models"] = BuildModels();
        _sections["appearance"] = BuildAppearance();
        _sections["apps"] = BuildApps();
        _sections["permissions"] = BuildPermissions();
        _sections["integrations"] = BuildIntegrations();
        _sections["extensions"] = BuildExtensions();
        _sections["voice"] = BuildVoice();
        _sections["privacy"] = BuildPrivacy();
        _sections["advanced"] = BuildAdvanced();
        _sections["updates"] = BuildUpdates();
        _sections["about"] = BuildAbout();
        foreach (var section in _sections.Values) Content.Add(section);

        SearchButton.Invoked += OnSearchInvoked;
        DeleteSelectedButton.Invoked += OnDeleteSelectedInvoked;
        CancelDeleteButton.Invoked += OnCancelDeleteInvoked;
        TemperatureSlider.ValueChanged += OnAdvancedValueChanged;
        ContextLimitSlider.ValueChanged += OnAdvancedValueChanged;
        ActionLimitSlider.ValueChanged += OnAdvancedValueChanged;
        CompactPercentSlider.ValueChanged += OnCompactValueChanged;
        NavigateTo("home");
    }

    public Page Root { get; }
    public Container Sidebar { get; }
    public Container CompactNavigation { get; }
    public Container Content { get; }
    public HavenText PageTitle { get; }
    public HavenText PageDescription { get; }
    public Input SearchInput { get; }
    public HavenButton SearchButton { get; }
    public HavenText StatusText { get; }
    public string ActiveSection { get; private set; } = "home";

    public Toggle AlwaysLoadedToggle { get; private set; } = null!;
    public Select InstalledModelSelect { get; private set; } = null!;
    public Select EffortSelect { get; private set; } = null!;
    public HavenButton RefreshModelsButton { get; private set; } = null!;
    public HavenButton SaveModelDefaultsButton { get; private set; } = null!;
    public Input InstallModelInput { get; private set; } = null!;
    public Select CatalogModelSelect { get; private set; } = null!;
    public HavenButton InstallModelButton { get; private set; } = null!;
    public HavenButton InstallCatalogButton { get; private set; } = null!;
    public HavenButton CancelInstallButton { get; private set; } = null!;
    public Progress InstallProgress { get; private set; } = null!;
    public HavenButton DeleteSelectedButton { get; private set; } = null!;
    public Container DeleteConfirmation { get; private set; } = null!;
    public HavenButton ConfirmDeleteButton { get; private set; } = null!;
    public HavenButton CancelDeleteButton { get; private set; } = null!;

    public Select AppearanceSelect { get; private set; } = null!;
    public Select DefaultTabSelect { get; private set; } = null!;
    public Toggle ReduceMotionToggle { get; private set; } = null!;
    public Select ThemeSelect { get; private set; } = null!;
    public Toggle AccentOverrideToggle { get; private set; } = null!;
    public Container AccentSwatches { get; private set; } = null!;
    public HavenText AccentSelectionText { get; private set; } = null!;
    public Select FontSelect { get; private set; } = null!;
    public Toggle UserAvatarToggle { get; private set; } = null!;
    public HavenButton UserAvatarChooseButton { get; private set; } = null!;
    public HavenButton UserAvatarRemoveButton { get; private set; } = null!;
    public Toggle HavenAvatarToggle { get; private set; } = null!;
    public HavenButton HavenAvatarChooseButton { get; private set; } = null!;
    public HavenButton HavenAvatarRemoveButton { get; private set; } = null!;
    public IReadOnlyList<HavenButton> AccentSwatchButtons => _accentSwatchButtons;

    private readonly List<HavenButton> _accentSwatchButtons = [];

    public Toggle AutoSwitchToggle { get; private set; } = null!;
    public Toggle AgenticInChatToggle { get; private set; } = null!;
    public Toggle ConfidenceToggle { get; private set; } = null!;
    public Toggle AutoCompactToggle { get; private set; } = null!;
    public Slider CompactPercentSlider { get; private set; } = null!;
    public HavenText CompactPercentValue { get; private set; } = null!;
    public Toggle AdaptiveHelpToggle { get; private set; } = null!;
    public Toggle BrowserSideToggle { get; private set; } = null!;
    public Toggle AutoWakeToggle { get; private set; } = null!;
    public HavenButton SaveFeaturesButton { get; private set; } = null!;

    public Select FilePermissionSelect { get; private set; } = null!;
    public Select CommandPermissionSelect { get; private set; } = null!;
    public Select BrowserPermissionSelect { get; private set; } = null!;
    public Select ComputerPermissionSelect { get; private set; } = null!;
    public HavenButton SavePermissionsButton { get; private set; } = null!;

    public HavenText VoiceProfileStatus { get; private set; } = null!;

    public Input ExtensionSourceUriInput { get; private set; } = null!;
    public Input ExtensionSourceNameInput { get; private set; } = null!;
    public Input ExtensionConnectedAccountInput { get; private set; } = null!;
    public Toggle ExtensionPrivateToggle { get; private set; } = null!;
    public Select ExtensionUpdateModeSelect { get; private set; } = null!;
    public HavenButton ExtensionAddSourceButton { get; private set; } = null!;
    public Select ExtensionSourceSelect { get; private set; } = null!;
    public HavenButton ExtensionRefreshButton { get; private set; } = null!;
    public HavenButton ExtensionRemoveSourceButton { get; private set; } = null!;
    public Select AvailableExtensionSelect { get; private set; } = null!;
    public HavenText AvailableExtensionDetails { get; private set; } = null!;
    public HavenButton ExtensionInstallButton { get; private set; } = null!;
    public Select InstalledExtensionSelect { get; private set; } = null!;
    public HavenText InstalledExtensionDetails { get; private set; } = null!;
    public HavenButton ExtensionToggleButton { get; private set; } = null!;
    public HavenButton ExtensionUninstallButton { get; private set; } = null!;
    public HavenText ExtensionStatusText { get; private set; } = null!;

    public Toggle LocalOnlyToggle { get; private set; } = null!;
    public Toggle BackgroundLearningToggle { get; private set; } = null!;
    public Toggle ModelImprovementSharingToggle { get; private set; } = null!;
    public HavenButton SavePrivacyButton { get; private set; } = null!;
    public Select BackgroundModeSelect { get; private set; } = null!;
    public Dictionary<KnowledgeCategory, Toggle> LearningCategoryToggles { get; } = [];
    public HavenButton LearningRefreshButton { get; private set; } = null!;
    public HavenButton LearningCleanupButton { get; private set; } = null!;
    public HavenText LearningStatusText { get; private set; } = null!;
    public HavenText LearningStorageText { get; private set; } = null!;
    public Select LearningTaskSelect { get; private set; } = null!;
    public HavenText LearningTaskDetails { get; private set; } = null!;
    public HavenButton LearningTaskPauseButton { get; private set; } = null!;
    public HavenButton LearningTaskResumeButton { get; private set; } = null!;
    public HavenButton LearningTaskCancelButton { get; private set; } = null!;
    public Select LearnMeSelect { get; private set; } = null!;
    public HavenText LearnMeDetails { get; private set; } = null!;
    public Input LearnMeCorrectionInput { get; private set; } = null!;
    public HavenButton LearnMeCorrectButton { get; private set; } = null!;
    public HavenButton LearnMePinButton { get; private set; } = null!;
    public HavenButton LearnMeRejectButton { get; private set; } = null!;
    public HavenButton LearnMeForgetButton { get; private set; } = null!;
    public Select ApiBankSelect { get; private set; } = null!;
    public HavenText ApiBankDetails { get; private set; } = null!;
    public HavenButton ApiBankPinButton { get; private set; } = null!;
    public HavenButton ApiBankRemoveButton { get; private set; } = null!;

    public Slider TemperatureSlider { get; private set; } = null!;
    public Slider ContextLimitSlider { get; private set; } = null!;
    public Slider ActionLimitSlider { get; private set; } = null!;
    public HavenText TemperatureValue { get; private set; } = null!;
    public HavenText ContextLimitValue { get; private set; } = null!;
    public HavenText ActionLimitValue { get; private set; } = null!;
    public HavenButton SaveAdvancedButton { get; private set; } = null!;

    public void LoadPreferences(UserPreferencesService preferences, MotionPreferencesService motionPreferences)
    {
        AlwaysLoadedToggle.IsChecked = preferences.AlwaysLoaded;
        EffortSelect.SelectedIndex = Array.IndexOf(Enum.GetNames<EffortLevel>(), preferences.DefaultEffort.ToString());
        AppearanceSelect.SelectedIndex = preferences.Appearance switch
        {
            HavenUiAppearance.SuperBright => 0,
            HavenUiAppearance.Bright => 1,
            HavenUiAppearance.Dark => 2,
            _ => 3
        };
        ThemeSelect.SelectedIndex = Math.Max(0, HavenThemeCatalog.All.ToList().FindIndex(expression => expression.Theme == preferences.Theme));
        AccentOverrideToggle.IsChecked = preferences.OverrideAccentColour;
        ApplyAccentSwatchColours(preferences.Appearance);
        AccentSelectionText.Content = preferences.OverrideAccentColour && preferences.AccentColourSelection is { } accentName
            ? $"Accent: {accentName}"
            : "Accent: surface colours";
        UserAvatarToggle.IsChecked = preferences.UserAvatarEnabled;
        UserAvatarRemoveButton.SetValue(HavenProperties.Enabled, preferences.UserAvatarEnabled || AvatarStore.Current?.Has(HavenAvatarKind.User) == true);
        HavenAvatarToggle.IsChecked = preferences.HavenAvatarEnabled;
        HavenAvatarRemoveButton.SetValue(HavenProperties.Enabled, preferences.HavenAvatarEnabled || AvatarStore.Current?.Has(HavenAvatarKind.Haven) == true);
        ReduceMotionToggle.IsChecked = motionPreferences.ReduceAnimations;

        AutoSwitchToggle.IsChecked = preferences.AutoSwitchCompatibleModels;
        AgenticInChatToggle.IsChecked = preferences.ShowAgenticInChat;
        ConfidenceToggle.IsChecked = preferences.ConfidenceMeter;
        AutoCompactToggle.IsChecked = preferences.AutoCompactContext;
        CompactPercentSlider.Value = preferences.CompactAtPercent;
        AdaptiveHelpToggle.IsChecked = preferences.AdaptiveHelp;
        BrowserSideToggle.IsChecked = preferences.BrowserSideAssistant;
        AutoWakeToggle.IsChecked = preferences.AutoWakeOllama;

        SelectText(FilePermissionSelect, preferences.FilePermission.ToString());
        SelectText(CommandPermissionSelect, preferences.CommandPermission.ToString());
        SelectText(BrowserPermissionSelect, preferences.BrowserPermission.ToString());
        SelectText(ComputerPermissionSelect, preferences.ComputerPermission.ToString());

        TemperatureSlider.Value = preferences.GenerationOptions.Temperature;
        ContextLimitSlider.Value = preferences.GenerationOptions.ContextLimit;
        ActionLimitSlider.Value = preferences.GenerationOptions.ActionLimit;
        VoiceProfileStatus.Content = preferences.CustomVoiceProfiles.Count == 0
            ? "No custom voice profiles are stored. Built-in profiles remain available to Haven Voice."
            : $"{preferences.CustomVoiceProfiles.Count} custom voice profile{(preferences.CustomVoiceProfiles.Count == 1 ? string.Empty : "s")} stored locally.";
        UpdateAdvancedValues();
        UpdateCompactValue();
    }

    public void LoadPrivacyPreferences(PrivacyPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        LocalOnlyToggle.IsChecked = preferences.LocalOnlyMode;
        BackgroundLearningToggle.IsChecked = preferences.BackgroundLearningEnabled;
        ModelImprovementSharingToggle.IsChecked = preferences.ModelImprovementSharingEnabled;
    }

    public void SetModels(IReadOnlyList<string> models, string? preferred)
    {
        var ordered = models.OrderBy(model => model, StringComparer.OrdinalIgnoreCase).ToArray();
        InstalledModelSelect.Items = ordered;
        var selected = !string.IsNullOrWhiteSpace(preferred)
            ? Array.FindIndex(ordered, model => model.Equals(preferred, StringComparison.OrdinalIgnoreCase))
            : -1;
        InstalledModelSelect.SelectedIndex = selected >= 0 ? selected : ordered.Length > 0 ? 0 : -1;
        DeleteSelectedButton.SetValue(HavenProperties.Enabled, ordered.Length > 0);
        SaveModelDefaultsButton.SetValue(HavenProperties.Enabled, ordered.Length > 0);
        SetDeleteConfirmation(false);
    }

    public bool RunSearch()
    {
        var query = SearchInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            SetStatus("Type a setting or category to search.");
            return false;
        }
        var terms = query.Split([' ', ',', '.', '?', '!', '&'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var match = Definitions.Skip(1)
            .Select(definition => new
            {
                Definition = definition,
                Score = definition.Keywords.Count(keyword => terms.Any(term => keyword.Contains(term, StringComparison.OrdinalIgnoreCase) || term.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                    + (definition.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ? 4 : 0)
                    + (definition.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ? 2 : 0)
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Definition.Title, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (match is null)
        {
            SetStatus("No matching Haven setting was found. Try model, appearance, permissions, voice, privacy or advanced.");
            return false;
        }
        NavigateTo(match.Definition.Key);
        SetStatus($"Opened {match.Definition.Title}.");
        return true;
    }

    public void NavigateTo(string key)
    {
        if (!_sections.ContainsKey(key)) return;
        ActiveSection = key;
        var definition = Definitions.First(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        PageTitle.Content = definition.Title;
        PageDescription.Content = definition.Description;
        foreach (var (sectionKey, section) in _sections)
            section.SetValue(HavenProperties.Visibility, sectionKey.Equals(key, StringComparison.OrdinalIgnoreCase) ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        foreach (var (sectionKey, buttons) in _navigation)
        {
            var selected = sectionKey.Equals(key, StringComparison.OrdinalIgnoreCase);
            foreach (var button in buttons)
            {
                button.SetState(HavenElementState.Selected, selected);
                button.SetValue(HavenProperties.Background, selected ? "AccentMuted" : "Transparent");
            }
        }
    }

    public void SetStatus(string text) => StatusText.Content = text;

    public void SetInstallState(bool busy)
    {
        InstallModelButton.SetValue(HavenProperties.Enabled, !busy);
        InstallCatalogButton.SetValue(HavenProperties.Enabled, !busy);
        RefreshModelsButton.SetValue(HavenProperties.Enabled, !busy);
        CancelInstallButton.SetValue(HavenProperties.Visibility, busy ? HavenVisibility.Visible : HavenVisibility.Collapsed);
    }

    public void SetDeleteConfirmation(bool visible)
    {
        DeleteConfirmation.SetValue(HavenProperties.Visibility, visible ? HavenVisibility.Visible : HavenVisibility.Collapsed);
    }

    public void SetDeleteBusy(bool busy)
    {
        ConfirmDeleteButton.SetValue(HavenProperties.Enabled, !busy);
        CancelDeleteButton.SetValue(HavenProperties.Enabled, !busy);
        DeleteSelectedButton.SetValue(HavenProperties.Enabled, !busy && InstalledModelSelect.SelectedIndex >= 0);
    }

    private Container BuildSidebar()
    {
        var sidebar = new Container { Name = "Settings.Sidebar", Layout = HavenLayout.Vertical };
        sidebar.SetValue(HavenProperties.Width, HavenLength.Px(224));
        sidebar.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        sidebar.SetValue(HavenProperties.Background, "Surface");
        sidebar.SetValue(HavenProperties.BorderColor, "Border");
        sidebar.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        sidebar.SetValue(HavenProperties.Padding, HavenThickness.Parse("18px 14px"));
        sidebar.SetValue(HavenProperties.Gap, HavenLength.Px(5));
        sidebar.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        var title = Heading("Settings.Sidebar.Title", "Settings", 20);
        title.SetValue(HavenProperties.Margin, HavenThickness.Parse("0px 6px 12px 8px"));
        sidebar.Add(title);
        foreach (var definition in Definitions)
            AddNavigationButton(sidebar, definition, compact: false);
        return sidebar;
    }

    private Container BuildCompactNavigation()
    {
        var navigation = new Container { Name = "Settings.CompactNavigation", Layout = HavenLayout.Wrap };
        navigation.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        navigation.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        navigation.SetValue(HavenProperties.Padding, HavenThickness.Parse("2px 0px 6px 0px"));
        foreach (var definition in Definitions)
            AddNavigationButton(navigation, definition, compact: true);
        return navigation;
    }

    private void AddNavigationButton(Container parent, SectionDefinition definition, bool compact)
    {
        var button = new HavenButton
        {
            Name = $"Settings.Nav.{definition.Key}.{(compact ? "Compact" : "Wide")}",
            Content = definition.Key == "home" ? "Home" : definition.Title,
            Variant = compact ? ButtonVariant.Ghost : ButtonVariant.Navigation
        };
        if (!compact) button.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        var target = definition.Key;
        button.Invoked += (_, _) => NavigateTo(target);
        if (!_navigation.TryGetValue(target, out var buttons))
        {
            buttons = [];
            _navigation[target] = buttons;
        }
        buttons.Add(button);
        parent.Add(button);
    }

    private Container BuildHome()
    {
        var section = Section("Settings.Home");
        section.Add(Heading("Settings.Home.Heading", "Choose a category", 22));
        section.Add(Muted("Settings.Home.Description", "Every mutable control here is backed by a real Haven service; unavailable capabilities are clearly marked informational."));
        foreach (var definition in Definitions.Skip(1))
        {
            var card = Card($"Settings.Home.{definition.Key}");
            var open = new HavenButton { Content = definition.Title, Variant = ButtonVariant.Ghost };
            open.SetValue(HavenProperties.Width, HavenLength.Percent(100));
            var target = definition.Key;
            open.Invoked += (_, _) => NavigateTo(target);
            card.Add(open);
            card.Add(Muted($"Settings.Home.{definition.Key}.Description", definition.Description));
            section.Add(card);
        }
        return section;
    }

    private Container BuildModels()
    {
        var section = Section("Settings.Models");
        var residency = Card("Settings.Models.Residency");
        AlwaysLoadedToggle = new Toggle { Name = "Settings.Models.AlwaysLoaded" };
        residency.Add(SettingRow("Model residency", "Keep the configured local runtime loaded after Haven closes for faster startup at the cost of memory.", AlwaysLoadedToggle));
        section.Add(residency);

        var defaults = Card("Settings.Models.Defaults");
        InstalledModelSelect = NewSelect("Settings.Models.Installed", []);
        EffortSelect = NewSelect("Settings.Models.Effort", Enum.GetNames<EffortLevel>());
        RefreshModelsButton = new HavenButton { Name = "Settings.Models.Refresh", Content = "Refresh models", Variant = ButtonVariant.Secondary };
        SaveModelDefaultsButton = new HavenButton { Name = "Settings.Models.SaveDefaults", Content = "Save model defaults", Variant = ButtonVariant.Primary };
        defaults.Add(SettingRow("Default local model", "Choose from models currently reported by Ollama.", InstalledModelSelect));
        defaults.Add(SettingRow("Default reasoning effort", "Controls the default effort sent to compatible model runtimes.", EffortSelect));
        defaults.Add(RefreshModelsButton);
        defaults.Add(SaveModelDefaultsButton);
        section.Add(defaults);

        var install = Card("Settings.Models.Install");
        install.Add(Heading("Settings.Models.Install.Heading", "Install a local model", 18));
        install.Add(Muted("Settings.Models.Install.Description", "Install an Ollama model by name, or choose a model from Haven's small supported starter catalogue."));
        InstallModelInput = new Input { Name = "Settings.Models.InstallName", Placeholder = "e.g. qwen3:4b" };
        InstallModelInput.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        CatalogModelSelect = NewSelect("Settings.Models.Catalog", CatalogModels);
        CatalogModelSelect.SelectedIndex = 0;
        InstallModelButton = new HavenButton { Name = "Settings.Models.InstallDirect", Content = "Install entered model", Variant = ButtonVariant.Primary };
        InstallCatalogButton = new HavenButton { Name = "Settings.Models.InstallCatalog", Content = "Install selected catalogue model", Variant = ButtonVariant.Secondary };
        CancelInstallButton = new HavenButton { Name = "Settings.Models.CancelInstall", Content = "Cancel installation", Variant = ButtonVariant.Ghost };
        CancelInstallButton.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        InstallProgress = new Progress { Name = "Settings.Models.InstallProgress", Minimum = 0, Maximum = 1, Value = 0 };
        InstallProgress.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        install.Add(InstallModelInput);
        install.Add(InstallModelButton);
        install.Add(CatalogModelSelect);
        install.Add(InstallCatalogButton);
        install.Add(InstallProgress);
        install.Add(CancelInstallButton);
        section.Add(install);

        var removal = Card("Settings.Models.Remove");
        DeleteSelectedButton = new HavenButton { Name = "Settings.Models.RemoveSelected", Content = "Remove selected model", Variant = ButtonVariant.Danger };
        DeleteSelectedButton.SetValue(HavenProperties.Enabled, false);
        removal.Add(DeleteSelectedButton);
        DeleteConfirmation = new Container { Name = "Settings.Models.DeleteConfirmation", Layout = HavenLayout.Vertical };
        DeleteConfirmation.SetValue(HavenProperties.Background, "Surface");
        DeleteConfirmation.SetValue(HavenProperties.BorderColor, "Danger");
        DeleteConfirmation.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        DeleteConfirmation.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(16)));
        DeleteConfirmation.SetValue(HavenProperties.Padding, HavenThickness.Parse("14px"));
        DeleteConfirmation.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        DeleteConfirmation.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        DeleteConfirmation.Add(Muted("Settings.Models.DeleteWarning", "This permanently removes the selected model from the local Ollama store."));
        ConfirmDeleteButton = new HavenButton { Name = "Settings.Models.ConfirmDelete", Content = "Confirm removal", Variant = ButtonVariant.Danger };
        CancelDeleteButton = new HavenButton { Name = "Settings.Models.CancelDelete", Content = "Cancel", Variant = ButtonVariant.Ghost };
        DeleteConfirmation.Add(ConfirmDeleteButton);
        DeleteConfirmation.Add(CancelDeleteButton);
        removal.Add(DeleteConfirmation);
        section.Add(removal);

        BuildGovernance(section);
        return section;
    }

    private Container BuildAppearance()
    {
        var section = Section("Settings.Appearance");
        var card = Card("Settings.Appearance.Card");
        DefaultTabSelect = NewSelect("Settings.Personalisation.DefaultTab", []);
        AppearanceSelect = NewSelect("Settings.Appearance.Mode", ["Super Bright", "Bright", "Dark", "Super Dark"]);
        ReduceMotionToggle = new Toggle { Name = "Settings.Appearance.ReduceMotion" };
        ThemeSelect = NewSelect("Settings.Personalisation.Theme", HavenThemeCatalog.All.Select(expression => expression.DisplayName).ToArray());
        AccentOverrideToggle = new Toggle { Name = "Settings.Personalisation.AccentOverride" };
        AccentSwatches = new Container { Name = "Settings.Personalisation.AccentSwatches", Layout = HavenLayout.Wrap };
        AccentSwatches.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        foreach (var colour in AccentColourCatalog.Colours)
        {
            var name = AccentColourCatalog.Name(colour);
            var swatch = new HavenButton
            {
                Name = $"Settings.Personalisation.Accent.{name}",
                Content = string.Empty,
                Variant = ButtonVariant.Icon
            };
            swatch.Accessibility.AccessibleName = $"{name} accent colour";
            swatch.SetValue(HavenProperties.Width, HavenLength.Px(34));
            swatch.SetValue(HavenProperties.Height, HavenLength.Px(34));
            swatch.SetValue(HavenProperties.MinHeight, HavenLength.Px(34));
            swatch.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(17)));
            AccentSwatches.Add(swatch);
            _accentSwatchButtons.Add(swatch);
        }

        AccentSelectionText = Muted("Settings.Personalisation.AccentSelection", "Accent: surface colours");
        FontSelect = NewSelect("Settings.Personalisation.Font", []);
        UserAvatarToggle = new Toggle { Name = "Settings.Personalisation.UserAvatar" };
        UserAvatarChooseButton = new HavenButton { Name = "Settings.Personalisation.UserAvatar.Choose", Content = "Choose image", Variant = ButtonVariant.Secondary };
        UserAvatarRemoveButton = new HavenButton { Name = "Settings.Personalisation.UserAvatar.Remove", Content = "Remove", Variant = ButtonVariant.Ghost };
        HavenAvatarToggle = new Toggle { Name = "Settings.Personalisation.HavenAvatar" };
        HavenAvatarChooseButton = new HavenButton { Name = "Settings.Personalisation.HavenAvatar.Choose", Content = "Choose image", Variant = ButtonVariant.Secondary };
        HavenAvatarRemoveButton = new HavenButton { Name = "Settings.Personalisation.HavenAvatar.Remove", Content = "Remove", Variant = ButtonVariant.Ghost };
        card.Add(SettingRow("Default Tab", "Normal new tabs open the selected installed Haven app. If it is temporarily unavailable, Haven uses the standard new-tab experience without deleting this preference.", DefaultTabSelect));
        card.Add(SettingRow("Theme", "Choose how Haven looks and reacts. Themes change visual expression only; layouts, navigation and workflows stay the same.", ThemeSelect));
        card.Add(SettingRow("Colour appearance", "Choose one of Haven's four canonical light/dark appearances.", AppearanceSelect));
        card.Add(SettingRow("Reduce animations", "Reduces decorative and transition motion throughout Haven.", ReduceMotionToggle));
        card.Add(SettingRow("Override accent colour", "Replace each app's own accent with one global colour family from the palette below.", AccentOverrideToggle));
        card.Add(AccentSwatches);
        card.Add(AccentSelectionText);
        card.Add(SettingRow("Font", "Applies the selected installed font across every theme. Montserrat is Haven's bundled default and final fallback.", FontSelect));
        card.Add(SettingRow("User profile picture", "Show your own picture beside your chat messages. Images are processed and stored locally only.", UserAvatarToggle));
        card.Add(AvatarActionRow("Settings.Personalisation.UserAvatar.Actions", UserAvatarChooseButton, UserAvatarRemoveButton));
        card.Add(SettingRow("Haven profile picture", "Show a separate Haven picture beside Haven chat messages. Works independently from your own picture.", HavenAvatarToggle));
        card.Add(AvatarActionRow("Settings.Personalisation.HavenAvatar.Actions", HavenAvatarChooseButton, HavenAvatarRemoveButton));
        section.Add(card);
        return section;
    }

    private static Container AvatarActionRow(string name, HavenButton choose, HavenButton remove)
    {
        var actions = new Container { Name = name, Layout = HavenLayout.Horizontal };
        actions.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        actions.Add(choose);
        actions.Add(remove);
        return actions;
    }

    /// <summary>Refreshes accent swatch fills for the current appearance family.</summary>
    internal void ApplyAccentSwatchColours(HavenUiAppearance appearance)
    {
        for (var index = 0; index < _accentSwatchButtons.Count && index < AccentColourCatalog.Colours.Count; index++)
        {
            var anchors = AccentColourCatalog.Resolve(AccentColourCatalog.Colours[index], appearance);
            _accentSwatchButtons[index].SetValue(HavenProperties.Background, anchors.Primary);
        }
    }

    public void SetDefaultTabModes(IReadOnlyList<string> displayNames, int selectedIndex)
    {
        DefaultTabSelect.Items = displayNames;
        DefaultTabSelect.SelectedIndex = selectedIndex >= 0 && selectedIndex < displayNames.Count
            ? selectedIndex
            : -1;
    }

    private Container BuildApps()
    {
        var section = Section("Settings.Apps");
        var card = Card("Settings.Apps.Card");
        AutoSwitchToggle = NewToggle("Settings.Apps.AutoSwitch");
        AgenticInChatToggle = NewToggle("Settings.Apps.AgenticInChat");
        ConfidenceToggle = NewToggle("Settings.Apps.Confidence");
        AutoCompactToggle = NewToggle("Settings.Apps.AutoCompact");
        AdaptiveHelpToggle = NewToggle("Settings.Apps.AdaptiveHelp");
        BrowserSideToggle = NewToggle("Settings.Apps.BrowserSide");
        AutoWakeToggle = NewToggle("Settings.Apps.AutoWake");
        card.Add(SettingRow("Compatible-model switching", "Allow Haven to switch to a compatible model when a selected model cannot perform a requested capability.", AutoSwitchToggle));
        card.Add(SettingRow("Agentic capabilities in Chat", "Expose action-capable features in normal Haven Chat.", AgenticInChatToggle));
        card.Add(SettingRow("Confidence indicators", "Show confidence indicators where the product supports them.", ConfidenceToggle));
        card.Add(SettingRow("Automatic context compaction", "Compact conversation context automatically near the configured threshold.", AutoCompactToggle));
        CompactPercentSlider = new Slider { Name = "Settings.Apps.CompactPercent", Minimum = 50, Maximum = 95, Step = 1 };
        CompactPercentValue = Muted("Settings.Apps.CompactPercentValue", string.Empty);
        card.Add(CompactPercentValue);
        card.Add(CompactPercentSlider);
        card.Add(SettingRow("Adaptive project help", "Allow Haven to adapt assistance to the active project context.", AdaptiveHelpToggle));
        card.Add(SettingRow("Browser side assistant", "Enable Haven's browser-side assistance preference.", BrowserSideToggle));
        card.Add(SettingRow("Wake Ollama automatically", "Start the local runtime when a local-model request finds it offline.", AutoWakeToggle));
        SaveFeaturesButton = new HavenButton { Name = "Settings.Apps.Save", Content = "Save Chat & Apps settings", Variant = ButtonVariant.Primary };
        card.Add(SaveFeaturesButton);
        section.Add(card);
        return section;
    }

    private Container BuildPermissions()
    {
        var section = Section("Settings.Permissions");
        var card = Card("Settings.Permissions.Card");
        var names = Enum.GetNames<PermissionMode>();
        FilePermissionSelect = NewSelect("Settings.Permissions.Files", names);
        CommandPermissionSelect = NewSelect("Settings.Permissions.Commands", names);
        BrowserPermissionSelect = NewSelect("Settings.Permissions.Browser", names);
        ComputerPermissionSelect = NewSelect("Settings.Permissions.Computer", names);
        card.Add(SettingRow("Files", "Controls whether Haven may read or change local files requested by tools. Ask requires approval, Deny blocks access, and Allow permits it within the configured scope.", FilePermissionSelect));
        card.Add(SettingRow("Commands", "Controls local command execution. Commands may start processes, change files or use the network depending on the command; Ask requires approval before execution.", CommandPermissionSelect));
        card.Add(SettingRow("Browser", "Controls browser actions that can read the current page and perform navigation or form actions. Use Ask when you want approval before Haven acts.", BrowserPermissionSelect));
        card.Add(SettingRow("Device use", "Controls supported device-control actions such as clicking, typing or changing device state. Ask requires approval before the action.", ComputerPermissionSelect));
        SavePermissionsButton = new HavenButton { Name = "Settings.Permissions.Save", Content = "Save permission defaults", Variant = ButtonVariant.Primary };
        card.Add(SavePermissionsButton);
        section.Add(card);
        return section;
    }

    private Container BuildIntegrations() => BuildConnectionsSection();

    private Container BuildExtensions()
    {
        var section = Section("Settings.Extensions");
        var source = Card("Settings.Extensions.Sources");
        source.Add(Heading("Settings.Extensions.Sources.Heading", "Sources / Repositories", 18));
        source.Add(Muted("Settings.Extensions.Sources.Description", "A GitHub repository may publish multiple Plugins, multiple Skills, or combined bundles. Private sources use an existing connected-account reference; Haven never requests a GitHub password."));
        ExtensionSourceNameInput = new Input { Name = "Settings.Extensions.SourceName", Placeholder = "Source name" };
        ExtensionSourceUriInput = new Input { Name = "Settings.Extensions.SourceUri", Placeholder = "https://github.com/owner/repository" };
        ExtensionPrivateToggle = new Toggle { Name = "Settings.Extensions.Private" };
        ExtensionConnectedAccountInput = new Input { Name = "Settings.Extensions.ConnectedAccount", Placeholder = "Connected GitHub account reference" };
        ExtensionUpdateModeSelect = NewSelect("Settings.Extensions.UpdateMode", Enum.GetNames<ExtensionUpdateMode>());
        ExtensionUpdateModeSelect.SelectedIndex = 1;
        ExtensionAddSourceButton = new HavenButton { Name = "Settings.Extensions.AddSource", Content = "Add GitHub Repository", Variant = ButtonVariant.Primary };
        ExtensionSourceSelect = NewSelect("Settings.Extensions.Source", []);
        ExtensionRefreshButton = new HavenButton { Name = "Settings.Extensions.Refresh", Content = "Refresh selected source", Variant = ButtonVariant.Secondary };
        ExtensionRemoveSourceButton = new HavenButton { Name = "Settings.Extensions.RemoveSource", Content = "Remove selected source", Variant = ButtonVariant.Danger };
        source.Add(ExtensionSourceNameInput);
        source.Add(ExtensionSourceUriInput);
        source.Add(SettingRow("Private repository", "Requires an authorised connected GitHub account reference. Authentication stays in Haven's credential infrastructure.", ExtensionPrivateToggle));
        source.Add(ExtensionConnectedAccountInput);
        source.Add(SettingRow("Update mode", "Manual, Notify, or Automatic. Permission expansion always requires a separate review.", ExtensionUpdateModeSelect));
        source.Add(ExtensionAddSourceButton);
        source.Add(ExtensionSourceSelect);
        source.Add(ExtensionRefreshButton);
        source.Add(ExtensionRemoveSourceButton);
        section.Add(source);

        var available = Card("Settings.Extensions.Available");
        available.Add(Heading("Settings.Extensions.Available.Heading", "Available Plugins & Skills", 18));
        AvailableExtensionSelect = NewSelect("Settings.Extensions.AvailableSelect", []);
        AvailableExtensionDetails = Muted("Settings.Extensions.AvailableDetails", "Refresh a source to discover validated packages.");
        ExtensionInstallButton = new HavenButton { Name = "Settings.Extensions.Install", Content = "Review permissions", Variant = ButtonVariant.Primary };
        available.Add(AvailableExtensionSelect);
        available.Add(AvailableExtensionDetails);
        available.Add(ExtensionInstallButton);
        section.Add(available);

        var installed = Card("Settings.Extensions.Installed");
        installed.Add(Heading("Settings.Extensions.Installed.Heading", "Installed Plugins & Skills", 18));
        InstalledExtensionSelect = NewSelect("Settings.Extensions.InstalledSelect", []);
        InstalledExtensionDetails = Muted("Settings.Extensions.InstalledDetails", "No installed packages loaded.");
        ExtensionToggleButton = new HavenButton { Name = "Settings.Extensions.Toggle", Content = "Enable / disable", Variant = ButtonVariant.Secondary };
        ExtensionUninstallButton = new HavenButton { Name = "Settings.Extensions.Uninstall", Content = "Uninstall", Variant = ButtonVariant.Danger };
        installed.Add(InstalledExtensionSelect);
        installed.Add(InstalledExtensionDetails);
        installed.Add(ExtensionToggleButton);
        installed.Add(ExtensionUninstallButton);
        section.Add(installed);
        ExtensionStatusText = Muted("Settings.Extensions.Status", string.Empty);
        section.Add(ExtensionStatusText);
        return section;
    }

    public void SetExtensionSources(IReadOnlyList<string> values, int selectedIndex)
    {
        ExtensionSourceSelect.Items = values;
        ExtensionSourceSelect.SelectedIndex = values.Count == 0 ? -1 : Math.Clamp(selectedIndex, 0, values.Count - 1);
    }

    public void SetAvailableExtensions(IReadOnlyList<string> values, int selectedIndex)
    {
        AvailableExtensionSelect.Items = values;
        AvailableExtensionSelect.SelectedIndex = values.Count == 0 ? -1 : Math.Clamp(selectedIndex, 0, values.Count - 1);
    }

    public void SetInstalledExtensions(IReadOnlyList<string> values, int selectedIndex)
    {
        InstalledExtensionSelect.Items = values;
        InstalledExtensionSelect.SelectedIndex = values.Count == 0 ? -1 : Math.Clamp(selectedIndex, 0, values.Count - 1);
    }

    private Container BuildVoice()
    {
        var section = Section("Settings.Voice");
        var card = Card("Settings.Voice.Card");
        card.Add(Heading("Settings.Voice.Heading", "Voice profiles", 18));
        VoiceProfileStatus = Muted("Settings.Voice.Status", string.Empty);
        card.Add(VoiceProfileStatus);
        card.Add(Muted("Settings.Voice.Description", "Custom voice profiles are persisted by UserPreferencesService. This page reports real stored state without inventing unsupported microphone or notification switches."));
        section.Add(card);
        return section;
    }

    private Container BuildPrivacy()
    {
        var section = Section("Settings.Privacy");

        var local = Card("Settings.Privacy.Local");
        local.Add(Heading("Settings.Privacy.LocalHeading", "Local preferences", 18));
        local.Add(Muted("Settings.Privacy.LocalDescription", "These privacy choices are persisted in the configured local application-data directory and enforced by Haven services."));
        LocalOnlyToggle = NewToggle("Settings.Privacy.LocalOnly");
        local.Add(SettingRow("Local-only mode", "Restrict model discovery and generation to local providers. Cloud providers remain configured but are not used while this is on.", LocalOnlyToggle));
        section.Add(local);

        var learning = Card("Settings.Privacy.BackgroundLearning");
        learning.Add(Heading("Settings.Privacy.BackgroundHeading", "Background Learning", 18));
        learning.Add(Muted("Settings.Privacy.BackgroundDescription", "Control what Haven may learn in the background. Running work is resource-gated and reports real task states rather than simulated progress."));
        BackgroundLearningToggle = NewToggle("Settings.Privacy.BackgroundLearning.Enabled");
        learning.Add(SettingRow("Background Learning", "Turn all background learning on or off.", BackgroundLearningToggle));
        BackgroundModeSelect = NewSelect("Settings.Privacy.BackgroundLearning.Mode", Enum.GetNames<BackgroundLearningMode>());
        learning.Add(SettingRow("Learning mode", "Minimal and Balanced avoid battery-heavy work; Maximum permits more resource use.", BackgroundModeSelect));
        foreach (var category in Enum.GetValues<KnowledgeCategory>())
        {
            var toggle = NewToggle($"Settings.Privacy.BackgroundLearning.Category.{category}");
            LearningCategoryToggles[category] = toggle;
            learning.Add(SettingRow(category.ToString(), $"Allow Background Learning tasks for {category}.", toggle));
        }
        LearningRefreshButton = new HavenButton { Name = "Settings.Privacy.BackgroundLearning.Refresh", Content = "Refresh learning status", Variant = ButtonVariant.Secondary };
        LearningStatusText = Muted("Settings.Privacy.BackgroundLearning.Status", "Loading Background Learning state…");
        learning.Add(LearningRefreshButton);
        learning.Add(LearningStatusText);
        ModelImprovementSharingToggle = NewToggle("Settings.Privacy.ModelImprovementSharingEnabled");
        learning.Add(SettingRow("Share for model improvement", "Allow explicitly eligible data to be considered for model-improvement sharing. This stays off by default and does not itself transmit data.", ModelImprovementSharingToggle));
        SavePrivacyButton = new HavenButton { Name = "Settings.Privacy.Save", Content = "Save privacy choices", Variant = ButtonVariant.Primary };
        learning.Add(SavePrivacyButton);
        section.Add(learning);

        var storage = Card("Settings.Privacy.Storage");
        storage.Add(Heading("Settings.Privacy.StorageHeading", "Storage and cleanup", 18));
        storage.Add(Muted("Settings.Privacy.StorageDescription", "Background knowledge is capped at 512 MB. API Bank has a separate 1 GB budget. Pinned records are protected from automatic cleanup."));
        LearningStorageText = Muted("Settings.Privacy.StorageStatus", "Loading storage usage…");
        LearningCleanupButton = new HavenButton { Name = "Settings.Privacy.Cleanup", Content = "Clean up stale knowledge", Variant = ButtonVariant.Secondary };
        storage.Add(LearningStorageText);
        storage.Add(LearningCleanupButton);
        section.Add(storage);

        var activity = Card("Settings.Privacy.Activity");
        activity.Add(Heading("Settings.Privacy.ActivityHeading", "Background activity", 18));
        LearningTaskSelect = NewSelect("Settings.Privacy.Activity.Task", []);
        LearningTaskDetails = Muted("Settings.Privacy.Activity.Details", "No background-learning tasks yet.");
        LearningTaskPauseButton = new HavenButton { Name = "Settings.Privacy.Activity.Pause", Content = "Pause", Variant = ButtonVariant.Secondary };
        LearningTaskResumeButton = new HavenButton { Name = "Settings.Privacy.Activity.Resume", Content = "Resume", Variant = ButtonVariant.Secondary };
        LearningTaskCancelButton = new HavenButton { Name = "Settings.Privacy.Activity.Cancel", Content = "Cancel task", Variant = ButtonVariant.Danger };
        activity.Add(LearningTaskSelect);
        activity.Add(LearningTaskDetails);
        activity.Add(LearningTaskPauseButton);
        activity.Add(LearningTaskResumeButton);
        activity.Add(LearningTaskCancelButton);
        section.Add(activity);

        var learnMe = Card("Settings.Privacy.LearnMe");
        learnMe.Add(Heading("Settings.Privacy.LearnMeHeading", "Learn Me", 18));
        learnMe.Add(Muted("Settings.Privacy.LearnMeDescription", "Inspect what Haven believes about you, including why it learned it, confidence, freshness and provenance. Corrections become explicit user-authoritative records."));
        LearnMeSelect = NewSelect("Settings.Privacy.LearnMe.Item", []);
        LearnMeDetails = Muted("Settings.Privacy.LearnMe.Details", "No Learn Me records stored.");
        LearnMeCorrectionInput = new Input { Name = "Settings.Privacy.LearnMe.Correction", Placeholder = "Enter the corrected information" };
        LearnMeCorrectionInput.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        LearnMeCorrectButton = new HavenButton { Name = "Settings.Privacy.LearnMe.Correct", Content = "Save correction", Variant = ButtonVariant.Primary };
        LearnMePinButton = new HavenButton { Name = "Settings.Privacy.LearnMe.Pin", Content = "Pin / unpin", Variant = ButtonVariant.Secondary };
        LearnMeRejectButton = new HavenButton { Name = "Settings.Privacy.LearnMe.Reject", Content = "Reject inference", Variant = ButtonVariant.Secondary };
        LearnMeForgetButton = new HavenButton { Name = "Settings.Privacy.LearnMe.Forget", Content = "Forget item", Variant = ButtonVariant.Danger };
        learnMe.Add(LearnMeSelect);
        learnMe.Add(LearnMeDetails);
        learnMe.Add(LearnMeCorrectionInput);
        learnMe.Add(LearnMeCorrectButton);
        learnMe.Add(LearnMePinButton);
        learnMe.Add(LearnMeRejectButton);
        learnMe.Add(LearnMeForgetButton);
        section.Add(learnMe);

        var api = Card("Settings.Privacy.ApiBank");
        api.Add(Heading("Settings.Privacy.ApiBankHeading", "API Bank", 18));
        api.Add(Muted("Settings.Privacy.ApiBankDescription", "A separate local knowledge store for API actions, inputs, outputs, auth/scopes, limits, pricing, examples, provenance, versions and deprecations. Credentials are never stored here as ordinary knowledge."));
        ApiBankSelect = NewSelect("Settings.Privacy.ApiBank.Item", []);
        ApiBankDetails = Muted("Settings.Privacy.ApiBank.Details", "No API Bank records stored.");
        ApiBankPinButton = new HavenButton { Name = "Settings.Privacy.ApiBank.Pin", Content = "Pin / unpin", Variant = ButtonVariant.Secondary };
        ApiBankRemoveButton = new HavenButton { Name = "Settings.Privacy.ApiBank.Remove", Content = "Remove API record", Variant = ButtonVariant.Danger };
        api.Add(ApiBankSelect);
        api.Add(ApiBankDetails);
        api.Add(ApiBankPinButton);
        api.Add(ApiBankRemoveButton);
        section.Add(api);

        return section;
    }

    public void SetLearningSnapshot(BackgroundLearningSchedulerSnapshot snapshot, KnowledgeStorageSnapshot storage, IReadOnlyList<KnowledgeRecord> learnMe, IReadOnlyList<ApiBankRecord> apiRecords)
    {
        BackgroundLearningToggle.IsChecked = snapshot.IsGloballyEnabled;
        SelectText(BackgroundModeSelect, snapshot.Mode.ToString());
        foreach (var (category, toggle) in LearningCategoryToggles)
            toggle.IsChecked = snapshot.Categories.TryGetValue(category, out var enabled) && enabled;
        LearningStorageText.Content = $"Knowledge: {FormatBytes(storage.KnowledgeBytes)} of {FormatBytes(storage.KnowledgeLimitBytes)} · {storage.KnowledgeCount} records ({storage.KnowledgePinnedCount} pinned). API Bank: {FormatBytes(storage.ApiBankBytes)} of {FormatBytes(storage.ApiBankLimitBytes)} · {storage.ApiBankCount} records ({storage.ApiBankPinnedCount} pinned).";
        LearningStatusText.Content = $"{(snapshot.IsGloballyEnabled ? "Enabled" : "Disabled")} · {snapshot.Mode} · {snapshot.Tasks.Count} tracked task(s)" + (snapshot.LastChangedAt is { } changed ? $" · settings updated {changed.LocalDateTime:g}" : string.Empty);
        LearnMeSelect.Items = learnMe.Select(item => $"{item.Title} · {item.Status}").ToArray();
        LearnMeSelect.SelectedIndex = learnMe.Count > 0 ? Math.Clamp(LearnMeSelect.SelectedIndex, 0, learnMe.Count - 1) : -1;
        ApiBankSelect.Items = apiRecords.Select(item => $"{item.Application} · {item.ApiName} {item.Version}").ToArray();
        ApiBankSelect.SelectedIndex = apiRecords.Count > 0 ? Math.Clamp(ApiBankSelect.SelectedIndex, 0, apiRecords.Count - 1) : -1;
        LearningTaskSelect.Items = snapshot.Tasks.Select(item => $"{item.Title} · {item.Status}").ToArray();
        LearningTaskSelect.SelectedIndex = snapshot.Tasks.Count > 0 ? Math.Clamp(LearningTaskSelect.SelectedIndex, 0, snapshot.Tasks.Count - 1) : -1;
    }

    public void ShowLearnMe(KnowledgeRecord? record)
    {
        if (record is null) { LearnMeDetails.Content = "No Learn Me records stored."; return; }
        var provenance = record.Sources.Count == 0 ? "No source metadata" : string.Join("; ", record.Sources.Select(source => $"{source.Title} ({source.SourceType}){(string.IsNullOrWhiteSpace(source.Url) ? string.Empty : $" — {source.Url}")}"));
        LearnMeDetails.Content = $"{record.Summary}\nOrigin: {record.Origin} · Confidence: {record.Confidence:P0} · Freshness: {record.Freshness} · Status: {record.Status} · Scope: {record.Scope} · Pinned: {record.IsPinned}\nLast confirmed: {(record.LastConfirmedAt?.LocalDateTime.ToString("g") ?? "not confirmed")} · Updated: {record.UpdatedAt.LocalDateTime:g}\nWhy Haven learned this: {record.LearnedBecause}\nProvenance: {provenance}";
    }

    public void ShowApi(ApiBankRecord? record)
    {
        if (record is null) { ApiBankDetails.Content = "No API Bank records stored."; return; }
        ApiBankDetails.Content = $"{record.Application} — {record.ApiName} {record.Version}\nDocs/source: {record.DocumentationUrl}{(string.IsNullOrWhiteSpace(record.SourceUrl) ? string.Empty : $" · {record.SourceUrl}")}\nAuth: {record.Authentication} · Scopes: {record.ScopesJson} · Internet: {record.RequiresInternet} · Credentials required: {record.RequiresCredentials}\nRate limits: {record.RateLimits} · Pricing: {record.Pricing} · Per-request cost: {(record.CostPerRequest?.ToString() ?? "not specified")}\nInputs: {record.InputsJson}\nOutputs: {record.OutputsJson}\nCapabilities: {record.CapabilityNotes}\nLimitations: {record.Limitations}\nOffline queue: {record.OfflineQueuePolicy} · Deprecation: {record.Deprecation ?? "none"} · Last verified: {record.LastCheckedAt.LocalDateTime:g} · Pinned: {record.IsPinned}";
    }

    public void ShowTask(BackgroundLearningTask? task)
    {
        if (task is null) { LearningTaskDetails.Content = "No background-learning tasks yet."; return; }
        LearningTaskDetails.Content = $"Source: {task.Source} · Category: {task.Category} · Priority: {task.Priority} · Status: {task.Status}\nCreated: {task.CreatedAt.LocalDateTime:g} · Started: {(task.StartedAt?.LocalDateTime.ToString("g") ?? "not started")} · Last run: {(task.LastRunAt?.LocalDateTime.ToString("g") ?? "never")} · Completed: {(task.CompletedAt?.LocalDateTime.ToString("g") ?? "not completed")}\nRequires network: {task.RequiresNetwork} · Requires model: {task.RequiresModel}\nResult: {task.Result ?? "none"}\nError: {task.Error ?? "none"}";
    }

    public void SetLearningFeedback(string text) => LearningStatusText.Content = text;

    private static string FormatBytes(long bytes)
        => bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024d * 1024 * 1024):0.##} GB"
            : bytes >= 1024L * 1024 ? $"{bytes / (1024d * 1024):0.##} MB"
            : $"{bytes / 1024d:0.##} KB";

    private Container BuildAdvanced()
    {
        var section = Section("Settings.Advanced");
        var card = Card("Settings.Advanced.Card");
        TemperatureSlider = new Slider { Name = "Settings.Advanced.Temperature", Minimum = 0, Maximum = 2, Step = .1 };
        ContextLimitSlider = new Slider { Name = "Settings.Advanced.ContextLimit", Minimum = 2048, Maximum = 262144, Step = 1024 };
        ActionLimitSlider = new Slider { Name = "Settings.Advanced.ActionLimit", Minimum = 1, Maximum = 100, Step = 1 };
        TemperatureValue = Muted("Settings.Advanced.TemperatureValue", string.Empty);
        ContextLimitValue = Muted("Settings.Advanced.ContextLimitValue", string.Empty);
        ActionLimitValue = Muted("Settings.Advanced.ActionLimitValue", string.Empty);
        card.Add(TemperatureValue);
        card.Add(TemperatureSlider);
        card.Add(ContextLimitValue);
        card.Add(ContextLimitSlider);
        card.Add(ActionLimitValue);
        card.Add(ActionLimitSlider);
        SaveAdvancedButton = new HavenButton { Name = "Settings.Advanced.Save", Content = "Save advanced limits", Variant = ButtonVariant.Primary };
        card.Add(SaveAdvancedButton);
        section.Add(card);
        return section;
    }

    private Container BuildAbout()
    {
        var section = Section("Settings.About");
        var card = Card("Settings.About.Card");
        card.Add(Heading("Settings.About.Heading", "Haven Settings", 18));
        card.Add(Muted("Settings.About.Description", "This production Settings surface is rendered through Haven.UI. Mutable controls are limited to capabilities backed by current services and persistence."));
        section.Add(card);
        return section;
    }

    private static Container Section(string name)
    {
        var section = new Container { Name = name, Layout = HavenLayout.Vertical };
        section.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        section.SetValue(HavenProperties.MaxWidth, HavenLength.Px(860));
        section.SetValue(HavenProperties.Gap, HavenLength.Px(12));
        return section;
    }

    private static Container Card(string name)
    {
        var card = new Container { Name = name, Layout = HavenLayout.Vertical };
        card.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        card.SetValue(HavenProperties.Background, "SurfaceRaised");
        card.SetValue(HavenProperties.BorderColor, "Border");
        card.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(18)));
        card.SetValue(HavenProperties.Shadow, "Card");
        card.SetValue(HavenProperties.Padding, HavenThickness.Parse("16px"));
        card.SetValue(HavenProperties.Gap, HavenLength.Px(9));
        return card;
    }

    private static Container SettingRow(string title, string description, HavenElement control)
    {
        var row = new Container { Layout = HavenLayout.Vertical };
        row.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        row.SetValue(HavenProperties.Gap, HavenLength.Px(5));
        row.SetValue(HavenProperties.Padding, HavenThickness.Parse("7px 0px"));
        row.Add(Heading(null, title, 14));
        row.Add(Muted(null, description));
        control.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        row.Add(control);
        return row;
    }

    private static Select NewSelect(string name, IReadOnlyList<string> items)
    {
        var select = new Select { Name = name, Items = items };
        select.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        select.SetValue(HavenProperties.MaxWidth, HavenLength.Px(420));
        return select;
    }

    private static Toggle NewToggle(string name) => new() { Name = name };

    private static HavenText Heading(string? name, string content, double size)
    {
        var text = new HavenText(content) { Name = name, Level = TextLevel.H3 };
        text.SetValue(HavenProperties.FontSize, size);
        text.SetValue(HavenProperties.FontWeight, 750);
        return text;
    }

    private static HavenText Muted(string? name, string content)
    {
        var text = new HavenText(content) { Name = name, Level = TextLevel.Paragraph };
        text.SetValue(HavenProperties.Foreground, "TextSecondary");
        text.SetValue(HavenProperties.FontSize, 12d);
        return text;
    }

    private static void SelectText(Select select, string text)
    {
        var index = select.Items.ToList().FindIndex(item => item.Equals(text, StringComparison.OrdinalIgnoreCase));
        select.SelectedIndex = index >= 0 ? index : select.Items.Count > 0 ? 0 : -1;
    }

    private void OnSearchInvoked(object? sender, EventArgs e) => RunSearch();
    private void OnDeleteSelectedInvoked(object? sender, EventArgs e)
    {
        if (InstalledModelSelect.SelectedIndex >= 0) SetDeleteConfirmation(true);
        else SetStatus("Choose an installed model first.");
    }
    private void OnCancelDeleteInvoked(object? sender, EventArgs e) => SetDeleteConfirmation(false);
    private void OnAdvancedValueChanged(object? sender, EventArgs e) => UpdateAdvancedValues();
    private void OnCompactValueChanged(object? sender, EventArgs e) => UpdateCompactValue();

    private void UpdateAdvancedValues()
    {
        TemperatureValue.Content = $"Temperature: {TemperatureSlider.Value:0.0}";
        ContextLimitValue.Content = $"Context limit: {Math.Round(ContextLimitSlider.Value):N0}";
        ActionLimitValue.Content = $"Maximum tool actions per turn: {Math.Round(ActionLimitSlider.Value):N0}";
    }

    private void UpdateCompactValue() => CompactPercentValue.Content = $"Compact context at {Math.Round(CompactPercentSlider.Value):N0}%";

    public void Dispose()
    {
        SearchButton.Invoked -= OnSearchInvoked;
        DeleteSelectedButton.Invoked -= OnDeleteSelectedInvoked;
        CancelDeleteButton.Invoked -= OnCancelDeleteInvoked;
        TemperatureSlider.ValueChanged -= OnAdvancedValueChanged;
        ContextLimitSlider.ValueChanged -= OnAdvancedValueChanged;
        ActionLimitSlider.ValueChanged -= OnAdvancedValueChanged;
        CompactPercentSlider.ValueChanged -= OnCompactValueChanged;
    }
}
