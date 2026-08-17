using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Microsoft.Web.WebView2.Core;

namespace Haven.Desktop.Views.Pages.Browser;

/// <summary>
/// Bridges Avalonia's supported Windows WebView2 platform handle into Haven's approval-gated
/// page-download pipeline without creating a second browser or replaying the page request.
/// </summary>
internal sealed class BrowserNativeWebViewDownloadBridge
{
    private static readonly ConditionalWeakTable<NativeWebView, BrowserNativeWebViewDownloadBridge> Bridges = new();

    private readonly NativeWebView _webView;
    private readonly BrowserDownloadTransport _transport;
    private readonly IBrowserNativeDownloadService _nativeDownloads;
    private readonly bool _isPrivate;
    private readonly Action<Exception> _reportError;
    private CoreWebView2? _coreWebView2;

    private BrowserNativeWebViewDownloadBridge(
        NativeWebView webView,
        BrowserDownloadTransport transport,
        IBrowserNativeDownloadService nativeDownloads,
        bool isPrivate,
        Action<Exception> reportError)
    {
        _webView = webView;
        _transport = transport;
        _nativeDownloads = nativeDownloads;
        _isPrivate = isPrivate;
        _reportError = reportError;
        _webView.AdapterCreated += OnAdapterCreated;
        _webView.AdapterDestroyed += OnAdapterDestroyed;
    }

    public static void Attach(
        NativeWebView webView,
        BrowserDownloadTransport transport,
        IBrowserNativeDownloadService nativeDownloads,
        bool isPrivate,
        Action<Exception> reportError)
    {
        ArgumentNullException.ThrowIfNull(webView);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(nativeDownloads);
        ArgumentNullException.ThrowIfNull(reportError);
        if (Bridges.TryGetValue(webView, out _)) return;
        Bridges.Add(webView, new BrowserNativeWebViewDownloadBridge(
            webView, transport, nativeDownloads, isPrivate, reportError));
    }

