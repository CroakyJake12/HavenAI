/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Services/WindowsNaturalSpeechOutputService.cs, in the Desktop services layer, adapting application behavior to Windows and Avalonia concerns.
 * What: This file owns WindowsNaturalSpeechOutputService, PlaybackState. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

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
    /// <summary>
    /// Stores utterance gate locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly SemaphoreSlim _utteranceGate = new(1, 1);
    /// <summary>
    /// Stores state gate locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly object _stateGate = new();
    /// <summary>
    /// Stores current locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private PlaybackState? _current;
    /// <summary>
    /// Stores disposed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
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

    /// <summary>
    /// Gets or updates unavailable reason, the bindable or domain state represented by this property.
    /// </summary>
    public string? UnavailableReason => IsAvailable
        ? null
        : OperatingSystem.IsWindows()
            ? "No modern Windows speech voices are installed. Add a Windows language speech pack."
            : "Modern Windows speech synthesis requires Windows.";

    /// <summary>
    /// Gets or updates devices, the bindable or domain state represented by this property.
    /// </summary>
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

    /// <summary>
    /// Performs speak async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs stop async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        StopCurrent();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Performs the stop current step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs dispose async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Represents playback state and keeps its related state and behavior together.
    /// </summary>
    private sealed class PlaybackState : IDisposable
    {
        /// <summary>
        /// Stores synthesizer locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        private readonly SpeechSynthesizer _synthesizer;
        /// <summary>
        /// Stores stream locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        private readonly SpeechSynthesisStream _stream;
        /// <summary>
        /// Stores source locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        private readonly MediaSource _source;
        /// <summary>
        /// Stores player locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        private readonly MediaPlayer _player;
        /// <summary>
        /// Stores playback cancellation locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        private readonly CancellationTokenSource _playbackCancellation;
        /// <summary>
        /// Stores registration locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        private readonly CancellationTokenRegistration _registration;
        /// <summary>
        /// Stores disposed locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        private int _disposed;

        /// <summary>
        /// Gets or updates completion, the bindable or domain state represented by this property.
        /// </summary>
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

        /// <summary>
        /// Reports whether cancel is true for the current state.
        /// </summary>
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

        /// <summary>
        /// Handles the ended event raised by the UI or runtime.
        /// </summary>
        private void OnEnded(MediaPlayer sender, object args) => Completion.TrySetResult();

        /// <summary>
        /// Handles the failed event raised by the UI or runtime.
        /// </summary>
        private void OnFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args) =>
            Completion.TrySetException(new InvalidOperationException(
                "Windows speech playback failed: " + args.ErrorMessage));

        /// <summary>
        /// Performs the dispose step owned by this component.
        /// </summary>
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
