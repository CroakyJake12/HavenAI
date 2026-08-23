using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Desktop.HavenUI.Components;

namespace Haven.Desktop.Views.Pages.ProjectPreview;

/// <summary>Native project preview surface with lifecycle-based resource throttling.</summary>
public sealed class ProjectPreviewPage : UserControl, IDisposable
{
    private readonly IProjectPreviewProvider _provider;
    private readonly string _projectRoot;
    private readonly NativeWebView _webView = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Classes = { "muted" } };
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, Height = 3 };
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _reloadDebounce;
    private CancellationTokenSource? _startingCancellation;
    private CancellationTokenSource? _hiddenDisposal;
    private IProjectPreviewSession? _session;
    private readonly TimeSpan _hiddenDisposalDelay;
    private bool _starting;
    private bool _disposed;

    public ProjectPreviewPage(IProjectPreviewProvider provider, string projectRoot, TimeSpan? hiddenDisposalDelay = null)
    {
        _provider = provider;
        _projectRoot = projectRoot;
        _hiddenDisposalDelay = hiddenDisposalDelay ?? TimeSpan.FromSeconds(15);
        var refresh = new HavenButton { Content = "Refresh", MinWidth = 88 };
        refresh.Click += (_, _) => Refresh();
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Avalonia.Thickness(14, 10), Children = { _status, refresh } };
        Grid.SetColumn(refresh, 1);
        Content = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), Children = { header, _progress, _webView } };
        Grid.SetRow(_progress, 1);
        Grid.SetRow(_webView, 2);
        _webView.NavigationStarted += (_, args) =>
        {
            if (_session is null || args.Request is null || !args.Request.IsLoopback || args.Request.Port != _session.PreviewUri.Port)
            {
                args.Cancel = true;
                _status.Text = "Preview navigation was blocked outside its loopback origin.";
                return;
            }
            _progress.IsVisible = true;
            _status.Text = "Loading live preview…";
        };
        _webView.NavigationCompleted += (_, args) =>
        {
            _progress.IsVisible = false;
            _status.Text = args.IsSuccess ? _session?.Descriptor.EntryDescription ?? "Live preview" : "Preview navigation failed.";
        };
        _webView.NewWindowRequested += (_, args) => args.Handled = true;
        AttachedToVisualTree += async (_, _) =>
        {
            CancelHiddenDisposal();
            await EnsureStartedAsync();
        };
        DetachedFromVisualTree += (_, _) => ScheduleHiddenDisposal();
    }

    private async Task EnsureStartedAsync()
    {
        if (_disposed || _starting || _session is not null) return;
        _starting = true;
        _progress.IsVisible = true;
        _status.Text = "Starting Project preview…";
        try
        {
            var session = await StartProviderSessionAsync();
            if (session is null)
            {
                _progress.IsVisible = false;
                return;
            }
            _session = session;
            _session.SourceChanged += OnSourceChanged;
            _webView.IsVisible = true;
            _webView.Navigate(_session.PreviewUri);
        }
        catch (Exception ex)
        {
            _progress.IsVisible = false;
            _status.Text = "Preview failed\n" + SensitiveTextRedactor.Redact(ex.Message, 2_000);
            _webView.IsVisible = false;
        }
        finally { _starting = false; }
    }

    internal async Task<IProjectPreviewSession?> StartProviderSessionAsync()
    {
        if (_disposed || _session is not null) return _session;
        var startingCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _startingCancellation = startingCancellation;
        try
        {
            var session = await _provider.StartAsync(_projectRoot, startingCancellation.Token);
            if (startingCancellation.IsCancellationRequested || _disposed)
            {
                await session.DisposeAsync();
                return null;
            }
            return session;
        }
        catch (OperationCanceledException) when (startingCancellation.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            if (ReferenceEquals(_startingCancellation, startingCancellation)) _startingCancellation = null;
            startingCancellation.Dispose();
        }
    }

    private void OnSourceChanged(object? sender, EventArgs e)
    {
        _reloadDebounce?.Cancel();
        _reloadDebounce?.Dispose();
        _reloadDebounce = new CancellationTokenSource();
        var token = _reloadDebounce.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(600, token);
                Dispatcher.UIThread.Post(Refresh);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }, token);
    }

    private void Refresh()
    {
        if (_session is null) { _ = EnsureStartedAsync(); return; }
        var builder = new UriBuilder(_session.PreviewUri) { Query = "havenRefresh=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        _webView.IsVisible = true;
        _webView.Navigate(builder.Uri);
    }

    internal void ScheduleHiddenDisposal()
    {
        CancelHiddenDisposal();
        var hiddenDisposal = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _hiddenDisposal = hiddenDisposal;
        _ = Task.Run(async () =>
        {
            try
            {
                await DisposeWhenHiddenAsync(
                    async _ => await Dispatcher.UIThread.InvokeAsync(() => this.IsAttachedToVisualTree()),
                    hiddenDisposal.Token);
            }
            catch (OperationCanceledException) when (hiddenDisposal.IsCancellationRequested) { }
            finally
            {
                if (ReferenceEquals(_hiddenDisposal, hiddenDisposal)) _hiddenDisposal = null;
                hiddenDisposal.Dispose();
            }
        }, hiddenDisposal.Token);
    }

    internal async Task DisposeWhenHiddenAsync(
        Func<CancellationToken, Task<bool>> isAttachedAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(isAttachedAsync);
        await Task.Delay(_hiddenDisposalDelay, cancellationToken);
        var isAttached = await isAttachedAsync(cancellationToken);
        if (_disposed || isAttached || cancellationToken.IsCancellationRequested) return;
        _startingCancellation?.Cancel();
        await StopSessionAsync();
    }

    private void CancelHiddenDisposal()
    {
        var hiddenDisposal = Interlocked.Exchange(ref _hiddenDisposal, null);
        if (hiddenDisposal is null) return;
        hiddenDisposal.Cancel();
    }

    private async Task StopSessionAsync()
    {
        var session = Interlocked.Exchange(ref _session, null);
        if (session is null) return;
        session.SourceChanged -= OnSourceChanged;
        await session.DisposeAsync();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelHiddenDisposal();
        _startingCancellation?.Cancel();
        _reloadDebounce?.Cancel();
        _reloadDebounce?.Dispose();
        _lifetime.Cancel();
        _ = StopSessionAsync();
        _lifetime.Dispose();
    }
}
