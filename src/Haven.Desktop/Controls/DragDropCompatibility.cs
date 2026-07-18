/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Controls/DragDropCompatibility.cs, in the Desktop controls layer, containing reusable Avalonia behavior and visual building blocks.
 * What: This file owns HavenDragDropCompatibility. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

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
    /// <summary>
    /// Stores drag enter event locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public static readonly RoutedEvent<DragEventArgs> DragEnterEvent =
        Avalonia.Input.DragDrop.DragEnterEvent;

    /// <summary>
    /// Stores drag leave event locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public static readonly RoutedEvent<DragEventArgs> DragLeaveEvent =
        Avalonia.Input.DragDrop.DragLeaveEvent;

    /// <summary>
    /// Stores drag over event locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public static readonly RoutedEvent<DragEventArgs> DragOverEvent =
        Avalonia.Input.DragDrop.DragOverEvent;

    /// <summary>
    /// Stores drop event locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public static readonly RoutedEvent<DragEventArgs> DropEvent =
        Avalonia.Input.DragDrop.DropEvent;

    /// <summary>
    /// Performs the set allow drop step owned by this component.
    /// </summary>
    public static void SetAllowDrop(Interactive element, bool value) =>
        Avalonia.Input.DragDrop.SetAllowDrop(element, value);

    /// <summary>
    /// Performs the add drag over handler step owned by this component.
    /// </summary>
    public static void AddDragOverHandler(Interactive element, EventHandler<DragEventArgs> handler) =>
        Avalonia.Input.DragDrop.AddDragOverHandler(element, handler);

    /// <summary>
    /// Performs the add drag leave handler step owned by this component.
    /// </summary>
    public static void AddDragLeaveHandler(Interactive element, EventHandler<DragEventArgs> handler) =>
        Avalonia.Input.DragDrop.AddDragLeaveHandler(element, handler);

    /// <summary>
    /// Performs the add drop handler step owned by this component.
    /// </summary>
    public static void AddDropHandler(Interactive element, EventHandler<DragEventArgs> handler) =>
        Avalonia.Input.DragDrop.AddDropHandler(element, handler);

    /// <summary>
    /// Performs do drag drop async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public static Task<DragDropEffects> DoDragDropAsync(
        PointerPressedEventArgs triggerEvent,
        IDataTransfer dataTransfer,
        DragDropEffects allowedEffects) =>
        Avalonia.Input.DragDrop.DoDragDropAsync(triggerEvent, dataTransfer, allowedEffects);

    /// <summary>
    /// Performs do drag drop async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public static Task<DragDropEffects> DoDragDropAsync(
        PointerEventArgs triggerEvent,
        IDataTransfer dataTransfer,
        DragDropEffects allowedEffects) =>
        triggerEvent is PointerPressedEventArgs pressed
            ? Avalonia.Input.DragDrop.DoDragDropAsync(pressed, dataTransfer, allowedEffects)
            : Task.FromResult(DragDropEffects.None);
}
