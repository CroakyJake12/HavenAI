using Avalonia;
using Avalonia.Controls;
using Canvas = Avalonia.Controls.Canvas;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Core;
using Haven.Desktop.Controls;

namespace Haven.Desktop.Dashboard;

internal enum DashboardWidgetDataState
{
    Loading,
    Ready,
    Error,
    Stale
}

internal sealed record DashboardWidgetViewState(
    DashboardTileDefinition Definition,
    DashboardTileData? Data,
    DashboardWidgetDataState State,
    string? ErrorMessage = null);

internal sealed record DashboardWidgetPlacement(
    string Key,
    int Column,
    int Row,
    int Width,
    int Height,
    bool IsVisible = true);

internal static class DashboardWidgetLayoutEngine
{
    public const int Columns = 6;

    public static IReadOnlyList<DashboardWidgetPlacement> EnsurePlacements(
        IEnumerable<DashboardTileDefinition> definitions,
        IReadOnlyList<DashboardWidgetPlacement>? existing = null)
    {
        var ordered = definitions
            .GroupBy(definition => definition.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(definition => definition.DefaultOrder)
            .ThenBy(definition => definition.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var existingByKey = (existing ?? [])
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => Normalize(group.Last()), StringComparer.OrdinalIgnoreCase);
        var result = new List<DashboardWidgetPlacement>(ordered.Length);

        foreach (var definition in ordered)
        {
            if (existingByKey.TryGetValue(definition.Key, out var restored))
            {
                result.Add(restored);
                continue;
            }

            var (width, height) = SpanFor(definition.DefaultSize);
            result.Add(FindFirstOpen(definition.Key, width, height, result, isVisible: true));
        }

        return ResolveCollisions(result, priorityKey: null);
    }

    public static IReadOnlyList<DashboardWidgetPlacement> Move(
        IReadOnlyList<DashboardWidgetPlacement> layout,
        string key,
        int column,
        int row)
    {
        var changed = layout
            .Select(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase)
                ? Normalize(item with { Column = column, Row = row })
                : Normalize(item))
            .ToArray();
        return ResolveCollisions(changed, key);
    }

