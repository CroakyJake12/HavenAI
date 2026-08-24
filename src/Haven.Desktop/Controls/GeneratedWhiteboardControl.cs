using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Haven.Desktop.HavenUI.Components;

namespace Haven.Desktop.Controls;

/// <summary>
/// Interactive persisted whiteboard for trusted HavenCanvas GenUI components.
/// Inline and fullscreen presentations share one session, so every user or
/// agent edit has the same semantic element IDs, history, and persisted state.
/// </summary>
public sealed class GeneratedWhiteboardControl : UserControl
{
    private static readonly (string Name, string Value)[] Palette =
    [
        ("Red", "#E53935"), ("Orange", "#FB8C00"), ("Yellow", "#FDD835"),
        ("Lime", "#AEEA00"), ("Green", "#43A047"), ("Cyan", "#00ACC1"),
        ("Blue", "#1E88E5"), ("Indigo", "#3949AB"), ("Purple", "#8E24AA"),
        ("Magenta", "#D81B60"), ("Pink", "#F48FB1"), ("White", "#FFFFFF"),
        ("Grey", "#9E9E9E"), ("Charcoal", "#424242"), ("Black", "#111111"),
        ("Brown", "#795548")
    ];

    private readonly WhiteboardSession _session;
    private readonly Action<JsonElement> _persist;
    private readonly Func<JsonElement, Task>? _requestAgent;
    private readonly string _title;
    private readonly string _prompt;

    public GeneratedWhiteboardControl(
        string title,
        string prompt,
        double minHeight,
        JsonElement? persistedState,
        Action<JsonElement> persist,
        Func<JsonElement, Task>? requestAgent = null)
    {
        _title = string.IsNullOrWhiteSpace(title) ? "Whiteboard" : title;
        _prompt = prompt ?? string.Empty;
        _persist = persist ?? throw new ArgumentNullException(nameof(persist));
        _requestAgent = requestAgent;
        _session = WhiteboardSession.Restore(persistedState);
        _session.Changed += PersistSession;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        MinHeight = Math.Max(320, minHeight);
        AutomationProperties.SetName(this, _title);
        Content = BuildEmbeddedContent();
        DetachedFromVisualTree += (_, _) => _session.Changed -= PersistSession;
    }

