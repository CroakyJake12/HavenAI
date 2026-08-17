using Avalonia.Controls;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Services;
using Haven.UI;

namespace Haven.Desktop.Views.Pages.Settings;

/// <summary>
/// Production adapter for the Haven.UI Settings scene. Product state and persistence
/// remain in existing services; Avalonia is only the platform host.
/// </summary>
public sealed partial class SettingsHavenPage : UserControl, IDisposable
{
    private readonly HavenEventBus _bus;
    private readonly UserPreferencesService _preferences;
    private readonly IOllamaClient _ollama;
    private readonly MotionPreferencesService _motionPreferences = MotionPreferencesService.Current;
    private readonly SettingsHavenScene _route;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _installCancellation;
    private IReadOnlyList<ModelDescriptor> _models = [];
    private bool _disposed;

    public SettingsHavenPage(HavenEventBus bus, UserPreferencesService preferences, IOllamaClient ollama)
    {
        _bus = bus;
        _preferences = preferences;
        _ollama = ollama;

        InitializeComponent();
        _route = new SettingsHavenScene();
        Scene.Root = _route.Root;
        _route.LoadPreferences(_preferences, _motionPreferences);
        WireEvents();
        _ = RefreshModelsAsync();
    }

    internal SettingsHavenScene Route => _route;
    internal HavenSceneControl SceneHost => Scene;
    internal HavenElement SceneRoot => _route.Root;

    private void WireEvents()
    {
        _route.RefreshModelsButton.Invoked += async (_, _) => await RefreshModelsAsync();
        _route.SaveModelDefaultsButton.Invoked += (_, _) => SaveModelDefaults();
        _route.AlwaysLoadedToggle.CheckedChanged += (_, _) =>
        {
            _preferences.SetAlwaysLoaded(_route.AlwaysLoadedToggle.IsChecked);
            _route.SetStatus(_route.AlwaysLoadedToggle.IsChecked
                ? "Local models will stay ready after Haven closes."
                : "Local models will unload when Haven closes.");
            _bus.Fire("Settings.Models.ResidencyChanged");
        };

        _route.AppearanceSelect.SelectionChanged += (_, _) => ApplyAppearance();
        _route.ReduceMotionToggle.CheckedChanged += (_, _) =>
        {
            _motionPreferences.SetReduceAnimations(_route.ReduceMotionToggle.IsChecked);
            _route.SetStatus(_route.ReduceMotionToggle.IsChecked ? "Reduced motion is on." : "Reduced motion is off.");
            _bus.Fire("Settings.Appearance.MotionChanged");
        };

        _route.SaveFeaturesButton.Invoked += (_, _) => SaveFeatures();
        _route.SavePermissionsButton.Invoked += (_, _) => SavePermissions();
        _route.SaveAdvancedButton.Invoked += (_, _) => SaveAdvanced();

        _route.InstallModelButton.Invoked += async (_, _) => await InstallModelAsync(_route.InstallModelInput.Text);
        _route.InstallCatalogButton.Invoked += async (_, _) => await InstallModelAsync(_route.CatalogModelSelect.SelectedItem);
        _route.CancelInstallButton.Invoked += (_, _) => _installCancellation?.Cancel();
        _route.ConfirmDeleteButton.Invoked += async (_, _) => await DeleteSelectedModelAsync();
    }

    private async Task RefreshModelsAsync()
    {
        if (_disposed) return;
        try
        {
            _route.SetStatus("Loading installed models…");
            _models = await _ollama.GetModelsAsync(_lifetime.Token);
            _route.SetModels(_models.Select(model => model.Name).ToArray(), _preferences.DefaultModel);
            _route.SetStatus(_models.Count == 0 ? "No local models are installed." : $"{_models.Count} local model{(_models.Count == 1 ? string.Empty : "s")} available.");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _route.SetStatus($"Could not load local models: {ex.Message}");
        }
    }

    private void SaveModelDefaults()
    {
        var model = _route.InstalledModelSelect.SelectedItem;
        var effort = Enum.TryParse<EffortLevel>(_route.EffortSelect.SelectedItem, true, out var parsed)
            ? parsed
            : EffortLevel.Medium;
        _preferences.SetModelDefaults(model, effort);
        _route.SetStatus("Model defaults saved.");
        _bus.Fire("Settings.Models.DefaultsSaved");
    }

