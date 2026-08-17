using Haven.Application;

namespace Haven.Android;

public sealed record AndroidProjectorControllerActionResult(bool Succeeded, string Message);

public interface IAndroidProjectorControllerActionHandler
{
    string ActionKey { get; }
    ValueTask<AndroidProjectorControllerActionResult> InvokeAsync(
        ProjectorSessionSnapshot session,
        ProjectorControllerAction action,
        CancellationToken cancellationToken);
}

public sealed class AndroidProjectorControllerActionDispatcher
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<IAndroidProjectorControllerActionHandler>> _handlers;

    public AndroidProjectorControllerActionDispatcher(IEnumerable<IAndroidProjectorControllerActionHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        _handlers = handlers
            .Where(handler => !string.IsNullOrWhiteSpace(handler.ActionKey))
            .GroupBy(handler => handler.ActionKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<IAndroidProjectorControllerActionHandler>)group.ToArray(),
                StringComparer.Ordinal);
    }

    public async ValueTask<AndroidProjectorControllerActionResult> InvokeAsync(
        ProjectorSessionSnapshot session,
        ProjectorControllerAction action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();

        if (session.State != ProjectorSessionState.Active)
            return new(false, "Projector controller actions are only available for an active experience.");

        var controller = session.Controller;
        if (controller is null)
            return new(false, "This Projector experience has not exposed phone controls.");

        var declared = controller.Actions.Any(candidate =>
            string.Equals(candidate.Id, action.Id, StringComparison.Ordinal)
            && string.Equals(candidate.ActionKey, action.ActionKey, StringComparison.Ordinal));
        if (!declared)
            return new(false, "That action is not declared by the active Projector controller.");

        if (!_handlers.TryGetValue(action.ActionKey, out var handlers) || handlers.Count == 0)
            return new(false, $"No Android handler is registered for {action.Label}.");
        if (handlers.Count != 1)
            return new(false, $"Multiple Android handlers are registered for {action.Label}; Haven will not choose one implicitly.");

        try
        {
            return await handlers[0]
                .InvokeAsync(session, action, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Warn("HavenProjector", "Projector controller action failed: " + exception.Message);
            return new(false, $"{action.Label} could not be completed.");
        }
    }
}
