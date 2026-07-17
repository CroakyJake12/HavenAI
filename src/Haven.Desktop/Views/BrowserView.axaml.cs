using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Browser;
using Haven.Desktop.Controls;
using Haven.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views;

public sealed partial class BrowserView : UserControl
{
    private NativeWebViewHost? _host;
    private BrowserPageViewModel? _viewModel;
    private BrowserUtilitiesControl? _utilities;
    private NativeWebView _nativeBrowser = null!;
    private BrowserPrivateProfileManager? _privateProfiles;
    private Guid? _mountedTabId;
    private bool _mountedPrivate;
    private CancellationTokenSource _lifetime = new();

    public BrowserView()
    {
        InitializeComponent();
        _nativeBrowser = NativeBrowser;
        ConfigureEnvironmentForCurrentTab(_nativeBrowser);
        DataContextChanged += (_, _) => ChangeViewModel(DataContext as BrowserPageViewModel);
        AttachedToVisualTree += (_, _) =>
        {
            if (_lifetime.IsCancellationRequested)
            {
                _lifetime.Dispose();
                _lifetime = new CancellationTokenSource();
            }
            EnsureUtilities();
            _ = MountSelectedTabAsync();
        };
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _lifetime.Cancel();
        DetachBrowser();
        base.OnDetachedFromVisualTree(e);
    }

    private void ChangeViewModel(BrowserPageViewModel? next)
    {
        if (_viewModel is not null)
        {
            _viewModel.ImportExtensionRequested -= OnImportExtensionRequested;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Tabs.CollectionChanged -= OnTabsChanged;
        }

        DetachBrowser();
        _viewModel = next;
        _privateProfiles = next is null ? null : new BrowserPrivateProfileManager(next.Browser.ProfileDirectory);
        _mountedTabId = null;
        _mountedPrivate = false;

        if (_viewModel is not null)
        {
            _viewModel.ImportExtensionRequested += OnImportExtensionRequested;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.Tabs.CollectionChanged += OnTabsChanged;
        }

        if (_utilities is not null) _utilities.DataContext = DataContext;
        EnsureUtilities();
        _ = CleanupPrivateProfilesAsync();
        _ = MountSelectedTabAsync();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(BrowserPageViewModel.SelectedTab) or nameof(BrowserPageViewModel.IsPrivate))
            _ = MountSelectedTabAsync();
    }

    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs args) => _ = CleanupPrivateProfilesAsync();

    private async Task CleanupPrivateProfilesAsync()
    {
        if (_privateProfiles is null || _viewModel is null) return;
        try
        {
            var active = _viewModel.Tabs.Where(tab => tab.IsPrivate).Select(tab => tab.Id).ToHashSet();
            await _privateProfiles.CleanupOrphansAsync(active, _lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (IOException ex) { _viewModel.ReportBrowserError(new IOException("A private Browser profile could not be removed. Close other Browser processes and try again.", ex)); }
        catch (UnauthorizedAccessException ex) { _viewModel.ReportBrowserError(new UnauthorizedAccessException("Haven could not remove a private Browser profile.", ex)); }
    }

    private void DetachBrowser()
    {
        if (_host is null) return;
        _viewModel?.Browser.Detach(_host);
        _host.Dispose();
        _host = null;
    }

    private async void OnImportExtensionRequested(object? sender, bool convertChrome)
    {
        if (DataContext is not BrowserPageViewModel vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        try
        {
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = convertChrome ? "Select a Chrome manifest.json" : "Select a Haven extension manifest",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("JSON manifest") { Patterns = ["*.json"] }]
            });
            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path)) await vm.ImportExtensionAsync(path, convertChrome);
        }
        catch (Exception ex) { vm.ReportBrowserError(ex); }
    }

    private void EnsureUtilities()
    {
        if (_utilities is not null) return;
        var addressBox = this.GetVisualDescendants()
            .OfType<TextBox>()
            .FirstOrDefault(textBox => string.Equals(textBox.PlaceholderText, "Search or enter address", StringComparison.Ordinal));
        if (addressBox?.Parent is not Grid navigation) return;

        navigation.ColumnDefinitions.Insert(5, new ColumnDefinition(GridLength.Auto));
        foreach (var child in navigation.Children.ToArray())
        {
            var column = Grid.GetColumn(child);
            if (column >= 5) Grid.SetColumn(child, column + 1);
        }
        _utilities = new BrowserUtilitiesControl { DataContext = DataContext };
        Grid.SetColumn(_utilities, 5);
        navigation.Children.Add(_utilities);
    }

    private async Task MountSelectedTabAsync()
    {
        if (!this.IsAttachedToVisualTree() || _viewModel?.SelectedTab is not { } tab) return;
        if (_host is not null && _mountedTabId == tab.Id && _mountedPrivate == tab.IsPrivate) return;

        try
        {
            var token = _lifetime.Token;
            token.ThrowIfCancellationRequested();
            var profileDirectory = tab.IsPrivate
                ? await (_privateProfiles ?? throw new InvalidOperationException("Private profile management is unavailable."))
                    .CreateAsync(tab.Id, token).ConfigureAwait(false)
                : _viewModel.Browser.ProfileDirectory;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                token.ThrowIfCancellationRequested();
                ReplaceNativeBrowser(profileDirectory, tab.IsPrivate, tab.Id);
                AttachBrowser();
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _viewModel.ReportBrowserError(ex); }
    }

    private void ReplaceNativeBrowser(string profileDirectory, bool isPrivate, Guid tabId)
    {
        DetachBrowser();
        if (_nativeBrowser.Parent is not Border container)
            throw new InvalidOperationException("The native Browser host container is unavailable.");

        var replacement = new NativeWebView();
        replacement.EnvironmentRequested += (_, args) => ConfigureEnvironment(args, profileDirectory, isPrivate, tabId);
        container.Child = replacement;
        _nativeBrowser = replacement;
        _mountedTabId = tabId;
        _mountedPrivate = isPrivate;
    }

    private void AttachBrowser()
    {
        if (_host is not null || !this.IsAttachedToVisualTree() || _viewModel is not { } vm) return;
        try
        {
            var services = App.Services ?? throw new InvalidOperationException("Haven services are unavailable.");
            var permissions = BrowserSitePermissionStoreProvider.Get(services.GetRequiredService<IAppPaths>());
            _host = new NativeWebViewHost(_nativeBrowser, permissions);
            vm.Browser.Attach(_host);
            _ = vm.NavigateSafelyAsync();
        }
        catch (Exception ex) { vm.ReportBrowserError(ex); }
    }

    private void OnAddressKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not BrowserPageViewModel vm) return;
        vm.NavigateCommand.Execute(null);
        e.Handled = true;
    }

    private void ConfigureEnvironmentForCurrentTab(NativeWebView webView)
    {
        webView.EnvironmentRequested += (_, args) =>
        {
            if (_viewModel?.SelectedTab is not { } tab) return;
            var profile = tab.IsPrivate && _privateProfiles is not null
                ? _privateProfiles.GetProfileDirectory(tab.Id)
                : _viewModel.Browser.ProfileDirectory;
            ConfigureEnvironment(args, profile, tab.IsPrivate, tab.Id);
        };
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
            if (property?.CanWrite == true && property.PropertyType.IsInstanceOfType(value)) property.SetValue(target, value);
        }
        catch (Exception ex) when (ex is ArgumentException or System.Reflection.TargetInvocationException) { }
    }
}

