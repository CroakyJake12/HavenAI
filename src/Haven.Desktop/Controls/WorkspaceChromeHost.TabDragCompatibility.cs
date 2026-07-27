/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Controls/WorkspaceChromeHost.TabDragCompatibility.cs, in the Desktop controls layer, containing reusable Avalonia behavior and visual building blocks.
 * What: This file owns WorkspaceChromeHost, TabDragCompatibilityState. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Controls;

/// <summary>
/// Represents workspace chrome host and keeps its related state and behavior together.
/// </summary>
public sealed partial class WorkspaceChromeHost
{
    /// <summary>
    /// Stores tab drag compatibility timer locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly DispatcherTimer _tabDragCompatibilityTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(250)
    };

    /// <summary>
    /// Stores tab drag compatibility states locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Dictionary<Button, TabDragCompatibilityState> _tabDragCompatibilityStates = new();

    /// <summary>
    /// Performs the initialize tab drag compatibility step owned by this component.
    /// </summary>
    private void InitializeTabDragCompatibility()
    {
        _tabDragCompatibilityTimer.Tick += OnTabDragCompatibilityTick;
        _tabDragCompatibilityTimer.Start();
        AuditTabDragCompatibility();
    }

    /// <summary>
    /// Performs the dispose tab drag compatibility step owned by this component.
    /// </summary>
    private void DisposeTabDragCompatibility()
    {
        _tabDragCompatibilityTimer.Stop();
        _tabDragCompatibilityTimer.Tick -= OnTabDragCompatibilityTick;

        foreach (var pair in _tabDragCompatibilityStates.ToArray())
            DetachTabDragCompatibility(pair.Key, pair.Value);

        _tabDragCompatibilityStates.Clear();
    }

    /// <summary>
    /// Handles the tab drag compatibility tick event raised by the UI or runtime.
    /// </summary>
    private void OnTabDragCompatibilityTick(object? sender, EventArgs e) =>
        AuditTabDragCompatibility();

    /// <summary>
    /// Performs the audit tab drag compatibility step owned by this component.
    /// </summary>
    private void AuditTabDragCompatibility()
    {
        if (_modernShell is null) return;

        var activeButtons = new HashSet<Button>();
        var count = Math.Min(_modernTabs.Children.Count, _modernShell.OpenTabs.Count);

        for (var index = 0; index < count; index++)
        {
            var host = _modernTabs.Children[index];
            var button = host as Button
                         ?? host.GetVisualDescendants().OfType<Button>().FirstOrDefault();
            if (button is null) continue;

            activeButtons.Add(button);
            if (_tabDragCompatibilityStates.ContainsKey(button)) continue;

            AttachTabDragCompatibility(button, _modernShell.OpenTabs[index]);
        }

        foreach (var pair in _tabDragCompatibilityStates
                     .Where(pair => !activeButtons.Contains(pair.Key))
                     .ToArray())
        {
            DetachTabDragCompatibility(pair.Key, pair.Value);
            _tabDragCompatibilityStates.Remove(pair.Key);
        }
    }

    /// <summary>
    /// Performs the attach tab drag compatibility step owned by this component.
    /// </summary>
    private void AttachTabDragCompatibility(Button button, WorkspaceTabViewModel tab)
    {
        var state = new TabDragCompatibilityState(tab);

        state.PressedHandler = (_, args) =>
        {
            if (!args.GetCurrentPoint(button).Properties.IsLeftButtonPressed) return;

            state.PressedArgs = args;
            state.Start = args.GetPosition(this);
            state.IsDragging = false;
        };

        state.MovedHandler = async (_, args) =>
        {
            if (state.PressedArgs is null || state.IsDragging) return;
            if (!args.GetCurrentPoint(button).Properties.IsLeftButtonPressed)
            {
                state.Reset();
                return;
            }

            var current = args.GetPosition(this);
            if (Math.Abs(current.X - state.Start.X) < 6
                && Math.Abs(current.Y - state.Start.Y) < 6)
                return;

            state.IsDragging = true;
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.CreateText("haven-tab:" + state.Tab.Key));

            try
            {
                await Avalonia.Input.DragDrop.DoDragDropAsync(
                    state.PressedArgs,
                    transfer,
                    DragDropEffects.Move);
            }
            finally
            {
                state.Reset();
            }
        };

        state.ReleasedHandler = (_, _) => state.Reset();

        button.PointerPressed += state.PressedHandler;
        button.PointerMoved += state.MovedHandler;
        button.PointerReleased += state.ReleasedHandler;
        _tabDragCompatibilityStates[button] = state;
    }

    /// <summary>
    /// Performs the detach tab drag compatibility step owned by this component.
    /// </summary>
    private static void DetachTabDragCompatibility(Button button, TabDragCompatibilityState state)
    {
        if (state.PressedHandler is not null)
            button.PointerPressed -= state.PressedHandler;
        if (state.MovedHandler is not null)
            button.PointerMoved -= state.MovedHandler;
        if (state.ReleasedHandler is not null)
            button.PointerReleased -= state.ReleasedHandler;
    }

    // In a lambda declared as (_, args), `out _` binds to the object-typed sender parameter
    // rather than acting as a discard. A generic fallback accepts that existing variable,
    // while calls using `out var` continue to select the concrete string overload.
    private static bool TryReadTabTransfer<T>(IDataTransfer transfer, out T key)
    {
        var succeeded = TryReadTabTransfer(transfer, out string stringKey);
        key = stringKey is T typedKey ? typedKey : default!;
        return succeeded;
    }

    /// <summary>
    /// Represents tab drag compatibility state and keeps its related state and behavior together.
    /// </summary>
    private sealed class TabDragCompatibilityState(WorkspaceTabViewModel tab)
    {
        /// <summary>
        /// Gets or updates tab, the bindable or domain state represented by this property.
        /// </summary>
        public WorkspaceTabViewModel Tab { get; } = tab;
        /// <summary>
        /// Gets or updates pressed args, the bindable or domain state represented by this property.
        /// </summary>
        public PointerPressedEventArgs? PressedArgs { get; set; }
        /// <summary>
        /// Gets or updates start, the bindable or domain state represented by this property.
        /// </summary>
        public Point Start { get; set; }
        /// <summary>
        /// Reports whether dragging applies to the current state.
        /// </summary>
        public bool IsDragging { get; set; }
        /// <summary>
        /// Gets or updates pressed handler, the bindable or domain state represented by this property.
        /// </summary>
        public EventHandler<PointerPressedEventArgs>? PressedHandler { get; set; }
        /// <summary>
        /// Gets or updates moved handler, the bindable or domain state represented by this property.
        /// </summary>
        public EventHandler<PointerEventArgs>? MovedHandler { get; set; }
        /// <summary>
        /// Gets or updates released handler, the bindable or domain state represented by this property.
        /// </summary>
        public EventHandler<PointerReleasedEventArgs>? ReleasedHandler { get; set; }

        /// <summary>
        /// Performs the reset step owned by this component.
        /// </summary>
        public void Reset()
        {
            PressedArgs = null;
            IsDragging = false;
        }
    }
}
