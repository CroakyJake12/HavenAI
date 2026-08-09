using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Events;
using Haven.Desktop.HavenUI.Components;
using Haven.Desktop.HavenUI.Components.Buttons;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Settings;

public sealed partial class SettingsPage : UserControl
{
    private readonly HavenEventBus _bus;
    private readonly UserPreferencesService _preferences;
    private readonly IOllamaClient _ollama;
    private static readonly string[] RecommendedModels = ["qwen3:4b", "gemma3:4b", "llama3.2:3b", "deepseek-r1:1.5b"];

    private IReadOnlyList<ModelDescriptor> _models = [];
    private ModelDescriptor? _selectedModel;
    private EffortLevel _selectedEffort = EffortLevel.Medium;
    private PermissionMode[] _permissionModes = Enum.GetValues<PermissionMode>();
    private string _activeSection = "models";
    private readonly Dictionary<string, Control> _sections = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Control> _navButtons = new(StringComparer.OrdinalIgnoreCase);

    private readonly HavenComboBox _modelCombo = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly HavenComboBox _effortCombo = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly HavenButton _refreshModelsButton = new() { Content = "Refresh" };
    private readonly HavenButton _saveDefaultsButton = new() { Content = "Save defaults", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly HavenButton _deleteModelButton = new() { Content = "Delete selected model", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly HavenAdaptiveSurface _deleteConfirmBorder = new() { Classes = { "permission" }, IsVisible = false };
    private readonly HavenTextInput _installModelBox = new() { PlaceholderText = "e.g. qwen3:4b" };
    private readonly HavenButton _browseModelsButton = new() { Content = "Browse models" };
    private readonly HavenButton _installModelButton = new() { Content = "Install" };
    private readonly HavenProgressBar _installProgress = new() { Minimum = 0, Maximum = 1, Height = 8 };
    private readonly HavenAdaptiveSurface _recommendedModelsBorder = new() { Classes = { "permission" }, IsVisible = false };
    private readonly StackPanel _recommendedModelsPanel = new();
    private readonly HavenButton _huggingFaceSearchButton = new() { Content = "Search public Hugging Face GGUF models", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly WrapPanel _modelsPanel = new();
    private readonly HavenTextInput _modelSearchBox = new() { PlaceholderText = "Search installed models" };

    private readonly HavenCheckBox _autoSwitchCheck = new() { Content = "Automatically switch to a compatible model" };
    private readonly HavenCheckBox _agenticInChatCheck = new() { Content = "Show agentic plugins in Haven Chat" };
    private readonly HavenCheckBox _confidenceCheck = new() { Content = "Show confidence indicators" };
    private readonly HavenCheckBox _autoCompactCheck = new() { Content = "Compact context automatically" };
    private readonly HavenNumericInput _compactPercentBox = new() { Minimum = 50, Maximum = 95 };
    private readonly HavenCheckBox _adaptiveHelpCheck = new() { Content = "Adaptive project help" };
    private readonly HavenCheckBox _browserSideCheck = new() { Content = "Browser side assistant" };
    private readonly HavenCheckBox _autoWakeCheck = new() { Content = "Wake Ollama automatically when a local model is offline" };
    private readonly HavenButton _saveFeaturesButton = new() { Content = "Save feature settings", HorizontalAlignment = HorizontalAlignment.Stretch };

    private readonly HavenComboBox _filePermCombo = new();
    private readonly HavenComboBox _commandPermCombo = new();
    private readonly HavenComboBox _browserPermCombo = new();
    private readonly HavenComboBox _computerPermCombo = new();
    private readonly HavenButton _savePermissionsButton = new() { Content = "Save permissions", HorizontalAlignment = HorizontalAlignment.Stretch };

    private readonly HavenNumericInput _temperatureBox = new() { Minimum = 0, Maximum = 2, Increment = 0.1m, FormatString = "0.0" };
    private readonly HavenNumericInput _contextLimitBox = new() { Minimum = 2048, Maximum = 262144, Increment = 1024 };
    private readonly HavenNumericInput _actionLimitBox = new() { Minimum = 1, Maximum = 100 };
    private readonly HavenButton _saveAdvancedButton = new() { Content = "Save advanced limits", HorizontalAlignment = HorizontalAlignment.Stretch };

    private readonly TextBlock _statusText = new() { Classes = { "muted" }, TextWrapping = TextWrapping.Wrap };

    public SettingsPage(HavenEventBus bus, UserPreferencesService preferences, IOllamaClient ollama)
    {
        _bus = bus;
        _preferences = preferences;
        _ollama = ollama;

        InitializeComponent();
        BuildUI();
        LoadPreferences();
        WireEvents();
        NavigateTo("models");
        _ = LoadModelsAsync();
    }

    private void BuildUI()
    {
        var root = this.FindControl<Grid>("SettingsRoot")!;

        // Sidebar
        var sidebar = new HavenSidebarSurface { Padding = new Thickness(12, 16), CornerRadius = new CornerRadius(0) };
        var sidebarContent = new StackPanel { Spacing = 4 };
        sidebarContent.Children.Add(new TextBlock
        {
            Text = "Settings",
            FontSize = 18,
            FontWeight = FontWeight.ExtraBold,
            Margin = new Thickness(8, 0, 0, 16)
        });
        var searchBox = new HavenSearchInput { PlaceholderText = "Search settings\u2026", Margin = new Thickness(0, 0, 0, 12) };
        searchBox.TextChanged += (_, _) =>
        {
            var query = searchBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(query)) return;
            var match = FindSectionByKeyword(query);
            if (match is not null) NavigateTo(match);
        };
        sidebarContent.Children.Add(searchBox);

        var sectionButtons = new StackPanel { Spacing = 2 };
        foreach (var (key, label) in new[] { ("models", "Models"), ("connections", "Connections"), ("appearance", "Appearance"), ("features", "Features"), ("permissions", "Permissions"), ("privacy", "Privacy"), ("advanced", "Advanced"), ("about", "About") })
        {
            var button = new HavenNavigationButton
            {
                Content = label,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            button.Click += (_, _) => NavigateTo(key);
            _navButtons[key] = button;
            sectionButtons.Children.Add(button);
        }
        sidebarContent.Children.Add(sectionButtons);
        sidebar.Child = sidebarContent;
        root.Children.Add(sidebar);

        // Content
        var scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var contentPanel = new StackPanel { Margin = new Thickness(28, 20, 28, 120), Spacing = 14, MaxWidth = 860 };
        scroller.Content = contentPanel;
        Grid.SetColumn(scroller, 1);
        root.Children.Add(scroller);

        // Build sections
        _sections["models"] = BuildModelsSection();
        _sections["connections"] = BuildConnectionsSection();
        _sections["appearance"] = BuildAppearanceSection();
        _sections["features"] = BuildFeaturesSection();
        _sections["permissions"] = BuildPermissionsSection();
        _sections["privacy"] = BuildPrivacySection();
        _sections["advanced"] = BuildAdvancedSection();
        _sections["about"] = BuildAboutSection();

        // Store reference for navigation
        _contentPanel = contentPanel;
    }

    private StackPanel? _contentPanel;

    private void NavigateTo(string section)
    {
        _activeSection = section;
        if (_contentPanel is null) return;
        _contentPanel.Children.Clear();
        if (_sections.TryGetValue(section, out var content))
            _contentPanel.Children.Add(content);

        foreach (var (key, btn) in _navButtons)
            btn.Classes.Set("selected", key == section);
    }

    private Control BuildModelsSection()
    {
        var panel = new StackPanel { Spacing = 14 };
        panel.Children.Add(SectionHeader("MODELS", "Model browser and local model management"));

        // Model residency
        var residencyCard = new HavenAdaptiveSurface { Classes = { "card" } };
        var residencyPanel = new StackPanel { Spacing = 10 };
        residencyPanel.Children.Add(new TextBlock { Text = "MODEL RESIDENCY", Classes = { "eyebrow" } });
        residencyPanel.Children.Add(new TextBlock { Text = "Keep Haven Models Ready", Classes = { "sectionHeading" }, Margin = new Thickness(0, 4, 0, 0) });
        residencyPanel.Children.Add(new TextBlock
        {
            Text = "When enabled, the configured local model stays loaded after Haven closes. This makes reopening faster but uses more memory.",
            Classes = { "muted" }, TextWrapping = TextWrapping.Wrap
        });
        var alwaysLoadedCheck = new HavenCheckBox
        {
            Content = "Keep models loaded when Haven is closed",
            IsChecked = _preferences.AlwaysLoaded
        };
        alwaysLoadedCheck.IsCheckedChanged += (_, _) =>
        {
            _preferences.SetAlwaysLoaded(alwaysLoadedCheck.IsChecked == true);
            _statusText.Text = alwaysLoadedCheck.IsChecked == true
                ? "Models will stay loaded after Haven closes."
                : "Models will unload when Haven closes.";
        };
        residencyPanel.Children.Add(alwaysLoadedCheck);
        residencyPanel.Children.Add(new TextBlock
        {
            Text = "On Android, this creates a persistent notification. On Windows, a system tray indicator shows when models are resident.",
            FontSize = 11, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap
        });
        residencyCard.Child = residencyPanel;
        panel.Children.Add(residencyCard);

        // Installed models
        var modelsCard = new HavenAdaptiveSurface { Classes = { "card" } };
        var modelsPanel = new StackPanel { Spacing = 10 };
        modelsPanel.Children.Add(new TextBlock { Text = "LOCAL MODELS", Classes = { "eyebrow" } });
        modelsPanel.Children.Add(new TextBlock { Text = "Installed models", Classes = { "sectionHeading" }, Margin = new Thickness(0, 4, 0, 0) });
        var searchRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        searchRow.Children.Add(_modelSearchBox);
        searchRow.Children.Add(_refreshModelsButton);
        Grid.SetColumn(_refreshModelsButton, 1);
        modelsPanel.Children.Add(searchRow);
        modelsPanel.Children.Add(_modelsPanel);
        var activeRow = new StackPanel { Spacing = 8 };
        activeRow.Children.Add(new TextBlock { Text = "Active model", Classes = { "muted" } });
        activeRow.Children.Add(_modelCombo);
        activeRow.Children.Add(new TextBlock { Text = "Reasoning effort", Classes = { "muted" } });
        activeRow.Children.Add(_effortCombo);
        activeRow.Children.Add(_saveDefaultsButton);
        activeRow.Children.Add(_deleteModelButton);
        modelsPanel.Children.Add(activeRow);

        // Delete confirmation
        var deletePanel = new StackPanel { Spacing = 8 };
        deletePanel.Children.Add(new TextBlock { Text = "Permanently remove the selected model from Ollama?", TextWrapping = TextWrapping.Wrap });
        var deleteRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8 };
        var cancelBtn = new HavenButton { Content = "Cancel" };
        cancelBtn.Click += (_, _) => _deleteConfirmBorder.IsVisible = false;
        deleteRow.Children.Add(cancelBtn);
        var confirmBtn = new HoldToConfirmButton { Content = "Delete model", ActionLabel = "delete model" };
        confirmBtn.Click += async (_, _) => await DeleteSelectedModelAsync();
        deleteRow.Children.Add(confirmBtn);
        Grid.SetColumn(confirmBtn, 1);
        deletePanel.Children.Add(deleteRow);
        _deleteConfirmBorder.Child = deletePanel;
        modelsPanel.Children.Add(_deleteConfirmBorder);
        modelsCard.Child = modelsPanel;
        panel.Children.Add(modelsCard);

        // Install
        var installCard = new HavenAdaptiveSurface { Classes = { "card" } };
        var installPanel = new StackPanel { Spacing = 10 };
        installPanel.Children.Add(new TextBlock { Text = "INSTALL", Classes = { "eyebrow" } });
        installPanel.Children.Add(new TextBlock { Text = "Install a model", Classes = { "sectionHeading" }, Margin = new Thickness(0, 4, 0, 0) });
        installPanel.Children.Add(new TextBlock { Text = "Enter an Ollama model name or choose a recommended model.", Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });
        installPanel.Children.Add(_installModelBox);
        var installRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8 };
        installRow.Children.Add(_browseModelsButton);
        installRow.Children.Add(_installModelButton);
        Grid.SetColumn(_installModelButton, 1);
        installPanel.Children.Add(installRow);
        installPanel.Children.Add(_installProgress);

        // Recommended
        var recPanel = new StackPanel { Spacing = 8 };
        recPanel.Children.Add(new TextBlock { Text = "Recommended for mobile", Classes = { "sectionHeading" } });
        recPanel.Children.Add(new TextBlock { Text = "These smaller models are practical starting points on phones.", Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });
        BuildRecommendedModels();
        recPanel.Children.Add(_recommendedModelsPanel);
        recPanel.Children.Add(_huggingFaceSearchButton);
        _recommendedModelsBorder.Child = recPanel;
        installPanel.Children.Add(_recommendedModelsBorder);
        installCard.Child = installPanel;
        panel.Children.Add(installCard);

        panel.Children.Add(_statusText);
        return panel;
    }

    private Control BuildConnectionsSection()
    {
        var panel = new StackPanel { Spacing = 14 };
        panel.Children.Add(SectionHeader("CONNECTIONS", "Provider connections and API keys"));
        panel.Children.Add(new ProviderConnectionsView());
        panel.Children.Add(new ReliabilityStatusView());
        return panel;
    }

    private Control BuildAppearanceSection()
    {
        var panel = new StackPanel { Spacing = 14 };
        panel.Children.Add(SectionHeader("APPEARANCE", "Theme, colours and display"));
        panel.Children.Add(new HavenAppearanceSettingsView());
        return panel;
    }

    private Control BuildFeaturesSection()
    {
        var panel = new StackPanel { Spacing = 14 };
        panel.Children.Add(SectionHeader("FEATURES", "Behaviour and capability toggles"));
        var card = new HavenAdaptiveSurface { Classes = { "card" } };
        var cardPanel = new StackPanel { Spacing = 8 };
        cardPanel.Children.Add(_autoSwitchCheck);
        cardPanel.Children.Add(_agenticInChatCheck);
        cardPanel.Children.Add(_confidenceCheck);
        cardPanel.Children.Add(_autoCompactCheck);
        cardPanel.Children.Add(new TextBlock { Text = "Compact when context reaches this percent", Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });
        cardPanel.Children.Add(_compactPercentBox);
        cardPanel.Children.Add(_adaptiveHelpCheck);
        cardPanel.Children.Add(_browserSideCheck);
        cardPanel.Children.Add(_autoWakeCheck);
        cardPanel.Children.Add(_saveFeaturesButton);
        card.Child = cardPanel;
        panel.Children.Add(card);
        return panel;
    }

    private Control BuildPermissionsSection()
    {
        var panel = new StackPanel { Spacing = 14 };
        panel.Children.Add(SectionHeader("PERMISSIONS", "Tool and capability access control"));
        var card = new HavenAdaptiveSurface { Classes = { "card" } };
        var cardPanel = new StackPanel { Spacing = 10 };
        cardPanel.Children.Add(new TextBlock { Text = "Ask is the safest default.", Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });
        cardPanel.Children.Add(PermissionRow("Files", _filePermCombo));
        cardPanel.Children.Add(PermissionRow("Commands", _commandPermCombo));
        cardPanel.Children.Add(PermissionRow("Browser", _browserPermCombo));
        cardPanel.Children.Add(PermissionRow("Device Use", _computerPermCombo));
        cardPanel.Children.Add(_savePermissionsButton);
        card.Child = cardPanel;
        panel.Children.Add(card);
        return panel;
    }

    private Control BuildPrivacySection()
    {
        var panel = new StackPanel { Spacing = 14 };
        panel.Children.Add(SectionHeader("PRIVACY", "Data storage and privacy controls"));
        var card = new HavenAdaptiveSurface { Classes = { "card" } };
        var cardPanel = new StackPanel { Spacing = 7 };
        cardPanel.Children.Add(new TextBlock { Text = "LOCAL DATA", Classes = { "eyebrow" } });
        cardPanel.Children.Add(new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Text = "Haven stores data locally in your user profile. Nothing leaves your device without a provider you explicitly configure." });
        card.Child = cardPanel;
        panel.Children.Add(card);
        return panel;
    }

