using Haven.Core;
using Haven.Desktop.Prefabs;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Plan;

/// <summary>
/// Haven-native presentation for the production Plan Today slice. Domain and persistence state stay outside this scene;
/// it projects canonical planner snapshots into DynamicUI rows and emits semantic user intent.
/// </summary>
internal sealed class PlanHavenScene : IDisposable
{
    private readonly HavenPrefabCatalog _prefabs;
    private readonly HavenDynamicUITemplateCatalog _templates;
    private readonly DynamicUI _dynamicUi;
    private readonly Dictionary<string, DynamicUIItem> _dayRows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DynamicUIItem> _freeRows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DynamicUIItem> _countdownRows = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<Guid, PlannerStudyLink> _studyLinks = new Dictionary<Guid, PlannerStudyLink>();
    private bool _disposed;

    public PlanHavenScene()
    {
        _prefabs = HavenPrefabCatalog.FromAssembly(typeof(PlanHavenScene).Assembly);
        _templates = HavenDynamicUITemplateCatalog.FromAssembly(typeof(PlanHavenScene).Assembly);
        Root = BuildRoot();
        _dynamicUi = new DynamicUI(Root, _templates, _prefabs);

        DateLabel = Get<HavenText>("DateLabel");
        CurrentTitle = Get<HavenText>("CurrentTitle");
        CurrentMeta = Get<HavenText>("CurrentMeta");
        NextTitle = Get<HavenText>("NextTitle");
        NextMeta = Get<HavenText>("NextMeta");
        Progress = Get<Progress>("DayProgress");
        ProgressLabel = Get<HavenText>("ProgressLabel");
        Status = Get<HavenText>("Status");
        DayItems = Get<DynamicUIRuntime>("DayItems");
        FreeWindows = Get<DynamicUIRuntime>("FreeWindows");
        Countdowns = Get<DynamicUIRuntime>("Countdowns");
        EmptyDay = Get<HavenElement>("EmptyDay");
        EmptyFree = Get<HavenElement>("EmptyFree");
        EmptyCountdowns = Get<HavenElement>("EmptyCountdowns");
        RefreshButton = Get<HavenButton>("Refresh");
        FullPlannerButton = Get<HavenButton>("FullPlanner");

        RefreshButton.Accessibility.AccessibleName = "Refresh Plan Today";
        FullPlannerButton.Accessibility.AccessibleName = "Open full planner";
        RefreshButton.Invoked += OnRefreshInvoked;
        FullPlannerButton.Invoked += OnFullPlannerInvoked;
    }

    public Page Root { get; }
    public HavenText DateLabel { get; }
    public HavenText CurrentTitle { get; }
    public HavenText CurrentMeta { get; }
    public HavenText NextTitle { get; }
    public HavenText NextMeta { get; }
    public Progress Progress { get; }
    public HavenText ProgressLabel { get; }
    public HavenText Status { get; }
    public DynamicUIRuntime DayItems { get; }
    public DynamicUIRuntime FreeWindows { get; }
    public DynamicUIRuntime Countdowns { get; }
    public HavenElement EmptyDay { get; }
    public HavenElement EmptyFree { get; }
    public HavenElement EmptyCountdowns { get; }
    public HavenButton RefreshButton { get; }
    public HavenButton FullPlannerButton { get; }

    public event EventHandler? RefreshRequested;
    public event EventHandler? FullPlannerRequested;
    public event EventHandler<Guid>? CompleteTaskRequested;
    public event EventHandler<PlannerStudyLink>? StudyRequested;

    public void SetLoading(bool loading)
    {
        RefreshButton.SetValue(HavenProperties.Enabled, !loading);
        if (loading) SetStatus("Refreshing your plan…");
    }

    public void SetStatus(string? message)
    {
        Status.Content = message ?? string.Empty;
        Status.SetValue(HavenProperties.Visibility, string.IsNullOrWhiteSpace(message) ? HavenVisibility.Collapsed : HavenVisibility.Visible);
    }

