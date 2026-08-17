using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Present;

internal sealed class PresentHavenScene : IDisposable
{
    private bool _suppressChanges;
    private string _deckTitle = string.Empty;
    private string _slideTitle = string.Empty;
    private string _bodyText = string.Empty;
    private string _notesText = string.Empty;
    private bool _disposed;

    public PresentHavenScene()
    {
        Root = new Page { Name = "Present.Root", Layout = HavenLayout.Grid, Columns = "1fr", Rows = "Auto Auto Auto 1fr Auto" };
        Root.SetValue(HavenProperties.Padding, HavenThickness.Parse("18px 26px"));
        Root.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        Root.SetValue(HavenProperties.Background, "Surface");
        Root.SetValue(HavenProperties.Overflow, HavenOverflow.Clip);

        Header = new Container { Name = "Present.Header", Layout = HavenLayout.Grid, Columns = "1fr Auto", Rows = "Auto" };
        Header.SetValue(HavenProperties.Row, 0);
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
        AddSlideButton = NewButton("Present.Slide.Add", "Add slide");
        DeleteSlideButton = NewButton("Present.Slide.Delete", "Delete slide");
        SlideToolbar.Add(PreviousSlideButton); SlideToolbar.Add(NextSlideButton); SlideToolbar.Add(AddSlideButton); SlideToolbar.Add(DeleteSlideButton);
        Root.Add(SlideToolbar);

        EditorHost = new Container { Name = "Present.Editor.Host", Layout = HavenLayout.Vertical };
        EditorHost.SetValue(HavenProperties.Row, 3);
        EditorHost.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        EditorHost.SetValue(HavenProperties.MaxWidth, HavenLength.Px(1120));
        EditorHost.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        EditorHost.SetValue(HavenProperties.Background, "SurfaceRaised");
        EditorHost.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(18)));
        EditorHost.SetValue(HavenProperties.Padding, HavenThickness.Parse("20px"));
        EditorHost.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        EditorHost.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);

        SlideLabel = Caption("Slide"); EditorHost.Add(SlideLabel);
        SlideTitleInput = new Input { Name = "Present.Slide.Title", Placeholder = "Slide title" };
        SlideTitleInput.Accessibility.AccessibleName = "Slide title";
        SlideTitleInput.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        SlideTitleInput.SetValue(HavenProperties.MinHeight, HavenLength.Px(50));
        SlideTitleInput.SetValue(HavenProperties.FontSize, 21d);
        SlideTitleInput.SetValue(HavenProperties.FontWeight, 700);
        EditorHost.Add(SlideTitleInput);

        BodyLabel = Caption("Slide content"); EditorHost.Add(BodyLabel);
        BodyInput = new Input { Name = "Present.Slide.Body", Placeholder = "Add the main content for this slide…", Multiline = true };
        BodyInput.Accessibility.AccessibleName = "Slide content";
        BodyInput.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        BodyInput.SetValue(HavenProperties.MinHeight, HavenLength.Px(230));
        BodyInput.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12)));
        EditorHost.Add(BodyInput);

        NotesLabel = Caption("Speaker notes"); EditorHost.Add(NotesLabel);
        NotesInput = new Input { Name = "Present.Slide.Notes", Placeholder = "Speaker notes…", Multiline = true };
        NotesInput.Accessibility.AccessibleName = "Speaker notes";
        NotesInput.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        NotesInput.SetValue(HavenProperties.MinHeight, HavenLength.Px(120));
        NotesInput.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12)));
        EditorHost.Add(NotesInput);
        RichContentText = new HavenText { Name = "Present.Slide.RichContent", Level = TextLevel.Caption };
        RichContentText.SetValue(HavenProperties.Foreground, "TextSecondary"); EditorHost.Add(RichContentText);
        Root.Add(EditorHost);

        StatusText = new HavenText("Opening local presentations…") { Name = "Present.Status", Level = TextLevel.Caption };
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
    }

    public event EventHandler? PreviousDeckRequested; public event EventHandler? NextDeckRequested; public event EventHandler? NewDeckRequested;
    public event EventHandler? SaveRequested; public event EventHandler? ExportRequested;
    public event EventHandler? PreviousSlideRequested; public event EventHandler? NextSlideRequested; public event EventHandler? AddSlideRequested; public event EventHandler? DeleteSlideRequested;
    public event Action<string>? DeckTitleChanged; public event Action<string>? SlideTitleChanged; public event Action<string>? BodyChanged; public event Action<string>? NotesChanged;

    public Page Root { get; } public Container Header { get; } public Input DeckTitleInput { get; } public HavenText PositionText { get; }
    public Container DeckToolbar { get; } public Container SlideToolbar { get; } public Container EditorHost { get; }
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
            PositionText.Content = $"Deck {deckIndex + 1} of {Math.Max(deckCount, 1)} · Slide {slideIndex + 1} of {document.Slides.Count} · v{document.Version}";
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
        toolbar.SetValue(HavenProperties.Row, row); toolbar.SetValue(HavenProperties.Gap, HavenLength.Px(8)); toolbar.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll); return toolbar;
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
