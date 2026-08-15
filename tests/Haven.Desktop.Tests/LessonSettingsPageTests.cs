using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Views.Pages.LessonSettings;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class LessonSettingsPageTests
{
    [AvaloniaFact]
    public void Lesson_settings_route_renders_through_a_single_haven_scene_surface()
    {
        using var page = BuildPage(out _);
        var window = new Window { Width = 960, Height = 720, Content = page };
        try
        {
            window.Show();
            window.UpdateLayout();

            Assert.Same(page.SceneRoot, page.Route.Root);
            Assert.Single(page.SceneHost.Children);
            Assert.Contains(page.Route.Root.DescendantsAndSelf().OfType<Input>(), input => ReferenceEquals(input, page.Route.NameInput));
            Assert.Contains(page.Route.Root.DescendantsAndSelf().OfType<Input>(), input => ReferenceEquals(input, page.Route.TopicGroupInput));
            Assert.Contains(page.Route.Root.DescendantsAndSelf().OfType<Input>(), input => ReferenceEquals(input, page.Route.StructureJsonInput));
            Assert.DoesNotContain(page.Route.Root.DescendantsAndSelf(), element => element is Video or Web);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Lesson_state_loads_into_haven_inputs_and_preserves_accessibility_metadata()
    {
        using var page = BuildPage(out _);
        var window = new Window { Width = 960, Height = 720, Content = page };
        try
        {
            window.Show();
            window.UpdateLayout();

            Assert.Equal("Integers", page.Route.NameHeading.Content);
            Assert.Equal("Integers", page.Route.NameInput.Text);
            Assert.Equal("Arithmetic", page.Route.TopicGroupInput.Text);
            Assert.Equal("{\"sections\":[]}", page.Route.StructureJsonInput.Text);
            Assert.Equal("Lesson name", page.Route.NameInput.Accessibility.AccessibleName);
            Assert.Equal("Topic group", page.Route.TopicGroupInput.Accessibility.AccessibleName);
            Assert.Equal("Lesson structure JSON", page.Route.StructureJsonInput.Accessibility.AccessibleName);
            Assert.Equal(HavenAccessibleRole.Input, page.Route.NameInput.Accessibility.Role);
            Assert.Equal(HavenAccessibleRole.Input, page.Route.StructureJsonInput.Accessibility.Role);
            Assert.Equal("Save lesson", page.Route.SaveButton.Accessibility.AccessibleName);
            Assert.Equal(HavenAccessibleRole.Button, page.Route.SaveButton.Accessibility.Role);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Save_invokes_the_container_repository_with_haven_input_values()
    {
        var container = new RecordingLessonContainerRepository();
        var saved = new TaskCompletionSource();
        using var page = new LessonSettingsPage(new HavenEventBus(), BuildLesson(), container, () => { saved.TrySetResult(); return Task.CompletedTask; });
        var window = new Window { Width = 960, Height = 720, Content = page };
        try
        {
            window.Show();
            window.UpdateLayout();

            page.Route.NameInput.Text = "Fractions";
            page.Route.TopicGroupInput.Text = "Algebra";
            page.Route.StructureJsonInput.Text = "  {\"lessons\":[\"Adding\"]}  ";

            var router = new HavenInputRouter(page.SceneRoot);
            Click(router, page.Route.SaveButton);

            saved.Task.Wait(TimeSpan.FromSeconds(2));

            Assert.NotNull(container.LastSaved);
            Assert.Equal("Fractions", container.LastSaved!.Name);
            Assert.Equal("Algebra", container.LastSaved.TopicGroup);
            Assert.Equal("{\"lessons\":[\"Adding\"]}", container.LastSaved.StructureJson);
            Assert.Equal("Lesson saved.", page.Route.StatusText.Content);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Empty_lesson_name_blocks_save_and_reports_a_required_status()
    {
        var container = new RecordingLessonContainerRepository();
        using var page = new LessonSettingsPage(new HavenEventBus(), BuildLesson(), container, () => Task.CompletedTask);
        var window = new Window { Width = 960, Height = 720, Content = page };
        try
        {
            window.Show();
            window.UpdateLayout();

            page.Route.NameInput.Text = "   ";
            var router = new HavenInputRouter(page.SceneRoot);
            Click(router, page.Route.SaveButton);

            Assert.Null(container.LastSaved);
            Assert.Equal("Lesson name is required.", page.Route.StatusText.Content);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Hovering_the_save_button_morphs_through_a_haven_transition()
    {
        using var page = BuildPage(out _);
        var window = new Window { Width = 960, Height = 720, Content = page };
        try
        {
            window.Show();
            window.UpdateLayout();
            var router = new HavenInputRouter(page.SceneRoot);
            var mid = new HavenPoint(page.Route.SaveButton.Bounds.X + page.Route.SaveButton.Bounds.Width / 2, page.Route.SaveButton.Bounds.Y + page.Route.SaveButton.Bounds.Height / 2);

            router.PointerMoved(mid);

            Assert.True(page.Route.SaveButton.State.HasFlag(HavenElementState.Hover));
            Assert.Equal(ButtonDefaults.HoverTransition, page.Route.SaveButton.GetValue(HavenProperties.Transition));
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Lesson_settings_route_renders_haven_owned_chrome_through_one_surface()
    {
        var captureDirectory = Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(captureDirectory))
        {
            return;
        }

        using var page = BuildPage(out _);
        var window = new Window { Width = 880, Height = 720, Content = page };
        try
        {
            window.Show();
            window.UpdateLayout();

            Directory.CreateDirectory(captureDirectory);
            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            var capturePath = Path.Combine(captureDirectory, "lesson-settings-pass-f.png");
            frame.Save(capturePath);
            Assert.True(new FileInfo(capturePath).Length > 2_000);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static LessonSettingsPage BuildPage(out RecordingLessonContainerRepository container)
    {
        container = new RecordingLessonContainerRepository();
        return new LessonSettingsPage(new HavenEventBus(), BuildLesson(), container, () => Task.CompletedTask);
    }

    private static Lesson BuildLesson() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "Arithmetic",
        "Integers",
        "{\"sections\":[]}",
        0,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);

    private static void Click(HavenInputRouter router, HavenElement element)
    {
        var point = new HavenPoint(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);
        router.PointerPressed(point);
        Assert.True(router.PointerReleased(point));
    }

    private sealed class RecordingLessonContainerRepository : IContainerRepository
    {
        public Lesson? LastSaved { get; private set; }

        public Task UpsertLessonAsync(Lesson lesson, CancellationToken cancellationToken)
        {
            LastSaved = lesson;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ContainerDefinition>> GetByModeAsync(HavenMode mode, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ContainerDefinition>>(Array.Empty<ContainerDefinition>());
        public Task<IReadOnlyList<ContainerDefinition>> GetArchivedByModeAsync(HavenMode mode, int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ContainerDefinition>>(Array.Empty<ContainerDefinition>());
        public Task UpsertAsync(ContainerDefinition item, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Lesson> CreateSubjectAsync(ContainerDefinition subject, CancellationToken cancellationToken) => Task.FromResult<Lesson>(new Lesson(Guid.NewGuid(), subject.Id, string.Empty, "Subject", "{}", 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteAndDetachConversationsAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<Lesson>> GetLessonsAsync(Guid subjectId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Lesson>>(Array.Empty<Lesson>());
        public Task DeleteLessonAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