    public void SetDay(PlannerDaySnapshot snapshot, TimeZoneInfo timeZone, IReadOnlyDictionary<Guid, PlannerStudyLink> studyLinks, IReadOnlyDictionary<Guid, string> subjectNames)
    {
        _studyLinks = studyLinks;
        DateLabel.Content = TimeZoneInfo.ConvertTime(snapshot.DayStart, timeZone).ToString("dddd, d MMMM");
        CurrentTitle.Content = snapshot.CurrentItem?.Title ?? "Nothing in progress";
        CurrentMeta.Content = snapshot.CurrentItem is null ? "You are between scheduled items." : FormatRange(snapshot.CurrentItem, timeZone);
        NextTitle.Content = snapshot.NextItem?.Title ?? "No next item";
        NextMeta.Content = snapshot.NextItem is null ? "Your remaining day is clear." : FormatRange(snapshot.NextItem, timeZone);
        Progress.Value = snapshot.Progress * 100d;
        ProgressLabel.Content = snapshot.ScheduleStart is null ? "No timed schedule yet" : $"{snapshot.Progress:P0} through scheduled day";

        var expected = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < snapshot.Items.Count; index++)
        {
            var item = snapshot.Items[index];
            var id = RowId(item);
            expected.Add(id);
            var values = DayValues(item, snapshot, timeZone, studyLinks, subjectNames);
            if (!_dayRows.TryGetValue(id, out var row))
            {
                row = _dynamicUi.CreateItem("PlanDayItem", "DayItems", id, values, index);
                _dayRows[id] = row;
                WireDayRow(row, item);
            }
            else
            {
                row.SetVariables(values);
                var currentIndex = DayItems.Items.ToList().IndexOf(row);
                if (currentIndex != index) _dynamicUi.MoveItem("DayItems", id, index);
            }
        }
        foreach (var stale in _dayRows.Keys.Where(id => !expected.Contains(id)).ToArray())
        {
            _dynamicUi.DeleteItem("DayItems", stale);
            _dayRows.Remove(stale);
        }
        DayItems.SetValue(HavenProperties.Visibility, snapshot.Items.Count > 0 ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        EmptyDay.SetValue(HavenProperties.Visibility, snapshot.Items.Count > 0 ? HavenVisibility.Collapsed : HavenVisibility.Visible);
    }

    public void SetFreeWindows(IReadOnlyList<PlannerFreeWindow> windows, TimeZoneInfo timeZone)
    {
        var expected = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < windows.Count; index++)
        {
            var window = windows[index];
            var id = $"free-{window.StartsAt.UtcTicks}-{window.EndsAt.UtcTicks}";
            expected.Add(id);
            var values = new Dictionary<string, object?>
            {
                ["TIME"] = $"{FormatTime(window.StartsAt, timeZone)}–{FormatTime(window.EndsAt, timeZone)}",
                ["DURATION"] = FormatDuration(window.EndsAt - window.StartsAt)
            };
            if (!_freeRows.TryGetValue(id, out var row))
            {
                row = _dynamicUi.CreateItem("PlanFreeWindow", "FreeWindows", id, values, index);
                _freeRows[id] = row;
            }
            else
            {
                row.SetVariables(values);
                var currentIndex = FreeWindows.Items.ToList().IndexOf(row);
                if (currentIndex != index) _dynamicUi.MoveItem("FreeWindows", id, index);
            }
        }
        foreach (var stale in _freeRows.Keys.Where(id => !expected.Contains(id)).ToArray())
        {
            _dynamicUi.DeleteItem("FreeWindows", stale);
            _freeRows.Remove(stale);
        }
        FreeWindows.SetValue(HavenProperties.Visibility, windows.Count > 0 ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        EmptyFree.SetValue(HavenProperties.Visibility, windows.Count > 0 ? HavenVisibility.Collapsed : HavenVisibility.Visible);
    }

    public void SetCountdowns(IReadOnlyList<PlannerCountdown> countdowns, TimeZoneInfo timeZone)
    {
        var visible = countdowns.Take(8).ToArray();
        var expected = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < visible.Length; index++)
        {
            var countdown = visible[index];
            var id = $"countdown-{countdown.SourceKind}-{countdown.SourceId:N}";
            expected.Add(id);
            var values = new Dictionary<string, object?>
            {
                ["TITLE"] = countdown.Title,
                ["TARGET"] = TimeZoneInfo.ConvertTime(countdown.TargetAt, timeZone).ToString("ddd d MMM · HH:mm"),
                ["REMAINING"] = FormatRemaining(countdown),
                ["STATECOLOR"] = countdown.State is PlannerCountdownState.Passed or PlannerCountdownState.Due ? "Danger" : "Accent"
            };
            if (!_countdownRows.TryGetValue(id, out var row))
            {
                row = _dynamicUi.CreateItem("PlanCountdown", "Countdowns", id, values, index);
                _countdownRows[id] = row;
            }
            else
            {
                row.SetVariables(values);
                var currentIndex = Countdowns.Items.ToList().IndexOf(row);
                if (currentIndex != index) _dynamicUi.MoveItem("Countdowns", id, index);
            }
        }
        foreach (var stale in _countdownRows.Keys.Where(id => !expected.Contains(id)).ToArray())
        {
            _dynamicUi.DeleteItem("Countdowns", stale);
            _countdownRows.Remove(stale);
        }
        Countdowns.SetValue(HavenProperties.Visibility, visible.Length > 0 ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        EmptyCountdowns.SetValue(HavenProperties.Visibility, visible.Length > 0 ? HavenVisibility.Collapsed : HavenVisibility.Visible);
    }

