/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/DeveloperTools/DeveloperToolsWindow.cs in the Desktop composition layer.
 * What: Implements the Elements, Properties, live-edit, and AXAML-source panes of Haven's inspector.
 * How: It traverses public Avalonia visual-tree APIs and renders a separate debug window.
 * Why: Contributors need a Chrome DevTools-style UI inspector without separately licensed tooling.
 */

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Haven.Desktop.DeveloperTools;

/// <summary>
/// A standalone inspector window that mirrors the most useful Chrome DevTools Elements features.
/// </summary>
internal sealed class DeveloperToolsWindow : Window
{
    private static readonly IBrush PanelBrush = new SolidColorBrush(Color.FromRgb(29, 32, 40));
    private static readonly IBrush PanelAltBrush = new SolidColorBrush(Color.FromRgb(36, 40, 50));
    private new static readonly IBrush BorderBrush = new SolidColorBrush(Color.FromRgb(64, 70, 86));
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.FromRgb(95, 168, 255));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.FromRgb(171, 177, 191));

    private readonly Window _inspectedWindow;
    private readonly TreeView _tree = new();
    private readonly TextBox _searchBox = new();
    private readonly TextBlock _selectionHeading = new();
    private readonly StackPanel _propertyRows = new();
    private readonly TextBlock _sourceHeading = new();
    private readonly TextBox _sourcePreview = new();
    private readonly TextBlock _status = new();
    private readonly ComboBox _editProperty = new();
    private readonly TextBox _editValue = new();
    private readonly Button _openSourceButton = new();
    private readonly Dictionary<Visual, TreeViewItem> _itemsByVisual = new(ReferenceEqualityComparer.Instance);

    private Visual? _selectedVisual;
    private AxamlSourceLocation? _sourceLocation;
    private bool _refreshingTree;

    public DeveloperToolsWindow(Window inspectedWindow)
    {
        _inspectedWindow = inspectedWindow;
        Title = "Haven Developer Tools";
        Width = 1180;
        Height = 720;
        MinWidth = 900;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(24, 27, 34));

        Content = BuildLayout();
        KeyDown += OnKeyDown;
        Closed += (_, _) => KeyDown -= OnKeyDown;
        RefreshTree();
    }

    public event EventHandler? InspectRequested;

    public void RefreshTree()
    {
        if (_refreshingTree) return;
        _refreshingTree = true;
        try
        {
            var previousSelection = _selectedVisual;
            _tree.Items.Clear();
            _itemsByVisual.Clear();

            var filter = _searchBox.Text?.Trim();
            var count = 0;
            var root = BuildTreeItem(_inspectedWindow, filter, ref count, forceInclude: true);
            if (root is not null)
            {
                root.IsExpanded = true;
                _tree.Items.Add(root);
            }

            _status.Text = filter is { Length: > 0 }
                ? $"{count:N0} matching visual-tree nodes"
                : $"{count:N0} visual-tree nodes";

            if (previousSelection is not null && _itemsByVisual.ContainsKey(previousSelection))
                SelectVisual(previousSelection);
            else
                SelectVisual(_inspectedWindow);
        }
        finally
        {
            _refreshingTree = false;
        }
    }

    public void SelectVisual(Visual visual)
    {
        _selectedVisual = visual;
        if (_itemsByVisual.TryGetValue(visual, out var item))
        {
            ExpandAncestors(item);
            _tree.SelectedItem = item;
        }
        UpdateSelectionPanels(visual);
    }

    private Control BuildLayout()
    {
        var inspectButton = ToolbarButton("⌖  Pick element", "Ctrl+Shift+C");
        inspectButton.Click += (_, _) => InspectRequested?.Invoke(this, EventArgs.Empty);

        var refreshButton = ToolbarButton("↻  Refresh", "Refresh visual tree");
        refreshButton.Click += (_, _) => RefreshTree();

        var copySelectorButton = ToolbarButton("Copy selector", "Copy a CSS-like selector for the selected control");
        copySelectorButton.Click += async (_, _) =>
        {
            if (_selectedVisual is null || Clipboard is null) return;
            await Clipboard.SetTextAsync(DeveloperElementFormatter.BuildSelector(_selectedVisual));
            SetStatus("Selector copied.");
        };

        _openSourceButton.Content = "Open AXAML";
        _openSourceButton.IsEnabled = false;
        _openSourceButton.Padding = new Thickness(12, 7);
        _openSourceButton.Click += (_, _) => OpenSource();

        _searchBox.PlaceholderText = "Filter tree by type, name, class, or text";
        _searchBox.MinWidth = 260;
        _searchBox.VerticalContentAlignment = VerticalAlignment.Center;
        _searchBox.TextChanged += (_, _) => RefreshTree();

        var toolbar = new Border
        {
            Background = PanelBrush,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 8),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,*,Auto"),
                ColumnSpacing = 8,
                Children =
                {
                    AtColumn(inspectButton, 0),
                    AtColumn(refreshButton, 1),
                    AtColumn(copySelectorButton, 2),
                    AtColumn(_openSourceButton, 3),
                    AtColumn(_searchBox, 4),
                    AtColumn(new TextBlock
                    {
                        Text = "F12 toggles • Ctrl+Shift+C picks",
                        Foreground = MutedBrush,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(12, 0, 0, 0)
                    }, 5)
                }
            }
        };

        _tree.SelectionChanged += OnTreeSelectionChanged;
        _tree.Background = Brushes.Transparent;
        _tree.HorizontalAlignment = HorizontalAlignment.Stretch;
        _tree.VerticalAlignment = VerticalAlignment.Stretch;

        var treePanel = new Border
        {
            Background = PanelBrush,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*"),
                Children =
                {
                    AtRow(SectionHeader("Elements", "Runtime visual tree"), 0),
                    AtRow(new ScrollViewer
                    {
                        Content = _tree,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Padding = new Thickness(8)
                    }, 1)
                }
            }
        };

        _selectionHeading.Text = "Select an element";
        _selectionHeading.FontSize = 17;
        _selectionHeading.FontWeight = FontWeight.SemiBold;

        _editProperty.ItemsSource = new[] { "Opacity", "Width", "Height", "Margin", "IsVisible", "Text", "Content" };
        _editProperty.SelectedIndex = 0;
        _editProperty.MinWidth = 120;
        _editProperty.SelectionChanged += (_, _) => LoadLiveEditValue();

        _editValue.PlaceholderText = "Value";
        _editValue.MinWidth = 180;
        _editValue.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            ApplyLiveEdit();
            e.Handled = true;
        };

        var applyButton = ToolbarButton("Apply", "Apply the value immediately to the selected runtime element");
        applyButton.Click += (_, _) => ApplyLiveEdit();

        var liveEditor = new Border
        {
            Background = PanelAltBrush,
            CornerRadius = new CornerRadius(6),
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 10, 0, 8),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"),
                ColumnSpacing = 8,
                Children =
                {
                    AtColumn(new TextBlock
                    {
                        Text = "Live edit",
                        FontWeight = FontWeight.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center
                    }, 0),
                    AtColumn(_editProperty, 1),
                    AtColumn(_editValue, 2),
                    AtColumn(applyButton, 3)
                }
            }
        };

        var propertyContent = new StackPanel
        {
            Margin = new Thickness(14),
            Children =
            {
                _selectionHeading,
                liveEditor,
                _propertyRows
            }
        };

        _sourceHeading.Text = "No AXAML source match";
        _sourceHeading.FontSize = 15;
        _sourceHeading.FontWeight = FontWeight.SemiBold;
        _sourcePreview.IsReadOnly = true;
        _sourcePreview.AcceptsReturn = true;
        _sourcePreview.TextWrapping = TextWrapping.NoWrap;
        _sourcePreview.FontFamily = new FontFamily("Consolas");
        ScrollViewer.SetHorizontalScrollBarVisibility(_sourcePreview, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(_sourcePreview, ScrollBarVisibility.Auto);
        _sourcePreview.MinHeight = 220;

        var sourcePanel = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Margin = new Thickness(14),
            RowSpacing = 10,
            Children =
            {
                AtRow(new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        _sourceHeading,
                        new TextBlock
                        {
                            Text = "Named controls map exactly. Unnamed controls map only when their type is unique in the project.",
                            Foreground = MutedBrush,
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                }, 0),
                AtRow(_sourcePreview, 1)
            }
        };

        var detailsTabs = new TabControl
        {
            Items =
            {
                new TabItem
                {
                    Header = "Properties",
                    Content = new ScrollViewer
                    {
                        Content = propertyContent,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                    }
                },
                new TabItem
                {
                    Header = "AXAML source",
                    Content = sourcePanel
                }
            }
        };

        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("0.46*,5,0.54*"),
            Children =
            {
                AtColumn(treePanel, 0),
                AtColumn(new GridSplitter
                {
                    Width = 5,
                    Background = BorderBrush,
                    ResizeDirection = GridResizeDirection.Columns
                }, 1),
                AtColumn(detailsTabs, 2)
            }
        };

        _status.Foreground = MutedBrush;
        _status.VerticalAlignment = VerticalAlignment.Center;
        var statusBar = new Border
        {
            Background = PanelBrush,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12, 6),
            Child = _status
        };

        return new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Children =
            {
                AtRow(toolbar, 0),
                AtRow(content, 1),
                AtRow(statusBar, 2)
            }
        };
    }

    private TreeViewItem? BuildTreeItem(Visual visual, string? filter, ref int count, bool forceInclude = false)
    {
        var children = new List<TreeViewItem>();
        foreach (var child in visual.GetVisualChildren())
        {
            var childItem = BuildTreeItem(child, filter, ref count);
            if (childItem is not null) children.Add(childItem);
        }

        var ownMatch = string.IsNullOrWhiteSpace(filter)
                       || DeveloperElementFormatter.Matches(visual, filter);
        if (!forceInclude && !ownMatch && children.Count == 0) return null;

        count++;
        var item = new TreeViewItem
        {
            Header = BuildTreeHeader(visual),
            Tag = visual,
            IsExpanded = forceInclude || (!string.IsNullOrWhiteSpace(filter) && children.Count > 0)
        };
        foreach (var child in children) item.Items.Add(child);
        _itemsByVisual[visual] = item;
        return item;
    }

    private static Control BuildTreeHeader(Visual visual)
    {
        var type = visual.GetType().Name;
        var name = string.Empty;
        if (visual is StyledElement element && !string.IsNullOrEmpty(element.Name))
            name = $"#{element.Name}";
        var classes = visual is StyledElement styled && styled.Classes.Count > 0
            ? "." + string.Join('.', styled.Classes)
            : string.Empty;
        var text = DeveloperElementFormatter.GetBriefText(visual);

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = type, Foreground = AccentBrush, FontFamily = new FontFamily("Consolas") },
                new TextBlock { Text = name, Foreground = new SolidColorBrush(Color.FromRgb(249, 199, 79)), FontFamily = new FontFamily("Consolas") },
                new TextBlock { Text = classes, Foreground = new SolidColorBrush(Color.FromRgb(142, 215, 133)), FontFamily = new FontFamily("Consolas") },
                new TextBlock { Text = text, Foreground = MutedBrush, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260 }
            }
        };
    }

    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_tree.SelectedItem is TreeViewItem { Tag: Visual visual })
            SelectVisual(visual);
    }

    private void UpdateSelectionPanels(Visual visual)
    {
        _selectionHeading.Text = DeveloperElementFormatter.BuildSelector(visual);
        _propertyRows.Children.Clear();
        foreach (var property in DeveloperElementFormatter.GetProperties(visual))
            _propertyRows.Children.Add(PropertyRow(property.Name, property.Value));

        _sourceLocation = AxamlSourceLocator.Locate(visual);
        _openSourceButton.IsEnabled = _sourceLocation is not null;
        if (_sourceLocation is null)
        {
            _sourceHeading.Text = "No AXAML source match";
            _sourcePreview.Text = "This may be a template-generated runtime element. Select its nearest named parent, or add x:Name to the control you want to locate exactly.";
        }
        else
        {
            var exactLabel = _sourceLocation.IsExact ? "exact name match" : "unique type match";
            _sourceHeading.Text = $"{Path.GetFileName(_sourceLocation.FilePath)}:{_sourceLocation.Line} • {exactLabel}";
            _sourcePreview.Text = _sourceLocation.Snippet;
        }

        LoadLiveEditValue();
        SetStatus($"Selected {visual.GetType().Name}.");
    }

    private void LoadLiveEditValue()
    {
        if (_selectedVisual is null || _editProperty.SelectedItem is not string property)
        {
            _editValue.Text = string.Empty;
            return;
        }

        _editValue.Text = DeveloperElementEditor.Read(_selectedVisual, property);
    }

    private void ApplyLiveEdit()
    {
        if (_selectedVisual is null || _editProperty.SelectedItem is not string property) return;
        if (DeveloperElementEditor.TryApply(_selectedVisual, property, _editValue.Text ?? string.Empty, out var message))
        {
            UpdateSelectionPanels(_selectedVisual);
            SetStatus(message);
        }
        else
        {
            SetStatus(message);
        }
    }

    private void OpenSource()
    {
        if (_sourceLocation is null) return;
        if (SourceFileLauncher.TryOpen(_sourceLocation, out var message))
            SetStatus(message);
        else
            SetStatus(message);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (e.Key == Key.F12)
        {
            Hide();
            e.Handled = true;
        }
        else if (control && shift && e.Key == Key.C)
        {
            InspectRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
        else if (control && e.Key == Key.F)
        {
            _searchBox.Focus();
            _searchBox.SelectAll();
            e.Handled = true;
        }
    }

    private void SetStatus(string message) => _status.Text = message;

    private static Button ToolbarButton(string text, string tip)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(12, 7),
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(button, tip);
        return button;
    }

    private static Border SectionHeader(string title, string subtitle) => new()
    {
        Background = PanelAltBrush,
        BorderBrush = BorderBrush,
        BorderThickness = new Thickness(0, 0, 0, 1),
        Padding = new Thickness(12, 9),
        Child = new StackPanel
        {
            Spacing = 1,
            Children =
            {
                new TextBlock { Text = title, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = subtitle, Foreground = MutedBrush, FontSize = 11 }
            }
        }
    };

    private static Control PropertyRow(string name, string value)
    {
        var valueBlock = new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas"),
            Foreground = new SolidColorBrush(Color.FromRgb(220, 224, 234))
        };
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("150,*"),
            ColumnSpacing = 12,
            Margin = new Thickness(0, 0, 0, 1),
            Background = PanelAltBrush,
            Children =
            {
                AtColumn(new TextBlock
                {
                    Text = name,
                    Foreground = MutedBrush,
                    Margin = new Thickness(9, 7),
                    VerticalAlignment = VerticalAlignment.Top
                }, 0),
                AtColumn(new Border
                {
                    BorderBrush = BorderBrush,
                    BorderThickness = new Thickness(1, 0, 0, 0),
                    Padding = new Thickness(9, 7),
                    Child = valueBlock
                }, 1)
            }
        };
        return grid;
    }

    private static void ExpandAncestors(TreeViewItem item)
    {
        Control? current = item;
        while (current is not null)
        {
            if (current is TreeViewItem treeItem) treeItem.IsExpanded = true;
            current = current.GetVisualParent<Control>();
        }
    }

    private static T AtColumn<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private static T AtRow<T>(T control, int row) where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }
}
