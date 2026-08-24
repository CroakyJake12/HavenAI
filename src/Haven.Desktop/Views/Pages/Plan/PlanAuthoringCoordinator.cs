using Haven.Application;
using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenContainer = Haven.UI.Components.Container;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Plan;

internal sealed class PlanAuthoringCoordinator : IDisposable
{
    private enum EditorKind { Task, Event }
    private readonly PlanHavenScene _scene;
    private readonly IPlannerRepository _planner;
    private readonly Func<CancellationToken, Task> _refresh;
    private readonly HavenContainer _root;
    private readonly HavenText _heading = new() { Name = "PlanEditorHeading", Level = TextLevel.H2 };
    private readonly HavenText _date = new() { Name = "PlanEditorDate" };
    private readonly HavenText _time = new() { Name = "PlanEditorTime" };
    private readonly HavenText _duration = new() { Name = "PlanEditorDuration" };
    private readonly HavenText _recurrence = new() { Name = "PlanEditorRecurrence" };
    private readonly HavenText _priority = new() { Name = "PlanEditorPriority" };
    private readonly HavenText _destination = new() { Name = "PlanEditorDestination" };
    private readonly HavenText _status = new() { Name = "PlanEditorStatus" };
    private readonly Input _title = new() { Name = "PlanEditorTitle", Placeholder = "Title" };
    private readonly Input _notes = new() { Name = "PlanEditorNotes", Placeholder = "Notes (optional)", Multiline = true };
    private readonly Input _location = new() { Name = "PlanEditorLocation", Placeholder = "Location (optional)" };
    private readonly HavenButton _save;
    private readonly HavenButton _delete;
    private readonly HavenButton _allDay;
    private readonly HavenButton _recurrenceButton;
    private readonly HavenButton _priorityButton;
    private readonly HavenButton _destinationButton;
    private EditorKind _kind;
    private PlannerTask? _task;
    private PlannerEvent? _event;
    private IReadOnlyList<PlannerCollection> _collections = [];
    private IReadOnlyList<PlannerCalendar> _calendars = [];
    private Guid _destinationId;
    private DateTimeOffset _localStart;
    private int _durationMinutes = 60;
    private string? _recurrenceRule;
    private PlannerPriority _taskPriority;
    private bool _allDayValue;
    private bool _deleteArmed;
    private bool _busy;
    private bool _disposed;

    public PlanAuthoringCoordinator(PlanHavenScene scene, IPlannerRepository planner, Func<CancellationToken, Task> refresh)
    {
        _scene = scene; _planner = planner; _refresh = refresh;
        var host = (HavenContainer)scene.Root.DescendantsAndSelf().Single(x => x.Name == "AuthoringHost");
        _root = BuildRoot(); host.Add(_root);
        _save = FindButton("PlanEditorSave"); _delete = FindButton("PlanEditorDelete");
        _allDay = FindButton("PlanEditorAllDay"); _recurrenceButton = FindButton("PlanEditorRecurrenceButton");
        _priorityButton = FindButton("PlanEditorPriorityButton"); _destinationButton = FindButton("PlanEditorDestinationButton");
        scene.NewTaskRequested += OnNewTask; scene.NewEventRequested += OnNewEvent; scene.EditItemRequested += OnEditItem;
        WireButtons(); Hide();
    }
    private void WireButtons()
    {
        FindButton("PlanEditorPreviousDay").Invoked += (_, _) => ShiftDay(-1);
        FindButton("PlanEditorToday").Invoked += (_, _) => SetDay(DateTime.Today);
        FindButton("PlanEditorTomorrow").Invoked += (_, _) => SetDay(DateTime.Today.AddDays(1));
        FindButton("PlanEditorNextDay").Invoked += (_, _) => ShiftDay(1);
        FindButton("PlanEditorEarlier").Invoked += (_, _) => ShiftTime(-30);
        FindButton("PlanEditorLater").Invoked += (_, _) => ShiftTime(30);
        FindButton("PlanEditorShorter").Invoked += (_, _) => ChangeDuration(-30);
        FindButton("PlanEditorLonger").Invoked += (_, _) => ChangeDuration(30);
        _allDay.Invoked += (_, _) => { _allDayValue = !_allDayValue; UpdateLabels(); };
        _recurrenceButton.Invoked += (_, _) => { CycleRecurrence(); UpdateLabels(); };
        _priorityButton.Invoked += (_, _) => { CyclePriority(); UpdateLabels(); };
        _destinationButton.Invoked += (_, _) => { CycleDestination(); UpdateLabels(); };
        _save.Invoked += async (_, _) => await SaveAsync();
        _delete.Invoked += async (_, _) => await DeleteAsync();
        FindButton("PlanEditorCancel").Invoked += (_, _) => Hide();
    }