    public static IReadOnlyList<DashboardWidgetPlacement> Resize(
        IReadOnlyList<DashboardWidgetPlacement> layout,
        string key,
        int width,
        int height)
    {
        var changed = layout
            .Select(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase)
                ? Normalize(item with { Width = width, Height = height })
                : Normalize(item))
            .ToArray();
        return ResolveCollisions(changed, key);
    }

    public static IReadOnlyList<DashboardWidgetPlacement> SetVisibility(
        IReadOnlyList<DashboardWidgetPlacement> layout,
        string key,
        bool isVisible)
    {
        var normalized = layout.Select(Normalize).ToList();
        var index = normalized.FindIndex(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return normalized;
        var current = normalized[index];
        if (current.IsVisible == isVisible) return normalized;

        if (!isVisible)
        {
            normalized[index] = current with { IsVisible = false };
            return normalized;
        }

        normalized.RemoveAt(index);
        var open = FindFirstOpen(current.Key, current.Width, current.Height, normalized, isVisible: true);
        normalized.Insert(index, open);
        return ResolveCollisions(normalized, current.Key);
    }

    public static bool Intersects(DashboardWidgetPlacement left, DashboardWidgetPlacement right)
    {
        if (!left.IsVisible || !right.IsVisible) return false;
        return left.Column < right.Column + right.Width
               && left.Column + left.Width > right.Column
               && left.Row < right.Row + right.Height
               && left.Row + left.Height > right.Row;
    }

    public static DashboardTileSize ToTileSize(DashboardWidgetPlacement placement) =>
        placement.Width >= Columns ? DashboardTileSize.Wide
        : placement.Width <= 2 && placement.Height <= 1 ? DashboardTileSize.Compact
        : DashboardTileSize.Standard;

    private static IReadOnlyList<DashboardWidgetPlacement> ResolveCollisions(
        IReadOnlyList<DashboardWidgetPlacement> layout,
        string? priorityKey)
    {
        var normalized = layout.Select(Normalize).ToArray();
        var visible = normalized.Where(item => item.IsVisible).ToList();
        var ordered = new List<DashboardWidgetPlacement>(visible.Count);

        if (!string.IsNullOrWhiteSpace(priorityKey))
        {
            var priority = visible.FirstOrDefault(item => item.Key.Equals(priorityKey, StringComparison.OrdinalIgnoreCase));
            if (priority is not null) ordered.Add(priority);
        }

        foreach (var item in visible
                     .Where(item => string.IsNullOrWhiteSpace(priorityKey) || !item.Key.Equals(priorityKey, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(item => item.Row)
                     .ThenBy(item => item.Column)
                     .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            var candidate = item;
            while (ordered.Any(placed => Intersects(candidate, placed)))
                candidate = candidate with { Row = candidate.Row + 1 };
            ordered.Add(candidate);
        }

        var byKey = ordered.ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);
        return normalized
            .Select(item => item.IsVisible && byKey.TryGetValue(item.Key, out var resolved) ? resolved : item)
            .ToArray();
    }

    private static DashboardWidgetPlacement FindFirstOpen(
        string key,
        int width,
        int height,
        IReadOnlyCollection<DashboardWidgetPlacement> occupied,
        bool isVisible)
    {
        width = Math.Clamp(width, 1, Columns);
        height = Math.Clamp(height, 1, 6);
        for (var row = 0; row < 1000; row++)
        {
            for (var column = 0; column <= Columns - width; column++)
            {
                var candidate = new DashboardWidgetPlacement(key, column, row, width, height, isVisible);
                if (occupied.All(item => !Intersects(candidate, item))) return candidate;
            }
        }
        return new DashboardWidgetPlacement(key, 0, 1000, width, height, isVisible);
    }

    private static DashboardWidgetPlacement Normalize(DashboardWidgetPlacement item)
    {
        var width = Math.Clamp(item.Width, 1, Columns);
        var height = Math.Clamp(item.Height, 1, 6);
        var column = Math.Clamp(item.Column, 0, Columns - width);
        var row = Math.Max(0, item.Row);
        return item with { Column = column, Row = row, Width = width, Height = height };
    }

    private static (int Width, int Height) SpanFor(DashboardTileSize size) => size switch
    {
        DashboardTileSize.Compact => (2, 1),
        DashboardTileSize.Wide => (6, 2),
        _ => (3, 2)
    };
}

internal sealed class DashboardWidgetCanvas : Canvas
{
    private const double Gap = 12;
    private const double RowHeight = 118;
    private readonly Dictionary<string, DashboardWidgetViewState> _views = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Control> _frames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<IReadOnlyList<DashboardWidgetPlacement>> _undo = new();
    private readonly Stack<IReadOnlyList<DashboardWidgetPlacement>> _redo = new();
    private IReadOnlyList<DashboardWidgetPlacement> _placements = [];
    private bool _isCustomizing;

    public DashboardWidgetCanvas()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        MinHeight = 480;
        ClipToBounds = false;
        SizeChanged += (_, _) => ApplyGeometry();
    }

    public event Action<IReadOnlyList<DashboardWidgetPlacement>>? LayoutChanged;
    public event Action<string>? OpenRequested;

    public IReadOnlyList<DashboardWidgetPlacement> Placements => _placements;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void SetWidgets(
        IReadOnlyList<DashboardWidgetViewState> views,
        IReadOnlyList<DashboardWidgetPlacement>? placements,
        bool isCustomizing)
    {
        _views.Clear();
        foreach (var view in views) _views[view.Definition.Key] = view;
        _placements = DashboardWidgetLayoutEngine.EnsurePlacements(views.Select(view => view.Definition), placements);
        _isCustomizing = isCustomizing;
        _undo.Clear();
        _redo.Clear();
        Rebuild();
    }

    public void SetCustomizing(bool isCustomizing)
    {
        if (_isCustomizing == isCustomizing) return;
        _isCustomizing = isCustomizing;
        Rebuild();
    }

    public bool MoveWidget(string key, int column, int row) => Mutate(layout => DashboardWidgetLayoutEngine.Move(layout, key, column, row));
    public bool ResizeWidget(string key, int width, int height) => Mutate(layout => DashboardWidgetLayoutEngine.Resize(layout, key, width, height));
    public bool HideWidget(string key) => Mutate(layout => DashboardWidgetLayoutEngine.SetVisibility(layout, key, false));
    public bool ShowWidget(string key) => Mutate(layout => DashboardWidgetLayoutEngine.SetVisibility(layout, key, true));

    public bool ApplyLayout(IReadOnlyList<DashboardWidgetPlacement> layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var next = DashboardWidgetLayoutEngine.EnsurePlacements(_views.Values.Select(view => view.Definition), layout);
        if (_placements.SequenceEqual(next)) return false;
        _undo.Push(_placements.ToArray());
        _redo.Clear();
        _placements = next;
        Rebuild();
        LayoutChanged?.Invoke(_placements);
        return true;
    }

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        _redo.Push(_placements.ToArray());
        _placements = _undo.Pop();
        Rebuild();
        LayoutChanged?.Invoke(_placements);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        _undo.Push(_placements.ToArray());
        _placements = _redo.Pop();
        Rebuild();
        LayoutChanged?.Invoke(_placements);
        return true;
    }

    private bool Mutate(Func<IReadOnlyList<DashboardWidgetPlacement>, IReadOnlyList<DashboardWidgetPlacement>> change)
    {
        var next = change(_placements);
        if (_placements.SequenceEqual(next)) return false;
        _undo.Push(_placements.ToArray());
        _redo.Clear();
        _placements = next;
        Rebuild();
        LayoutChanged?.Invoke(_placements);
        return true;
    }

    private void Rebuild()
    {
        Children.Clear();
        _frames.Clear();
        foreach (var placement in _placements.Where(item => item.IsVisible).OrderBy(item => item.Row).ThenBy(item => item.Column))
        {
            if (!_views.TryGetValue(placement.Key, out var view)) continue;
            var frame = BuildFrame(view, placement.Key);
            _frames[placement.Key] = frame;
            Children.Add(frame);
        }
        ApplyGeometry();
    }

    private Control BuildFrame(DashboardWidgetViewState view, string key)
    {
        var stateBadge = view.State switch
        {
            DashboardWidgetDataState.Loading => "Loading",
            DashboardWidgetDataState.Error => "Error",
            DashboardWidgetDataState.Stale => "Stale",
            _ => view.Data?.Badge
        };
        var primary = view.State switch
        {
            DashboardWidgetDataState.Loading => "Loading…",
            DashboardWidgetDataState.Error => "Unavailable",
            _ => view.Data?.Primary ?? string.Empty
        };
        var secondary = view.State switch
        {
            DashboardWidgetDataState.Error => view.ErrorMessage ?? "This widget could not refresh.",
            DashboardWidgetDataState.Loading => view.Definition.Description,
            _ => view.Data?.Secondary ?? view.Definition.Description
        };

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 9 };
        header.Children.Add(new HavenIcon { IconKey = view.Definition.IconKey, Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center });
        var title = new TextBlock { Text = view.Definition.Title, FontWeight = FontWeight.SemiBold, FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(title, 1);
        header.Children.Add(title);
        if (!string.IsNullOrWhiteSpace(stateBadge))
        {
            var badge = new TextBlock { Text = stateBadge, FontSize = 10, Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(badge, 2);
            header.Children.Add(badge);
        }

        var valueStack = new StackPanel { Spacing = 3 };
        valueStack.Children.Add(new TextBlock { Text = primary, FontSize = 28, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        valueStack.Children.Add(new TextBlock { Text = secondary, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, MaxLines = 2 });
        if (view.State is DashboardWidgetDataState.Ready or DashboardWidgetDataState.Stale)
            valueStack.Children.Add(new TextBlock { Text = view.Definition.Description, Classes = { "muted2" }, FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis });

        var open = new HavenButton
        {
            Content = view.Definition.ProviderKey.Equals("custom-local", StringComparison.OrdinalIgnoreCase) ? "Edit" : "Open",
            Classes = { "subtle" },
            HorizontalAlignment = HorizontalAlignment.Left
        };
        open.Click += (_, _) => OpenRequested?.Invoke(view.Definition.ActionKey);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        actions.Children.Add(open);
        if (_isCustomizing)
        {
            var hide = new HavenButton { Content = "Hide", Classes = { "subtle" } };
            hide.Click += (_, _) => HideWidget(key);
            actions.Children.Add(hide);
        }

        var resize = new Border
        {
            Width = 22,
            Height = 22,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = Brushes.Transparent,
            Child = new TextBlock { Text = "↘", FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            IsVisible = _isCustomizing
        };

        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 10 };
        content.Children.Add(header);
        Grid.SetRow(valueStack, 1);
        content.Children.Add(valueStack);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        footer.Children.Add(actions);
        Grid.SetColumn(resize, 1);
        footer.Children.Add(resize);
        Grid.SetRow(footer, 2);
        content.Children.Add(footer);

        var frame = new HavenAdaptiveSurface
        {
            Classes = { "dashboardTile" },
            Padding = new Thickness(16),
            ClipToBounds = true,
            Tag = key,
            Child = content
        };
        AttachDrag(header, frame, key);
        AttachResize(resize, frame, key);
        return frame;
    }

    private void AttachDrag(Control handle, Control frame, string key)
    {
        var dragging = false;
        Point start = default;
        double originalX = 0;
        double originalY = 0;
        handle.PointerPressed += (_, args) =>
        {
            if (!_isCustomizing || !args.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;
            dragging = true;
            start = args.GetPosition(this);
            originalX = Canvas.GetLeft(frame);
            originalY = Canvas.GetTop(frame);
            args.Pointer.Capture(handle);
            args.Handled = true;
        };
        handle.PointerMoved += (_, args) =>
        {
            if (!dragging) return;
            var point = args.GetPosition(this);
            Canvas.SetLeft(frame, Math.Max(0, originalX + point.X - start.X));
            Canvas.SetTop(frame, Math.Max(0, originalY + point.Y - start.Y));
            args.Handled = true;
        };
        handle.PointerReleased += (_, args) =>
        {
            if (!dragging) return;
            dragging = false;
            args.Pointer.Capture(null);
            CommitDrag(frame, key);
            args.Handled = true;
        };
        handle.PointerCaptureLost += (_, _) =>
        {
            if (!dragging) return;
            dragging = false;
            CommitDrag(frame, key);
        };
    }

    private void CommitDrag(Control frame, string key)
    {
        var unitX = CellWidth + Gap;
        var unitY = RowHeight + Gap;
        var column = (int)Math.Round(Math.Max(0, Canvas.GetLeft(frame)) / unitX);
        var row = (int)Math.Round(Math.Max(0, Canvas.GetTop(frame)) / unitY);
        MoveWidget(key, column, row);
    }

    private void AttachResize(Control handle, Control frame, string key)
    {
        var resizing = false;
        Point start = default;
        double originalWidth = 0;
        double originalHeight = 0;
        handle.PointerPressed += (_, args) =>
        {
            if (!_isCustomizing || !args.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;
            resizing = true;
            start = args.GetPosition(this);
            originalWidth = frame.Bounds.Width > 0 ? frame.Bounds.Width : frame.Width;
            originalHeight = frame.Bounds.Height > 0 ? frame.Bounds.Height : frame.Height;
            args.Pointer.Capture(handle);
            args.Handled = true;
        };
        handle.PointerMoved += (_, args) =>
        {
            if (!resizing) return;
            var point = args.GetPosition(this);
            frame.Width = Math.Max(CellWidth, originalWidth + point.X - start.X);
            frame.Height = Math.Max(RowHeight, originalHeight + point.Y - start.Y);
            args.Handled = true;
        };
        handle.PointerReleased += (_, args) =>
        {
            if (!resizing) return;
            resizing = false;
            args.Pointer.Capture(null);
            CommitResize(frame, key);
            args.Handled = true;
        };
        handle.PointerCaptureLost += (_, _) =>
        {
            if (!resizing) return;
            resizing = false;
            CommitResize(frame, key);
        };
    }

    private void CommitResize(Control frame, string key)
    {
        var width = Math.Clamp((int)Math.Round((frame.Width + Gap) / (CellWidth + Gap)), 1, DashboardWidgetLayoutEngine.Columns);
        var height = Math.Clamp((int)Math.Round((frame.Height + Gap) / (RowHeight + Gap)), 1, 6);
        ResizeWidget(key, width, height);
    }

    private void ApplyGeometry()
    {
        foreach (var placement in _placements.Where(item => item.IsVisible))
        {
            if (!_frames.TryGetValue(placement.Key, out var frame)) continue;
            Canvas.SetLeft(frame, placement.Column * (CellWidth + Gap));
            Canvas.SetTop(frame, placement.Row * (RowHeight + Gap));
            frame.Width = placement.Width * CellWidth + Math.Max(0, placement.Width - 1) * Gap;
            frame.Height = placement.Height * RowHeight + Math.Max(0, placement.Height - 1) * Gap;
        }

        var rows = _placements.Where(item => item.IsVisible).Select(item => item.Row + item.Height).DefaultIfEmpty(4).Max();
        Height = Math.Max(480, rows * RowHeight + Math.Max(0, rows - 1) * Gap + 8);
    }

    private double CellWidth
    {
        get
        {
            var available = Bounds.Width > 0 ? Bounds.Width : 960;
            return Math.Max(110, (available - Gap * (DashboardWidgetLayoutEngine.Columns - 1)) / DashboardWidgetLayoutEngine.Columns);
        }
    }
}
