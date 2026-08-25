using Avalonia.Controls;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.HavenUI.Tokens;
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
    private readonly IModeRegistry? _modeRegistry;
    private readonly MotionPreferencesService _motionPreferences = MotionPreferencesService.Current;
    private readonly SettingsHavenScene _route;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _installCancellation;
    private IReadOnlyList<ModelDescriptor> _models = [];
    private readonly IBackgroundLearningScheduler? _backgroundLearning;
    private readonly IKnowledgeLibrary? _knowledge;
    private readonly IApiBank? _apiBank;
    private readonly IKnowledgeMaintenanceService? _knowledgeMaintenance;
    private readonly ExtensionManager? _extensionManager;
    private IReadOnlyList<KnowledgeRecord> _learnMeRecords = [];
    private IReadOnlyList<ApiBankRecord> _apiRecords = [];
    private IReadOnlyList<BackgroundLearningTask> _learningTasks = [];
    private IReadOnlyList<ModeDefinition> _defaultTabModes = [];
    private bool _refreshingDefaultTabs;
    private bool _refreshingLearning;
    private IReadOnlyList<ExtensionSource> _extensionSources = [];
    private IReadOnlyList<DiscoveredExtensionPackage> _availableExtensions = [];
    private IReadOnlyList<InstalledExtensionPackage> _installedExtensions = [];
    private string? _permissionReviewPackageId;
    private Guid? _uninstallConfirmationId;
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
        _modeRegistry = App.Services?.GetService<IModeRegistry>();
        _ = modelProviders;
        _ = providerConfigurations;
        _ = providerSecrets;
        _backgroundLearning = App.Services?.GetService<IBackgroundLearningScheduler>();
        _knowledge = App.Services?.GetService<IKnowledgeLibrary>();
        _apiBank = App.Services?.GetService<IApiBank>();
        _knowledgeMaintenance = App.Services?.GetService<IKnowledgeMaintenanceService>();
        _extensionManager = App.Services?.GetService<ExtensionManager>();

        InitializeComponent();
        _route = new SettingsHavenScene();
        Scene.Root = _route.Root;
        LoadFonts();
        _route.LoadPreferences(_preferences, _motionPreferences);
        _route.LoadPrivacyPreferences(_privacy.Current);
        WireEvents();
        InitializeConnections();
        InitializeGovernance();
        _ = RefreshModelsAsync();
        _ = RefreshDefaultTabsAsync();
        _ = RefreshExtensionsAsync();
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
        _route.DefaultTabSelect.SelectionChanged += (_, _) => SaveDefaultTab();
        _route.ThemeSelect.SelectionChanged += (_, _) => ApplyThemeChoice();
        _route.AccentOverrideToggle.CheckedChanged += (_, _) => ApplyAccentOverrideToggle();
        foreach (var swatch in _route.AccentSwatchButtons)
        {
            var captured = swatch;
            captured.Invoked += (_, _) => ApplyAccentSwatch(captured);
        }        _route.ReduceMotionToggle.CheckedChanged += (_, _) =>
        {
            _motionPreferences.SetReduceAnimations(_route.ReduceMotionToggle.IsChecked);
            _route.SetStatus(_route.ReduceMotionToggle.IsChecked ? "Reduced motion is on." : "Reduced motion is off.");
            _bus.Fire("Settings.Appearance.MotionChanged");
        };
        _route.UserAvatarToggle.CheckedChanged += (_, _) => ApplyAvatarToggle(HavenAvatarKind.User);
        _route.UserAvatarChooseButton.Invoked += async (_, _) => await ChooseAvatarAsync(HavenAvatarKind.User);
        _route.UserAvatarRemoveButton.Invoked += (_, _) => RemoveAvatar(HavenAvatarKind.User);
        _route.HavenAvatarToggle.CheckedChanged += (_, _) => ApplyAvatarToggle(HavenAvatarKind.Haven);
        _route.HavenAvatarChooseButton.Invoked += async (_, _) => await ChooseAvatarAsync(HavenAvatarKind.Haven);
        _route.HavenAvatarRemoveButton.Invoked += (_, _) => RemoveAvatar(HavenAvatarKind.Haven);

        _route.SaveFeaturesButton.Invoked += (_, _) => SaveFeatures();
        _route.SavePermissionsButton.Invoked += (_, _) => SavePermissions();
        _route.SavePrivacyButton.Invoked += async (_, _) => await SavePrivacyAsync();
        _route.SaveAdvancedButton.Invoked += (_, _) => SaveAdvanced();

        _route.ExtensionAddSourceButton.Invoked += async (_, _) => await AddExtensionSourceAsync();
        _route.ExtensionRefreshButton.Invoked += async (_, _) => await RefreshSelectedExtensionSourceAsync();
        _route.ExtensionRemoveSourceButton.Invoked += async (_, _) => await RemoveSelectedExtensionSourceAsync();
        _route.AvailableExtensionSelect.SelectionChanged += (_, _) => ShowSelectedAvailableExtension();
        _route.InstalledExtensionSelect.SelectionChanged += (_, _) => ShowSelectedInstalledExtension();
        _route.ExtensionInstallButton.Invoked += async (_, _) => await InstallSelectedExtensionAsync();
        _route.ExtensionToggleButton.Invoked += async (_, _) => await ToggleSelectedExtensionAsync();
        _route.ExtensionUninstallButton.Invoked += async (_, _) => await UninstallSelectedExtensionAsync();

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
        _route.ApplyAccentSwatchColours(appearance);
        _route.SetStatus($"Appearance changed to {_route.AppearanceSelect.SelectedItem}.");
        _bus.Fire("Settings.Appearance.Changed");
    }

    private void ApplyThemeChoice()
    {
        var name = _route.ThemeSelect.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(name)) return;
        _preferences.ApplyThemeChoice(name);
        SyncAccentSelectionLabel();
        _route.SetStatus($"Theme changed to {name}.");
        _bus.Fire("Settings.Personalisation.ThemeChanged");
    }

    private void ApplyAccentOverrideToggle()
    {
        _preferences.ApplyAccentOverride(_route.AccentOverrideToggle.IsChecked, _preferences.AccentColourSelection);
        SyncAccentSelectionLabel();
        _route.SetStatus(_route.AccentOverrideToggle.IsChecked
            ? "Accent override is on."
            : "Accent override is off; apps use their own surface accents.");
        _bus.Fire("Settings.Personalisation.AccentChanged");
    }

    private void ApplyAccentSwatch(Haven.UI.Components.Button swatch)
    {
        var index = -1;
        for (var position = 0; position < _route.AccentSwatchButtons.Count; position++)
            if (ReferenceEquals(_route.AccentSwatchButtons[position], swatch)) { index = position; break; }
        if (index < 0 || index >= AccentColourCatalog.Colours.Count) return;
        var name = AccentColourCatalog.Name(AccentColourCatalog.Colours[index]);
        _preferences.ApplyAccentOverride(true, name);
        for (var position = 0; position < _route.AccentSwatchButtons.Count; position++)
            _route.AccentSwatchButtons[position].Content = position == index ? "✓" : string.Empty;
        SyncAccentSelectionLabel();
        _route.SetStatus($"Accent colour changed to {name}.");
        _bus.Fire("Settings.Personalisation.AccentChanged");
    }

    private void ApplyFontChoice()
    {
        var family = _route.FontSelect.SelectedItem as string;
        if (_refreshingFonts) return;
        _preferences.SetFontPreference(string.IsNullOrWhiteSpace(family) ? null : family);
        _route.SetStatus($"Font changed to {family ?? HavenUiFont.DefaultFamily}.");
        _bus.Fire("Settings.Personalisation.FontChanged");
    }

    private void SyncAccentSelectionLabel() =>
        _route.AccentSelectionText.Content = _preferences.OverrideAccentColour && _preferences.AccentColourSelection is { } accentName
            ? $"Accent: {accentName}"
            : "Accent: surface colours";

    private void ApplyAvatarToggle(HavenAvatarKind kind)
    {
        var enabled = kind == HavenAvatarKind.User
            ? _route.UserAvatarToggle.IsChecked
            : _route.HavenAvatarToggle.IsChecked;
        if (kind == HavenAvatarKind.User) _preferences.SetUserAvatarEnabled(enabled);
        else _preferences.SetHavenAvatarEnabled(enabled);
        var label = kind == HavenAvatarKind.User ? "User" : "Haven";
        if (enabled && AvatarStore.Current?.Has(kind) != true)
        {
            _route.SetStatus($"Select an image first to use the {label} profile picture.");
        }
        else
        {
            _route.SetStatus($"{label} profile picture {(enabled ? "shown" : "hidden")}.");
            _bus.Fire("Settings.Personalisation.AvatarChanged");
        }
    }

    private async Task ChooseAvatarAsync(HavenAvatarKind kind)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
        {
            _route.SetStatus("Image selection is unavailable in this host.");
            return;
        }
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = kind == HavenAvatarKind.User ? "Choose your profile picture" : "Choose the Haven profile picture",
            FileTypeFilter =
            [
                new("Images")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif"]
                }
            ]
        });
        var file = files.FirstOrDefault();
        if (file is null) return;
        var path = file.Path.LocalPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            _route.SetStatus("That image could not be opened from its location.");
            return;
        }

        try
        {
            AvatarStore.Current?.SetFromFile(kind, path);
        }
        catch (Exception exception) when (exception is ArgumentException or FileNotFoundException or IOException)
        {
            _route.SetStatus($"Couldn't use that image: {exception.Message}");
            return;
        }

        if (kind == HavenAvatarKind.User) _preferences.SetUserAvatarEnabled(true);
        else _preferences.SetHavenAvatarEnabled(true);
        SyncAvatarControls();
        _route.SetStatus($"{(kind == HavenAvatarKind.User ? "Your" : "The Haven")} profile picture was updated and is now shown.");
        _bus.Fire("Settings.Personalisation.AvatarChanged");
    }

    private void RemoveAvatar(HavenAvatarKind kind)
    {
        var removed = AvatarStore.Current?.Remove(kind) == true;
        if (kind == HavenAvatarKind.User) _preferences.SetUserAvatarEnabled(false);
        else _preferences.SetHavenAvatarEnabled(false);
        SyncAvatarControls();
        _route.SetStatus(removed
            ? $"{(kind == HavenAvatarKind.User ? "Your" : "The Haven")} profile picture was removed."
            : "No profile picture was stored.");
        _bus.Fire("Settings.Personalisation.AvatarChanged");
    }

    private void SyncAvatarControls()
    {
        _route.UserAvatarToggle.IsChecked = _preferences.UserAvatarEnabled;
        _route.HavenAvatarToggle.IsChecked = _preferences.HavenAvatarEnabled;
        _route.UserAvatarRemoveButton.SetValue(HavenProperties.Enabled, AvatarStore.Current?.Has(HavenAvatarKind.User) == true);
        _route.HavenAvatarRemoveButton.SetValue(HavenProperties.Enabled, AvatarStore.Current?.Has(HavenAvatarKind.Haven) == true);
    }

    private bool _refreshingFonts;

    private void LoadFonts()
    {
        _refreshingFonts = true;
        try
        {
            var families = new List<string> { HavenUiFont.DefaultFamily };
            try
            {
                families.AddRange(Avalonia.Media.FontManager.Current.SystemFonts
                    .Select(font => font.Name)
                    .Where(name => !name.Equals(HavenUiFont.DefaultFamily, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                    .Take(80));
            }
            catch (InvalidOperationException)
            {
                // Font enumeration unavailable; Montserrat remains the safe default.
            }
            _route.FontSelect.Items = families;
            var selected = _preferences.FontPreference;
            var index = selected is null ? 0 : families.FindIndex(name => name.Equals(selected, StringComparison.OrdinalIgnoreCase));
            _route.FontSelect.SelectedIndex = index;
        }
        finally
        {
            _refreshingFonts = false;
        }
    }

    private async Task RefreshDefaultTabsAsync()
    {
        if (_modeRegistry is null)
        {
            _route.SetDefaultTabModes([], -1);
            return;
        }
        try
        {
            _refreshingDefaultTabs = true;
            _defaultTabModes = (await _modeRegistry.GetModesAsync(_lifetime.Token))
                .Where(mode => mode.IsEnabled && mode.InstallState == ModeInstallState.Installed)
                .OrderBy(mode => mode.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            var selected = _defaultTabModes.ToList().FindIndex(mode =>
                mode.Key.Equals(_preferences.DefaultTabAppKey, StringComparison.OrdinalIgnoreCase));
            _route.SetDefaultTabModes(_defaultTabModes.Select(mode => mode.Name).ToArray(), selected);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            _refreshingDefaultTabs = false;
        }
    }

    private void SaveDefaultTab()
    {
        if (_refreshingDefaultTabs) return;
        var index = _route.DefaultTabSelect.SelectedIndex;
        if (index < 0 || index >= _defaultTabModes.Count) return;
        var mode = _defaultTabModes[index];
        _preferences.SetDefaultTabAppKey(mode.Key);
        _route.SetStatus($"New tabs will open {mode.Name}. If it becomes unavailable, Haven will fall back safely.");
        _bus.Fire("Settings.Personalisation.DefaultTabChanged");
    }

    private async Task RefreshExtensionsAsync()
    {
        if (_extensionManager is null)
        {
            _route.ExtensionStatusText.Content = "Plugin and Skill services are unavailable in this build.";
            return;
        }
        try
        {
            _extensionSources = await _extensionManager.GetSourcesAsync(_lifetime.Token);
            _installedExtensions = await _extensionManager.GetInstalledAsync(_lifetime.Token);
            _route.SetExtensionSources(_extensionSources.Select(source => $"{source.DisplayName} · {source.UpdateMode}").ToArray(), _route.ExtensionSourceSelect.SelectedIndex);
            _route.SetInstalledExtensions(_installedExtensions.Select(package => $"{PackageTypeLabel(package.Manifest.PackageType)} · {package.Manifest.DisplayName} · {package.Manifest.Version}").ToArray(), _route.InstalledExtensionSelect.SelectedIndex);
            ShowSelectedInstalledExtension();
            _route.ExtensionStatusText.Content = $"{_extensionSources.Count} source(s), {_installedExtensions.Count} installed package(s).";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { _route.ExtensionStatusText.Content = "Could not load Plugins & Skills: " + ex.Message; }
    }

    private async Task AddExtensionSourceAsync()
    {
        if (_extensionManager is null) return;
        var uri = _route.ExtensionSourceUriInput.Text.Trim();
        var name = _route.ExtensionSourceNameInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(uri) || string.IsNullOrWhiteSpace(name))
        {
            _route.ExtensionStatusText.Content = "A source name and GitHub repository URL are required.";
            return;
        }
        try
        {
            var isPrivate = _route.ExtensionPrivateToggle.IsChecked;
            var account = _route.ExtensionConnectedAccountInput.Text.Trim();
            var mode = Enum.TryParse<ExtensionUpdateMode>(_route.ExtensionUpdateModeSelect.SelectedItem, true, out var selected) ? selected : ExtensionUpdateMode.Notify;
            await _extensionManager.AddSourceAsync(new ExtensionSource(Guid.NewGuid(), ExtensionSourceType.GitHubRepository, name, uri, null,
                isPrivate, string.IsNullOrWhiteSpace(account) ? null : account, mode, true, null, null), _lifetime.Token);
            _route.ExtensionSourceNameInput.Text = string.Empty;
            _route.ExtensionSourceUriInput.Text = string.Empty;
            _route.ExtensionConnectedAccountInput.Text = string.Empty;
            await RefreshExtensionsAsync();
            _route.ExtensionStatusText.Content = "GitHub repository source added. Refresh it to discover packages.";
        }
        catch (Exception ex) { _route.ExtensionStatusText.Content = "Source was not added: " + ex.Message; }
    }

    private async Task RefreshSelectedExtensionSourceAsync()
    {
        if (_extensionManager is null || Selected(_extensionSources, _route.ExtensionSourceSelect.SelectedIndex) is not { } source) return;
        try
        {
            _route.ExtensionStatusText.Content = "Refreshing repository manifest…";
            _availableExtensions = await _extensionManager.RefreshAsync(source.Id, _lifetime.Token);
            _permissionReviewPackageId = null;
            _route.ExtensionInstallButton.Content = "Review permissions";
            _route.SetAvailableExtensions(_availableExtensions.Select(package => $"{PackageTypeLabel(package.Manifest.PackageType)} · {package.Manifest.DisplayName} · {package.Manifest.Version} · {package.State}").ToArray(), 0);
            ShowSelectedAvailableExtension();
            await RefreshExtensionsAsync();
            _route.ExtensionStatusText.Content = $"Discovered {_availableExtensions.Count} validated package(s) from {source.DisplayName}.";
        }
        catch (Exception ex) { _route.ExtensionStatusText.Content = "Repository refresh failed: " + ex.Message; }
    }

    private async Task RemoveSelectedExtensionSourceAsync()
    {
        if (_extensionManager is null || Selected(_extensionSources, _route.ExtensionSourceSelect.SelectedIndex) is not { } source) return;
        await _extensionManager.RemoveSourceAsync(source.Id, _lifetime.Token);
        _availableExtensions = _availableExtensions.Where(package => package.SourceId != source.Id).ToArray();
        _route.SetAvailableExtensions(_availableExtensions.Select(package => package.Manifest.DisplayName).ToArray(), 0);
        await RefreshExtensionsAsync();
    }

    private void ShowSelectedAvailableExtension()
    {
        var package = Selected(_availableExtensions, _route.AvailableExtensionSelect.SelectedIndex);
        if (package is null)
        {
            _route.AvailableExtensionDetails.Content = "Refresh a source to discover validated packages.";
            return;
        }
        var manifest = package.Manifest;
        _route.AvailableExtensionDetails.Content = $"{PackageTypeLabel(manifest.PackageType)} · {manifest.DisplayName} {manifest.Version}\n{manifest.Description}\nAuthor: {manifest.Author} · Publisher: {manifest.Publisher}\nHaven compatibility: {manifest.HavenVersionRange}\nSource package: {manifest.PackagePath}\nRequested permissions: {PermissionLabel(manifest.RequestedPermissions)}\nCapabilities: {manifest.Capabilities.Count} · Skills: {manifest.Skills.Count}\nState: {package.State}";
        if (_permissionReviewPackageId != manifest.PackageId) _route.ExtensionInstallButton.Content = "Review permissions";
    }

    private async Task InstallSelectedExtensionAsync()
    {
        if (_extensionManager is null || Selected(_availableExtensions, _route.AvailableExtensionSelect.SelectedIndex) is not { } package) return;
        if (_permissionReviewPackageId != package.Manifest.PackageId)
        {
            _permissionReviewPackageId = package.Manifest.PackageId;
            _route.AvailableExtensionDetails.Content += $"\n\nPermission review\nSource: {Selected(_extensionSources, _route.ExtensionSourceSelect.SelectedIndex)?.RepositoryUri ?? "repository"}\nThis executable package requests: {PermissionLabel(package.Manifest.RequestedPermissions)}. GitHub-hosted code is not automatically trusted. Select the button again to grant exactly these permissions and install.";
            _route.ExtensionInstallButton.Content = "Grant listed permissions & install";
            return;
        }
        try
        {
            _route.ExtensionStatusText.Content = "Installing package atomically…";
            await _extensionManager.InstallAsync(package, package.Manifest.RequestedPermissions, _lifetime.Token);
            _permissionReviewPackageId = null;
            _route.ExtensionInstallButton.Content = "Review permissions";
            await RefreshExtensionsAsync();
            _route.ExtensionStatusText.Content = $"{package.Manifest.DisplayName} installed and registered.";
        }
        catch (Exception ex) { _route.ExtensionStatusText.Content = "Installation failed: " + ex.Message; }
    }

    private void ShowSelectedInstalledExtension()
    {
        var package = Selected(_installedExtensions, _route.InstalledExtensionSelect.SelectedIndex);
        if (package is null)
        {
            _route.InstalledExtensionDetails.Content = "No installed packages.";
            return;
        }
        _route.InstalledExtensionDetails.Content = $"{PackageTypeLabel(package.Manifest.PackageType)} · {package.Manifest.DisplayName} {package.Manifest.Version}\n{package.Manifest.Description}\nPublisher: {package.Manifest.Publisher}\nGranted permissions: {PermissionLabel(package.GrantedPermissions)}\nEnabled: {package.IsEnabled} · State: {package.State}\nSource ID: {package.SourceId}\nLocal modifications: {package.HasLocalModifications}";
        _route.ExtensionToggleButton.Content = package.IsEnabled ? "Disable" : "Enable";
        if (_uninstallConfirmationId != package.Id) _route.ExtensionUninstallButton.Content = "Uninstall";
    }

    private async Task ToggleSelectedExtensionAsync()
    {
        if (_extensionManager is null || Selected(_installedExtensions, _route.InstalledExtensionSelect.SelectedIndex) is not { } package) return;
        await _extensionManager.SetEnabledAsync(package.Id, !package.IsEnabled, _lifetime.Token);
        await RefreshExtensionsAsync();
    }

    private async Task UninstallSelectedExtensionAsync()
    {
        if (_extensionManager is null || Selected(_installedExtensions, _route.InstalledExtensionSelect.SelectedIndex) is not { } package) return;
        if (_uninstallConfirmationId != package.Id)
        {
            _uninstallConfirmationId = package.Id;
            _route.ExtensionUninstallButton.Content = "Confirm uninstall";
            _route.ExtensionStatusText.Content = $"Confirm uninstalling {package.Manifest.DisplayName}. Installed package files will be removed; repository sources remain.";
            return;
        }
        await _extensionManager.UninstallAsync(package.Id, _lifetime.Token);
        _uninstallConfirmationId = null;
        await RefreshExtensionsAsync();
    }

    private static T? Selected<T>(IReadOnlyList<T> values, int index) where T : class => index >= 0 && index < values.Count ? values[index] : null;
    private static string PackageTypeLabel(ExtensionPackageType type) => type switch { ExtensionPackageType.PluginAndSkills => "Plugin + Skills", _ => type.ToString() };
    private static string PermissionLabel(ExtensionPermission permissions) => permissions == ExtensionPermission.None ? "None" : string.Join(", ", Enum.GetValues<ExtensionPermission>().Where(value => value != ExtensionPermission.None && permissions.HasFlag(value)));

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
