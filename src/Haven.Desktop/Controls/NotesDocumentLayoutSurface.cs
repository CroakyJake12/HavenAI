using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Controls;

public static class NotesDocumentLayoutSurface
{
    public static Control BuildPaginated(
        NotesWorkspaceViewModel viewModel,
        NotesAdvancedDocumentState advanced,
        Action<NotesBlock> beginEdit,
        Action<NotesBlock, string> endEdit,
        Action refresh,
        Func<Task> importMedia)
    {
        var document = viewModel.Document ?? throw new InvalidOperationException("No Notes document is open.");
        var section = viewModel.CurrentSection ?? throw new InvalidOperationException("No Notes section is selected.");
        var page = viewModel.CurrentPage ?? throw new InvalidOperationException("No Notes page is selected.");
        var setup = document.PageSetup;
        var scale = Math.Clamp(advanced.View.InterfaceScale, 0.5, 3);
        var usableWidth = Math.Clamp(setup.WidthPoints * scale, 320, 1800);
        var minimumHeight = Math.Clamp(setup.HeightPoints * scale, 420, 2600);
        var content = new StackPanel { Spacing = 12 };
        var variant = advanced.SectionHeaders.TryGetValue(section.Id, out var value)
            ? value
            : new NotesSectionHeaderFooterState();
        var pageIndex = section.Pages.OrderBy(candidate => candidate.Order).ToList().IndexOf(page);
        var isFirst = pageIndex == 0;
        var isOdd = (pageIndex + 1) % 2 == 1;
        var header = HeaderFor(section, variant, advanced.PageLayout, isFirst, isOdd);
        var footer = FooterFor(section, variant, advanced.PageLayout, isFirst, isOdd);
        if (!string.IsNullOrWhiteSpace(header))
            content.Children.Add(new TextBlock
            {
                Text = ExpandFields(header, document, pageIndex + advanced.PageLayout.PageNumberStart),
                Classes = { "muted" },
                TextWrapping = TextWrapping.Wrap,
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Stretch
            });

        if (!string.IsNullOrWhiteSpace(advanced.PageLayout.Watermark))
            content.Children.Add(new TextBlock
            {
                Text = advanced.PageLayout.Watermark,
                Opacity = 0.14,
                FontSize = 42,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 20)
            });

        var columns = Math.Clamp(advanced.PageLayout.Columns, 1, 12);
        if (columns == 1)
        {
            foreach (var block in page.Blocks.OrderBy(block => block.Order))
                content.Children.Add(NotesBlockEditorFactory.Build(viewModel, block, beginEdit, endEdit, refresh, importMedia));
        }
        else
        {
            var grid = new Grid { ColumnSpacing = advanced.PageLayout.ColumnSpacingPoints * scale };
            for (var index = 0; index < columns; index++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var buckets = Enumerable.Range(0, columns).Select(_ => new StackPanel { Spacing = 12 }).ToArray();
            for (var index = 0; index < page.Blocks.Count; index++)
                buckets[index % columns].Children.Add(NotesBlockEditorFactory.Build(viewModel, page.Blocks[index], beginEdit, endEdit, refresh, importMedia));
            for (var index = 0; index < columns; index++)
            {
                Grid.SetColumn(buckets[index], index);
                grid.Children.Add(buckets[index]);
            }
            content.Children.Add(grid);
        }

        if (!string.IsNullOrWhiteSpace(footer) || setup.ShowPageNumbers)
        {
            var pageNumber = pageIndex + advanced.PageLayout.PageNumberStart;
            content.Children.Add(new TextBlock
            {
                Text = ExpandFields(footer, document, pageNumber)
                       + (setup.ShowPageNumbers ? (string.IsNullOrWhiteSpace(footer) ? string.Empty : " · ") + FormatPageNumber(pageNumber, advanced.PageLayout.PageNumberFormat) : string.Empty),
                Classes = { "muted" },
                TextWrapping = TextWrapping.Wrap,
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 20, 0, 0)
            });
        }