    private async void OnNewTask(object? sender, EventArgs e) => await OpenNewAsync(EditorKind.Task);
    private async void OnNewEvent(object? sender, EventArgs e) => await OpenNewAsync(EditorKind.Event);
    private async void OnEditItem(object? sender, PlanItemEditRequest request) => await OpenItemAsync(request);

    internal async Task OpenItemAsync(PlanItemEditRequest request)
    {
        try
        {
            string? warning = null;
            if (request.Kind == PlannerDayItemKind.Task)
            {
                var task = await _planner.GetTaskAsync(request.EntityId, CancellationToken.None);
                if (task is not null)
                {
                    warning = await LoadDestinationsAsync(EditorKind.Task, task.CollectionId);
                    OpenTask(task);
                }
            }
            else
            {
                var item = await _planner.GetEventAsync(request.EntityId, CancellationToken.None);
                if (item is not null)
                {
                    warning = await LoadDestinationsAsync(EditorKind.Event, item.CalendarId);
                    OpenEvent(item);
                }
            }
            if (warning is not null) SetStatus(warning);
        }
        catch (Exception ex) { SetStatus($"Could not open item: {ex.Message}"); }
    }

    private async Task OpenNewAsync(EditorKind kind)
    {
        _kind = kind; _task = null; _event = null; _title.Text = string.Empty; _notes.Text = string.Empty; _location.Text = string.Empty;
        _durationMinutes = 60; _recurrenceRule = null; _taskPriority = PlannerPriority.None; _allDayValue = false;
        var preferred = kind == EditorKind.Task ? PlannerDefaults.PersonalCollectionId : PlannerDefaults.LocalCalendarId;
        var warning = await LoadDestinationsAsync(kind, preferred);
        var now = DateTimeOffset.Now.AddMinutes(30);
        var minute = now.Minute < 30 ? 30 : 0;
        var hour = minute == 0 ? now.AddHours(1).Hour : now.Hour;
        _localStart = AtLocal(now.Date, new TimeSpan(hour, minute, 0));
        Show();
        if (warning is not null) SetStatus(warning);
    }

    private async Task<string?> LoadDestinationsAsync(EditorKind kind, Guid preferredId)
    {
        _destinationId = preferredId;
        try
        {
            await _planner.EnsureDefaultsAsync(CancellationToken.None);
            if (kind == EditorKind.Task)
            {
                _collections = (await _planner.GetCollectionsAsync(false, CancellationToken.None))
                    .OrderBy(x => x.SortOrder).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
                if (_collections.All(x => x.Id != _destinationId))
                    _destinationId = _collections.FirstOrDefault()?.Id ?? PlannerDefaults.PersonalCollectionId;
            }
            else
            {
                _calendars = (await _planner.GetCalendarsAsync(true, CancellationToken.None))
                    .OrderBy(x => x.Provider).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
                var writable = WritableCalendars();
                if (writable.All(x => x.Id != _destinationId))
                    _destinationId = writable.FirstOrDefault()?.Id ?? PlannerDefaults.LocalCalendarId;
            }
            return null;
        }
        catch (Exception ex)
        {
            _collections = []; _calendars = [];
            return $"Could not load task lists or calendars: {ex.Message}";
        }
    }

