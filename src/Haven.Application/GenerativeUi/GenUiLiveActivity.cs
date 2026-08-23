using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Represents a live GenUI activity surface that materialises when Haven
/// performs a meaningful visible operation on content. The surface shows
/// real document/state operations occurring in place rather than hiding
/// work behind detached status messages.
/// </summary>
public interface IGenUiLiveActivitySurface
{
    /// <summary>The unique activity surface instance ID.</summary>
    Guid ActivityId { get; }

    /// <summary>The owning thread this activity is scoped to.</summary>
    Guid ThreadId { get; }

    /// <summary>The owning App key.</summary>
    string AppKey { get; }

    /// <summary>Descriptive title for the activity.</summary>
    string Title { get; }

    /// <summary>Current phase of the live activity.</summary>
    GenUiLiveActivityPhase Phase { get; }

    /// <summary>Progress percentage (0-100) if measurable.</summary>
    double Progress { get; }

    /// <summary>Current status message.</summary>
    string StatusMessage { get; }
    GenUiLiveActivityControlMode ControlMode { get; }
    GenUiActivityPresentation Presentation { get; }
    string? LastError { get; }

    /// <summary>Fires when the activity state changes.</summary>
    event EventHandler? StateChanged;

    /// <summary>Updates the activity with new state/progress.</summary>
    void Update(GenUiLiveActivityUpdate update);
    void SetControlMode(GenUiLiveActivityControlMode mode);
    void SetPresentation(GenUiActivityPresentation presentation);

    /// <summary>Cancels the underlying operation if supported.</summary>
    void Cancel();

    /// <summary>Dismisses the surface.</summary>
    void Dismiss();
}

public enum GenUiLiveActivityPhase
{
    Preparing,
    Operating,
    Validating,
    Completed,
    Failed,
    Cancelled,
    Dismissed
}

public enum GenUiLiveActivityControlMode { Watch, Steer, TakeOver }
public enum GenUiActivityPresentation { Compact, FullScreen }

/// <summary>
/// Structured update for a live activity surface, supporting incremental
/// state changes without rebuilding the entire surface.
/// </summary>
public sealed record GenUiLiveActivityUpdate(
    GenUiLiveActivityPhase? Phase,
    string? StatusMessage,
    double? Progress,
    IReadOnlyList<GenUiStatePatch>? Patches,
    JsonElement? StructuredResult,
    DateTimeOffset Timestamp);

/// <summary>
/// Factory for creating live activity surfaces when Haven performs
/// meaningful visible operations on content.
/// </summary>
public interface IGenUiLiveActivityFactory
{
    /// <summary>
    /// Creates a new live activity surface for the given operation.
    /// </summary>
    IGenUiLiveActivitySurface Create(GenUiLiveActivityRequest request);
}

public sealed record GenUiLiveActivityRequest(
    Guid ThreadId,
    string AppKey,
    string Title,
    string OperationType,
    string? TargetResourceId,
    GenUiOrigin Origin);

/// <summary>
/// Default in-memory live activity surface implementation.
/// Platform-specific hosts render this state through HavenUI.
/// </summary>
public sealed class DefaultGenUiLiveActivitySurface : IGenUiLiveActivitySurface
{
    private readonly object _gate = new();
    private readonly GenUiInstanceStore _instances;

    public DefaultGenUiLiveActivitySurface(
        Guid activityId,
        Guid threadId,
        string appKey,
        string title,
        GenUiInstanceStore instances)
    {
        ActivityId = activityId;
        ThreadId = threadId;
        AppKey = appKey;
        Title = title;
        _instances = instances;
    }

    public Guid ActivityId { get; }
    public Guid ThreadId { get; }
    public string AppKey { get; }
    public string Title { get; }

    public GenUiLiveActivityPhase Phase { get; private set; } = GenUiLiveActivityPhase.Preparing;
    public double Progress { get; private set; }
    public string StatusMessage { get; private set; } = "Preparing…";
    public GenUiLiveActivityControlMode ControlMode { get; private set; } = GenUiLiveActivityControlMode.Watch;
    public GenUiActivityPresentation Presentation { get; private set; } = GenUiActivityPresentation.Compact;
    public string? LastError { get; private set; }

    public event EventHandler? StateChanged;

    public void Update(GenUiLiveActivityUpdate update)
    {
        lock (_gate)
        {
            if (update.Phase.HasValue) Phase = update.Phase.Value;
            if (update.Progress.HasValue) Progress = Math.Clamp(update.Progress.Value, 0, 100);
            if (update.StatusMessage is not null) StatusMessage = update.StatusMessage;

            if (update.Patches is not null)
            {
                try
                {
                    _instances.ApplyPatchesAtomically(update.Patches);
                    LastError = null;
                }
                catch (Exception ex)
                {
                    Phase = GenUiLiveActivityPhase.Failed;
                    LastError = ex.Message;
                    StatusMessage = $"Activity update failed: {ex.Message}";
                }
            }
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetControlMode(GenUiLiveActivityControlMode mode)
    {
        lock (_gate)
        {
            if (Phase is GenUiLiveActivityPhase.Completed or GenUiLiveActivityPhase.Cancelled or GenUiLiveActivityPhase.Dismissed)
                throw new InvalidOperationException("A finished activity cannot change control mode.");
            ControlMode = mode;
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetPresentation(GenUiActivityPresentation presentation)
    {
        lock (_gate) Presentation = presentation;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Cancel()
    {
        lock (_gate) Phase = GenUiLiveActivityPhase.Cancelled;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dismiss()
    {
        lock (_gate) Phase = GenUiLiveActivityPhase.Dismissed;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Tracks active live activity surfaces across threads.
/// </summary>
public sealed class GenUiLiveActivityTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, IGenUiLiveActivitySurface> _activities = new();

    public event EventHandler<IGenUiLiveActivitySurface>? ActivityCreated;
    public event EventHandler<IGenUiLiveActivitySurface>? ActivityUpdated;
    public event EventHandler<IGenUiLiveActivitySurface>? ActivityDismissed;

    public IReadOnlyList<IGenUiLiveActivitySurface> ActiveActivities
    {
        get
        {
            lock (_gate)
                return _activities.Values
                    .Where(a => a.Phase is not (GenUiLiveActivityPhase.Dismissed or GenUiLiveActivityPhase.Completed))
                    .ToArray();
        }
    }

    public IReadOnlyList<IGenUiLiveActivitySurface> GetForThread(Guid threadId)
    {
        lock (_gate)
            return _activities.Values
                .Where(a => a.ThreadId == threadId && a.Phase is not GenUiLiveActivityPhase.Dismissed)
                .ToArray();
    }

    public void Track(IGenUiLiveActivitySurface activity)
    {
        lock (_gate) _activities[activity.ActivityId] = activity;
        activity.StateChanged += (_, _) =>
        {
            if (activity.Phase is GenUiLiveActivityPhase.Dismissed)
            {
                lock (_gate) _activities.Remove(activity.ActivityId);
                ActivityDismissed?.Invoke(this, activity);
            }
            else
            {
                ActivityUpdated?.Invoke(this, activity);
            }
        };
        ActivityCreated?.Invoke(this, activity);
    }
}
