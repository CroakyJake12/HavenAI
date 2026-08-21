using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenContainer = Haven.UI.Components.Container;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Plan;

/// <summary>Owns the Haven-native week projection while canonical Plan services remain authoritative.</summary>
internal sealed class PlanWeekCoordinator
{
    private readonly PlanHavenScene _scene;
    private readonly IPlannerRepository _planner;
    private readonly IContainerRepository _containers;
    private readonly IPlannerDayService _days;
    private readonly Func<CancellationToken, Task> _refreshToday;
    private readonly Action<PlannerStudyLink> _openStudy;
    private readonly HavenElement _todayViewport;
    private readonly HavenContainer _root;
    private readonly HavenContainer _strip;
    private readonly HavenContainer _monthRoot;
    private readonly HavenContainer _monthHost;
    private readonly HavenText _monthRange;
    private readonly HavenButton _monthPrevious;
    private readonly HavenButton _monthThisMonth;
    private readonly HavenButton _monthNext;
    private readonly HavenButton _monthRefresh;
    private readonly HavenText _range;
    private readonly HavenButton _previous;
    private readonly HavenButton _thisWeek;
    private readonly HavenButton _next;
    private readonly HavenButton _refresh;
    private DateTimeOffset _anchor = DateTimeOffset.Now;
    private DateTimeOffset _monthAnchor = DateTimeOffset.Now;
    private int _version;
    private int _monthVersion;
    private bool _visible;
    private bool _monthVisible;

    private PlanWeekCoordinator(
        PlanHavenScene scene,
        IPlannerRepository planner,
        IContainerRepository containers,
        Func<CancellationToken, Task> refreshToday,
        Action<PlannerStudyLink> openStudy)
    {
        _scene = scene;
        _planner = planner;
        _containers = containers;
        _days = new PlannerDayService(planner);
        _refreshToday = refreshToday;
        _openStudy = openStudy;
        _todayViewport = scene.Root.DescendantsAndSelf().Single(x => x.Name == "Viewport");

        _root = Vertical("PlanWeekRoot", 12);
        _root.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        _root.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        _root.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        var nav = Horizontal("PlanWeekNavigation", 8);
        nav.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        _previous = Button("PlanWeekPrevious", "Previous", "chevron-left");
        _thisWeek = Button("PlanWeekThisWeek", "This week", "calendar");
        _next = Button("PlanWeekNext", "Next", "chevron-right");
        _refresh = Button("PlanWeekRefresh", "Refresh", "refresh");
        _range = new HavenText { Name = "PlanWeekRange", Content = "Week", Level = TextLevel.H2 };
        _range.SetValue(HavenProperties.MinWidth, HavenLength.Px(210));
        nav.Add(_previous); nav.Add(_range); nav.Add(_thisWeek); nav.Add(_next); nav.Add(_refresh);
        _root.Add(nav);
        _strip = Horizontal("PlanWeekDays", 10);
        _strip.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        _strip.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        _strip.SetValue(HavenProperties.Padding, HavenThickness.Parse("2px 2px 12px 2px"));
        _root.Add(_strip);
        _monthRoot = Vertical("PlanMonthRoot", 12);
        _monthRoot.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        _monthRoot.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        _monthRoot.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        var monthNav = Horizontal("PlanMonthNavigation", 8);
        _monthPrevious = Button("PlanMonthPrevious", "Previous", "chevron-left");
        _monthThisMonth = Button("PlanMonthThisMonth", "This month", "calendar");
        _monthNext = Button("PlanMonthNext", "Next", "chevron-right");
        _monthRefresh = Button("PlanMonthRefresh", "Refresh", "refresh");
        _monthRange = new HavenText { Name = "PlanMonthRange", Content = "Month", Level = TextLevel.H2 };
        monthNav.Add(_monthPrevious); monthNav.Add(_monthRange); monthNav.Add(_monthThisMonth); monthNav.Add(_monthNext); monthNav.Add(_monthRefresh);
        _monthRoot.Add(monthNav);
        _monthHost = Vertical("PlanMonthHost", 10); _monthHost.SetValue(HavenProperties.Width, HavenLength.Percent(100)); _monthHost.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll); _monthRoot.Add(_monthHost);
        scene.Root.Add(_root); scene.Root.Add(_monthRoot);

