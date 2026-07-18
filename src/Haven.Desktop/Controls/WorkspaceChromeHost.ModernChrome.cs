using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Core;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views;

namespace Haven.Desktop.Controls;

public sealed partial class WorkspaceChromeHost
{
    private static readonly string[] ActionCategories = ["File", "Edit", "View", "Chat", "Project", "Tools", "Help"];

    private readonly StackPanel _modernTabs = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 3,
        VerticalAlignment = VerticalAlignment.Center
    };

    private readonly TextBlock _modelStatusText = new()
    {
        Text = "Connecting to Ollama",
        FontSize = 10,
        MaxWidth = 235,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center
    };

    private readonly HavenIcon _modelRefreshIcon = new()
    {
        IconKey = "refresh",
        Width = 14,
        Height = 14,
        Opacity = 0.68,
        VerticalAlignment = VerticalAlignment.Center
    };

    private readonly TextBox _actionsSearch = new()
    {
        PlaceholderText = "Search actions",
        MinHeight = 36
    };

    private readonly StackPanel _actionsSections = new() { Spacing = 5 };
    private readonly DispatcherTimer _railAuditTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<WorkspaceTabViewModel> _observedTabs = [];
    private readonly HashSet<Button> _directRailButtons = [];

    private MainWindowViewModel? _modernShell;
    private Button? _actionsButton;
    private Flyout? _actionsFlyout;
    private CancellationTokenSource? _statusDebounce;
    private WorkspaceTabViewModel? _tabDragCandidate;
    private Point _tabDragStart;
    private bool _tabDragInProgress;
    private bool _suppressPaletteRedirect;
    private bool _updatingActionSearch;
    private bool _actionsRebuildQueued;
    private string _latestRawStatus = string.Empty;
    private string _lastConfirmedModelStatus = string.Empty;

    private Border BuildModernTopBar()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
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

        var addTab = new Button
        {
            Classes = { "chrome" },
            Content = new HavenIcon { IconKey = "plus", Width = 15, Height = 15 },
            ToolTip = "New Home tab",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        };
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
        Grid.SetColumn(tabArea, 1);
        grid.Children.Add(tabArea);

        var modelStatus = new Button
        {
            Classes = { "status" },
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                Children = { _modelStatusText, _modelRefreshIcon }
            },
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Refresh local models"
        };
        modelStatus.Click += (_, _) =>
        {
            if (_modernShell?.RefreshModelsCommand.CanExecute(null) == true)
                _modernShell.RefreshModelsCommand.Execute(null);
            _ = ShowModelRefreshPulseAsync();
        };
        Grid.SetColumn(modelStatus, 2);
        grid.Children.Add(modelStatus);

        _actionsButton = new Button
        {
            Classes = { "primary" },
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
            Padding = new Thickness(15, 8),
            ToolTip = "Search Haven actions · Ctrl+K"
        };
        _actionsFlyout = BuildActionsFlyout();
        _actionsButton.Flyout = _actionsFlyout;
        _actionsButton.Click += (_, _) =>
        {
            RebuildActions();
            Dispatcher.UIThread.Post(() => _actionsSearch.Focus());
        };
        Grid.SetColumn(_actionsButton, 3);
        grid.Children.Add(_actionsButton);

        return new Border
        {
            Background = ModernResourceBrush("HavenPanelBrush", Color.FromArgb(245, 38, 45, 61)),
            BorderBrush = ModernResourceBrush("HavenLineBrush", Color.FromArgb(54, 255, 255, 255)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid
        };
    }

    private Flyout BuildActionsFlyout()
    {
        _actionsSearch.TextChanged += OnActionsSearchChanged;

        var content = new StackPanel
        {
            Width = 560,
            Spacing = 10,
            Margin = new Thickness(10),
            Children =
            {
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    Children =
                    {
                        new StackPanel
                        {
                            Spacing = 2,
                            Children =
                            {
                                new TextBlock { Text = "Actions", FontSize = 18, FontWeight = FontWeight.SemiBold },
                                new TextBlock { Text = "Search commands or browse a collapsible section", Classes = { "muted" }, FontSize = 10 }
                            }
                        },
                        WithModernColumn(new TextBlock
                        {
                            Text = "Ctrl+K",
                            Classes = { "muted" },
                            FontSize = 10,
                            VerticalAlignment = VerticalAlignment.Center
                        }, 1)
                    }
                },
                new Grid
                {
                    Children =
                    {
                        _actionsSearch,
                        new HavenIcon
                        {
                            IconKey = "search",
                            Width = 15,
                            Height = 15,
                            Margin = new Thickness(12, 0, 0, 0),
                            HorizontalAlignment = HorizontalAlignment.Left,
                            VerticalAlignment = VerticalAlignment.Center,
                            Opacity = 0.65,
                            IsHitTestVisible = false
                        }
                    }
                },
                new ScrollViewer
                {
                    MaxHeight = 520,
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                    Content = _actionsSections
                }
            }
        };

        return new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            Content = content
        };
    }

    private void InitializeModernChrome()
    {
        DataContextChanged += OnModernDataContextChanged;
        AttachedToVisualTree += OnModernAttachedToVisualTree;
        _railAuditTimer.Tick += OnRailAuditTimerTick;
        _railAuditTimer.Start();
        AttachModernShell(DataContext as MainWindowViewModel);
    }

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

    private void OnModernAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e) =>
        Dispatcher.UIThread.Post(AuditRailButtons);

    private void OnRailAuditTimerTick(object? sender, EventArgs e) => AuditRailButtons();

    private void OnModernDataContextChanged(object? sender, EventArgs e) =>
        AttachModernShell(DataContext as MainWindowViewModel);

    private void AttachModernShell(MainWindowViewModel? shell)
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

    private void OnModernShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_modernShell is null) return;

        if (e.PropertyName == nameof(MainWindowViewModel.OllamaStatus))
            QueueModelStatusUpdate();
        else if (e.PropertyName == nameof(MainWindowViewModel.IsCommandPaletteOpen)
                 && _modernShell.IsCommandPaletteOpen
                 && !_suppressPaletteRedirect)
            RedirectCommandPaletteToActions();
        else if (e.PropertyName is nameof(MainWindowViewModel.CurrentPage)
                 or nameof(MainWindowViewModel.CurrentChat)
                 or nameof(MainWindowViewModel.ProductName))
            Dispatcher.UIThread.Post(AuditRailButtons);
    }

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

    private void OnOpenTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ObserveTabs();
        RebuildTabs();
    }

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

    private void UnobserveTabs()
    {
        foreach (var tab in _observedTabs) tab.PropertyChanged -= OnTabPropertyChanged;
        _observedTabs.Clear();
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceTabViewModel.Title)
            or nameof(WorkspaceTabViewModel.IsSelected)
            or nameof(WorkspaceTabViewModel.Surface))
            RebuildTabs();
    }

    private void RebuildTabs()
    {
        _modernTabs.Children.Clear();
        if (_modernShell is null) return;
        foreach (var tab in _modernShell.OpenTabs)
            _modernTabs.Children.Add(BuildModernTab(tab));
    }

    private Control BuildModernTab(WorkspaceTabViewModel tab)
    {
        var leftDropIndicator = DropIndicator();
        var rightDropIndicator = DropIndicator();

        var title = new TextBlock
        {
            Text = tab.Title,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            FontSize = 12
        };
        var underline = new Border
        {
            Height = 2,
            Width = EstimateUnderlineWidth(tab.Title),
            Margin = new Thickness(0, 1, 0, 0),
            Background = ModernResourceBrush("HavenAccentBrush", Colors.DodgerBlue),
            CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Center,
            IsVisible = tab.IsSelected
        };
        var label = new StackPanel
        {
            Spacing = 0,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { title, underline }
        };

        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto"),
            ColumnSpacing = 7,
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
                WithModernColumn(label, 1)
            }
        };

        var button = new Button
        {
            Content = content,
            MinWidth = 78,
            MaxWidth = 225,
            MinHeight = 38,
            Padding = new Thickness(10, 3),
            Background = tab.IsSelected
                ? ModernResourceBrush("HavenAccentSoftBrush", Color.FromArgb(60, 0, 120, 212))
                : Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(10),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = tab.Title
        };
        button.Classes.Add("workspaceTab");
        if (tab.IsSelected) button.Classes.Add("active");
        button.Click += (_, _) =>
        {
            if (_modernShell is not null) _modernShell.SelectedTab = tab;
        };
        button.ContextMenu = BuildTabContextMenu(tab);

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

    private ContextMenu BuildTabContextMenu(WorkspaceTabViewModel tab)
    {
        var rename = new MenuItem { Header = "Rename tab" };
        rename.Click += async (_, _) => await RenameTabAsync(tab);
        var close = new MenuItem { Header = "Close tab" };
        close.Click += (_, _) => CloseModernTab(tab);
        return new ContextMenu { ItemsSource = new object[] { rename, close } };
    }

    private async Task RenameTabAsync(WorkspaceTabViewModel tab)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var input = new TextBox { Text = tab.Title, MinWidth = 340 };
        var accepted = false;
        var dialog = new Window
        {
            Title = "Rename tab",
            Width = 430,
            Height = 185,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var save = new Button { Content = "Rename", Classes = { "accent" } };
        var cancel = new Button { Content = "Cancel" };
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

    private void CloseModernTab(WorkspaceTabViewModel tab)
    {
        if (_modernShell is null) return;
        var index = _modernShell.OpenTabs.IndexOf(tab);
        if (index < 0) return;
        var wasSelected = ReferenceEquals(_modernShell.SelectedTab, tab);
        _modernShell.OpenTabs.RemoveAt(index);

        if (wasSelected && _modernShell.OpenTabs.Count > 0)
        {
            var nextIndex = Math.Clamp(index - 1, 0, _modernShell.OpenTabs.Count - 1);
            _modernShell.SelectedTab = _modernShell.OpenTabs[nextIndex];
        }
        else if (_modernShell.OpenTabs.Count == 0)
        {
            _ = OpenFreshHomeTabAsync();
        }
    }

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
            if (_tabDragCandidate != tab || _tabDragInProgress ||
                !args.GetCurrentPoint(button).Properties.IsLeftButtonPressed) return;
            var current = args.GetPosition(this);
            if (Math.Abs(current.X - _tabDragStart.X) < 6 && Math.Abs(current.Y - _tabDragStart.Y) < 6) return;

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
            if (!_tabDragInProgress) _tabDragCandidate = null;
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
            ClearDropPreview(host, leftIndicator, rightIndicator);
            if (!TryReadTabTransfer(args.DataTransfer, out var sourceKey)) return;
            MoveTab(sourceKey, tab, insertAfter);
            args.Handled = true;
        });
    }

    private void MoveTab(string sourceKey, WorkspaceTabViewModel target, bool insertAfter)
    {
        if (_modernShell is null) return;
        var source = _modernShell.OpenTabs.FirstOrDefault(tab => tab.Key.Equals(sourceKey, StringComparison.Ordinal));
        if (source is null || ReferenceEquals(source, target)) return;
        var sourceIndex = _modernShell.OpenTabs.IndexOf(source);
        var targetIndex = _modernShell.OpenTabs.IndexOf(target) + (insertAfter ? 1 : 0);
        if (sourceIndex < targetIndex) targetIndex--;
        targetIndex = Math.Clamp(targetIndex, 0, _modernShell.OpenTabs.Count - 1);
        if (sourceIndex != targetIndex) _modernShell.OpenTabs.Move(sourceIndex, targetIndex);
    }

    private static bool TryReadTabTransfer(IDataTransfer transfer, out string key)
    {
        key = string.Empty;
        var text = transfer.TryGetText();
        if (text is null || !text.StartsWith("haven-tab:", StringComparison.Ordinal)) return false;
        key = text[10..];
        return key.Length > 0;
    }

    private static Border DropIndicator() => new()
    {
        Width = 2,
        Margin = new Thickness(0, 5),
        Background = ModernResourceBrush("HavenAccentBrush", Colors.DodgerBlue),
        CornerRadius = new CornerRadius(2),
        IsVisible = false
    };

    private static void ClearDropPreview(Grid host, Border left, Border right)
    {
        left.IsVisible = false;
        right.IsVisible = false;
        host.Opacity = 1;
    }

    private static double EstimateUnderlineWidth(string title) =>
        Math.Clamp(title.Length * 6.6 + 10, 28, 155);

    private static string IconForTab(WorkspaceTabViewModel tab)
    {
        if (tab.Key.StartsWith("file-", StringComparison.OrdinalIgnoreCase)) return "file";
        if (tab.Key.Contains("settings", StringComparison.OrdinalIgnoreCase)) return "settings";
        if (tab.Key.Contains("archive", StringComparison.OrdinalIgnoreCase)) return "archive";
        if (tab.Key.Contains("notes", StringComparison.OrdinalIgnoreCase) || tab.Page.GetType().Name.Contains("Notes", StringComparison.OrdinalIgnoreCase)) return "notes";
        return tab.Surface switch
        {
            HavenSurface.Home => "home",
            HavenSurface.Chat => "chat",
            HavenSurface.Teach => "teach",
            HavenSurface.Do => "tasks",
            HavenSurface.Studio => "studio",
            HavenSurface.Call => "call",
            HavenSurface.Browse => "browse",
            HavenSurface.Plan => "plan",
            HavenSurface.Training => "training",
            _ => "info"
        };
    }

    private void OnActionsSearchChanged(object? sender, TextChangedEventArgs e)
    {
        if (_updatingActionSearch || _modernShell is null) return;
        _modernShell.CommandSearch = _actionsSearch.Text ?? string.Empty;
    }

    private void OnCommandItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => QueueActionsRebuild();

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

    private void RebuildActions()
    {
        _actionsSections.Children.Clear();
        if (_modernShell is null) return;

        foreach (var category in ActionCategories)
        {
            var items = _modernShell.CommandItems.Where(item => CategoryFor(item) == category).ToArray();
            var rows = new StackPanel { Spacing = 2, Margin = new Thickness(0, 3, 0, 7) };
            foreach (var item in items) rows.Children.Add(BuildActionRow(item));
            if (items.Length == 0)
            {
                rows.Children.Add(new TextBlock
                {
                    Text = category == "Help" ? "Ctrl+K opens this Actions menu from anywhere." : "No matching actions in this section.",
                    Classes = { "muted" },
                    FontSize = 10,
                    Margin = new Thickness(10, 6)
                });
            }

            _actionsSections.Children.Add(new Expander
            {
                Header = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                    ColumnSpacing = 9,
                    Children =
                    {
                        new HavenIcon { IconKey = CategoryIcon(category), Width = 15, Height = 15, Opacity = 0.72 },
                        WithModernColumn(new TextBlock { Text = category, FontWeight = FontWeight.SemiBold }, 1)
                    }
                },
                IsExpanded = category is "File" or "Chat" or "Tools",
                Content = rows
            });
        }
    }

    private Button BuildActionRow(CommandPaletteItemViewModel item)
    {
        var button = new Button
        {
            Classes = { "sidebar" },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                ColumnSpacing = 10,
                Children =
                {
                    new HavenIcon { IconKey = CommandIcon(item.Name), Width = 16, Height = 16, Opacity = 0.76, VerticalAlignment = VerticalAlignment.Center },
                    WithModernColumn(new StackPanel
                    {
                        Spacing = 1,
                        Children =
                        {
                            new TextBlock { Text = item.Name, FontWeight = FontWeight.SemiBold },
                            new TextBlock { Text = item.Description, Classes = { "muted" }, FontSize = 10, TextWrapping = TextWrapping.Wrap }
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
            }
        };
        button.Click += (_, _) =>
        {
            _actionsFlyout?.Hide();
            if (item.RunCommand.CanExecute(null)) item.RunCommand.Execute(null);
        };
        return button;
    }

    private static string CategoryFor(CommandPaletteItemViewModel item)
    {
        var name = item.Name.ToLowerInvariant();
        if (name.Contains("new ") || name.StartsWith("archive") || name.Contains("activity log")) return "File";
        if (name.Contains("rename") || name.Contains("copy") || name.Contains("undo") || name.Contains("redo") || name.Contains("save")) return "Edit";
        if (name.Contains("sidebar") || name.Contains("mode library")) return "View";
        if (name.Contains("branch") || name.Contains("chat") || name.Contains("context") || name.Contains("model") || name.Contains("prompt") || name.Contains("plugin") || name.Contains("pin")) return "Chat";
        if (name.Contains("project") || name.Contains("macro") || name.Contains("extension")) return "Project";
        if (name.Contains("browse") || name.Contains("training") || name.Contains("scheduled") || name.Contains("refresh") || name.Contains("settings")) return "Tools";
        return "Help";
    }

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
        if (value.Contains("prompt")) return "prompt";
        if (value.Contains("pin")) return "pin";
        if (value.Contains("project") || value.Contains("build") || value.Contains("extension")) return "studio";
        if (value.Contains("scheduled") || value.Contains("plan")) return "plan";
        return "commands";
    }

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

    private async Task ConfirmFailureStatusAsync(string candidate, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2.5), cancellationToken);
            if (cancellationToken.IsCancellationRequested || !string.Equals(_latestRawStatus, candidate, StringComparison.Ordinal)) return;
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

    private async Task ShowModelRefreshPulseAsync()
    {
        _modelRefreshIcon.Opacity = 1;
        await Task.Delay(TimeSpan.FromSeconds(1.2));
        if (!IsTransientModelStatus(_latestRawStatus))
            await Dispatcher.UIThread.InvokeAsync(() => _modelRefreshIcon.Opacity = 0.68);
    }

    private static bool IsTransientModelStatus(string status) =>
        status.Contains("connecting", StringComparison.OrdinalIgnoreCase)
        || status.Contains("refreshing", StringComparison.OrdinalIgnoreCase)
        || status.Contains("loading", StringComparison.OrdinalIgnoreCase);

    private static bool IsFailureModelStatus(string status) =>
        status.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
        || status.Contains("disconnected", StringComparison.OrdinalIgnoreCase)
        || status.Contains("offline", StringComparison.OrdinalIgnoreCase)
        || status.Contains("failed", StringComparison.OrdinalIgnoreCase)
        || status.Contains("error", StringComparison.OrdinalIgnoreCase);

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

            if (button.Name is "ExperienceCallButton" or "ExperiencePlanButton" or "ExperienceBrowseButton")
                button.IsVisible = true;

            if (button.Name is "ExperienceChatButton" or "ExperienceStudioButton" or "ExperiencePlanButton" or "ExperienceNotesButton")
                MakeRailButtonDirect(button);
        }
    }

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
                case "ExperienceStudioButton":
                    await _modernShell.NavigateStudioCommand.ExecuteAsync();
                    break;
                case "ExperiencePlanButton":
                    _modernShell.NavigatePlanCommand.Execute(null);
                    break;
                case "ExperienceNotesButton":
                    await NotesExperienceNavigation.OpenAsync(_modernShell, NotesExperienceKind.Notes);
                    break;
            }
        };
    }

    private static string? RailIconKey(Button button)
    {
        var fixedKey = button.Name switch
        {
            "ExperienceHomeButton" => "home",
            "ExperienceChatButton" => "chat",
            "ExperienceStudioButton" => "studio",
            "ExperienceCallButton" => "call",
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

    private static string SemanticIconForText(string? value)
    {
        var text = value?.ToLowerInvariant() ?? string.Empty;
        if (text.Contains("chat")) return "chat";
        if (text.Contains("teach") || text.Contains("learn")) return "teach";
        if (text.Contains("call") || text.Contains("phone")) return "call";
        if (text.Contains("studio") || text.Contains("code") || text.Contains("create")) return "studio";
        if (text.Contains("browse") || text.Contains("web")) return "browse";
        if (text.Contains("plan") || text.Contains("calendar") || text.Contains("automation")) return "plan";
        if (text.Contains("note") || text.Contains("document")) return "notes";
        if (text.Contains("task") || text.Contains("do")) return "tasks";
        if (text.Contains("train")) return "training";
        return "info";
    }

    private static IBrush ModernResourceBrush(string key, Color fallback) =>
        Avalonia.Application.Current?.Resources[key] as IBrush ?? new SolidColorBrush(fallback);

    private static T WithModernColumn<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }
}