    private void WireDayRow(DynamicUIItem row, PlannerDayItem item)
    {
        var complete = row.GetComponent<HavenButton>("Complete");
        complete.Accessibility.AccessibleName = $"Complete {item.Title}";
        complete.Invoked += (_, _) => CompleteTaskRequested?.Invoke(this, item.EntityId);
        var study = row.GetComponent<HavenButton>("Study");
        study.Accessibility.AccessibleName = $"Open Study for {item.Title}";
        study.Invoked += (_, _) =>
        {
            if (_studyLinks.TryGetValue(item.EntityId, out var link)) StudyRequested?.Invoke(this, link);
        };
    }

    private static Dictionary<string, object?> DayValues(PlannerDayItem item, PlannerDaySnapshot snapshot, TimeZoneInfo timeZone, IReadOnlyDictionary<Guid, PlannerStudyLink> studyLinks, IReadOnlyDictionary<Guid, string> subjectNames)
    {
        var isCurrent = snapshot.CurrentItem?.EntityId == item.EntityId && snapshot.CurrentItem.Kind == item.Kind;
        PlannerStudyLink? study = item.Kind == PlannerDayItemKind.Task && studyLinks.TryGetValue(item.EntityId, out var link) ? link : null;
        var subject = study is null ? null : subjectNames.GetValueOrDefault(study.SubjectId);
        var status = item.IsCompleted ? "Completed" : item.IsCancelled ? "Cancelled" : isCurrent ? "Now" : item.IsReadOnly ? "Calendar · read only" : item.Kind == PlannerDayItemKind.Event ? "Calendar event" : "Task";
        if (study is not null) status += $" · Study{(string.IsNullOrWhiteSpace(subject) ? string.Empty : $" · {subject}")}";
        return new Dictionary<string, object?>
        {
            ["TIME"] = item.IsAllDay ? "All day" : FormatItemTime(item, timeZone),
            ["TITLE"] = item.Title,
            ["META"] = status,
            ["STATECOLOR"] = isCurrent ? "Accent" : item.IsCompleted ? "TextSecondary" : "TextPrimary",
            ["COMPLETEVISIBILITY"] = item.Kind == PlannerDayItemKind.Task && item.IsActionable ? "Visible" : "Collapsed",
            ["STUDYVISIBILITY"] = study is not null ? "Visible" : "Collapsed",
            ["STUDYLABEL"] = string.IsNullOrWhiteSpace(subject) ? "Study" : subject
        };
    }

