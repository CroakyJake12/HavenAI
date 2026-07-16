using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views;

public sealed partial class PlanView : UserControl
{
    private PlanAutomationControl? _automationControl;

    public PlanView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => InstallAutomationControl();
        DetachedFromVisualTree += (_, _) =>
        {
            _automationControl?.Dispose();
            _automationControl = null;
        };
    }

    private void InstallAutomationControl()
    {
        if (_automationControl is not null || Content is not Grid root) return;
        _automationControl = new PlanAutomationControl
        {
            Margin = new Thickness(0, 22, 22, 0)
        };
        Grid.SetColumn(_automationControl, 2);
        Panel.SetZIndex(_automationControl, 20);
        root.Children.Add(_automationControl);
    }

    private async void TaskDrag_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: PlannerTaskItemViewModel task }) return;
        await StartDragAsync(e, $"haven-plan:task:{task.Id:D}");
    }

    private async void CalendarEntryDrag_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: PlannerCalendarEntryViewModel item }) return;
        await StartDragAsync(e, $"haven-plan:{(item.IsEvent ? "event" : "task")}:{item.Id:D}");
    }

    private static async Task StartDragAsync(PointerPressedEventArgs e, string value)
    {
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateText(value));
        await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Move);
    }

    private void PlannerDropTarget_OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = TryReadDraggedItem(e.DataTransfer, out _, out _) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private async void CalendarDay_OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not PlanPageViewModel planner || sender is not Control { DataContext: PlannerCalendarDayViewModel day }
            || !TryReadDraggedItem(e.DataTransfer, out var kind, out var id)) return;
        e.Handled = true;
        if (kind == "task") await planner.RescheduleTaskAsync(id, day.Date);
        else if (kind == "event") await planner.RescheduleEventAsync(id, day.Date);
    }

    private async void BoardColumn_OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not PlanPageViewModel planner || sender is not Control { DataContext: PlannerBoardColumnViewModel column }
            || !TryReadDraggedItem(e.DataTransfer, out var kind, out var id) || kind != "task") return;
        e.Handled = true;
        await planner.MoveTaskToStatusAsync(id, column.Status);
    }

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
