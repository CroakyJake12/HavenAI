/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Views/BrowserView.axaml.cs, in the Desktop view layer, where Avalonia controls connect XAML interaction to view models.
 * What: This file owns BrowserView, NativeWebViewHost. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

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

/// <summary>
/// Represents browser view and keeps its related state and behavior together.
/// </summary>
public sealed partial class BrowserView : UserControl
{
    /// <summary>
    /// Stores host locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private NativeWebViewHost? _host;
    /// <summary>
    /// Stores view model locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private BrowserPageViewModel? _viewModel;
    /// <summary>
    /// Stores utilities locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private BrowserUtilitiesControl? _utilities;
    /// <summary>
    /// Stores native browser locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private NativeWebView _nativeBrowser = null!;
    /// <summary>
    /// Stores private profiles locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private BrowserPrivateProfileManager? _privateProfiles;
    /// <summary>
    /// Stores mounted tab id locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private Guid? _mountedTabId;
    /// <summary>
    /// Stores mounted private locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _mountedPrivate;
    /// <summary>
    /// Stores lifetime locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
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

    /// <summary>
    /// Handles the detached from visual tree event raised by the UI or runtime.
    /// </summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _lifetime.Cancel();
        DetachBrowser();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// Performs the change view model step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Handles the view model property changed event raised by the UI or runtime.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(BrowserPageViewModel.SelectedTab) or nameof(BrowserPageViewModel.IsPrivate))
            _ = MountSelectedTabAsync();
    }

    /// <summary>
    /// Handles the tabs changed event raised by the UI or runtime.
    /// </summary>
    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs args) => _ = CleanupPrivateProfilesAsync();

    /// <summary>
    /// Performs cleanup private profiles asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs the detach browser step owned by this component.
    /// </summary>
    private void DetachBrowser()
    {
        if (_host is null) return;
        _viewModel?.Browser.Detach(_host);
        _host.Dispose();
        _host = null;
    }

    /// <summary>
    /// Handles the import extension requested event raised by the UI or runtime.
    /// </summary>
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

    /// <summary>
    /// Performs the ensure utilities step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs mount selected tab asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs the replace native browser step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the attach browser step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Handles the address key down event raised by the UI or runtime.
    /// </summary>
    private void OnAddressKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not BrowserPageViewModel vm) return;
        vm.NavigateCommand.Execute(null);
        e.Handled = true;
    }

    /// <summary>
    /// Performs the configure environment for current tab step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the configure environment step owned by this component.
    /// </summary>
    private static void ConfigureEnvironment(object arguments, string profileDirectory, bool isPrivate, Guid tabId)
    {
        Directory.CreateDirectory(profileDirectory);
        SetPlatformOption(arguments, "UserDataFolder", profileDirectory);
        SetPlatformOption(arguments, "ProfileName", isPrivate ? "Private-" + tabId.ToString("N") : "Haven");
        SetPlatformOption(arguments, "InPrivateModeEnabled", isPrivate);
    }

    /// <summary>
    /// Performs the set platform option step owned by this component.
    /// </summary>
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

/// <summary>
/// Represents native web view host and keeps its related state and behavior together.
/// </summary>
internal sealed class NativeWebViewHost : IEmbeddedBrowserHost, IDisposable
{
    /// <summary>
    /// Stores web view locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly NativeWebView _webView;
    /// <summary>
    /// Stores permissions locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly BrowserSitePermissionStore _permissions;
    /// <summary>
    /// Stores recovery limiter locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly BrowserRecoveryLimiter _recoveryLimiter = new(2, TimeSpan.FromMinutes(1));
    /// <summary>
    /// Stores state locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private BrowserSnapshot _state;
    /// <summary>
    /// Stores last committed address locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private Uri? _lastCommittedAddress;
    /// <summary>
    /// Stores adapter lost locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _adapterLost;
    /// <summary>
    /// Stores disposed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
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

    /// <summary>
    /// Stores state changed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event EventHandler<BrowserSnapshot>? StateChanged;
    /// <summary>
    /// Gets or updates state, the bindable or domain state represented by this property.
    /// </summary>
    public BrowserSnapshot State => _state;

