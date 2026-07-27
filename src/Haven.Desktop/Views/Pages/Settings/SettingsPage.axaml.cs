using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Events;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Settings;

/// <summary>
/// Settings page. Manages models, themes, features, and permissions.
/// </summary>
public sealed partial class SettingsPage : UserControl
{
    private readonly HavenEventBus _bus;
    private readonly UserPreferencesService _preferences;
    private readonly IOllamaClient _ollama;

    private IReadOnlyList<ModelDescriptor> _models = [];
    private ModelDescriptor? _selectedModel;
    private EffortLevel _selectedEffort = EffortLevel.Medium;
    private PermissionMode[] _permissionModes = Enum.GetValues<PermissionMode>();

    public SettingsPage(HavenEventBus bus, UserPreferencesService preferences, IOllamaClient ollama)
    {
        _bus = bus;
        _preferences = preferences;
        _ollama = ollama;

        InitializeComponent();
        LoadPreferences();
        WireEvents();
        _ = LoadModelsAsync();
    }

    private void LoadPreferences()
    {
        var gen = _preferences.GenerationOptions;
        TemperatureBox.Value = (decimal)gen.Temperature;
        ContextLimitBox.Value = gen.ContextLimit;
        ActionLimitBox.Value = gen.ActionLimit;
        AutoSwitchCheck.IsChecked = _preferences.AutoSwitchCompatibleModels;
        AgenticInChatCheck.IsChecked = _preferences.ShowAgenticInChat;
        ConfidenceCheck.IsChecked = _preferences.ConfidenceMeter;
        AutoCompactCheck.IsChecked = _preferences.AutoCompactContext;
        CompactPercentBox.Value = _preferences.CompactAtPercent;
        AdaptiveHelpCheck.IsChecked = _preferences.AdaptiveHelp;
        BrowserSideCheck.IsChecked = _preferences.BrowserSideAssistant;
        AutoWakeCheck.IsChecked = _preferences.AutoWakeOllama;

        FilePermCombo.ItemsSource = _permissionModes;
        CommandPermCombo.ItemsSource = _permissionModes;
        BrowserPermCombo.ItemsSource = _permissionModes;
        ComputerPermCombo.ItemsSource = _permissionModes;
        FilePermCombo.SelectedItem = _preferences.FilePermission;
        CommandPermCombo.SelectedItem = _preferences.CommandPermission;
        BrowserPermCombo.SelectedItem = _preferences.BrowserPermission;
        ComputerPermCombo.SelectedItem = _preferences.ComputerPermission;
    }

    private async Task LoadModelsAsync()
    {
        try
        {
            _models = await _ollama.GetModelsAsync(CancellationToken.None);
            ModelCombo.ItemsSource = _models;
            ModelCombo.ItemTemplate = new FuncDataTemplate<ModelDescriptor>((m, _) => new TextBlock { Text = m?.Name });
            _selectedModel = _models.FirstOrDefault(m => m.Name == _preferences.DefaultModel) ?? _models.FirstOrDefault();
            ModelCombo.SelectedItem = _selectedModel;

            EffortCombo.ItemsSource = Enum.GetValues<EffortLevel>();
            EffortCombo.SelectedItem = _selectedEffort;
        }
        catch { StatusText.Text = "Could not load models. Is Ollama running?"; }
    }

    private void WireEvents()
    {
        _bus.RegisterElement("Settings.Actions.RefreshModels", RefreshModelsButton);
        _bus.WirePointerEvents("Settings.Actions.RefreshModels", RefreshModelsButton);
        RefreshModelsButton.Click += async (_, _) =>
        {
            _bus.Fire("Settings.Actions.RefreshModels");
            await LoadModelsAsync();
        };

        _bus.RegisterElement("Settings.Actions.SaveDefaults", SaveDefaultsButton);
        _bus.WirePointerEvents("Settings.Actions.SaveDefaults", SaveDefaultsButton);
        SaveDefaultsButton.Click += (_, _) =>
        {
            _bus.Fire("Settings.Actions.SaveDefaults");
            _preferences.SetModelDefaults(_selectedModel?.Name, _selectedEffort);
            StatusText.Text = "Model defaults saved.";
        };

        _bus.RegisterElement("Settings.Actions.SaveAdvanced", SaveAdvancedButton);
        _bus.WirePointerEvents("Settings.Actions.SaveAdvanced", SaveAdvancedButton);
        SaveAdvancedButton.Click += (_, _) =>
        {
            _bus.Fire("Settings.Actions.SaveAdvanced");
            _preferences.SetAdvancedModelOptions(
                (double)(TemperatureBox.Value ?? 1.0m),
                (int)(ContextLimitBox.Value ?? 16384),
                (int)(ActionLimitBox.Value ?? 50));
            StatusText.Text = "Advanced settings saved.";
        };

        _bus.RegisterElement("Settings.Actions.SaveFeatures", SaveFeaturesButton);
        _bus.WirePointerEvents("Settings.Actions.SaveFeatures", SaveFeaturesButton);
        SaveFeaturesButton.Click += (_, _) =>
        {
            _bus.Fire("Settings.Actions.SaveFeatures");
            _preferences.SetFeatureOptions(
                AutoSwitchCheck.IsChecked == true,
                AgenticInChatCheck.IsChecked == true,
                _preferences.VerticalTabs,
                ConfidenceCheck.IsChecked == true,
                AutoCompactCheck.IsChecked == true,
                (int)(CompactPercentBox.Value ?? 80),
                AdaptiveHelpCheck.IsChecked == true,
                BrowserSideCheck.IsChecked == true,
                AutoWakeCheck.IsChecked == true);
            StatusText.Text = "Feature settings saved.";
        };

        _bus.RegisterElement("Settings.Actions.SavePermissions", SavePermissionsButton);
        _bus.WirePointerEvents("Settings.Actions.SavePermissions", SavePermissionsButton);
        SavePermissionsButton.Click += (_, _) =>
        {
            _bus.Fire("Settings.Actions.SavePermissions");
            _preferences.SetToolPermissions(
                FilePermCombo.SelectedItem is PermissionMode fp ? fp : PermissionMode.Ask,
                CommandPermCombo.SelectedItem is PermissionMode cp ? cp : PermissionMode.Ask,
                BrowserPermCombo.SelectedItem is PermissionMode bp ? bp : PermissionMode.Ask,
                ComputerPermCombo.SelectedItem is PermissionMode comp ? comp : PermissionMode.Ask);
            StatusText.Text = "Permissions saved.";
        };
    }
}
