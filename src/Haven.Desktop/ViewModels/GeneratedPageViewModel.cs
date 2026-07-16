using System.Collections.ObjectModel;
using Avalonia.Threading;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

public sealed class GeneratedPageViewModel : ObservableObject, IDisposable
{
    public GeneratedPageViewModel(
        GeneratedPageDefinition definition,
        Func<string, Task> executeCommand)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Widgets = new ObservableCollection<GeneratedWidgetViewModel>(
            definition.Widgets.Select(widget => new GeneratedWidgetViewModel(widget, executeCommand)));
    }

    public GeneratedPageDefinition Definition { get; }
    public string Title => Definition.Title;
    public string Description => Definition.Description;
    public ObservableCollection<GeneratedWidgetViewModel> Widgets { get; }

    public void Dispose()
    {
        foreach (var widget in Widgets) widget.Dispose();
    }
}

public sealed class GeneratedWidgetViewModel : ObservableObject, IDisposable
{
    private readonly GeneratedWidgetDefinition _definition;
    private readonly DispatcherTimer? _timer;
    private int _remainingSeconds;
    private bool _isTimerRunning;

    public GeneratedWidgetViewModel(
        GeneratedWidgetDefinition definition,
        Func<string, Task> executeCommand)
    {
        _definition = definition;
        Title = definition.Title;
        Text = definition.Text ?? string.Empty;
        IsText = definition.Kind == GeneratedWidgetKind.Text;
        IsShortcutGrid = definition.Kind == GeneratedWidgetKind.ShortcutGrid;
        IsTimer = definition.Kind == GeneratedWidgetKind.Timer;
        IsCommandButton = definition.Kind == GeneratedWidgetKind.CommandButton;
        IsDivider = definition.Kind == GeneratedWidgetKind.Divider;
        CommandButtonLabel = ResolveCommand(definition.CommandId)?.DisplayName ?? definition.Title;
        InvokeCommand = new AsyncRelayCommand(async () =>
        {
            if (!string.IsNullOrWhiteSpace(definition.CommandId)) await executeCommand(definition.CommandId);
        }, () => IsCommandButton && !string.IsNullOrWhiteSpace(definition.CommandId));
        Shortcuts = new ObservableCollection<GeneratedShortcutViewModel>(
            definition.ShortcutCommandIds
                .Select(ResolveCommand)
                .Where(command => command is not null)
                .Select(command => new GeneratedShortcutViewModel(command!, executeCommand)));

        if (IsTimer)
        {
            _remainingSeconds = definition.DurationSeconds;
            _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, OnTimerTick);
        }
        ToggleTimerCommand = new RelayCommand(ToggleTimer, () => IsTimer && RemainingSeconds > 0);
        ResetTimerCommand = new RelayCommand(ResetTimer, () => IsTimer);
    }

    public string Title { get; }
    public string Text { get; }
    public string CommandButtonLabel { get; }
    public bool IsText { get; }
    public bool IsShortcutGrid { get; }
    public bool IsTimer { get; }
    public bool IsCommandButton { get; }
    public bool IsDivider { get; }
    public ObservableCollection<GeneratedShortcutViewModel> Shortcuts { get; }
    public AsyncRelayCommand InvokeCommand { get; }
    public RelayCommand ToggleTimerCommand { get; }
    public RelayCommand ResetTimerCommand { get; }
    public int RemainingSeconds
    {
        get => _remainingSeconds;
        private set
        {
            if (!SetProperty(ref _remainingSeconds, Math.Max(0, value))) return;
            RaisePropertyChanged(nameof(TimeLabel));
            RaisePropertyChanged(nameof(TimerProgress));
            RaisePropertyChanged(nameof(IsTimerComplete));
            ToggleTimerCommand.RaiseCanExecuteChanged();
        }
    }
    public bool IsTimerRunning
    {
        get => _isTimerRunning;
        private set
        {
            if (!SetProperty(ref _isTimerRunning, value)) return;
            RaisePropertyChanged(nameof(TimerActionLabel));
        }
    }
    public bool IsTimerComplete => IsTimer && RemainingSeconds == 0;
    public string TimerActionLabel => IsTimerRunning ? "Pause" : IsTimerComplete ? "Complete" : "Start";
    public string TimeLabel => TimeSpan.FromSeconds(RemainingSeconds).ToString(RemainingSeconds >= 3600 ? @"hh\:mm\:ss" : @"mm\:ss");
    public double TimerProgress => !IsTimer || _definition.DurationSeconds <= 0
        ? 0
        : 100d * (_definition.DurationSeconds - RemainingSeconds) / _definition.DurationSeconds;

    public void Dispose() => _timer?.Stop();

    private void ToggleTimer()
    {
        if (_timer is null || RemainingSeconds == 0) return;
        if (IsTimerRunning)
        {
            _timer.Stop();
            IsTimerRunning = false;
        }
        else
        {
            _timer.Start();
            IsTimerRunning = true;
        }
    }

    private void ResetTimer()
    {
        _timer?.Stop();
        IsTimerRunning = false;
        RemainingSeconds = _definition.DurationSeconds;
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (RemainingSeconds > 0) RemainingSeconds--;
        if (RemainingSeconds != 0 || _timer is null) return;
        _timer.Stop();
        IsTimerRunning = false;
    }

    private static GeneratedCommandDescriptor? ResolveCommand(string? commandId) =>
        string.IsNullOrWhiteSpace(commandId)
            ? null
            : GenerativeUiCatalog.PageCommands.FirstOrDefault(command => command.Id.Equals(commandId, StringComparison.OrdinalIgnoreCase));
}

public sealed class GeneratedShortcutViewModel
{
    public GeneratedShortcutViewModel(
        GeneratedCommandDescriptor command,
        Func<string, Task> executeCommand)
    {
        Command = command;
        InvokeCommand = new AsyncRelayCommand(() => executeCommand(command.Id));
    }

    public GeneratedCommandDescriptor Command { get; }
    public string Title => Command.DisplayName;
    public string Description => Command.Description;
    public string IconKey => Command.IconKey;
    public AsyncRelayCommand InvokeCommand { get; }
}

public sealed record GeneratedPageLinkViewModel(GeneratedPageDefinition Definition)
{
    public string Id => Definition.Id;
    public string Title => Definition.Title;
    public string Description => Definition.Description;
    public string IconKey => Definition.IconKey;
}