    private void OpenTask(PlannerTask task)
    {
        _kind = EditorKind.Task; _task = task; _event = null; _destinationId = task.CollectionId; _title.Text = task.Title; _notes.Text = task.Notes; _location.Text = string.Empty;
        _localStart = TimeZoneInfo.ConvertTime(task.DueAt ?? task.StartsAt ?? DateTimeOffset.Now, TimeZoneInfo.Local);
        _durationMinutes = Math.Max(30, task.EstimatedMinutes ?? 60); _recurrenceRule = task.RecurrenceRule; _taskPriority = task.Priority; _allDayValue = false; Show();
    }

    private void OpenEvent(PlannerEvent item)
    {
        _kind = EditorKind.Event; _event = item; _task = null; _destinationId = item.CalendarId; _title.Text = item.Title; _notes.Text = item.Notes; _location.Text = item.Location;
        _localStart = TimeZoneInfo.ConvertTime(item.StartsAt, TimeZoneInfo.Local); _durationMinutes = Math.Max(30, (int)Math.Round((item.EndsAt - item.StartsAt).TotalMinutes));
        _recurrenceRule = item.RecurrenceRule; _allDayValue = item.IsAllDay; Show();
    }
    private async Task SaveAsync()
    {
        if (_busy) return;
        var title = _title.Text.Trim();
        if (string.IsNullOrWhiteSpace(title)) { SetStatus("Add a title before saving."); return; }
        if (_event?.IsReadOnly == true) { SetStatus("This calendar event is read-only."); return; }
        SetBusy(true);
        try
        {
            await _planner.EnsureDefaultsAsync(CancellationToken.None);
            var now = DateTimeOffset.UtcNow;
            if (_kind == EditorKind.Task)
            {
                var collectionId = _collections.Any(x => x.Id == _destinationId) ? _destinationId : PlannerDefaults.PersonalCollectionId;
                var task = _task is null
                    ? new PlannerTask(Guid.NewGuid(), collectionId, null, title, _notes.Text.Trim(), _taskPriority, PlannerTaskStatus.Planned, "[]", _durationMinutes, null, _localStart, _recurrenceRule, null, null, 0, now, now, TimeZoneInfo.Local.Id)
                    : _task with { CollectionId = collectionId, Title = title, Notes = _notes.Text.Trim(), Priority = _taskPriority, EstimatedMinutes = _durationMinutes, DueAt = _localStart, RecurrenceRule = _recurrenceRule, UpdatedAt = now, TimeZoneId = TimeZoneInfo.Local.Id };
                await _planner.UpsertTaskAsync(task, CancellationToken.None);
            }
            else
            {
                var start = _allDayValue ? AtLocal(_localStart.Date, TimeSpan.Zero) : _localStart;
                var end = _allDayValue ? AtLocal(_localStart.Date.AddDays(1), TimeSpan.Zero) : start.AddMinutes(_durationMinutes);
                var writable = WritableCalendars();
                var calendarId = writable.Any(x => x.Id == _destinationId) ? _destinationId : PlannerDefaults.LocalCalendarId;
                var item = _event is null
                    ? new PlannerEvent(Guid.NewGuid(), calendarId, title, _notes.Text.Trim(), _location.Text.Trim(), start, end, _allDayValue, _recurrenceRule, null, false, null, null, now, now, null, TimeZoneInfo.Local.Id)
                    : _event with { Title = title, Notes = _notes.Text.Trim(), Location = _location.Text.Trim(), StartsAt = start, EndsAt = end, IsAllDay = _allDayValue, RecurrenceRule = _recurrenceRule, UpdatedAt = now, TimeZoneId = TimeZoneInfo.Local.Id };
                await _planner.UpsertEventAsync(item, CancellationToken.None);
            }
            await _refresh(CancellationToken.None);
            Hide();
        }
        catch (Exception ex) { SetStatus($"Could not save: {ex.Message}"); }
        finally { SetBusy(false); }
    }