    private void OnAdapterCreated(object? sender, WebViewAdapterEventArgs args)
    {
        DetachCoreWebView2();
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            if (args.TryGetPlatformHandle() is not IWindowsWebView2PlatformHandle handle
                || handle.CoreWebView2 == IntPtr.Zero)
                return;

            _coreWebView2 = CoreWebView2.CreateFromComICoreWebView2(handle.CoreWebView2);
            _coreWebView2.DownloadStarting += OnDownloadStarting;
        }
        catch (Exception exception)
        {
            _reportError(new InvalidOperationException(
                "Haven could not attach its managed download handler to the native browser.", exception));
        }
    }

    private void OnAdapterDestroyed(object? sender, WebViewAdapterEventArgs args) => DetachCoreWebView2();

    private async void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs args)
    {
        args.Handled = true;
        var deferral = args.GetDeferral();
        var operation = args.DownloadOperation;
        BrowserDownloadTransport.NativeDownloadPlan? plan = null;
        try
        {
            // Pause before any await. WebView2 therefore keeps the original authenticated/POST/
            // service-worker/blob request alive without racing into its default download location.
            operation.Pause();
            if (operation.TotalBytesToReceive is { } totalBytesToReceive
                && totalBytesToReceive > (ulong)BrowserDownloadTransport.MaximumDownloadBytes)
                throw new InvalidOperationException("The download exceeds Haven's 250 MB limit.");
            if (!Uri.TryCreate(operation.Uri, UriKind.Absolute, out var sourceAddress))
                throw new InvalidOperationException("The native browser download did not expose a valid source address.");

            var initiatorAddress = _webView.Source;
            var approvalAddress = IsHttpAddress(sourceAddress)
                ? sourceAddress
                : initiatorAddress is not null && IsHttpAddress(initiatorAddress)
                    ? initiatorAddress
                    : throw new UnauthorizedAccessException("Browser-local downloads require an active HTTP or HTTPS page origin.");

            var actionId = Guid.NewGuid();
            var suggestedFileName = string.IsNullOrWhiteSpace(args.ResultFilePath)
                ? null
                : Path.GetFileName(args.ResultFilePath);
            plan = await _transport.PrepareNativeDownloadAsync(
                actionId,
                sourceAddress,
                initiatorAddress,
                suggestedFileName,
                operation.ContentDisposition,
                CancellationToken.None);

            // WebView2 requires an absolute, non-existing result path. Haven points it at the
            // unique managed partial; finalization later hashes and atomically promotes it.
            args.ResultFilePath = plan.PartialPath;
            var execution = new WebView2NativeDownloadExecution(operation, plan, _transport);
            await _nativeDownloads.RequestNativeDownloadAsync(
                new BrowserNativeDownloadRequest(actionId, approvalAddress, plan.FileName, _isPrivate),
                execution,
                CancellationToken.None);
            // The operation intentionally remains paused until Browser approval calls ApproveAsync.
        }
        catch (Exception exception)
        {
            args.Cancel = true;
            try { operation.Cancel(); } catch { }
            if (plan is not null)
            {
                try { _transport.AbortNativeDownload(plan); } catch { }
            }
            if (exception is not OperationCanceledException)
            {
                _reportError(new InvalidOperationException(
                    "The page download could not enter Haven's approval queue. " + exception.Message,
                    exception));
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private static bool IsHttpAddress(Uri address) =>
        address.IsAbsoluteUri
        && address.Scheme is "http" or "https"
        && string.IsNullOrEmpty(address.UserInfo);

    private void DetachCoreWebView2()
    {
        if (_coreWebView2 is null) return;
        try { _coreWebView2.DownloadStarting -= OnDownloadStarting; }
        catch { }
        _coreWebView2 = null;
    }

    private sealed class WebView2NativeDownloadExecution : IBrowserNativeDownloadExecution
    {
        private readonly CoreWebView2DownloadOperation _operation;
        private readonly BrowserDownloadTransport.NativeDownloadPlan _plan;
        private readonly BrowserDownloadTransport _transport;
        private readonly TaskCompletionSource<BrowserDownloadRecord> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _started;
        private int _terminal;

        public WebView2NativeDownloadExecution(
            CoreWebView2DownloadOperation operation,
            BrowserDownloadTransport.NativeDownloadPlan plan,
            BrowserDownloadTransport transport)
        {
            _operation = operation ?? throw new ArgumentNullException(nameof(operation));
            _plan = plan ?? throw new ArgumentNullException(nameof(plan));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _operation.StateChanged += OnStateChanged;
            _operation.BytesReceivedChanged += OnBytesReceivedChanged;
        }

        public async Task<BrowserDownloadRecord> ExecuteAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.CompareExchange(ref _started, 1, 0) == 0)
            {
                try
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (_operation.BytesReceived > BrowserDownloadTransport.MaximumDownloadBytes)
                        {
                            _ = FailAsync(new InvalidOperationException(
                                "The download exceeded Haven's 250 MB limit while streaming."));
                            return;
                        }
                        if (_operation.State == CoreWebView2DownloadState.Completed)
                        {
                            _ = FinalizeAsync();
                            return;
                        }
                        if (_operation.State == CoreWebView2DownloadState.Interrupted && !_operation.CanResume)
                        {
                            _ = FailAsync(new InvalidOperationException(
                                "The native download cannot resume: " + _operation.InterruptReason));
                            return;
                        }
                        _operation.Resume();
                    });
                }
                catch (Exception exception)
                {
                    _ = FailAsync(exception);
                }
            }

            using var registration = cancellationToken.Register(() => _ = CancelAsync(CancellationToken.None));
            return await _completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task CancelAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.CompareExchange(ref _terminal, 1, 0) != 0) return;
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    DetachHandlers();
                    try { _operation.Cancel(); } catch { }
                });
            }
            finally
            {
                try { _transport.AbortNativeDownload(_plan); } catch { }
                _completion.TrySetCanceled();
            }
        }

        private void OnBytesReceivedChanged(object? sender, object args)
        {
            if (Volatile.Read(ref _terminal) != 0) return;
            if (_operation.BytesReceived > BrowserDownloadTransport.MaximumDownloadBytes)
                _ = FailAsync(new InvalidOperationException(
                    "The download exceeded Haven's 250 MB limit while streaming."));
        }

        private void OnStateChanged(object? sender, object args)
        {
            if (Volatile.Read(ref _started) == 0 || Volatile.Read(ref _terminal) != 0) return;
            switch (_operation.State)
            {
                case CoreWebView2DownloadState.Completed:
                    _ = FinalizeAsync();
                    break;
                case CoreWebView2DownloadState.Interrupted when !_operation.CanResume:
                    _ = FailAsync(new InvalidOperationException(
                        "The native download was interrupted: " + _operation.InterruptReason));
                    break;
            }
        }

        private async Task FinalizeAsync()
        {
            if (Interlocked.CompareExchange(ref _terminal, 1, 0) != 0) return;
            await Dispatcher.UIThread.InvokeAsync(DetachHandlers);
            try
            {
                var record = await _transport.FinalizeNativeDownloadAsync(
                    _plan,
                    _operation.MimeType,
                    CancellationToken.None).ConfigureAwait(false);
                _completion.TrySetResult(record);
            }
            catch (Exception exception)
            {
                try { _transport.AbortNativeDownload(_plan); } catch { }
                _completion.TrySetException(exception);
            }
        }

        private async Task FailAsync(Exception exception)
        {
            if (Interlocked.CompareExchange(ref _terminal, 1, 0) != 0) return;
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    DetachHandlers();
                    try { _operation.Cancel(); } catch { }
                });
            }
            finally
            {
                try { _transport.AbortNativeDownload(_plan); } catch { }
                _completion.TrySetException(exception);
            }
        }

        private void DetachHandlers()
        {
            try { _operation.StateChanged -= OnStateChanged; } catch { }
            try { _operation.BytesReceivedChanged -= OnBytesReceivedChanged; } catch { }
        }
    }
}
