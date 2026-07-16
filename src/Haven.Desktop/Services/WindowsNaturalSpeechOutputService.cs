using Haven.Application;
using Haven.Core;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace Haven.Desktop.Services;

/// <summary>
/// Desktop-only speech output using the modern Windows speech synthesis voice bank.
/// Playback is process-local and interruptible without waiting behind the active utterance.
/// </summary>
public sealed class WindowsNaturalSpeechOutputService : ISpeechOutputService, IAsyncDisposable
{
    private readonly SemaphoreSlim _utteranceGate = new(1, 1);
    private readonly object _stateGate = new();
    private PlaybackState? _current;
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
        if (!string.IsNullOrWhiteSpace(outputDeviceId)
            && !string.Equals(outputDeviceId, "default", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The modern Windows speech service currently supports only the default output device.", nameof(outputDeviceId));
        }

        await _utteranceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        PlaybackState? state = null;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            StopCurrent();
            cancellationToken.ThrowIfCancellationRequested();

            var synthesizer = new SpeechSynthesizer();
            var selected = SpeechSynthesizer.AllVoices.FirstOrDefault(voice =>
                string.Equals(voice.Id, voiceName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(voice.DisplayName, voiceName, StringComparison.OrdinalIgnoreCase));
            if (selected is not null) synthesizer.Voice = selected;

            SpeechSynthesisStream? stream = null;
            MediaSource? source = null;
            MediaPlayer? player = null;
            try
            {
                stream = await synthesizer.SynthesizeTextToStreamAsync(text);
                cancellationToken.ThrowIfCancellationRequested();
                source = MediaSource.CreateFromStream(stream, stream.ContentType);
                player = new MediaPlayer
                {
                    AutoPlay = false,
                    Source = source
                };

                state = new PlaybackState(synthesizer, stream, source, player, cancellationToken);
                lock (_stateGate)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    _current = state;
                }

                player.Play();
                await state.Completion.Task.ConfigureAwait(false);
            }
            catch
            {
                if (state is null)
                {
                    player?.Dispose();
                    source?.Dispose();
                    stream?.Dispose();
                    synthesizer.Dispose();
                }
                throw;
            }
        }
        finally
        {
            if (state is not null)
            {
                lock (_stateGate)
                {
                    if (ReferenceEquals(_current, state)) _current = null;
                }
                state.Dispose();
            }
            _utteranceGate.Release();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        StopCurrent();
        return Task.CompletedTask;
    }

    private void StopCurrent()
    {
        PlaybackState? state;
        lock (_stateGate)
        {
            state = _current;
            _current = null;
        }
        state?.Cancel();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        StopCurrent();
        await _utteranceGate.WaitAsync().ConfigureAwait(false);
        _utteranceGate.Release();
        _utteranceGate.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class PlaybackState : IDisposable
    {
        private readonly SpeechSynthesizer _synthesizer;
        private readonly SpeechSynthesisStream _stream;
        private readonly MediaSource _source;
        private readonly MediaPlayer _player;
        private readonly CancellationTokenSource _playbackCancellation;
        private readonly CancellationTokenRegistration _registration;
        private int _disposed;

        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PlaybackState(
            SpeechSynthesizer synthesizer,
            SpeechSynthesisStream stream,
            MediaSource source,
            MediaPlayer player,
            CancellationToken cancellationToken)
        {
            _synthesizer = synthesizer;
            _stream = stream;
            _source = source;
            _player = player;
            _playbackCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _registration = _playbackCancellation.Token.Register(
                () => Completion.TrySetCanceled(_playbackCancellation.Token));
            _player.MediaEnded += OnEnded;
            _player.MediaFailed += OnFailed;
        }

        public void Cancel()
        {
            try { _playbackCancellation.Cancel(); }
            catch (ObjectDisposedException) { }
            try
            {
                _player.Pause();
                _player.Source = null;
            }
            catch (ObjectDisposedException) { }
        }

        private void OnEnded(MediaPlayer sender, object args) => Completion.TrySetResult();

        private void OnFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args) =>
            Completion.TrySetException(new InvalidOperationException(
                "Windows speech playback failed: " + args.ErrorMessage));

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _player.MediaEnded -= OnEnded;
            _player.MediaFailed -= OnFailed;
            _registration.Dispose();
            try
            {
                _player.Pause();
                _player.Source = null;
            }
            catch (ObjectDisposedException) { }
            _player.Dispose();
            _source.Dispose();
            _stream.Dispose();
            _synthesizer.Dispose();
            _playbackCancellation.Dispose();
        }
    }
}
