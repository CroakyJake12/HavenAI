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

    public BrowserView()
    {
        InitializeComponent();
        NativeBrowser.EnvironmentRequested += (_, args) => ConfigureEnvironment(args);
        DataContextChanged += (_, _) =>
        {
            if (_viewModel is not null) _viewModel.ImportExtensionRequested -= OnImportExtensionRequested;
            DetachBrowser();
            _viewModel = DataContext as BrowserPageViewModel;
            if (_viewModel is not null) _viewModel.ImportExtensionRequested += OnImportExtensionRequested;
            if (_utilities is not null) _utilities.DataContext = DataContext;
            EnsureUtilities();
            AttachBrowser();
        };
        AttachedToVisualTree += (_, _) =>
        {
            EnsureUtilities();
            AttachBrowser();
        };
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DetachBrowser();
        base.OnDetachedFromVisualTree(e);
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
            .FirstOrDefault(textBox => string.Equals(
                textBox.PlaceholderText,
                "Search or enter address",
                StringComparison.Ordinal));
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

    private void AttachBrowser()
    {
        if (_host is not null || !this.IsAttachedToVisualTree() || _viewModel is not { } vm) return;

        try
        {
            Directory.CreateDirectory(vm.Browser.ProfileDirectory);
            var services = App.Services ?? throw new InvalidOperationException("Haven services are unavailable.");
            var permissions = BrowserSitePermissionStoreProvider.Get(services.GetRequiredService<IAppPaths>());
            _host = new NativeWebViewHost(NativeBrowser, permissions);
            vm.Browser.Attach(_host);
            _ = vm.NavigateSafelyAsync();
        }
        catch (Exception ex)
        {
            vm.ReportBrowserError(ex);
        }
    }

    private void OnAddressKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not BrowserPageViewModel vm) return;
        vm.NavigateCommand.Execute(null);
        e.Handled = true;
    }

    private void ConfigureEnvironment(object arguments)
    {
        if (_viewModel is not { } vm) return;
        Directory.CreateDirectory(vm.Browser.ProfileDirectory);
        SetPlatformOption(arguments, "UserDataFolder", vm.Browser.ProfileDirectory);
        SetPlatformOption(arguments, "ProfileName", "Haven");
    }

    private static void SetPlatformOption(object target, string name, string value)
    {
        try
        {
            var property = target.GetType().GetProperty(name);
            if (property?.CanWrite == true && property.PropertyType == typeof(string)) property.SetValue(target, value);
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

    public Task GoBackAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_webView.CanGoBack) _webView.GoBack();
        return Task.CompletedTask;
    }

    public Task GoForwardAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_webView.CanGoForward) _webView.GoForward();
        return Task.CompletedTask;
    }

    public Task ReloadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _webView.Refresh();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _webView.Stop();
        return Task.CompletedTask;
    }

    public Task<string?> ExecuteScriptAsync(string script, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _webView.InvokeScript(script);
    }

    public async Task OpenDeveloperToolsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var targets = new List<object> { _webView };
        var adapter = _webView.GetType().GetProperty("Adapter")?.GetValue(_webView);
        if (adapter is not null) targets.Add(adapter);
        foreach (var target in targets)
        {
            var method = target.GetType().GetMethods().FirstOrDefault(candidate =>
                candidate.GetParameters().Length == 0 && candidate.Name is "OpenDevToolsWindow" or "OpenDeveloperTools" or "ShowDevTools");
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
        if (args.IsSuccess && assessment.IsAllowed && args.Request.Scheme is "http" or "https")
            _lastCommittedAddress = args.Request;
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
        if (!assessment.IsAllowed)
        {
            Publish(_state with { Status = assessment.Reason });
            return;
        }

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
        if (!_adapterLost)
        {
            Publish(Snapshot("Native browser ready"));
            return;
        }

        _adapterLost = false;
        if (!_recoveryLimiter.TryAcquire(DateTimeOffset.UtcNow))
        {
            Publish(_state with { IsLoading = false, Status = "Browser recovery paused after repeated adapter failures. Use Reload to try again." });
            return;
        }

        var restore = _lastCommittedAddress;
        if (restore is null)
        {
            Publish(Snapshot("Browser adapter recovered."));
            return;
        }

        Publish(_state with { Address = restore, IsLoading = true, Status = "Browser adapter recovered. Restoring the last page…" });
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed) _webView.Navigate(restore);
        });
    }

    private BrowserSnapshot Snapshot(string status, bool isLoading = false) => new(
        _webView.Source,
        _webView.Source?.Host ?? "Browser",
        _webView.CanGoBack,
        _webView.CanGoForward,
        isLoading,
        status);

    private void Publish(BrowserSnapshot state)
    {
        void Update()
        {
            if (_disposed) return;
            _state = state;
            StateChanged?.Invoke(this, state);
        }
        if (Dispatcher.UIThread.CheckAccess()) Update();
        else Dispatcher.UIThread.Post(Update);
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
