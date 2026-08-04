using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Services;

/// <summary>
/// Android compatibility adapter for the Windows speech fallback used by the shared
/// hybrid speech router. Kokoro neural voices remain available; a request for a
/// Windows voice is rejected explicitly rather than silently ignored.
/// </summary>
public sealed class WindowsNaturalSpeechOutputService : ISpeechOutputService, IAsyncDisposable
{
    public bool IsAvailable => false;

    public string? UnavailableReason =>
        "Windows speech voices are not available on Android. Haven neural voices remain available.";

    public IReadOnlyList<CallAudioDevice> Devices { get; } =
        [new CallAudioDevice("default", "Android default output", true)];

    public IReadOnlyList<CallVoice> Voices { get; } = [];

    public Task SpeakAsync(
        string text,
        string? voiceName,
        string? outputDeviceId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new PlatformNotSupportedException(UnavailableReason);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
