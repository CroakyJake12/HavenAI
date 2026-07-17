using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Services;

public sealed record NotesDictationStatus(
    bool IsActive,
    string Message,
    bool IsError = false);

/// <summary>
/// Reuses Haven's local Whisper microphone boundary for one Notes utterance.
/// Raw audio remains inside ISpeechInputService and is discarded there; this
/// controller receives only derived levels and final transcript text.
/// </summary>
public sealed class NotesDictationController(
    ISpeechInputService speechInput,
    ISpeechModelManager speechModels,
    ICallCoordinator calls,
    IProductionDiagnostics diagnostics) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _activeCancellation;
    private CancellationTokenRegistration _timeoutRegistration;
    private int _active;
    private bool _disposed;

    public bool IsActive => Volatile.Read(ref _active) == 1;
    public event EventHandler<NotesDictationStatus>? StatusChanged;

    public async Task StartOneUtteranceAsync(
        Func<string, CancellationToken, Task> applyFinalTranscript,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(applyFinalTranscript);
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsActive) throw new InvalidOperationException("Notes dictation is already listening.");
            if (RuntimeSafetyState.IsSafeMode)
                throw new InvalidOperationException("Notes dictation is disabled while Haven recovery safe mode is active.");
            if (calls.IsActive)
                throw new InvalidOperationException("End the active Haven Call before starting Notes dictation.");
            if (!speechInput.IsAvailable)
                throw new InvalidOperationException(speechInput.UnavailableReason ?? "Local microphone transcription is unavailable.");

            var models = await speechModels.GetModelsAsync(cancellationToken).ConfigureAwait(false);
            var model = models.FirstOrDefault(value => value.IsInstalled && value.Size == SpeechModelSize.Base)
                        ?? models.FirstOrDefault(value => value.IsInstalled);
            if (model is null)
                throw new InvalidOperationException("Download a local Whisper speech model in Call settings before using Notes dictation.");

            var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(TimeSpan.FromSeconds(60));
            _activeCancellation = linked;
            _timeoutRegistration = linked.Token.Register(() =>
            {
                if (IsActive)
                {
                    RaiseStatus("Notes dictation stopped after one minute without a final passage.", isError: true);
                    _ = StopAsync(CancellationToken.None);
                }
            });
            Volatile.Write(ref _active, 1);
            RaiseStatus("Listening locally for one passage…");
            await speechInput.StartAsync(
                new SpeechInputOptions(
                    speechInput.Devices.FirstOrDefault(value => value.IsDefault)?.Id
                    ?? speechInput.Devices.FirstOrDefault()?.Id,
                    model,
                    CallInputMode.HandsFree),
                async (input, eventCancellation) =>
                {
                    switch (input.Kind)
                    {
                        case SpeechInputEventKind.SpeechStarted:
                            RaiseStatus("Speech detected. Keep speaking, then pause.");
                            break;
                        case SpeechInputEventKind.PartialTranscript when !string.IsNullOrWhiteSpace(input.Text):
                            RaiseStatus("Transcribing locally: " + Truncate(input.Text, 120));
                            break;
                        case SpeechInputEventKind.FinalTranscript when !string.IsNullOrWhiteSpace(input.Text):
                            try
                            {
                                await applyFinalTranscript(input.Text.Trim(), eventCancellation).ConfigureAwait(false);
                                await diagnostics.WriteAsync(
                                    ReliabilitySeverity.Information,
                                    "notes",
                                    "dictation-applied",
                                    "A local final speech transcript was applied to a reviewed Notes text block.",
                                    new Dictionary<string, string>
                                    {
                                        ["characters"] = input.Text.Trim().Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                        ["rawAudioPersisted"] = bool.FalseString
                                    },
                                    cancellationToken: CancellationToken.None).ConfigureAwait(false);
                                RaiseStatus("Dictation inserted. Raw microphone audio was discarded.");
                            }
                            finally
                            {
                                _ = StopAsync(CancellationToken.None);
                            }
                            break;
                        case SpeechInputEventKind.Error:
                            RaiseStatus(input.Error ?? "Local speech transcription failed.", isError: true);
                            _ = StopAsync(CancellationToken.None);
                            break;
                        case SpeechInputEventKind.SourceClosed:
                            RaiseStatus("The microphone source closed.", isError: true);
                            _ = StopAsync(CancellationToken.None);
                            break;
                    }
                },
                linked.Token).ConfigureAwait(false);
        }
        catch
        {
            await StopCoreAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
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
            await StopCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        var cancellation = Interlocked.Exchange(ref _activeCancellation, null);
        _timeoutRegistration.Dispose();
        _timeoutRegistration = default;
        Volatile.Write(ref _active, 0);
        try
        {
            await speechInput.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            cancellation?.Cancel();
            cancellation?.Dispose();
        }
    }

    private void RaiseStatus(string message, bool isError = false) =>
        StatusChanged?.Invoke(this, new NotesDictationStatus(IsActive, message, isError));

    private static string Truncate(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum] + "…";

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }
}