    private async Task DeleteAsync()
    {
        if (_busy || (_task is null && _event is null)) return;
        if (_event?.IsReadOnly == true) { SetStatus("This calendar event is read-only."); return; }
        if (!_deleteArmed)
        {
            _deleteArmed = true;
            _delete.Content = _task is not null ? "Confirm delete task" : "Confirm delete event";
            SetStatus("Delete is permanent. Select confirm delete to continue.");
            return;
        }
        SetBusy(true);
        try
        {
            if (_task is not null) await _planner.DeleteTaskAsync(_task.Id, CancellationToken.None);
            else if (_event is not null) await _planner.DeleteEventAsync(_event.Id, DateTimeOffset.UtcNow, CancellationToken.None);
            await _refresh(CancellationToken.None); Hide();
        }
        catch (Exception ex) { ResetDeleteConfirmation(); SetStatus($"Could not delete: {ex.Message}"); }
        finally { SetBusy(false); }
    }
    private void Show()
    {
        _root.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
        _heading.Content = _kind == EditorKind.Task ? (_task is null ? "New task" : "Task details") : (_event is null ? "New event" : "Event details");
        var eventMode = _kind == EditorKind.Event;
        _location.SetValue(HavenProperties.Visibility, eventMode ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        _allDay.SetValue(HavenProperties.Visibility, eventMode ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        _priorityButton.SetValue(HavenProperties.Visibility, eventMode ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        _priority.SetValue(HavenProperties.Visibility, eventMode ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        _delete.SetValue(HavenProperties.Visibility, _task is not null || _event is not null ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        ResetDeleteConfirmation();
        SetStatus(_event?.IsReadOnly == true ? "This calendar event is read-only." : null);
        SetBusy(false); UpdateLabels();
    }

    private void Hide()
    {
        _root.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed); _task = null; _event = null; _busy = false; ResetDeleteConfirmation(); SetStatus(null);
    }

    private void UpdateLabels()
    {
        _date.Content = _localStart.ToString("dddd d MMMM yyyy");
        _time.Content = _allDayValue && _kind == EditorKind.Event ? "All day" : _localStart.ToString("HH:mm");
        _duration.Content = _kind == EditorKind.Task ? $"Estimated time: {FormatDuration(_durationMinutes)}" : $"Duration: {FormatDuration(_durationMinutes)}";
        _recurrence.Content = $"Repeats: {RecurrenceLabel()}"; _priority.Content = $"Priority: {_taskPriority}";
        _destination.Content = _kind == EditorKind.Task ? $"List: {DestinationName()}" : $"Calendar: {DestinationName()}";
        _allDay.Content = _allDayValue ? "All day: On" : "All day: Off";
        _recurrenceButton.Content = "Change repeat"; _priorityButton.Content = "Change priority";
        _destinationButton.Content = _kind == EditorKind.Task ? "Change list" : "Change calendar";
        var timeControls = _root.DescendantsAndSelf().Single(x => x.Name == "PlanEditorTimeControls");
        timeControls.SetValue(HavenProperties.Visibility, _allDayValue && _kind == EditorKind.Event ? HavenVisibility.Collapsed : HavenVisibility.Visible);
    }

    private void ResetDeleteConfirmation()
    {
        _deleteArmed = false;
        _delete.Content = "Delete";
    }

    private void ShiftDay(int days) => SetDay(_localStart.Date.AddDays(days));
    private void SetDay(DateTime day) { _localStart = AtLocal(day, _localStart.TimeOfDay); UpdateLabels(); }
    private void ShiftTime(int minutes)
    {
        var value = _localStart.Date + _localStart.TimeOfDay + TimeSpan.FromMinutes(minutes); _localStart = AtLocal(value.Date, value.TimeOfDay); UpdateLabels();
    }
    private void ChangeDuration(int minutes) { _durationMinutes = Math.Clamp(_durationMinutes + minutes, 30, 1440); UpdateLabels(); }

    private void CycleDestination()
    {
        if (_kind == EditorKind.Task)
        {
            CycleDestination(_collections.Select(x => x.Id).ToArray());
            return;
        }
        if (_event is null) CycleDestination(WritableCalendars().Select(x => x.Id).ToArray());
    }

    private void CycleDestination(Guid[] ids)
    {
        if (ids.Length < 2) return;
        var index = Array.IndexOf(ids, _destinationId);
        _destinationId = ids[(index + 1 + ids.Length) % ids.Length];
    }

    private string DestinationName()
    {
        if (_kind == EditorKind.Task)
            return _collections.FirstOrDefault(x => x.Id == _destinationId)?.Name ?? "Personal";
        return _calendars.FirstOrDefault(x => x.Id == _destinationId)?.Name ?? "Local calendar";
    }

    private PlannerCalendar[] WritableCalendars() => _calendars
        .Where(x => x.Permission is CalendarPermission.Owner or CalendarPermission.Writer)
        .ToArray();

    private void CyclePriority() => _taskPriority = _taskPriority switch
    {
        PlannerPriority.None => PlannerPriority.Low, PlannerPriority.Low => PlannerPriority.Medium, PlannerPriority.Medium => PlannerPriority.High,
        PlannerPriority.High => PlannerPriority.Urgent, _ => PlannerPriority.None
    };

    private void CycleRecurrence()
    {
        _recurrenceRule = _recurrenceRule switch
        {
            null or "" => "FREQ=DAILY", "FREQ=DAILY" => $"FREQ=WEEKLY;BYDAY={DayCode(_localStart.DayOfWeek)}",
            var value when value.StartsWith("FREQ=WEEKLY", StringComparison.OrdinalIgnoreCase) => "FREQ=MONTHLY", _ => null
        };
    }

    private string RecurrenceLabel() => _recurrenceRule switch
    {
        null or "" => "Does not repeat", "FREQ=DAILY" => "Daily", "FREQ=MONTHLY" => "Monthly",
        var value when value.StartsWith("FREQ=WEEKLY", StringComparison.OrdinalIgnoreCase) => "Weekly", _ => "Custom recurrence"
    };
    private HavenContainer BuildRoot()
    {
        var root = Vertical("PlanEditorRoot", 10); root.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        root.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(16))); root.SetValue(HavenProperties.Background, "SurfaceRaised");
        root.SetValue(HavenProperties.BorderColor, "Border"); root.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1)); root.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(18)));
        root.Add(_heading); StyleInput(_title, 42); StyleInput(_notes, 92); StyleInput(_location, 42); root.Add(_title); root.Add(_notes); root.Add(_location);

        var destinationRow = Horizontal("PlanEditorDestinationControls", 8);
        destinationRow.Add(_destination); destinationRow.Add(Button("PlanEditorDestinationButton", "Change list", "chevron-right")); root.Add(destinationRow);

        var dateRow = Horizontal("PlanEditorDateControls", 8);
        dateRow.Add(Button("PlanEditorPreviousDay", "Previous day", "chevron-left")); dateRow.Add(_date);
        dateRow.Add(Button("PlanEditorToday", "Today", "calendar")); dateRow.Add(Button("PlanEditorTomorrow", "Tomorrow", "calendar"));
        dateRow.Add(Button("PlanEditorNextDay", "Next day", "chevron-right")); root.Add(dateRow);

        var timeRow = Horizontal("PlanEditorTimeControls", 8);
        timeRow.Add(Button("PlanEditorEarlier", "-30 min", "minus")); timeRow.Add(_time); timeRow.Add(Button("PlanEditorLater", "+30 min", "plus")); root.Add(timeRow);
        var durationRow = Horizontal("PlanEditorDurationControls", 8);
        durationRow.Add(Button("PlanEditorShorter", "-30 min", "minus")); durationRow.Add(_duration); durationRow.Add(Button("PlanEditorLonger", "+30 min", "plus")); root.Add(durationRow);

        var repeatRow = Horizontal("PlanEditorRepeatControls", 8); repeatRow.Add(_recurrence); repeatRow.Add(Button("PlanEditorRecurrenceButton", "Change repeat", "repeat"));
        repeatRow.Add(Button("PlanEditorAllDay", "All day: Off", "calendar")); root.Add(repeatRow);
        var priorityRow = Horizontal("PlanEditorPriorityControls", 8); priorityRow.Add(_priority); priorityRow.Add(Button("PlanEditorPriorityButton", "Change priority", "flag")); root.Add(priorityRow);
        _status.SetValue(HavenProperties.Foreground, "TextSecondary"); root.Add(_status);

        var actions = Horizontal("PlanEditorActions", 8); actions.Add(Button("PlanEditorSave", "Save", "check", ButtonVariant.Primary));
        actions.Add(Button("PlanEditorDelete", "Delete", "delete", ButtonVariant.Danger)); actions.Add(Button("PlanEditorCancel", "Cancel", "close")); root.Add(actions);
        return root;
    }
    private void SetBusy(bool busy)
    {
        _busy = busy; var readOnly = _event?.IsReadOnly == true;
        _save.SetValue(HavenProperties.Enabled, !busy && !readOnly);
        _delete.SetValue(HavenProperties.Enabled, !busy && !readOnly && (_task is not null || _event is not null));
        var canChangeDestination = _kind == EditorKind.Task
            ? _collections.Count > 1
            : _event is null && WritableCalendars().Length > 1;
        _destinationButton.SetValue(HavenProperties.Enabled, !busy && !readOnly && canChangeDestination);
        _title.SetValue(HavenProperties.Enabled, !busy && !readOnly); _notes.SetValue(HavenProperties.Enabled, !busy && !readOnly);
        _location.SetValue(HavenProperties.Enabled, !busy && !readOnly);
    }

    private void SetStatus(string? value)
    {
        _status.Content = value ?? string.Empty;
        _status.SetValue(HavenProperties.Visibility, string.IsNullOrWhiteSpace(value) ? HavenVisibility.Collapsed : HavenVisibility.Visible);
    }

    private HavenButton FindButton(string name) => (HavenButton)_root.DescendantsAndSelf().Single(x => x.Name == name);
    private static HavenContainer Vertical(string name, double gap)
    {
        var value = new HavenContainer { Name = name, Layout = HavenLayout.Vertical }; value.SetValue(HavenProperties.Gap, HavenLength.Px(gap)); return value;
    }
    private static HavenContainer Horizontal(string name, double gap)
    {
        var value = new HavenContainer { Name = name, Layout = HavenLayout.Horizontal }; value.SetValue(HavenProperties.Gap, HavenLength.Px(gap));
        value.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll); return value;
    }
    private static HavenButton Button(string name, string content, string icon, ButtonVariant variant = ButtonVariant.Ghost)
    {
        var value = new HavenButton { Name = name, Content = content, IconKey = icon, Variant = variant }; value.SetValue(HavenProperties.MinHeight, HavenLength.Px(36)); return value;
    }
    private static void StyleInput(Input input, double minHeight)
    {
        input.SetValue(HavenProperties.Width, HavenLength.Percent(100)); input.SetValue(HavenProperties.MinHeight, HavenLength.Px(minHeight));
    }
    private static DateTimeOffset AtLocal(DateTime day, TimeSpan time)
    {
        var local = DateTime.SpecifyKind(day.Date + time, DateTimeKind.Unspecified);
        if (TimeZoneInfo.Local.IsInvalidTime(local)) local = local.AddHours(1);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }
    private static string DayCode(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "MO", DayOfWeek.Tuesday => "TU", DayOfWeek.Wednesday => "WE", DayOfWeek.Thursday => "TH",
        DayOfWeek.Friday => "FR", DayOfWeek.Saturday => "SA", _ => "SU"
    };
    private static string FormatDuration(int minutes) => minutes >= 60 ? minutes % 60 == 0 ? $"{minutes / 60}h" : $"{minutes / 60}h {minutes % 60}m" : $"{minutes}m";

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _scene.NewTaskRequested -= OnNewTask; _scene.NewEventRequested -= OnNewEvent; _scene.EditItemRequested -= OnEditItem; Hide();
    }
}