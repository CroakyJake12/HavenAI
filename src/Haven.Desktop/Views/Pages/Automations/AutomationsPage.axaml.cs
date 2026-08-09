using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Application;
using Haven.Automations;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Events;
using Haven.Desktop.HavenUI.Components.Buttons;

namespace Haven.Desktop.Views.Pages.Automations;

public sealed partial class AutomationsPage : UserControl
{
    private readonly HavenEventBus _bus;
    private readonly IAutomationRepository _repository;
    private readonly WindowsAutomationRegistrationService _registration;
    private readonly AutomationRunner _runner;
    private readonly ScheduleCalculator _schedules;
    private readonly HavenMode[] _modes = Enum.GetValues<HavenMode>();
    private readonly AutomationScheduleKind[] _scheduleKinds = Enum.GetValues<AutomationScheduleKind>();
    private HavenMode _selectedMode = HavenMode.Chat;
    private AutomationScheduleKind _selectedScheduleKind = AutomationScheduleKind.Daily;

    public AutomationsPage(
        HavenEventBus bus,
        IAutomationRepository repository,
        WindowsAutomationRegistrationService registration,
        AutomationRunner runner,
        ScheduleCalculator schedules)
    {
        _bus = bus;
        _repository = repository;
        _registration = registration;
        _runner = runner;
        _schedules = schedules;

        InitializeComponent();
        WireEvents();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e) => _ = RefreshAsync();

    private static IBrush? Brush(string key) =>
        Avalonia.Application.Current?.TryFindResource(key, out var value) == true ? value as IBrush : null;

    private void WireEvents()
    {
        _bus.RegisterElement("Automations.Actions.RegisterWorker", RegisterWorkerButton);
        _bus.WirePointerEvents("Automations.Actions.RegisterWorker", RegisterWorkerButton);
        RegisterWorkerButton.Click += async (_, _) =>
        {
            _bus.Fire("Automations.Actions.RegisterWorker");
            var worker = System.IO.Path.Combine(AppContext.BaseDirectory, "Haven.AutomationWorker.exe");
            var result = await _registration.RegisterAsync(worker, CancellationToken.None);
            StatusText.Text = result.Message;
        };

        _bus.RegisterElement("Automations.Actions.Refresh", RefreshButton);
        _bus.WirePointerEvents("Automations.Actions.Refresh", RefreshButton);
        RefreshButton.Click += async (_, _) =>
        {
            _bus.Fire("Automations.Actions.Refresh");
            await RefreshAsync();
        };

        _bus.RegisterElement("Automations.Actions.Create", CreateButton);
        _bus.WirePointerEvents("Automations.Actions.Create", CreateButton);
        CreateButton.Click += async (_, _) =>
        {
            _bus.Fire("Automations.Actions.Create");
            await CreateAsync();
        };

        ModeCombo.ItemsSource = _modes;
        ModeCombo.SelectedItem = _selectedMode;
        ModeCombo.SelectionChanged += (_, _) =>
        {
            if (ModeCombo.SelectedItem is HavenMode mode) _selectedMode = mode;
        };

        ScheduleKindCombo.ItemsSource = _scheduleKinds;
        ScheduleKindCombo.SelectedItem = _selectedScheduleKind;
        ScheduleKindCombo.SelectionChanged += (_, _) =>
        {
            if (ScheduleKindCombo.SelectedItem is AutomationScheduleKind sk) _selectedScheduleKind = sk;
        };

        ScheduleJsonBox.Text = "{\"time\":\"08:00\"}";
    }

    private async Task RefreshAsync()
    {
        ItemsPanel.Children.Clear();
        StatusText.Text = "Loading…";

        try
        {
            var items = await _repository.GetAllAsync(CancellationToken.None);
            foreach (var item in items)
                ItemsPanel.Children.Add(CreateItemCard(item));
            StatusText.Text = items.Count == 0 ? "No automations yet." : $"{items.Count} automation{(items.Count == 1 ? "" : "s")}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to load: {ex.Message}";
        }
    }

