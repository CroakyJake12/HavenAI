using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Services;

/// <summary>
/// Coordinates one-at-a-time voice previews through the same singleton speech output
/// service used by CallCoordinator. Preview text is fixed and no transcript is persisted.
/// </summary>
public sealed class CallVoicePreviewController(
    ISpeechOutputService speechOutput,
    IProductionDiagnostics diagnostics,
    ICallCoordinator? calls = null) : IAsyncDisposable
{
    private const string PreviewText = "Hello, this is Haven. This voice will be used for local calls.";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _previewCancellation;
    private bool _disposed;

    public async Task PreviewAsync(CallVoice? voice, string? outputDeviceId, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (calls?.IsActive == true)
            throw new InvalidOperationException("End the active Haven Call before previewing a voice.");
        if (!speechOutput.IsAvailable)
            throw new InvalidOperationException(speechOutput.UnavailableReason ?? "Speech output is unavailable.");
        if (voice is null) throw new InvalidOperationException("Choose a voice before previewing it.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        CancellationTokenSource? previewCancellation = null;
        var correlationId = Guid.NewGuid().ToString("N");
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (calls?.IsActive == true)
                throw new InvalidOperationException("End the active Haven Call before previewing a voice.");

            previewCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _previewCancellation = previewCancellation;

            await diagnostics.WriteAsync(
                ReliabilitySeverity.Information,
                "call",
                "voice-preview-started",
                "A local Call voice preview started.",
                new Dictionary<string, string>
                {
                    ["voiceCulture"] = voice.Culture ?? "unknown",
                    ["usesDefaultOutput"] = string.Equals(outputDeviceId, "default", StringComparison.OrdinalIgnoreCase).ToString()
                },
                correlationId,
                cancellationToken).ConfigureAwait(false);

            if (calls?.IsActive == true)
                throw new InvalidOperationException("End the active Haven Call before previewing a voice.");

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
            if (ReferenceEquals(_previewCancellation, previewCancellation))
                _previewCancellation = null;
            previewCancellation?.Dispose();
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var active = Interlocked.Exchange(ref _previewCancellation, null);
        if (active is null) return;

        active.Cancel();
        await speechOutput.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        var active = Interlocked.Exchange(ref _previewCancellation, null);
        if (active is not null)
        {
            active.Cancel();
            try { await speechOutput.StopAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (ObjectDisposedException) { }
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        _gate.Release();
        active?.Dispose();
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }
}
