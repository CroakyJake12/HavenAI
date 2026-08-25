using Haven.Application;
using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Present;

internal sealed partial class PresentHavenScene
{
    private PopupMenu? _workspacePopup;
    private bool _suppressInlineText;
    private Guid? _inlineTextElementId;
    private Guid? _activeVectorElementId;
    private PresentThemeColors _shapeThemeColors = new();
    private HavenButton _contextBoldButton = null!;
    private HavenButton _contextItalicButton = null!;
    private HavenButton _shapeFillButton = null!;
    private HavenButton _shapeOutlineButton = null!;
    private HavenButton _shapeEditPointsButton = null!;
    private HavenButton _contextCopyButton = null!;
    private HavenButton _contextArrangeButton = null!;
    private HavenButton _contextDeleteButton = null!;

    public event Action<Guid>? OpenDocumentRequested;
    public event Action<Guid>? PinDocumentRequested;
    public event Action<string>? TemplateRequested;
    public event EventHandler? ReturnToLibraryRequested;
    public event EventHandler? AiCreateRequested;
    public event EventHandler? AddImageRequested;
    public event EventHandler? AddMediaRequested;
    public event EventHandler? AddTableRequested;
    public event EventHandler? AddChartRequested;
    public event Action<Guid, string>? InlineTextChanged;
    public event Action<int, int>? TableSizeRequested;
    public event Action<string>? TableDataRequested;
    public event Action<PresentChartType>? ChartTypeRequested;
    public event Action<string>? ChartDataRequested;
    public event EventHandler? DistributeHorizontalRequested;
    public event EventHandler? DistributeVerticalRequested;
    public event EventHandler? AiEditRequested;
    public event Action<string?>? ShapeFillRequested;
    public event Action<string?>? ShapeStrokeRequested;
    public event Action<double>? ShapeStrokeWidthRequested;

    public Container MenuBar { get; private set; } = null!;
    public Container LibraryHost { get; private set; } = null!;
    public Container RecentDecks { get; private set; } = null!;
    public Container PinnedDecks { get; private set; } = null!;
    public Container WorkspaceHost { get; private set; } = null!;
    public Container SlideRail { get; private set; } = null!;
    public Container StageHost { get; private set; } = null!;
    public Container CanvasOverlay { get; private set; } = null!;
    public Container WorkspacePill { get; private set; } = null!;
    public HavenButton AdvancedButton { get; private set; } = null!;
    public Container AdvancedPanel { get; private set; } = null!;
    public Container ContextPill { get; private set; } = null!;
    public Container StructuredEditor { get; private set; } = null!;
    public Input InlineTextInput { get; private set; } = null!;
    public Input TableRowsInput { get; private set; } = null!;
    public Input TableColumnsInput { get; private set; } = null!;
    public Input TableDataInput { get; private set; } = null!;
    public Input ChartDataInput { get; private set; } = null!;
    public Container PlaybackOverlay { get; private set; } = null!;
    public PresentSlideCanvas PlaybackCanvas { get; private set; } = null!;
    public HavenText PlaybackNotes { get; private set; } = null!;
    public HavenButton PlaybackPreviousButton { get; private set; } = null!;
    public HavenButton PlaybackAdvanceButton { get; private set; } = null!;
    public HavenButton PlaybackExitButton { get; private set; } = null!;

    private void BuildWorkspaceControls()
    {
        Root.Rows = "Auto 1fr Auto";
        Root.SetValue(HavenProperties.Padding, HavenThickness.Parse("10px 16px 12px 16px"));
        Header.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        DeckToolbar.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        SlideToolbar.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        EditorHost.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        Workspace.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        StatusText.SetValue(HavenProperties.Row, 2);

        Header.Remove(DeckTitleInput);
        Header.Remove(PositionText);
        EditorHost.Remove(SlideCanvas);
        EditorHost.Remove(SlideTitleInput);
        NotesHost.Remove(NotesInput);

        MenuBar = new Container { Name = "Present.MenuBar", Layout = HavenLayout.Horizontal };
        MenuBar.SetValue(HavenProperties.Row, 0);
        MenuBar.SetValue(HavenProperties.Gap, HavenLength.Px(2));
        MenuBar.SetValue(HavenProperties.MinHeight, HavenLength.Px(38));
        MenuBar.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        AddMenuButton("File", ShowFileMenu);
        AddMenuButton("Edit", ShowEditMenu);
        AddMenuButton("View", ShowViewMenu);
        AddMenuButton("Slide", ShowSlideMenu);
        AddMenuButton("Arrange", ShowArrangeMenu);
        AddMenuButton("Present", ShowPresentMenu);
        Root.Add(MenuBar);

        LibraryHost = new Container { Name = "Present.Library", Layout = HavenLayout.Vertical };
        LibraryHost.SetValue(HavenProperties.Row, 1);
        LibraryHost.SetValue(HavenProperties.MaxWidth, HavenLength.Px(1180));
        LibraryHost.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        LibraryHost.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        LibraryHost.SetValue(HavenProperties.Padding, HavenThickness.Parse("28px 28px"));
        LibraryHost.SetValue(HavenProperties.Gap, HavenLength.Px(18));
        LibraryHost.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        var libraryTitle = new HavenText("Present") { Level = TextLevel.H2 };
        libraryTitle.SetValue(HavenProperties.FontSize, 32d);
        LibraryHost.Add(libraryTitle);
        var librarySubtitle = new HavenText("Create, import or reopen a presentation. Your decks stay editable and local.") { Level = TextLevel.Paragraph };
        librarySubtitle.SetValue(HavenProperties.Foreground, "TextSecondary");
        LibraryHost.Add(librarySubtitle);

        var createRow = new Container { Name = "Present.Library.Create", Layout = HavenLayout.Wrap };
        createRow.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        createRow.Add(ActionButton("Present.Library.New", "New presentation", ButtonVariant.Primary, () => NewDeckRequested?.Invoke(this, EventArgs.Empty)));
        createRow.Add(ActionButton("Present.Library.Import", "Open / import .pptx", ButtonVariant.Secondary, () => ImportRequested?.Invoke(this, EventArgs.Empty)));
        createRow.Add(ActionButton("Present.Library.AI", "Create with AI", ButtonVariant.Tertiary, () => AiCreateRequested?.Invoke(this, EventArgs.Empty)));
        LibraryHost.Add(createRow);

        LibraryHost.Add(SectionHeading("Templates"));
        var templates = new Container { Name = "Present.Library.Templates", Layout = HavenLayout.Wrap };
        templates.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        templates.Add(TemplateButton("Title + content", "title-content"));
        templates.Add(TemplateButton("Lesson deck", "lesson"));
        templates.Add(TemplateButton("Project pitch", "pitch"));
        LibraryHost.Add(templates);

        LibraryHost.Add(SectionHeading("Pinned"));
        PinnedDecks = DeckGallery("Present.Library.Pinned");
        LibraryHost.Add(PinnedDecks);
        LibraryHost.Add(SectionHeading("Recent"));
        RecentDecks = DeckGallery("Present.Library.Recent");
        LibraryHost.Add(RecentDecks);
        Root.Add(LibraryHost);

        WorkspaceHost = new Container { Name = "Present.Workspace", Layout = HavenLayout.Grid, Columns = "190px 1fr", Rows = "1fr" };
        WorkspaceHost.SetValue(HavenProperties.Row, 1);
        WorkspaceHost.SetValue(HavenProperties.Gap, HavenLength.Px(12));
        WorkspaceHost.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);

