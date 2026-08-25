using System.Reflection;
using Haven.Core;
using Haven.Desktop.Views.Pages.Study;
using Haven.Desktop.Views.Shell;
using Haven.UI;
using Haven.UI.Components;
using Button = Haven.UI.Components.Button;
using Text = Haven.UI.Components.Text;

namespace Haven.Desktop.Tests;

public sealed class NativeStudyWorkspaceTests
{
    [Fact]
    public void Study_containers_route_to_native_study_workspace_only()
    {
        var now = DateTimeOffset.UtcNow;
        var study = new ContainerDefinition(Guid.NewGuid(), HavenMode.Study, "Maths", null, "A-Level Maths", string.Empty, now, now);
        var chat = new ContainerDefinition(Guid.NewGuid(), HavenMode.Chat, "General", null, string.Empty, string.Empty, now, now);

        Assert.True(MainView.UsesNativeStudyWorkspace(study));
        Assert.False(MainView.UsesNativeStudyWorkspace(chat));
    }

    [Fact]
    public void Study_home_can_refresh_repeatedly_without_reparenting_subject_input()
    {
        using var scene = new StudyHomeScene();
        var now = DateTimeOffset.UtcNow;
        var lessons = new Dictionary<Guid, IReadOnlyList<Lesson>>();
        var assignments = new Dictionary<Guid, IReadOnlyList<PlannerStudyAssignment>>();

        scene.Render(now, (0, 0, 0), (0, 1, 1000), [], lessons, assignments);
        scene.Render(now, (0, 0, 0), (0, 1, 1000), [], lessons, assignments);

        Assert.Single(scene.Root.DescendantsAndSelf().OfType<Input>(), input => input.Name == "StudySubjectName");
    }

    [Fact]
    public void Subject_workspace_exposes_native_learning_tabs_and_rag_topic_action()
    {
        var now = DateTimeOffset.UtcNow;
        var subject = new ContainerDefinition(Guid.NewGuid(), HavenMode.Study, "Maths", null, "A-Level Maths", string.Empty, now, now);
        var lesson = new Lesson(Guid.NewGuid(), subject.Id, "Pure", "Algebra", "{}", 0, now, now);
        using var scene = new StudySubjectScene();

        scene.Render(subject, [lesson], [], [], [], (0, 0, 0), (0, 1, 0), now, false);

        var navLabels = Buttons(scene).Select(button => button.Content).ToArray();
        Assert.Contains("Dashboard", navLabels);
        Assert.Contains("Topics", navLabels);
        Assert.Contains("Resources", navLabels);
        Assert.Contains("Manage Subject", navLabels);
        Assert.Contains("Paper Builder", navLabels);

        Invoke(FindButton(scene, "Topics"));
        Assert.Contains(Texts(scene), text => text.Content == "Maths Topics");
        Assert.Contains(Texts(scene), text => text.Content == "Algebra");

        Guid? ragLessonId = null;
        scene.RagChangeRequested += (_, id) => ragLessonId = id;
        var rag = Assert.Single(Buttons(scene), button => button.Content.StartsWith("RAG:", StringComparison.Ordinal));
        Invoke(rag);
        Assert.Equal(lesson.Id, ragLessonId);

        var activities = new List<StudyActivityRequest>();
        scene.ActivityRequested += (_, request) => activities.Add(request);
        Invoke(FindButton(scene, "Flashcards"));
        Invoke(FindButton(scene, "Quiz"));
        Invoke(FindButton(scene, "Knowledge check"));
        Assert.Equal(3, activities.Count);
        Assert.All(activities, item => Assert.Equal(lesson.Id, item.LessonId));
        Assert.Equal(StudyActivityKind.Flashcards, activities[0].Kind);
        Assert.Equal(StudyActivityKind.Quiz, activities[1].Kind);
        Assert.Equal(StudyActivityKind.KnowledgeCheck, activities[2].Kind);

        Invoke(FindButton(scene, "Manage Subject"));
        Assert.Contains(Buttons(scene), button => button.Content == "Save Subject");

        Invoke(FindButton(scene, "Paper Builder"));
        Assert.Contains(Buttons(scene), button => button.Content == "Build Practice Paper");
    }

    private static IEnumerable<Button> Buttons(StudySubjectScene scene) =>
        scene.Root.DescendantsAndSelf().OfType<Button>();

    private static IEnumerable<Text> Texts(StudySubjectScene scene) =>
        scene.Root.DescendantsAndSelf().OfType<Text>();

    private static Button FindButton(StudySubjectScene scene, string content) =>
        Assert.Single(Buttons(scene), button => button.Content == content);

    private static void Invoke(Button button)
    {
        var method = typeof(HavenElement).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(button, null);
    }
}