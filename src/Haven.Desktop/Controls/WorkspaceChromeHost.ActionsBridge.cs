/*
 * FILE DOCUMENTATION
 * Where: src/Haven.OldHaven/Controls/WorkspaceChromeHost.ActionsBridge.cs, in the Desktop controls layer, containing reusable Avalonia behavior and visual building blocks.
 * What: This file owns WorkspaceChromeHost. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia;
using Avalonia.Interactivity;

namespace Haven.Desktop.Controls;

/// <summary>
/// Represents workspace chrome host and keeps its related state and behavior together.
/// </summary>
public sealed partial class WorkspaceChromeHost
{
    /// <summary>
    /// Stores actions bridge handler locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private EventHandler<RoutedEventArgs>? _actionsBridgeHandler;

    /// <summary>
    /// Performs the initialize actions bridge step owned by this component.
    /// </summary>
    private void InitializeActionsBridge()
    {
        _actionsSearch.Padding = new Thickness(36, 9, 12, 9);
        if (_actionsButton is null) return;

        // The button must use the same command path as Ctrl+K so the view model builds and
        // filters its command collection before the modern flyout is shown. Keeping the
        // flyout detached also prevents Avalonia from opening it a second time automatically.
        _actionsButton.Flyout = null;
        _actionsBridgeHandler = (_, _) =>
        {
            if (_modernShell?.OpenCommandPaletteCommand.CanExecute(null) == true)
            {
                _modernShell.OpenCommandPaletteCommand.Execute(null);
                return;
            }

            if (_actionsFlyout is null || _actionsButton is null) return;
            RebuildActions();
            _actionsFlyout.ShowAt(_actionsButton);
        };
        _actionsButton.Click += _actionsBridgeHandler;
    }

    /// <summary>
    /// Performs the dispose actions bridge step owned by this component.
    /// </summary>
    private void DisposeActionsBridge()
    {
        if (_actionsButton is not null && _actionsBridgeHandler is not null)
            _actionsButton.Click -= _actionsBridgeHandler;
        _actionsBridgeHandler = null;
    }
}
