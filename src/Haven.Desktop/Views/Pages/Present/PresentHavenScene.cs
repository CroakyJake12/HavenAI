using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Present;

internal sealed partial class PresentHavenScene : IDisposable
{
    private bool _suppressChanges;
    private string _deckTitle = string.Empty;
    private string _slideTitle = string.Empty;
    private string _bodyText = string.Empty;
    private string _notesText = string.Empty;
    private bool _disposed;

    public PresentHavenScene()
    {
        Root = new Page { Name = "Present.Root", Layout = HavenLayout.Grid, Columns = "1fr", Rows = "58px 48px 48px 1fr 34px" };
        Root.SetValue(HavenProperties.Padding, HavenThickness.Parse("10px 14px"));
        Root.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        Root.SetValue(HavenProperties.Background, "Surface");
        Root.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Responsive, true);
        Root.SetValue(HavenProperties.Overflow, HavenOverflow.Clip);

        Header = new Container { Name = "Present.Header", Layout = HavenLayout.Grid, Columns = "1fr Auto", Rows = "Auto" };
        Header.SetValue(HavenProperties.Row, 0);
        Header.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Header.SetValue(HavenProperties.Height, HavenLength.Px(58));
        Header.SetValue(HavenProperties.Responsive, true);
        Header.SetValue(HavenProperties.Gap, HavenLength.Px(12));
        DeckTitleInput = new Input { Name = "Present.Deck.Title", Placeholder = "Untitled presentation" };
        DeckTitleInput.Accessibility.AccessibleName = "Presentation title";
        DeckTitleInput.SetValue(HavenProperties.Column, 0);
        DeckTitleInput.SetValue(HavenProperties.FontSize, 24d);
        DeckTitleInput.SetValue(HavenProperties.FontWeight, 700);
        DeckTitleInput.SetValue(HavenProperties.MinHeight, HavenLength.Px(52));
        Header.Add(DeckTitleInput);
        PositionText = new HavenText { Name = "Present.Position", Level = TextLevel.Caption };
        PositionText.SetValue(HavenProperties.Column, 1);
        PositionText.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        PositionText.SetValue(HavenProperties.Foreground, "TextSecondary");
        Header.Add(PositionText); Root.Add(Header);

        DeckToolbar = NewToolbar("Present.Deck.Toolbar", 1);
        PreviousDeckButton = NewButton("Present.Deck.Previous", "Previous deck");
        NextDeckButton = NewButton("Present.Deck.Next", "Next deck");
        NewDeckButton = NewButton("Present.Deck.New", "New deck");
        SaveButton = NewButton("Present.Deck.Save", "Save");
        ExportButton = NewButton("Present.Deck.Export", "Export .pptx");
        DeckToolbar.Add(PreviousDeckButton); DeckToolbar.Add(NextDeckButton); DeckToolbar.Add(NewDeckButton); DeckToolbar.Add(SaveButton); DeckToolbar.Add(ExportButton);
        Root.Add(DeckToolbar);

        SlideToolbar = NewToolbar("Present.Slide.Toolbar", 2);
        PreviousSlideButton = NewButton("Present.Slide.Previous", "Previous slide");
        NextSlideButton = NewButton("Present.Slide.Next", "Next slide");
        AddSlideButton = NewButton("Present.Slide.Add", "+ Slide");
        DeleteSlideButton = NewButton("Present.Slide.Delete", "Delete");
        SlideToolbar.Add(PreviousSlideButton); SlideToolbar.Add(NextSlideButton); SlideToolbar.Add(AddSlideButton); SlideToolbar.Add(DeleteSlideButton);
        Root.Add(SlideToolbar);

        Workspace = new Container { Name = "Present.Workspace", Layout = HavenLayout.Grid, Columns = "190px 1fr 270px", Rows = "1fr" };
        Workspace.SetValue(HavenProperties.Row, 3);
        Workspace.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Workspace.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        Workspace.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        Workspace.SetValue(HavenProperties.Responsive, true);
        Workspace.SetValue(HavenProperties.Overflow, HavenOverflow.Clip);
        Workspace.SetValue(HavenProperties.PointerEvents, HavenPointerEvents.ChildrenOnly);