        SlideRail = new Container { Name = "Present.SlideRail", Layout = HavenLayout.Vertical };
        SlideRail.SetValue(HavenProperties.Column, 0);
        SlideRail.SetValue(HavenProperties.Background, "SurfaceRaised");
        SlideRail.SetValue(HavenProperties.BorderColor, "Border");
        SlideRail.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        SlideRail.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(16)));
        SlideRail.SetValue(HavenProperties.Padding, HavenThickness.Parse("10px"));
        SlideRail.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        SlideRail.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        WorkspaceHost.Add(SlideRail);

        StageHost = new Container { Name = "Present.Stage", Layout = HavenLayout.Grid, Columns = "1fr", Rows = "1fr Auto" };
        StageHost.SetValue(HavenProperties.Column, 1);
        StageHost.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        CanvasOverlay = new Container { Name = "Present.Stage.CanvasOverlay", Layout = HavenLayout.Overlay };
        CanvasOverlay.SetValue(HavenProperties.Row, 0);
        CanvasOverlay.SetValue(HavenProperties.MinHeight, HavenLength.Px(500));
        CanvasOverlay.Add(SlideCanvas);

        InlineTextInput = new Input { Name = "Present.InlineText", Multiline = true, Placeholder = "Type on the slide…" };
        InlineTextInput.Accessibility.AccessibleName = "Selected slide text";
        InlineTextInput.SetValue(HavenProperties.LayoutParticipation, HavenLayoutParticipation.Overlay);
        InlineTextInput.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Start);
        InlineTextInput.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Start);
        InlineTextInput.SetValue(HavenProperties.ZIndex, 40);
        InlineTextInput.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        InlineTextInput.SetValue(HavenProperties.Background, "SurfaceRaised");
        InlineTextInput.SetValue(HavenProperties.BorderColor, "Accent");
        InlineTextInput.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        InlineTextInput.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(4)));
        InlineTextInput.Invalidated += OnInlineTextInvalidated;
        CanvasOverlay.Add(InlineTextInput);

        WorkspacePill = Pill("Present.WorkspacePill");
        WorkspacePill.SetValue(HavenProperties.LayoutParticipation, HavenLayoutParticipation.Overlay);
        WorkspacePill.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        WorkspacePill.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Start);
        WorkspacePill.SetValue(HavenProperties.Height, HavenLength.Px(48));
        WorkspacePill.SetValue(HavenProperties.Margin, HavenThickness.Parse("8px 0px 0px 0px"));
        WorkspacePill.SetValue(HavenProperties.ZIndex, 60);
        DeckTitleInput.SetValue(HavenProperties.Width, HavenLength.Px(260));
        DeckTitleInput.SetValue(HavenProperties.MinHeight, HavenLength.Px(36));
        DeckTitleInput.SetValue(HavenProperties.FontSize, 15d);
        WorkspacePill.Add(DeckTitleInput);
        HavenButton insert = null!;
        insert = ActionButton("Present.Insert", "+", ButtonVariant.Primary, () => ShowInsertMenu(insert));
        insert.Accessibility.AccessibleName = "Insert";
        WorkspacePill.Add(insert);
        WorkspacePill.Add(ActionButton("Present.Workspace.Undo", "Undo", ButtonVariant.Ghost, () => UndoRequested?.Invoke(this, EventArgs.Empty)));
        WorkspacePill.Add(ActionButton("Present.Workspace.Redo", "Redo", ButtonVariant.Ghost, () => RedoRequested?.Invoke(this, EventArgs.Empty)));
        WorkspacePill.Add(ActionButton("Present.Workspace.AI", "AI edit", ButtonVariant.Tertiary, () => AiEditRequested?.Invoke(this, EventArgs.Empty)));
        AdvancedButton = ActionButton("Present.Workspace.Design", "Design", ButtonVariant.Ghost, ToggleAdvancedPanel);
        AdvancedButton.Accessibility.AccessibleName = "Design, transitions and animations";
        WorkspacePill.Add(AdvancedButton);
        DeckToolbar.Remove(PresentButton);
        PresentButton.Variant = ButtonVariant.Secondary;
        WorkspacePill.Add(PresentButton);
        CanvasOverlay.Add(WorkspacePill);

        InspectorPane.Remove(DesignPlaybackControls);
        AdvancedPanel = new Container { Name = "Present.AdvancedPanel", Layout = HavenLayout.Vertical };
        AdvancedPanel.SetValue(HavenProperties.LayoutParticipation, HavenLayoutParticipation.Overlay);
        AdvancedPanel.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.End);
        AdvancedPanel.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Start);
        AdvancedPanel.SetValue(HavenProperties.Width, HavenLength.Px(340));
        AdvancedPanel.SetValue(HavenProperties.MaxHeight, HavenLength.Px(720));
        AdvancedPanel.SetValue(HavenProperties.Margin, HavenThickness.Parse("64px 12px 0px 0px"));
        AdvancedPanel.SetValue(HavenProperties.Padding, HavenThickness.Parse("14px"));
        AdvancedPanel.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        AdvancedPanel.SetValue(HavenProperties.Background, "SurfaceRaised");
        AdvancedPanel.SetValue(HavenProperties.BorderColor, "Border");
        AdvancedPanel.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        AdvancedPanel.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(16)));
        AdvancedPanel.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        AdvancedPanel.SetValue(HavenProperties.ZIndex, 200);
        AdvancedPanel.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        AdvancedPanel.Add(DesignPlaybackControls);
        CanvasOverlay.Add(AdvancedPanel);

        ContextPill = Pill("Present.ContextPill");
        ContextPill.SetValue(HavenProperties.LayoutParticipation, HavenLayoutParticipation.Overlay);
        ContextPill.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        ContextPill.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Start);
        ContextPill.SetValue(HavenProperties.Height, HavenLength.Px(48));
        ContextPill.SetValue(HavenProperties.Margin, HavenThickness.Parse("60px 0px 0px 0px"));
        ContextPill.SetValue(HavenProperties.ZIndex, 60);
        ContextPill.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        _contextBoldButton = ActionButton("Present.Context.Bold", "B", ButtonVariant.Ghost, () => BoldRequested?.Invoke(this, EventArgs.Empty));
        _contextItalicButton = ActionButton("Present.Context.Italic", "I", ButtonVariant.Ghost, () => ItalicRequested?.Invoke(this, EventArgs.Empty));
        _shapeFillButton = ActionButton("Present.Context.Shape.Fill", "Fill", ButtonVariant.Tertiary, () => ShowShapeFillMenu(_shapeFillButton));
        _shapeOutlineButton = ActionButton("Present.Context.Shape.Outline", "Outline", ButtonVariant.Ghost, () => ShowShapeOutlineMenu(_shapeOutlineButton));
        _shapeEditPointsButton = ActionButton("Present.Context.Shape.EditPoints", "Edit points", ButtonVariant.Ghost, ToggleVectorPointEditing);
        _contextCopyButton = ActionButton("Present.Context.Copy", "Copy", ButtonVariant.Ghost, () => CopyRequested?.Invoke(this, EventArgs.Empty));
        _contextArrangeButton = ActionButton("Present.Context.Arrange", "Arrange", ButtonVariant.Ghost, () => ShowArrangeMenu(_contextArrangeButton));
        _contextDeleteButton = ActionButton("Present.Context.Delete", "Delete", ButtonVariant.Ghost, () => DeleteObjectRequested?.Invoke(this, EventArgs.Empty));
        ContextPill.Add(_contextBoldButton);
        ContextPill.Add(_contextItalicButton);
        ContextPill.Add(_shapeFillButton);
        ContextPill.Add(_shapeOutlineButton);
        ContextPill.Add(_shapeEditPointsButton);
        ContextPill.Add(_contextCopyButton);
        ContextPill.Add(_contextArrangeButton);
        ContextPill.Add(_contextDeleteButton);




        CanvasOverlay.Add(ContextPill);

        StructuredEditor = new Container { Name = "Present.StructuredEditor", Layout = HavenLayout.Vertical };
        StructuredEditor.SetValue(HavenProperties.LayoutParticipation, HavenLayoutParticipation.Overlay);
        StructuredEditor.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.End);
        StructuredEditor.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Start);
        StructuredEditor.SetValue(HavenProperties.Margin, HavenThickness.Parse("112px 8px 0px 0px"));
        StructuredEditor.SetValue(HavenProperties.Width, HavenLength.Px(270));
        StructuredEditor.SetValue(HavenProperties.MaxHeight, HavenLength.Px(360));
        StructuredEditor.SetValue(HavenProperties.Background, "SurfaceRaised");
        StructuredEditor.SetValue(HavenProperties.BorderColor, "Border");
        StructuredEditor.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        StructuredEditor.SetValue(HavenProperties.Padding, HavenThickness.Parse("12px"));
        StructuredEditor.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        StructuredEditor.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(16)));
        StructuredEditor.SetValue(HavenProperties.Shadow, "Card");
        StructuredEditor.SetValue(HavenProperties.ZIndex, 61);
        StructuredEditor.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        StructuredEditor.Add(SectionHeading("Structured object"));
        var tableRow = new Container { Name = "Present.Table.Size", Layout = HavenLayout.Horizontal };
        tableRow.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        TableRowsInput = new Input { Name = "Present.Table.Rows", Placeholder = "Rows" };
        TableRowsInput.Accessibility.AccessibleName = "Table rows";
        TableRowsInput.SetValue(HavenProperties.Width, HavenLength.Px(70));
        TableColumnsInput = new Input { Name = "Present.Table.Columns", Placeholder = "Columns" };
        TableColumnsInput.Accessibility.AccessibleName = "Table columns";
        TableColumnsInput.SetValue(HavenProperties.Width, HavenLength.Px(80));
        tableRow.Add(TableRowsInput); tableRow.Add(TableColumnsInput);
        tableRow.Add(ActionButton("Present.Table.ApplySize", "Apply", ButtonVariant.Tertiary, ApplyTableSize));
        StructuredEditor.Add(tableRow);
        TableDataInput = new Input { Name = "Present.Table.Data", Multiline = true, Placeholder = "Cell | Cell\nCell | Cell" };
        TableDataInput.Accessibility.AccessibleName = "Table cell data";
        TableDataInput.SetValue(HavenProperties.MinHeight, HavenLength.Px(110));
        StructuredEditor.Add(TableDataInput);
        StructuredEditor.Add(ActionButton("Present.Table.ApplyData", "Apply table data", ButtonVariant.Tertiary, () => TableDataRequested?.Invoke(TableDataInput.Text)));
        var chartTypes = new Container { Name = "Present.Chart.Types", Layout = HavenLayout.Wrap };
        chartTypes.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        foreach (var type in new[] { PresentChartType.Column, PresentChartType.Bar, PresentChartType.Line, PresentChartType.Area, PresentChartType.Pie })
        {
            var captured = type;
            chartTypes.Add(ActionButton($"Present.Chart.{captured}", captured.ToString(), ButtonVariant.Ghost, () => ChartTypeRequested?.Invoke(captured)));
        }
        StructuredEditor.Add(chartTypes);
        ChartDataInput = new Input { Name = "Present.Chart.Data", Multiline = true, Placeholder = "Category, value\nCategory, value" };
        ChartDataInput.Accessibility.AccessibleName = "Chart data";
        ChartDataInput.SetValue(HavenProperties.MinHeight, HavenLength.Px(110));
        StructuredEditor.Add(ChartDataInput);
        StructuredEditor.Add(ActionButton("Present.Chart.ApplyData", "Apply chart data", ButtonVariant.Tertiary, () => ChartDataRequested?.Invoke(ChartDataInput.Text)));
        CanvasOverlay.Add(StructuredEditor);

        StageHost.Add(CanvasOverlay);
        var notesBar = new Container { Name = "Present.NotesBar", Layout = HavenLayout.Grid, Columns = "120px 1fr 220px", Rows = "Auto" };
        notesBar.SetValue(HavenProperties.Row, 1);
        notesBar.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        SlideTitleInput.SetValue(HavenProperties.Column, 0);
        SlideTitleInput.SetValue(HavenProperties.MinHeight, HavenLength.Px(38));
        SlideTitleInput.SetValue(HavenProperties.FontSize, 13d);
        SlideTitleInput.Placeholder = "Slide name";
        NotesInput.SetValue(HavenProperties.Column, 1);
        NotesInput.SetValue(HavenProperties.MinHeight, HavenLength.Px(54));
        NotesInput.SetValue(HavenProperties.MaxHeight, HavenLength.Px(92));
        notesBar.Add(SlideTitleInput);
        notesBar.Add(NotesInput);
        PositionText.SetValue(HavenProperties.Column, 2);
        PositionText.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        notesBar.Add(PositionText);
        StageHost.Add(notesBar);
        WorkspaceHost.Add(StageHost);
        Root.Add(WorkspaceHost);

        PlaybackOverlay = new Container { Name = "Present.Playback", Layout = HavenLayout.Grid, Columns = "1fr", Rows = "1fr Auto" };
        PlaybackOverlay.SetValue(HavenProperties.Row, 0);
        PlaybackOverlay.SetValue(HavenProperties.RowSpan, 3);
        PlaybackOverlay.SetValue(HavenProperties.LayoutParticipation, HavenLayoutParticipation.Overlay);
        PlaybackOverlay.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        PlaybackOverlay.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        PlaybackOverlay.SetValue(HavenProperties.Background, "Surface");
        PlaybackOverlay.SetValue(HavenProperties.ZIndex, 800);
        PlaybackOverlay.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        PlaybackCanvas = new PresentSlideCanvas();
        PlaybackCanvas.SetValue(HavenProperties.Row, 0);
        PlaybackCanvas.SetValue(HavenProperties.MinHeight, HavenLength.Px(620));
        PlaybackOverlay.Add(PlaybackCanvas);
        var playbackBar = new Container { Name = "Present.Playback.Controls", Layout = HavenLayout.Grid, Columns = "Auto Auto 1fr Auto", Rows = "Auto" };
        playbackBar.SetValue(HavenProperties.Row, 1);
        playbackBar.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        PlaybackPreviousButton = ActionButton("Present.Playback.Previous", "Previous", ButtonVariant.Ghost, () => PlaybackPreviousRequested?.Invoke(this, EventArgs.Empty));
        PlaybackAdvanceButton = ActionButton("Present.Playback.Next", "Next", ButtonVariant.Primary, () => PlaybackAdvanceRequested?.Invoke(this, EventArgs.Empty));
        PlaybackNotes = new HavenText { Name = "Present.Playback.Notes", Level = TextLevel.Caption };
        PlaybackNotes.SetValue(HavenProperties.Column, 2);
        PlaybackNotes.SetValue(HavenProperties.Foreground, "TextSecondary");
        PlaybackExitButton = ActionButton("Present.Playback.Exit", "Exit presentation", ButtonVariant.Tertiary, () => PlaybackExitRequested?.Invoke(this, EventArgs.Empty));
        PlaybackPreviousButton.SetValue(HavenProperties.Column, 0); PlaybackAdvanceButton.SetValue(HavenProperties.Column, 1); PlaybackExitButton.SetValue(HavenProperties.Column, 3);
        playbackBar.Add(PlaybackPreviousButton); playbackBar.Add(PlaybackAdvanceButton); playbackBar.Add(PlaybackNotes); playbackBar.Add(PlaybackExitButton);
        PlaybackOverlay.Add(playbackBar);
        Root.Add(PlaybackOverlay);
        ApplyWorkspacePolish();
    }

    public void SetLibrary(IReadOnlyList<PresentDocumentSummary> documents)
    {
        documents ??= Array.Empty<PresentDocumentSummary>();
        LibraryHost.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
        WorkspaceHost.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        MenuBar.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        PlaybackOverlay.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        FillDeckGalleryPolished(PinnedDecks, documents.Where(document => document.Pinned));
        FillDeckGalleryPolished(RecentDecks, documents);
        StatusText.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        SetStatus(documents.Count == 0 ? "No presentations yet. Create one or import a PowerPoint file." : $"{documents.Count} presentation{(documents.Count == 1 ? string.Empty : "s")} available locally.");
    }

    public void SetWorkspaceDocument(PresentDocument document, int slideIndex)
    {
        ArgumentNullException.ThrowIfNull(document);
        LibraryHost.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        WorkspaceHost.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
        MenuBar.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
        PlaybackOverlay.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        ClearChildren(SlideRail);
        for (var index = 0; index < document.Slides.Count; index++)
        {
            var captured = index;
            var slide = document.Slides[index];
            var button = ActionButton($"Present.Rail.{slide.Id:N}", $"{index + 1}  {DisplayTitle(slide.Title)}", ButtonVariant.Navigation, () => SlideSelected?.Invoke(captured));
            button.SetState(HavenElementState.Selected, index == slideIndex);
            button.Accessibility.AccessibleName = $"Slide {index + 1}: {DisplayTitle(slide.Title)}";
            SlideRail.Add(button);
        }
        SlideToolbar.Remove(AddSlideButton);
        AddSlideButton.Content = "+ Add slide";
        AddSlideButton.Variant = ButtonVariant.Tertiary;
        SlideRail.Add(AddSlideButton);
        SlideToolbar.Remove(DeleteSlideButton);
        DeleteSlideButton.Variant = ButtonVariant.Ghost;
        SlideRail.Add(DeleteSlideButton);
        PolishSlideRail();
        StatusText.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
    }

    public void SetWorkspaceSelection(PresentDocument document, int slideIndex, IReadOnlyCollection<Guid> selectedElementIds)
    {
        var selected = selectedElementIds?.ToHashSet() ?? [];
        var slide = document.Slides[Math.Clamp(slideIndex, 0, document.Slides.Count - 1)];
        var elements = slide.Elements.Where(element => selected.Contains(element.Id)).ToArray();
        _shapeThemeColors = document.Theme.Colors;
        var single = elements.Length == 1 ? elements[0] : null;
        var isText = single?.Kind == PresentElementKind.Text;
        var isShape = single?.Kind == PresentElementKind.Shape;
        var isVector = isShape && single?.VectorShape is not null;
        SetContextVisibility(_contextBoldButton, isText);
        SetContextVisibility(_contextItalicButton, isText);
        SetContextVisibility(_shapeFillButton, isShape);
        SetContextVisibility(_shapeOutlineButton, isShape);
        SetContextVisibility(_shapeEditPointsButton, isVector);
        SetContextVisibility(_contextCopyButton, elements.Length > 0);
        SetContextVisibility(_contextArrangeButton, elements.Length > 0);
        SetContextVisibility(_contextDeleteButton, elements.Length > 0);
        _activeVectorElementId = isVector ? single!.Id : null;
        if (!isVector) SlideCanvas.SetVectorPointEditing(null);
        var pointEditing = isVector && SlideCanvas.VectorPointEditingElementId == single!.Id;
        _shapeEditPointsButton.SetState(HavenElementState.Selected, pointEditing);
        _shapeEditPointsButton.Content = pointEditing ? "Done" : "Edit points";
        _shapeEditPointsButton.Accessibility.AccessibleName = pointEditing ? "Finish editing vector points" : "Edit vector points";
        if (elements.Length > 0)
        {
            ContextPill.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Start);
            if (single is not null)
            {
                var selectedBounds = SlideCanvas.GetElementEditorBounds(single);
                var availableWidth = Math.Max(1d, SlideCanvas.Bounds.Width);
                var pillLeft = Math.Clamp(selectedBounds.X + selectedBounds.Width / 2d - 150d, 8d, Math.Max(8d, availableWidth - 320d));
                var pillTop = selectedBounds.Y >= 110d
                    ? selectedBounds.Y - 50d
                    : Math.Min(selectedBounds.Bottom + 8d, Math.Max(58d, SlideCanvas.Bounds.Height - 50d));
                ContextPill.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Start);
                ContextPill.SetValue(HavenProperties.Margin, new HavenThickness(
                    HavenLength.Px(pillLeft),
                    HavenLength.Px(Math.Max(58d, pillTop)),
                    HavenLength.Px(0),
                    HavenLength.Px(0)));
            }
            else
            {
                ContextPill.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
                ContextPill.SetValue(HavenProperties.Margin, HavenThickness.Parse("58px 0px 0px 0px"));
            }
        }
        ContextPill.SetValue(HavenProperties.Visibility, elements.Length == 0 ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        StructuredEditor.SetValue(HavenProperties.Visibility, elements.Length == 1 && elements[0].Kind is PresentElementKind.Table or PresentElementKind.Chart ? HavenVisibility.Visible : HavenVisibility.Collapsed);

        _inlineTextElementId = elements.Length == 1 && elements[0].Kind == PresentElementKind.Text ? elements[0].Id : null;
        if (_inlineTextElementId is { } textId)
        {
            var element = elements[0];
            var bounds = SlideCanvas.GetElementEditorBounds(element);
            _suppressInlineText = true;
            try { InlineTextInput.Text = element.Text; } finally { _suppressInlineText = false; }
            InlineTextInput.SetValue(HavenProperties.Margin, new HavenThickness(
                HavenLength.Px(bounds.X),
                HavenLength.Px(bounds.Y),
                HavenLength.Px(0),
                HavenLength.Px(0)));
            InlineTextInput.SetValue(HavenProperties.Width, HavenLength.Px(Math.Max(80, bounds.Width)));
            InlineTextInput.SetValue(HavenProperties.Height, HavenLength.Px(Math.Max(42, bounds.Height)));
            InlineTextInput.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
        }
        else InlineTextInput.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);

        if (elements.Length == 1 && elements[0].Kind == PresentElementKind.Table)
        {
            var table = elements[0].ReadTable();
            TableRowsInput.Text = table.Rows.ToString(System.Globalization.CultureInfo.InvariantCulture);
            TableColumnsInput.Text = table.Columns.ToString(System.Globalization.CultureInfo.InvariantCulture);
            TableDataInput.Text = string.Join("\n", Enumerable.Range(0, table.Rows)
                .Select(row => string.Join(" | ", Enumerable.Range(0, table.Columns).Select(column => table.GetCell(row, column).Text))));
            TableDataInput.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
            ChartDataInput.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        }
        else if (elements.Length == 1 && elements[0].Kind == PresentElementKind.Chart)
        {
            var chart = elements[0].ReadChart();
            var series = chart.Series.FirstOrDefault();
            TableDataInput.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
            ChartDataInput.Text = string.Join("\n", chart.Categories.Select((category, index) => $"{category}, {(series is not null && index < series.Values.Count ? series.Values[index] : 0):0.###}"));
            ChartDataInput.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
        }
        else
        {
            TableDataInput.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
            ChartDataInput.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        }
    }

    public void SetPlayback(PresentDocument document, PresentPlaybackFrame frame)
    {
        WorkspaceHost.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        PlaybackOverlay.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
        PlaybackCanvas.SetSlide(document, BuildPlaybackSlide(frame), Array.Empty<Guid>());
        PlaybackNotes.Content = $"Slide {frame.SlideNumber} of {frame.SlideCount} · reveal {frame.ActiveAnimations.Count}/{frame.Slide.Animations.Count} · {frame.Elapsed.ToString(@"mm\:ss")} · {frame.SpeakerNotes}";
    }

    public void HidePlayback()
    {
        PlaybackOverlay.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        WorkspaceHost.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
    }

    private void FillDeckGallery(Container gallery, IEnumerable<PresentDocumentSummary> documents)
    {
        ClearChildren(gallery);
        var materialized = documents.ToArray();
        if (materialized.Length == 0)
        {
            var empty = new HavenText("Nothing here yet.") { Level = TextLevel.Caption };
            empty.SetValue(HavenProperties.Foreground, "TextSecondary");
            gallery.Add(empty);
            return;
        }
        foreach (var document in materialized)
        {
            var card = new Container { Name = $"Present.Library.Card.{document.Id:N}", Layout = HavenLayout.Vertical };
            card.SetValue(HavenProperties.Width, HavenLength.Px(250));
            card.SetValue(HavenProperties.MinHeight, HavenLength.Px(126));
            card.SetValue(HavenProperties.Background, "SurfaceRaised");
            card.SetValue(HavenProperties.BorderColor, "Border");
            card.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
            card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(16)));
            card.SetValue(HavenProperties.Padding, HavenThickness.Parse("14px"));
            card.SetValue(HavenProperties.Gap, HavenLength.Px(6));
            var open = ActionButton($"Present.Library.Open.{document.Id:N}", document.Title, ButtonVariant.Navigation, () => OpenDocumentRequested?.Invoke(document.Id));
            open.SetValue(HavenProperties.FontSize, 16d);
            card.Add(open);
            var detail = new HavenText($"{document.SlideCount} slide{(document.SlideCount == 1 ? string.Empty : "s")} · {document.UpdatedAt.LocalDateTime:g}") { Level = TextLevel.Caption };
            detail.SetValue(HavenProperties.Foreground, "TextSecondary");
            card.Add(detail);
            card.Add(ActionButton($"Present.Library.Pin.{document.Id:N}", document.Pinned ? "Unpin" : "Pin", ButtonVariant.Ghost, () => PinDocumentRequested?.Invoke(document.Id)));
            gallery.Add(card);
        }
    }

    private void AddMenuButton(string label, Action<HavenButton> show)
    {
        HavenButton? button = null;
        button = ActionButton($"Present.Menu.{label}", label, ButtonVariant.Text, () => show(button!));
        MenuBar.Add(button);
    }

    private HavenButton TemplateButton(string label, string templateId) =>
        ActionButton($"Present.Template.{templateId}", label, ButtonVariant.Navigation, () => TemplateRequested?.Invoke(templateId));

    private static Container DeckGallery(string name)
    {
        var gallery = new Container { Name = name, Layout = HavenLayout.Wrap };
        gallery.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        return gallery;
    }

    private static HavenText SectionHeading(string text)
    {
        var heading = new HavenText(text) { Level = TextLevel.H4 };
        heading.SetValue(HavenProperties.FontSize, 18d);
        return heading;
    }

    private static Container Pill(string name)
    {
        var pill = new Container { Name = name, Layout = HavenLayout.Horizontal };
        pill.SetValue(HavenProperties.Background, "SurfaceRaised");
        pill.SetValue(HavenProperties.BorderColor, "Border");
        pill.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        pill.SetValue(HavenProperties.Padding, HavenThickness.Parse("6px 8px"));
        pill.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        pill.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(22)));
        pill.SetValue(HavenProperties.Shadow, "Card");
        return pill;
    }

    private static HavenButton ActionButton(string name, string content, ButtonVariant variant, Action action)
    {
        var button = new HavenButton { Name = name, Content = content, Variant = variant };
        button.Accessibility.AccessibleName = content;
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(34));
        button.Invoked += (_, _) => action();
        return button;
    }

    private void ShowFileMenu(HavenButton anchor) => ShowWorkspacePopup(anchor, "File menu",
    [
        new PopupMenuItem("New presentation", () => NewDeckRequested?.Invoke(this, EventArgs.Empty)),
        new PopupMenuItem("Open / import PowerPoint", () => ImportRequested?.Invoke(this, EventArgs.Empty)),
        new PopupMenuItem("Save", () => SaveRequested?.Invoke(this, EventArgs.Empty)),
        new PopupMenuItem("Export PowerPoint", () => ExportRequested?.Invoke(this, EventArgs.Empty)),
        new PopupMenuItem("Back to presentations", () => ReturnToLibraryRequested?.Invoke(this, EventArgs.Empty))
    ]);

    private void ShowEditMenu(HavenButton anchor) => ShowWorkspacePopup(anchor, "Edit menu",
    [
        new PopupMenuItem("Undo", () => UndoRequested?.Invoke(this, EventArgs.Empty)),
        new PopupMenuItem("Redo", () => RedoRequested?.Invoke(this, EventArgs.Empty)),
        new PopupMenuItem("Copy", () => CopyRequested?.Invoke(this, EventArgs.Empty)),
        new PopupMenuItem("Paste", () => PasteRequested?.Invoke(this, EventArgs.Empty)),
        new PopupMenuItem("Delete selected object", () => DeleteObjectRequested?.Invoke(this, EventArgs.Empty), true)
    ]);

    private void ShowViewMenu(HavenButton anchor) => ShowWorkspacePopup(anchor, "View menu",
    [
        new PopupMenuItem("Presentation library", () => ReturnToLibraryRequested?.Invoke(this, EventArgs.Empty)),
        new PopupMenuItem("Focus slide", () => SetStatus("Slide-first workspace is active.")),
        new PopupMenuItem("Speaker notes", () => SetStatus("Speaker notes are shown beneath the slide."))
    ]);

    private void ShowSlideMenu(HavenButton anchor) => ShowWorkspacePopup(anchor, "Slide menu",
    [
        new PopupMenuItem("New slide", () => AddSlideRequested?.Invoke(this, EventArgs.Empty)),
        new PopupMenuItem("Duplicate slide", () => DuplicateSlideRequested?.Invoke(this, EventArgs.Empty)),
        new PopupMenuItem("Move earlier", () => MoveSlideEarlierRequested?.Invoke(this, EventArgs.Empty)),
        new PopupMenuItem("Move later", () => MoveSlideLaterRequested?.Invoke(this, EventArgs.Empty)),
        new PopupMenuItem("Delete slide", () => DeleteSlideRequested?.Invoke(this, EventArgs.Empty), true)
    ]);

    private static void SetContextVisibility(HavenElement element, bool visible) =>
        element.SetValue(HavenProperties.Visibility, visible ? HavenVisibility.Visible : HavenVisibility.Collapsed);

    private void ToggleVectorPointEditing()
    {
        if (_activeVectorElementId is not { } id) return;
        var enable = SlideCanvas.VectorPointEditingElementId != id;
        SlideCanvas.SetVectorPointEditing(enable ? id : null);
        _shapeEditPointsButton.SetState(HavenElementState.Selected, enable);
        _shapeEditPointsButton.Content = enable ? "Done" : "Edit points";
        _shapeEditPointsButton.Accessibility.AccessibleName = enable ? "Finish editing vector points" : "Edit vector points";
        SetStatus(enable ? "Point editing · drag nodes and Bézier handles." : "Shape editing · drag to move, resize or rotate.");
    }

    private void ShowShapeFillMenu(HavenButton anchor) => ShowWorkspacePopup(anchor, "Shape fill",
    [
        new PopupMenuItem("No fill", () => ShapeFillRequested?.Invoke(null)),
        new PopupMenuItem("Accent 1", () => ShapeFillRequested?.Invoke(_shapeThemeColors.Accent1)),
        new PopupMenuItem("Accent 2", () => ShapeFillRequested?.Invoke(_shapeThemeColors.Accent2)),
        new PopupMenuItem("Accent 3", () => ShapeFillRequested?.Invoke(_shapeThemeColors.Accent3)),
        new PopupMenuItem("Accent 4", () => ShapeFillRequested?.Invoke(_shapeThemeColors.Accent4)),
        new PopupMenuItem("Accent 5", () => ShapeFillRequested?.Invoke(_shapeThemeColors.Accent5)),
        new PopupMenuItem("Accent 6", () => ShapeFillRequested?.Invoke(_shapeThemeColors.Accent6)),
        new PopupMenuItem("White", () => ShapeFillRequested?.Invoke("#FFFFFFFF")),
        new PopupMenuItem("Black", () => ShapeFillRequested?.Invoke("#FF202020"))
    ]);

    private void ShowShapeOutlineMenu(HavenButton anchor) => ShowWorkspacePopup(anchor, "Shape outline",
    [
        new PopupMenuItem("No outline", () => ShapeStrokeRequested?.Invoke(null)),
        new PopupMenuItem("Accent 1", () => ShapeStrokeRequested?.Invoke(_shapeThemeColors.Accent1)),
        new PopupMenuItem("Accent 2", () => ShapeStrokeRequested?.Invoke(_shapeThemeColors.Accent2)),
        new PopupMenuItem("Accent 3", () => ShapeStrokeRequested?.Invoke(_shapeThemeColors.Accent3)),
        new PopupMenuItem("Dark", () => ShapeStrokeRequested?.Invoke("#FF202020")),
        new PopupMenuItem("Thin · 1 pt", () => ShapeStrokeWidthRequested?.Invoke(1)),
        new PopupMenuItem("Medium · 2 pt", () => ShapeStrokeWidthRequested?.Invoke(2)),
        new PopupMenuItem("Bold · 4 pt", () => ShapeStrokeWidthRequested?.Invoke(4)),
        new PopupMenuItem("Heavy · 8 pt", () => ShapeStrokeWidthRequested?.Invoke(8))
    ]);

    private void ShowArrangeMenu(HavenButton anchor) => ShowWorkspacePopup(anchor, "Arrange menu",
    [
        new PopupMenuItem("Bring to front", () => BringFrontRequested?.Invoke(this, EventArgs.Empty)),
        new PopupMenuItem("Send to back", () => SendBackRequested?.Invoke(this, EventArgs.Empty)),
        new PopupMenuItem("Group", () => GroupRequested?.Invoke(this, EventArgs.Empty)),
        new PopupMenuItem("Ungroup", () => UngroupRequested?.Invoke(this, EventArgs.Empty)),
        new PopupMenuItem("Align left", () => AlignLeftRequested?.Invoke(this, EventArgs.Empty)),
        new PopupMenuItem("Align centre", () => AlignCenterRequested?.Invoke(this, EventArgs.Empty)),
        new PopupMenuItem("Distribute horizontally", () => DistributeHorizontalRequested?.Invoke(this, EventArgs.Empty)),
        new PopupMenuItem("Distribute vertically", () => DistributeVerticalRequested?.Invoke(this, EventArgs.Empty))
    ]);

    private void ToggleAdvancedPanel()
    {
        var visible = AdvancedPanel.GetValue<HavenVisibility>(HavenProperties.Visibility) != HavenVisibility.Visible;
        AdvancedPanel.SetValue(HavenProperties.Visibility, visible ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        AdvancedButton.SetState(HavenElementState.Selected, visible);
        AdvancedButton.Accessibility.AccessibleName = visible ? "Close design, transitions and animations" : "Design, transitions and animations";
    }

    private void ShowPresentMenu(HavenButton anchor) => ShowWorkspacePopup(anchor, "Present menu",
    [
        new PopupMenuItem("Start presentation", () => PresentRequested?.Invoke(this, EventArgs.Empty)),
        new PopupMenuItem("Presenter view", () => PresentRequested?.Invoke(this, EventArgs.Empty))
    ]);

    private void ShowInsertMenu(HavenButton anchor) => ShowWorkspacePopup(anchor, "Insert menu",
    [
        new PopupMenuItem("Text box", () => AddTextRequested?.Invoke(this, EventArgs.Empty)),
        new PopupMenuItem("Image", () => AddImageRequested?.Invoke(this, EventArgs.Empty)),
        new PopupMenuItem("Shape", () => AddShapeRequested?.Invoke(this, EventArgs.Empty)),
        new PopupMenuItem("Table", () => AddTableRequested?.Invoke(this, EventArgs.Empty)),
        new PopupMenuItem("Chart", () => AddChartRequested?.Invoke(this, EventArgs.Empty)),
        new PopupMenuItem("Media", () => AddMediaRequested?.Invoke(this, EventArgs.Empty))
    ]);

    private void ShowWorkspacePopup(HavenElement anchor, string name, IReadOnlyList<PopupMenuItem> items)
    {
        _workspacePopup?.Dismiss();
        var popup = new PopupMenu(anchor, Root, items, 250d, name);
        popup.Dismissed += (_, _) => { if (ReferenceEquals(_workspacePopup, popup)) _workspacePopup = null; };
        _workspacePopup = popup;
        Root.Add(popup);
    }

    private void ApplyTableSize()
    {
        if (!int.TryParse(TableRowsInput.Text, out var rows) || !int.TryParse(TableColumnsInput.Text, out var columns))
        {
            SetStatus("Enter whole-number rows and columns.");
            return;
        }
        TableSizeRequested?.Invoke(Math.Clamp(rows, 1, 100), Math.Clamp(columns, 1, 100));
    }

    private void OnInlineTextInvalidated(object? sender, EventArgs e)
    {
        if (_suppressInlineText || _inlineTextElementId is not { } id) return;
        InlineTextChanged?.Invoke(id, InlineTextInput.Text);
    }
}
