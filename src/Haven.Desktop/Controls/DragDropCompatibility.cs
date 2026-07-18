using Avalonia.Input;
using Avalonia.Interactivity;

namespace Haven.Desktop.Controls;

/// <summary>
/// Keeps the existing control code source-compatible with Avalonia 12, where the public
/// drag source API requires PointerPressedEventArgs. Pointer-move calls are deliberately
/// ignored; WorkspaceChromeHost.TabDragCompatibility starts the real operation with the
/// original pressed event after the movement threshold has been crossed.
/// </summary>
internal static class HavenDragDropCompatibility
{
    // The project-wide DragDrop alias also applies to routed-event registrations. Forward
    // Avalonia's event fields so existing AddHandler/RemoveHandler calls keep compiling.
    public static readonly RoutedEvent<DragEventArgs> DragEnterEvent =
        Avalonia.Input.DragDrop.DragEnterEvent;

    public static readonly RoutedEvent<DragEventArgs> DragLeaveEvent =
        Avalonia.Input.DragDrop.DragLeaveEvent;

    public static readonly RoutedEvent<DragEventArgs> DragOverEvent =
        Avalonia.Input.DragDrop.DragOverEvent;

    public static readonly RoutedEvent<DragEventArgs> DropEvent =
        Avalonia.Input.DragDrop.DropEvent;

    public static void SetAllowDrop(Interactive element, bool value) =>
        Avalonia.Input.DragDrop.SetAllowDrop(element, value);

    public static void AddDragOverHandler(Interactive element, EventHandler<DragEventArgs> handler) =>
        Avalonia.Input.DragDrop.AddDragOverHandler(element, handler);

    public static void AddDragLeaveHandler(Interactive element, EventHandler<DragEventArgs> handler) =>
        Avalonia.Input.DragDrop.AddDragLeaveHandler(element, handler);

    public static void AddDropHandler(Interactive element, EventHandler<DragEventArgs> handler) =>
        Avalonia.Input.DragDrop.AddDropHandler(element, handler);

    public static Task<DragDropEffects> DoDragDropAsync(
        PointerPressedEventArgs triggerEvent,
        IDataTransfer dataTransfer,
        DragDropEffects allowedEffects) =>
        Avalonia.Input.DragDrop.DoDragDropAsync(triggerEvent, dataTransfer, allowedEffects);

    public static Task<DragDropEffects> DoDragDropAsync(
        PointerEventArgs triggerEvent,
        IDataTransfer dataTransfer,
        DragDropEffects allowedEffects) =>
        triggerEvent is PointerPressedEventArgs pressed
            ? Avalonia.Input.DragDrop.DoDragDropAsync(pressed, dataTransfer, allowedEffects)
            : Task.FromResult(DragDropEffects.None);
}
