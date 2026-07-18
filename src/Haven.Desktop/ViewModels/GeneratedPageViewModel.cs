/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/ViewModels/GeneratedPageViewModel.cs, in the Desktop presentation-model layer, exposing bindable state and commands to Avalonia views.
 * What: This file owns GeneratedPageViewModel, GeneratedWidgetViewModel, GeneratedShortcutViewModel, GeneratedPageLinkViewModel. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Keeping UI state here makes the XAML declarative and keeps behavior testable without recreating the full window.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.ObjectModel;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Represents generated page view model and keeps its related state and behavior together.
/// </summary>
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

    /// <summary>
    /// Gets or updates definition, the bindable or domain state represented by this property.
    /// </summary>
    public GeneratedPageDefinition Definition { get; }
    /// <summary>
    /// Gets or updates title, the bindable or domain state represented by this property.
    /// </summary>
    public string Title => Definition.Title;
    /// <summary>
    /// Gets or updates description, the bindable or domain state represented by this property.
    /// </summary>
    public string Description => Definition.Description;
    /// <summary>
    /// Gets or updates widgets, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<GeneratedWidgetViewModel> Widgets { get; }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose()
    {
        foreach (var widget in Widgets) widget.Dispose();
    }
}

/// <summary>
/// Represents generated widget view model and keeps its related state and behavior together.
/// </summary>
public sealed class GeneratedWidgetViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Stores definition locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly GeneratedWidgetDefinition _definition;
    /// <summary>
    /// Stores timer locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly DispatcherTimer? _timer;
    /// <summary>
    /// Stores remaining seconds locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int _remainingSeconds;
    /// <summary>
    /// Stores is timer running locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
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

    /// <summary>
    /// Gets or updates title, the bindable or domain state represented by this property.
    /// </summary>
    public string Title { get; }
    /// <summary>
    /// Gets or updates text, the bindable or domain state represented by this property.
    /// </summary>
    public string Text { get; }
    /// <summary>
    /// Gets or updates command button label, the bindable or domain state represented by this property.
    /// </summary>
    public string CommandButtonLabel { get; }
    /// <summary>
    /// Reports whether is text is true for the current state.
    /// </summary>
    public bool IsText { get; }
    /// <summary>
    /// Reports whether is shortcut grid is true for the current state.
    /// </summary>
    public bool IsShortcutGrid { get; }
    /// <summary>
    /// Reports whether is timer is true for the current state.
    /// </summary>
    public bool IsTimer { get; }
    /// <summary>
    /// Reports whether is command button is true for the current state.
    /// </summary>
    public bool IsCommandButton { get; }
    /// <summary>
    /// Reports whether is divider is true for the current state.
    /// </summary>
    public bool IsDivider { get; }
    /// <summary>
    /// Gets or updates shortcuts, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<GeneratedShortcutViewModel> Shortcuts { get; }
    /// <summary>
    /// Gets or updates invoke command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand InvokeCommand { get; }
    /// <summary>
    /// Gets or updates toggle timer command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand ToggleTimerCommand { get; }
    /// <summary>
    /// Gets or updates reset timer command, the bindable or domain state represented by this property.
    /// </summary>
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
    /// <summary>
    /// Reports whether is timer complete is true for the current state.
    /// </summary>
    public bool IsTimerComplete => IsTimer && RemainingSeconds == 0;
    /// <summary>
    /// Gets or updates timer action label, the bindable or domain state represented by this property.
    /// </summary>
    public string TimerActionLabel => IsTimerRunning ? "Pause" : IsTimerComplete ? "Complete" : "Start";
    /// <summary>
    /// Gets or updates time label, the bindable or domain state represented by this property.
    /// </summary>
    public string TimeLabel => TimeSpan.FromSeconds(RemainingSeconds).ToString(RemainingSeconds >= 3600 ? @"hh\:mm\:ss" : @"mm\:ss");
    /// <summary>
    /// Gets or updates timer progress, the bindable or domain state represented by this property.
    /// </summary>
    public double TimerProgress => !IsTimer || _definition.DurationSeconds <= 0
        ? 0
        : 100d * (_definition.DurationSeconds - RemainingSeconds) / _definition.DurationSeconds;

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose() => _timer?.Stop();

    /// <summary>
    /// Performs the toggle timer step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the reset timer step owned by this component.
    /// </summary>
    private void ResetTimer()
    {
        _timer?.Stop();
        IsTimerRunning = false;
        RemainingSeconds = _definition.DurationSeconds;
    }

    /// <summary>
    /// Handles the timer tick event raised by the UI or runtime.
    /// </summary>
    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (RemainingSeconds > 0) RemainingSeconds--;
        if (RemainingSeconds != 0 || _timer is null) return;
        _timer.Stop();
        IsTimerRunning = false;
    }

    /// <summary>
    /// Performs the resolve command step owned by this component.
    /// </summary>
    private static GeneratedCommandDescriptor? ResolveCommand(string? commandId) =>
        string.IsNullOrWhiteSpace(commandId)
            ? null
            : GenerativeUiCatalog.PageCommands.FirstOrDefault(command => command.Id.Equals(commandId, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Represents generated shortcut view model and keeps its related state and behavior together.
/// </summary>
public sealed class GeneratedShortcutViewModel
{
    public GeneratedShortcutViewModel(
        GeneratedCommandDescriptor command,
        Func<string, Task> executeCommand)
    {
        Command = command;
        InvokeCommand = new AsyncRelayCommand(() => executeCommand(command.Id));
    }

    /// <summary>
    /// Gets or updates command, the bindable or domain state represented by this property.
    /// </summary>
    public GeneratedCommandDescriptor Command { get; }
    /// <summary>
    /// Gets or updates title, the bindable or domain state represented by this property.
    /// </summary>
    public string Title => Command.DisplayName;
    /// <summary>
    /// Gets or updates description, the bindable or domain state represented by this property.
    /// </summary>
    public string Description => Command.Description;
    /// <summary>
    /// Gets or updates icon key, the bindable or domain state represented by this property.
    /// </summary>
    public string IconKey => Command.IconKey;
    /// <summary>
    /// Gets or updates invoke command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand InvokeCommand { get; }
}

/// <summary>
/// Represents generated page link view model and keeps its related state and behavior together.
/// </summary>
public sealed record GeneratedPageLinkViewModel(GeneratedPageDefinition Definition)
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public string Id => Definition.Id;
    /// <summary>
    /// Gets or updates title, the bindable or domain state represented by this property.
    /// </summary>
    public string Title => Definition.Title;
    /// <summary>
    /// Gets or updates description, the bindable or domain state represented by this property.
    /// </summary>
    public string Description => Definition.Description;
    /// <summary>
    /// Gets or updates icon key, the bindable or domain state represented by this property.
    /// </summary>
    public string IconKey => Definition.IconKey;
}
