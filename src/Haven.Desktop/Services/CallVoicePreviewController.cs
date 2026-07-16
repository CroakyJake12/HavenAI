using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Services;

/// <summary>
/// Coordinates one-at-a-time voice previews through the same singleton speech output
/// service used by CallCoordinator. Preview text is fixed and no transcript is persisted.
/// </summary>
public sealed class CallVoicePreviewController(
    ISpeechOutputService speechOutput,
    IProductionDiagnostics diagnostics) : IAsyncDisposable
{
    private const string PreviewText = "Hello, this is Haven. This voice will be used for local calls.";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _previewCancellation;
    private bool _disposed;

    public async Task PreviewAsync(CallVoice? voice, string? outputDeviceId, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!speechOutput.IsAvailable)
            throw new InvalidOperationException(speechOutput.UnavailableReason ?? "Speech output is unavailable.");
        if (voice is null) throw new InvalidOperationException("Choose a voice before previewing it.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        CancellationTokenSource? previewCancellation = null;
        var correlationId = Guid.NewGuid().ToString("N");
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await StopCoreAsync().ConfigureAwait(false);
            previewCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _previewCancellation = previewCancellation;

            await diagnostics.WriteAsync(
                ReliabilitySeverity.Information,
                "call",
                "voice-preview-started",
                "A local Call voice preview started.",
                new Dictionary<string, string>
                {
                    ["voiceLanguage"] = voice.Language,
                    ["usesDefaultOutput"] = string.Equals(outputDeviceId, "default", StringComparison.OrdinalIgnoreCase).ToString()
                },
                correlationId,
                cancellationToken).ConfigureAwait(false);

            await speechOutput.SpeakAsync(
                PreviewText,
                voice.Id,
                outputDeviceId,
                previewCancellation.Token).ConfigureAwait(false);

            await diagnostics.WriteAsync(
                ReliabilitySeverity.Information,
                "call",
                "voice-preview-completed",
                "The local Call voice preview completed.",
                correlationId: correlationId,
                cancellationToken: CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (previewCancellation?.IsCancellationRequested == true)
        {
            await diagnostics.WriteAsync(
                ReliabilitySeverity.Trace,
                "call",
                "voice-preview-cancelled",
                "The local Call voice preview was cancelled.",
                correlationId: correlationId,
                cancellationToken: CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await diagnostics.WriteAsync(
                ReliabilitySeverity.Warning,
                "call",
                "voice-preview-failed",
                "The local Call voice preview failed.",
                new Dictionary<string, string>
                {
                    ["exceptionType"] = ex.GetType().FullName ?? ex.GetType().Name,
                    ["hResult"] = ex.HResult.ToString(System.Globalization.CultureInfo.InvariantCulture)
                },
                correlationId,
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            if (ReferenceEquals(_previewCancellation, previewCancellation)) _previewCancellation = null;
            previewCancellation?.Dispose();
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        _previewCancellation?.Cancel();
        await speechOutput.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task StopCoreAsync()
    {
        _previewCancellation?.Cancel();
        await speechOutput.StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _previewCancellation?.Cancel();
        try { await speechOutput.StopAsync(CancellationToken.None).ConfigureAwait(false); }
        catch (ObjectDisposedException) { }
        await _gate.WaitAsync().ConfigureAwait(false);
        _gate.Release();
        _previewCancellation?.Dispose();
        _previewCancellation = null;
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }
}