    private static string RowId(PlannerDayItem item) => $"{item.Kind.ToString().ToLowerInvariant()}-{item.EntityId:N}";
    private static string FormatItemTime(PlannerDayItem item, TimeZoneInfo zone)
    {
        if (item.StartsAt is not null && item.EndsAt is not null) return $"{FormatTime(item.StartsAt.Value, zone)}–{FormatTime(item.EndsAt.Value, zone)}";
        if (item.StartsAt is not null) return FormatTime(item.StartsAt.Value, zone);
        if (item.DueAt is not null) return $"Due {FormatTime(item.DueAt.Value, zone)}";
        return "Any time";
    }
    private static string FormatRange(PlannerDayItem item, TimeZoneInfo zone) => item.IsAllDay ? "All day" : FormatItemTime(item, zone);
    private static string FormatTime(DateTimeOffset value, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(value, zone).ToString("HH:mm");
    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            var hours = (int)duration.TotalHours;
            return duration.Minutes == 0 ? $"{hours}h free" : $"{hours}h {duration.Minutes}m free";
        }
        return $"{Math.Max(0, (int)duration.TotalMinutes)}m free";
    }
    private static string FormatRemaining(PlannerCountdown countdown)
    {
        if (countdown.State == PlannerCountdownState.Completed) return "Completed";
        if (countdown.State == PlannerCountdownState.Cancelled) return "Cancelled";
        if (countdown.State == PlannerCountdownState.Due) return "Due now";
        var absolute = countdown.Remaining.Duration();
        var value = absolute.TotalDays >= 1 ? $"{(int)absolute.TotalDays}d {absolute.Hours}h" : absolute.TotalHours >= 1 ? $"{(int)absolute.TotalHours}h {absolute.Minutes}m" : $"{Math.Max(0, (int)absolute.TotalMinutes)}m";
        return countdown.State == PlannerCountdownState.Passed ? $"{value} ago" : value;
    }

    private T Get<T>(string name) where T : HavenElement => (T)Root.DescendantsAndSelf().Single(element => element.Name == name);
    private void OnRefreshInvoked(object? sender, EventArgs e) => RefreshRequested?.Invoke(this, EventArgs.Empty);
    private void OnFullPlannerInvoked(object? sender, EventArgs e) => FullPlannerRequested?.Invoke(this, EventArgs.Empty);

    private Page BuildRoot()
    {
        const string markup = """
            <Page Name="PlanRoot" Layout="Vertical" Background="Transparent" Width="100%" Height="100%" Padding="30px 26px 30px 22px" Gap="16px">
              <Container Name="Header" Layout="Grid" Columns="1fr Auto Auto" Rows="Auto" Width="100%" Gap="8px">
                <Container Name="Heading" Column="0" Layout="Vertical" Gap="2px"><Text Content="Plan" Level="H1" /><Text Name="DateLabel" Content="Today" FontSize="12" FontWeight="600" Foreground="TextSecondary" /></Container>
                <Button Name="FullPlanner" Column="1" Variant="Ghost" IconKey="calendar" Content="Full planner" MinHeight="36px" VerticalAlignment="Center" />
                <Button Name="Refresh" Column="2" Variant="Icon" IconKey="refresh" Content="" Width="36px" Height="36px" MinHeight="36px" VerticalAlignment="Center" />
              </Container>
              <Container Name="Viewport" Layout="Vertical" Width="100%" Overflow="Scroll" Clip="true" Gap="16px">
                <Container Name="NowCard" Layout="Vertical" Width="100%" Gap="9px" Padding="18px" Background="Surface" BorderColor="Border" BorderWidth="1px" Radius="20px" Shadow="Card">
                  <Text Content="NOW" FontSize="10" FontWeight="800" Foreground="Accent" /><Text Name="CurrentTitle" Content="Nothing in progress" Level="H2" /><Text Name="CurrentMeta" Content="You are between scheduled items." FontSize="12" Foreground="TextSecondary" />
                  <Container Layout="Grid" Columns="Auto 1fr" Rows="Auto" Width="100%" Gap="10px" Margin="0px 6px 0px 0px"><Text Content="Next" Column="0" FontSize="11" FontWeight="700" Foreground="TextSecondary" /><Container Column="1" Layout="Vertical" Gap="2px"><Text Name="NextTitle" Content="No next item" FontSize="12" FontWeight="700" /><Text Name="NextMeta" Content="Your remaining day is clear." FontSize="10" Foreground="TextSecondary" /></Container></Container>
                  <Progress Name="DayProgress" Minimum="0" Maximum="100" Value="0" MinHeight="8px" /><Text Name="ProgressLabel" Content="No timed schedule yet" FontSize="10" Foreground="TextSecondary" />
                </Container>
                <Container Name="BodyGrid" Layout="Grid" Columns="1.7fr 1fr" Rows="Auto" Width="100%" Gap="16px">
                  <Container Name="TimelineSection" Column="0" Layout="Vertical" Gap="9px"><Text Content="Today" Level="H2" /><Text Name="EmptyDay" Content="Nothing scheduled for today yet." FontSize="12" Foreground="TextSecondary" /><DynamicUIRuntime Name="DayItems" Width="100%" /></Container>
                  <Container Name="SideColumn" Column="1" Layout="Vertical" Gap="16px">
                    <Container Name="FreeSection" Layout="Vertical" Gap="8px" Padding="14px" Background="Surface" BorderColor="Border" BorderWidth="1px" Radius="18px"><Text Content="Free time" Level="H3" /><Text Name="EmptyFree" Content="No 30-minute free windows left today." FontSize="11" Foreground="TextSecondary" /><DynamicUIRuntime Name="FreeWindows" Width="100%" /></Container>
                    <Container Name="CountdownSection" Layout="Vertical" Gap="8px" Padding="14px" Background="Surface" BorderColor="Border" BorderWidth="1px" Radius="18px"><Text Content="Coming up" Level="H3" /><Text Name="EmptyCountdowns" Content="No deadlines or events in the next two weeks." FontSize="11" Foreground="TextSecondary" /><DynamicUIRuntime Name="Countdowns" Width="100%" /></Container>
                  </Container>
                </Container>
              </Container>
              <Text Name="Status" Content="" FontSize="11" Foreground="TextSecondary" HorizontalAlignment="Center" Visibility="Collapsed" />
            </Page>
            """;
        return (Page)new HavenMarkupParser(_prefabs).Parse(markup, "PlanToday.hui");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        RefreshButton.Invoked -= OnRefreshInvoked;
        FullPlannerButton.Invoked -= OnFullPlannerInvoked;
        _dayRows.Clear();
        _freeRows.Clear();
        _countdownRows.Clear();
    }
}
