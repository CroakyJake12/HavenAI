using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Controls;

public sealed partial class WorkspaceChromeHost
{
    private readonly DispatcherTimer _tabDragCompatibilityTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(250)
    };

    private readonly Dictionary<Button, TabDragCompatibilityState> _tabDragCompatibilityStates = new();

    private void InitializeTabDragCompatibility()
    {
        _tabDragCompatibilityTimer.Tick += OnTabDragCompatibilityTick;
        _tabDragCompatibilityTimer.Start();
        AuditTabDragCompatibility();
    }

    private void DisposeTabDragCompatibility()
    {
        _tabDragCompatibilityTimer.Stop();
        _tabDragCompatibilityTimer.Tick -= OnTabDragCompatibilityTick;

        foreach (var pair in _tabDragCompatibilityStates.ToArray())
            DetachTabDragCompatibility(pair.Key, pair.Value);

        _tabDragCompatibilityStates.Clear();
    }

    private void OnTabDragCompatibilityTick(object? sender, EventArgs e) =>
        AuditTabDragCompatibility();

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

    private sealed class TabDragCompatibilityState(WorkspaceTabViewModel tab)
    {
        public WorkspaceTabViewModel Tab { get; } = tab;
        public PointerPressedEventArgs? PressedArgs { get; set; }
        public Point Start { get; set; }
        public bool IsDragging { get; set; }
        public EventHandler<PointerPressedEventArgs>? PressedHandler { get; set; }
        public EventHandler<PointerEventArgs>? MovedHandler { get; set; }
        public EventHandler<PointerReleasedEventArgs>? ReleasedHandler { get; set; }

        public void Reset()
        {
            PressedArgs = null;
            IsDragging = false;
        }
    }
}
