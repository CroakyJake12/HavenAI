/*
 * FILE DOCUMENTATION
 * Where: src/Haven.OldHaven/Controls/PlanAutomationControl.cs, in the Desktop controls layer, containing reusable Avalonia behavior and visual building blocks.
 * What: This file owns PlanAutomationControl. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

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
/// Plan-owned automation builder backed directly by the production repository,
/// schedule calculator, runner and Windows background-worker registration.
/// </summary>
public sealed class PlanAutomationControl : Button, IDisposable
{
    /// <summary>
    /// Stores repository locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IAutomationRepository? _repository;
    /// <summary>
    /// Stores schedules locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ScheduleCalculator? _schedules;
    /// <summary>
    /// Stores runner locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly AutomationRunner? _runner;
    /// <summary>
    /// Stores registration locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly WindowsAutomationRegistrationService? _registration;

    /// <summary>
    /// Stores name locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TextBox _name = new() { PlaceholderText = "Automation name" };
    /// <summary>
    /// Stores instruction locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TextBox _instruction = new()
    {
        PlaceholderText = "What should Haven do or check?",
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 86,
        MaxHeight = 180
    };
    /// <summary>
    /// Stores mode locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ComboBox _mode = new() { ItemsSource = Enum.GetValues<HavenMode>() };
    /// <summary>
    /// Stores kind locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ComboBox _kind = new() { ItemsSource = Enum.GetValues<AutomationScheduleKind>() };
    /// <summary>
    /// Stores once date locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly CalendarDatePicker _onceDate = new() { PlaceholderText = "Run date" };
    /// <summary>
    /// Stores time locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TimePicker _time = new() { ClockIdentifier = "24HourClock" };
    /// <summary>
    /// Stores day locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ComboBox _day = new() { ItemsSource = Enum.GetValues<DayOfWeek>() };
    /// <summary>
    /// Stores interval hours locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly NumericUpDown _intervalHours = new()
    {
        Minimum = 1,
        Maximum = 168,
        Increment = 1,
        Value = 1
    };
    /// <summary>
    /// Stores condition minutes locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly NumericUpDown _conditionMinutes = new()
    {
        Minimum = 60,
        Maximum = 10_080,
        Increment = 60,
        Value = 60
    };
    /// <summary>
    /// Stores enabled locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly CheckBox _enabled = new() { Content = "Enabled", IsChecked = true };
    /// <summary>
    /// Stores schedule hint locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TextBlock _scheduleHint = new()
    {
        Classes = { "muted" },
        FontSize = 10,
        TextWrapping = TextWrapping.Wrap
    };
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TextBlock _status = new()
    {
        Classes = { "muted" },
        FontSize = 10,
        TextWrapping = TextWrapping.Wrap
    };
    /// <summary>
    /// Stores items locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly StackPanel _items = new() { Spacing = 7 };
    /// <summary>
    /// Stores save button locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Button _saveButton = new() { Content = "Create automation" };

    /// <summary>
    /// Stores editing id locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private Guid? _editingId;
    /// <summary>
    /// Stores editing created at locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private DateTimeOffset? _editingCreatedAt;
    /// <summary>
    /// Stores disposed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
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

        _saveButton.Classes.Add("accent");
        _saveButton.Click += async (_, _) => await SaveAsync();
        var reset = new Button { Content = "Clear" };
        reset.Click += (_, _) => ResetEditor();
        var refresh = new Button { Content = "Refresh" };
        refresh.Click += async (_, _) => await RefreshAsync();
        var register = new Button { Content = "Enable background checks" };
        register.Click += async (_, _) => await RegisterWorkerAsync();
        var unregister = new Button { Content = "Disable background checks" };
        unregister.Click += async (_, _) => await UnregisterWorkerAsync();

        _kind.SelectedItem = AutomationScheduleKind.Daily;
        _mode.SelectedItem = HavenMode.Chat;
        _day.SelectedItem = DayOfWeek.Monday;
        _onceDate.SelectedDate = DateTime.Today.AddDays(1);
        _time.SelectedTime = new TimeSpan(8, 0, 0);
        _kind.SelectionChanged += (_, _) => UpdateScheduleVisibility();
        foreach (var control in new AvaloniaObject[] { _onceDate, _time, _day, _intervalHours, _conditionMinutes })
            control.PropertyChanged += (_, _) => UpdateScheduleHint();

        Flyout = new Flyout
        {
            Placement = PlacementMode.Left,
            Content = BuildEditor(
                reset,
                refresh,
                register,
                unregister)
        };
        AttachedToVisualTree += async (_, _) => await RefreshAsync();
        UpdateScheduleVisibility();
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Flyout?.Hide();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Builds editor from the currently available inputs.
    /// </summary>
    private Control BuildEditor(
        Button reset,
        Button refresh,
        Button register,
        Button unregister) => new Grid
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
                    new TextBlock
                    {
                        Text = "Schedule work and condition watches",
                        FontSize = 20,
                        FontWeight = FontWeight.SemiBold
                    },
                    new TextBlock
                    {
                        Text = "Definitions and run history are stored locally. Condition watches record a structured met/not-met result and fail closed when the available evidence is missing or ambiguous.",
                        Classes = { "muted" },
                        FontSize = 10,
                        TextWrapping = TextWrapping.Wrap
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
                    new WrapPanel
                    {
                        ItemSpacing = 7,
                        LineSpacing = 7,
                        Children = { _saveButton, reset, refresh, register, unregister }
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

    /// <summary>
    /// Performs the update schedule visibility step owned by this component.
    /// </summary>
    private void UpdateScheduleVisibility()
    {
        var kind = SelectedKind;
        _onceDate.IsVisible = kind == AutomationScheduleKind.Once;
        _time.IsVisible = kind is AutomationScheduleKind.Once
            or AutomationScheduleKind.Daily
            or AutomationScheduleKind.Weekly;
        _day.IsVisible = kind == AutomationScheduleKind.Weekly;
        _intervalHours.IsVisible = kind == AutomationScheduleKind.Hourly;
        _conditionMinutes.IsVisible = kind == AutomationScheduleKind.ConditionWatch;
        UpdateScheduleHint();
    }

    /// <summary>
    /// Performs the update schedule hint step owned by this component.
    /// </summary>
    private void UpdateScheduleHint()
    {
        try
        {
            _scheduleHint.Text = AutomationScheduleComposer.Describe(SelectedKind, ReadDraft());
        }
        catch (Exception ex)
        {
            _scheduleHint.Text = ex.Message;
        }
    }

    /// <summary>
    /// Performs save asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task SaveAsync()
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

            var scheduleJson = AutomationScheduleComposer.Compose(SelectedKind, draft);
            var enabled = _enabled.IsChecked == true;
            var wasEditing = _editingId is not null;
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
            await _repository.UpsertAsync(definition, CancellationToken.None).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ResetEditor();
                _status.Text = wasEditing ? "Automation updated." : "Automation created.";
            });
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                _status.Text = "Could not save automation: " + ex.Message);
        }
    }

    /// <summary>
    /// Performs refresh asynchronously so I/O does not block the caller's thread.
    /// </summary>
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
            await Dispatcher.UIThread.InvokeAsync(() =>
                _status.Text = "Could not load automations: " + ex.Message);
        }
    }

    /// <summary>
    /// Performs the rebuild items step owned by this component.
    /// </summary>
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
            _items.Children.Add(BuildDefinitionCard(definition));
    }

    /// <summary>
    /// Builds definition card from the currently available inputs.
    /// </summary>
    private Control BuildDefinitionCard(AutomationDefinition definition)
    {
        var draft = AutomationScheduleComposer.Parse(
            definition.ScheduleKind,
            definition.ScheduleJson,
            DateTimeOffset.Now);
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

        return new Border
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
                            new TextBlock
                            {
                                Text = definition.Name,
                                FontWeight = FontWeight.SemiBold,
                                FontSize = 14
                            },
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
                    new TextBlock
                    {
                        Text = definition.Instruction,
                        Classes = { "muted" },
                        FontSize = 10,
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = $"{AutomationScheduleComposer.Describe(definition.ScheduleKind, draft)} · Next: {FormatNext(definition.NextRunAt)} · {definition.Mode}",
                        Classes = { "muted2" },
                        FontSize = 9
                    },
                    new WrapPanel
                    {
                        ItemSpacing = 5,
                        LineSpacing = 5,
                        Children = { edit, toggle, run, history, delete }
                    }
                }
            }
        };
    }

    /// <summary>
    /// Performs the load for edit step owned by this component.
    /// </summary>
    private void LoadForEdit(AutomationDefinition definition)
    {
        _editingId = definition.Id;
        _editingCreatedAt = definition.CreatedAt;
        _name.Text = definition.Name;
        _instruction.Text = definition.Instruction;
        _mode.SelectedItem = definition.Mode;
        _kind.SelectedItem = definition.ScheduleKind;
        _enabled.IsChecked = definition.IsEnabled;

        var draft = AutomationScheduleComposer.Parse(
            definition.ScheduleKind,
            definition.ScheduleJson,
            DateTimeOffset.Now);
        _onceDate.SelectedDate = draft.OnceAt.ToLocalTime().Date;
        _time.SelectedTime = draft.Time.ToTimeSpan();
        _day.SelectedItem = draft.DayOfWeek;
        _intervalHours.Value = draft.IntervalHours;
        _conditionMinutes.Value = draft.ConditionIntervalMinutes;
        _saveButton.Content = "Update automation";
        UpdateScheduleVisibility();
        _status.Text = $"Editing {definition.Name}.";
    }

    /// <summary>
    /// Performs the reset editor step owned by this component.
    /// </summary>
    private void ResetEditor()
    {
        _editingId = null;
        _editingCreatedAt = null;
        _name.Text = string.Empty;
        _instruction.Text = string.Empty;
        _enabled.IsChecked = true;
        _saveButton.Content = "Create automation";
    }

    /// <summary>
    /// Performs toggle asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ToggleAsync(AutomationDefinition definition)
    {
        if (_repository is null || _schedules is null) return;
        var enabled = !definition.IsEnabled;
        var now = DateTimeOffset.UtcNow;
        await _repository.UpsertAsync(definition with
        {
            IsEnabled = enabled,
            NextRunAt = enabled
                ? _schedules.GetInitialRun(definition.ScheduleKind, definition.ScheduleJson, now)
                : null,
            UpdatedAt = now
        }, CancellationToken.None).ConfigureAwait(false);
        await RefreshAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Runs run now async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    private async Task RunNowAsync(AutomationDefinition definition)
    {
        if (_runner is null) return;
        try
        {
            var run = await _runner.RunOneAsync(
                definition,
                DateTimeOffset.UtcNow,
                CancellationToken.None).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
                _status.Text = run.Status == AutomationRunStatus.Succeeded
                    ? $"{definition.Name} finished successfully. {SummariseRun(run)}"
                    : $"{definition.Name} failed. {SummariseRun(run)}");
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                _status.Text = "Run-now failed: " + ex.Message);
        }
    }

    /// <summary>
    /// Performs show history asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ShowHistoryAsync(AutomationDefinition definition)
    {
        if (_repository is null) return;
        try
        {
            var runs = await _repository.GetRunsAsync(
                definition.Id,
                5,
                CancellationToken.None).ConfigureAwait(false);
            var summary = runs.Count == 0
                ? "No runs have been recorded yet."
                : string.Join("\n", runs.Select(run =>
                    $"{run.ScheduledFor.ToLocalTime():g} · {run.Status} · {SummariseRun(run)}"));
            await Dispatcher.UIThread.InvokeAsync(() =>
                _status.Text = definition.Name + " history:\n" + summary);
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                _status.Text = "Could not read history: " + ex.Message);
        }
    }

    /// <summary>
    /// Performs delete asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task DeleteAsync(AutomationDefinition definition)
    {
        if (_repository is null) return;
        await _repository.DeleteAsync(definition.Id, CancellationToken.None).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
            _status.Text = $"Deleted {definition.Name}.");
        await RefreshAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Performs register worker asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task RegisterWorkerAsync()
    {
        if (_registration is null) return;
        var executable = ResolveWorkerExecutable();
        if (executable is null)
        {
            _status.Text = "Haven.AutomationWorker.exe was not found beside the app. Build or install the worker before enabling background checks.";
            return;
        }
        var result = await _registration.RegisterAsync(
            executable,
            CancellationToken.None).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() => _status.Text = result.Message);
    }

    /// <summary>
    /// Performs unregister worker asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task UnregisterWorkerAsync()
    {
        if (_registration is null) return;
        var result = await _registration.UnregisterAsync(CancellationToken.None).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() => _status.Text = result.Message);
    }

    /// <summary>
    /// Performs the read draft step owned by this component.
    /// </summary>
    private AutomationScheduleDraft ReadDraft()
    {
        var selectedDate = _onceDate.SelectedDate ?? DateTime.Today.AddDays(1);
        var selectedTime = _time.SelectedTime ?? new TimeSpan(8, 0, 0);
        var local = DateTime.SpecifyKind(
            selectedDate.Date.Add(selectedTime),
            DateTimeKind.Unspecified);
        var once = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        return new AutomationScheduleDraft(
            once,
            TimeOnly.FromTimeSpan(selectedTime),
            _day.SelectedItem is DayOfWeek day ? day : DayOfWeek.Monday,
            (int)(_intervalHours.Value ?? 1),
            (int)(_conditionMinutes.Value ?? 60));
    }

    /// <summary>
    /// Performs the resolve worker executable step owned by this component.
    /// </summary>
    private static string? ResolveWorkerExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Haven.AutomationWorker.exe"),
            Path.Combine(AppContext.BaseDirectory, "workers", "Haven.AutomationWorker.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// Gets or updates selected kind, the bindable or domain state represented by this property.
    /// </summary>
    private AutomationScheduleKind SelectedKind =>
        _kind.SelectedItem is AutomationScheduleKind kind
            ? kind
            : AutomationScheduleKind.Daily;

    /// <summary>
    /// Gets or updates selected mode, the bindable or domain state represented by this property.
    /// </summary>
    private HavenMode SelectedMode =>
        _mode.SelectedItem is HavenMode mode ? mode : HavenMode.Chat;

    /// <summary>
    /// Performs the format next step owned by this component.
    /// </summary>
    private static string FormatNext(DateTimeOffset? next) =>
        next is null ? "not scheduled" : next.Value.ToLocalTime().ToString("g");

    /// <summary>
    /// Performs the summarise run step owned by this component.
    /// </summary>
    private static string SummariseRun(AutomationRun run)
    {
        var value = run.Error ?? run.Result ?? "No report";
        value = value.ReplaceLineEndings(" ").Trim();
        return value.Length <= 150 ? value : value[..150] + "…";
    }

    /// <summary>
    /// Performs the labelled step owned by this component.
    /// </summary>
    private static StackPanel Labelled(string label, Control control) => new()
    {
        Spacing = 3,
        Children =
        {
            new TextBlock { Text = label, Classes = { "muted" }, FontSize = 10 },
            control
        }
    };

    /// <summary>
    /// Performs the resource brush step owned by this component.
    /// </summary>
    private static IBrush ResourceBrush(string key, Color fallback) =>
        Avalonia.Application.Current?.Resources[key] as IBrush ?? new SolidColorBrush(fallback);

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
