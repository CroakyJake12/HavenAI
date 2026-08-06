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
    private static readonly string[] RecommendedModels =
    [
        "qwen3:4b",
        "gemma3:4b",
        "llama3.2:3b",
        "deepseek-r1:1.5b"
    ];

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
        BuildRecommendedModels();
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

    private void BuildRecommendedModels()
    {
        RecommendedModelsPanel.Children.Clear();
        foreach (var model in RecommendedModels)
        {
            var selected = model;
            var button = new Button
            {
                Content = selected,
                Margin = new Avalonia.Thickness(0, 0, 8, 8),
                MinHeight = 42
            };
            button.Click += async (_, _) =>
            {
                InstallModelBox.Text = selected;
                await InstallModelAsync(selected);
            };
            RecommendedModelsPanel.Children.Add(button);
        }
    }

    private async Task InstallModelAsync(string? requested = null)
    {
        var model = (requested ?? InstallModelBox.Text)?.Trim();
        if (string.IsNullOrWhiteSpace(model))
        {
            StatusText.Text = "Enter a model name or choose a recommended model.";
            return;
        }

        InstallModelButton.IsEnabled = false;
        BrowseModelsButton.IsEnabled = false;
        InstallProgress.Value = 0;
        StatusText.Text = $"Installing {model}…";
        try
        {
            if (!await _ollama.IsAvailableAsync(CancellationToken.None))
            {
                StatusText.Text = "The local model service is not available. Start or connect Ollama, or use the Android GGUF importer.";
                return;
            }

            var progress = new Progress<double>(value =>
            {
                InstallProgress.Value = Math.Clamp(value, 0d, 1d);
                StatusText.Text = $"Installing {model}… {Math.Round(InstallProgress.Value * 100)}%";
            });
            await _ollama.PullModelAsync(model, progress, CancellationToken.None);
            InstallProgress.Value = 1;
            StatusText.Text = $"{model} installed.";
            await LoadModelsAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Install failed: {ex.Message}";
        }
        finally
        {
            InstallModelButton.IsEnabled = true;
            BrowseModelsButton.IsEnabled = true;
        }
    }

    private async Task DeleteSelectedModelAsync()
    {
        if (_selectedModel is null)
        {
            StatusText.Text = "Choose a model to delete.";
            return;
        }

        try
        {
            await _ollama.DeleteModelAsync(_selectedModel.Name, CancellationToken.None);
            DeleteConfirmBorder.IsVisible = false;
            StatusText.Text = $"{_selectedModel.Name} deleted.";
            await LoadModelsAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Delete failed: {ex.Message}";
        }
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

        ModelCombo.SelectionChanged += (_, _) =>
            _selectedModel = ModelCombo.SelectedItem as ModelDescriptor;
        EffortCombo.SelectionChanged += (_, _) =>
        {
            if (EffortCombo.SelectedItem is EffortLevel effort)
                _selectedEffort = effort;
        };
        InstallModelButton.Click += async (_, _) => await InstallModelAsync();
        BrowseModelsButton.Click += (_, _) =>
            RecommendedModelsBorder.IsVisible = !RecommendedModelsBorder.IsVisible;
        HuggingFaceSearchButton.Click += async (_, _) =>
        {
            var query = Uri.EscapeDataString(InstallModelBox.Text?.Trim() ?? string.Empty);
            var uri = new Uri($"https://huggingface.co/models?library=gguf&search={query}");
            var launcher = TopLevel.GetTopLevel(this)?.Launcher;
            if (launcher is null || !await launcher.LaunchUriAsync(uri))
                StatusText.Text = "Could not open the public Hugging Face catalogue.";
        };
        DeleteModelButton.Click += (_, _) =>
        {
            _selectedModel = ModelCombo.SelectedItem as ModelDescriptor;
            DeleteConfirmBorder.IsVisible = _selectedModel is not null;
            if (_selectedModel is null)
                StatusText.Text = "Choose a model to delete.";
        };
        CancelDeleteButton.Click += (_, _) => DeleteConfirmBorder.IsVisible = false;
        ConfirmDeleteButton.Click += async (_, _) => await DeleteSelectedModelAsync();
        ModelSearchBox.TextChanged += (_, _) =>
        {
            var query = ModelSearchBox.Text?.Trim() ?? string.Empty;
            ModelCombo.ItemsSource = string.IsNullOrWhiteSpace(query)
                ? _models
                : _models.Where(model =>
                    model.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || model.Family.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
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