    private void ApplyAppearance()
    {
        var appearance = _route.AppearanceSelect.SelectedIndex switch
        {
            0 => HavenUiAppearance.SuperBright,
            1 => HavenUiAppearance.Bright,
            2 => HavenUiAppearance.Dark,
            _ => HavenUiAppearance.SuperDark
        };
        _preferences.ApplyAppearance(appearance);
        _route.SetStatus($"Appearance changed to {_route.AppearanceSelect.SelectedItem}.");
        _bus.Fire("Settings.Appearance.Changed");
    }

    private void SaveFeatures()
    {
        _preferences.SetFeatureOptions(
            _route.AutoSwitchToggle.IsChecked,
            _route.AgenticInChatToggle.IsChecked,
            _preferences.VerticalTabs,
            _route.ConfidenceToggle.IsChecked,
            _route.AutoCompactToggle.IsChecked,
            (int)Math.Round(_route.CompactPercentSlider.Value),
            _route.AdaptiveHelpToggle.IsChecked,
            _route.BrowserSideToggle.IsChecked,
            _route.AutoWakeToggle.IsChecked);
        _route.SetStatus("Chat and app preferences saved.");
        _bus.Fire("Settings.Apps.Saved");
    }

    private void SavePermissions()
    {
        static PermissionMode Parse(string? value) => Enum.TryParse<PermissionMode>(value, true, out var parsed) ? parsed : PermissionMode.Ask;
        _preferences.SetToolPermissions(
            Parse(_route.FilePermissionSelect.SelectedItem),
            Parse(_route.CommandPermissionSelect.SelectedItem),
            Parse(_route.BrowserPermissionSelect.SelectedItem),
            Parse(_route.ComputerPermissionSelect.SelectedItem));
        _route.SetStatus("Permission defaults saved.");
        _bus.Fire("Settings.Permissions.Saved");
    }

    private void SaveAdvanced()
    {
        _preferences.SetAdvancedModelOptions(
            _route.TemperatureSlider.Value,
            (int)Math.Round(_route.ContextLimitSlider.Value),
            (int)Math.Round(_route.ActionLimitSlider.Value));
        _route.SetStatus("Advanced generation limits saved.");
        _bus.Fire("Settings.Advanced.Saved");
    }

    private async Task InstallModelAsync(string? requested)
    {
        if (_disposed || _installCancellation is not null) return;
        var model = requested?.Trim();
        if (string.IsNullOrWhiteSpace(model))
        {
            _route.SetStatus("Enter or choose a local model name first.");
            return;
        }

        _installCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var cancellationToken = _installCancellation.Token;
        _route.SetInstallState(true);
        _route.InstallProgress.Value = 0;
        try
        {
            if (!await _ollama.IsAvailableAsync(cancellationToken))
            {
                _route.SetStatus("Ollama is not available.");
                return;
            }

            _route.SetStatus($"Installing {model}…");
            var progress = new Progress<double>(value =>
            {
                _route.InstallProgress.Value = Math.Clamp(value, 0, 1);
                _route.SetStatus($"Installing {model}… {Math.Round(Math.Clamp(value, 0, 1) * 100)}%");
            });
            await _ollama.PullModelAsync(model, progress, cancellationToken);
            _route.InstallProgress.Value = 1;
            _route.SetStatus($"{model} installed.");
            await RefreshModelsAsync();
            _bus.Fire("Settings.Models.Installed");
        }
        catch (OperationCanceledException)
        {
            if (!_lifetime.IsCancellationRequested) _route.SetStatus("Model installation cancelled.");
        }
        catch (Exception ex)
        {
            _route.SetStatus($"Model installation failed: {ex.Message}");
        }
        finally
        {
            _installCancellation?.Dispose();
            _installCancellation = null;
            if (!_disposed) _route.SetInstallState(false);
        }
    }

    private async Task DeleteSelectedModelAsync()
    {
        if (_disposed) return;
        var model = _route.InstalledModelSelect.SelectedItem;
        if (string.IsNullOrWhiteSpace(model))
        {
            _route.SetDeleteConfirmation(false);
            _route.SetStatus("Choose an installed model first.");
            return;
        }

        _route.SetDeleteBusy(true);
        try
        {
            await _ollama.DeleteModelAsync(model, _lifetime.Token);
            _route.SetDeleteConfirmation(false);
            _route.SetStatus($"{model} removed.");
            await RefreshModelsAsync();
            _bus.Fire("Settings.Models.Deleted");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _route.SetStatus($"Could not remove model: {ex.Message}");
        }
        finally
        {
            if (!_disposed) _route.SetDeleteBusy(false);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _installCancellation?.Cancel();
        _lifetime.Cancel();
        _installCancellation?.Dispose();
        _lifetime.Dispose();
        _route.Dispose();
    }
}
