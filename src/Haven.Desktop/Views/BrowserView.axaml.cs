using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Browser;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views;

public sealed partial class BrowserView : UserControl
{
    private NativeWebViewHost? _host;
    private BrowserPageViewModel? _viewModel;

    public BrowserView()
    {
        InitializeComponent();
        NativeBrowser.EnvironmentRequested += (_, args) => ConfigureEnvironment(args);
        DataContextChanged += (_, _) =>
        {
            if (_viewModel is not null) _viewModel.ImportExtensionRequested -= OnImportExtensionRequested;
            _viewModel = DataContext as BrowserPageViewModel;
            if (_viewModel is not null) _viewModel.ImportExtensionRequested += OnImportExtensionRequested;
            AttachBrowser();
        };
        AttachedToVisualTree += (_, _) => AttachBrowser();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_host is not null && DataContext is BrowserPageViewModel vm)
        {
            vm.Browser.Detach(_host);
        }
        _host = null;
        base.OnDetachedFromVisualTree(e);
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

    private void AttachBrowser()
    {
        if (_host is not null || !this.IsAttachedToVisualTree() || DataContext is not BrowserPageViewModel vm) return;

        try
        {
            Directory.CreateDirectory(vm.Browser.ProfileDirectory);
            _host = new NativeWebViewHost(NativeBrowser);
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
        if (DataContext is not BrowserPageViewModel vm) return;
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

internal sealed class NativeWebViewHost : IEmbeddedBrowserHost
{
    private readonly NativeWebView _webView;
    private BrowserSnapshot _state;

    public NativeWebViewHost(NativeWebView webView)
    {
        _webView = webView;
        _state = Snapshot("Native browser ready");
        _webView.AdapterCreated += (_, _) => Publish(Snapshot("Native browser ready"));
        _webView.NavigationStarted += (_, _) => Publish(Snapshot("Loading…", isLoading: true));
        _webView.NavigationCompleted += (_, _) => Publish(Snapshot(_webView.Source?.Host ?? "Ready"));
    }

    public event EventHandler<BrowserSnapshot>? StateChanged;
    public BrowserSnapshot State => _state;

    public Task NavigateAsync(Uri address, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
            _state = state;
            StateChanged?.Invoke(this, state);
        }
        if (Dispatcher.UIThread.CheckAccess()) Update();
        else Dispatcher.UIThread.Post(Update);
    }
}