        scene.WeekRequested += (_, _) => _ = ToggleAsync();
        scene.MonthRequested += (_, _) => _ = ToggleMonthAsync();
        _monthPrevious.Invoked += (_, _) => ShiftMonth(-1);
        _monthThisMonth.Invoked += (_, _) => { _monthAnchor = DateTimeOffset.Now; _ = RefreshMonthAsync(); };
        _monthNext.Invoked += (_, _) => ShiftMonth(1);
        _monthRefresh.Invoked += (_, _) => _ = RefreshMonthAsync();
        _previous.Invoked += (_, _) => Shift(-7);
        _thisWeek.Invoked += (_, _) => { _anchor = DateTimeOffset.Now; _ = RefreshAsync(); };
        _next.Invoked += (_, _) => Shift(7);
        _refresh.Invoked += (_, _) => _ = RefreshAsync();
    }

    public static void Attach(
        PlanHavenScene scene,
        IPlannerRepository planner,
        IContainerRepository containers,
        Func<CancellationToken, Task> refreshToday,
        Action<PlannerStudyLink> openStudy) =>
        _ = new PlanWeekCoordinator(scene, planner, containers, refreshToday, openStudy);

    private async Task ToggleAsync() { if (_visible) { _visible=false; ApplyVisibility(); await RefreshTodaySafeAsync(); return; } _monthVisible=false; _visible=true; Interlocked.Increment(ref _monthVersion); _anchor=DateTimeOffset.Now; ApplyVisibility(); await RefreshAsync(); }

    private async Task ToggleMonthAsync() { if (_monthVisible) { _monthVisible=false; ApplyVisibility(); await RefreshTodaySafeAsync(); return; } _visible=false; _monthVisible=true; Interlocked.Increment(ref _version); _monthAnchor=DateTimeOffset.Now; ApplyVisibility(); await RefreshMonthAsync(); }

    private async Task RefreshTodaySafeAsync() { try { await _refreshToday(CancellationToken.None); } catch (Exception ex) { _scene.SetStatus($"Plan Today could not refresh: {ex.Message}"); } }

    private void ApplyVisibility() { var any=_visible||_monthVisible; _todayViewport.SetValue(HavenProperties.Visibility,any?HavenVisibility.Collapsed:HavenVisibility.Visible); _root.SetValue(HavenProperties.Visibility,_visible?HavenVisibility.Visible:HavenVisibility.Collapsed); _monthRoot.SetValue(HavenProperties.Visibility,_monthVisible?HavenVisibility.Visible:HavenVisibility.Collapsed); _scene.DateLabel.SetValue(HavenProperties.Visibility,any?HavenVisibility.Collapsed:HavenVisibility.Visible); _scene.RefreshButton.SetValue(HavenProperties.Visibility,any?HavenVisibility.Collapsed:HavenVisibility.Visible); _scene.WeekButton.Content=_visible?"Today":"Week"; _scene.MonthButton.Content=_monthVisible?"Today":"Month"; _scene.WeekButton.Accessibility.AccessibleName=_visible?"Return to Plan Today":"Open Plan Week"; _scene.MonthButton.Accessibility.AccessibleName=_monthVisible?"Return to Plan Today":"Open Plan Month"; }

    private void Shift(int days)
    {
        var zone = TimeZoneInfo.Local;
        _anchor = LocalNoon(TimeZoneInfo.ConvertTime(_anchor, zone).Date.AddDays(days), zone);
        _ = RefreshAsync();
    }

    private void ShiftMonth(int months)
    {
        var zone = TimeZoneInfo.Local;
        var local = TimeZoneInfo.ConvertTime(_monthAnchor, zone).Date;
        _monthAnchor = LocalNoon(local.AddMonths(months), zone);
        _ = RefreshMonthAsync();
    }

    private async Task RefreshMonthAsync()
    {
        if (!_monthVisible) return;
        var version = Interlocked.Increment(ref _monthVersion);
        try
        {
            await _planner.EnsureDefaultsAsync(CancellationToken.None);
            var zone = TimeZoneInfo.Local;
            var local = TimeZoneInfo.ConvertTime(_monthAnchor, zone).Date;
            var monthStart = new DateTime(local.Year, local.Month, 1);
            var gridStart = monthStart.AddDays(-(((int)monthStart.DayOfWeek + 6) % 7));
            var (start, _) = PlannerDayTimeline.GetDayBounds(LocalNoon(gridStart, zone), zone.Id);
            var (_, end) = PlannerDayTimeline.GetDayBounds(LocalNoon(gridStart.AddDays(41), zone), zone.Id);            var tasksTask = _planner.GetTasksAsync(new PlannerTaskQuery(RangeStart: start, RangeEnd: end, IncludeCompleted: true), CancellationToken.None);
            var eventsTask = _planner.GetEventsAsync(start, end, null, CancellationToken.None);
            await Task.WhenAll(tasksTask, eventsTask);
            if (!_monthVisible || version != _monthVersion) return;
            var tasks = await tasksTask; var events = await eventsTask;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_monthVisible && version == _monthVersion) RenderMonth(monthStart, gridStart, tasks, events, zone);
            });
        }
        catch (Exception ex) { _scene.SetStatus($"Plan Month could not refresh: {ex.Message}"); }
    }
    private async Task RefreshAsync()
    {
        if (!_visible) return;
        var version = Interlocked.Increment(ref _version);
        SetLoading(true);
        try
        {
            await _planner.EnsureDefaultsAsync(CancellationToken.None);
            var now = DateTimeOffset.Now;
            var zone = TimeZoneInfo.Local;
            var local = TimeZoneInfo.ConvertTime(_anchor, zone).Date;
            var monday = local.AddDays(-(((int)local.DayOfWeek + 6) % 7));
            var snapshots = new List<PlannerDaySnapshot>(7);
            for (var i = 0; i < 7; i++)
                snapshots.Add(await _days.GetDayAsync(LocalNoon(monday.AddDays(i), zone), now, zone.Id, CancellationToken.None));

            var tasksTask = _planner.GetTasksAsync(
                new PlannerTaskQuery(RangeStart: snapshots[0].DayStart, RangeEnd: snapshots[^1].DayEnd, IncludeCompleted: true),
                CancellationToken.None);
            var subjectsTask = _containers.GetByModeAsync(HavenMode.Study, CancellationToken.None);
            await Task.WhenAll(tasksTask, subjectsTask);
            if (!_visible || version != _version) return;

            var links = new Dictionary<Guid, PlannerStudyLink>();
            foreach (var task in await tasksTask)
                if (PlannerStudyAssignmentTags.TryRead(task.TagsJson, out var link)) links[task.Id] = link;
            var subjects = (await subjectsTask).ToDictionary(x => x.Id, x => x.Name);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_visible || version != _version) return;
                Render(snapshots, zone, links, subjects, now);
                _scene.SetStatus(null);
                SetLoading(false);
            });
        }
        catch (Exception ex)
        {
            if (!_visible || version != _version) return;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _scene.SetStatus($"Plan week could not refresh: {ex.Message}");
                SetLoading(false);
            });
        }
    }

    private void Render(
        IReadOnlyList<PlannerDaySnapshot> days,
        TimeZoneInfo zone,
        IReadOnlyDictionary<Guid, PlannerStudyLink> links,
        IReadOnlyDictionary<Guid, string> subjects,
        DateTimeOffset now)
    {
        var first = TimeZoneInfo.ConvertTime(days[0].DayStart, zone);
        var last = TimeZoneInfo.ConvertTime(days[^1].DayStart, zone);
        _range.Content = first.Month == last.Month
            ? $"{first.Day}–{last.Day} {last:MMM yyyy}"
            : $"{first:d MMM}–{last:d MMM yyyy}";
        foreach (var child in _strip.Children.ToArray()) _strip.Remove(child);
        var today = TimeZoneInfo.ConvertTime(now, zone).Date;
        _strip.Add(WeekGrid(days, zone, links, subjects, today));
    }

    private void RenderMonth(DateTime monthStart, DateTime gridStart, IReadOnlyList<PlannerTask> tasks, IReadOnlyList<PlannerEvent> events, TimeZoneInfo zone)
    {
        _monthRange.Content = monthStart.ToString("MMMM yyyy");
        foreach (var child in _monthHost.Children.ToArray()) _monthHost.Remove(child);
        var grid = new HavenContainer { Name = "PlanMonthGrid", Layout = HavenLayout.Grid, Columns = "1fr 1fr 1fr 1fr 1fr 1fr 1fr", Rows = "Auto 112px 112px 112px 112px 112px 112px" };
        grid.SetValue(HavenProperties.MinWidth, HavenLength.Px(980));
        grid.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        grid.SetValue(HavenProperties.Background, "Surface");
        grid.SetValue(HavenProperties.BorderColor, "Border");
        grid.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        grid.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(16)));
        grid.SetValue(HavenProperties.Clip, true);
        var names = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
        for (var col = 0; col < 7; col++)
        {
            var header = new HavenText { Content = names[col], Level = TextLevel.Caption };
            header.SetValue(HavenProperties.Row, 0); header.SetValue(HavenProperties.Column, col); header.SetValue(HavenProperties.Padding, HavenThickness.Parse("9px 10px")); header.SetValue(HavenProperties.FontWeight, 700); grid.Add(header);
        }        var today = DateTime.Today;
        for (var index = 0; index < 42; index++)
        {
            var day = gridStart.AddDays(index); var cell = Vertical($"PlanMonthDay-{day:yyyyMMdd}", 4); Card(cell, 8, 0);
            cell.SetValue(HavenProperties.Row, 1 + index / 7); cell.SetValue(HavenProperties.Column, index % 7); cell.SetValue(HavenProperties.MinHeight, HavenLength.Px(112));
            if (day.Month != monthStart.Month) cell.SetValue(HavenProperties.Opacity, .55d);
            if (day == today) cell.SetValue(HavenProperties.BorderColor, "Accent");
            var open = Button($"PlanMonthOpen-{day:yyyyMMdd}", day.Day.ToString(), "calendar"); open.Accessibility.AccessibleName = $"Open week containing {day:dddd d MMMM}";
            open.Invoked += (_, _) => OpenWeekAt(day); cell.Add(open);
            var labels = MonthLabels(day, tasks, events, zone);
            foreach (var label in labels.Take(3)) { var text = new HavenText { Content = label, Level = TextLevel.Caption }; cell.Add(text); }
            if (labels.Count > 3)
            {
                var more = Button($"PlanMonthMore-{day:yyyyMMdd}", $"+{labels.Count - 3} more", "more-horizontal"); more.Invoked += (_, _) => OpenWeekAt(day); cell.Add(more);
            }
            grid.Add(cell);
        }
        _monthHost.Add(grid);
    }
    private static List<string> MonthLabels(DateTime day, IReadOnlyList<PlannerTask> tasks, IReadOnlyList<PlannerEvent> events, TimeZoneInfo zone)
    {
        var labels = new List<(DateTimeOffset Sort, string Label)>();
        foreach (var task in tasks)
        {
            var when = task.StartsAt ?? task.DueAt; if (when is null) continue; var local = TimeZoneInfo.ConvertTime(when.Value, zone); if (local.Date != day.Date) continue;
            labels.Add((local, $"{local:HH:mm} {task.Title}"));
        }
        var (dayStart, dayEnd) = PlannerDayTimeline.GetDayBounds(LocalNoon(day, zone), zone.Id);
        foreach (var item in events)
        {
            if (item.StartsAt >= dayEnd || item.EndsAt <= dayStart) continue; var local = TimeZoneInfo.ConvertTime(item.StartsAt, zone);
            labels.Add((local, item.IsAllDay ? $"All day {item.Title}" : $"{local:HH:mm} {item.Title}"));
        }
        return labels.OrderBy(x => x.Sort).ThenBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase).Select(x => x.Label).ToList();
    }

    private void OpenWeekAt(DateTime day)
    {
        _monthVisible = false; _visible = true; _anchor = LocalNoon(day, TimeZoneInfo.Local); Interlocked.Increment(ref _monthVersion); ApplyVisibility(); _ = RefreshAsync();
    }
    private HavenContainer WeekGrid(
        IReadOnlyList<PlannerDaySnapshot> days,
        TimeZoneInfo zone,
        IReadOnlyDictionary<Guid, PlannerStudyLink> links,
        IReadOnlyDictionary<Guid, string> subjects,
        DateTime today)
    {
        const int slotMinutes = 30;
        const int slotCount = 48;
        const int firstTimedRow = 2;
        var grid = new HavenContainer
        {
            Name = "PlanWeekTemporalGrid",
            Layout = HavenLayout.Grid,
            Columns = "64px 1fr 1fr 1fr 1fr 1fr 1fr 1fr",
            Rows = $"Auto Auto {string.Join(' ', Enumerable.Repeat("32px", slotCount))}"
        };
        grid.SetValue(HavenProperties.MinWidth, HavenLength.Px(1060));
        grid.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        grid.SetValue(HavenProperties.Background, "Surface");
        grid.SetValue(HavenProperties.BorderColor, "Border");
        grid.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        grid.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(16)));
        grid.SetValue(HavenProperties.Clip, true);

        var corner = Muted("Time");
        corner.SetValue(HavenProperties.Row, 0);
        corner.SetValue(HavenProperties.Column, 0);
        corner.SetValue(HavenProperties.Padding, HavenThickness.Parse("10px 8px"));
        grid.Add(corner);

        var allDayLabel = Muted("All day");
        allDayLabel.SetValue(HavenProperties.Row, 1);
        allDayLabel.SetValue(HavenProperties.Column, 0);
        allDayLabel.SetValue(HavenProperties.Padding, HavenThickness.Parse("10px 8px"));
        grid.Add(allDayLabel);

        for (var dayIndex = 0; dayIndex < days.Count; dayIndex++)
        {
            var day = days[dayIndex];
            var localDay = TimeZoneInfo.ConvertTime(day.DayStart, zone);
            var column = dayIndex + 1;
            var header = Vertical($"PlanWeekHeader-{localDay:yyyyMMdd}", 2);
            header.SetValue(HavenProperties.Row, 0);
            header.SetValue(HavenProperties.Column, column);
            header.SetValue(HavenProperties.Padding, HavenThickness.Parse("9px 8px"));
            header.SetValue(HavenProperties.BorderColor, "Border");
            header.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
            if (localDay.Date == today) header.SetValue(HavenProperties.Background, "AccentSecondary");
            var weekday = new HavenText { Content = localDay.ToString("ddd"), Level = TextLevel.Caption };
            weekday.SetValue(HavenProperties.FontWeight, 800);
            weekday.SetValue(HavenProperties.Foreground, localDay.Date == today ? "Accent" : "TextSecondary");
            header.Add(weekday);
            header.Add(new HavenText { Content = localDay.ToString("d MMM"), Level = TextLevel.H3 });
            grid.Add(header);

            var allDay = Vertical($"PlanWeekAllDay-{localDay:yyyyMMdd}", 4);
            allDay.SetValue(HavenProperties.Row, 1);
            allDay.SetValue(HavenProperties.Column, column);
            allDay.SetValue(HavenProperties.MinHeight, HavenLength.Px(44));
            allDay.SetValue(HavenProperties.Padding, HavenThickness.Parse("5px"));
            allDay.SetValue(HavenProperties.BorderColor, "Border");
            allDay.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
            foreach (var item in day.Items.Where(IsUntimedOrAllDay))
                allDay.Add(CompactAgendaItem(item, zone, links, subjects));
            grid.Add(allDay);
        }

        for (var hour = 0; hour < 24; hour++)
        {
            var row = firstTimedRow + hour * 2;
            var label = Muted($"{hour:00}:00");
            label.SetValue(HavenProperties.Row, row);
            label.SetValue(HavenProperties.RowSpan, 2);
            label.SetValue(HavenProperties.Column, 0);
            label.SetValue(HavenProperties.Padding, HavenThickness.Parse("5px 8px"));
            label.SetValue(HavenProperties.BorderColor, "Border");
            label.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
            grid.Add(label);

            for (var dayIndex = 0; dayIndex < days.Count; dayIndex++)
            {
                var cell = new HavenContainer { Name = $"PlanWeekHour-{dayIndex}-{hour}", Layout = HavenLayout.Vertical };
                cell.SetValue(HavenProperties.Row, row);
                cell.SetValue(HavenProperties.RowSpan, 2);
                cell.SetValue(HavenProperties.Column, dayIndex + 1);
                cell.SetValue(HavenProperties.BorderColor, "Border");
                cell.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
                cell.SetValue(HavenProperties.Background, "Surface");
                grid.Add(cell);
            }
        }

        for (var dayIndex = 0; dayIndex < days.Count; dayIndex++)
        {
            var day = days[dayIndex];
            var localDay = TimeZoneInfo.ConvertTime(day.DayStart, zone);
            foreach (var item in day.Items.Where(item => !IsUntimedOrAllDay(item)))
            {
                var placement = Place(item, localDay, zone, slotMinutes, slotCount);
                var eventCard = TimedItem(item, localDay.Date, zone, links, subjects, placement.Span);
                eventCard.SetValue(HavenProperties.Row, firstTimedRow + placement.Start);
                eventCard.SetValue(HavenProperties.RowSpan, placement.Span);
                eventCard.SetValue(HavenProperties.Column, dayIndex + 1);
                eventCard.SetValue(HavenProperties.Margin, HavenThickness.Parse("2px 3px"));
                eventCard.SetValue(HavenProperties.ZIndex, 3);
                grid.Add(eventCard);
            }
        }

        return grid;
    }

    private HavenContainer CompactAgendaItem(
        PlannerDayItem item,
        TimeZoneInfo zone,
        IReadOnlyDictionary<Guid, PlannerStudyLink> links,
        IReadOnlyDictionary<Guid, string> subjects)
    {
        var row = Horizontal($"PlanWeekAllDayItem-{item.Kind}-{item.EntityId:N}", 4);
        row.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        row.SetValue(HavenProperties.Padding, HavenThickness.Parse("5px 7px"));
        row.SetValue(HavenProperties.Background, "SurfaceSecondary");
        row.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(8)));
        var title = new HavenText { Content = item.Title, Level = TextLevel.Caption };
        title.SetValue(HavenProperties.FontWeight, 700);
        row.Add(title);
        if (item.Kind == PlannerDayItemKind.Task && item.IsActionable)
        {
            var done = Button("Complete", "Done", "check");
            done.Accessibility.AccessibleName = $"Complete {item.Title}";
            done.Invoked += async (_, _) => await CompleteAsync(item.EntityId);
            row.Add(done);
        }
        if (item.Kind == PlannerDayItemKind.Task && links.TryGetValue(item.EntityId, out var study))
        {
            var subject = subjects.GetValueOrDefault(study.SubjectId);
            var open = Button("Study", string.IsNullOrWhiteSpace(subject) ? "Study" : subject, "book");
            open.Invoked += (_, _) => _openStudy(study);
            row.Add(open);
        }
        return row;
    }

    private HavenContainer TimedItem(
        PlannerDayItem item,
        DateTime localDay,
        TimeZoneInfo zone,
        IReadOnlyDictionary<Guid, PlannerStudyLink> links,
        IReadOnlyDictionary<Guid, string> subjects,
        int span)
    {
        var card = Vertical($"PlanWeekTimed-{item.Kind}-{item.EntityId:N}-{localDay:yyyyMMdd}", 2);
        card.SetValue(HavenProperties.Padding, HavenThickness.Parse(span >= 2 ? "6px 7px" : "3px 6px"));
        card.SetValue(HavenProperties.Background, item.Kind == PlannerDayItemKind.Event ? "AccentSecondary" : "SurfaceRaised");
        card.SetValue(HavenProperties.BorderColor, item.Kind == PlannerDayItemKind.Event ? "Accent" : "Border");
        card.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(8)));
        if (item.IsCompleted) card.SetValue(HavenProperties.Opacity, .62d);

        var title = new HavenText { Content = item.Title, Level = TextLevel.Caption };
        title.SetValue(HavenProperties.FontWeight, 750);
        card.Add(title);
        if (span >= 2) card.Add(Muted(Time(item, zone)));
        if (span >= 3)
        {
            var actions = Horizontal(null, 4);
            if (item.Kind == PlannerDayItemKind.Task && item.IsActionable)
            {
                var done = Button("Complete", "Done", "check");
                done.Accessibility.AccessibleName = $"Complete {item.Title}";
                done.Invoked += async (_, _) => await CompleteAsync(item.EntityId);
                actions.Add(done);
            }
            if (item.Kind == PlannerDayItemKind.Task && links.TryGetValue(item.EntityId, out var study))
            {
                var subject = subjects.GetValueOrDefault(study.SubjectId);
                var open = Button("Study", string.IsNullOrWhiteSpace(subject) ? "Study" : subject, "book");
                open.Invoked += (_, _) => _openStudy(study);
                actions.Add(open);
            }
            if (actions.Children.Count > 0) card.Add(actions);
        }
        return card;
    }

    private static bool IsUntimedOrAllDay(PlannerDayItem item) =>
        item.IsAllDay || (item.StartsAt is null && item.DueAt is null);

    private static (int Start, int Span) Place(
        PlannerDayItem item,
        DateTimeOffset localDay,
        TimeZoneInfo zone,
        int slotMinutes,
        int slotCount)
    {
        var dayStart = localDay.Date;
        var startValue = item.StartsAt ?? item.DueAt ?? item.EndsAt ?? localDay;
        var localStart = TimeZoneInfo.ConvertTime(startValue, zone).DateTime;
        var localEnd = item.EndsAt is null
            ? localStart.AddMinutes(slotMinutes)
            : TimeZoneInfo.ConvertTime(item.EndsAt.Value, zone).DateTime;
        var startMinutes = Math.Clamp((localStart - dayStart).TotalMinutes, 0, 24 * 60 - slotMinutes);
        var endMinutes = Math.Clamp((localEnd - dayStart).TotalMinutes, startMinutes + slotMinutes, 24 * 60);
        var start = Math.Clamp((int)Math.Floor(startMinutes / slotMinutes), 0, slotCount - 1);
        var end = Math.Clamp((int)Math.Ceiling(endMinutes / slotMinutes), start + 1, slotCount);
        return (start, Math.Max(1, end - start));
    }

    private HavenContainer DayCard(
        PlannerDaySnapshot day,
        TimeZoneInfo zone,
        IReadOnlyDictionary<Guid, PlannerStudyLink> links,
        IReadOnlyDictionary<Guid, string> subjects,
        DateTime today)
    {
        var local = TimeZoneInfo.ConvertTime(day.DayStart, zone);
        var card = Vertical($"PlanWeekDay-{local:yyyyMMdd}", 8);
        card.SetValue(HavenProperties.Width, HavenLength.Px(220));
        card.SetValue(HavenProperties.MinWidth, HavenLength.Px(220));
        Card(card, 12, 16);
        var weekday = new HavenText { Content = local.ToString("ddd"), Level = TextLevel.Caption };
        weekday.SetValue(HavenProperties.FontWeight, 800);
        weekday.SetValue(HavenProperties.Foreground, local.Date == today ? "Accent" : "TextSecondary");
        card.Add(weekday);
        card.Add(new HavenText { Content = local.ToString("d MMMM"), Level = TextLevel.H3 });
        if (day.Items.Count == 0)
        {
            card.Add(Muted("Nothing planned."));
            return card;
        }
        foreach (var item in day.Items) card.Add(Item(item, local, zone, links, subjects));
        return card;
    }

    private HavenContainer Item(
        PlannerDayItem item,
        DateTimeOffset localDay,
        TimeZoneInfo zone,
        IReadOnlyDictionary<Guid, PlannerStudyLink> links,
        IReadOnlyDictionary<Guid, string> subjects)
    {
        var row = Vertical($"PlanWeekItem-{item.Kind}-{item.EntityId:N}-{localDay:yyyyMMdd}", 4);
        row.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Card(row, 9, 12);
        if (item.IsCompleted) row.SetValue(HavenProperties.Opacity, .62d);
        row.Add(Muted(Time(item, zone)));
        row.Add(new HavenText { Content = item.Title, Level = TextLevel.Paragraph });

        PlannerStudyLink? study = item.Kind == PlannerDayItemKind.Task && links.TryGetValue(item.EntityId, out var link) ? link : null;
        var subject = study is null ? null : subjects.GetValueOrDefault(study.SubjectId);
        var meta = item.IsCompleted ? "Completed" : item.IsCancelled ? "Cancelled"
            : item.Kind == PlannerDayItemKind.Event ? item.IsReadOnly ? "Calendar · read only" : "Calendar event" : "Task";
        if (study is not null) meta += $" · Study{(string.IsNullOrWhiteSpace(subject) ? "" : $" · {subject}")}";
        row.Add(Muted(meta));

        if ((item.Kind == PlannerDayItemKind.Task && item.IsActionable) || study is not null)
        {
            var actions = Horizontal(null, 5);
            if (item.Kind == PlannerDayItemKind.Task && item.IsActionable)
            {
                var done = Button("Complete", "Done", "check");
                done.Accessibility.AccessibleName = $"Complete {item.Title}";
                done.Invoked += async (_, _) => await CompleteAsync(item.EntityId);
                actions.Add(done);
            }
            if (study is not null)
            {
                var open = Button("Study", string.IsNullOrWhiteSpace(subject) ? "Study" : subject, "book");
                open.Accessibility.AccessibleName = $"Open Study for {item.Title}";
                var target = study;
                open.Invoked += (_, _) => _openStudy(target);
                actions.Add(open);
            }
            row.Add(actions);
        }
        return row;
    }

    private async Task CompleteAsync(Guid id)
    {
        try
        {
            var task = await _planner.GetTaskAsync(id, CancellationToken.None);
            if (task is null || task.Status is PlannerTaskStatus.Completed or PlannerTaskStatus.Cancelled) return;
            await _planner.CompleteTaskAsync(id, DateTimeOffset.Now, CancellationToken.None);
            await RefreshAsync();
        }
        catch (Exception ex) { _scene.SetStatus($"Could not complete task: {ex.Message}"); }
    }

    private void SetLoading(bool value)
    {
        foreach (var button in new[] { _previous, _thisWeek, _next, _refresh })
            button.SetValue(HavenProperties.Enabled, !value);
    }

    private static HavenContainer Vertical(string? name, double gap)
    {
        var value = new HavenContainer { Name = name, Layout = HavenLayout.Vertical };
        value.SetValue(HavenProperties.Gap, HavenLength.Px(gap));
        return value;
    }

    private static HavenContainer Horizontal(string? name, double gap)
    {
        var value = new HavenContainer { Name = name, Layout = HavenLayout.Horizontal };
        value.SetValue(HavenProperties.Gap, HavenLength.Px(gap));
        value.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        return value;
    }

    private static void Card(HavenContainer value, double padding, double radius)
    {
        value.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(padding)));
        value.SetValue(HavenProperties.Background, "Surface");
        value.SetValue(HavenProperties.BorderColor, "Border");
        value.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        value.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(radius)));
    }

    private static HavenText Muted(string value)
    {
        var text = new HavenText { Content = value, Level = TextLevel.Caption };
        text.SetValue(HavenProperties.Foreground, "TextSecondary");
        return text;
    }

    private static HavenButton Button(string name, string content, string icon) =>
        new() { Name = name, Content = content, IconKey = icon, Variant = ButtonVariant.Ghost };

    private static string Time(PlannerDayItem item, TimeZoneInfo zone)
    {
        if (item.IsAllDay) return "All day";
        if (item.StartsAt is not null && item.EndsAt is not null)
        {
            var start = TimeZoneInfo.ConvertTime(item.StartsAt.Value, zone);
            var end = TimeZoneInfo.ConvertTime(item.EndsAt.Value, zone);
            return start.Date == end.Date ? $"{start:HH:mm}–{end:HH:mm}" : $"{start:HH:mm}–{end:ddd HH:mm}";
        }
        if (item.StartsAt is not null) return TimeZoneInfo.ConvertTime(item.StartsAt.Value, zone).ToString("HH:mm");
        return item.DueAt is null ? "Any time" : $"Due {TimeZoneInfo.ConvertTime(item.DueAt.Value, zone):HH:mm}";
    }

    private static DateTimeOffset LocalNoon(DateTime date, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(date.Date.AddHours(12), DateTimeKind.Unspecified);
        var offset = zone.GetUtcOffset(local);
        if (zone.IsAmbiguousTime(local)) offset = zone.GetAmbiguousTimeOffsets(local).Max();
        return new DateTimeOffset(local, offset);
    }
}
