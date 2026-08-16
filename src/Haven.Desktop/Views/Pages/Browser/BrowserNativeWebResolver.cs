/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Views/Pages/Browser/BrowserNativeWebResolver.cs, Browser platform backend.
 * What: Maps the Browser Haven.UI Web primitive to cached real native WebViews.
 * How: Keeps one native WebView per Browser tab, preserves private profiles, and attaches the selected view to BrowserSessionService.
 * Why: Browser product chrome remains Haven.UI-owned while page rendering stays platform-native.
 */

using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Browser;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Pages.Browser;

internal sealed class BrowserNativeWebResolver : IHavenAvaloniaNativeControlResolver, IDisposable
{
    private readonly BrowserPage _page;
    private readonly Haven.UI.Components.Web _webElement;
    private readonly Grid _surface = new();
    private readonly Dictionary<Guid, NativeWebView> _webViews = [];
    private readonly BrowserPrivateProfileManager _privateProfiles;
    private readonly CancellationTokenSource _lifetime = new();
    private NativeWebViewHost? _host;
    private NativeWebView? _activeWebView;
    private Guid? _mountedTabId;
    private bool _mountedPrivate;
    private bool _disposed;

    public BrowserNativeWebResolver(BrowserPage page, Haven.UI.Components.Web webElement)
    {
        _page = page ?? throw new ArgumentNullException(nameof(page));
        _webElement = webElement ?? throw new ArgumentNullException(nameof(webElement));
        _privateProfiles = new BrowserPrivateProfileManager(page.Browser.ProfileDirectory);
        _surface.AttachedToVisualTree += OnAttached;
        _surface.DetachedFromVisualTree += OnDetached;
        _page.PropertyChanged += OnPagePropertyChanged;
        _page.Tabs.CollectionChanged += OnTabsChanged;
    }

    public bool TryCreate(HavenElement element, out Control? control)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!ReferenceEquals(element, _webElement))
        {
            control = null;
            return false;
        }
        control = _surface;
        return true;
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e) => _ = MountSelectedTabAsync();
    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e) => DetachBrowser();

    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BrowserPage.SelectedTab) or nameof(BrowserPage.IsPrivate))
            _ = MountSelectedTabAsync();
    }

    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is not BrowserTabViewModel tab || !_webViews.Remove(tab.Id, out var webView))
                    continue;
                _surface.Children.Remove(webView);
                if (_mountedTabId == tab.Id)
                {
                    DetachBrowser();
                    _activeWebView = null;
                    _mountedTabId = null;
                }
            }
        }
        _ = CleanupPrivateProfilesAsync();
    }

    private async Task MountSelectedTabAsync()
    {
        if (_disposed || !_surface.IsAttachedToVisualTree() || _page.SelectedTab is not { } tab)
            return;
        if (_host is not null && _mountedTabId == tab.Id && _mountedPrivate == tab.IsPrivate)
            return;
        try
        {
            var token = _lifetime.Token;
            token.ThrowIfCancellationRequested();
            var cached = _webViews.ContainsKey(tab.Id);
            var profileDirectory = tab.IsPrivate
                ? await _privateProfiles.CreateAsync(tab.Id, token).ConfigureAwait(false)
                : _page.Browser.ProfileDirectory;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                token.ThrowIfCancellationRequested();
                ReplaceNativeBrowser(profileDirectory, tab.IsPrivate, tab.Id);
                AttachBrowser(skipNavigation: cached);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { _page.ReportBrowserError(exception); }
    }

    private void ReplaceNativeBrowser(string profileDirectory, bool isPrivate, Guid tabId)
    {
        DetachBrowser();
        if (!_webViews.TryGetValue(tabId, out var replacement))
        {
            replacement = new NativeWebView();
            replacement.EnvironmentRequested += (_, args) =>
                ConfigureEnvironment(args, profileDirectory, isPrivate, tabId);
            replacement.IsVisible = false;
            _surface.Children.Add(replacement);
            _webViews[tabId] = replacement;
        }
        foreach (var pair in _webViews)
            pair.Value.IsVisible = pair.Key == tabId;
        _activeWebView = replacement;
        _mountedTabId = tabId;
        _mountedPrivate = isPrivate;
    }

    private void AttachBrowser(bool skipNavigation)
    {
        if (_host is not null || !_surface.IsAttachedToVisualTree() || _activeWebView is null)
            return;
        try
        {
            var services = App.Services ?? throw new InvalidOperationException("Haven services are unavailable.");
            var permissions = BrowserSitePermissionStoreProvider.Get(services.GetRequiredService<IAppPaths>());
            _host = new NativeWebViewHost(_activeWebView, permissions, _page.OpenPopupInNewTabAsync);
            _page.Browser.Attach(_host);
            if (!skipNavigation)
                _ = _page.NavigateSafelyAsync();
        }
        catch (Exception exception) { _page.ReportBrowserError(exception); }
    }

    private void DetachBrowser()
    {
        if (_host is null) return;
        _page.Browser.Detach(_host);
        _host.Dispose();
        _host = null;
    }

    private async Task CleanupPrivateProfilesAsync()
    {
        try
        {
            var active = _page.Tabs.Where(tab => tab.IsPrivate).Select(tab => tab.Id).ToHashSet();
            await _privateProfiles.CleanupOrphansAsync(active, _lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (IOException exception)
        {
            _page.ReportBrowserError(new IOException(
                "A private Browser profile could not be removed. Close other Browser processes and try again.",
                exception));
        }
        catch (UnauthorizedAccessException exception)
        {
            _page.ReportBrowserError(new UnauthorizedAccessException(
                "Haven could not remove a private Browser profile.", exception));
        }
    }

    private static void ConfigureEnvironment(object arguments, string profileDirectory, bool isPrivate, Guid tabId)
    {
        Directory.CreateDirectory(profileDirectory);
        SetPlatformOption(arguments, "UserDataFolder", profileDirectory);
        SetPlatformOption(arguments, "ProfileName", isPrivate ? "Private-" + tabId.ToString("N") : "Haven");
        SetPlatformOption(arguments, "InPrivateModeEnabled", isPrivate);
    }

    private static void SetPlatformOption(object target, string name, object value)
    {
        try
        {
            var property = target.GetType().GetProperty(name);
            if (property?.CanWrite == true && property.PropertyType.IsInstanceOfType(value))
                property.SetValue(target, value);
        }
        catch (Exception exception) when (
            exception is ArgumentException or System.Reflection.TargetInvocationException) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _page.PropertyChanged -= OnPagePropertyChanged;
        _page.Tabs.CollectionChanged -= OnTabsChanged;
        _surface.AttachedToVisualTree -= OnAttached;
        _surface.DetachedFromVisualTree -= OnDetached;
        DetachBrowser();
        _lifetime.Cancel();
        _surface.Children.Clear();
        _webViews.Clear();
        _lifetime.Dispose();
    }
}
