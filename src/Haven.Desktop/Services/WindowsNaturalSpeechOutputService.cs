using Haven.Application;
using Haven.Core;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace Haven.Desktop.Services;

/// <summary>
/// Desktop-only speech output using the modern Windows speech synthesis voice bank.
/// It replaces legacy System.Speech at the outer host while preserving the existing
/// Call coordinator contract and local-only media flow.
/// </summary>
public sealed class WindowsNaturalSpeechOutputService : ISpeechOutputService, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MediaPlayer? _player;
    private SpeechSynthesizer? _synthesizer;
    private CancellationTokenSource? _playbackCancellation;
    private bool _disposed;

    public bool IsAvailable
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            try { return SpeechSynthesizer.AllVoices.Count > 0; }
            catch (Exception) { return false; }
        }
    }

    public string? UnavailableReason => IsAvailable
        ? null
        : OperatingSystem.IsWindows()
            ? "No modern Windows speech voices are installed. Add a Windows language speech pack."
            : "Modern Windows speech synthesis requires Windows.";

    public IReadOnlyList<CallAudioDevice> Devices { get; } =
        [new CallAudioDevice("default", "Windows default output", true)];

    public IReadOnlyList<CallVoice> Voices
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return [];
            try
            {
                var defaultId = SpeechSynthesizer.DefaultVoice?.Id;
                return SpeechSynthesizer.AllVoices
                    .OrderByDescending(voice => string.Equals(voice.Id, defaultId, StringComparison.OrdinalIgnoreCase))
                    .ThenBy(voice => voice.Language, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(voice => voice.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Select(voice => new CallVoice(
                        voice.Id,
                        voice.DisplayName,
                        voice.Language,
                        string.Equals(voice.Id, defaultId, StringComparison.OrdinalIgnoreCase)))
                    .ToArray();
            }
            catch (Exception)
            {
                return [];
            }
        }
    }

    public async Task SpeakAsync(
        string text,
        string? voiceName,
        string? outputDeviceId,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsAvailable) throw new InvalidOperationException(UnavailableReason);
        if (string.IsNullOrWhiteSpace(text)) return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var synthesizer = new SpeechSynthesizer();
            var selected = SpeechSynthesizer.AllVoices.FirstOrDefault(voice =>
                string.Equals(voice.Id, voiceName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(voice.DisplayName, voiceName, StringComparison.OrdinalIgnoreCase));
            if (selected is not null) synthesizer.Voice = selected;

            var stream = await synthesizer.SynthesizeTextToStreamAsync(text);
            cancellationToken.ThrowIfCancellationRequested();
            var source = MediaSource.CreateFromStream(stream, stream.ContentType);
            var player = new MediaPlayer
            {
                AutoPlay = false,
                Source = source
            };
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnEnded(MediaPlayer sender, object args) => completion.TrySetResult();
            void OnFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args) =>
                completion.TrySetException(new InvalidOperationException("Windows speech playback failed: " + args.ErrorMessage));
            player.MediaEnded += OnEnded;
            player.MediaFailed += OnFailed;

            var playbackCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _synthesizer = synthesizer;
            _player = player;
            _playbackCancellation = playbackCancellation;
            using var registration = playbackCancellation.Token.Register(() => completion.TrySetCanceled(playbackCancellation.Token));
            try
            {
                player.Play();
                await completion.Task.ConfigureAwait(false);
            }
            finally
            {
                player.MediaEnded -= OnEnded;
                player.MediaFailed -= OnFailed;
                await StopCoreAsync().ConfigureAwait(false);
                source.Dispose();
                stream.Dispose();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await StopCoreAsync().ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    private Task StopCoreAsync()
    {
        var cancellation = _playbackCancellation;
        var player = _player;
        var synthesizer = _synthesizer;
        _playbackCancellation = null;
        _player = null;
        _synthesizer = null;
        try { cancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
        try
        {
            if (player is not null)
            {
                player.Pause();
                player.Source = null;
                player.Dispose();
            }
        }
        finally
        {
            synthesizer?.Dispose();
            cancellation?.Dispose();
        }
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            await StopCoreAsync().ConfigureAwait(false);
            _disposed = true;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}
