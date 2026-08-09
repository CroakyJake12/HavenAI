using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Haven.Desktop.Controls;

namespace Haven.Desktop.Views.Shell.TopRail;

/// <summary>
/// Fixed Capabilities entry point for the top rail. Pinned and recommended capabilities live
/// inside the contextual flyout so the header never changes width unexpectedly.
/// </summary>
public sealed class DynamicActionToolbar : StackPanel, IDisposable
{
    private readonly List<ToolbarAction> _availableActions = [];
    private readonly Button _actionsButton;
    private ActionsFlyoutControl? _flyoutControl;
    private Flyout? _flyout;
    private Action? _editActions;
    private bool _disposed;

    public DynamicActionToolbar()
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        _actionsButton = new HavenButton
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                Children =
                {
                    new HavenIcon { IconKey = "bolt", Width = 19, Height = 19 },
                    new TextBlock
                    {
                        Text = "Capabilities",
                        FontWeight = Avalonia.Media.FontWeight.ExtraBold,
                        FontSize = 14,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            },
            Height = 42,
            Padding = new Thickness(16, 0),
            CornerRadius = new CornerRadius(21),
            VerticalAlignment = VerticalAlignment.Center
        };
        _actionsButton.Classes.Add("headerPill");
        _actionsButton.Classes.Add("accent");
        ToolTip.SetTip(_actionsButton, "Capabilities · Ctrl+K");
        _actionsButton.Click += (_, _) =>
        {
            ActionsClicked?.Invoke(this, EventArgs.Empty);
            ShowActionsFlyout();
        };
        Children.Add(_actionsButton);
    }

    public event EventHandler? ActionsClicked;

    public void SetActions(IReadOnlyList<ToolbarAction> actions)
    {
        _availableActions.Clear();
        _availableActions.AddRange(actions);
        _flyoutControl?.SetActions(_availableActions);
    }

    public void SetEditActionsHandler(Action onExecute)
    {
        _editActions = onExecute;
        _flyoutControl?.SetEditActionsHandler(onExecute);
    }

    public void ShowActionsFlyout()
    {
        _flyoutControl ??= CreateFlyoutControl();
        _flyoutControl.SetActions(_availableActions);
        _flyout ??= new HavenAdaptivePopup
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            FlyoutPresenterTheme = Avalonia.Application.Current?.TryFindResource(
                "HavenFloatingFlyoutPresenterTheme", out var theme) == true
                    ? theme as Avalonia.Styling.ControlTheme
                    : null,
            Content = _flyoutControl
        };
        _flyout.ShowAt(_actionsButton);
        _flyoutControl.FocusSearch();
    }

    private ActionsFlyoutControl CreateFlyoutControl()
    {
        var control = new ActionsFlyoutControl();
        control.SetEditActionsHandler(() => _editActions?.Invoke());
        control.ActionInvoked += (_, _) => _flyout?.Hide();
        return control;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _flyout?.Hide();
        _availableActions.Clear();
        _editActions = null;
    }

    public sealed record ToolbarAction(
        string Label,
        string IconKey,
        Action OnExecute,
        string? Tooltip = null,
        string Category = "",
        string Description = "",
        string Shortcut = "",
        bool IsFeatured = false);
}
