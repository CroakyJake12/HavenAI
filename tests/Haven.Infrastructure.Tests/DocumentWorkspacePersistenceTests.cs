using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class DocumentWorkspacePersistenceTests : IDisposable
{
    private readonly WorkspaceTestPaths _paths = new();

    [Fact]
    public async Task Write_table_and_media_authoring_survives_real_repository_reopen()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var validator = new NotesDocumentValidator();
        var document = NotesDocument.Create("Write restart");
        var editor = new WriteDocumentEditor(document);

        var table = editor.InsertBlock(NotesBlockKind.Table);
        editor.SelectBlock(table.Id);
        var firstCell = table.Table!.Rows[0].Cells[0];
        editor.UpdateTableCell(firstCell.Id, "Persistent cell");
        Assert.Equal("Persistent cell", firstCell.Text);
        Assert.True(editor.SetTableCellBackground(firstCell.Id, "#FFEAF2FF"));
        Assert.True(editor.MergeTableCellRight(firstCell.Id));

        var media = editor.InsertMedia(new NotesMediaData
        {
            OriginalName = "diagram.png",
            MediaType = "image/png",
            AltText = "Architecture diagram",
            Caption = "System overview",
            Width = 320,
            Height = 180
        });
        editor.SelectBlock(media.Id);
        Assert.True(editor.ResizeSelectedMedia(480, 270));
        Assert.True(editor.RotateSelectedMedia(25));
        Assert.True(editor.SetSelectedMediaCrop(.05, .1, .05, .1));
        editor.UpdateMedia("Architecture diagram", "System overview", "Square");

        await new NotesRepository(_paths, validator, diagnostics)
            .SaveAsync(document, "Write restart persistence", CancellationToken.None);
        var reopened = await new NotesRepository(_paths, validator, diagnostics)
            .LoadAsync(document.Id, CancellationToken.None);

        Assert.NotNull(reopened);
        var reopenedBlocks = reopened!.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks).ToArray();
        var reopenedTable = reopenedBlocks.Single(block => block.Id == table.Id).Table!;
        var reopenedCell = reopenedTable.Rows.SelectMany(row => row.Cells).Single(cell => cell.Id == firstCell.Id);
        Assert.Contains("Persistent cell", reopenedCell.Text, StringComparison.Ordinal);
        Assert.Contains("Column 2", reopenedCell.Text, StringComparison.Ordinal);
        Assert.Equal("#FFEAF2FF", reopenedCell.Background);
        Assert.Equal(2, reopenedCell.ColumnSpan);

        var reopenedMedia = reopenedBlocks.Single(block => block.Id == media.Id).Media!;
        Assert.Equal(480, reopenedMedia.Width, 3);
        Assert.Equal(270, reopenedMedia.Height, 3);
        Assert.Equal(25, reopenedMedia.Rotation, 3);
        Assert.Equal(.05, reopenedMedia.CropLeft, 3);
        Assert.Equal(.1, reopenedMedia.CropTop, 3);
        Assert.Equal("Square", reopenedMedia.Wrapping);
        Assert.Equal("Architecture diagram", reopenedMedia.AltText);
        Assert.Equal("System overview", reopenedMedia.Caption);
    }

    [Fact]
    public async Task Present_design_animation_and_media_playback_survive_real_repository_reopen()
    {
        var document = PresentDocument.Create("Present restart");
        var editor = new PresentEditor(document);
        var slide = document.Slides[0];

        Assert.True(editor.ApplyThemePreset("Midnight"));
        Assert.True(editor.SetSlideSizePreset(PresentSlideSizePreset.Standard4By3));
        Assert.True(editor.SetSlideBackgroundColor(slide.Id, "#111827"));
        Assert.True(editor.SetSlideTransition(slide.Id, PresentTransitionKind.Push, .75, PresentEasingKind.EaseOut, PresentMotionDirection.Left));
        Assert.True(editor.SetSlideHidden(slide.Id, true));

        var shape = editor.AddShape(slide.Id);
        editor.SelectElements([shape.Id]);
        Assert.True(editor.SetSelectedAlternativeText("Persistent visual callout"));
        var animationIds = editor.AddAnimationToSelection(PresentAnimationEffect.Fly, PresentAnimationTrigger.OnClick, .5, PresentMotionDirection.Up);
        Assert.Single(animationIds);

        var media = editor.AddMedia(slide.Id, "asset-video", "video/mp4", "Demo recording");
        editor.SelectElements([media.Id]);
        Assert.True(editor.SetSelectedAlternativeText("Persistent demo recording"));
        Assert.True(editor.SetSelectedMediaPlayback(autoPlay: true, loop: true, startSeconds: 2.5, endSeconds: 18));

        await new PresentRepository(_paths).SaveAsync(document, "Present restart persistence", CancellationToken.None);
        var reopened = await new PresentRepository(_paths).LoadAsync(document.Id, CancellationToken.None);

        Assert.NotNull(reopened);
        Assert.Equal("Midnight", reopened!.Theme.Name);
        Assert.Equal(PresentSlideSizePreset.Standard4By3, reopened.SlideSize.Preset);
        var reopenedSlide = reopened.Slides.Single(value => value.Id == slide.Id);
        Assert.True(reopenedSlide.Hidden);
        Assert.Equal(PresentBackgroundKind.Solid, reopenedSlide.Background.Kind);
        Assert.Equal("#111827", reopenedSlide.Background.Color);
        Assert.Equal(PresentTransitionKind.Push, reopenedSlide.Transition.Kind);
        Assert.Equal(.75, reopenedSlide.Transition.DurationSeconds, 3);
        Assert.Equal(PresentEasingKind.EaseOut, reopenedSlide.Transition.Easing);
        Assert.Equal(PresentMotionDirection.Left, reopenedSlide.Transition.Direction);

        var reopenedShape = reopenedSlide.Elements.Single(value => value.Id == shape.Id);
        Assert.Equal("Persistent visual callout", reopenedShape.AlternativeText);
        var cue = reopenedSlide.Animations.Single(value => value.TargetElementId == shape.Id);
        Assert.Equal(PresentAnimationEffect.Fly, cue.Effect);
        Assert.Equal(PresentAnimationTrigger.OnClick, cue.Trigger);
        Assert.Equal(PresentMotionDirection.Up, cue.Direction);

        var reopenedMedia = reopenedSlide.Elements.Single(value => value.Id == media.Id);
        Assert.Equal("Persistent demo recording", reopenedMedia.AlternativeText);
        Assert.True(reopenedMedia.Media.AutoPlay);
        Assert.True(reopenedMedia.Media.Loop);
        Assert.Equal(2.5, reopenedMedia.Media.StartSeconds, 3);
        Assert.Equal(18, reopenedMedia.Media.EndSeconds);
    }

    public void Dispose() => _paths.Dispose();

    private sealed class WorkspaceTestPaths : IAppPaths, IDisposable
    {
        public WorkspaceTestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-document-workspace-persistence-" + Guid.NewGuid().ToString("N"));
            DatabasePath = Path.Combine(DataDirectory, "haven.db");
            BrowserProfileDirectory = Path.Combine(DataDirectory, "browser");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LegacyStatePath = Path.Combine(DataDirectory, "legacy.json");
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(LogsDirectory);
        }

        public string DataDirectory { get; }
        public string DatabasePath { get; }
        public string BrowserProfileDirectory { get; }
        public string AttachmentsDirectory { get; }
        public string LogsDirectory { get; }
        public string LegacyStatePath { get; }

        public void Dispose()
        {
            try { if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
