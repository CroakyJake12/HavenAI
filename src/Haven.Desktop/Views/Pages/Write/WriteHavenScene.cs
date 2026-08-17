using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Write;

/// <summary>Haven-owned visible scene for the recovered Write document engine.</summary>
internal sealed class WriteHavenScene : IDisposable
{
    private readonly Dictionary<Guid, Input> _blockInputs = [];
    private readonly List<(Input Input, EventHandler Handler)> _blockSubscriptions = [];
    private bool _suppressChanges;
    private string _lastTitle = string.Empty;
    private bool _disposed;

    public WriteHavenScene()
    {
        Root = new Page
        {
            Name = "Write.Root",
            Layout = HavenLayout.Grid,
            Columns = "1fr",
            Rows = "Auto Auto 1fr Auto"
        };
        Root.SetValue(HavenProperties.Padding, HavenThickness.Parse("18px 26px"));
        Root.SetValue(HavenProperties.Gap, HavenLength.Px(12));
        Root.SetValue(HavenProperties.Background, "Surface");
        Root.SetValue(HavenProperties.Overflow, HavenOverflow.Clip);

        Header = new Container
        {
            Name = "Write.Header",
            Layout = HavenLayout.Grid,
            Columns = "1fr Auto",
            Rows = "Auto"
        };
        Header.SetValue(HavenProperties.Row, 0);
        Header.SetValue(HavenProperties.Gap, HavenLength.Px(12));

        TitleInput = new Input
        {
            Name = "Write.Document.Title",
            Placeholder = "Untitled document"
        };
        TitleInput.Accessibility.AccessibleName = "Document title";
        TitleInput.SetValue(HavenProperties.Column, 0);
        TitleInput.SetValue(HavenProperties.FontSize, 24d);
        TitleInput.SetValue(HavenProperties.FontWeight, 700);
        TitleInput.SetValue(HavenProperties.MinHeight, HavenLength.Px(52));
        Header.Add(TitleInput);

        DocumentPositionText = new HavenText
        {
            Name = "Write.Document.Position",
            Level = TextLevel.Caption
        };
        DocumentPositionText.SetValue(HavenProperties.Column, 1);
        DocumentPositionText.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        DocumentPositionText.SetValue(HavenProperties.Foreground, "TextSecondary");
        Header.Add(DocumentPositionText);
        Root.Add(Header);

        Toolbar = new Container { Name = "Write.Toolbar", Layout = HavenLayout.Horizontal };
        Toolbar.SetValue(HavenProperties.Row, 1);
        Toolbar.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        Toolbar.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        PreviousButton = CreateButton("Write.Toolbar.Previous", "Previous");
        NextButton = CreateButton("Write.Toolbar.Next", "Next");
        NewButton = CreateButton("Write.Toolbar.New", "New");
        ImportButton = CreateButton("Write.Toolbar.Import", "Import");
        ExportButton = CreateButton("Write.Toolbar.Export", "Export");
        SaveButton = CreateButton("Write.Toolbar.Save", "Save");
        Toolbar.Add(PreviousButton);
        Toolbar.Add(NextButton);
        Toolbar.Add(NewButton);
        Toolbar.Add(ImportButton);
        Toolbar.Add(ExportButton);
        Toolbar.Add(SaveButton);
        Root.Add(Toolbar);

        DocumentHost = new Container { Name = "Write.Document.Host", Layout = HavenLayout.Vertical };
        DocumentHost.SetValue(HavenProperties.Row, 2);
        DocumentHost.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        DocumentHost.SetValue(HavenProperties.MaxWidth, HavenLength.Px(980));
        DocumentHost.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        DocumentHost.SetValue(HavenProperties.Background, "SurfaceRaised");
        DocumentHost.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(18)));
        DocumentHost.SetValue(HavenProperties.Padding, HavenThickness.Parse("22px"));
        DocumentHost.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);

        BlockHost = new Container { Name = "Write.Document.Blocks", Layout = HavenLayout.Vertical };
        BlockHost.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        BlockHost.SetValue(HavenProperties.Gap, HavenLength.Px(14));
        DocumentHost.Add(BlockHost);
        Root.Add(DocumentHost);

        StatusText = new HavenText("Opening local documents…")
        {
            Name = "Write.Status",
            Level = TextLevel.Caption
        };
        StatusText.SetValue(HavenProperties.Row, 3);
        StatusText.SetValue(HavenProperties.Foreground, "TextSecondary");
        Root.Add(StatusText);

        TitleInput.Invalidated += OnTitleInvalidated;
        NewButton.Invoked += (_, _) => NewRequested?.Invoke(this, EventArgs.Empty);
        ImportButton.Invoked += (_, _) => ImportRequested?.Invoke(this, EventArgs.Empty);
        ExportButton.Invoked += (_, _) => ExportRequested?.Invoke(this, EventArgs.Empty);
        SaveButton.Invoked += (_, _) => SaveRequested?.Invoke(this, EventArgs.Empty);
        PreviousButton.Invoked += (_, _) => PreviousRequested?.Invoke(this, EventArgs.Empty);
        NextButton.Invoked += (_, _) => NextRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? NewRequested;
    public event EventHandler? ImportRequested;
    public event EventHandler? ExportRequested;
    public event EventHandler? SaveRequested;
    public event EventHandler? PreviousRequested;
    public event EventHandler? NextRequested;
    public event Action<string>? TitleChanged;
    public event Action<WriteBlockTextChangedEventArgs>? BlockTextChanged;

    public Page Root { get; }
    public Container Header { get; }
    public Input TitleInput { get; }
    public HavenText DocumentPositionText { get; }
    public Container Toolbar { get; }
    public HavenButton PreviousButton { get; }
    public HavenButton NextButton { get; }
    public HavenButton NewButton { get; }
    public HavenButton ImportButton { get; }
    public HavenButton ExportButton { get; }
    public HavenButton SaveButton { get; }
    public Container DocumentHost { get; }
    public Container BlockHost { get; }
    public HavenText StatusText { get; }
    public IReadOnlyDictionary<Guid, Input> BlockInputs => _blockInputs;

    public void SetDocument(NotesDocument document, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(document);
        _suppressChanges = true;
        try
        {
            _lastTitle = document.Title;
            TitleInput.Text = document.Title;
            DocumentPositionText.Content = count <= 0
                ? "Local document"
                : $"{index + 1} of {count} · v{document.Version}";
            ClearBlocks();

            foreach (var section in document.Sections)
            {
                foreach (var page in section.Pages.OrderBy(page => page.Order))
                {
                    var context = new HavenText($"{section.Title} · {page.Title}") { Level = TextLevel.Caption };
                    context.SetValue(HavenProperties.Foreground, "TextSecondary");
                    BlockHost.Add(context);

                    foreach (var block in page.Blocks.OrderBy(block => block.Order))
                        AddBlock(block);
                }
            }

            if (_blockInputs.Count == 0)
            {
                var empty = new HavenText(
                    "This document has no directly editable text blocks yet. Its rich content is preserved.")
                {
                    Level = TextLevel.Paragraph
                };
                empty.SetValue(HavenProperties.Foreground, "TextSecondary");
                BlockHost.Add(empty);
            }
        }
        finally
        {
            _suppressChanges = false;
        }
    }

    public void SetStatus(string status) => StatusText.Content = status ?? string.Empty;

    public void SetTitleFromModel(string title)
    {
        _suppressChanges = true;
        try
        {
            _lastTitle = title ?? string.Empty;
            TitleInput.Text = _lastTitle;
        }
        finally
        {
            _suppressChanges = false;
        }
    }

    public void SetBusy(bool busy)
    {
        var enabled = !busy;
        foreach (var button in new[] { PreviousButton, NextButton, NewButton, ImportButton, ExportButton, SaveButton })
            button.SetValue(HavenProperties.Enabled, enabled);
        TitleInput.SetValue(HavenProperties.Enabled, enabled);
        foreach (var input in _blockInputs.Values)
            input.SetValue(HavenProperties.Enabled, enabled);
    }

    private void AddBlock(NotesBlock block)
    {
        if (block.Kind is NotesBlockKind.Paragraph
            or NotesBlockKind.Heading
            or NotesBlockKind.Quote
            or NotesBlockKind.Code)
        {
            AddEditableBlock(block, EditableText(block), isList: false);
            return;
        }

        if (block.Kind == NotesBlockKind.List && block.List is not null)
        {
            AddEditableBlock(block, string.Join("\n", block.List.Items.Select(item => item.Text)), isList: true);
            return;
        }

        var preserved = new Container
        {
            Name = $"Write.Block.{block.Id:N}.Preserved",
            Layout = HavenLayout.Vertical
        };
        preserved.SetValue(HavenProperties.Padding, HavenThickness.Parse("10px 12px"));
        preserved.SetValue(HavenProperties.Background, "Surface");
        preserved.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(10)));
        preserved.Add(new HavenText($"{block.Kind} block · preserved in this document") { Level = TextLevel.Paragraph });

        var detail = new HavenText(PreservedBlockDetail(block)) { Level = TextLevel.Caption };
        detail.SetValue(HavenProperties.Foreground, "TextSecondary");
        preserved.Add(detail);
        BlockHost.Add(preserved);
    }

    private void AddEditableBlock(NotesBlock block, string text, bool isList)
    {
        var host = new Container
        {
            Name = $"Write.Block.{block.Id:N}",
            Layout = HavenLayout.Vertical
        };
        host.SetValue(HavenProperties.Gap, HavenLength.Px(6));

        var label = new HavenText(isList ? "List" : block.Kind.ToString()) { Level = TextLevel.Caption };
        label.SetValue(HavenProperties.Foreground, "TextSecondary");
        host.Add(label);

        var input = new Input
        {
            Name = $"Write.Block.{block.Id:N}.Input",
            Placeholder = block.Kind == NotesBlockKind.Heading ? "Heading" : "Write here…",
            Multiline = block.Kind != NotesBlockKind.Heading,
            Text = text ?? string.Empty
        };
        input.Accessibility.AccessibleName = isList ? "List block" : $"{block.Kind} block";
        input.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        input.SetValue(
            HavenProperties.MinHeight,
            HavenLength.Px(block.Kind == NotesBlockKind.Heading ? 50 : 86));
        input.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12)));
        if (block.Kind == NotesBlockKind.Heading)
        {
            input.SetValue(HavenProperties.FontSize, 21d);
            input.SetValue(HavenProperties.FontWeight, 700);
        }

        var last = input.Text;
        EventHandler handler = (_, _) =>
        {
            if (_suppressChanges || input.Text == last)
                return;

            last = input.Text;
            BlockTextChanged?.Invoke(new WriteBlockTextChangedEventArgs(block.Id, input.Text, isList));
        };
        input.Invalidated += handler;
        _blockSubscriptions.Add((input, handler));
        _blockInputs[block.Id] = input;
        host.Add(input);
        BlockHost.Add(host);
    }

    private static string EditableText(NotesBlock block) =>
        block.Runs.Count > 0
            ? string.Concat(block.Runs.Select(run => run.Text))
            : block.PlainText;

    private static string PreservedBlockDetail(NotesBlock block) => block.Kind switch
    {
        NotesBlockKind.Table => $"{block.Table?.Rows.Count ?? 0} table rows",
        NotesBlockKind.Image or NotesBlockKind.Audio or NotesBlockKind.Video
            => block.Media?.OriginalName ?? "Media attachment",
        NotesBlockKind.Equation => block.Equation?.Source ?? "Equation",
        NotesBlockKind.HtmlWidget => "Interactive HTML content",
        NotesBlockKind.Canvas => $"{block.Canvas?.Objects.Count ?? 0} canvas objects",
        NotesBlockKind.Flashcard => block.Flashcard?.Front ?? "Flashcard",
        NotesBlockKind.Divider => "Divider",
        _ => block.Kind.ToString()
    };

    private void OnTitleInvalidated(object? sender, EventArgs e)
    {
        if (_suppressChanges || TitleInput.Text == _lastTitle)
            return;

        _lastTitle = TitleInput.Text;
        TitleChanged?.Invoke(_lastTitle);
    }

    private void ClearBlocks()
    {
        foreach (var (input, handler) in _blockSubscriptions)
            input.Invalidated -= handler;

        _blockSubscriptions.Clear();
        _blockInputs.Clear();
        foreach (var child in BlockHost.Children.ToArray())
            BlockHost.Remove(child);
    }

    private static HavenButton CreateButton(string name, string label)
    {
        var button = new HavenButton
        {
            Name = name,
            Variant = ButtonVariant.Tertiary,
            Content = label
        };
        button.Accessibility.AccessibleName = label;
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(40));
        return button;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        TitleInput.Invalidated -= OnTitleInvalidated;
        ClearBlocks();
    }
}

internal sealed class WriteBlockTextChangedEventArgs(Guid blockId, string text, bool isList)
{
    public Guid BlockId { get; } = blockId;
    public string Text { get; } = text;
    public bool IsList { get; } = isList;
}
