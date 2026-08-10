using Haven.Application;
using Haven.Core;

namespace Haven.Android.Compatibility;

/// <summary>
/// Android host boundary for detached HavenUI surfaces. Availability is
/// reported from the Android overlay capability; callers must not assume an
/// overlay permission or claim a surface was presented without an observed
/// platform result.
/// </summary>
public sealed class AndroidFloatingActivityHost : IFloatingActivityHost
{
    private readonly FloatingActivityStateStore _stateStore;

    public AndroidFloatingActivityHost(FloatingActivityStateStore stateStore) => _stateStore = stateStore;

    public string Platform => "Android";
    public bool IsAvailable => OperatingSystem.IsAndroid();
    public string? UnavailableReason => IsAvailable ? "Android overlay permission must be checked by the active host." : "Android host unavailable on this platform.";
    public event EventHandler<FloatingActivitySnapshot>? StateChanged;

    public Task<FloatingActivitySnapshot> PresentAsync(FloatingActivityDefinition definition, IFloatingActivityContent content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new PlatformNotSupportedException("Android detached hosting requires an approved overlay provider and runtime permission.");
    }

    public Task<FloatingActivitySnapshot> UpdateAsync(FloatingActivitySnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _stateStore.Set(snapshot);
        StateChanged?.Invoke(this, snapshot);
        return Task.FromResult(snapshot);
    }

    public Task DismissAsync(Guid activityId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _stateStore.Remove(activityId);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
