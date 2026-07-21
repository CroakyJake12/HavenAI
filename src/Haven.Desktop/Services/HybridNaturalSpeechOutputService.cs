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
using KokoroSharp.Core;
using KokoroSharp.Processing;

namespace Haven.Desktop.Services;

/// <summary>
/// Routes curated neural voices to Kokoro and all installed system voices to the
/// modern Windows synthesizer. Cached neural models are prepared in the background
/// so the first spoken answer does not pay model-loading cost.
/// </summary>
public sealed class HybridNaturalSpeechOutputService : ISpeechOutputService, ISpeechOutputWarmup, IAsyncDisposable
{
    private const string NeuralPrefix = "kokoro:";
    private readonly WindowsNaturalSpeechOutputService _windows;
    private readonly SemaphoreSlim _speechGate = new(1, 1);
    private readonly object _stateGate = new();
    private readonly Dictionary<string, KokoroVoice> _voiceProfiles = new(StringComparer.OrdinalIgnoreCase);
    private Task<KokoroTTS>? _neuralLoadTask;
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

    public HybridNaturalSpeechOutputService(WindowsNaturalSpeechOutputService windows)
    {
        _windows = windows;
        if (IsNeuralModelReady) _ = WarmCachedModelSafelyAsync();
    }

    public bool IsAvailable => true;
    public string? UnavailableReason => null;
    public IReadOnlyList<CallAudioDevice> Devices => _windows.Devices;
    public IReadOnlyList<CallVoice> Voices =>
        NeuralVoices.Concat(_windows.Voices.Select(voice => voice with
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

    public async Task WarmAsync(string? voiceName, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (voiceName?.StartsWith(NeuralPrefix, StringComparison.OrdinalIgnoreCase) != true) return;
        _ = await GetNeuralEngineAsync(cancellationToken).ConfigureAwait(false);
        _ = ResolveVoiceProfile(voiceName[NeuralPrefix.Length..]);
    }

    public async Task SpeakAsync(string text, string? voiceName, string? outputDeviceId, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(text)) return;

        await _speechGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (voiceName?.StartsWith(NeuralPrefix, StringComparison.OrdinalIgnoreCase) != true)
            {
                await _windows.SpeakAsync(text, voiceName, outputDeviceId, cancellationToken).ConfigureAwait(false);
                return;
            }

            var engine = await GetNeuralEngineAsync(cancellationToken).ConfigureAwait(false);
            var id = voiceName[NeuralPrefix.Length..];
            var voice = ResolveVoiceProfile(id);
            var spokenText = PrepareForSpeech(text);
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            // CallCoordinator already supplies phrase-sized chunks. Using the
            // unsegmented path here avoids KokoroSharp's documented quality loss
            // from segmenting the same short phrase a second time.
            var handle = engine.Speak(spokenText, voice, new KokoroTTSPipelineConfig
            {
                Speed = ResolveSpeed(id, spokenText),
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
    /// Creates slightly blended same-accent profiles. Small blends soften the flat
    /// single-speaker delivery while keeping pronunciation and identity stable.
    /// </summary>
    private KokoroVoice ResolveVoiceProfile(string voiceId)
    {
        lock (_stateGate)
        {
            if (_voiceProfiles.TryGetValue(voiceId, out var cached)) return cached;

            var primary = KokoroVoiceManager.GetVoice(voiceId);
            var companionId = voiceId switch
            {
                "af_heart" or "af_nova" or "af_bella" => "af_sarah",
                "af_sky" => "af_nicole",
                "am_michael" => "am_adam",
                "am_adam" => "am_michael",
                "bf_emma" => "bf_isabella",
                "bm_george" => "bm_lewis",
                _ => voiceId
            };
            if (string.Equals(companionId, voiceId, StringComparison.OrdinalIgnoreCase))
                return _voiceProfiles[voiceId] = primary;

            var companion = KokoroVoiceManager.GetVoice(companionId);
            return _voiceProfiles[voiceId] = KokoroVoiceManager.Mix([(primary, 9), (companion, 1)]);
        }
    }

    private static string PrepareForSpeech(string text)
    {
        var value = text.Trim()
            .Replace(" **", " ", StringComparison.Ordinal)
            .Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("__", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal)
            .Replace(" — ", ", ", StringComparison.Ordinal);

        if (value.Length > 0 && value[^1] is not ('.' or '!' or '?' or '…' or ':' or ';'))
            value += ".";
        return value;
    }

    private static float ResolveSpeed(string voiceId, string text)
    {
        var speed = voiceId switch
        {
            "af_nova" => 0.99f,
            "af_sky" => 0.98f,
            "af_bella" => 0.97f,
            "am_michael" => 0.98f,
            "am_adam" => 0.99f,
            "bf_emma" => 0.97f,
            "bm_george" => 0.96f,
            _ => 0.98f
        };

        var value = text.Trim();
        if (value.Length <= 36) speed -= 0.02f;
        if (value.EndsWith('?')) speed -= 0.01f;
        if (value.EndsWith('!')) speed += 0.01f;
        return Math.Clamp(speed, 0.93f, 1.02f);
    }

    private async Task WarmCachedModelSafelyAsync()
    {
        try { _ = await GetNeuralEngineAsync(CancellationToken.None).ConfigureAwait(false); }
        catch (Exception) { /* ordinary use can retry and report through the normal path */ }
    }

    private Task<KokoroTTS> GetNeuralEngineAsync(CancellationToken cancellationToken)
    {
        Task<KokoroTTS> loadTask;
        lock (_stateGate)
        {
            if (_neural is not null) return Task.FromResult(_neural);
            _neuralLoadTask ??= LoadNeuralEngineAsync();
            loadTask = _neuralLoadTask;
        }
        return loadTask.WaitAsync(cancellationToken);
    }

    private async Task<KokoroTTS> LoadNeuralEngineAsync()
    {
        try
        {
            var loaded = await KokoroTTS.LoadModelAsync(KModel.int8, null, null).ConfigureAwait(false);
            loaded.NicifyAudio = true;
            lock (_stateGate)
            {
                if (_disposed)
                {
                    loaded.Dispose();
                    throw new ObjectDisposedException(nameof(HybridNaturalSpeechOutputService));
                }
                _neural = loaded;
                return loaded;
            }
        }
        catch
        {
            lock (_stateGate) _neuralLoadTask = null;
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_stateGate) _neural?.StopPlayback();
        await _windows.StopAsync(cancellationToken).ConfigureAwait(false);
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
            _voiceProfiles.Clear();
        }
        _speechGate.Release();
        _speechGate.Dispose();
    }
}
