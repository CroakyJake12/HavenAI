using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class WindowsSpeechInputLifecycleTests
{
    [Fact]
    public async Task DisposeIsIdempotentAndMakesTheServiceUnavailable()
    {
        var service = new WindowsSpeechInputService();

        await service.DisposeAsync();
        await service.DisposeAsync();

        Assert.False(service.IsAvailable);
        Assert.Empty(service.Devices);
        Assert.Contains("disposed", service.UnavailableReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PushToTalkCannotRestartAfterDisposal()
    {
        var service = new WindowsSpeechInputService();
        await service.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            service.BeginPushToTalkAsync(CancellationToken.None));
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            service.EndPushToTalkAsync(CancellationToken.None));
    }
}
