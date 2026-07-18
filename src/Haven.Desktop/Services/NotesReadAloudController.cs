/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Services/NotesReadAloudController.cs, in the Desktop services layer, adapting application behavior to Windows and Avalonia concerns.
 * What: This file owns NotesReadAloudStatus, NotesReadAloudController. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Services;

/// <summary>
/// Represents notes read aloud status and keeps its related state and behavior together.
/// </summary>
public sealed record NotesReadAloudStatus(
    bool IsActive,
    string Message,
    bool IsError = false);

/// <summary>
/// Reads one selected Notes block through the same local Windows speech-output
/// singleton used by Call. It never sends document content to a model or network.
/// </summary>
public sealed class NotesReadAloudController(
    ISpeechOutputService speech,
    ICallCoordinator calls,
    IProductionDiagnostics diagnostics) : IAsyncDisposable
{
    /// <summary>
    /// Stores gate locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);
    /// <summary>
    /// Stores active cancellation locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private CancellationTokenSource? _activeCancellation;
    /// <summary>
    /// Stores active locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int _active;
    /// <summary>
    /// Stores disposed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Reports whether active applies to the current state.
    /// </summary>
    public bool IsActive => Volatile.Read(ref _active) == 1;
    /// <summary>
    /// Stores status changed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event EventHandler<NotesReadAloudStatus>? StatusChanged;

    /// <summary>
    /// Performs read asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task ReadAsync(
        string text,
        string? language,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("The selected Notes block has no readable text.", nameof(text));

        CancellationTokenSource linked;
        CallVoice? voice;
        string? outputDeviceId;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (RuntimeSafetyState.IsSafeMode)
                throw new InvalidOperationException("Notes read aloud is disabled while Haven recovery safe mode is active.");
            if (calls.IsActive)
                throw new InvalidOperationException("End the active Haven Call before starting Notes read aloud.");
            if (!speech.IsAvailable)
                throw new InvalidOperationException(speech.UnavailableReason ?? "Local speech output is unavailable.");
            if (IsActive)
                throw new InvalidOperationException("Notes read aloud is already active. Stop it before starting another passage.");

            voice = SelectVoice(speech.Voices, language);
            var output = speech.Devices.FirstOrDefault(value => value.IsDefault)
                         ?? speech.Devices.FirstOrDefault();
            outputDeviceId = output?.Id;
            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeCancellation = linked;
            Volatile.Write(ref _active, 1);
            RaiseStatus("Reading the selected block aloud locally…");
        }
        finally
        {
            _gate.Release();
        }

        try
        {
            await speech.SpeakAsync(
                text.Trim(),
                voice?.Id,
                outputDeviceId,
                linked.Token).ConfigureAwait(false);
            if (!linked.IsCancellationRequested)
            {
                await diagnostics.WriteAsync(
                    ReliabilitySeverity.Information,
                    "notes",
                    "read-aloud-completed",
                    "A selected Notes block was read aloud through local speech synthesis.",
                    new Dictionary<string, string>
                    {
                        ["characters"] = text.Trim().Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["voice"] = voice?.Name ?? "system default",
                        ["networkContentSent"] = bool.FalseString
                    },
                    cancellationToken: CancellationToken.None).ConfigureAwait(false);
                RaiseStatus("Read aloud completed locally.");
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            // StopAsync or the caller intentionally interrupted this local playback.
        }
        finally
        {
            await CompleteReadAsync(linked).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Performs stop asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cancellation = Interlocked.Exchange(ref _activeCancellation, null);
            var wasActive = Interlocked.Exchange(ref _active, 0) == 1;
            cancellation?.Cancel();
            try
            {
                // ISpeechOutputService is shared with Call. Never interrupt it unless
                // this controller actually owns an active Notes utterance.
                if (wasActive)
                    await speech.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                cancellation?.Dispose();
                if (wasActive) RaiseStatus("Read aloud stopped.");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Performs complete read asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task CompleteReadAsync(CancellationTokenSource owner)
    {
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(_activeCancellation, owner)) return;
            _activeCancellation = null;
            Volatile.Write(ref _active, 0);
            owner.Dispose();
            RaiseStatus("Read aloud is idle.");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Performs the select voice step owned by this component.
    /// </summary>
    private static CallVoice? SelectVoice(
        IReadOnlyList<CallVoice> voices,
        string? language)
    {
        if (voices.Count == 0) return null;
        if (!string.IsNullOrWhiteSpace(language))
        {
            var exact = voices.FirstOrDefault(value =>
                string.Equals(value.Culture, language, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact;
            var prefix = language.Split('-', StringSplitOptions.RemoveEmptyEntries)[0];
            var matching = voices.FirstOrDefault(value =>
                value.Culture?.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase) == true
                || string.Equals(value.Culture, prefix, StringComparison.OrdinalIgnoreCase));
            if (matching is not null) return matching;
        }
        return voices.FirstOrDefault(value => value.IsDefault) ?? voices[0];
    }

    /// <summary>
    /// Performs the raise status step owned by this component.
    /// </summary>
    private void RaiseStatus(string message, bool isError = false) =>
        StatusChanged?.Invoke(this, new NotesReadAloudStatus(IsActive, message, isError));

    /// <summary>
    /// Performs dispose asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }
}