internal sealed class NativeWebViewHost : IEmbeddedBrowserHost, IDisposable
{
    private readonly NativeWebView _webView;
    private readonly BrowserSitePermissionStore _permissions;
    private readonly BrowserRecoveryLimiter _recoveryLimiter = new(2, TimeSpan.FromMinutes(1));
    private BrowserSnapshot _state;
    private Uri? _lastCommittedAddress;
    private bool _adapterLost;
    private bool _disposed;

    public NativeWebViewHost(NativeWebView webView, BrowserSitePermissionStore permissions)
    {
        _webView = webView;
        _permissions = permissions;
        _state = Snapshot("Native browser ready");
        _webView.AdapterCreated += OnAdapterCreated;
        _webView.AdapterDestroyed += OnAdapterDestroyed;
        _webView.NavigationStarted += OnNavigationStarted;
        _webView.NavigationCompleted += OnNavigationCompleted;
        _webView.NewWindowRequested += OnNewWindowRequested;
    }

    public event EventHandler<BrowserSnapshot>? StateChanged;
    public BrowserSnapshot State => _state;

    public Task NavigateAsync(Uri address, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var assessment = BrowserNativeRequestPolicy.AssessTopLevel(address);
        if (!assessment.IsAllowed) throw new InvalidOperationException(assessment.Reason);
        Publish(_state with { Address = address, IsLoading = true, Status = "Loading…" });
        _webView.Navigate(address);
        return Task.CompletedTask;
    }

