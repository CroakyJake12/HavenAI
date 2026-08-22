using Avalonia.Controls;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Services;
using Haven.UI;
using Microsoft.Extensions.DependencyInjection;

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
    private readonly IPrivacyPreferenceStore _privacy;
    private readonly MotionPreferencesService _motionPreferences = MotionPreferencesService.Current;
    private readonly SettingsHavenScene _route;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _installCancellation;
    private IReadOnlyList<ModelDescriptor> _models = [];
    private readonly IBackgroundLearningScheduler? _backgroundLearning;
    private readonly IKnowledgeLibrary? _knowledge;
    private readonly IApiBank? _apiBank;
    private readonly IKnowledgeMaintenanceService? _knowledgeMaintenance;
    private IReadOnlyList<KnowledgeRecord> _learnMeRecords = [];
    private IReadOnlyList<ApiBankRecord> _apiRecords = [];
    private IReadOnlyList<BackgroundLearningTask> _learningTasks = [];
    private bool _refreshingLearning;
    private bool _disposed;

    public SettingsHavenPage(
        HavenEventBus bus,
        UserPreferencesService preferences,
        IOllamaClient ollama,
        IPrivacyPreferenceStore privacy,
        IModelProviderRegistry modelProviders,
        IProviderConfigurationStore providerConfigurations,
        IProviderSecretStore providerSecrets)
    {
        _bus = bus;
        _preferences = preferences;
        _ollama = ollama;
        _privacy = privacy;
        _ = modelProviders;
        _ = providerConfigurations;
        _ = providerSecrets;
        _backgroundLearning = App.Services?.GetService<IBackgroundLearningScheduler>();
        _knowledge = App.Services?.GetService<IKnowledgeLibrary>();
        _apiBank = App.Services?.GetService<IApiBank>();
        _knowledgeMaintenance = App.Services?.GetService<IKnowledgeMaintenanceService>();

        InitializeComponent();
        _route = new SettingsHavenScene();
        Scene.Root = _route.Root;
        _route.LoadPreferences(_preferences, _motionPreferences);
        _route.LoadPrivacyPreferences(_privacy.Current);
        WireEvents();
        InitializeConnections();
        _ = RefreshModelsAsync();
        _ = RefreshLearningAsync();
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
        _route.SavePrivacyButton.Invoked += async (_, _) => await SavePrivacyAsync();
        _route.SaveAdvancedButton.Invoked += (_, _) => SaveAdvanced();

        _route.InstallModelButton.Invoked += async (_, _) => await InstallModelAsync(_route.InstallModelInput.Text);
        _route.InstallCatalogButton.Invoked += async (_, _) => await InstallModelAsync(_route.CatalogModelSelect.SelectedItem);
        _route.CancelInstallButton.Invoked += (_, _) => _installCancellation?.Cancel();
        _route.ConfirmDeleteButton.Invoked += async (_, _) => await DeleteSelectedModelAsync();

        _route.BackgroundLearningToggle.CheckedChanged += async (_, _) =>
        {
            if (_refreshingLearning || _backgroundLearning is null) return;
            await RunLearningActionAsync(
                token => _backgroundLearning.SetGlobalEnabledAsync(_route.BackgroundLearningToggle.IsChecked, token),
                _route.BackgroundLearningToggle.IsChecked ? "Background Learning enabled." : "Background Learning disabled.");
        };
        _route.BackgroundModeSelect.SelectionChanged += async (_, _) =>
        {
            if (_refreshingLearning || _backgroundLearning is null || !Enum.TryParse<BackgroundLearningMode>(_route.BackgroundModeSelect.SelectedItem, true, out var mode)) return;
            await RunLearningActionAsync(token => _backgroundLearning.SetModeAsync(mode, token), $"Background Learning mode changed to {mode}.");
        };
        foreach (var (category, toggle) in _route.LearningCategoryToggles)
        {
            toggle.CheckedChanged += async (_, _) =>
            {
                if (_refreshingLearning || _backgroundLearning is null) return;
                await RunLearningActionAsync(token => _backgroundLearning.SetCategoryEnabledAsync(category, toggle.IsChecked, token), $"{category} learning {(toggle.IsChecked ? "enabled" : "disabled")}.");
            };
        }
        _route.LearningRefreshButton.Invoked += async (_, _) => await RefreshLearningAsync();
        _route.LearningCleanupButton.Invoked += async (_, _) => await CleanupLearningAsync();
        _route.LearnMeSelect.SelectionChanged += (_, _) => ShowSelectedLearnMe();
        _route.LearnMeCorrectButton.Invoked += async (_, _) => await CorrectSelectedLearnMeAsync();
        _route.LearnMePinButton.Invoked += async (_, _) => await ToggleSelectedLearnMePinAsync();
        _route.LearnMeRejectButton.Invoked += async (_, _) => await RejectSelectedLearnMeAsync();
        _route.LearnMeForgetButton.Invoked += async (_, _) => await ForgetSelectedLearnMeAsync();
        _route.ApiBankSelect.SelectionChanged += (_, _) => ShowSelectedApi();
        _route.ApiBankPinButton.Invoked += async (_, _) => await ToggleSelectedApiPinAsync();
        _route.ApiBankRemoveButton.Invoked += async (_, _) => await RemoveSelectedApiAsync();
        _route.LearningTaskSelect.SelectionChanged += (_, _) => ShowSelectedTask();
        _route.LearningTaskPauseButton.Invoked += async (_, _) => await ChangeSelectedTaskAsync(BackgroundLearningTaskStatus.Paused);
        _route.LearningTaskResumeButton.Invoked += async (_, _) => await ChangeSelectedTaskAsync(BackgroundLearningTaskStatus.Queued);
        _route.LearningTaskCancelButton.Invoked += async (_, _) => await ChangeSelectedTaskAsync(BackgroundLearningTaskStatus.Cancelled);
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

    private async Task RefreshLearningAsync()
    {
        if (_disposed) return;
        if (_backgroundLearning is null || _knowledge is null || _apiBank is null || _knowledgeMaintenance is null)
        {
            _route.SetLearningFeedback("Background Learning services are unavailable in this build. Existing local data has not been changed.");
            return;
        }

        _refreshingLearning = true;
        try
        {
            _route.SetLearningFeedback("Loading Background Learning state…");
            await _backgroundLearning.InitializeAsync(_lifetime.Token);
            var snapshot = await _backgroundLearning.GetSnapshotAsync(_lifetime.Token);
            _learningTasks = snapshot.Tasks;

            var errors = new List<string>();
            var storage = new KnowledgeStorageSnapshot(0, KnowledgeStorageLimits.BackgroundLearningBytes, 0, 0, 0, KnowledgeStorageLimits.ApiBankBytes, 0, 0);
            try { storage = await _knowledgeMaintenance.GetStorageAsync(_lifetime.Token); }
            catch (Exception ex) { errors.Add($"storage: {ex.Message}"); }

            try { _learnMeRecords = await _knowledge.SearchMetadataAsync(null, KnowledgeCategory.LearnMe, _lifetime.Token); }
            catch (Exception ex) { _learnMeRecords = []; errors.Add($"Learn Me: {ex.Message}"); }

            try { _apiRecords = await _apiBank.SearchAsync(null, _lifetime.Token); }
            catch (Exception ex) { _apiRecords = []; errors.Add($"API Bank: {ex.Message}"); }

            _route.SetLearningSnapshot(snapshot, storage, _learnMeRecords, _apiRecords);
            ShowSelectedLearnMe();
            ShowSelectedApi();
            ShowSelectedTask();
            if (errors.Count > 0)
                _route.SetLearningFeedback($"Background Learning loaded partially — {string.Join(" · ", errors)}");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _route.SetLearningFeedback($"Could not load Background Learning: {ex.Message}");
        }
        finally
        {
            _refreshingLearning = false;
        }
    }

    private async Task RunLearningActionAsync(Func<CancellationToken, Task> action, string success)
    {
        try
        {
            await action(_lifetime.Token);
            await RefreshLearningAsync();
            if (!_disposed) _route.SetLearningFeedback(success);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _route.SetLearningFeedback($"Background Learning change failed: {ex.Message}");
        }
    }

    private async Task CleanupLearningAsync()
    {
        if (_knowledgeMaintenance is null) return;
        try
        {
            _route.SetLearningFeedback("Cleaning up stale, expired and superseded unpinned knowledge…");
            var result = await _knowledgeMaintenance.CleanupAsync(_lifetime.Token);
            await RefreshLearningAsync();
            _route.SetLearningFeedback(result.Summary);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { _route.SetLearningFeedback($"Cleanup failed: {ex.Message}"); }
    }

    private KnowledgeRecord? SelectedLearnMe()
        => _route.LearnMeSelect.SelectedIndex is var index && index >= 0 && index < _learnMeRecords.Count ? _learnMeRecords[index] : null;

    private ApiBankRecord? SelectedApi()
        => _route.ApiBankSelect.SelectedIndex is var index && index >= 0 && index < _apiRecords.Count ? _apiRecords[index] : null;

    private BackgroundLearningTask? SelectedTask()
        => _route.LearningTaskSelect.SelectedIndex is var index && index >= 0 && index < _learningTasks.Count ? _learningTasks[index] : null;

    private void ShowSelectedLearnMe() => _route.ShowLearnMe(SelectedLearnMe());
    private void ShowSelectedApi() => _route.ShowApi(SelectedApi());
    private void ShowSelectedTask() => _route.ShowTask(SelectedTask());

    private async Task CorrectSelectedLearnMeAsync()
    {
        if (_knowledge is null) return;
        var record = SelectedLearnMe();
        var correction = _route.LearnMeCorrectionInput.Text.Trim();
        if (record is null) { _route.SetLearningFeedback("Choose a Learn Me record to correct."); return; }
        if (string.IsNullOrWhiteSpace(correction)) { _route.SetLearningFeedback("Enter the corrected information first."); return; }
        try
        {
            await _knowledge.CorrectAsync(record.Id, correction, "Corrected from Privacy & Memory", _lifetime.Token);
            _route.LearnMeCorrectionInput.Text = string.Empty;
            await RefreshLearningAsync();
            _route.SetLearningFeedback("Correction saved as explicit user-authoritative knowledge; the previous record was superseded.");
        }
        catch (Exception ex) { _route.SetLearningFeedback($"Could not save correction: {ex.Message}"); }
    }

    private async Task ToggleSelectedLearnMePinAsync()
    {
        if (_knowledge is null) return;
        var record = SelectedLearnMe();
        if (record is null) { _route.SetLearningFeedback("Choose a Learn Me record first."); return; }
        await _knowledge.SetPinnedAsync(record.Id, !record.IsPinned, _lifetime.Token);
        await RefreshLearningAsync();
        _route.SetLearningFeedback(record.IsPinned ? "Learn Me record unpinned." : "Learn Me record pinned and protected from cleanup.");
    }

    private async Task RejectSelectedLearnMeAsync()
    {
        if (_knowledge is null) return;
        var record = SelectedLearnMe();
        if (record is null) { _route.SetLearningFeedback("Choose a Learn Me record to reject."); return; }
        await _knowledge.RejectAsync(record.Id, "Rejected from Privacy & Memory", _lifetime.Token);
        await RefreshLearningAsync();
        _route.SetLearningFeedback("Inference rejected and suppressed from immediate re-learning.");
    }

    private async Task ForgetSelectedLearnMeAsync()
    {
        if (_knowledge is null) return;
        var record = SelectedLearnMe();
        if (record is null) { _route.SetLearningFeedback("Choose a Learn Me record to forget."); return; }
        await _knowledge.ForgetAsync(record.Id, _lifetime.Token);
        await RefreshLearningAsync();
        _route.SetLearningFeedback("Learn Me record forgotten.");
    }

    private async Task ToggleSelectedApiPinAsync()
    {
        if (_apiBank is null) return;
        var record = SelectedApi();
        if (record is null) { _route.SetLearningFeedback("Choose an API Bank record first."); return; }
        await _apiBank.SetPinnedAsync(record.Id, !record.IsPinned, _lifetime.Token);
        await RefreshLearningAsync();
        _route.SetLearningFeedback(record.IsPinned ? "API Bank record unpinned." : "API Bank record pinned and protected from cleanup.");
    }

    private async Task RemoveSelectedApiAsync()
    {
        if (_apiBank is null) return;
        var record = SelectedApi();
        if (record is null) { _route.SetLearningFeedback("Choose an API Bank record to remove."); return; }
        await _apiBank.RemoveAsync(record.Id, _lifetime.Token);
        await RefreshLearningAsync();
        _route.SetLearningFeedback("API Bank record removed.");
    }

    private async Task ChangeSelectedTaskAsync(BackgroundLearningTaskStatus requestedStatus)
    {
        if (_backgroundLearning is null) return;
        var task = SelectedTask();
        if (task is null) { _route.SetLearningFeedback("Choose a background-learning task first."); return; }
        var changed = requestedStatus switch
        {
            BackgroundLearningTaskStatus.Paused => await _backgroundLearning.PauseAsync(task.Id, _lifetime.Token),
            BackgroundLearningTaskStatus.Queued => await _backgroundLearning.ResumeAsync(task.Id, _lifetime.Token),
            BackgroundLearningTaskStatus.Cancelled => await _backgroundLearning.CancelAsync(task.Id, _lifetime.Token),
            _ => false
        };
        await RefreshLearningAsync();
        _route.SetLearningFeedback(changed ? $"Task state changed to {requestedStatus}." : "That task cannot make the requested state transition.");
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

    private async Task SavePrivacyAsync()
    {
        if (_disposed) return;
        try
        {
            var updated = _privacy.Current with
            {
                LocalOnlyMode = _route.LocalOnlyToggle.IsChecked,
                BackgroundLearningEnabled = _route.BackgroundLearningToggle.IsChecked,
                ModelImprovementSharingEnabled = _route.ModelImprovementSharingToggle.IsChecked
            };
            await _privacy.UpdateAsync(updated, _lifetime.Token);
            _route.LoadPrivacyPreferences(_privacy.Current);
            _route.SetStatus("Privacy choices saved locally.");
            _bus.Fire("Settings.Privacy.Saved");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _route.SetStatus($"Could not save privacy choices: {ex.Message}");
        }
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
