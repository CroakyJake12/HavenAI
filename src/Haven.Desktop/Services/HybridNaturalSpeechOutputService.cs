/*
 * FILE DOCUMENTATION
 * Where: Desktop speech services, between CallCoordinator and the concrete audio engines.
 * What: Exposes curated Kokoro neural voices alongside the installed Windows voicebank.
 * How: Neural voices use the local Kokoro 82M int8 model; Windows voices remain a no-download fallback.
 * Why: The Windows synthesizer is reliable but noticeably robotic. Kokoro provides conversational
 *      prosody locally without requiring a paid cloud API or transmitting call text.
 * Maintenance: Voice ids beginning `kokoro:` are part of saved Call metadata and must remain stable.
 */

using Haven.Application;
using Haven.Core;
using KokoroSharp;
using KokoroSharp.Processing;

namespace Haven.Desktop.Services;

/// <summary>
/// Routes curated neural voices to Kokoro and all installed system voices to the
/// modern Windows synthesizer. The neural model is loaded lazily so ordinary app
/// startup remains fast and users who never enable spoken responses pay no cost.
/// </summary>
public sealed class HybridNaturalSpeechOutputService(
    WindowsNaturalSpeechOutputService windows) : ISpeechOutputService, IAsyncDisposable
{
    private const string NeuralPrefix = "kokoro:";
    private readonly SemaphoreSlim _speechGate = new(1, 1);
    private readonly object _stateGate = new();
    private KokoroTTS? _neural;
    private bool _disposed;

    private static readonly CallVoice[] NeuralVoices =
    [
        new("kokoro:af_heart", "Heart · Haven Neural", "en-US", true),
        new("kokoro:af_nova", "Nova · Haven Neural", "en-US"),
        new("kokoro:af_sky", "Sky · Haven Neural", "en-US"),
        new("kokoro:af_bella", "Bella · Haven Neural", "en-US"),
        new("kokoro:am_michael", "Michael · Haven Neural", "en-US"),
        new("kokoro:am_adam", "Adam · Haven Neural", "en-US"),
        new("kokoro:bf_emma", "Emma · Haven Neural", "en-GB"),
        new("kokoro:bm_george", "George · Haven Neural", "en-GB")
    ];

    public bool IsAvailable => true;
    public string? UnavailableReason => null;
    public IReadOnlyList<CallAudioDevice> Devices => windows.Devices;
    public IReadOnlyList<CallVoice> Voices =>
        NeuralVoices.Concat(windows.Voices.Select(voice => voice with
        {
            Name = voice.Name + " · Windows",
            IsDefault = false
        })).ToArray();

    /// <summary>Reports whether the compact neural model is already cached locally.</summary>
    public bool IsNeuralModelReady
    {
        get
        {
            try { return KokoroTTS.IsDownloaded(KModel.int8); }
            catch (Exception) { return false; }
        }
    }

    public async Task SpeakAsync(string text, string? voiceName, string? outputDeviceId, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(text)) return;

        // Serialize every output engine, not only Kokoro. This prevents a short
        // acknowledgement, voice preview or streamed reply from speaking over the
        // next chunk and makes interruption behavior predictable across engines.
        await _speechGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (voiceName?.StartsWith(NeuralPrefix, StringComparison.OrdinalIgnoreCase) != true)
            {
                await windows.SpeakAsync(text, voiceName, outputDeviceId, cancellationToken).ConfigureAwait(false);
                return;
            }

            var engine = await GetNeuralEngineAsync(cancellationToken).ConfigureAwait(false);
            var id = voiceName[NeuralPrefix.Length..];
            var voice = KokoroVoiceManager.GetVoice(id);
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var handle = engine.SpeakFast(text.Trim(), voice, new KokoroTTSPipelineConfig
            {
                // Each curated voice gets a subtle pace profile. Short cues and
                // questions slow down slightly so they sound intentional rather
                // than clipped, while ordinary reply chunks remain brisk.
                Speed = ResolveSpeed(id, text),
                PreprocessText = true
            });
            handle.OnSpeechCompleted += _ => completion.TrySetResult();
            handle.OnSpeechCanceled += _ => completion.TrySetCanceled(cancellationToken);
            using var registration = cancellationToken.Register(() =>
            {
                lock (_stateGate) engine.StopPlayback();
                completion.TrySetCanceled(cancellationToken);
            });
            await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _speechGate.Release();
        }
    }

    /// <summary>
    /// Applies small per-voice and per-utterance pacing changes. Kokoro derives most
    /// expression from punctuation and wording; this keeps that delivery varied
    /// without introducing cloud-only style controls or unstable voice identifiers.
    /// </summary>
    private static float ResolveSpeed(string voiceId, string text)
    {
        var speed = voiceId switch
        {
            "af_nova" => 1.04f,
            "af_sky" => 1.03f,
            "af_bella" => 1.01f,
            "am_michael" => 1.02f,
            "am_adam" => 1.03f,
            "bf_emma" => 0.99f,
            "bm_george" => 0.98f,
            _ => 1.01f
        };

        var value = text.Trim();
        if (value.Length <= 28) speed -= 0.04f;
        if (value.EndsWith('?')) speed -= 0.02f;
        if (value.EndsWith('!')) speed += 0.01f;
        return Math.Clamp(speed, 0.92f, 1.08f);
    }

    private async Task<KokoroTTS> GetNeuralEngineAsync(CancellationToken cancellationToken)
    {
        lock (_stateGate)
        {
            if (_neural is not null) return _neural;
        }

        // LoadModelAsync downloads the compact model once when necessary, then uses
        // the local cache for every later preview and call.
        var loaded = await KokoroTTS.LoadModelAsync(KModel.int8, null, null)
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        loaded.NicifyAudio = true;
        lock (_stateGate)
        {
            if (_disposed)
            {
                loaded.Dispose();
                throw new ObjectDisposedException(nameof(HybridNaturalSpeechOutputService));
            }
            _neural ??= loaded;
            if (!ReferenceEquals(_neural, loaded)) loaded.Dispose();
            return _neural;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_stateGate) _neural?.StopPlayback();
        await windows.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_stateGate) _neural?.StopPlayback();
        await _speechGate.WaitAsync().ConfigureAwait(false);
        lock (_stateGate)
        {
            _neural?.Dispose();
            _neural = null;
        }
        _speechGate.Release();
        _speechGate.Dispose();
    }
}
