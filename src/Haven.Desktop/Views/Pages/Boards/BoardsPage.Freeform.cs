using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Haven.Core;
using AvaloniaCanvas = Avalonia.Controls.Canvas;

namespace Haven.Desktop.Views.Pages.Boards;

public sealed partial class BoardsPage
{
    private bool _freeformMode;

    private void ToggleFreeform()
    {
        _freeformMode = !_freeformMode;
        RebuildEditor();
        SetStatus(_freeformMode ? "Freeform page · drag or resize objects to arrange them" : "Structured page");
    }

    private async Task AddFreeformCardAsync()
    {
        if (_document is null || _page is null) return;
        _boards.AddCanvasObject(
            _document,
            _page.Id,
            NotesCanvasObjectKind.Text,
            "New card",
            60 + (_page.CanvasObjects.Count % 4) * 40,
            60 + (_page.CanvasObjects.Count % 5) * 40,
            300,
            180);
        _freeformMode = true;
        RebuildEditor();
        await SaveAsync("Added Boards freeform card");
    }

    private Control BuildFreeformSurface()
    {
        var canvas = new AvaloniaCanvas
        {
            Width = Math.Max(1800, _page?.CanvasWidth ?? 1800),
            Height = Math.Max(1200, _page?.CanvasHeight ?? 1200),
            Background = new SolidColorBrush(Color.FromArgb(12, 255, 255, 255))
        };
        AutomationProperties.SetName(canvas, "Boards freeform page");

        var objects = _page?.CanvasObjects.OrderBy(value => value.ZIndex).ToArray() ?? [];
        if (objects.Length == 0)
        {
            var empty = new TextBlock
            {
                Text = "This freeform page is empty. Add a freeform card to start arranging ideas.",
                Margin = new Thickness(32),
                TextWrapping = TextWrapping.Wrap
            };
            canvas.Children.Add(empty);
        }

        foreach (var value in objects)
        {
            var card = BuildFreeformObjectCard(canvas, value);
            AvaloniaCanvas.SetLeft(card, value.X);
            AvaloniaCanvas.SetTop(card, value.Y);
            card.SetValue(Panel.ZIndexProperty, value.ZIndex);
            canvas.Children.Add(card);
        }

        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            MinHeight = 600,
            Content = canvas
        };
    }
    private Border BuildFreeformObjectCard(AvaloniaCanvas canvas, NotesCanvasObject value)
    {
        var card = new Border
        {
            Width = Math.Clamp(value.Width, 120, 5000),
            Height = Math.Clamp(value.Height, 80, 5000),
            MinWidth = 120,
            MinHeight = 80,
            Padding = new Thickness(10),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 150, 155, 170)),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(245, 32, 34, 42))
        };
        AutomationProperties.SetName(card, string.IsNullOrWhiteSpace(value.Text) ? $"{value.Kind} freeform object" : value.Text);
        var grid = new Grid();
        var text = new TextBox
        {
            Text = value.Text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap
        };
        text.TextChanged += (_, _) =>
        {
            if (_document is null || _page is null) return;
            _boards.UpdateCanvasObjectText(_document, _page.Id, value.Id, text.Text);
            SetStatus("Unsaved freeform text");
        };
        grid.Children.Add(text);
        var resizeHandle = new Border
        {
            Width = 18,
            Height = 18,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
            Background = new SolidColorBrush(Color.FromArgb(150, 150, 155, 170)),
            CornerRadius = new CornerRadius(4)
        };
        AutomationProperties.SetName(resizeHandle, "Resize freeform object");
        grid.Children.Add(resizeHandle);
        card.Child = grid;
        WireFreeformDrag(canvas, card, value);
        WireFreeformResize(resizeHandle, card, value);
        return card;
    }
    private void WireFreeformDrag(AvaloniaCanvas canvas, Border card, NotesCanvasObject value)
    {
        var moving = false;
        Point start = default;
        double left = 0, top = 0;
        card.PointerPressed += (_, e) =>
        {
            if (e.Handled || e.Source is TextBox) return;
            if (e.GetCurrentPoint(card).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;
            moving = true;
            start = e.GetPosition(canvas);
            left = AvaloniaCanvas.GetLeft(card);
            top = AvaloniaCanvas.GetTop(card);
            e.Pointer.Capture(card);
        };
        card.PointerMoved += (_, e) =>
        {
            if (!moving || _document is null || _page is null) return;
            var current = e.GetPosition(canvas);
            var x = Math.Max(0, left + current.X - start.X);
            var y = Math.Max(0, top + current.Y - start.Y);
            AvaloniaCanvas.SetLeft(card, x);
            AvaloniaCanvas.SetTop(card, y);
            _boards.MoveCanvasObject(_document, _page.Id, value.Id, x, y);
        };
        card.PointerReleased += (_, e) =>
        {
            if (!moving) return;
            moving = false;
            e.Pointer.Capture(null);
            SetStatus("Unsaved freeform position");
        };
    }

    private void WireFreeformResize(Border handle, Border card, NotesCanvasObject value)
    {
        var resizing = false;
        Point start = default;
        double width = 0, height = 0;
        handle.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(handle).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;
            resizing = true;
            start = e.GetPosition(this);
            width = card.Bounds.Width;
            height = card.Bounds.Height;
            e.Pointer.Capture(handle);
            e.Handled = true;
        };
        handle.PointerMoved += (_, e) =>
        {
            if (!resizing || _document is null || _page is null) return;
            var current = e.GetPosition(this);
            var nextWidth = Math.Clamp(width + current.X - start.X, 120, 5000);
            var nextHeight = Math.Clamp(height + current.Y - start.Y, 80, 5000);
            card.Width = nextWidth;
            card.Height = nextHeight;
            _boards.ResizeCanvasObject(_document, _page.Id, value.Id, nextWidth, nextHeight);
            e.Handled = true;
        };
        handle.PointerReleased += (_, e) =>
        {
            if (!resizing) return;
            resizing = false;
            e.Pointer.Capture(null);
            SetStatus("Unsaved freeform size");
            e.Handled = true;
        };
    }
}
