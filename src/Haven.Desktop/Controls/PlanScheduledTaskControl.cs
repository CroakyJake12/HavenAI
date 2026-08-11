using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Haven.Application;
using Haven.Application.Tasks;
using Haven.Core;
using Haven.Desktop.HavenUI.Components;
using Haven.Desktop.HavenUI.Components.Buttons;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Controls;

/// <summary>
/// HavenUI-native Plan entry point for reusable scheduled Tasks. It deliberately
/// owns no background worker, OS scheduler registration, or parallel automation
/// surface; execution is delegated to the Tasks-owned application runtime.
/// </summary>
public sealed class PlanScheduledTaskControl : UserControl, IDisposable
{
    private static readonly string[] ModeNames = Enum.GetNames<HavenMode>();
    private static readonly string[] ScheduleNames = Enum.GetNames<AutomationScheduleKind>();
    private static readonly string[] DayNames = Enum.GetNames<DayOfWeek>();

    private readonly IAutomationRepository? _repository;
    private readonly ScheduledTaskScheduleCalculator? _schedules;
    private readonly ScheduledTaskRunner? _runner;
    private readonly HavenTextInput _name = new() { PlaceholderText = "Task name" };
    private readonly HavenMultilineInput _instruction = new()
    {
        PlaceholderText = "What should Haven handle or check?",
        MinHeight = 92,
        MaxHeight = 180
    };
    private readonly HavenSelect _mode = new() { ItemsSource = ModeNames, SelectedIndex = 0 };
    private readonly HavenSelect _kind = new() { ItemsSource = ScheduleNames, SelectedIndex = (int)AutomationScheduleKind.Daily };
    private readonly HavenCalendarPicker _onceDate = new() { PlaceholderText = "Run date" };
    private readonly HavenTimePicker _time = new() { ClockIdentifier = "24HourClock" };
    private readonly HavenSelect _day = new() { ItemsSource = DayNames, SelectedIndex = (int)DayOfWeek.Monday };
    private readonly HavenNumericInput _intervalHours = new() { Minimum = 1, Maximum = 168, Increment = 1, Value = 1 };
    private readonly HavenNumericInput _conditionMinutes = new() { Minimum = 60, Maximum = 10_080, Increment = 60, Value = 60 };
    private readonly HavenCheckBox _enabled = new() { Content = "Enabled", IsChecked = true };
    private readonly TextBlock _scheduleHint = new() { Classes = { "muted" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBlock _status = new() { Classes = { "muted" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly StackPanel _items = new() { Spacing = 8 };
    private readonly HavenPrimaryButton _save = new() { Content = "Create task" };
    private readonly HavenAdaptivePopup _popup = new() { Placement = PlacementMode.Left };
    private Guid? _editingId;
    private DateTimeOffset? _editingCreatedAt;
    private bool _disposed;

    public PlanScheduledTaskControl()
    {
        if (App.Services is not null)
        {
            _repository = App.Services.GetService<IAutomationRepository>();
            _schedules = App.Services.GetService<ScheduledTaskScheduleCalculator>();
            _runner = App.Services.GetService<ScheduledTaskRunner>();
        }

        _onceDate.SelectedDate = DateTime.Today.AddDays(1);
        _time.SelectedTime = new TimeSpan(8, 0, 0);
        _kind.SelectionChanged += (_, _) => UpdateScheduleVisibility();
        _day.SelectionChanged += (_, _) => UpdateScheduleHint();
        _onceDate.SelectedDateChanged += (_, _) => UpdateScheduleHint();
        _time.SelectedTimeChanged += (_, _) => UpdateScheduleHint();
        _intervalHours.ValueChanged += (_, _) => UpdateScheduleHint();
        _conditionMinutes.ValueChanged += (_, _) => UpdateScheduleHint();
        _save.Click += async (_, _) => await SaveAsync();

        var launcher = new HavenSecondaryButton
        {
            Content = "Scheduled tasks",
            MinWidth = 132,
            Flyout = _popup
        };
        _popup.Content = BuildPopup();
        Content = launcher;
        AttachedToVisualTree += async (_, _) => await RefreshAsync();
        UpdateScheduleVisibility();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _popup.Hide();
        GC.SuppressFinalize(this);
    }

    private Control BuildPopup()
    {
        var clear = new HavenTextButton { Content = "Clear" };
        clear.Click += (_, _) => ResetEditor();
        var refresh = new HavenTextButton { Content = "Refresh" };
        refresh.Click += async (_, _) => await RefreshAsync();

        var form = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                _name,
                _instruction,
                TwoColumns(Labelled("Experience", _mode), Labelled("Schedule", _kind)),
                ThreeColumns(Labelled("Once date", _onceDate), Labelled("Time", _time), Labelled("Weekly day", _day)),
                ThreeColumns(Labelled("Every N hours", _intervalHours), Labelled("Condition interval (minutes)", _conditionMinutes), _enabled),
                _scheduleHint,
                new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Children = { _save, clear, refresh } },
                _status
            }
        };

        return new HavenPopupCard
        {
            Width = 620,
            MaxHeight = 760,
            Padding = new Thickness(20),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,*"),
                RowSpacing = 14,
                Children =
                {
                    new StackPanel
                    {
                        Spacing = 3,
                        Children =
                        {
                            new TextBlock { Text = "PLAN · SCHEDULED TASKS", Classes = { "eyebrow" } },
                            new TextBlock { Text = "Schedule work and condition watches", FontSize = 20, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                            new TextBlock
                            {
                                Text = "Schedules and run history stay in Haven Tasks. This surface has no separate worker or OS scheduler controls.",
                                Classes = { "muted" },
                                TextWrapping = Avalonia.Media.TextWrapping.Wrap
                            }
                        }
                    },
                    Row(form, 1),
                    Row(new ScrollViewer
                    {
                        MaxHeight = 330,
                        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        Content = _items
                    }, 2)
                }
            }
        };
    }

    private async Task SaveAsync()
    {
        if (_repository is null || _schedules is null)
        {
            _status.Text = "Scheduled Tasks are unavailable in this process.";
            return;
        }

        var name = _name.Text?.Trim() ?? string.Empty;
        var instruction = _instruction.Text?.Trim() ?? string.Empty;
        if (name.Length == 0 || instruction.Length == 0)
        {
            _status.Text = "Enter both a task name and an instruction.";
            return;
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            var draft = ReadDraft();
            if (SelectedKind == AutomationScheduleKind.Once && draft.OnceAt <= DateTimeOffset.Now)
                throw new InvalidOperationException("A one-time task must be scheduled in the future.");

            var scheduleJson = ScheduledTaskScheduleComposer.Compose(SelectedKind, draft);
            var enabled = _enabled.IsChecked == true;
            var definition = new AutomationDefinition(
                _editingId ?? Guid.NewGuid(),
                name,
                SelectedMode,
                instruction,
                SelectedKind,
                scheduleJson,
                enabled ? _schedules.GetInitialRun(SelectedKind, scheduleJson, now) : null,
                null,
                enabled,
                _editingCreatedAt ?? now,
                now);
            var editing = _editingId is not null;
            await _repository.UpsertAsync(definition, CancellationToken.None).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ResetEditor();
                _status.Text = editing ? "Scheduled task updated." : "Scheduled task created.";
            });
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => _status.Text = "Could not save scheduled task: " + exception.Message);
        }
    }

    private async Task RefreshAsync()
    {
        if (_disposed || _repository is null) return;
        try
        {
            var definitions = await _repository.GetAllAsync(CancellationToken.None).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() => RebuildItems(definitions));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => _status.Text = "Could not load scheduled tasks: " + exception.Message);
        }
    }

    private void RebuildItems(IReadOnlyList<AutomationDefinition> definitions)
    {
        _items.Children.Clear();
        if (definitions.Count == 0)
        {
            _items.Children.Add(new TextBlock { Text = "No scheduled tasks yet.", Classes = { "muted" }, Margin = new Thickness(0, 8) });
            return;
        }

        foreach (var definition in definitions.OrderBy(item => item.NextRunAt ?? DateTimeOffset.MaxValue))
            _items.Children.Add(BuildTaskCard(definition));
    }

    private Control BuildTaskCard(AutomationDefinition definition)
    {
        var edit = new HavenTextButton { Content = "Edit" };
        edit.Click += (_, _) => LoadForEdit(definition);
        var toggle = new HavenTextButton { Content = definition.IsEnabled ? "Disable" : "Enable" };
        toggle.Click += async (_, _) => await ToggleAsync(definition);
        var run = new HavenPrimaryButton { Content = "Run now" };
        run.Click += async (_, _) => await RunNowAsync(definition);
        var history = new HavenTextButton { Content = "History" };
        history.Click += async (_, _) => await ShowHistoryAsync(definition);
        var delete = new HoldToConfirmButton { Content = "Delete" };
        delete.Click += async (_, _) => await DeleteAsync(definition);
        var draft = ScheduledTaskScheduleComposer.Parse(definition.ScheduleKind, definition.ScheduleJson, DateTimeOffset.Now);

        return new HavenCard
        {
            Padding = new Thickness(14),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = definition.Name, FontSize = 14, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                    new TextBlock { Text = definition.Instruction, Classes = { "muted" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new TextBlock
                    {
                        Text = $"{ScheduledTaskScheduleComposer.Describe(definition.ScheduleKind, draft)} · Next: {FormatNext(definition.NextRunAt)} · {(definition.IsEnabled ? "Enabled" : "Disabled")}",
                        Classes = { "muted2" }
                    },
                    new WrapPanel { ItemSpacing = 6, LineSpacing = 6, Children = { edit, toggle, run, history, delete } }
                }
            }
        };
    }

    private void LoadForEdit(AutomationDefinition definition)
    {
        _editingId = definition.Id;
        _editingCreatedAt = definition.CreatedAt;
        _name.Text = definition.Name;
        _instruction.Text = definition.Instruction;
        _mode.SelectedIndex = Array.IndexOf(ModeNames, definition.Mode.ToString());
        _kind.SelectedIndex = Array.IndexOf(ScheduleNames, definition.ScheduleKind.ToString());
        _enabled.IsChecked = definition.IsEnabled;
        var draft = ScheduledTaskScheduleComposer.Parse(definition.ScheduleKind, definition.ScheduleJson, DateTimeOffset.Now);
        _onceDate.SelectedDate = draft.OnceAt.ToLocalTime().Date;
        _time.SelectedTime = draft.Time.ToTimeSpan();
        _day.SelectedIndex = Array.IndexOf(DayNames, draft.DayOfWeek.ToString());
        _intervalHours.Value = draft.IntervalHours;
        _conditionMinutes.Value = draft.ConditionIntervalMinutes;
        _save.Content = "Update task";
        _status.Text = $"Editing {definition.Name}.";
        UpdateScheduleVisibility();
    }

    private async Task ToggleAsync(AutomationDefinition definition)
    {
        if (_repository is null || _schedules is null) return;
        var enabled = !definition.IsEnabled;
        var now = DateTimeOffset.UtcNow;
        await _repository.UpsertAsync(definition with
        {
            IsEnabled = enabled,
            NextRunAt = enabled ? _schedules.GetInitialRun(definition.ScheduleKind, definition.ScheduleJson, now) : null,
            UpdatedAt = now
        }, CancellationToken.None).ConfigureAwait(false);
        await RefreshAsync().ConfigureAwait(false);
    }

    private async Task RunNowAsync(AutomationDefinition definition)
    {
        if (_runner is null)
        {
            _status.Text = "The Tasks runner is unavailable.";
            return;
        }
        var run = await _runner.RunOneAsync(definition, DateTimeOffset.UtcNow, CancellationToken.None).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() => _status.Text = run.Status == AutomationRunStatus.Succeeded
            ? $"{definition.Name} finished. {SummariseRun(run)}"
            : $"{definition.Name} did not finish successfully. {SummariseRun(run)}");
        await RefreshAsync().ConfigureAwait(false);
    }

    private async Task ShowHistoryAsync(AutomationDefinition definition)
    {
        if (_repository is null) return;
        var runs = await _repository.GetRunsAsync(definition.Id, 5, CancellationToken.None).ConfigureAwait(false);
        var summary = runs.Count == 0
            ? "No runs have been recorded yet."
            : string.Join("\n", runs.Select(run => $"{run.ScheduledFor.ToLocalTime():g} · {run.Status} · {SummariseRun(run)}"));
        await Dispatcher.UIThread.InvokeAsync(() => _status.Text = definition.Name + " history:\n" + summary);
    }

    private async Task DeleteAsync(AutomationDefinition definition)
    {
        if (_repository is null) return;
        await _repository.DeleteAsync(definition.Id, CancellationToken.None).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() => _status.Text = $"Deleted {definition.Name}.");
        await RefreshAsync().ConfigureAwait(false);
    }

    private void ResetEditor()
    {
        _editingId = null;
        _editingCreatedAt = null;
        _name.Text = string.Empty;
        _instruction.Text = string.Empty;
        _enabled.IsChecked = true;
        _save.Content = "Create task";
    }

    private void UpdateScheduleVisibility()
    {
        var kind = SelectedKind;
        _onceDate.IsVisible = kind == AutomationScheduleKind.Once;
        _time.IsVisible = kind is AutomationScheduleKind.Once or AutomationScheduleKind.Daily or AutomationScheduleKind.Weekly;
        _day.IsVisible = kind == AutomationScheduleKind.Weekly;
        _intervalHours.IsVisible = kind == AutomationScheduleKind.Hourly;
        _conditionMinutes.IsVisible = kind == AutomationScheduleKind.ConditionWatch;
        UpdateScheduleHint();
    }

    private void UpdateScheduleHint()
    {
        try { _scheduleHint.Text = ScheduledTaskScheduleComposer.Describe(SelectedKind, ReadDraft()); }
        catch (Exception exception) { _scheduleHint.Text = exception.Message; }
    }

    private ScheduledTaskScheduleDraft ReadDraft()
    {
        var selectedDate = _onceDate.SelectedDate ?? DateTime.Today.AddDays(1);
        var selectedTime = _time.SelectedTime ?? new TimeSpan(8, 0, 0);
        var local = DateTime.SpecifyKind(selectedDate.Date.Add(selectedTime), DateTimeKind.Unspecified);
        return new ScheduledTaskScheduleDraft(
            new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)),
            TimeOnly.FromTimeSpan(selectedTime),
            Enum.TryParse<DayOfWeek>(_day.SelectedItem, out var day) ? day : DayOfWeek.Monday,
            (int)(_intervalHours.Value ?? 1),
            (int)(_conditionMinutes.Value ?? 60));
    }

    private AutomationScheduleKind SelectedKind =>
        Enum.TryParse<AutomationScheduleKind>(_kind.SelectedItem, out var kind) ? kind : AutomationScheduleKind.Daily;

    private HavenMode SelectedMode =>
        Enum.TryParse<HavenMode>(_mode.SelectedItem, out var mode) ? mode : HavenMode.Chat;

    private static string FormatNext(DateTimeOffset? next) => next is null ? "not scheduled" : next.Value.ToLocalTime().ToString("g");

    private static string SummariseRun(AutomationRun run)
    {
        var value = (run.Error ?? run.Result ?? "No report").ReplaceLineEndings(" ").Trim();
        return value.Length <= 150 ? value : value[..150] + "…";
    }

    private static StackPanel Labelled(string label, Control control) => new()
    {
        Spacing = 4,
        Children = { new TextBlock { Text = label, Classes = { "muted" } }, control }
    };

    private static Grid TwoColumns(Control first, Control second)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 10 };
        grid.Children.Add(first);
        Grid.SetColumn(second, 1);
        grid.Children.Add(second);
        return grid;
    }

    private static Grid ThreeColumns(Control first, Control second, Control third)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 10 };
        grid.Children.Add(first);
        Grid.SetColumn(second, 1);
        grid.Children.Add(second);
        Grid.SetColumn(third, 2);
        grid.Children.Add(third);
        return grid;
    }

    private static T Row<T>(T control, int row) where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }
}