        SlidePane = new Container { Name = "Present.Slides.Pane", Layout = HavenLayout.Vertical };
        SlidePane.SetValue(HavenProperties.Column, 0);
        SlidePane.SetValue(HavenProperties.Width, HavenLength.Px(190));
        SlidePane.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        SlidePane.SetValue(HavenProperties.Responsive, true);
        SlidePane.SetValue(HavenProperties.Background, "SurfaceRaised");
        SlidePane.SetValue(HavenProperties.BorderColor, "Border");
        SlidePane.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        SlidePane.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(14)));
        SlidePane.SetValue(HavenProperties.Padding, HavenThickness.Parse("10px"));
        SlidePane.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        SlidePane.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        SlidePane.Add(new HavenText("Slides") { Level = TextLevel.H3 });
        Workspace.Add(SlidePane);

        EditorHost = new Container { Name = "Present.Editor.Host", Layout = HavenLayout.Grid, Columns = "1fr", Rows = "1fr Auto" };
        EditorHost.SetValue(HavenProperties.Column, 1);
        EditorHost.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        EditorHost.SetValue(HavenProperties.MinWidth, HavenLength.Px(480));
        EditorHost.SetValue(HavenProperties.Responsive, true);
        EditorHost.SetValue(HavenProperties.Background, "SurfaceSubtle");
        EditorHost.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(14)));
        EditorHost.SetValue(HavenProperties.Padding, HavenThickness.Parse("12px"));
        EditorHost.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        EditorHost.SetValue(HavenProperties.Overflow, HavenOverflow.Clip);
        EditorHost.SetValue(HavenProperties.PointerEvents, HavenPointerEvents.ChildrenOnly);

        SlideCanvas = new PresentSlideCanvas();
        SlideCanvas.SetValue(HavenProperties.Row, 0);
        SlideCanvas.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        SlideCanvas.SetValue(HavenProperties.MinHeight, HavenLength.Px(430));
        EditorHost.Add(SlideCanvas);

        NotesHost = new Container { Name = "Present.Notes.Host", Layout = HavenLayout.Vertical };
        NotesHost.SetValue(HavenProperties.Row, 1);
        NotesHost.SetValue(HavenProperties.Background, "SurfaceRaised");
        NotesHost.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(10)));
        NotesHost.SetValue(HavenProperties.Padding, HavenThickness.Parse("8px 10px"));
        NotesHost.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        NotesLabel = Caption("Speaker notes"); NotesHost.Add(NotesLabel);
        NotesInput = new Input { Name = "Present.Slide.Notes", Placeholder = "Add speaker notes...", Multiline = true };
        NotesInput.Accessibility.AccessibleName = "Speaker notes";
        NotesInput.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        NotesInput.SetValue(HavenProperties.MinHeight, HavenLength.Px(72));
        NotesInput.SetValue(HavenProperties.MaxHeight, HavenLength.Px(118));
        NotesInput.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(8)));
        NotesHost.Add(NotesInput);
        EditorHost.Add(NotesHost);
        Workspace.Add(EditorHost);

        InspectorPane = new Container { Name = "Present.Inspector.Pane", Layout = HavenLayout.Vertical };
        InspectorPane.SetValue(HavenProperties.Column, 2);
        InspectorPane.SetValue(HavenProperties.Width, HavenLength.Px(270));
        InspectorPane.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        InspectorPane.SetValue(HavenProperties.Responsive, true);
        InspectorPane.SetValue(HavenProperties.Background, "SurfaceRaised");
        InspectorPane.SetValue(HavenProperties.BorderColor, "Border");
        InspectorPane.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        InspectorPane.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(14)));
        InspectorPane.SetValue(HavenProperties.Padding, HavenThickness.Parse("10px"));
        InspectorPane.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        InspectorPane.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        InspectorPane.Add(new HavenText("Format") { Level = TextLevel.H3 });
        RichContentText = new HavenText { Name = "Present.Slide.RichContent", Level = TextLevel.Caption };
        RichContentText.SetValue(HavenProperties.Foreground, "TextSecondary"); InspectorPane.Add(RichContentText);
        Workspace.Add(InspectorPane);

        // Compatibility fields remain model mirrors for existing callers/tests but no longer consume editor space.
        SlideLabel = Caption("Slide");
        SlideTitleInput = new Input { Name = "Present.Slide.Title", Placeholder = "Slide title" };
        SlideTitleInput.Accessibility.AccessibleName = "Slide title"; SlideTitleInput.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        BodyLabel = Caption("Slide content");
        BodyInput = new Input { Name = "Present.Slide.Body", Placeholder = "Slide content", Multiline = true };
        BodyInput.Accessibility.AccessibleName = "Slide content"; BodyInput.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);

        Root.Add(Workspace);

        StatusText = new HavenText("Opening local presentations...") { Name = "Present.Status", Level = TextLevel.Caption };
        StatusText.SetValue(HavenProperties.Row, 4); StatusText.SetValue(HavenProperties.Foreground, "TextSecondary"); Root.Add(StatusText);

        DeckTitleInput.Invalidated += OnDeckTitleInvalidated;
        SlideTitleInput.Invalidated += OnSlideTitleInvalidated;
        BodyInput.Invalidated += OnBodyInvalidated;
        NotesInput.Invalidated += OnNotesInvalidated;
        PreviousDeckButton.Invoked += (_, _) => PreviousDeckRequested?.Invoke(this, EventArgs.Empty);
        NextDeckButton.Invoked += (_, _) => NextDeckRequested?.Invoke(this, EventArgs.Empty);
        NewDeckButton.Invoked += (_, _) => NewDeckRequested?.Invoke(this, EventArgs.Empty);
        SaveButton.Invoked += (_, _) => SaveRequested?.Invoke(this, EventArgs.Empty);
        ExportButton.Invoked += (_, _) => ExportRequested?.Invoke(this, EventArgs.Empty);
        PreviousSlideButton.Invoked += (_, _) => PreviousSlideRequested?.Invoke(this, EventArgs.Empty);
        NextSlideButton.Invoked += (_, _) => NextSlideRequested?.Invoke(this, EventArgs.Empty);
        AddSlideButton.Invoked += (_, _) => AddSlideRequested?.Invoke(this, EventArgs.Empty);
        DeleteSlideButton.Invoked += (_, _) => DeleteSlideRequested?.Invoke(this, EventArgs.Empty);
        BuildPhase2Controls();
        BuildPresenterControls();
    }

    public event EventHandler? PreviousDeckRequested; public event EventHandler? NextDeckRequested; public event EventHandler? NewDeckRequested;
    public event EventHandler? SaveRequested; public event EventHandler? ExportRequested;
    public event EventHandler? PreviousSlideRequested; public event EventHandler? NextSlideRequested; public event EventHandler? AddSlideRequested; public event EventHandler? DeleteSlideRequested;
    public event Action<string>? DeckTitleChanged; public event Action<string>? SlideTitleChanged; public event Action<string>? BodyChanged; public event Action<string>? NotesChanged;

    public Page Root { get; } public Container Header { get; } public Input DeckTitleInput { get; } public HavenText PositionText { get; }
    public Container DeckToolbar { get; } public Container SlideToolbar { get; } public Container Workspace { get; } public Container SlidePane { get; } public Container EditorHost { get; } public Container NotesHost { get; } public Container InspectorPane { get; }
    internal PresentSlideCanvas SlideCanvas { get; }
    public HavenButton PreviousDeckButton { get; } public HavenButton NextDeckButton { get; } public HavenButton NewDeckButton { get; } public HavenButton SaveButton { get; } public HavenButton ExportButton { get; }
    public HavenButton PreviousSlideButton { get; } public HavenButton NextSlideButton { get; } public HavenButton AddSlideButton { get; } public HavenButton DeleteSlideButton { get; }
    public HavenText SlideLabel { get; } public Input SlideTitleInput { get; } public HavenText BodyLabel { get; } public Input BodyInput { get; } public HavenText NotesLabel { get; } public Input NotesInput { get; } public HavenText RichContentText { get; } public HavenText StatusText { get; }

    public void SetDocument(PresentDocument document, int deckIndex, int deckCount, int slideIndex)
    {
        ArgumentNullException.ThrowIfNull(document); document.Normalize();
        slideIndex = Math.Clamp(slideIndex, 0, document.Slides.Count - 1);
        var slide = document.Slides[slideIndex]; var body = slide.GetOrCreateBodyText();
        _suppressChanges = true;
        try
        {
            _deckTitle = document.Title; _slideTitle = slide.Title; _bodyText = body.Text; _notesText = slide.SpeakerNotes;
            DeckTitleInput.Text = _deckTitle; SlideTitleInput.Text = _slideTitle; BodyInput.Text = _bodyText; NotesInput.Text = _notesText;
            PositionText.Content = $"Deck {deckIndex + 1} of {Math.Max(deckCount, 1)} Ã‚Â· Slide {slideIndex + 1} of {document.Slides.Count} Ã‚Â· v{document.Version}";
            var rich = slide.Elements.Count(element => element.Kind != PresentElementKind.Text);
            RichContentText.Content = rich == 0 ? "All slide content in this view is directly editable." : $"{rich} richer Haven element{(rich == 1 ? string.Empty : "s")} preserved in this slide.";
        }
        finally { _suppressChanges = false; }
    }

    public void SetStatus(string text) => StatusText.Content = text ?? string.Empty;
    public void SetBusy(bool busy)
    {
        var enabled = !busy;
        foreach (var button in new[] { PreviousDeckButton, NextDeckButton, NewDeckButton, SaveButton, ExportButton, PreviousSlideButton, NextSlideButton, AddSlideButton, DeleteSlideButton }) button.SetValue(HavenProperties.Enabled, enabled);
        foreach (var input in new[] { DeckTitleInput, SlideTitleInput, BodyInput, NotesInput }) input.SetValue(HavenProperties.Enabled, enabled);
    }

    private void OnDeckTitleInvalidated(object? sender, EventArgs e) { if (_suppressChanges || DeckTitleInput.Text == _deckTitle) return; _deckTitle = DeckTitleInput.Text; DeckTitleChanged?.Invoke(_deckTitle); }
    private void OnSlideTitleInvalidated(object? sender, EventArgs e) { if (_suppressChanges || SlideTitleInput.Text == _slideTitle) return; _slideTitle = SlideTitleInput.Text; SlideTitleChanged?.Invoke(_slideTitle); }
    private void OnBodyInvalidated(object? sender, EventArgs e) { if (_suppressChanges || BodyInput.Text == _bodyText) return; _bodyText = BodyInput.Text; BodyChanged?.Invoke(_bodyText); }
    private void OnNotesInvalidated(object? sender, EventArgs e) { if (_suppressChanges || NotesInput.Text == _notesText) return; _notesText = NotesInput.Text; NotesChanged?.Invoke(_notesText); }

    private static Container NewToolbar(string name, int row)
    {
        var toolbar = new Container { Name = name, Layout = HavenLayout.Horizontal };
        toolbar.SetValue(HavenProperties.Row, row);
        toolbar.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        toolbar.SetValue(HavenProperties.Responsive, true);
        toolbar.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        toolbar.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        return toolbar;
    }
    private static HavenButton NewButton(string name, string label)
    {
        var button = new HavenButton { Name = name, Variant = ButtonVariant.Tertiary, Content = label };
        button.Accessibility.AccessibleName = label; button.SetValue(HavenProperties.MinHeight, HavenLength.Px(40)); return button;
    }
    private static HavenText Caption(string text)
    {
        var label = new HavenText(text) { Level = TextLevel.Caption }; label.SetValue(HavenProperties.Foreground, "TextSecondary"); return label;
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        DeckTitleInput.Invalidated -= OnDeckTitleInvalidated; SlideTitleInput.Invalidated -= OnSlideTitleInvalidated; BodyInput.Invalidated -= OnBodyInvalidated; NotesInput.Invalidated -= OnNotesInvalidated;
    }
}
