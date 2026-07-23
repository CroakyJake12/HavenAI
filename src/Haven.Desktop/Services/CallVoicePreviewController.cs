/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Services/CallVoicePreviewController.cs, in the Desktop services layer, adapting application behavior to Windows and Avalonia concerns.
 * What: This file owns CallVoicePreviewController. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

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
    /// <summary>
    /// Stores preview text locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const string PreviewText = "Hello, this is Haven. This voice will be used for local calls.";
    /// <summary>
    /// Stores gate locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);
    /// <summary>
    /// Stores preview cancellation locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private CancellationTokenSource? _previewCancellation;
    /// <summary>
    /// Stores disposed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Performs preview asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task PreviewAsync(CallVoice? voice, string? outputDeviceId, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (calls?.IsActive == true)
            throw new InvalidOperationException("End the active Haven Voice before previewing a voice.");
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
                throw new InvalidOperationException("End the active Haven Voice before previewing a voice.");

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
                throw new InvalidOperationException("End the active Haven Voice before previewing a voice.");

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

    /// <summary>
    /// Performs stop asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var active = Interlocked.Exchange(ref _previewCancellation, null);
        if (active is null) return;

        active.Cancel();
        await speechOutput.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs dispose asynchronously so I/O does not block the caller's thread.
    /// </summary>
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