    private Border CreateItemCard(AutomationDefinition def)
    {
        var qName = $"Automations.List.Item{ItemsPanel.Children.Count}";

        var nameBlock = new TextBlock { Text = def.Name, FontWeight = FontWeight.SemiBold };
        var stateLabel = new TextBlock { Text = def.IsEnabled ? "Enabled" : "Paused", Foreground = Brush("HavenAccentBrush"), FontSize = 10 };
        var nameStack = new StackPanel { Children = { nameBlock, stateLabel } };

        var modeBlock = new TextBlock { Text = def.Mode.ToString(), Foreground = Brush("HavenAccentSecondaryBrush"), VerticalAlignment = VerticalAlignment.Center };
        var instructionBlock = new TextBlock { Text = def.Instruction, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center };
        var nextRunBlock = new TextBlock { Text = def.NextRunAt?.LocalDateTime.ToString("g") ?? "Not scheduled", Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };

        var runNowButton = new HavenButton { Content = "Run now" };
        var toggleButton = new HavenButton { Content = def.IsEnabled ? "Pause" : "Resume" };
        var deleteButton = new HoldToConfirmButton { Content = "Delete" };

        runNowButton.RegisterWithEvents($"{qName}.Run", _bus);
        runNowButton.Click += async (_, _) =>
        {
            _bus.Fire($"{qName}.Run");
            await RunNowAsync(def);
        };

        toggleButton.RegisterWithEvents($"{qName}.Toggle", _bus);
        toggleButton.Click += async (_, _) =>
        {
            _bus.Fire($"{qName}.Toggle");
            await ToggleAsync(def);
        };

        deleteButton.RegisterWithEvents($"{qName}.Delete", _bus);
        deleteButton.Click += async (_, _) =>
        {
            _bus.Fire($"{qName}.Delete");
            await DeleteAsync(def);
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("170,90,*,150,Auto,Auto,Auto"),
            ColumnSpacing = 10
        };
        grid.Children.Add(nameStack);
        Grid.SetColumn(modeBlock, 1);
        grid.Children.Add(modeBlock);
        Grid.SetColumn(instructionBlock, 2);
        grid.Children.Add(instructionBlock);
        Grid.SetColumn(nextRunBlock, 3);
        grid.Children.Add(nextRunBlock);
        Grid.SetColumn(runNowButton, 4);
        grid.Children.Add(runNowButton);
        Grid.SetColumn(toggleButton, 5);
        grid.Children.Add(toggleButton);
        Grid.SetColumn(deleteButton, 6);
        grid.Children.Add(deleteButton);

        var border = new HavenAdaptiveSurface { Classes = { "card" }, Margin = new Avalonia.Thickness(0, 0, 0, 10), Child = grid };
        border.PointerEntered += (_, _) => _bus.Fire($"{qName}.Hover");
        border.PointerExited += (_, _) => _bus.Fire($"{qName}.Leave");
        return border;
    }

    private async Task CreateAsync()
    {
        var name = NewNameBox.Text?.Trim() ?? "";
        var instruction = NewInstructionBox.Text?.Trim() ?? "";
        var scheduleJson = ScheduleJsonBox.Text?.Trim() ?? "{\"time\":\"08:00\"}";
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(instruction))
        {
            StatusText.Text = "Name and instruction are required.";
            return;
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            var next = _schedules.GetInitialRun(_selectedScheduleKind, scheduleJson, now);
            var def = new AutomationDefinition(Guid.NewGuid(), name, _selectedMode, instruction, _selectedScheduleKind, scheduleJson, next, null, true, now, now);
            await _repository.UpsertAsync(def, CancellationToken.None);
            NewNameBox.Text = string.Empty;
            NewInstructionBox.Text = string.Empty;
            await RefreshAsync();
            StatusText.Text = "Automation created. Register the background worker once to run it while Haven is closed.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not create automation: {ex.Message}";
        }
    }

    private async Task ToggleAsync(AutomationDefinition def)
    {
        var now = DateTimeOffset.UtcNow;
        var enabled = !def.IsEnabled;
        var next = enabled ? _schedules.GetNextRun(def with { IsEnabled = true }, now) : null;
        await _repository.UpsertAsync(def with { IsEnabled = enabled, NextRunAt = next, UpdatedAt = now }, CancellationToken.None);
        await RefreshAsync();
    }

    private async Task DeleteAsync(AutomationDefinition def)
    {
        await _repository.DeleteAsync(def.Id, CancellationToken.None);
        await RefreshAsync();
        StatusText.Text = "Automation deleted.";
    }

    private async Task RunNowAsync(AutomationDefinition def)
    {
        var now = DateTimeOffset.UtcNow;
        await _repository.UpsertAsync(def with { IsEnabled = true, NextRunAt = now, UpdatedAt = now }, CancellationToken.None);
        var result = await _runner.RunDueAsync(now, CancellationToken.None);
        await RefreshAsync();
        StatusText.Text = $"Run pass: {result.Succeeded} succeeded, {result.Failed} failed, {result.Skipped} skipped.";
    }
}