        var background = TryBrush(setup.Background, Brushes.White);
        var border = new Border
        {
            Width = usableWidth,
            MinHeight = minimumHeight,
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = background,
            BorderBrush = TryBrush(advanced.PageLayout.PageBorder, new SolidColorBrush(Color.FromArgb(55, 0, 0, 0))),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(
                Math.Clamp(setup.MarginLeftPoints * scale + advanced.PageLayout.GutterPoints * scale, 8, usableWidth / 2 - 1),
                Math.Clamp(setup.MarginTopPoints * scale, 8, minimumHeight / 2 - 1),
                Math.Clamp(setup.MarginRightPoints * scale, 8, usableWidth / 2 - 1),
                Math.Clamp(setup.MarginBottomPoints * scale, 8, minimumHeight / 2 - 1)),
            Child = content,
            BoxShadow = new BoxShadows(new BoxShadow
            {
                Blur = 16,
                OffsetX = 0,
                OffsetY = 5,
                Color = Color.FromArgb(55, 0, 0, 0)
            })
        };
        return new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = $"{page.Title} · {setup.WidthPoints:0}×{setup.HeightPoints:0} pt · {columns} column{(columns == 1 ? string.Empty : "s")}",
                    Classes = { "muted2" },
                    FontSize = 9,
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                border
            }
        };
    }

    public static Control BuildFreeform(
        NotesWorkspaceViewModel viewModel,
        bool infinite,
        Action<NotesBlock> beginEdit,
        Action<NotesBlock, string> endEdit,
        Action refresh,
        Func<Task> importMedia) =>
        new NotesFreeformPageControl(viewModel, infinite, beginEdit, endEdit, refresh, importMedia);

    private static string HeaderFor(
        NotesSection section,
        NotesSectionHeaderFooterState variant,
        NotesExtendedPageLayout layout,
        bool first,
        bool odd)
    {
        if (first && layout.DifferentFirstPage && !string.IsNullOrWhiteSpace(variant.FirstPageHeader)) return variant.FirstPageHeader;
        if (layout.DifferentOddEvenPages)
        {
            var value = odd ? variant.OddPageHeader : variant.EvenPageHeader;
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return section.Header;
    }

    private static string FooterFor(
        NotesSection section,
        NotesSectionHeaderFooterState variant,
        NotesExtendedPageLayout layout,
        bool first,
        bool odd)
    {
        if (first && layout.DifferentFirstPage && !string.IsNullOrWhiteSpace(variant.FirstPageFooter)) return variant.FirstPageFooter;
        if (layout.DifferentOddEvenPages)
        {
            var value = odd ? variant.OddPageFooter : variant.EvenPageFooter;
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return section.Footer;
    }

    private static string ExpandFields(string value, NotesDocument document, int pageNumber)
    {
        var statistics = NotesTextStatistics.Calculate(document);
        return (value ?? string.Empty)
            .Replace("{title}", document.Title, StringComparison.OrdinalIgnoreCase)
            .Replace("{author}", document.Collaboration.OwnerId, StringComparison.OrdinalIgnoreCase)
            .Replace("{date}", DateTimeOffset.Now.ToString("d", CultureInfo.CurrentCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{time}", DateTimeOffset.Now.ToString("t", CultureInfo.CurrentCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{page}", pageNumber.ToString(CultureInfo.CurrentCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{pages}", document.Sections.Sum(section => section.Pages.Count).ToString(CultureInfo.CurrentCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{words}", statistics.Words.ToString(CultureInfo.CurrentCulture), StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatPageNumber(int value, string format) => format switch
    {
        "i, ii, iii" => Roman(value).ToLowerInvariant(),
        "I, II, III" => Roman(value),
        "a, b, c" => Alpha(value).ToLowerInvariant(),
        "A, B, C" => Alpha(value),
        _ => value.ToString(CultureInfo.CurrentCulture)
    };

    private static string Roman(int number)
    {
        number = Math.Clamp(number, 1, 3999);
        var map = new (int Value, string Symbol)[]
        {
            (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
            (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
            (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
        };
        var result = string.Empty;
        foreach (var item in map)
            while (number >= item.Value) { result += item.Symbol; number -= item.Value; }
        return result;
    }

    private static string Alpha(int number)
    {
        number = Math.Max(1, number);
        var result = string.Empty;
        while (number > 0)
        {
            number--;
            result = (char)('A' + number % 26) + result;
            number /= 26;
        }
        return result;
    }

    private static IBrush TryBrush(string value, IBrush fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        try { return new SolidColorBrush(Color.Parse(value)); }
        catch (FormatException) { return fallback; }
    }
}

public sealed class NotesFreeformPageControl : UserControl
{
    private readonly NotesWorkspaceViewModel _viewModel;
    private readonly NotesPage _page;
    private readonly Canvas _surface = new();
    private readonly TextBlock _status = new() { Classes = { "muted2" }, FontSize = 9 };
    private readonly Action<NotesBlock> _beginEdit;
    private readonly Action<NotesBlock, string> _endEdit;
    private readonly Action _refresh;
    private readonly Func<Task> _importMedia;
    private readonly bool _infinite;
    private double _zoom = 1;

    public NotesFreeformPageControl(
        NotesWorkspaceViewModel viewModel,
        bool infinite,
        Action<NotesBlock> beginEdit,
        Action<NotesBlock, string> endEdit,
        Action refresh,
        Func<Task> importMedia)
    {
        _viewModel = viewModel;
        _page = viewModel.CurrentPage ?? throw new InvalidOperationException("No Notes page is selected.");
        _infinite = infinite;
        _beginEdit = beginEdit;
        _endEdit = endEdit;
        _refresh = refresh;
        _importMedia = importMedia;
        Build();
    }

    private void Build()
    {
        var zoom = new Slider { Minimum = 0.25, Maximum = 3, Value = 1, Width = 180 };
        zoom.ValueChanged += (_, _) =>
        {
            _zoom = zoom.Value;
            _surface.RenderTransform = new ScaleTransform(_zoom, _zoom);
            _status.Text = StatusText();
        };
        var reset = new Button { Content = "Reset view" };
        reset.Click += (_, _) =>
        {
            zoom.Value = 1;
            _surface.RenderTransform = Transform.Identity;
        };
        var addText = new Button { Content = "+ Text" };
        addText.Click += (_, _) =>
        {
            _viewModel.AddParagraphCommand.Execute(null);
            var block = _viewModel.CurrentPage?.Blocks.OrderBy(value => value.Order).LastOrDefault();
            if (block is not null)
            {
                block.Metadata["freeform-x"] = "80";
                block.Metadata["freeform-y"] = "80";
                block.Metadata["freeform-width"] = "420";
                block.Metadata["freeform-height"] = "220";
            }
            _refresh();
        };
        var addCanvas = new Button { Content = "+ Ink canvas" };
        addCanvas.Click += (_, _) =>
        {
            _viewModel.AddCanvasCommand.Execute(null);
            var block = _viewModel.CurrentPage?.Blocks.OrderBy(value => value.Order).LastOrDefault();
            if (block is not null)
            {
                block.Metadata["freeform-x"] = "120";
                block.Metadata["freeform-y"] = "120";
                block.Metadata["freeform-width"] = "700";
                block.Metadata["freeform-height"] = "520";
            }
            _refresh();
        };
        var toolbar = new WrapPanel { Children = { _status, zoom, reset, addText, addCanvas } };
        _surface.Width = _infinite ? 12_000 : Math.Max(1200, _page.CanvasWidth);
        _surface.Height = _infinite ? 12_000 : Math.Max(900, _page.CanvasHeight);
        _surface.Background = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255));
        _surface.RenderTransformOrigin = RelativePoint.TopLeft;
        for (var index = 0; index < _page.Blocks.Count; index++) AddBlock(_page.Blocks[index], index);
        _status.Text = StatusText();
        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 7,
            Children =
            {
                toolbar,
                WithRow(new ScrollViewer
                {
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    Content = _surface
                }, 1)
            }
        };
    }

    private void AddBlock(NotesBlock block, int index)
    {
        var x = Read(block, "freeform-x", 50 + index % 4 * 440);
        var y = Read(block, "freeform-y", 50 + index / 4 * 300);
        var width = Math.Clamp(Read(block, "freeform-width", block.Kind == NotesBlockKind.Canvas ? 720 : 400), 180, 1800);
        var height = Math.Clamp(Read(block, "freeform-height", block.Kind == NotesBlockKind.Canvas ? 540 : 240), 100, 1400);
        var body = NotesBlockEditorFactory.Build(_viewModel, block, _beginEdit, _endEdit, _refresh, _importMedia);
        var header = new Border
        {
            Height = 28,
            Background = new SolidColorBrush(Color.FromArgb(55, 47, 128, 237)),
            Padding = new Thickness(8, 4),
            Child = new TextBlock
            {
                Text = block.Kind + " · drag",
                FontSize = 9,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        var resize = new Border
        {
            Width = 20,
            Height = 20,
            Background = new SolidColorBrush(Color.FromArgb(110, 47, 128, 237)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = new TextBlock { Text = "↘", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 10 }
        };
        var frame = new Border
        {
            Width = width,
            Height = height,
            Background = new SolidColorBrush(Color.FromArgb(244, 27, 27, 31)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*"),
                Children =
                {
                    header,
                    WithRow(new ScrollViewer
                    {
                        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        Content = body
                    }, 1),
                    WithRow(resize, 1)
                }
            }
        };
        Canvas.SetLeft(frame, x);
        Canvas.SetTop(frame, y);
        AttachDrag(header, frame, block);
        AttachResize(resize, frame, block);
        _surface.Children.Add(frame);
    }

    private void AttachDrag(Control handle, Control frame, NotesBlock block)
    {
        var dragging = false;
        Point start = default;
        double originalX = 0;
        double originalY = 0;
        handle.PointerPressed += (_, args) =>
        {
            if (!args.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;
            dragging = true;
            start = args.GetPosition(_surface);
            originalX = Canvas.GetLeft(frame);
            originalY = Canvas.GetTop(frame);
            _beginEdit(block);
            args.Pointer.Capture(handle);
            args.Handled = true;
        };
        handle.PointerMoved += (_, args) =>
        {
            if (!dragging) return;
            var current = args.GetPosition(_surface);
            Canvas.SetLeft(frame, Math.Max(0, originalX + current.X - start.X));
            Canvas.SetTop(frame, Math.Max(0, originalY + current.Y - start.Y));
            args.Handled = true;
        };
        handle.PointerReleased += (_, args) =>
        {
            if (!dragging) return;
            dragging = false;
            args.Pointer.Capture(null);
            Write(block, "freeform-x", Canvas.GetLeft(frame));
            Write(block, "freeform-y", Canvas.GetTop(frame));
            _endEdit(block, "Moved freeform block");
            args.Handled = true;
        };
        handle.PointerCaptureLost += (_, _) =>
        {
            if (!dragging) return;
            dragging = false;
            Write(block, "freeform-x", Canvas.GetLeft(frame));
            Write(block, "freeform-y", Canvas.GetTop(frame));
            _endEdit(block, "Moved freeform block");
        };
    }

    private void AttachResize(Control handle, Control frame, NotesBlock block)
    {
        var resizing = false;
        Point start = default;
        double originalWidth = 0;
        double originalHeight = 0;
        handle.PointerPressed += (_, args) =>
        {
            if (!args.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;
            resizing = true;
            start = args.GetPosition(_surface);
            originalWidth = frame.Bounds.Width;
            originalHeight = frame.Bounds.Height;
            _beginEdit(block);
            args.Pointer.Capture(handle);
            args.Handled = true;
        };
        handle.PointerMoved += (_, args) =>
        {
            if (!resizing) return;
            var current = args.GetPosition(_surface);
            frame.Width = Math.Clamp(originalWidth + current.X - start.X, 180, 1800);
            frame.Height = Math.Clamp(originalHeight + current.Y - start.Y, 100, 1400);
            args.Handled = true;
        };
        handle.PointerReleased += (_, args) =>
        {
            if (!resizing) return;
            resizing = false;
            args.Pointer.Capture(null);
            Write(block, "freeform-width", frame.Width);
            Write(block, "freeform-height", frame.Height);
            _endEdit(block, "Resized freeform block");
            args.Handled = true;
        };
        handle.PointerCaptureLost += (_, _) =>
        {
            if (!resizing) return;
            resizing = false;
            Write(block, "freeform-width", frame.Width);
            Write(block, "freeform-height", frame.Height);
            _endEdit(block, "Resized freeform block");
        };
    }

    private string StatusText() => $"{(_infinite ? "Infinite" : "Freeform")} canvas · {_page.Blocks.Count} mixed blocks · zoom {_zoom:P0}";

    private static double Read(NotesBlock block, string key, double fallback) =>
        block.Metadata.TryGetValue(key, out var text)
        && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static void Write(NotesBlock block, string key, double value) =>
        block.Metadata[key] = value.ToString("R", CultureInfo.InvariantCulture);

    private static T WithRow<T>(T control, int row) where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }
}
