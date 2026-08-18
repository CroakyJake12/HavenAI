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
    private readonly HavenText _range;
    private readonly HavenButton _previous;
    private readonly HavenButton _thisWeek;
    private readonly HavenButton _next;
    private readonly HavenButton _refresh;
    private DateTimeOffset _anchor = DateTimeOffset.Now;
    private int _version;
    private bool _visible;

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
        scene.Root.Add(_root);

        scene.FullPlannerRequested += (_, _) => _ = ToggleAsync();
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

    private async Task ToggleAsync()
    {
        _visible = !_visible;
        Interlocked.Increment(ref _version);
        ApplyVisibility();
        if (_visible)
        {
            _anchor = DateTimeOffset.Now;
            await RefreshAsync();
        }
        else
        {
            try { await _refreshToday(CancellationToken.None); }
            catch (Exception ex) { _scene.SetStatus($"Plan Today could not refresh: {ex.Message}"); }
        }
    }

    private void ApplyVisibility()
    {
        _todayViewport.SetValue(HavenProperties.Visibility, _visible ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        _root.SetValue(HavenProperties.Visibility, _visible ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        _scene.DateLabel.SetValue(HavenProperties.Visibility, _visible ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        _scene.RefreshButton.SetValue(HavenProperties.Visibility, _visible ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        _scene.FullPlannerButton.Content = _visible ? "Today" : "Full planner";
        _scene.FullPlannerButton.Accessibility.AccessibleName = _visible ? "Return to Plan Today" : "Open full planner";
    }

    private void Shift(int days)
    {
        var zone = TimeZoneInfo.Local;
        _anchor = LocalNoon(TimeZoneInfo.ConvertTime(_anchor, zone).Date.AddDays(days), zone);
        _ = RefreshAsync();
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
        foreach (var day in days) _strip.Add(DayCard(day, zone, links, subjects, today));
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
