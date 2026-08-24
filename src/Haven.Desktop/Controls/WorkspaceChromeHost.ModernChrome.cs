/*
 * FILE DOCUMENTATION
 * Where: src/Haven.OldHaven/Controls/WorkspaceChromeHost.ModernChrome.cs, in the Desktop controls layer, containing reusable Avalonia behavior and visual building blocks.
 * What: This file owns WorkspaceChromeHost. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Core;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Shell;
using Haven.Desktop.Views.Pages.Browser;

namespace Haven.Desktop.Controls;

/// <summary>
/// Represents workspace chrome host and keeps its related state and behavior together.
/// </summary>
public sealed partial class WorkspaceChromeHost
{
    /// <summary>
    /// Stores action categories locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly string[] ActionCategories = ["File", "Edit", "View", "Chat", "Project", "Tools", "Help"];

    /// <summary>
    /// Stores modern tabs locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly StackPanel _modernTabs = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 3,
        VerticalAlignment = VerticalAlignment.Center
    };

    /// <summary>
    /// Stores model status text locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TextBlock _modelStatusText = new()
    {
        Text = "Connecting to Ollama",
        FontSize = 10,
        MaxWidth = 235,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly Button _backButton = new()
    {
        Content = new HavenIcon { IconKey = "chevron-left", Width = 14, Height = 14 },
        Width = 30,
        Height = 30,
        Padding = new Thickness(0),
        VerticalContentAlignment = VerticalAlignment.Center,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };
    private readonly Button _forwardButton = new()
    {
        Content = new HavenIcon { IconKey = "chevron-right", Width = 14, Height = 14 },
        Width = 30,
        Height = 30,
        Padding = new Thickness(0),
        VerticalContentAlignment = VerticalAlignment.Center,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };

    /// <summary>
    /// Stores model refresh icon locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly HavenIcon _modelRefreshIcon = new()
    {
        IconKey = "refresh",
        Width = 14,
        Height = 14,
        Opacity = 0.68,
        VerticalAlignment = VerticalAlignment.Center
    };

    /// <summary>
    /// Stores actions search locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TextBox _actionsSearch = new()
    {
        PlaceholderText = "Search actions",
        MinHeight = 36
    };

    /// <summary>
    /// Stores actions sections locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly StackPanel _actionsSections = new() { Spacing = 5 };
    /// <summary>
    /// Stores rail audit timer locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly DispatcherTimer _railAuditTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    /// <summary>
    /// Stores observed tabs locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly List<WorkspaceTabViewModel> _observedTabs = [];
    /// <summary>
    /// Stores direct rail buttons locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly HashSet<Button> _directRailButtons = [];

    /// <summary>
    /// Stores modern shell locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private MainView? _modernShell;
    /// <summary>
    /// Stores actions button locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private Button? _actionsButton;
    /// <summary>
    /// Stores actions flyout locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private Flyout? _actionsFlyout;
    /// <summary>
    /// Stores status debounce locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private CancellationTokenSource? _statusDebounce;
    /// <summary>
    /// Stores tab drag candidate locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private WorkspaceTabViewModel? _tabDragCandidate;
    /// <summary>
    /// Stores tab drag start locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private Point _tabDragStart;
    /// <summary>
    /// Stores tab drag in progress locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _tabDragInProgress;
    /// <summary>
    /// Stores suppress palette redirect locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _suppressPaletteRedirect;
    /// <summary>
    /// Stores updating action search locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _updatingActionSearch;
    /// <summary>
    /// Stores actions rebuild queued locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _actionsRebuildQueued;
    /// <summary>
    /// Stores latest raw status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _latestRawStatus = string.Empty;
    /// <summary>
    /// Stores last confirmed model status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _lastConfirmedModelStatus = string.Empty;

    /// <summary>
    /// Builds modern top bar from the currently available inputs.
    /// </summary>
    private Border BuildModernTopBar()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"),
            ColumnSpacing = 10,
            Margin = new Thickness(18, 7, 12, 6)
        };

        grid.Children.Add(new TextBlock
        {
            Text = "Haven",
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 8, 0)
        });

        _backButton.Classes.Add("chrome");
        _forwardButton.Classes.Add("chrome");
        ToolTip.SetTip(_backButton, "Back");
        ToolTip.SetTip(_forwardButton, "Forward");
        _backButton.Click += (_, _) =>
        {
            if (_modernShell?.NavigateBackCommand.CanExecute(null) == true)
                _modernShell.NavigateBackCommand.Execute(null);
        };
        _forwardButton.Click += (_, _) =>
        {
            if (_modernShell?.NavigateForwardCommand.CanExecute(null) == true)
                _modernShell.NavigateForwardCommand.Execute(null);
        };
        var navigation = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _backButton, _forwardButton }
        };
        Grid.SetColumn(navigation, 1);
        grid.Children.Add(navigation);

        var addTab = new HavenButton
        {
            Content = new HavenIcon { IconKey = "plus", Width = 15, Height = 15 },
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        };
        addTab.Classes.Add("chrome");
        ToolTip.SetTip(addTab, "New Home tab");
        addTab.Click += async (_, _) => await OpenFreshHomeTabAsync();

        var tabArea = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 4,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        tabArea.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = _modernTabs
        });
        Grid.SetColumn(addTab, 1);
        tabArea.Children.Add(addTab);
        Grid.SetColumn(tabArea, 2);
        grid.Children.Add(tabArea);

        var modelStatus = new HavenButton
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                Children = { _modelStatusText, _modelRefreshIcon }
            },
            VerticalAlignment = VerticalAlignment.Center
        };
        modelStatus.Classes.Add("status");
        ToolTip.SetTip(modelStatus, "Refresh local models");
        modelStatus.Click += (_, _) =>
        {
            if (_modernShell?.RefreshModelsCommand.CanExecute(null) == true)
                _modernShell.RefreshModelsCommand.Execute(null);
            _ = ShowModelRefreshPulseAsync();
        };
        Grid.SetColumn(modelStatus, 3);
        grid.Children.Add(modelStatus);

        _actionsButton = new HavenButton
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Actions", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
                    new HavenIcon { IconKey = "chevron-down", Width = 13, Height = 13, VerticalAlignment = VerticalAlignment.Center }
                }
            },
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(15, 8)
        };
        _actionsButton.Classes.Add("primary");
        ToolTip.SetTip(_actionsButton, "Search Haven actions · Ctrl+K");
        _actionsFlyout = BuildActionsFlyout();
        _actionsButton.Flyout = _actionsFlyout;
        _actionsButton.Click += (_, _) =>
        {
            RebuildActions();
            Dispatcher.UIThread.Post(() => _actionsSearch.Focus());
        };
        Grid.SetColumn(_actionsButton, 4);
        grid.Children.Add(_actionsButton);

        return new HavenAdaptiveSurface
        {
            Background = ModernResourceBrush("HavenPanelBrush", Color.FromArgb(245, 38, 45, 61)),
            BorderBrush = ModernResourceBrush("HavenLineBrush", Color.FromArgb(54, 255, 255, 255)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid
        };
    }

    /// <summary>
    /// Builds actions flyout from the currently available inputs.
    /// </summary>
    private Flyout BuildActionsFlyout()
    {
        _actionsSearch.TextChanged += OnActionsSearchChanged;

        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        heading.Children.Add(new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = "Actions", FontSize = 18, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = "Search commands or browse a collapsible section", Classes = { "muted" }, FontSize = 10 }
            }
        });
        heading.Children.Add(WithModernColumn(new TextBlock
        {
            Text = "Ctrl+K",
            Classes = { "muted" },
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center
        }, 1));

        var searchHost = new Grid();
        searchHost.Children.Add(_actionsSearch);
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

        var content = new StackPanel
        {
            Width = 560,
            Spacing = 10,
            Margin = new Thickness(10),
            Children =
            {
                heading,
                searchHost,
                new ScrollViewer
                {
                    MaxHeight = 520,
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                    Content = _actionsSections
                }
            }
        };

        return new HavenAdaptivePopup
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            FlyoutPresenterTheme = Avalonia.Application.Current?.Resources["HavenAcrylicFlyoutPresenterTheme"] as ControlTheme,
            Content = content
        };
    }

    /// <summary>
    /// Performs the initialize modern chrome step owned by this component.
    /// </summary>
    private void InitializeModernChrome()
    {
        DataContextChanged += OnModernDataContextChanged;
        AttachedToVisualTree += OnModernAttachedToVisualTree;
        _railAuditTimer.Tick += OnRailAuditTimerTick;
        _railAuditTimer.Start();
        AttachModernShell(DataContext as MainView);
    }

    /// <summary>
    /// Performs the dispose modern chrome step owned by this component.
    /// </summary>
    private void DisposeModernChrome()
    {
        DataContextChanged -= OnModernDataContextChanged;
        AttachedToVisualTree -= OnModernAttachedToVisualTree;
        _railAuditTimer.Stop();
        _railAuditTimer.Tick -= OnRailAuditTimerTick;
        _actionsSearch.TextChanged -= OnActionsSearchChanged;
        _statusDebounce?.Cancel();
        _statusDebounce?.Dispose();
        _statusDebounce = null;
        AttachModernShell(null);
    }

    /// <summary>
    /// Handles the modern attached to visual tree event raised by the UI or runtime.
    /// </summary>
    private void OnModernAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e) =>
        Dispatcher.UIThread.Post(AuditRailButtons);

    /// <summary>
    /// Handles the rail audit timer tick event raised by the UI or runtime.
    /// </summary>
    private void OnRailAuditTimerTick(object? sender, EventArgs e) => AuditRailButtons();

    /// <summary>
    /// Handles the modern data context changed event raised by the UI or runtime.
    /// </summary>
    private void OnModernDataContextChanged(object? sender, EventArgs e) =>
        AttachModernShell(DataContext as MainView);

    /// <summary>
    /// Performs the attach modern shell step owned by this component.
    /// </summary>
    private void AttachModernShell(MainView? shell)
    {
        if (_modernShell is not null)
        {
            _modernShell.PropertyChanged -= OnModernShellPropertyChanged;
            _modernShell.OpenTabs.CollectionChanged -= OnOpenTabsChanged;
            _modernShell.CommandItems.CollectionChanged -= OnCommandItemsChanged;
        }
        UnobserveTabs();

        _modernShell = shell;
        if (_modernShell is null)
        {
            _modernTabs.Children.Clear();
            _actionsSections.Children.Clear();
            return;
        }

        _modernShell.PropertyChanged += OnModernShellPropertyChanged;
        _modernShell.OpenTabs.CollectionChanged += OnOpenTabsChanged;
        _modernShell.CommandItems.CollectionChanged += OnCommandItemsChanged;
        ObserveTabs();

        _updatingActionSearch = true;
        _actionsSearch.Text = _modernShell.CommandSearch;
        _updatingActionSearch = false;

        RebuildTabs();
        RebuildActions();
        QueueModelStatusUpdate();
        Dispatcher.UIThread.Post(AuditRailButtons);
    }

    /// <summary>
    /// Handles the modern shell property changed event raised by the UI or runtime.
    /// </summary>
    private void OnModernShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_modernShell is null) return;

        if (e.PropertyName == nameof(MainView.OllamaStatus))
            QueueModelStatusUpdate();
        else if (e.PropertyName == nameof(MainView.IsCommandPaletteOpen)
                 && _modernShell.IsCommandPaletteOpen
                 && !_suppressPaletteRedirect)
            RedirectCommandPaletteToActions();
        else if (e.PropertyName is nameof(MainView.CurrentPage)
                 or nameof(MainView.CurrentChat)
                 or nameof(MainView.ProductName))
        {
            UpdateNavigationButtons();
            Dispatcher.UIThread.Post(AuditRailButtons);
        }
    }

    /// <summary>
    /// Performs the redirect command palette to actions step owned by this component.
    /// </summary>
    private void RedirectCommandPaletteToActions()
    {
        if (_modernShell is null || _actionsButton is null || _actionsFlyout is null) return;

        _suppressPaletteRedirect = true;
        try
        {
            _modernShell.CloseCommandPaletteCommand.Execute(null);
        }
        finally
        {
            _suppressPaletteRedirect = false;
        }

        RebuildActions();
        _actionsFlyout.ShowAt(_actionsButton);
        Dispatcher.UIThread.Post(() => _actionsSearch.Focus());
    }

    /// <summary>
    /// Handles the open tabs changed event raised by the UI or runtime.
    /// </summary>
    private void OnOpenTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ObserveTabs();
        RebuildTabs();
    }

    /// <summary>
    /// Performs the observe tabs step owned by this component.
    /// </summary>
    private void ObserveTabs()
    {
        UnobserveTabs();
        if (_modernShell is null) return;

        foreach (var tab in _modernShell.OpenTabs)
        {
            tab.PropertyChanged += OnTabPropertyChanged;
            _observedTabs.Add(tab);
        }
    }

    /// <summary>
    /// Performs the unobserve tabs step owned by this component.
    /// </summary>
    private void UnobserveTabs()
    {
        foreach (var tab in _observedTabs)
            tab.PropertyChanged -= OnTabPropertyChanged;
        _observedTabs.Clear();
    }

    /// <summary>
    /// Handles the tab property changed event raised by the UI or runtime.
    /// </summary>
    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceTabViewModel.Title)
            or nameof(WorkspaceTabViewModel.IsSelected)
            or nameof(WorkspaceTabViewModel.Surface)
            or nameof(WorkspaceTabViewModel.GroupId)
            or nameof(WorkspaceTabViewModel.GroupName)
            or nameof(WorkspaceTabViewModel.IsGroupCollapsed)
            or nameof(WorkspaceTabViewModel.IsMarkedForGrouping))
            RebuildTabs();
    }

    /// <summary>
    /// Performs the rebuild tabs step owned by this component.
    /// </summary>
    private void RebuildTabs()
    {
        _modernTabs.Children.Clear();
        UpdateNavigationButtons();
        if (_modernShell is null) return;

        var renderedGroups = new HashSet<Guid>();
        foreach (var tab in _modernShell.OpenTabs)
        {
            if (tab.GroupId is { } groupId)
            {
                if (renderedGroups.Add(groupId)) _modernTabs.Children.Add(BuildTabGroupLabel(groupId));
                if (tab.IsGroupCollapsed) continue;
            }
            _modernTabs.Children.Add(BuildModernTab(tab));
        }
    }

    private void UpdateNavigationButtons()
    {
        _backButton.IsEnabled = _modernShell?.NavigateBackCommand.CanExecute(null) == true;
        _forwardButton.IsEnabled = _modernShell?.NavigateForwardCommand.CanExecute(null) == true;
    }

    /// <summary>
    /// Builds modern tab from the currently available inputs.
    /// </summary>
    private Control BuildModernTab(WorkspaceTabViewModel tab)
    {
        var leftDropIndicator = DropIndicator();
        var rightDropIndicator = DropIndicator();

        var title = new TextBlock
        {
            Text = tab.Title,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Left,
            FontSize = 12
        };
        var iconAndTitle = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new HavenIcon
                {
                    IconKey = IconForTab(tab),
                    Width = 15,
                    Height = 15,
                    Opacity = tab.IsSelected ? 1 : 0.72,
                    VerticalAlignment = VerticalAlignment.Center
                },
                title
            }
        };
        var button = new HavenTabButton
        {
            Content = iconAndTitle,
            IsSelected = tab.IsSelected,
            MinWidth = 78,
            MaxWidth = 225,
            MinHeight = 38,
            Padding = new Thickness(10, 5),
            Background = Brushes.Transparent,
            BorderThickness = tab.IsSelected ? new Thickness(0, 0, 0, 3) : new Thickness(0),
            CornerRadius = new CornerRadius(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        button.Classes.Add("workspaceTab");
        if (tab.IsMarkedForGrouping)
        {
            button.BorderBrush = ModernResourceBrush("HavenAccentBrush", Colors.DodgerBlue);
            button.BorderThickness = new Thickness(1);
        }
        ToolTip.SetTip(button, tab.Title);
        button.Click += (_, _) =>
        {
            if (_modernShell is not null)
                _modernShell.SelectedTab = tab;
        };
        button.ContextMenu = BuildTabContextMenu(tab);
        button.PointerPressed += (_, e) =>
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
            tab.IsMarkedForGrouping = !tab.IsMarkedForGrouping;
            e.Handled = true;
        };

        var host = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("3,Auto,3"),
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                leftDropIndicator,
                WithModernColumn(button, 1),
                WithModernColumn(rightDropIndicator, 2)
            }
        };

        AttachTabDragHandlers(host, button, tab, leftDropIndicator, rightDropIndicator);
        return host;
    }

    /// <summary>
    /// Builds tab context menu from the currently available inputs.
    /// </summary>
    private ContextMenu BuildTabContextMenu(WorkspaceTabViewModel tab)
    {
        var refresh = new HavenMenuItem { Header = "Refresh tab" };
        refresh.Click += async (_, _) => await RefreshTabAsync(tab);

        var rename = new HavenMenuItem { Header = "Rename tab" };
        rename.Click += async (_, _) => await RenameTabAsync(tab);

        var tabIndex = _modernShell?.OpenTabs.IndexOf(tab) ?? -1;
        var closeRight = new HavenMenuItem
        {
            Header = "Close all tabs to the right",
            IsEnabled = _modernShell is not null && tabIndex >= 0 && tabIndex < _modernShell.OpenTabs.Count - 1
        };
        closeRight.Click += (_, _) => CloseTabsBeside(tab, closeRight: true);

        var closeLeft = new HavenMenuItem
        {
            Header = "Close all tabs to the left",
            IsEnabled = tabIndex > 0
        };
        closeLeft.Click += (_, _) => CloseTabsBeside(tab, closeRight: false);

        var close = new HavenMenuItem
        {
            Header = "Close tab",
            IsVisible = _modernShell is { OpenTabs.Count: > 1 } && tab.IsCloseable
        };
        close.Click += (_, _) => CloseModernTab(tab);

        var markedCount = _modernShell?.OpenTabs.Count(item => item.IsMarkedForGrouping) ?? 0;
        var groupSelected = new HavenMenuItem { Header = "Group selected tabs...", IsVisible = markedCount >= 2 };
        groupSelected.Click += async (_, _) => await CreateGroupFromMarkedTabsAsync();
        var removeFromGroup = new HavenMenuItem { Header = "Remove from group", IsVisible = tab.GroupId is not null };
        removeFromGroup.Click += (_, _) => RemoveTabFromGroup(tab);

        return new HavenContextMenu
        {
            ItemsSource = new object[] { refresh, rename, new Separator(), groupSelected, removeFromGroup, new Separator(), closeRight, closeLeft, new Separator(), close }
        };
    }

    /// <summary>Creates the compact label that owns group-wide actions and collapse state.</summary>
    private Control BuildTabGroupLabel(Guid groupId)
    {
        var members = _modernShell!.OpenTabs.Where(tab => tab.GroupId == groupId).ToArray();
        var first = members[0];
        var button = new HavenButton
        {
            Content = $"{(first.IsGroupCollapsed ? "›" : "⌄")}  {first.GroupName}",
            Margin = new Thickness(5, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        button.Classes.Add("chip");
        ToolTip.SetTip(button, "Collapse or expand tab group");
        button.Click += (_, _) => SetGroupCollapsed(groupId, !first.IsGroupCollapsed);
        button.ContextMenu = BuildGroupContextMenu(groupId);
        return button;
    }

    private ContextMenu BuildGroupContextMenu(Guid groupId)
    {
        var rename = new HavenMenuItem { Header = "Rename group..." };
        rename.Click += async (_, _) => await RenameGroupAsync(groupId);
        var refresh = new HavenMenuItem { Header = "Refresh group" };
        refresh.Click += async (_, _) =>
        {
            foreach (var tab in _modernShell!.OpenTabs.Where(item => item.GroupId == groupId).ToArray())
                await RefreshTabAsync(tab);
        };
        var ungroup = new HavenMenuItem { Header = "Ungroup" };
        ungroup.Click += (_, _) => Ungroup(groupId);
        var closeGroup = new HavenMenuItem { Header = "Close group" };
        closeGroup.Click += (_, _) => CloseGroup(groupId, outside: false);
        var closeOutside = new HavenMenuItem { Header = "Close tabs outside group" };
        closeOutside.Click += (_, _) => CloseGroup(groupId, outside: true);
        return new HavenContextMenu { ItemsSource = new object[] { rename, refresh, ungroup, new Separator(), closeGroup, closeOutside } };
    }

    private async Task CreateGroupFromMarkedTabsAsync()
    {
        if (_modernShell is null) return;
        var selected = _modernShell.OpenTabs.Where(tab => tab.IsMarkedForGrouping).ToArray();
        if (selected.Length < 2) return;
        var name = await PromptForNameAsync("Create tab group", "Group name", "Tab group");
        if (string.IsNullOrWhiteSpace(name)) return;
        var id = Guid.NewGuid();
        foreach (var tab in selected)
        {
            tab.GroupId = id;
            tab.GroupName = name;
            tab.IsGroupCollapsed = false;
            tab.IsMarkedForGrouping = false;
        }
    }

    private async Task RenameGroupAsync(Guid groupId)
    {
        if (_modernShell is null) return;
        var members = _modernShell.OpenTabs.Where(tab => tab.GroupId == groupId).ToArray();
        if (members.Length == 0) return;
        var name = await PromptForNameAsync("Rename tab group", "Group name", members[0].GroupName);
        if (string.IsNullOrWhiteSpace(name)) return;
        foreach (var tab in members) tab.GroupName = name;
    }

    private async Task<string?> PromptForNameAsync(string title, string label, string initial)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return null;
        var input = new HavenTextInput { Text = initial, MinWidth = 320 };
        string? result = null;
        var dialog = new Window { Title = title, Width = 420, Height = 185, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var save = new HavenButton { Content = "Save" };
        save.Classes.Add("accent");
        var cancel = new HavenButton { Content = "Cancel" };
        save.Click += (_, _) => { result = input.Text?.Trim(); dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 12,
            Children =
            {
                new TextBlock { Text = label, FontSize = 18, FontWeight = FontWeight.SemiBold }, input,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, save } }
            }
        };
        dialog.Opened += (_, _) => input.Focus();
        await dialog.ShowDialog(owner);
        return result;
    }

    private void SetGroupCollapsed(Guid groupId, bool collapsed)
    {
        foreach (var tab in _modernShell!.OpenTabs.Where(tab => tab.GroupId == groupId)) tab.IsGroupCollapsed = collapsed;
    }

    private void RemoveTabFromGroup(WorkspaceTabViewModel tab)
    {
        tab.GroupId = null;
        tab.GroupName = string.Empty;
        tab.IsGroupCollapsed = false;
    }

    private void Ungroup(Guid groupId)
    {
        foreach (var tab in _modernShell!.OpenTabs.Where(tab => tab.GroupId == groupId).ToArray()) RemoveTabFromGroup(tab);
    }

    private void CloseGroup(Guid groupId, bool outside)
    {
        var targets = _modernShell!.OpenTabs.Where(tab => (tab.GroupId == groupId) != outside && tab.IsCloseable).ToArray();
        foreach (var tab in targets) CloseModernTab(tab);
    }

    private async Task RefreshTabAsync(WorkspaceTabViewModel tab)
    {
        if (tab.Page is BrowserPage browser)
        {
            await browser.ReloadCommand.ExecuteAsync();
            return;
        }

        if (tab.Page is IActivatablePage activatable)
        {
            activatable.Deactivate();
            await activatable.ActivateAsync(CancellationToken.None);
        }
    }

    private void CloseTabsBeside(WorkspaceTabViewModel anchor, bool closeRight)
    {
        if (_modernShell is null) return;
        var anchorIndex = _modernShell.OpenTabs.IndexOf(anchor);
        if (anchorIndex < 0) return;

        var candidates = _modernShell.OpenTabs
            .Where((tab, index) => tab.IsCloseable && (closeRight ? index > anchorIndex : index < anchorIndex))
            .ToArray();
        foreach (var candidate in candidates)
            CloseModernTab(candidate);
    }

    /// <summary>
    /// Performs rename tab asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task RenameTabAsync(WorkspaceTabViewModel tab)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var input = new HavenTextInput { Text = tab.Title, MinWidth = 340 };
        var accepted = false;
        var dialog = new Window
        {
            Title = "Rename tab",
            Width = 430,
            Height = 185,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var save = new HavenButton { Content = "Rename" };
        save.Classes.Add("accent");
        var cancel = new HavenButton { Content = "Cancel" };
        save.Click += (_, _) =>
        {
            accepted = true;
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Rename tab", FontSize = 18, FontWeight = FontWeight.SemiBold },
                input,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, save }
                }
            }
        };
        dialog.Opened += (_, _) => input.Focus();
        await dialog.ShowDialog(owner);

        if (accepted && !string.IsNullOrWhiteSpace(input.Text))
            tab.Title = input.Text.Trim();
    }

    /// <summary>
    /// Performs the close modern tab step owned by this component.
    /// </summary>
    private void CloseModernTab(WorkspaceTabViewModel tab)
    {
        if (_modernShell is null || _modernShell.OpenTabs.Count <= 1 || !tab.IsCloseable) return;
        // Route every close gesture through the shell command so selection,
        // disposal, and the one-tab minimum have one source of truth.
        if (_modernShell.CloseTabCommand.CanExecute(tab))
            _modernShell.CloseTabCommand.Execute(tab);
    }

    /// <summary>
    /// Performs open fresh home tab asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task OpenFreshHomeTabAsync()
    {
        if (_modernShell is null) return;

        var canonicalHomeBefore = _modernShell.OpenTabs.FirstOrDefault(tab =>
            tab.Key.Equals("home", StringComparison.OrdinalIgnoreCase));

        await _modernShell.NavigateHomeCommand.ExecuteAsync();
        var source = _modernShell.SelectedTab;
        if (source is null || source.Surface != HavenSurface.Home) return;

        if (canonicalHomeBefore is null && source.Key.Equals("home", StringComparison.OrdinalIgnoreCase))
            _modernShell.OpenTabs.Remove(source);

        var fresh = new WorkspaceTabViewModel(
            "home-" + Guid.NewGuid().ToString("N")[..8],
            "Home",
            source.Page,
            true,
            HavenSurface.Home);
        _modernShell.OpenTabs.Add(fresh);
        _modernShell.SelectedTab = fresh;
    }

    /// <summary>
    /// Performs the attach tab drag handlers step owned by this component.
    /// </summary>
    private void AttachTabDragHandlers(
        Grid host,
        Button button,
        WorkspaceTabViewModel tab,
        Border leftIndicator,
        Border rightIndicator)
    {
        button.PointerPressed += (_, args) =>
        {
            if (!args.GetCurrentPoint(button).Properties.IsLeftButtonPressed) return;
            _tabDragCandidate = tab;
            _tabDragStart = args.GetPosition(this);
            args.Pointer.Capture(button);
        };

        button.PointerMoved += async (_, args) =>
        {
            if (_tabDragCandidate != tab || _tabDragInProgress
                || !args.GetCurrentPoint(button).Properties.IsLeftButtonPressed)
                return;

            var current = args.GetPosition(this);
            if (Math.Abs(current.X - _tabDragStart.X) < 6
                && Math.Abs(current.Y - _tabDragStart.Y) < 6)
                return;

            _tabDragInProgress = true;
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.CreateText("haven-tab:" + tab.Key));
            try
            {
                await DragDrop.DoDragDropAsync(args, transfer, DragDropEffects.Move);
            }
            finally
            {
                _tabDragCandidate = null;
                _tabDragInProgress = false;
                args.Pointer.Capture(null);
            }
        };

        button.PointerReleased += (_, args) =>
        {
            if (!_tabDragInProgress)
                _tabDragCandidate = null;
            args.Pointer.Capture(null);
        };

        DragDrop.SetAllowDrop(host, true);
        DragDrop.AddDragOverHandler(host, (_, args) =>
        {
            if (!TryReadTabTransfer(args.DataTransfer, out _))
            {
                args.DragEffects = DragDropEffects.None;
                return;
            }

            var insertAfter = args.GetPosition(host).X >= host.Bounds.Width / 2;
            leftIndicator.IsVisible = !insertAfter;
            rightIndicator.IsVisible = insertAfter;
            host.Opacity = 0.72;
            args.DragEffects = DragDropEffects.Move;
            args.Handled = true;
        });
        DragDrop.AddDragLeaveHandler(host, (_, args) =>
        {
            ClearDropPreview(host, leftIndicator, rightIndicator);
            args.Handled = true;
        });
        DragDrop.AddDropHandler(host, (_, args) =>
        {
            var insertAfter = args.GetPosition(host).X >= host.Bounds.Width / 2;
            ClearDropPreview(host, leftIndicator, rightDropIndicator: rightIndicator);
            if (!TryReadTabTransfer(args.DataTransfer, out var sourceKey)) return;
            MoveTab(sourceKey, tab, insertAfter);
            args.Handled = true;
        });
    }

    /// <summary>
    /// Performs the move tab step owned by this component.
    /// </summary>
    private void MoveTab(string sourceKey, WorkspaceTabViewModel target, bool insertAfter)
    {
        if (_modernShell is null) return;

        var source = _modernShell.OpenTabs.FirstOrDefault(tab =>
            tab.Key.Equals(sourceKey, StringComparison.Ordinal));
        if (source is null || ReferenceEquals(source, target)) return;

        var sourceIndex = _modernShell.OpenTabs.IndexOf(source);
        var targetIndex = _modernShell.OpenTabs.IndexOf(target) + (insertAfter ? 1 : 0);
        if (sourceIndex < targetIndex) targetIndex--;
        targetIndex = Math.Clamp(targetIndex, 0, _modernShell.OpenTabs.Count - 1);
        if (sourceIndex != targetIndex)
            _modernShell.OpenTabs.Move(sourceIndex, targetIndex);
    }

    /// <summary>
    /// Attempts to read tab transfer and reports the result without using failure for normal control flow.
    /// </summary>
    private static bool TryReadTabTransfer(IDataTransfer transfer, out string key)
    {
        key = string.Empty;
        var text = transfer.TryGetText();
        if (text is null || !text.StartsWith("haven-tab:", StringComparison.Ordinal)) return false;
        key = text[10..];
        return key.Length > 0;
    }

    /// <summary>
    /// Performs the drop indicator step owned by this component.
    /// </summary>
    private static Border DropIndicator() => new()
    {
        Width = 2,
        Margin = new Thickness(0, 5),
        Background = ModernResourceBrush("HavenAccentBrush", Colors.DodgerBlue),
        CornerRadius = new CornerRadius(2),
        IsVisible = false
    };

    /// <summary>
    /// Performs the clear drop preview step owned by this component.
    /// </summary>
    private static void ClearDropPreview(Grid host, Border left, Border rightDropIndicator)
    {
        left.IsVisible = false;
        rightDropIndicator.IsVisible = false;
        host.Opacity = 1;
    }

    /// <summary>
    /// Performs the estimate underline width step owned by this component.
    /// </summary>
    private static double EstimateUnderlineWidth(string title) =>
        Math.Clamp(title.Length * 6.6 + 10, 28, 155);

    /// <summary>
    /// Performs the icon for tab step owned by this component.
    /// </summary>
    private static string IconForTab(WorkspaceTabViewModel tab)
    {
        if (tab.Key.StartsWith("file-", StringComparison.OrdinalIgnoreCase)) return "file";
        if (tab.Key.Contains("settings", StringComparison.OrdinalIgnoreCase)) return "settings";
        if (tab.Key.Contains("archive", StringComparison.OrdinalIgnoreCase)) return "archive";
        if (tab.Key.Contains("notes", StringComparison.OrdinalIgnoreCase)
            || tab.Page.GetType().Name.Contains("Notes", StringComparison.OrdinalIgnoreCase))
            return "notes";

        return tab.Surface switch
        {
            HavenSurface.Home => "home",
            HavenSurface.Chat => "chat",
            HavenSurface.Study => "study",
            HavenSurface.Tasks => "tasks",
            HavenSurface.Automations => "automation",
            HavenSurface.Terminal => "commands",
            HavenSurface.Studio => "studio",
            HavenSurface.Browse => "browse",
            HavenSurface.Plan => "plan",
            HavenSurface.Training => "training",
            _ => "info"
        };
    }

    /// <summary>
    /// Handles the actions search changed event raised by the UI or runtime.
    /// </summary>
    private void OnActionsSearchChanged(object? sender, TextChangedEventArgs e)
    {
        if (_updatingActionSearch || _modernShell is null) return;
        _modernShell.CommandSearch = _actionsSearch.Text ?? string.Empty;
    }

    /// <summary>
    /// Handles the command items changed event raised by the UI or runtime.
    /// </summary>
    private void OnCommandItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        QueueActionsRebuild();

    /// <summary>
    /// Performs the queue actions rebuild step owned by this component.
    /// </summary>
    private void QueueActionsRebuild()
    {
        if (_actionsRebuildQueued) return;
        _actionsRebuildQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _actionsRebuildQueued = false;
            RebuildActions();
        });
    }

    /// <summary>
    /// Performs the rebuild actions step owned by this component.
    /// </summary>
    private void RebuildActions()
    {
        _actionsSections.Children.Clear();
        if (_modernShell is null) return;

        foreach (var category in ActionCategories)
        {
            var items = _modernShell.CommandItems
                .Where(item => CategoryFor(item) == category)
                .ToArray();
            var rows = new StackPanel { Spacing = 2, Margin = new Thickness(0, 3, 0, 7) };
            foreach (var item in items)
                rows.Children.Add(BuildActionRow(item));

            if (items.Length == 0)
            {
                rows.Children.Add(new TextBlock
                {
                    Text = category == "Help"
                        ? "Ctrl+K opens this Actions menu from anywhere."
                        : "No matching actions in this section.",
                    Classes = { "muted" },
                    FontSize = 10,
                    Margin = new Thickness(10, 6)
                });
            }

            var header = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                ColumnSpacing = 9,
                Children =
                {
                    new HavenIcon { IconKey = CategoryIcon(category), Width = 15, Height = 15, Opacity = 0.72 },
                    WithModernColumn(new TextBlock { Text = category, FontWeight = FontWeight.SemiBold }, 1)
                }
            };
            var section = new HavenExpander
            {
                Header = header,
                IsExpanded = category is "File" or "Chat" or "Tools",
                Content = rows,
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            section.Classes.Add("actionSection");
            _actionsSections.Children.Add(section);
        }
    }

    /// <summary>
    /// Builds action row from the currently available inputs.
    /// </summary>
    private Button BuildActionRow(CommandPaletteItemViewModel item)
    {
        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 10,
            Children =
            {
                new HavenIcon
                {
                    IconKey = CommandIcon(item.Name),
                    Width = 16,
                    Height = 16,
                    Opacity = 0.76,
                    VerticalAlignment = VerticalAlignment.Center
                },
                WithModernColumn(new StackPanel
                {
                    Spacing = 1,
                    Children =
                    {
                        new TextBlock { Text = item.Name, FontWeight = FontWeight.SemiBold },
                        new TextBlock
                        {
                            Text = item.Description,
                            Classes = { "muted" },
                            FontSize = 10,
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                }, 1),
                WithModernColumn(new TextBlock
                {
                    Text = item.Shortcut,
                    Classes = { "muted" },
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsVisible = !string.IsNullOrWhiteSpace(item.Shortcut)
                }, 2)
            }
        };

        var button = new HavenButton
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = content
        };
        button.Classes.Add("sidebar");
        button.Classes.Add("actionRow");
        button.Click += (_, _) =>
        {
            _actionsFlyout?.Hide();
            if (item.RunCommand.CanExecute(null))
                item.RunCommand.Execute(null);
        };
        return button;
    }

    /// <summary>
    /// Performs the category for step owned by this component.
    /// </summary>
    private static string CategoryFor(CommandPaletteItemViewModel item)
    {
        var name = item.Name.ToLowerInvariant();
        if (name.Contains("new ") || name.StartsWith("archive") || name.Contains("activity log")) return "File";
        if (name.Contains("rename") || name.Contains("copy") || name.Contains("undo") || name.Contains("redo") || name.Contains("save")) return "Edit";
        if (name.Contains("sidebar") || name.Contains("app library")) return "View";
        if (name.Contains("branch") || name.Contains("chat") || name.Contains("context") || name.Contains("model") || name.Contains("instruction") || name.Contains("plugin") || name.Contains("pin")) return "Chat";
        if (name.Contains("project") || name.Contains("macro") || name.Contains("extension")) return "Project";
        if (name.Contains("browse") || name.Contains("training") || name.Contains("scheduled") || name.Contains("refresh") || name.Contains("settings")) return "Tools";
        return "Help";
    }

    /// <summary>
    /// Performs the command icon step owned by this component.
    /// </summary>
    private static string CommandIcon(string name)
    {
        var value = name.ToLowerInvariant();
        if (value.Contains("new")) return "plus";
        if (value.Contains("rename")) return "edit";
        if (value.Contains("archive")) return "archive";
        if (value.Contains("browse")) return "browse";
        if (value.Contains("training")) return "training";
        if (value.Contains("model") || value.Contains("refresh")) return "refresh";
        if (value.Contains("settings")) return "settings";
        if (value.Contains("plugin")) return "plugin";
        if (value.Contains("instruction")) return "instruction";
        if (value.Contains("pin")) return "pin";
        if (value.Contains("project") || value.Contains("build") || value.Contains("extension")) return "studio";
        if (value.Contains("scheduled") || value.Contains("plan")) return "plan";
        return "commands";
    }

    /// <summary>
    /// Performs the category icon step owned by this component.
    /// </summary>
    private static string CategoryIcon(string category) => category switch
    {
        "File" => "file",
        "Edit" => "edit",
        "View" => "browse",
        "Chat" => "chat",
        "Project" => "studio",
        "Tools" => "settings",
        _ => "info"
    };

    /// <summary>
    /// Performs the queue model status update step owned by this component.
    /// </summary>
    private void QueueModelStatusUpdate()
    {
        if (_modernShell is null) return;

        var raw = string.IsNullOrWhiteSpace(_modernShell.OllamaStatus)
            ? "Ollama status unavailable"
            : _modernShell.OllamaStatus.Trim();
        _latestRawStatus = raw;

        if (IsTransientModelStatus(raw))
        {
            _modelRefreshIcon.Opacity = 1;
            _modelStatusText.Text = string.IsNullOrWhiteSpace(_lastConfirmedModelStatus)
                ? raw
                : _lastConfirmedModelStatus;
            return;
        }

        if (IsFailureModelStatus(raw))
        {
            _statusDebounce?.Cancel();
            _statusDebounce?.Dispose();
            _statusDebounce = new CancellationTokenSource();
            _ = ConfirmFailureStatusAsync(raw, _statusDebounce.Token);
            return;
        }

        _statusDebounce?.Cancel();
        _lastConfirmedModelStatus = raw;
        _modelStatusText.Text = raw;
        _modelRefreshIcon.Opacity = 0.68;
    }

    /// <summary>
    /// Performs confirm failure status asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ConfirmFailureStatusAsync(string candidate, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2.5), cancellationToken);
            if (cancellationToken.IsCancellationRequested
                || !string.Equals(_latestRawStatus, candidate, StringComparison.Ordinal))
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _modelStatusText.Text = candidate;
                _modelRefreshIcon.Opacity = 0.68;
            });
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Performs show model refresh pulse asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ShowModelRefreshPulseAsync()
    {
        _modelRefreshIcon.Opacity = 1;
        await Task.Delay(TimeSpan.FromSeconds(1.2));
        if (!IsTransientModelStatus(_latestRawStatus))
            await Dispatcher.UIThread.InvokeAsync(() => _modelRefreshIcon.Opacity = 0.68);
    }

    /// <summary>
    /// Reports whether transient model status applies to the current state.
    /// </summary>
    private static bool IsTransientModelStatus(string status) =>
        status.Contains("connecting", StringComparison.OrdinalIgnoreCase)
        || status.Contains("refreshing", StringComparison.OrdinalIgnoreCase)
        || status.Contains("loading", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reports whether failure model status applies to the current state.
    /// </summary>
    private static bool IsFailureModelStatus(string status) =>
        status.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
        || status.Contains("disconnected", StringComparison.OrdinalIgnoreCase)
        || status.Contains("offline", StringComparison.OrdinalIgnoreCase)
        || status.Contains("failed", StringComparison.OrdinalIgnoreCase)
        || status.Contains("error", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Performs the audit rail buttons step owned by this component.
    /// </summary>
    private void AuditRailButtons()
    {
        if (_modernShell is null) return;

        foreach (var button in _experienceShell.GetVisualDescendants().OfType<Button>())
        {
            var iconKey = RailIconKey(button);
            if (iconKey is not null && button.Content is not HavenIcon)
            {
                button.Content = new HavenIcon
                {
                    IconKey = iconKey,
                    Width = 20,
                    Height = 20,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }

            if (button.Name is "ExperiencePlanButton" or "ExperienceBrowseButton")
                button.IsVisible = true;

            if (button.Name is "ExperienceChatButton" or "ExperiencePlanButton")
                MakeRailButtonDirect(button);
        }
    }

    /// <summary>
    /// Performs the make rail button direct step owned by this component.
    /// </summary>
    private void MakeRailButtonDirect(Button button)
    {
        button.Flyout = null;
        if (!_directRailButtons.Add(button)) return;

        button.Click += async (_, _) =>
        {
            if (_modernShell is null) return;
            switch (button.Name)
            {
                case "ExperienceChatButton":
                    await _modernShell.NavigateChatCommand.ExecuteAsync();
                    break;
                case "ExperiencePlanButton":
                    _modernShell.NavigatePlanCommand.Execute(null);
                    break;
            }
        };
    }

    /// <summary>
    /// Performs the rail icon key step owned by this component.
    /// </summary>
    private static string? RailIconKey(Button button)
    {
        var fixedKey = button.Name switch
        {
            "ExperienceHomeButton" => "home",
            "ExperienceChatButton" => "chat",
            "ExperienceStudioButton" => "studio",
            "ExperiencePlanButton" => "plan",
            "ExperienceBrowseButton" => "browse",
            "ExperienceNotesButton" => "notes",
            "ExperienceSettingsButton" => "settings",
            "ExperienceAllModesButton" => "all-modes",
            _ => null
        };
        if (fixedKey is not null) return fixedKey;
        if (button.Name?.StartsWith("PinnedMode_", StringComparison.Ordinal) != true) return null;
        return SemanticIconForText(ToolTip.GetTip(button)?.ToString());
    }

    /// <summary>
    /// Performs the semantic icon for text step owned by this component.
    /// </summary>
    private static string SemanticIconForText(string? value)
    {
        var text = value?.ToLowerInvariant() ?? string.Empty;
        if (text.Contains("chat")) return "chat";
        if (text.Contains("study") || text.Contains("teach") || text.Contains("learn")) return "study";
        if (text.Contains("call") || text.Contains("phone")) return "call";
        if (text.Contains("studio") || text.Contains("code") || text.Contains("create")) return "studio";
        if (text.Contains("browse") || text.Contains("web")) return "browse";
        if (text.Contains("plan") || text.Contains("calendar") || text.Contains("automation")) return "plan";
        if (text.Contains("note") || text.Contains("document")) return "notes";
        if (text.Contains("task") || text.Contains("do")) return "tasks";
        if (text.Contains("train")) return "training";
        return "info";
    }

    /// <summary>
    /// Performs the modern resource brush step owned by this component.
    /// </summary>
    private static IBrush ModernResourceBrush(string key, Color fallback) =>
        Avalonia.Application.Current?.Resources[key] as IBrush ?? new SolidColorBrush(fallback);

    private static T WithModernColumn<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }
}