    private Control BuildEmbeddedContent()
    {
        var drawing = CreateDrawingSurface(Math.Max(280, MinHeight - 92));
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        tools.Children.Add(ToolButton("Select", WhiteboardTool.Select));
        tools.Children.Add(ToolButton("Pen", WhiteboardTool.Pen));
        tools.Children.Add(ToolButton("Highlight", WhiteboardTool.Highlighter));
        tools.Children.Add(ToolButton("Eraser", WhiteboardTool.Eraser));
        tools.Children.Add(ActionButton("Undo", _session.Undo));
        tools.Children.Add(ActionButton("Redo", _session.Redo));

        var open = new HavenPrimaryButton { Content = "Open in Fullscreen", MinWidth = 190 };
        open.Click += async (_, _) => await OpenFullscreenAsync();
        AutomationProperties.SetName(open, "Open whiteboard in fullscreen");

        return new StackPanel
        {
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children =
            {
                drawing,
                new HavenToolbar
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Child = new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                        Children = { tools, Column(open, 1) }
                    }
                }
            }
        };
    }

    private HavenTextButton ToolButton(string label, WhiteboardTool tool)
    {
        var button = new HavenTextButton { Content = label, MinHeight = 38 };
        button.Click += (_, _) => _session.Tool = tool;
        AutomationProperties.SetName(button, $"Use {label} tool");
        return button;
    }

    private static HavenTextButton ActionButton(string label, Action action)
    {
        var button = new HavenTextButton { Content = label, MinHeight = 38 };
        button.Click += (_, _) => action();
        AutomationProperties.SetName(button, label);
        return button;
    }

    private HavenCard CreateDrawingSurface(double minHeight)
    {
        var surface = new WhiteboardDrawingSurface(_session)
        {
            MinHeight = minHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(surface, "Interactive whiteboard canvas");

        var overlay = new StackPanel
        {
            Margin = new Thickness(28, 22),
            IsHitTestVisible = false,
            Children =
            {
                new TextBlock
                {
                    Text = _title,
                    FontSize = 28,
                    FontWeight = FontWeight.ExtraBold,
                    Foreground = Brushes.Black
                },
                new TextBlock
                {
                    Text = _prompt,
                    Margin = new Thickness(0, 10, 0, 0),
                    FontSize = 18,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brushes.Black,
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };

        return new HavenCard
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(0),
            ClipToBounds = true,
            Child = new Grid { Children = { surface, overlay } }
        };
    }

    private async Task OpenFullscreenAsync()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var window = new Window
        {
            Title = _title,
            Width = 1320,
            Height = 860,
            MinWidth = 760,
            MinHeight = 580,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = ResourceBrush("HavenWindowBrush", Color.FromRgb(22, 24, 30))
        };
        var surface = CreateDrawingSurface(620);
        var status = new TextBlock
        {
            Text = "Select an element to move, copy, update, or delete it.",
            Classes = { "muted" },
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap
        };

        var toolStrip = new WrapPanel { Orientation = Orientation.Horizontal };
        AddTool(toolStrip, "Select", WhiteboardTool.Select);
        AddTool(toolStrip, "Pen", WhiteboardTool.Pen);
        AddTool(toolStrip, "Highlighter", WhiteboardTool.Highlighter);
        AddTool(toolStrip, "Eraser", WhiteboardTool.Eraser);
        AddTool(toolStrip, "Text", WhiteboardTool.Text);
        AddTool(toolStrip, "Rectangle", WhiteboardTool.Rectangle);
        AddTool(toolStrip, "Ellipse", WhiteboardTool.Ellipse);
        AddTool(toolStrip, "Line", WhiteboardTool.Line);
        AddTool(toolStrip, "Pan", WhiteboardTool.Pan);

        var thickness = new HavenSlider
        {
            Minimum = 2,
            Maximum = 32,
            Value = _session.Thickness,
            MinWidth = 190
        };
        thickness.ValueChanged += (_, _) => _session.Thickness = thickness.Value;
        AutomationProperties.SetName(thickness, "Pen thickness");

        var palette = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var colour in Palette) palette.Children.Add(BuildColorButton(colour.Name, colour.Value));
        palette.Children.Add(BuildRainbowButton());

        var customColour = new HavenTextInput
        {
            Text = _session.Color,
            PlaceholderText = "#RRGGBB",
            MinWidth = 120,
            MaxWidth = 150
        };
        var applyColour = new HavenSecondaryButton { Content = "Color Picker" };
        applyColour.Click += (_, _) =>
        {
            if (TryNormaliseColor(customColour.Text, out var colour))
            {
                _session.Color = colour;
                customColour.Text = colour;
                status.Text = $"Colour set to {colour}.";
            }
            else status.Text = "Enter a colour in #RRGGBB format.";
        };

        var effect = new HavenSecondaryButton { Content = $"Pen Effect: {_session.Effect}" };
        effect.Click += (_, _) =>
        {
            _session.Effect = _session.Effect switch
            {
                WhiteboardPenEffect.Solid => WhiteboardPenEffect.Glow,
                WhiteboardPenEffect.Glow => WhiteboardPenEffect.Dotted,
                _ => WhiteboardPenEffect.Solid
            };
            effect.Content = $"Pen Effect: {_session.Effect}";
            status.Text = $"{_session.Effect} pen effect selected.";
        };
        AutomationProperties.SetName(effect, "Generate Fancy Pen Texture or Effect");

        var textEditor = new HavenTextInput
        {
            PlaceholderText = "Text to add or update",
            MinWidth = 220
        };
        var addText = new HavenSecondaryButton { Content = "Add / Update Text" };
        addText.Click += (_, _) =>
        {
            var value = textEditor.Text?.Trim();
            if (string.IsNullOrWhiteSpace(value)) return;
            if (!_session.UpdateSelectedText(value))
                _session.CommitText(new Point(90, 130), value);
            _session.Tool = WhiteboardTool.Select;
            status.Text = "Text added to the canvas.";
        };

        var insertImage = new HavenSecondaryButton { Content = "Insert Image" };
        insertImage.Click += async (_, _) =>
        {
            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Insert an image",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Images")
                    {
                        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.gif", "*.bmp"]
                    }
                ]
            });
            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (path is null) return;
            _session.CommitImage(new Point(100, 150), path);
            status.Text = $"Inserted {Path.GetFileName(path)}.";
        };

        var gridToggle = new HavenSecondaryButton { Content = _session.ShowGrid ? "Hide Grid / Ruler" : "Show Grid / Ruler" };
        gridToggle.Click += (_, _) =>
        {
            _session.ShowGrid = !_session.ShowGrid;
            gridToggle.Content = _session.ShowGrid ? "Hide Grid / Ruler" : "Show Grid / Ruler";
        };
        var fit = new HavenSecondaryButton { Content = "Fit Canvas" };
        fit.Click += (_, _) => _session.ResetViewport();

        var undo = ActionButton("Undo", _session.Undo);
        var redo = ActionButton("Redo", _session.Redo);
        var copy = ActionButton("Copy", _session.CopySelected);
        var paste = ActionButton("Paste", _session.Paste);
        var delete = new HavenNegativeButton { Content = "Delete Selected" };
        delete.Click += (_, _) => _session.DeleteSelected();
        var clear = new HavenNegativeButton { Content = "Clear Canvas" };
        clear.Click += (_, _) => _session.Clear();

        var askText = new HavenTextInput
        {
            PlaceholderText = "Ask Haven to add, mark, or refine something",
            MinWidth = 300
        };
        var ask = new HavenPrimaryButton { Content = "Ask Haven", MinWidth = 112 };
        ask.Click += async (_, _) =>
        {
            var instruction = askText.Text?.Trim();
            if (string.IsNullOrWhiteSpace(instruction)) return;
            if (_requestAgent is null)
            {
                status.Text = "This generated canvas has no agent action attached.";
                return;
            }

            ask.IsEnabled = false;
            status.Text = "Sending the selected canvas context to Haven…";
            try
            {
                await _requestAgent(JsonSerializer.SerializeToElement(new
                {
                    instruction,
                    title = _title,
                    prompt = _prompt,
                    selectedElementIds = _session.SelectedId is null ? Array.Empty<string>() : new[] { _session.SelectedId },
                    canvasState = _session.ToJson()
                }));
                status.Text = "Haven received the whiteboard request.";
                askText.Text = string.Empty;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                status.Text = "Haven could not process the whiteboard request: " + exception.Message;
            }
            finally { ask.IsEnabled = true; }
        };
        AutomationProperties.SetName(ask, "Ask Haven about the whiteboard selection");

        var close = new HavenPrimaryButton { Content = "Close", MinWidth = 110 };
        close.Click += (_, _) => window.Close();

        var controls = new HavenPanel
        {
            Padding = new Thickness(14),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    toolStrip,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 10,
                        Children =
                        {
                            new TextBlock { Text = "Thickness", FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center },
                            thickness,
                            effect,
                            gridToggle,
                            fit
                        }
                    },
                    palette,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children = { customColour, applyColour, textEditor, addText, insertImage }
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children = { undo, redo, copy, paste, delete, clear }
                    },
                    new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                        ColumnSpacing = 8,
                        Children = { askText, Column(ask, 1) }
                    },
                    status
                }
            }
        };

        window.Content = new Grid
        {
            Margin = new Thickness(14),
            RowDefinitions = new RowDefinitions("*,Auto,Auto"),
            RowSpacing = 12,
            Children =
            {
                Row(surface, 0),
                Row(new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    MaxHeight = 245,
                    Content = controls
                }, 1),
                Row(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { close }
                }, 2)
            }
        };

        await window.ShowDialog(owner);
    }

    private void AddTool(Panel panel, string label, WhiteboardTool tool)
    {
        var button = ToolButton(label, tool);
        button.Margin = new Thickness(0, 0, 6, 6);
        panel.Children.Add(button);
    }

    private HavenIconButton BuildColorButton(string label, string color)
    {
        var button = new HavenIconButton
        {
            Width = 34,
            Height = 34,
            Margin = new Thickness(0, 0, 5, 5),
            Content = new HavenCard
            {
                Width = 20,
                Height = 20,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.Parse(color))
            }
        };
        button.Click += (_, _) =>
        {
            _session.Color = color;
            _session.Tool = WhiteboardTool.Pen;
        };
        AutomationProperties.SetName(button, label);
        ToolTip.SetTip(button, label);
        return button;
    }

    private HavenIconButton BuildRainbowButton()
    {
        var button = new HavenIconButton
        {
            Width = 34,
            Height = 34,
            Margin = new Thickness(0, 0, 5, 5),
            Content = new HavenCard
            {
                Width = 20,
                Height = 20,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(10),
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.Parse("#E53935"), 0),
                        new GradientStop(Color.Parse("#FDD835"), 0.25),
                        new GradientStop(Color.Parse("#43A047"), 0.5),
                        new GradientStop(Color.Parse("#1E88E5"), 0.75),
                        new GradientStop(Color.Parse("#8E24AA"), 1)
                    }
                }
            }
        };
        button.Click += (_, _) =>
        {
            _session.Color = "#8E24AA";
            _session.Effect = WhiteboardPenEffect.Glow;
            _session.Tool = WhiteboardTool.Pen;
        };
        AutomationProperties.SetName(button, "Rainbow pen");
        ToolTip.SetTip(button, "Rainbow / glow pen");
        return button;
    }

    private static bool TryNormaliseColor(string? value, out string colour)
    {
        colour = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var candidate = value.Trim();
        if (!candidate.StartsWith('#')) candidate = "#" + candidate;
        if (candidate.Length != 7) return false;
        try
        {
            _ = Color.Parse(candidate);
            colour = candidate.ToUpperInvariant();
            return true;
        }
        catch (FormatException) { return false; }
    }

    private void PersistSession() => _persist(_session.ToJson());

    private static T Column<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private static T Row<T>(T control, int row) where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }

    private static IBrush ResourceBrush(string key, Color fallback) =>
        Avalonia.Application.Current?.Resources[key] as IBrush ?? new SolidColorBrush(fallback);

    internal enum WhiteboardTool
    {
        Select,
        Pen,
        Highlighter,
        Eraser,
        Text,
        Rectangle,
        Ellipse,
        Line,
        Pan
    }

    internal enum WhiteboardElementKind { Stroke, Text, Rectangle, Ellipse, Line, Image }
    internal enum WhiteboardPenEffect { Solid, Glow, Dotted }

    internal sealed record WhiteboardInkPoint(double X, double Y, double Pressure = 0.5)
    {
        public Point Position => new(X, Y);
    }

    internal sealed record WhiteboardElement(
        string Id,
        WhiteboardElementKind Kind,
        string Color,
        double Thickness,
        double Opacity,
        WhiteboardPenEffect Effect,
        bool IsEraser,
        bool AgentGenerated,
        string Text,
        IReadOnlyList<WhiteboardInkPoint> Points)
    {
        public WhiteboardElement Translate(Vector delta) => this with
        {
            Points = Points.Select(point => point with { X = point.X + delta.X, Y = point.Y + delta.Y }).ToArray()
        };
    }

    internal sealed class WhiteboardSession
    {
        private const int MaximumElements = 250;
        private readonly List<WhiteboardElement> _elements = [];
        private readonly Stack<IReadOnlyList<WhiteboardElement>> _undo = [];
        private readonly Stack<IReadOnlyList<WhiteboardElement>> _redo = [];
        private WhiteboardElement? _clipboard;
        private WhiteboardTool _tool = WhiteboardTool.Pen;
        private WhiteboardPenEffect _effect = WhiteboardPenEffect.Solid;
        private string _color = "#111111";
        private double _thickness = 6;
        private double _zoom = 1;
        private Vector _offset;
        private bool _showGrid;

        public event Action? Changed;
        public event Action? Invalidated;
        public IReadOnlyList<WhiteboardElement> Elements => _elements;
        public string? SelectedId { get; private set; }

        public WhiteboardTool Tool
        {
            get => _tool;
            set { _tool = value; Invalidated?.Invoke(); }
        }

        public WhiteboardPenEffect Effect
        {
            get => _effect;
            set { _effect = value; Changed?.Invoke(); Invalidated?.Invoke(); }
        }

        public string Color
        {
            get => _color;
            set { if (TryNormaliseColor(value, out var colour)) _color = colour; Changed?.Invoke(); }
        }

        public double Thickness
        {
            get => _thickness;
            set { _thickness = Math.Clamp(value, 2, 32); Changed?.Invoke(); }
        }

        public double Zoom => _zoom;
        public Vector Offset => _offset;

        public bool ShowGrid
        {
            get => _showGrid;
            set { _showGrid = value; Changed?.Invoke(); Invalidated?.Invoke(); }
        }

        public void CommitStroke(IReadOnlyList<WhiteboardInkPoint> points)
        {
            if (points.Count < 2) return;
            Commit(new WhiteboardElement(
                Guid.NewGuid().ToString("N"), WhiteboardElementKind.Stroke, _color, _thickness,
                _tool == WhiteboardTool.Highlighter ? 0.34 : 1,
                _effect, false, false, string.Empty, points.Take(700).ToArray()));
        }

        public void CommitShape(WhiteboardElementKind kind, Point start, Point end)
        {
            if (Math.Abs(end.X - start.X) < 3 && Math.Abs(end.Y - start.Y) < 3) return;
            Commit(new WhiteboardElement(
                Guid.NewGuid().ToString("N"), kind, _color, _thickness, 1,
                _effect, false, false, string.Empty,
                [new WhiteboardInkPoint(start.X, start.Y), new WhiteboardInkPoint(end.X, end.Y)]));
        }

        public void CommitText(Point point, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            Commit(new WhiteboardElement(
                Guid.NewGuid().ToString("N"), WhiteboardElementKind.Text, _color, _thickness, 1,
                WhiteboardPenEffect.Solid, false, false, text.Trim(),
                [new WhiteboardInkPoint(point.X, point.Y), new WhiteboardInkPoint(point.X + 240, point.Y + 70)]));
        }

        public void CommitImage(Point point, string path) => Commit(new WhiteboardElement(
            Guid.NewGuid().ToString("N"), WhiteboardElementKind.Image, "#111111", 2, 1,
            WhiteboardPenEffect.Solid, false, false, path,
            [new WhiteboardInkPoint(point.X, point.Y), new WhiteboardInkPoint(point.X + 280, point.Y + 180)]));

        private void Commit(WhiteboardElement element)
        {
            PushUndo();
            _elements.Add(element);
            while (_elements.Count > MaximumElements) _elements.RemoveAt(0);
            SelectedId = element.Id;
            Changed?.Invoke();
            Invalidated?.Invoke();
        }

        public bool SelectAt(Point point)
        {
            SelectedId = _elements.LastOrDefault(element => HitTest(element, point, 14 / _zoom))?.Id;
            Invalidated?.Invoke();
            return SelectedId is not null;
        }

        public bool EraseAt(Point point)
        {
            var element = _elements.LastOrDefault(candidate => HitTest(candidate, point, 18 / _zoom));
            if (element is null) return false;
            PushUndo();
            _elements.Remove(element);
            if (SelectedId == element.Id) SelectedId = null;
            Changed?.Invoke();
            Invalidated?.Invoke();
            return true;
        }

        public WhiteboardElement? SelectedElement() =>
            SelectedId is null ? null : _elements.FirstOrDefault(item => item.Id == SelectedId);

        public void PreviewMove(WhiteboardElement original, Vector delta)
        {
            var index = _elements.FindIndex(item => item.Id == original.Id);
            if (index < 0) return;
            _elements[index] = original.Translate(delta);
            Invalidated?.Invoke();
        }

        public void CommitPreview(WhiteboardElement original)
        {
            var current = SelectedElement();
            if (current is null || current == original) return;
            var previous = CloneElements(_elements);
            var index = previous.ToList().FindIndex(item => item.Id == original.Id);
            if (index >= 0)
            {
                var mutable = previous.ToList();
                mutable[index] = Clone(original);
                _undo.Push(mutable);
                _redo.Clear();
            }
            Changed?.Invoke();
        }

        public bool UpdateSelectedText(string text)
        {
            var selected = SelectedElement();
            if (selected is null || selected.Kind != WhiteboardElementKind.Text) return false;
            PushUndo();
            _elements[_elements.IndexOf(selected)] = selected with { Text = text.Trim() };
            Changed?.Invoke();
            Invalidated?.Invoke();
            return true;
        }

        public void DeleteSelected()
        {
            var selected = SelectedElement();
            if (selected is null) return;
            PushUndo();
            _elements.Remove(selected);
            SelectedId = null;
            Changed?.Invoke();
            Invalidated?.Invoke();
        }

        public void CopySelected()
        {
            var selected = SelectedElement();
            if (selected is not null) _clipboard = Clone(selected);
        }

        public void CutSelected()
        {
            CopySelected();
            DeleteSelected();
        }

        public void Paste()
        {
            if (_clipboard is null) return;
            var pasted = _clipboard.Translate(new Vector(24, 24)) with { Id = Guid.NewGuid().ToString("N") };
            _clipboard = Clone(pasted);
            Commit(pasted);
        }

        public void Undo()
        {
            if (_undo.Count == 0) return;
            _redo.Push(CloneElements(_elements));
            ReplaceElements(_undo.Pop());
        }

        public void Redo()
        {
            if (_redo.Count == 0) return;
            _undo.Push(CloneElements(_elements));
            ReplaceElements(_redo.Pop());
        }

        public void Clear()
        {
            if (_elements.Count == 0) return;
            PushUndo();
            _elements.Clear();
            SelectedId = null;
            Changed?.Invoke();
            Invalidated?.Invoke();
        }

        public void PanBy(Vector delta)
        {
            _offset += delta;
            Changed?.Invoke();
            Invalidated?.Invoke();
        }

        public void ZoomAt(Point screenPoint, double factor)
        {
            var before = ToBoard(screenPoint);
            _zoom = Math.Clamp(_zoom * factor, 0.25, 4);
            _offset = new Vector(screenPoint.X - before.X * _zoom, screenPoint.Y - before.Y * _zoom);
            Changed?.Invoke();
            Invalidated?.Invoke();
        }

        public void ResetViewport()
        {
            _zoom = 1;
            _offset = default;
            Changed?.Invoke();
            Invalidated?.Invoke();
        }

        public Point ToBoard(Point screenPoint) => new(
            (screenPoint.X - _offset.X) / Math.Max(0.05, _zoom),
            (screenPoint.Y - _offset.Y) / Math.Max(0.05, _zoom));

        private void PushUndo()
        {
            _undo.Push(CloneElements(_elements));
            while (_undo.Count > 80)
            {
                var keep = _undo.Reverse().TakeLast(80).Reverse().ToArray();
                _undo.Clear();
                foreach (var state in keep) _undo.Push(state);
            }
            _redo.Clear();
        }

        private void ReplaceElements(IReadOnlyList<WhiteboardElement> elements)
        {
            _elements.Clear();
            _elements.AddRange(CloneElements(elements));
            if (SelectedId is not null && _elements.All(item => item.Id != SelectedId)) SelectedId = null;
            Changed?.Invoke();
            Invalidated?.Invoke();
        }

        public JsonElement ToJson() => JsonSerializer.SerializeToElement(new WhiteboardStateDto
        {
            Version = 2,
            Tool = _tool.ToString(),
            Effect = _effect.ToString(),
            Color = _color,
            Thickness = _thickness,
            Zoom = _zoom,
            OffsetX = _offset.X,
            OffsetY = _offset.Y,
            ShowGrid = _showGrid,
            Elements = _elements.Select(ToDto).ToList()
        });

        public static WhiteboardSession Restore(JsonElement? state)
        {
            var session = new WhiteboardSession();
            if (state is not { ValueKind: JsonValueKind.Object }) return session;
            try
            {
                var dto = JsonSerializer.Deserialize<WhiteboardStateDto>(state.Value.GetRawText());
                if (dto is null) return session;
                session._tool = Enum.TryParse<WhiteboardTool>(dto.Tool, true, out var tool) ? tool : WhiteboardTool.Pen;
                session._effect = Enum.TryParse<WhiteboardPenEffect>(dto.Effect, true, out var effect) ? effect : WhiteboardPenEffect.Solid;
                session._color = TryNormaliseColor(dto.Color, out var color) ? color : "#111111";
                session._thickness = Math.Clamp(dto.Thickness <= 0 ? 6 : dto.Thickness, 2, 32);
                session._zoom = Math.Clamp(dto.Zoom <= 0 ? 1 : dto.Zoom, 0.25, 4);
                session._offset = new Vector(dto.OffsetX, dto.OffsetY);
                session._showGrid = dto.ShowGrid;

                foreach (var item in dto.Elements.Take(MaximumElements))
                {
                    var element = FromDto(item);
                    if (element is not null) session._elements.Add(element);
                }

                // Version-one canvases persisted only strokes. Keep those
                // drawings intact while assigning stable IDs on migration.
                if (session._elements.Count == 0)
                {
                    foreach (var stroke in dto.Strokes.Take(MaximumElements))
                    {
                        var points = stroke.Points.Take(700)
                            .Select(point => new WhiteboardInkPoint(point.X, point.Y, point.Pressure <= 0 ? 0.5 : point.Pressure))
                            .ToArray();
                        if (points.Length < 2) continue;
                        session._elements.Add(new WhiteboardElement(
                            Guid.NewGuid().ToString("N"), WhiteboardElementKind.Stroke,
                            NormaliseLegacyColor(stroke.Color), Math.Clamp(stroke.Thickness, 2, 32),
                            1, WhiteboardPenEffect.Solid, stroke.IsEraser, false, string.Empty, points));
                    }
                }
            }
            catch (JsonException) { return new WhiteboardSession(); }
            return session;
        }

        private static WhiteboardElementDto ToDto(WhiteboardElement element) => new()
        {
            Id = element.Id,
            Kind = element.Kind.ToString(),
            Color = element.Color,
            Thickness = element.Thickness,
            Opacity = element.Opacity,
            Effect = element.Effect.ToString(),
            IsEraser = element.IsEraser,
            AgentGenerated = element.AgentGenerated,
            Text = element.Text,
            Points = element.Points.Select(point => new WhiteboardPointDto
            {
                X = point.X, Y = point.Y, Pressure = point.Pressure
            }).ToList()
        };

        private static WhiteboardElement? FromDto(WhiteboardElementDto dto)
        {
            if (!Enum.TryParse<WhiteboardElementKind>(dto.Kind, true, out var kind)) return null;
            var points = dto.Points.Take(700)
                .Select(point => new WhiteboardInkPoint(point.X, point.Y, Math.Clamp(point.Pressure, 0.05, 1)))
                .ToArray();
            if (points.Length == 0) return null;
            return new WhiteboardElement(
                Guid.TryParse(dto.Id, out _) || dto.Id?.Length == 32 ? dto.Id! : Guid.NewGuid().ToString("N"),
                kind,
                TryNormaliseColor(dto.Color, out var color) ? color : "#111111",
                Math.Clamp(dto.Thickness <= 0 ? 6 : dto.Thickness, 2, 32),
                Math.Clamp(dto.Opacity <= 0 ? 1 : dto.Opacity, 0.1, 1),
                Enum.TryParse<WhiteboardPenEffect>(dto.Effect, true, out var effect) ? effect : WhiteboardPenEffect.Solid,
                dto.IsEraser,
                dto.AgentGenerated,
                dto.Text ?? string.Empty,
                points);
        }

        private static bool HitTest(WhiteboardElement element, Point point, double radius)
        {
            if (element.Points.Count == 0) return false;
            if (element.Kind == WhiteboardElementKind.Stroke)
                return element.Points.Any(candidate => DistanceSquared(candidate.Position, point) <= radius * radius);

            var bounds = BoundsOf(element).Inflate(radius);
            if (!bounds.Contains(point)) return false;
            if (element.Kind == WhiteboardElementKind.Line && element.Points.Count > 1)
                return DistanceToSegment(point, element.Points[0].Position, element.Points[1].Position) <= radius;
            return true;
        }

        internal static Rect BoundsOf(WhiteboardElement element)
        {
            var minX = element.Points.Min(point => point.X);
            var minY = element.Points.Min(point => point.Y);
            var maxX = element.Points.Max(point => point.X);
            var maxY = element.Points.Max(point => point.Y);
            if (element.Kind == WhiteboardElementKind.Text && maxX - minX < 40) maxX = minX + 240;
            if (element.Kind == WhiteboardElementKind.Text && maxY - minY < 24) maxY = minY + 70;
            return new Rect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
        }

        private static double DistanceToSegment(Point point, Point start, Point end)
        {
            var lengthSquared = DistanceSquared(start, end);
            if (lengthSquared <= double.Epsilon) return Math.Sqrt(DistanceSquared(point, start));
            var t = Math.Clamp(((point.X - start.X) * (end.X - start.X) + (point.Y - start.Y) * (end.Y - start.Y)) / lengthSquared, 0, 1);
            return Math.Sqrt(DistanceSquared(point, new Point(start.X + t * (end.X - start.X), start.Y + t * (end.Y - start.Y))));
        }

        private static double DistanceSquared(Point first, Point second)
        {
            var dx = first.X - second.X;
            var dy = first.Y - second.Y;
            return dx * dx + dy * dy;
        }

        private static string NormaliseLegacyColor(string value) => value.ToLowerInvariant() switch
        {
            "blue" => "#1E88E5",
            "red" => "#E53935",
            "green" => "#43A047",
            "purple" => "#8E24AA",
            "white" => "#FFFFFF",
            _ => TryNormaliseColor(value, out var color) ? color : "#111111"
        };

        private static WhiteboardElement Clone(WhiteboardElement element) => element with
        {
            Points = element.Points.Select(point => point with { }).ToArray()
        };

        private static IReadOnlyList<WhiteboardElement> CloneElements(IEnumerable<WhiteboardElement> elements) =>
            elements.Select(Clone).ToArray();
    }

    private sealed class WhiteboardDrawingSurface : Control
    {
        private const int MaximumPointsPerStroke = 700;
        private readonly WhiteboardSession _session;
        private readonly Dictionary<string, Bitmap> _images = new(StringComparer.OrdinalIgnoreCase);
        private List<WhiteboardInkPoint>? _pending;
        private Point? _shapeStart;
        private Point _shapeCurrent;
        private Point _pointerStart;
        private Point _lastBoardPoint;
        private WhiteboardElement? _movingOriginal;
        private bool _panning;
        private long _strokeStart;

        public WhiteboardDrawingSurface(WhiteboardSession session)
        {
            _session = session;
            _session.Changed += OnSessionChanged;
            _session.Invalidated += OnSessionChanged;
            ClipToBounds = true;
            Focusable = true;
            Cursor = new Cursor(StandardCursorType.Cross);
            DetachedFromVisualTree += (_, _) =>
            {
                _session.Changed -= OnSessionChanged;
                _session.Invalidated -= OnSessionChanged;
                foreach (var image in _images.Values) image.Dispose();
                _images.Clear();
            };
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            context.DrawRectangle(Brushes.White, null, Bounds);
            var transform = Matrix.CreateTranslation(_session.Offset.X, _session.Offset.Y)
                            * Matrix.CreateScale(_session.Zoom, _session.Zoom);
            using (context.PushTransform(transform))
            {
                if (_session.ShowGrid) DrawGrid(context);
                foreach (var element in _session.Elements) DrawElement(context, element);
                DrawPreview(context);
                if (_session.SelectedElement() is { } selected) DrawSelection(context, selected);
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var pointer = e.GetCurrentPoint(this);
            if (!pointer.Properties.IsLeftButtonPressed) return;
            Focus();
            _pointerStart = pointer.Position;
            _lastBoardPoint = _session.ToBoard(pointer.Position);

            switch (_session.Tool)
            {
                case WhiteboardTool.Select:
                    if (_session.SelectAt(_lastBoardPoint)) _movingOriginal = _session.SelectedElement();
                    break;
                case WhiteboardTool.Eraser:
                    _session.EraseAt(_lastBoardPoint);
                    break;
                case WhiteboardTool.Text:
                    _session.CommitText(_lastBoardPoint, "Text");
                    _session.Tool = WhiteboardTool.Select;
                    break;
                case WhiteboardTool.Rectangle:
                case WhiteboardTool.Ellipse:
                case WhiteboardTool.Line:
                    _shapeStart = _lastBoardPoint;
                    _shapeCurrent = _lastBoardPoint;
                    break;
                case WhiteboardTool.Pan:
                    _panning = true;
                    break;
                default:
                    _strokeStart = Environment.TickCount64;
                    _pending = [ToInkPoint(pointer, _lastBoardPoint)];
                    break;
            }

            e.Pointer.Capture(this);
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var pointer = e.GetCurrentPoint(this);
            if (!pointer.Properties.IsLeftButtonPressed) return;
            var boardPoint = _session.ToBoard(pointer.Position);

            if (_pending is not null)
            {
                if (_pending.Count < MaximumPointsPerStroke && DistanceSquared(_pending[^1].Position, boardPoint) >= 1.5)
                    _pending.Add(ToInkPoint(pointer, boardPoint));
            }
            else if (_shapeStart is not null) _shapeCurrent = boardPoint;
            else if (_movingOriginal is not null)
                _session.PreviewMove(_movingOriginal, boardPoint - _lastBoardPoint);
            else if (_panning)
            {
                var delta = pointer.Position - _pointerStart;
                _session.PanBy(delta);
                _pointerStart = pointer.Position;
            }
            else if (_session.Tool == WhiteboardTool.Eraser) _session.EraseAt(boardPoint);

            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (_pending is not null)
            {
                var pointer = e.GetCurrentPoint(this);
                var point = _session.ToBoard(pointer.Position);
                if (_pending.Count < MaximumPointsPerStroke && DistanceSquared(_pending[^1].Position, point) >= 0.5)
                    _pending.Add(ToInkPoint(pointer, point));
                _session.CommitStroke(_pending.ToArray());
            }
            else if (_shapeStart is { } start)
            {
                var kind = _session.Tool switch
                {
                    WhiteboardTool.Rectangle => WhiteboardElementKind.Rectangle,
                    WhiteboardTool.Ellipse => WhiteboardElementKind.Ellipse,
                    _ => WhiteboardElementKind.Line
                };
                _session.CommitShape(kind, start, _shapeCurrent);
            }
            else if (_movingOriginal is not null) _session.CommitPreview(_movingOriginal);

            _pending = null;
            _shapeStart = null;
            _movingOriginal = null;
            _panning = false;
            e.Pointer.Capture(null);
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            _session.ZoomAt(e.GetPosition(this), e.Delta.Y > 0 ? 1.1 : 0.9);
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            if (e.Key is Key.Delete or Key.Back) _session.DeleteSelected();
            else if (control && e.Key == Key.Z) _session.Undo();
            else if (control && e.Key == Key.Y) _session.Redo();
            else if (control && e.Key == Key.C) _session.CopySelected();
            else if (control && e.Key == Key.X) _session.CutSelected();
            else if (control && e.Key == Key.V) _session.Paste();
            else return;
            e.Handled = true;
        }

        private WhiteboardInkPoint ToInkPoint(PointerPoint pointer, Point point)
        {
            var pressure = ReadNumber(pointer.Properties, "Pressure", 0.5);
            return new WhiteboardInkPoint(point.X, point.Y, Math.Clamp(pressure, 0.05, 1));
        }

        private void DrawGrid(DrawingContext context)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(32, 60, 70, 85)), 1 / _session.Zoom);
            const int step = 40;
            for (var x = -2000; x <= 4000; x += step) context.DrawLine(pen, new Point(x, -2000), new Point(x, 4000));
            for (var y = -2000; y <= 4000; y += step) context.DrawLine(pen, new Point(-2000, y), new Point(4000, y));
        }

        private void DrawElement(DrawingContext context, WhiteboardElement element)
        {
            var color = Color.Parse(element.Color);
            color = Color.FromArgb((byte)Math.Clamp(element.Opacity * 255, 0, 255), color.R, color.G, color.B);
            IBrush brush = element.IsEraser ? Brushes.White : new SolidColorBrush(color);
            var pen = new Pen(brush, element.Thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

            if (element.Effect == WhiteboardPenEffect.Glow && !element.IsEraser)
            {
                var glow = new SolidColorBrush(Color.FromArgb(42, color.R, color.G, color.B));
                DrawElementCore(context, element, glow, new Pen(glow, element.Thickness * 3.2, lineCap: PenLineCap.Round), color);
            }
            DrawElementCore(context, element, brush, pen, color);
        }

        private void DrawElementCore(DrawingContext context, WhiteboardElement element, IBrush brush, Pen pen, Color color)
        {
            var bounds = WhiteboardSession.BoundsOf(element);
            switch (element.Kind)
            {
                case WhiteboardElementKind.Stroke:
                    if (element.Effect == WhiteboardPenEffect.Dotted)
                    {
                        foreach (var point in element.Points)
                            context.DrawEllipse(brush, null, point.Position, element.Thickness / 2, element.Thickness / 2);
                        break;
                    }
                    for (var index = 1; index < element.Points.Count; index++)
                    {
                        var pressure = (element.Points[index - 1].Pressure + element.Points[index].Pressure) / 2;
                        var width = element.Thickness * (0.45 + pressure * 0.8);
                        context.DrawLine(new Pen(brush, width, lineCap: PenLineCap.Round),
                            element.Points[index - 1].Position, element.Points[index].Position);
                    }
                    break;
                case WhiteboardElementKind.Rectangle:
                    context.DrawRectangle(new SolidColorBrush(Color.FromArgb(24, color.R, color.G, color.B)), pen, bounds, 8, 8);
                    break;
                case WhiteboardElementKind.Ellipse:
                    context.DrawEllipse(null, pen, bounds.Center, bounds.Width / 2, bounds.Height / 2);
                    break;
                case WhiteboardElementKind.Line:
                    if (element.Points.Count > 1) context.DrawLine(pen, element.Points[0].Position, element.Points[1].Position);
                    break;
                case WhiteboardElementKind.Text:
                    var formatted = new FormattedText(element.Text, CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight, new Typeface("Segoe UI", FontStyle.Normal, FontWeight.SemiBold, FontStretch.Normal),
                        Math.Max(16, element.Thickness * 3), brush);
                    formatted.MaxTextWidth = bounds.Width;
                    context.DrawText(formatted, bounds.TopLeft);
                    break;
                case WhiteboardElementKind.Image:
                    DrawImage(context, element.Text, bounds);
                    break;
            }
        }

        private void DrawImage(DrawingContext context, string path, Rect bounds)
        {
            try
            {
                if (!_images.TryGetValue(path, out var bitmap))
                {
                    bitmap = new Bitmap(path);
                    _images[path] = bitmap;
                }
                context.DrawImage(bitmap, new Rect(bitmap.Size), bounds);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                context.DrawRectangle(new SolidColorBrush(Color.FromRgb(235, 238, 242)), new Pen(Brushes.Gray, 1), bounds, 8, 8);
                var label = new FormattedText(Path.GetFileName(path), CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, Typeface.Default, 14, Brushes.Black)
                {
                    MaxTextWidth = Math.Max(20, bounds.Width - 20)
                };
                context.DrawText(label, new Point(bounds.X + 10, bounds.Y + 10));
            }
        }

        private void DrawPreview(DrawingContext context)
        {
            if (_pending is { Count: > 1 })
            {
                var preview = new WhiteboardElement("preview", WhiteboardElementKind.Stroke,
                    _session.Color, _session.Thickness,
                    _session.Tool == WhiteboardTool.Highlighter ? 0.34 : 1,
                    _session.Effect, false, false, string.Empty, _pending);
                DrawElement(context, preview);
            }
            else if (_shapeStart is { } start)
            {
                var kind = _session.Tool switch
                {
                    WhiteboardTool.Rectangle => WhiteboardElementKind.Rectangle,
                    WhiteboardTool.Ellipse => WhiteboardElementKind.Ellipse,
                    _ => WhiteboardElementKind.Line
                };
                DrawElement(context, new WhiteboardElement("preview", kind, _session.Color,
                    _session.Thickness, 1, _session.Effect, false, false, string.Empty,
                    [new WhiteboardInkPoint(start.X, start.Y), new WhiteboardInkPoint(_shapeCurrent.X, _shapeCurrent.Y)]));
            }
        }

        private void DrawSelection(DrawingContext context, WhiteboardElement element)
        {
            var bounds = WhiteboardSession.BoundsOf(element).Inflate(8 / _session.Zoom);
            var pen = new Pen(new SolidColorBrush(element.AgentGenerated
                ? Color.Parse("#8E24AA")
                : Color.Parse("#1E88E5")), 2 / _session.Zoom, dashStyle: DashStyle.Dash);
            context.DrawRectangle(null, pen, bounds, 5, 5);
            if (element.AgentGenerated)
            {
                var label = new FormattedText("Haven", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    Typeface.Default, 11 / _session.Zoom, new SolidColorBrush(Color.Parse("#8E24AA")));
                context.DrawText(label, new Point(bounds.X, bounds.Y - 18 / _session.Zoom));
            }
        }

        private void OnSessionChanged() => InvalidateVisual();

        private static double DistanceSquared(Point first, Point second)
        {
            var dx = first.X - second.X;
            var dy = first.Y - second.Y;
            return dx * dx + dy * dy;
        }

        private static double ReadNumber(object properties, string name, double fallback)
        {
            try
            {
                var value = properties.GetType().GetProperty(name)?.GetValue(properties);
                return value is null ? fallback : Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException or System.Reflection.TargetInvocationException)
            {
                return fallback;
            }
        }
    }

    private sealed class WhiteboardStateDto
    {
        public int Version { get; set; }
        public string Tool { get; set; } = nameof(WhiteboardTool.Pen);
        public string Effect { get; set; } = nameof(WhiteboardPenEffect.Solid);
        public string Color { get; set; } = "#111111";
        public double Thickness { get; set; } = 6;
        public double Zoom { get; set; } = 1;
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
        public bool ShowGrid { get; set; }
        public List<WhiteboardElementDto> Elements { get; set; } = [];
        public List<WhiteboardStrokeDto> Strokes { get; set; } = [];
    }

    private sealed class WhiteboardElementDto
    {
        public string? Id { get; set; }
        public string Kind { get; set; } = nameof(WhiteboardElementKind.Stroke);
        public string Color { get; set; } = "#111111";
        public double Thickness { get; set; } = 6;
        public double Opacity { get; set; } = 1;
        public string Effect { get; set; } = nameof(WhiteboardPenEffect.Solid);
        public bool IsEraser { get; set; }
        public bool AgentGenerated { get; set; }
        public string? Text { get; set; }
        public List<WhiteboardPointDto> Points { get; set; } = [];
    }

    private sealed class WhiteboardStrokeDto
    {
        public string Color { get; set; } = "black";
        public double Thickness { get; set; } = 6;
        public bool IsEraser { get; set; }
        public List<WhiteboardPointDto> Points { get; set; } = [];
    }

    private sealed class WhiteboardPointDto
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Pressure { get; set; } = 0.5;
    }
}
