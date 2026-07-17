using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Services;

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
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _activeCancellation;
    private int _active;
    private bool _disposed;

    public bool IsActive => Volatile.Read(ref _active) == 1;
    public event EventHandler<NotesReadAloudStatus>? StatusChanged;

    public async Task ReadAsync(
        string text,
        string? language,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("The selected Notes block has no readable text.", nameof(text));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (RuntimeSafetyState.IsSafeMode)
                throw new InvalidOperationException("Notes read aloud is disabled while Haven recovery safe mode is active.");
            if (calls.IsActive)
                throw new InvalidOperationException("End the active Haven Call before starting Notes read aloud.");
            if (!speech.IsAvailable)
                throw new InvalidOperationException(speech.UnavailableReason ?? "Local speech output is unavailable.");
            await StopCoreAsync(CancellationToken.None, publishStatus: false).ConfigureAwait(false);
            var voice = SelectVoice(speech.Voices, language);
            var output = speech.OutputDevices.FirstOrDefault(value => value.IsDefault)
                         ?? speech.OutputDevices.FirstOrDefault();
            var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeCancellation = linked;
            Volatile.Write(ref _active, 1);
            RaiseStatus("Reading the selected block aloud locally…");
            try
            {
                await speech.SpeakAsync(
                    text.Trim(),
                    voice,
                    output?.Id,
                    linked.Token).ConfigureAwait(false);
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
            finally
            {
                var cancellation = Interlocked.Exchange(ref _activeCancellation, null);
                cancellation?.Dispose();
                Volatile.Write(ref _active, 0);
                RaiseStatus("Read aloud is idle.");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync(cancellationToken, publishStatus: true).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken, bool publishStatus)
    {
        var cancellation = Interlocked.Exchange(ref _activeCancellation, null);
        var wasActive = Interlocked.Exchange(ref _active, 0) == 1;
        try
        {
            cancellation?.Cancel();
            await speech.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            cancellation?.Dispose();
            if (publishStatus && wasActive) RaiseStatus("Read aloud stopped.");
        }
    }

    private static CallVoice? SelectVoice(
        IReadOnlyList<CallVoice> voices,
        string? language)
    {
        if (voices.Count == 0) return null;
        if (!string.IsNullOrWhiteSpace(language))
        {
            var exact = voices.FirstOrDefault(value =>
                value.Culture.Equals(language, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact;
            var prefix = language.Split('-', StringSplitOptions.RemoveEmptyEntries)[0];
            var matching = voices.FirstOrDefault(value =>
                value.Culture.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase)
                || value.Culture.Equals(prefix, StringComparison.OrdinalIgnoreCase));
            if (matching is not null) return matching;
        }
        return voices.FirstOrDefault(value => value.IsDefault) ?? voices[0];
    }

    private void RaiseStatus(string message, bool isError = false) =>
        StatusChanged?.Invoke(this, new NotesReadAloudStatus(IsActive, message, isError));

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }
}