    public Task GoBackAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); if (_webView.CanGoBack) _webView.GoBack(); return Task.CompletedTask; }
    public Task GoForwardAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); if (_webView.CanGoForward) _webView.GoForward(); return Task.CompletedTask; }
    public Task ReloadAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); _webView.Refresh(); return Task.CompletedTask; }
    public Task StopAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); _webView.Stop(); return Task.CompletedTask; }
    public Task<string?> ExecuteScriptAsync(string script, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return _webView.InvokeScript(script); }

    public async Task OpenDeveloperToolsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var targets = new List<object> { _webView };
        var adapter = _webView.GetType().GetProperty("Adapter")?.GetValue(_webView);
        if (adapter is not null) targets.Add(adapter);
        foreach (var target in targets)
        {
            var method = target.GetType().GetMethods().FirstOrDefault(candidate => candidate.GetParameters().Length == 0 && candidate.Name is "OpenDevToolsWindow" or "OpenDeveloperTools" or "ShowDevTools");
            if (method is null) continue;
            if (method.Invoke(target, null) is Task task) await task.ConfigureAwait(false);
            return;
        }
        throw new InvalidOperationException("Developer tools are not exposed by the installed native WebView adapter.");
    }

    private void OnNavigationStarted(object? sender, WebViewNavigationStartingEventArgs args)
    {
        if (_disposed) return;
        var assessment = BrowserNativeRequestPolicy.AssessTopLevel(args.Request);
        if (!assessment.IsAllowed)
        {
            args.Cancel = true;
            Publish(_state with { IsLoading = false, Status = "Navigation blocked: " + assessment.Reason });
            return;
        }
        Publish(_state with { Address = args.Request, IsLoading = true, Status = "Loading…" });
    }

    private void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs args)
    {
        if (_disposed) return;
        var assessment = BrowserNativeRequestPolicy.AssessTopLevel(args.Request);
        if (args.IsSuccess && assessment.IsAllowed && args.Request.Scheme is "http" or "https") _lastCommittedAddress = args.Request;
        Publish(Snapshot(args.IsSuccess ? args.Request.Host : "Navigation failed"));
    }

    private void OnNewWindowRequested(object? sender, WebViewNewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        if (_disposed) return;
        var requester = _webView.Source;
        var requesterAssessment = BrowserNativeRequestPolicy.AssessTopLevel(requester);
        var decision = requesterAssessment.IsAllowed && requester?.Scheme is "http" or "https"
            ? _permissions.GetDecision(requester, BrowserSitePermissionKind.WindowManagement)
            : BrowserSitePermissionDecision.Ask;
        var assessment = BrowserNativeRequestPolicy.AssessPopup(requester, args.Request, decision);
        if (!assessment.IsAllowed) { Publish(_state with { Status = assessment.Reason }); return; }
        Publish(_state with { Address = args.Request, IsLoading = true, Status = "Opening approved popup in the current tab…" });
        _webView.Navigate(args.Request);
    }

    private void OnAdapterDestroyed(object? sender, WebViewAdapterEventArgs args)
    {
        if (_disposed) return;
        _adapterLost = true;
        Publish(_state with { IsLoading = false, Status = "Browser process stopped. Waiting for the native adapter to recover…" });
    }

    private void OnAdapterCreated(object? sender, WebViewAdapterEventArgs args)
    {
        if (_disposed) return;
        if (!_adapterLost) { Publish(Snapshot("Native browser ready")); return; }
        _adapterLost = false;
        if (!_recoveryLimiter.TryAcquire(DateTimeOffset.UtcNow))
        {
            Publish(_state with { IsLoading = false, Status = "Browser recovery paused after repeated adapter failures. Use Reload to try again." });
            return;
        }
        var restore = _lastCommittedAddress;
        if (restore is null) { Publish(Snapshot("Browser adapter recovered.")); return; }
        Publish(_state with { Address = restore, IsLoading = true, Status = "Browser adapter recovered. Restoring the last page…" });
        Dispatcher.UIThread.Post(() => { if (!_disposed) _webView.Navigate(restore); });
    }

    private BrowserSnapshot Snapshot(string status, bool isLoading = false) => new(_webView.Source, _webView.Source?.Host ?? "Browser", _webView.CanGoBack, _webView.CanGoForward, isLoading, status);

    private void Publish(BrowserSnapshot state)
    {
        void Update() { if (_disposed) return; _state = state; StateChanged?.Invoke(this, state); }
        if (Dispatcher.UIThread.CheckAccess()) Update(); else Dispatcher.UIThread.Post(Update);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _webView.AdapterCreated -= OnAdapterCreated;
        _webView.AdapterDestroyed -= OnAdapterDestroyed;
        _webView.NavigationStarted -= OnNavigationStarted;
        _webView.NavigationCompleted -= OnNavigationCompleted;
        _webView.NewWindowRequested -= OnNewWindowRequested;
        StateChanged = null;
    }
}
