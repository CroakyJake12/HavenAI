using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Desktop.Controls;

namespace Haven.Desktop.Views.Shell.TopRail;

/// <summary>
/// Dynamic action toolbar rendered on the right side of the TopRail.
/// Pinned and contextually relevant actions are displayed as compact icons.
/// The static "Actions" button is always present and opens the full actions flyout.
/// </summary>
public sealed class DynamicActionToolbar : StackPanel, IDisposable
{
    private readonly List<ToolbarAction> _pinnedActions = [];
    private readonly List<ToolbarAction> _contextActions = [];
    private readonly Button _actionsButton;
    private Flyout? _actionsFlyout;
    private TextBox? _searchBox;
    private StackPanel? _menuItems;
    private bool _disposed;

    /// <summary>
    /// Raised when the user clicks the static "Actions" button.
    /// </summary>
    public event EventHandler? ActionsClicked;

    public DynamicActionToolbar()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 3;
        VerticalAlignment = VerticalAlignment.Center;

        _actionsButton = BuildActionsButton();
        Children.Add(_actionsButton);

        BuildDefaultPinnedActions();
        RebuildPinnedActions();
    }

    /// <summary>
    /// Pins an action to the toolbar.
    /// </summary>
    public void PinAction(string label, string iconKey, Action onExecute, string? tooltip = null)
    {
        _pinnedActions.Add(new ToolbarAction(label, iconKey, onExecute, tooltip));
        RebuildPinnedActions();
    }

    /// <summary>
    /// Removes a pinned action by label.
    /// </summary>
    public void UnpinAction(string label)
    {
        _pinnedActions.RemoveAll(a => a.Label == label);
        RebuildPinnedActions();
    }

    /// <summary>
    /// Updates the contextually relevant actions shown based on the current surface.
    /// </summary>
    public void SetContextActions(IReadOnlyList<ToolbarAction> actions)
    {
        _contextActions.Clear();
        _contextActions.AddRange(actions);
        RebuildPinnedActions();
    }

    /// <summary>
    /// Shows the full actions dropdown.
    /// </summary>
    public void ShowActionsFlyout()
    {
        if (_actionsFlyout is null) _actionsFlyout = BuildActionsFlyout();
        _actionsFlyout.ShowAt(_actionsButton);
    }

    private void BuildDefaultPinnedActions()
    {
        _pinnedActions.Add(new ToolbarAction("Voice Session", "mic", () => { }, "Start a voice session"));
        _pinnedActions.Add(new ToolbarAction("Notifications", "bell", () => { }, "Open notifications"));
    }

    private Button BuildActionsButton()
    {
        var btn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new HavenIcon { IconKey = "bolt", Width = 16, Height = 16 },
                    new TextBlock { Text = "Actions", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 },
                    new HavenIcon { IconKey = "chevron-down", Width = 11, Height = 11, Opacity = 0.6 }
                }
            },
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(12, 6)
        };
        btn.Classes.Add("chrome");
        ToolTip.SetTip(btn, "Actions menu \u00b7 Ctrl+K");
        btn.Click += (_, _) =>
        {
            ActionsClicked?.Invoke(this, EventArgs.Empty);
            ShowActionsFlyout();
        };
        return btn;
    }

    private void RebuildPinnedActions()
    {
        while (Children.Count > 1) Children.RemoveAt(0);

        foreach (var action in _pinnedActions.Concat(_contextActions))
        {
            var btn = new Button
            {
                Content = new HavenIcon { IconKey = action.IconKey, Width = 16, Height = 16 },
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            btn.Classes.Add("chrome");
            ToolTip.SetTip(btn, action.Tooltip ?? action.Label);
            var captured = action;
            btn.Click += (_, _) => captured.OnExecute();
            Children.Add(btn);
        }
    }

    private Flyout BuildActionsFlyout()
    {
        _searchBox = new TextBox { PlaceholderText = "Search actions", MinWidth = 280, Padding = new Thickness(36, 9, 12, 9) };
        _menuItems = new StackPanel { Spacing = 2 };

        var defaultItems = new (string Label, string IconKey, Action Execute)[]
        {
            ("Start Voice Session", "mic", () => { }),
            ("Open Notifications", "bell", () => { }),
            ("Open App (New Tab)", "plus", () => { }),
            ("Open App (Current Tab)", "home", () => { }),
            ("Settings", "settings", () => { }),
        };

        foreach (var item in defaultItems)
        {
            _menuItems.Children.Add(BuildMenuItem(item.Label, item.IconKey, item.Execute));
        }

        var editItem = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children =
                {
                    new HavenIcon { IconKey = "edit", Width = 16, Height = 16, Opacity = 0.76 },
                    new TextBlock { Text = "Edit Actions & Toolbar", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }
                }
            }
        };
        editItem.Classes.Add("sidebar");

        var searchHost = new Grid();
        searchHost.Children.Add(_searchBox);
        searchHost.Children.Add(new HavenIcon
        {
            IconKey = "search",
            Width = 15,
            Height = 15,
            Margin = new Thickness(12, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.65,
            IsHitTestVisible = false
        });

        var scrollContent = new StackPanel
        {
            Width = 320,
            Spacing = 6,
            Margin = new Thickness(8),
            Children =
            {
                searchHost,
                new ScrollViewer
                {
                    MaxHeight = 400,
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                    Content = _menuItems
                },
                new Border { Height = 1, Margin = new Thickness(4, 2), Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)) },
                editItem
            }
        };

        _searchBox.TextChanged += (_, _) => FilterMenuItems();

        return new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            Content = scrollContent
        };
    }

    private Button BuildMenuItem(string label, string iconKey, Action execute)
    {
        var btn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                ColumnSpacing = 10,
                Children =
                {
                    new HavenIcon { IconKey = iconKey, Width = 16, Height = 16, Opacity = 0.76, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }
                }
            }
        };
        btn.Classes.Add("sidebar");
        btn.Click += (_, _) =>
        {
            _actionsFlyout?.Hide();
            execute();
        };
        return btn;
    }

    private void FilterMenuItems()
    {
        if (_menuItems is null || _searchBox is null) return;
        var query = _searchBox.Text?.Trim() ?? string.Empty;
        foreach (var child in _menuItems.Children.OfType<Button>())
        {
            var label = child.Content is Grid grid
                ? grid.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? string.Empty
                : string.Empty;
            child.IsVisible = string.IsNullOrWhiteSpace(query) ||
                              label.Contains(query, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pinnedActions.Clear();
        _contextActions.Clear();
    }

    public sealed record ToolbarAction(string Label, string IconKey, Action OnExecute, string? Tooltip = null);
}