    /// <summary>
    /// Performs navigate asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task NavigateAsync(Uri address, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var assessment = BrowserNativeRequestPolicy.AssessTopLevel(address);
        if (!assessment.IsAllowed) throw new InvalidOperationException(assessment.Reason);
        Publish(_state with { Address = address, IsLoading = true, Status = "Loading…" });
        _webView.Navigate(address);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Performs go back asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task GoBackAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); if (_webView.CanGoBack) _webView.GoBack(); return Task.CompletedTask; }
    /// <summary>
    /// Performs go forward asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task GoForwardAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); if (_webView.CanGoForward) _webView.GoForward(); return Task.CompletedTask; }
    /// <summary>
    /// Performs reload asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task ReloadAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); _webView.Refresh(); return Task.CompletedTask; }
    /// <summary>
    /// Performs stop asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); _webView.Stop(); return Task.CompletedTask; }
    /// <summary>
    /// Runs execute script async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    public Task<string?> ExecuteScriptAsync(string script, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return _webView.InvokeScript(script); }

    /// <summary>
    /// Performs open developer tools asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task OpenDeveloperToolsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Avalonia intentionally hides the platform adapter behind its native-control
        // implementation. Walk only the known WebView wrapper/adapter members until
        // WebView2's CoreWebView2 is found; this works across the compositor and HWND
        // adapters instead of assuming a public `Adapter` property exists.
        var queue = new Queue<object>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        queue.Enqueue(_webView);
        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = queue.Dequeue();
            if (!visited.Add(target)) continue;
            var type = target.GetType();
            var method = type.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .FirstOrDefault(candidate => candidate.GetParameters().Length == 0 && candidate.Name is "OpenDevToolsWindow" or "OpenDeveloperTools" or "ShowDevTools");
            if (method is not null)
            {
                if (method.Invoke(target, null) is Task task) await task.ConfigureAwait(false);
                return;
            }

            foreach (var property in type.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                         .Where(item => IsWebViewBridgeMember(item.Name) && item.GetIndexParameters().Length == 0))
            {
                try { if (property.GetValue(target) is { } value) queue.Enqueue(value); }
                catch (Exception ex) when (ex is System.Reflection.TargetInvocationException or MethodAccessException) { }
            }
            foreach (var field in type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                         .Where(item => IsWebViewBridgeMember(item.Name)))
            {
                if (field.GetValue(target) is { } value) queue.Enqueue(value);
            }
        }
        throw new InvalidOperationException("Developer tools are not exposed by the installed native WebView adapter.");
    }

    private static bool IsWebViewBridgeMember(string name) =>
        name.Contains("adapter", StringComparison.OrdinalIgnoreCase)
        || name.Contains("platform", StringComparison.OrdinalIgnoreCase)
        || name.Contains("webview", StringComparison.OrdinalIgnoreCase)
        || name.Contains("native", StringComparison.OrdinalIgnoreCase)
        || name.Contains("control", StringComparison.OrdinalIgnoreCase)
        || name.Contains("host", StringComparison.OrdinalIgnoreCase)
        || name.Contains("handler", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Handles the navigation started event raised by the UI or runtime.
    /// </summary>
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

    /// <summary>
    /// Handles the navigation completed event raised by the UI or runtime.
    /// </summary>
    private void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs args)
    {
        if (_disposed) return;
        var request = args.Request;
        if (request is null)
        {
            Publish(_state with
            {
                IsLoading = false,
                Status = "Navigation completed without a request URI."
            });
            return;
        }
        var assessment = BrowserNativeRequestPolicy.AssessTopLevel(request);
        if (args.IsSuccess && assessment.IsAllowed && request.Scheme is "http" or "https")
            _lastCommittedAddress = request;
        Publish(Snapshot(args.IsSuccess ? request.Host : "Navigation failed"));
    }

    /// <summary>
    /// Handles the new window requested event raised by the UI or runtime.
    /// </summary>
    private void OnNewWindowRequested(object? sender, WebViewNewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        if (_disposed) return;
        var requested = args.Request;
        if (requested is null)
        {
            Publish(_state with { Status = "Popup request was missing a destination." });
            return;
        }
        var requester = _webView.Source;
        var requesterAssessment = BrowserNativeRequestPolicy.AssessTopLevel(requester);
        var decision = requesterAssessment.IsAllowed && requester?.Scheme is "http" or "https"
            ? _permissions.GetDecision(requester, BrowserSitePermissionKind.WindowManagement)
            : BrowserSitePermissionDecision.Ask;
        var assessment = BrowserNativeRequestPolicy.AssessPopup(requester, requested, decision);
        if (!assessment.IsAllowed)
        {
            Publish(_state with { Status = assessment.Reason });
            return;
        }
        Publish(_state with
        {
            Address = requested,
            IsLoading = true,
            Status = "Opening approved popup in the current tab…"
        });
        _webView.Navigate(requested);
    }

    /// <summary>
    /// Handles the adapter destroyed event raised by the UI or runtime.
    /// </summary>
    private void OnAdapterDestroyed(object? sender, WebViewAdapterEventArgs args)
    {
        if (_disposed) return;
        _adapterLost = true;
        Publish(_state with { IsLoading = false, Status = "Browser process stopped. Waiting for the native adapter to recover…" });
    }

    /// <summary>
    /// Handles the adapter created event raised by the UI or runtime.
    /// </summary>
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

    /// <summary>
    /// Performs the snapshot step owned by this component.
    /// </summary>
    private BrowserSnapshot Snapshot(string status, bool isLoading = false) => new(_webView.Source, _webView.Source?.Host ?? "Browser", _webView.CanGoBack, _webView.CanGoForward, isLoading, status);

    /// <summary>
    /// Performs the publish step owned by this component.
    /// </summary>
    private void Publish(BrowserSnapshot state)
    {
        void Update() { if (_disposed) return; _state = state; StateChanged?.Invoke(this, state); }
        if (Dispatcher.UIThread.CheckAccess()) Update(); else Dispatcher.UIThread.Post(Update);
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
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
