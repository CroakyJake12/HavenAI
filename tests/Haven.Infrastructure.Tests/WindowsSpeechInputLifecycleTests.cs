/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/WindowsSpeechInputLifecycleTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns WindowsSpeechInputLifecycleTests. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents windows speech input lifecycle tests and keeps its related state and behavior together.
/// </summary>
public sealed class WindowsSpeechInputLifecycleTests
{
    /// <summary>
    /// Performs the dispose is idempotent and makes the service unavailable step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the push to talk cannot restart after disposal step owned by this component.
    /// </summary>
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