    private Control BuildAdvancedSection()
    {
        var panel = new StackPanel { Spacing = 14 };
        panel.Children.Add(SectionHeader("ADVANCED", "Generation parameters and limits"));
        var card = new HavenAdaptiveSurface { Classes = { "card" } };
        var cardPanel = new StackPanel { Spacing = 9 };
        cardPanel.Children.Add(new TextBlock { Text = "Temperature (0\u20132)", Classes = { "muted" } });
        cardPanel.Children.Add(_temperatureBox);
        cardPanel.Children.Add(new TextBlock { Text = "Context limit", Classes = { "muted" } });
        cardPanel.Children.Add(_contextLimitBox);
        cardPanel.Children.Add(new TextBlock { Text = "Maximum tool actions per turn", Classes = { "muted" } });
        cardPanel.Children.Add(_actionLimitBox);
        cardPanel.Children.Add(_saveAdvancedButton);
        card.Child = cardPanel;
        panel.Children.Add(card);
        return panel;
    }

    private Control BuildAboutSection()
    {
        var panel = new StackPanel { Spacing = 14 };
        panel.Children.Add(SectionHeader("ABOUT", "Safety and browser information"));
        foreach (var (title, text) in new[] { ("ISOLATED BROWSER", "The browser runs in an isolated profile. Cookies, history and logins never leak between sessions."), ("WORKSPACE SAFETY", "Haven never modifies files outside the active workspace without explicit permission.") })
        {
            var card = new HavenAdaptiveSurface { Classes = { "card" } };
            var cardPanel = new StackPanel { Spacing = 7 };
            cardPanel.Children.Add(new TextBlock { Text = title, Classes = { "eyebrow" } });
            cardPanel.Children.Add(new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Text = text });
            card.Child = cardPanel;
            panel.Children.Add(card);
        }
        return panel;
    }

    private static StackPanel SectionHeader(string eyebrow, string description) => new()
    {
        Spacing = 4,
        Children =
        {
            new TextBlock { Text = eyebrow, Classes = { "eyebrow" } },
            new TextBlock { Text = description, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap }
        }
    };

    private static StackPanel PermissionRow(string label, ComboBox combo) => new()
    {
        Spacing = 4,
        Children = { new TextBlock { Text = label, Classes = { "muted" } }, combo }
    };

    private void LoadPreferences()
    {
        var gen = _preferences.GenerationOptions;
        _temperatureBox.Value = (decimal)gen.Temperature;
        _contextLimitBox.Value = gen.ContextLimit;
        _actionLimitBox.Value = gen.ActionLimit;
        _autoSwitchCheck.IsChecked = _preferences.AutoSwitchCompatibleModels;
        _agenticInChatCheck.IsChecked = _preferences.ShowAgenticInChat;
        _confidenceCheck.IsChecked = _preferences.ConfidenceMeter;
        _autoCompactCheck.IsChecked = _preferences.AutoCompactContext;
        _compactPercentBox.Value = _preferences.CompactAtPercent;
        _adaptiveHelpCheck.IsChecked = _preferences.AdaptiveHelp;
        _browserSideCheck.IsChecked = _preferences.BrowserSideAssistant;
        _autoWakeCheck.IsChecked = _preferences.AutoWakeOllama;
        _filePermCombo.ItemsSource = _permissionModes;
        _commandPermCombo.ItemsSource = _permissionModes;
        _browserPermCombo.ItemsSource = _permissionModes;
        _computerPermCombo.ItemsSource = _permissionModes;
        _filePermCombo.SelectedItem = _preferences.FilePermission;
        _commandPermCombo.SelectedItem = _preferences.CommandPermission;
        _browserPermCombo.SelectedItem = _preferences.BrowserPermission;
        _computerPermCombo.SelectedItem = _preferences.ComputerPermission;
    }

    private async Task LoadModelsAsync()
    {
        try
        {
            _models = await _ollama.GetModelsAsync(CancellationToken.None);
            _modelCombo.ItemsSource = _models;
            _modelCombo.ItemTemplate = new FuncDataTemplate<ModelDescriptor>((m, _) => new TextBlock { Text = m?.Name });
            _selectedModel = _models.FirstOrDefault(m => m.Name == _preferences.DefaultModel) ?? _models.FirstOrDefault();
            _modelCombo.SelectedItem = _selectedModel;
            _effortCombo.ItemsSource = Enum.GetValues<EffortLevel>();
            _effortCombo.SelectedItem = _selectedEffort;
        }
        catch { _statusText.Text = "Could not load models. Is Ollama running?"; }
    }

    private void BuildRecommendedModels()
    {
        _recommendedModelsPanel.Children.Clear();
        foreach (var model in RecommendedModels)
        {
            var selected = model;
            var button = new HavenButton { Content = selected, Margin = new Thickness(0, 0, 8, 8), MinHeight = 42 };
            button.Click += async (_, _) => { _installModelBox.Text = selected; await InstallModelAsync(selected); };
            _recommendedModelsPanel.Children.Add(button);
        }
    }

    private async Task InstallModelAsync(string? requested = null)
    {
        var model = (requested ?? _installModelBox.Text)?.Trim();
        if (string.IsNullOrWhiteSpace(model)) { _statusText.Text = "Enter a model name."; return; }
        _installModelButton.IsEnabled = false;
        _browseModelsButton.IsEnabled = false;
        _installProgress.Value = 0;
        _statusText.Text = $"Installing {model}\u2026";
        try
        {
            if (!await _ollama.IsAvailableAsync(CancellationToken.None)) { _statusText.Text = "Ollama is not available."; return; }
            var progress = new Progress<double>(v => { _installProgress.Value = Math.Clamp(v, 0, 1); _statusText.Text = $"Installing {model}\u2026 {Math.Round(v * 100)}%"; });
            await _ollama.PullModelAsync(model, progress, CancellationToken.None);
            _installProgress.Value = 1;
            _statusText.Text = $"{model} installed.";
            await LoadModelsAsync();
        }
        catch (Exception ex) { _statusText.Text = $"Install failed: {ex.Message}"; }
        finally { _installModelButton.IsEnabled = true; _browseModelsButton.IsEnabled = true; }
    }

    private async Task DeleteSelectedModelAsync()
    {
        if (_selectedModel is null) { _statusText.Text = "Choose a model to delete."; return; }
        try
        {
            await _ollama.DeleteModelAsync(_selectedModel.Name, CancellationToken.None);
            _deleteConfirmBorder.IsVisible = false;
            _statusText.Text = $"{_selectedModel.Name} deleted.";
            await LoadModelsAsync();
        }
        catch (Exception ex) { _statusText.Text = $"Delete failed: {ex.Message}"; }
    }

    private void WireEvents()
    {
        _refreshModelsButton.Click += async (_, _) => await LoadModelsAsync();
        _modelCombo.SelectionChanged += (_, _) => _selectedModel = _modelCombo.SelectedItem as ModelDescriptor;
        _effortCombo.SelectionChanged += (_, _) => { if (_effortCombo.SelectedItem is EffortLevel e) _selectedEffort = e; };
        _installModelButton.Click += async (_, _) => await InstallModelAsync();
        _browseModelsButton.Click += (_, _) => _recommendedModelsBorder.IsVisible = !_recommendedModelsBorder.IsVisible;
        _huggingFaceSearchButton.Click += async (_, _) =>
        {
            var query = Uri.EscapeDataString(_installModelBox.Text?.Trim() ?? string.Empty);
            var uri = new Uri($"https://huggingface.co/models?library=gguf&search={query}");
            var launcher = TopLevel.GetTopLevel(this)?.Launcher;
            if (launcher is null || !await launcher.LaunchUriAsync(uri)) _statusText.Text = "Could not open Hugging Face.";
        };
        _deleteModelButton.Click += (_, _) => { _selectedModel = _modelCombo.SelectedItem as ModelDescriptor; _deleteConfirmBorder.IsVisible = _selectedModel is not null; };
        _modelSearchBox.TextChanged += (_, _) =>
        {
            var q = _modelSearchBox.Text?.Trim() ?? string.Empty;
            _modelCombo.ItemsSource = string.IsNullOrWhiteSpace(q) ? _models : _models.Where(m => m.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || m.Family.Contains(q, StringComparison.OrdinalIgnoreCase)).ToArray();
        };

        _saveDefaultsButton.Click += (_, _) => { _preferences.SetModelDefaults(_selectedModel?.Name, _selectedEffort); _statusText.Text = "Model defaults saved."; };
        _saveAdvancedButton.Click += (_, _) => { _preferences.SetAdvancedModelOptions((double)(_temperatureBox.Value ?? 1.0m), (int)(_contextLimitBox.Value ?? 16384), (int)(_actionLimitBox.Value ?? 50)); _statusText.Text = "Advanced settings saved."; };
        _saveFeaturesButton.Click += (_, _) =>
        {
            _preferences.SetFeatureOptions(_autoSwitchCheck.IsChecked == true, _agenticInChatCheck.IsChecked == true, _preferences.VerticalTabs, _confidenceCheck.IsChecked == true, _autoCompactCheck.IsChecked == true, (int)(_compactPercentBox.Value ?? 80), _adaptiveHelpCheck.IsChecked == true, _browserSideCheck.IsChecked == true, _autoWakeCheck.IsChecked == true);
            _statusText.Text = "Feature settings saved.";
        };
        _savePermissionsButton.Click += (_, _) =>
        {
            _preferences.SetToolPermissions(_filePermCombo.SelectedItem is PermissionMode fp ? fp : PermissionMode.Ask, _commandPermCombo.SelectedItem is PermissionMode cp ? cp : PermissionMode.Ask, _browserPermCombo.SelectedItem is PermissionMode bp ? bp : PermissionMode.Ask, _computerPermCombo.SelectedItem is PermissionMode comp ? comp : PermissionMode.Ask);
            _statusText.Text = "Permissions saved.";
        };
    }

    private static string? FindSectionByKeyword(string query)
    {
        var lower = query.ToLowerInvariant();
        if (lower.Contains("model") || lower.Contains("ollama") || lower.Contains("install") || lower.Contains("resid")) return "models";
        if (lower.Contains("connect") || lower.Contains("api") || lower.Contains("provider")) return "connections";
        if (lower.Contains("theme") || lower.Contains("appearance") || lower.Contains("colour") || lower.Contains("color") || lower.Contains("bright") || lower.Contains("dark")) return "appearance";
        if (lower.Contains("feature") || lower.Contains("toggle") || lower.Contains("auto")) return "features";
        if (lower.Contains("permission") || lower.Contains("tool") || lower.Contains("file") || lower.Contains("command")) return "permissions";
        if (lower.Contains("privacy") || lower.Contains("data")) return "privacy";
        if (lower.Contains("advanced") || lower.Contains("temperature") || lower.Contains("context")) return "advanced";
        if (lower.Contains("about") || lower.Contains("safety")) return "about";
        return null;
    }
}
