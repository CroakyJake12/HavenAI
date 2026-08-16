using Android.Content;
using Android.OS;
using Android.Speech;
using Haven.Application;
using Haven.Core;
using OperationCanceledException = System.OperationCanceledException;

namespace Haven.Android;

/// <summary>Android Voice input backed only by Android's on-device recognizer.</summary>
public sealed class AndroidSpeechInputService : ISpeechInputService, IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _eventGate = new(1, 1);
    private SpeechRecognizer? _recognizer;
    private RecognitionListener? _listener;
    private Func<SpeechInputEvent, CancellationToken, Task>? _onEvent;
    private CancellationTokenSource? _sessionCts;
    private CallInputMode _inputMode;
    private bool _listening;
    private bool _disposed;
    private int _generation;

    public bool IsAvailable
    {
        get
        {
            if (_disposed || !OperatingSystem.IsAndroidVersionAtLeast(31)) return false;
            try { return SpeechRecognizer.IsOnDeviceRecognitionAvailable(global::Android.App.Application.Context); } catch { return false; }
        }
    }

    public string? UnavailableReason => IsAvailable ? null : _disposed ? "The Android speech input service has been disposed." : !OperatingSystem.IsAndroidVersionAtLeast(31) ? "Private on-device speech recognition requires Android 12 or newer." : "This device does not provide Android on-device speech recognition.";
    public IReadOnlyList<CallAudioDevice> Devices => IsAvailable ? [new CallAudioDevice("android-default", "Device microphone", true)] : [];

    public async Task StartAsync(SpeechInputOptions options, Func<SpeechInputEvent, CancellationToken, Task> onEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(onEvent);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsAvailable) throw new PlatformNotSupportedException(UnavailableReason);
        if (!await AndroidRuntimePermissions.EnsureRecordAudioPermissionAsync(cancellationToken).ConfigureAwait(false)) throw new UnauthorizedAccessException("Microphone permission is required for Haven Voice.");
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var activity = AndroidRuntimePermissions.CurrentActivity ?? throw new InvalidOperationException("Haven must be open before Voice can access the microphone.");
        var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        int generation;
        lock (_sync) { ObjectDisposedException.ThrowIf(_disposed, this); _onEvent = onEvent; _inputMode = options.InputMode; _sessionCts = sessionCts; generation = ++_generation; }
        try
        {
            await RunOnUiThreadAsync(activity, () =>
            {
                if (!IsCurrent(generation)) return;
                if (!OperatingSystem.IsAndroidVersionAtLeast(31)) return;
                var recognizer = SpeechRecognizer.CreateOnDeviceSpeechRecognizer(global::Android.App.Application.Context);
                var listener = new RecognitionListener(this, generation);
                recognizer.SetRecognitionListener(listener);
                lock (_sync)
                {
                    if (generation != _generation || _sessionCts is null) { recognizer.Destroy(); listener.Dispose(); return; }
                    _recognizer = recognizer; _listener = listener;
                }
            }, cancellationToken).ConfigureAwait(false);
            if (options.InputMode != CallInputMode.PushToTalk) await StartListeningAsync(generation, cancellationToken).ConfigureAwait(false);
        }
        catch { await StopAsync(CancellationToken.None).ConfigureAwait(false); throw; }
    }

    public Task BeginPushToTalkAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int generation; lock (_sync) { EnsureRunningLocked(); generation = _generation; }
        return StartListeningAsync(generation, cancellationToken);
    }

    public async Task EndPushToTalkAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SpeechRecognizer? recognizer; MainActivity? activity;
        lock (_sync) { EnsureRunningLocked(); if (!_listening) return; recognizer = _recognizer; activity = AndroidRuntimePermissions.CurrentActivity; }
        if (recognizer is not null && activity is not null) await RunOnUiThreadAsync(activity, recognizer.StopListening, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        SpeechRecognizer? recognizer; RecognitionListener? listener; CancellationTokenSource? sessionCts; MainActivity? activity;
        lock (_sync)
        {
            ++_generation; recognizer = _recognizer; listener = _listener; sessionCts = _sessionCts; activity = AndroidRuntimePermissions.CurrentActivity;
            _recognizer = null; _listener = null; _sessionCts = null; _onEvent = null; _listening = false;
        }
        sessionCts?.Cancel();
        try
        {
            if (recognizer is not null)
            {
                if (activity is not null) await RunOnUiThreadAsync(activity, () => { try { recognizer.Cancel(); } catch { } recognizer.Destroy(); }, cancellationToken).ConfigureAwait(false);
                else try { recognizer.Destroy(); } catch { }
            }
        }
        finally { listener?.Dispose(); sessionCts?.Dispose(); }
    }

    private async Task StartListeningAsync(int generation, CancellationToken cancellationToken)
    {
        SpeechRecognizer recognizer; MainActivity activity;
        lock (_sync)
        {
            if (generation != _generation || _sessionCts is null || _listening) return;
            recognizer = _recognizer ?? throw new InvalidOperationException("Android speech recognition is not ready.");
            activity = AndroidRuntimePermissions.CurrentActivity ?? throw new InvalidOperationException("Haven must be open before Voice can access the microphone.");
            _listening = true;
        }
        try
        {
            await RunOnUiThreadAsync(activity, () =>
            {
                if (!IsCurrent(generation)) return;
                using var intent = new Intent(RecognizerIntent.ActionRecognizeSpeech);
                intent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
                intent.PutExtra(RecognizerIntent.ExtraPartialResults, true);
                intent.PutExtra(RecognizerIntent.ExtraMaxResults, 1);
                intent.PutExtra(RecognizerIntent.ExtraPreferOffline, true);
                recognizer.StartListening(intent);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch { lock (_sync) { if (generation == _generation) _listening = false; } throw; }
    }

    private void OnBeginningOfSpeech(int generation) => Dispatch(generation, new SpeechInputEvent(SpeechInputEventKind.SpeechStarted));
    private void OnRmsChanged(int generation, float rmsDb) => Dispatch(generation, new SpeechInputEvent(SpeechInputEventKind.AudioLevel, AudioLevel: Math.Clamp((rmsDb + 2f) / 12f, 0f, 1f)));
    private void OnPartialResults(int generation, Bundle? results) { var text = ReadTopResult(results); if (!string.IsNullOrWhiteSpace(text)) Dispatch(generation, new SpeechInputEvent(SpeechInputEventKind.PartialTranscript, Text: text)); }
    private void OnResults(int generation, Bundle? results) { MarkRecognitionComplete(generation); var text = ReadTopResult(results); if (!string.IsNullOrWhiteSpace(text)) Dispatch(generation, new SpeechInputEvent(SpeechInputEventKind.FinalTranscript, Text: text)); Dispatch(generation, new SpeechInputEvent(SpeechInputEventKind.SpeechEnded)); ScheduleHandsFreeRestart(generation, TimeSpan.FromMilliseconds(120)); }

    private void OnError(int generation, SpeechRecognizerError error)
    {
        MarkRecognitionComplete(generation);
        switch (error)
        {
            case SpeechRecognizerError.NoMatch:
            case SpeechRecognizerError.SpeechTimeout:
                Dispatch(generation, new SpeechInputEvent(SpeechInputEventKind.SpeechEnded)); ScheduleHandsFreeRestart(generation, TimeSpan.FromMilliseconds(180)); break;
            case SpeechRecognizerError.RecognizerBusy:
                ScheduleHandsFreeRestart(generation, TimeSpan.FromMilliseconds(350)); break;
            default:
                Dispatch(generation, new SpeechInputEvent(SpeechInputEventKind.Error, Error: $"Android on-device speech recognition failed ({error}).")); break;
        }
    }

    private void MarkRecognitionComplete(int generation) { lock (_sync) { if (generation == _generation) _listening = false; } }

    private void ScheduleHandsFreeRestart(int generation, TimeSpan delay)
    {
        CancellationToken token; lock (_sync) { if (generation != _generation || _sessionCts is null || _inputMode == CallInputMode.PushToTalk) return; token = _sessionCts.Token; }
        _ = RestartAsync();
        async Task RestartAsync()
        {
            try { await Task.Delay(delay, token).ConfigureAwait(false); await StartListeningAsync(generation, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception exception) { Dispatch(generation, new SpeechInputEvent(SpeechInputEventKind.Error, Error: $"Android on-device speech recognition could not restart ({exception.Message}).")); }
        }
    }

    private void Dispatch(int generation, SpeechInputEvent inputEvent)
    {
        Func<SpeechInputEvent, CancellationToken, Task>? callback; CancellationToken token;
        lock (_sync) { if (generation != _generation || _sessionCts is null || _onEvent is null) return; callback = _onEvent; token = _sessionCts.Token; }
        _ = DispatchAsync(callback, inputEvent, token);
    }

    private async Task DispatchAsync(Func<SpeechInputEvent, CancellationToken, Task> callback, SpeechInputEvent inputEvent, CancellationToken cancellationToken)
    {
        try { await _eventGate.WaitAsync(cancellationToken).ConfigureAwait(false); try { await callback(inputEvent, cancellationToken).ConfigureAwait(false); } finally { _eventGate.Release(); } }
        catch (OperationCanceledException) { }
    }

    private bool IsCurrent(int generation) { lock (_sync) return generation == _generation && _sessionCts is not null; }
    private void EnsureRunningLocked() { if (_recognizer is null || _sessionCts is null) throw new InvalidOperationException("Start Haven Voice before controlling the microphone."); }
    private static string? ReadTopResult(Bundle? results) => results?.GetStringArrayList(SpeechRecognizer.ResultsRecognition)?.FirstOrDefault();

    private static Task RunOnUiThreadAsync(MainActivity activity, Action action, CancellationToken cancellationToken)
    {
        if (Looper.MyLooper() == Looper.MainLooper) { cancellationToken.ThrowIfCancellationRequested(); action(); return Task.CompletedTask; }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        activity.RunOnUiThread(() => { try { if (cancellationToken.IsCancellationRequested) completion.TrySetCanceled(cancellationToken); else { action(); completion.TrySetResult(); } } catch (Exception exception) { completion.TrySetException(exception); } });
        return completion.Task.WaitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync() { if (_disposed) return; _disposed = true; await StopAsync(CancellationToken.None).ConfigureAwait(false); _eventGate.Dispose(); }

    private sealed class RecognitionListener(AndroidSpeechInputService owner, int generation) : Java.Lang.Object, IRecognitionListener
    {
        public void OnReadyForSpeech(Bundle? @params) { }
        public void OnBeginningOfSpeech() => owner.OnBeginningOfSpeech(generation);
        public void OnRmsChanged(float rmsdB) => owner.OnRmsChanged(generation, rmsdB);
        public void OnBufferReceived(byte[]? buffer) { }
        public void OnEndOfSpeech() { }
        public void OnError(SpeechRecognizerError error) => owner.OnError(generation, error);
        public void OnResults(Bundle? results) => owner.OnResults(generation, results);
        public void OnPartialResults(Bundle? partialResults) => owner.OnPartialResults(generation, partialResults);
        public void OnEvent(int eventType, Bundle? @params) { }
        public void OnSegmentResults(Bundle segmentResults) { }
        public void OnEndOfSegmentedSession() { }
        public void OnLanguageDetection(Bundle results) { }
    }
}
