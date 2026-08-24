/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Views/PlanView.axaml.cs, in the Desktop view layer, where Avalonia controls connect XAML interaction to view models.
 * What: This file owns PlanView. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views;

/// <summary>
/// Represents plan view and keeps its related state and behavior together.
/// </summary>
public sealed partial class PlanView : UserControl
{
    /// <summary>
    /// Stores automation control locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private PlanScheduledTaskControl? _automationControl;

    public PlanView()
    {
        InitializeComponent();
        SizeChanged += (_, args) => ApplyResponsiveLayout(args.NewSize.Width);
        AttachedToVisualTree += (_, _) =>
        {
            InstallAutomationControl();
            ApplyResponsiveLayout(Bounds.Width);
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _automationControl?.Dispose();
            _automationControl = null;
        };
    }

    private void ApplyResponsiveLayout(double width)
    {
        if (width <= 0) return;
        var compact = width < 900;
        CollectionSidebar.IsVisible = !compact;
        CompactCollectionPicker.IsVisible = compact;
        RootGrid.ColumnDefinitions = new ColumnDefinitions(compact ? "0,*" : "220,*");
        PlannerInspector.Width = compact ? double.NaN : 360;
        PlannerInspector.HorizontalAlignment = compact ? Avalonia.Layout.HorizontalAlignment.Stretch : Avalonia.Layout.HorizontalAlignment.Right;
        PlannerInspector.Margin = compact ? new Thickness(12) : new Thickness(0);
    }

    /// <summary>
    /// Performs the install automation control step owned by this component.
    /// </summary>
    private void InstallAutomationControl()
    {
        if (_automationControl is not null) return;
        var host = this.FindControl<Grid>("ScheduledTaskHost");
        if (host is null) return;
        _automationControl = new PlanScheduledTaskControl
        {
            Margin = new Thickness(0),
            ZIndex = 20
        };
        host.Children.Add(_automationControl);
    }

    /// <summary>
    /// Performs the task drag_on pointer pressed step owned by this component.
    /// </summary>
    private async void TaskDrag_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: PlannerTaskItemViewModel task }) return;
        await StartDragAsync(e, $"haven-plan:task:{task.Id:D}");
    }

    /// <summary>
    /// Performs the calendar entry drag_on pointer pressed step owned by this component.
    /// </summary>
    private async void CalendarEntryDrag_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: PlannerCalendarEntryViewModel item }) return;
        await StartDragAsync(e, $"haven-plan:{(item.IsEvent ? "event" : "task")}:{item.Id:D}");
    }

    /// <summary>
    /// Performs start drag asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task StartDragAsync(PointerPressedEventArgs e, string value)
    {
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateText(value));
        await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Move);
    }

    /// <summary>
    /// Performs the planner drop target_on drag over step owned by this component.
    /// </summary>
    private void PlannerDropTarget_OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = TryReadDraggedItem(e.DataTransfer, out _, out _) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>
    /// Performs the calendar day_on drop step owned by this component.
    /// </summary>
    private async void CalendarDay_OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not PlanPageViewModel planner || sender is not Control { DataContext: PlannerCalendarDayViewModel day }
            || !TryReadDraggedItem(e.DataTransfer, out var kind, out var id)) return;
        e.Handled = true;
        if (kind == "task") await planner.RescheduleTaskAsync(id, day.Date);
        else if (kind == "event") await planner.RescheduleEventAsync(id, day.Date);
    }

    /// <summary>
    /// Performs the board column_on drop step owned by this component.
    /// </summary>
    private async void BoardColumn_OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not PlanPageViewModel planner || sender is not Control { DataContext: PlannerBoardColumnViewModel column }
            || !TryReadDraggedItem(e.DataTransfer, out var kind, out var id) || kind != "task") return;
        e.Handled = true;
        await planner.MoveTaskToStatusAsync(id, column.Status);
    }

    /// <summary>
    /// Attempts to read dragged item and reports the result without using failure for normal control flow.
    /// </summary>
    private static bool TryReadDraggedItem(IDataTransfer data, out string kind, out Guid id)
    {
        kind = string.Empty;
        id = Guid.Empty;
        var text = data.TryGetText();
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || parts[0] != "haven-plan" || parts[1] is not ("task" or "event") || !Guid.TryParse(parts[2], out id)) return false;
        kind = parts[1];
        return true;
    }
}
