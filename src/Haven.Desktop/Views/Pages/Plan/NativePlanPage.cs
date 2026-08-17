using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views.Pages.Plan;

/// <summary>
/// Platform host for the Haven-native Plan Today scene. Avalonia only hosts the Haven scene; canonical planner services own business state.
/// </summary>
public sealed class NativePlanPage : UserControl, IActivatablePage, IDisposable
{
    private readonly IPlannerRepository _planner;
    private readonly IContainerRepository _containers;
    private readonly IPlannerDayService _dayService;
    private readonly IPlannerAvailabilityService _availabilityService;
    private readonly IPlannerCountdownService _countdownService;
    private readonly PlanHavenScene _scene;
    private readonly DispatcherTimer _refreshTimer;
    private CancellationTokenSource? _refreshCancellation;
    private bool _active;
    private bool _disposed;

    public NativePlanPage(IPlannerRepository planner, IContainerRepository containers)
    {
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _containers = containers ?? throw new ArgumentNullException(nameof(containers));
        _dayService = new PlannerDayService(_planner);
        _availabilityService = new PlannerAvailabilityService(_dayService);
        _countdownService = new PlannerCountdownService(_planner);
        _scene = new PlanHavenScene();
        AutomationProperties.SetAutomationId(this, "HavenNativePlanPage");
        AutomationProperties.SetName(this, "Haven-native Plan");
        Scene = new HavenSceneControl { Root = _scene.Root };
        AutomationProperties.SetAutomationId(Scene, "HavenNativePlanScene");
        AutomationProperties.SetName(Scene, "Plan Today");
        Content = Scene;
        _scene.RefreshRequested += OnRefreshRequested;
        _scene.CompleteTaskRequested += OnCompleteTaskRequested;
        _scene.StudyRequested += OnStudyRequested;
        _scene.FullPlannerRequested += OnFullPlannerRequested;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _refreshTimer.Tick += OnRefreshTimer;
    }

    public HavenSceneControl Scene { get; }
    public event EventHandler<PlannerStudyLink>? StudyRequested;
    public event EventHandler? FullPlannerRequested;

    public async Task ActivateAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        _active = true;
        _refreshTimer.Start();
        await RefreshAsync(cancellationToken);
    }

    public void Deactivate()
    {
        _active = false;
        _refreshTimer.Stop();
        _refreshCancellation?.Cancel();
    }

    internal Task RefreshNowAsync(CancellationToken cancellationToken = default) => RefreshAsync(cancellationToken);

    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var refreshCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var previous = Interlocked.Exchange(ref _refreshCancellation, refreshCancellation);
        previous?.Cancel();
        var token = refreshCancellation.Token;
        await Dispatcher.UIThread.InvokeAsync(() => _scene.SetLoading(true));
        try
        {
            await _planner.EnsureDefaultsAsync(token);
            var now = DateTimeOffset.Now;
            var zone = TimeZoneInfo.Local;
            var snapshot = await _dayService.GetDayAsync(now, now, zone.Id, token);
            token.ThrowIfCancellationRequested();
            var freeStart = now < snapshot.DayStart ? snapshot.DayStart : now > snapshot.DayEnd ? snapshot.DayEnd : now;
            Task<IReadOnlyList<PlannerFreeWindow>> freeTask = freeStart < snapshot.DayEnd
                ? _availabilityService.GetFreeWindowsAsync(now, now, zone.Id, freeStart, snapshot.DayEnd, TimeSpan.FromMinutes(30), token)
                : Task.FromResult<IReadOnlyList<PlannerFreeWindow>>([]);
            var countdownEnd = now.AddDays(14);
            var countdownTask = _countdownService.GetCountdownsAsync(now.AddMinutes(-1), countdownEnd, now, token);
            var taskDetailsTask = _planner.GetTasksAsync(new PlannerTaskQuery(RangeStart: snapshot.DayStart, RangeEnd: countdownEnd, IncludeCompleted: true), token);
            var subjectsTask = _containers.GetByModeAsync(HavenMode.Study, token);
            await Task.WhenAll(freeTask, countdownTask, taskDetailsTask, subjectsTask);
            token.ThrowIfCancellationRequested();
            var links = new Dictionary<Guid, PlannerStudyLink>();
            foreach (var task in await taskDetailsTask)
                if (PlannerStudyAssignmentTags.TryRead(task.TagsJson, out var link)) links[task.Id] = link;
            var subjectNames = (await subjectsTask).ToDictionary(subject => subject.Id, subject => subject.Name);
            var free = await freeTask;
            var countdowns = await countdownTask;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _scene.SetDay(snapshot, zone, links, subjectNames);
                _scene.SetFreeWindows(free, zone);
                _scene.SetCountdowns(countdowns, zone);
                _scene.SetStatus(null);
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await Dispatcher.UIThread.InvokeAsync(() => _scene.SetStatus($"Plan could not refresh: {exception.Message}"));
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _refreshCancellation, null, refreshCancellation), refreshCancellation))
            {
                if (!token.IsCancellationRequested)
                    await Dispatcher.UIThread.InvokeAsync(() => _scene.SetLoading(false));
            }
            refreshCancellation.Dispose();
        }
    }

    private void OnRefreshRequested(object? sender, EventArgs e) => _ = RefreshAsync();
    private async void OnCompleteTaskRequested(object? sender, Guid taskId)
    {
        try
        {
            var task = await _planner.GetTaskAsync(taskId, CancellationToken.None);
            if (task is null || task.Status is PlannerTaskStatus.Completed or PlannerTaskStatus.Cancelled) return;
            await _planner.CompleteTaskAsync(taskId, DateTimeOffset.Now, CancellationToken.None);
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            _scene.SetStatus($"Could not complete task: {exception.Message}");
        }
    }
    private void OnStudyRequested(object? sender, PlannerStudyLink link) => StudyRequested?.Invoke(this, link);
    private void OnFullPlannerRequested(object? sender, EventArgs e) => FullPlannerRequested?.Invoke(this, EventArgs.Empty);
    private void OnRefreshTimer(object? sender, EventArgs e) { if (_active) _ = RefreshAsync(); }
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _active = false;
        _refreshTimer.Stop();
        _refreshTimer.Tick -= OnRefreshTimer;
        Interlocked.Exchange(ref _refreshCancellation, null)?.Cancel();
        _scene.RefreshRequested -= OnRefreshRequested;
        _scene.CompleteTaskRequested -= OnCompleteTaskRequested;
        _scene.StudyRequested -= OnStudyRequested;
        _scene.FullPlannerRequested -= OnFullPlannerRequested;
        _scene.Dispose();
    }
}
