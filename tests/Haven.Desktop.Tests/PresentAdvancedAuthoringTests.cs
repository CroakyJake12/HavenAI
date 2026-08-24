using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Views.Pages.Present;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class PresentAdvancedAuthoringTests
{
    [Fact]
    public void Present_editor_design_transition_animation_and_media_settings_are_real_undoable_state()
    {
        var document = PresentDocument.Create("Advanced deck");
        var editor = new PresentEditor(document);
        var slideId = document.Slides[0].Id;

        Assert.True(editor.ApplyThemePreset("Midnight"));
        Assert.Equal("Midnight", document.Theme.Name);
        Assert.True(editor.SetSlideSizePreset(PresentSlideSizePreset.Standard4By3));
        Assert.Equal(PresentSlideSizePreset.Standard4By3, document.SlideSize.Preset);

        var slide = document.Slides[0];
        slide.LayoutId = null;
        var layout = Assert.Single(document.Layouts.Take(1));
        Assert.True(editor.SetSlideLayout(slideId, layout.Id));
        Assert.Equal(layout.Id, slide.LayoutId);
        Assert.True(editor.SetSlideBackgroundColor(slideId, "#111827"));
        Assert.Equal(PresentBackgroundKind.Solid, slide.Background.Kind);
        Assert.True(editor.SetSlideTransition(slideId, PresentTransitionKind.Push, .75, PresentEasingKind.EaseOut, PresentMotionDirection.Left));
        Assert.Equal(PresentTransitionKind.Push, slide.Transition.Kind);
        Assert.Equal(.75, slide.Transition.DurationSeconds, 3);

        var shape = editor.AddShape(slideId);
        editor.SelectElements([shape.Id]);
        var cues = editor.AddAnimationToSelection(PresentAnimationEffect.Fly, PresentAnimationTrigger.OnClick, .5, PresentMotionDirection.Up);
        Assert.Single(cues);
        Assert.Equal(shape.Id, slide.Animations.Single().TargetElementId);
        Assert.True(editor.SetSelectedAlternativeText("Diagram callout"));
        Assert.Equal("Diagram callout", shape.AlternativeText);

        var media = editor.AddMedia(slideId, "asset-video", "video/mp4", "Demo clip");
        editor.SelectElements([media.Id]);
        Assert.True(editor.SetSelectedMediaPlayback(autoPlay: true, loop: true, startSeconds: 2.5, endSeconds: 18));
        Assert.True(media.Media.AutoPlay);
        Assert.True(media.Media.Loop);
        Assert.Equal(2.5, media.Media.StartSeconds, 3);
        Assert.Equal(18, media.Media.EndSeconds);

        Assert.True(editor.SetSlideHidden(slideId, true));
        Assert.True(document.Slides[0].Hidden);
        Assert.True(editor.Undo());
        Assert.False(editor.Document.Slides.Single(value => value.Id == slideId).Hidden);
    }

    [AvaloniaFact]
    public async Task Present_advanced_inspector_changes_theme_transition_hidden_state_and_adds_animation()
    {
        var document = PresentDocument.Create("Inspector deck");
        var body = document.Slides[0].GetOrCreateBodyText();
        body.Text = "Animate this";
        using var page = new PresentPage(new HavenEventBus(), new FakePresentRepository(document), new FakePresentExporter());
        await page.InitializeAsync();
        var window = new Window { Width = 1400, Height = 920, Content = page };
        try
        {
            window.Show();
            window.UpdateLayout();

            page.Route.ThemeSelect.SelectedIndex = Array.IndexOf(page.Route.ThemeSelect.Items.ToArray(), "Midnight");
            Assert.Equal("Midnight", page.Document!.Theme.Name);

            page.Route.TransitionSelect.SelectedIndex = (int)PresentTransitionKind.Fade;
            Assert.Equal(PresentTransitionKind.Fade, page.Document.Slides[0].Transition.Kind);
            page.Route.HiddenSlideToggle.IsChecked = true;
            Assert.True(page.Document.Slides[0].Hidden);
            page.Route.HiddenSlideToggle.IsChecked = false;

            page.Editor!.SelectElements([body.Id]);
            page.Route.SetPhase2Document(page.Document, 0, page.Editor.Selection.ElementIds, page.Editor.CanUndo, page.Editor.CanRedo);
            Assert.Equal(HavenAccessibleRole.Image, page.Route.SlideCanvas.Accessibility.Role);
            Assert.True(page.Route.SlideCanvas.Accessibility.Focusable);
            Assert.Equal("Editable presentation slide canvas", page.Route.SlideCanvas.Accessibility.AccessibleName);
            Assert.False(string.IsNullOrWhiteSpace(page.Route.SlideCanvas.Accessibility.Description));
            var slideRouter = new HavenInputRouter(page.SceneRoot);
            slideRouter.Focus(page.Route.SlideCanvas);
            Assert.True(slideRouter.KeyDown(HavenKey.Enter, new HavenInputModifiers()));
            Assert.True(slideRouter.TextInput(" accessible"));
            Assert.True(slideRouter.KeyDown(HavenKey.Enter, new HavenInputModifiers(Control: true)));
            Assert.Contains(" accessible", page.Document.Slides[0].Elements.Single(element => element.Id == body.Id).Text, StringComparison.Ordinal);
            page.Route.AnimationEffectSelect.SelectedIndex = (int)PresentAnimationEffect.Appear;
            page.Route.AnimationTriggerSelect.SelectedIndex = (int)PresentAnimationTrigger.OnClick;
            window.UpdateLayout();
            page.Route.InspectorPane.ScrollY = page.Route.InspectorPane.MaxScrollY;
            window.UpdateLayout();
            Click(new HavenInputRouter(page.SceneRoot), page.Route.AddAnimationButton);
            Assert.Contains(page.Document.Slides[0].Animations, cue => cue.TargetElementId == body.Id && cue.Effect == PresentAnimationEffect.Appear);
            Assert.Equal(HavenAccessibleRole.List, page.Route.ThemeSelect.Accessibility.Role);
            Assert.False(string.IsNullOrWhiteSpace(page.Route.ThemeSelect.Accessibility.AccessibleName));
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Present_mode_opens_real_presenter_reveals_animation_then_advances_and_exits()
    {
        var document = PresentDocument.Create("Playback deck");
        var first = document.Slides[0];
        first.Title = "Opening";
        first.SpeakerNotes = "Explain the evidence";
        var body = first.GetOrCreateBodyText();
        body.Text = "Animated point";
        first.Animations.Add(new PresentAnimationCue
        {
            TargetElementId = body.Id,
            Effect = PresentAnimationEffect.Appear,
            Trigger = PresentAnimationTrigger.OnClick,
            Order = 0
        });
        var second = PresentSlide.Create(1);
        second.Title = "Second slide";
        second.GetOrCreateBodyText().Text = "Follow-up";
        document.Slides.Add(second);
        document.Normalize();

        using var page = new PresentPage(new HavenEventBus(), new FakePresentRepository(document), new FakePresentExporter());
        await page.InitializeAsync();
        var window = new Window { Width = 1400, Height = 920, Content = page };
        try
        {
            window.Show();
            window.UpdateLayout();
            var router = new HavenInputRouter(page.SceneRoot);

            Click(router, page.Route.PresentButton);
            window.UpdateLayout();
            Assert.Equal(HavenVisibility.Visible, page.Route.PresenterHost.GetValue<HavenVisibility>(HavenProperties.Visibility));
            Assert.Contains("Slide 1 of 2", page.Route.PresenterStatus.Content, StringComparison.Ordinal);
            Assert.Contains("reveal 0/1", page.Route.PresenterStatus.Content, StringComparison.Ordinal);
            Assert.Equal("Explain the evidence", page.Route.PresenterNotes.Content);
            Assert.DoesNotContain(new HavenSceneRenderer().Render(page.SceneRoot).OfType<HavenTextCommand>(), command => command.Layout.Text == "Animated point");

            Click(router, page.Route.PresenterAdvanceButton);
            window.UpdateLayout();
            Assert.Contains("reveal 1/1", page.Route.PresenterStatus.Content, StringComparison.Ordinal);
            Assert.Contains(new HavenSceneRenderer().Render(page.SceneRoot).OfType<HavenTextCommand>(), command => command.Layout.Text == "Animated point");

            Click(router, page.Route.PresenterAdvanceButton);
            window.UpdateLayout();
            Assert.Contains("Slide 2 of 2", page.Route.PresenterStatus.Content, StringComparison.Ordinal);
            Assert.Contains(new HavenSceneRenderer().Render(page.SceneRoot).OfType<HavenTextCommand>(), command => command.Layout.Text == "Follow-up");

            Click(router, page.Route.PresenterExitButton);
            window.UpdateLayout();
            Assert.Equal(HavenVisibility.Collapsed, page.Route.PresenterHost.GetValue<HavenVisibility>(HavenProperties.Visibility));
            Assert.Equal(HavenVisibility.Visible, page.Route.SlideCanvas.GetValue<HavenVisibility>(HavenProperties.Visibility));
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static void Click(HavenInputRouter router, HavenElement element)
    {
        var point = new HavenPoint(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);
        var hit = router.HitTest(point);
        Assert.True(ReferenceEquals(element, hit), $"Expected pointer hit {element.Name}, hit {hit?.Name ?? "<none>"}. Bounds {element.Bounds}.");
        router.PointerPressed(point);
        Assert.True(router.PointerReleased(point));
    }

    private sealed class FakePresentRepository(params PresentDocument[] documents) : IPresentRepository
    {
        private readonly List<PresentDocument> _documents = [.. documents];
        public Task<IReadOnlyList<PresentDocumentSummary>> ListAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<PresentDocumentSummary> result = _documents.Select(value => new PresentDocumentSummary(value.Id, value.Title, value.UpdatedAt, value.Version, value.Slides.Count, false)).ToArray();
            return Task.FromResult(result);
        }
        public Task<PresentDocument?> LoadAsync(Guid documentId, CancellationToken cancellationToken) => Task.FromResult(_documents.FirstOrDefault(value => value.Id == documentId));
        public Task<PresentSaveResult> SaveAsync(PresentDocument document, string reason, CancellationToken cancellationToken)
        {
            var index = _documents.FindIndex(value => value.Id == document.Id);
            if (index < 0) _documents.Add(document); else _documents[index] = document;
            document.Version++;
            var root = Path.Combine(Path.GetTempPath(), "present-advanced-fake");
            return Task.FromResult(new PresentSaveResult(document.Id, document.Version, DateTimeOffset.UtcNow, Path.Combine(root, "current.json"), Path.Combine(root, "previous.json")));
        }
        public Task DeleteAsync(Guid documentId, CancellationToken cancellationToken) { _documents.RemoveAll(value => value.Id == documentId); return Task.CompletedTask; }
    }

    private sealed class FakePresentExporter : IPresentExportService
    {
        public IReadOnlyList<string> ExportExtensions { get; } = [".pptx"];
        public Task<string> ExportAsync(PresentDocument document, string destinationPath, CancellationToken cancellationToken) => Task.FromResult(destinationPath);
    }
}
