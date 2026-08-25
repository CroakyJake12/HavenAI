/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Views/Pages/Settings/SettingsHavenPage.Updates.cs, a code-behind partial of the production Settings page.
 * What: This file owns InitializeUpdates: resolves IUpdateService and DirectUpdateOptions, wires the Updates section controls, loads preferences, renders status reports and runs manual checks.
 * How: Mirrors the Governance/Connections precedents; one InitializeUpdates() call from SettingsHavenPage.axaml.cs, cancellation through _lifetime, UI writes marshalled to the Avalonia thread.
 * Why: The scene stays presentation-only while this partial keeps product state in existing update services.
 * Maintenance: Keep honesty rules: Store-managed installs never enable download/channel controls, placeholder feeds read as not-yet-configured, and no path ever claims an install completed.
 */

using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using Haven.UI;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Pages.Settings;

public sealed partial class SettingsHavenPage
{
    private IUpdateService? _updates;
    private DirectUpdateOptions? _updateOptions;
    private bool _loadingUpdates;

    private void InitializeUpdates()
    {
        _updates = App.Services?.GetService<IUpdateService>();
        _updateOptions = App.Services?.GetService<DirectUpdateOptions>();

        _route.UpdatesCheckNowButton.Invoked += async (_, _) => await RunManualUpdateCheckAsync();
        _route.UpdatesChannelSelect.SelectionChanged += async (_, _) => await SaveUpdatePreferencesAsync();
        _route.UpdatesBackgroundChecksToggle.CheckedChanged += async (_, _) => await SaveUpdatePreferencesAsync();

        if (_updates is null)
        {
            _route.UpdatesInstallSourceText.Content = "Installation source: unavailable";
            _route.UpdatesLatestStateText.Content = "Update services are unavailable in this build.";
            _route.UpdatesCheckNowButton.SetValue(HavenProperties.Enabled, false);
            _route.UpdatesChannelSelect.SetValue(HavenProperties.Enabled, false);
            return;
        }

        _updates.StatusChanged += OnUpdateStatusChanged;
        _ = RefreshUpdatesAsync();
    }

    private void OnUpdateStatusChanged(UpdateStatusReport report)
    {
        if (_disposed) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || _updates is null) return;
            _route.SetUpdateStatus(report, IsDirectFeedConfigured());
        });
    }

    private async Task RefreshUpdatesAsync()
    {
        if (_disposed || _updates is null) return;
        _loadingUpdates = true;
        try
        {
            var preferences = await _updates.GetPreferencesAsync(_lifetime.Token);
            if (_disposed) return;
            _route.UpdatesChannelSelect.SelectedIndex = Math.Clamp((int)preferences.PreferredChannel, 0, 2);
            _route.UpdatesBackgroundChecksToggle.IsChecked = preferences.BackgroundChecksEnabled;

            var report = await _updates.GetStatusAsync(_lifetime.Token);
            if (_disposed) return;
            _route.SetUpdateStatus(report, IsDirectFeedConfigured());
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!_disposed) _route.UpdatesStatusText.Content = $"Could not load update status: {ex.Message}";
        }
        finally
        {
            _loadingUpdates = false;
        }
    }

    private async Task RunManualUpdateCheckAsync()
    {
        if (_disposed || _updates is null) return;
        _route.UpdatesCheckNowButton.SetValue(HavenProperties.Enabled, false);
        try
        {
            await _updates.CheckInBackgroundAsync(_lifetime.Token);
            await RefreshUpdatesAsync();
            if (!_disposed) _route.SetStatus("Update check finished.");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (HttpRequestException ex)
        {
            // CheckInBackgroundAsync normally converts failures into Failed reports; surface any escape honestly.
            if (!_disposed) _route.UpdatesStatusText.Content = $"The update feed could not be reached: {ex.Message}";
        }
        catch (Exception ex)
        {
            if (!_disposed) _route.UpdatesStatusText.Content = $"Update check failed: {ex.Message}";
        }
        finally
        {
            if (!_disposed) _route.UpdatesCheckNowButton.SetValue(HavenProperties.Enabled, true);
        }
    }

    private async Task SaveUpdatePreferencesAsync()
    {
        if (_disposed || _updates is null || _loadingUpdates) return;
        var channel = _route.UpdatesChannelSelect.SelectedIndex switch
        {
            1 => UpdateChannel.Preview,
            2 => UpdateChannel.Development,
            _ => UpdateChannel.Stable
        };
        var preferences = new UpdatePreferences(_route.UpdatesBackgroundChecksToggle.IsChecked, channel);
        try
        {
            await _updates.SetPreferencesAsync(preferences, _lifetime.Token);
            if (!_disposed)
            {
                _route.SetStatus($"Saved update preferences ({channel} channel, background checks {(preferences.BackgroundChecksEnabled ? "on" : "off")}).");
                _bus.Fire("Settings.Updates.PreferencesSaved");
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!_disposed) _route.UpdatesStatusText.Content = $"Could not save update preferences: {ex.Message}";
        }
    }

    /// <summary>
    /// Returns true only when every direct-install channel URL has been moved off the placeholder template,
    /// so a partially configured feed still reads as "not yet configured" instead of promising working checks.
    /// </summary>
    private bool IsDirectFeedConfigured()
    {
        if (_updateOptions is null) return false;
        return !string.Equals(_updateOptions.StableUrl, DirectUpdateOptions.PlaceholderChannelTemplate, StringComparison.OrdinalIgnoreCase)
               && !string.Equals(_updateOptions.PreviewUrl, DirectUpdateOptions.PlaceholderChannelTemplate, StringComparison.OrdinalIgnoreCase)
               && !string.Equals(_updateOptions.DevelopmentUrl, DirectUpdateOptions.PlaceholderChannelTemplate, StringComparison.OrdinalIgnoreCase);
    }

    private void DetachUpdates()
    {
        if (_updates is not null)
        {
            _updates.StatusChanged -= OnUpdateStatusChanged;
        }
    }
}
