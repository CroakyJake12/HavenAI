using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Haven.Application;
using Haven.Automations;
using Haven.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Controls;

/// <summary>
/// A Plan-owned automation builder backed by the production repository, schedule
/// calculator and worker runner. It does not maintain a parallel in-memory model.
/// </summary>
public sealed class PlanAutomationControl : Button, IDisposable
{
    private readonly IAutomationRepository? _repository;
    private readonly ScheduleCalculator? _schedules;
    private readonly AutomationRunner? _runner;
    private readonly WindowsAutomationRegistrationService? _registration;
    private readonly TextBox _name = new() { PlaceholderText = "Automation name" };
    private readonly TextBox _instruction = new()
    {
        PlaceholderText = "What should Haven do or check?",
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 86,
        MaxHeight = 180
    };
    private readonly ComboBox _mode = new() { ItemsSource = Enum.GetValues<HavenMode>() };
    private readonly ComboBox _kind = new() { ItemsSource = Enum.GetValues<AutomationScheduleKind>() };
    private readonly CalendarDatePicker _onceDate = new() { PlaceholderText = "Run date" };
    private readonly TimePicker _time = new() { ClockIdentifier = "24HourClock" };
    private readonly ComboBox _day = new() { ItemsSource = Enum.GetValues<DayOfWeek>() };
    private readonly NumericUpDown _intervalHours = new() { Minimum = 1, Maximum = 168, Increment = 1, Value = 1 };
    private readonly NumericUpDown _conditionMinutes = new() { Minimum = 60, Maximum = 10_080, Increment = 60, Value = 60 };
    private readonly CheckBox _enabled = new() { Content = "Enabled", IsChecked = true };
    private readonly TextBlock _scheduleHint = new() { Classes = { "muted" }, FontSize = 10, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { Classes = { "muted" }, FontSize = 10, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _items = new() { Spacing = 7 };
    private Guid? _editingId;
    private DateTimeOffset? _editingCreatedAt;
    private bool _disposed;

    public PlanAutomationControl()
    {
        Content = "Automations";
        Classes.Add("secondary");
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Top;
        MinWidth = 118;

        if (App.Services is not null)
        {
            _repository = App.Services.GetService<IAutomationRepository>();
            _schedules = App.Services.GetService<ScheduleCalculator>();
            _runner = App.Services.GetService<AutomationRunner>();
            _registration = App.Services.GetService<WindowsAutomationRegistrationService>();
        }

        var save = new Button { Content = "Create automation" };
        save.Classes.Add("accent");
        save.Click += async (_, _) => await SaveAsync(save);
        var reset = new Button { Content = "Clear" };
        reset.Click += (_, _) => ResetEditor(save);
        var refresh = new Button { Content = "Refresh" };
        refresh.Click += async (_, _) => await RefreshAsync();
        var register = new Button { Content = "Enable background checks" };
        register.Click += async (_, _) => await RegisterWorkerAsync();
        var unregister = new Button { Content = "Disable background checks" };
        unregister.Click += async (_, _) => await UnregisterWorkerAsync();

        _kind.SelectedItem = AutomationScheduleKind.Daily;
        _mode.SelectedItem = HavenMode.Chat;
        _day.SelectedItem = DayOfWeek.Monday;
        _onceDate.SelectedDate = DateTimeOffset.Now.AddDays(1);
        _time.SelectedTime = new TimeSpan(8, 0, 0);
        _kind.SelectionChanged += (_, _) => UpdateScheduleVisibility();
        foreach (var control in new Control[] { _onceDate, _time, _day, _intervalHours, _conditionMinutes })
            control.PropertyChanged += (_, _) => UpdateScheduleHint();

        var panel = new Grid
        {
            Width = 620,
            MaxHeight = 760,
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 12,
            Children =
            {
                new StackPanel
                {
                    Spacing = 3,
                    Children =
                    {
                        new TextBlock { Text = "PLAN AUTOMATIONS", Classes = { "eyebrow" } },
                        new TextBlock { Text = "Schedule work and condition watches", FontSize = 20, FontWeight = FontWeight.SemiBold },
                        new TextBlock
                        {
                            Text = "Definitions are stored locally and executed by Haven's background worker. Condition watches notify through their run record only when structured evidence says the condition is met.",
                            Classes = { "muted" }, FontSize = 10, TextWrapping = TextWrapping.Wrap
                        }
                    }
                },
                WithRow(new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        _name,
                        _instruction,
                        new Grid
                        {
                            ColumnDefinitions = new ColumnDefinitions("*,*"),
                            ColumnSpacing = 8,
                            Children =
                            {
                                Labelled("Experience", _mode),
                                WithColumn(Labelled("Schedule", _kind), 1)
                            }
                        },
                        new Grid
                        {
                            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
                            ColumnSpacing = 8,
                            Children =
                            {
                                Labelled("Once date", _onceDate),
                                WithColumn(Labelled("Time", _time), 1),
                                WithColumn(Labelled("Weekly day", _day), 2)
                            }
                        },
                        new Grid
                        {
                            ColumnDefinitions = new ColumnDefinitions("*,*,Auto"),
                            ColumnSpacing = 8,
                            Children =
                            {
                                Labelled("Every N hours", _intervalHours),
                                WithColumn(Labelled("Condition interval (minutes)", _conditionMinutes), 1),
                                WithColumn(_enabled, 2)
                            }
                        },
                        _scheduleHint,
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 7,
                            Children = { save, reset, refresh, register, unregister }
                        },
                        _status
                    }
                }, 1),
                WithRow(new ScrollViewer
                {
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    MaxHeight = 330,
                    Content = _items
                }, 2)
            }
        };
        Flyout = new Flyout { Placement = PlacementMode.Left, Content = panel };
        AttachedToVisualTree += async (_, _) => await RefreshAsync();
        UpdateScheduleVisibility();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Flyout?.Hide();
        GC.SuppressFinalize(this);
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
        try { _scheduleHint.Text = AutomationScheduleComposer.Describe(SelectedKind, ReadDraft()); }
        catch (Exception ex) { _scheduleHint.Text = ex.Message; }
    }

    private async Task SaveAsync(Button saveButton)
    {
        if (_repository is null || _schedules is null)
        {
            _status.Text = "Automation services are unavailable in this process.";
            return;
        }
        var name = _name.Text?.Trim() ?? string.Empty;
        var instruction = _instruction.Text?.Trim() ?? string.Empty;
        if (name.Length == 0 || instruction.Length == 0)
        {
            _status.Text = "Enter both a name and an instruction.";
            return;
        }
        try
        {
            var now = DateTimeOffset.UtcNow;
            var draft = ReadDraft();
            if (SelectedKind == AutomationScheduleKind.Once && draft.OnceAt <= DateTimeOffset.Now)
                throw new InvalidOperationException("A one-time automation must be scheduled in the future.");
            var json = AutomationScheduleComposer.Compose(SelectedKind, draft);
            var enabled = _enabled.IsChecked == true;
            var definition = new AutomationDefinition(
                _editingId ?? Guid.NewGuid(),
                name,
                SelectedMode,
                instruction,
                SelectedKind,
                json,
                enabled ? _schedules.GetInitialRun(SelectedKind, json, now) : null,
                null,
                enabled,
                _editingCreatedAt ?? now,
                now);
            await _repository.UpsertAsync(definition, CancellationToken.None).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _status.Text = _editingId is null ? "Automation created." : "Automation updated.";
                ResetEditor(saveButton);
            });
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => _status.Text = "Could not save automation: " + ex.Message);
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
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => _status.Text = "Could not load automations: " + ex.Message);
        }
    }

    private void RebuildItems(IReadOnlyList<AutomationDefinition> definitions)
    {
        _items.Children.Clear();
        if (definitions.Count == 0)
        {
            _items.Children.Add(new TextBlock
            {
                Text = "No automations yet. Create one above.",
                Classes = { "muted" },
                Margin = new Thickness(0, 8)
            });
            return;
        }

        foreach (var definition in definitions)
        {
            var draft = AutomationScheduleComposer.Parse(definition.ScheduleKind, definition.ScheduleJson, DateTimeOffset.Now);
            var edit = new Button { Content = "Edit" };
            edit.Click += (_, _) => LoadForEdit(definition);
            var toggle = new Button { Content = definition.IsEnabled ? "Disable" : "Enable" };
            toggle.Click += async (_, _) => await ToggleAsync(definition);
            var run = new Button { Content = "Run now" };
            run.Classes.Add("accent");
            run.Click += async (_, _) => await RunNowAsync(definition);
            var history = new Button { Content = "History" };
            history.Click += async (_, _) => await ShowHistoryAsync(definition);
            var delete = new Button { Content = "Delete" };
            var deleteArmed = false;
            delete.Click += async (_, _) =>
            {
                if (!deleteArmed)
                {
                    deleteArmed = true;
                    delete.Content = "Confirm delete";
                    return;
                }
                await DeleteAsync(definition);
            };

            _items.Children.Add(new Border
            {
                Padding = new Thickness(11),
                CornerRadius = new CornerRadius(12),
                Background = ResourceBrush("HavenPanel2Brush", Color.FromArgb(215, 35, 35, 39)),
                BorderBrush = ResourceBrush("HavenLineBrush", Color.FromArgb(45, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Child = new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        new Grid
                        {
                            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                            Children =
                            {
                                new TextBlock { Text = definition.Name, FontWeight = FontWeight.SemiBold, FontSize = 14 },
                                WithColumn(new TextBlock
                                {
                                    Text = definition.IsEnabled ? "Enabled" : "Disabled",
                                    Foreground = definition.IsEnabled
                                        ? ResourceBrush("HavenAccentBrush", Colors.DeepSkyBlue)
                                        : ResourceBrush("HavenMutedBrush", Colors.Gray),
                                    FontSize = 10
                                }, 1)
                            }
                        },
                        new TextBlock { Text = definition.Instruction, Classes = { "muted" }, FontSize = 10, TextWrapping = TextWrapping.Wrap },
                        new TextBlock
                        {
                            Text = $"{AutomationScheduleComposer.Describe(definition.ScheduleKind, draft)} · Next: {FormatNext(definition.NextRunAt)} · {definition.Mode}",
                            Classes = { "muted2" }, FontSize = 9
                        },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 5,
                            Children = { edit, toggle, run, history, delete }
                        }
                    }
                }
            });
        }
    }

    private void LoadForEdit(AutomationDefinition definition)
    {
        _editingId = definition.Id;
        _editingCreatedAt = definition.CreatedAt;
        _name.Text = definition.Name;
        _instruction.Text = definition.Instruction;
        _mode.SelectedItem = definition.Mode;
        _kind.SelectedItem = definition.ScheduleKind;
        _enabled.IsChecked = definition.IsEnabled;
        var draft = AutomationScheduleComposer.Parse(definition.ScheduleKind, definition.ScheduleJson, DateTimeOffset.Now);
        _onceDate.SelectedDate = draft.OnceAt;
        _time.SelectedTime = draft.Time.ToTimeSpan();
        _day.SelectedItem = draft.DayOfWeek;
        _intervalHours.Value = draft.IntervalHours;
        _conditionMinutes.Value = draft.ConditionIntervalMinutes;
        UpdateScheduleVisibility();
        _status.Text = $"Editing {definition.Name}.";
    }

    private void ResetEditor(Button saveButton)
    {
        _editingId = null;
        _editingCreatedAt = null;
        _name.Text = string.Empty;
        _instruction.Text = string.Empty;
        _enabled.IsChecked = true;
        saveButton.Content = "Create automation";
        _status.Text = "Editor cleared.";
    }

    private async Task ToggleAsync(AutomationDefinition definition)
    {
        if (_repository is null || _schedules is null) return;
        var enabled = !definition.IsEnabled;
        var now = DateTimeOffset.UtcNow;
        var updated = definition with
        {
            IsEnabled = enabled,
            NextRunAt = enabled ? _schedules.GetInitialRun(definition.ScheduleKind, definition.ScheduleJson, now) : null,
            UpdatedAt = now
        };
        await _repository.UpsertAsync(updated, CancellationToken.None).ConfigureAwait(false);
        await RefreshAsync().ConfigureAwait(false);
    }

    private async Task RunNowAsync(AutomationDefinition definition)
    {
        if (_repository is null || _runner is null) return;
        try
        {
            var now = DateTimeOffset.UtcNow;
            await _repository.UpsertAsync(definition with
            {
                IsEnabled = true,
                NextRunAt = now,
                UpdatedAt = now
            }, CancellationToken.None).ConfigureAwait(false);
            var result = await _runner.RunDueAsync(now.AddSeconds(1), CancellationToken.None).ConfigureAwait(false);
            if (!definition.IsEnabled)
            {
                await _repository.UpsertAsync(definition with
                {
                    IsEnabled = false,
                    NextRunAt = null,
                    UpdatedAt = DateTimeOffset.UtcNow
                }, CancellationToken.None).ConfigureAwait(false);
            }
            await Dispatcher.UIThread.InvokeAsync(() =>
                _status.Text = $"Run-now finished: {result.Succeeded} succeeded, {result.Failed} failed, {result.Skipped} skipped.");
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => _status.Text = "Run-now failed: " + ex.Message);
        }
    }

    private async Task ShowHistoryAsync(AutomationDefinition definition)
    {
        if (_repository is null) return;
        try
        {
            var runs = await _repository.GetRunsAsync(definition.Id, 5, CancellationToken.None).ConfigureAwait(false);
            var summary = runs.Count == 0
                ? "No runs have been recorded yet."
                : string.Join("\n", runs.Select(run =>
                    $"{run.ScheduledFor.ToLocalTime():g} · {run.Status} · {SummariseRun(run)}"));
            await Dispatcher.UIThread.InvokeAsync(() => _status.Text = definition.Name + " history:\n" + summary);
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => _status.Text = "Could not read history: " + ex.Message);
        }
    }

    private async Task DeleteAsync(AutomationDefinition definition)
    {
        if (_repository is null) return;
        await _repository.DeleteAsync(definition.Id, CancellationToken.None).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() => _status.Text = $"Deleted {definition.Name}.");
        await RefreshAsync().ConfigureAwait(false);
    }

    private async Task RegisterWorkerAsync()
    {
        if (_registration is null) return;
        var path = Path.Combine(AppContext.BaseDirectory, "Haven.AutomationWorker.exe");
        var result = await _registration.RegisterAsync(path, CancellationToken.None).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() => _status.Text = result.Message);
    }

    private async Task UnregisterWorkerAsync()
    {
        if (_registration is null) return;
        var result = await _registration.UnregisterAsync(CancellationToken.None).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() => _status.Text = result.Message);
    }

    private AutomationScheduleDraft ReadDraft()
    {
        var selectedDate = _onceDate.SelectedDate ?? DateTimeOffset.Now.AddDays(1);
        var selectedTime = _time.SelectedTime ?? new TimeSpan(8, 0, 0);
        var local = DateTime.SpecifyKind(selectedDate.Date.Add(selectedTime), DateTimeKind.Unspecified);
        var once = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        return new AutomationScheduleDraft(
            once,
            TimeOnly.FromTimeSpan(selectedTime),
            _day.SelectedItem is DayOfWeek day ? day : DayOfWeek.Monday,
            (int)(_intervalHours.Value ?? 1),
            (int)(_conditionMinutes.Value ?? 60));
    }

    private AutomationScheduleKind SelectedKind =>
        _kind.SelectedItem is AutomationScheduleKind kind ? kind : AutomationScheduleKind.Daily;

    private HavenMode SelectedMode =>
        _mode.SelectedItem is HavenMode mode ? mode : HavenMode.Chat;

    private static string FormatNext(DateTimeOffset? next) =>
        next is null ? "not scheduled" : next.Value.ToLocalTime().ToString("g");

    private static string SummariseRun(AutomationRun run)
    {
        var value = run.Error ?? run.Result ?? "No report";
        value = value.ReplaceLineEndings(" ").Trim();
        return value.Length <= 150 ? value : value[..150] + "…";
    }

    private static StackPanel Labelled(string label, Control control) => new()
    {
        Spacing = 3,
        Children =
        {
            new TextBlock { Text = label, Classes = { "muted" }, FontSize = 10 },
            control
        }
    };

    private static IBrush ResourceBrush(string key, Color fallback) =>
        Application.Current?.Resources[key] as IBrush ?? new SolidColorBrush(fallback);

    private static T WithRow<T>(T control, int row) where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }

    private static T WithColumn<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }
}
