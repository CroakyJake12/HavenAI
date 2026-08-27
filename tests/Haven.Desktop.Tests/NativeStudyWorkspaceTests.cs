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
    public void Subject_workspace_exposes_native_learning_tabs_and_rag_topic_action()
    {
        var now = DateTimeOffset.UtcNow;
        var subject = new ContainerDefinition(Guid.NewGuid(), HavenMode.Study, "Maths", null, "A-Level Maths", string.Empty, now, now);
        var lesson = new Lesson(Guid.NewGuid(), subject.Id, "Pure", "Algebra", "{}", 0, now, now);
        using var scene = new StudySubjectScene();

        scene.Render(subject, [lesson], [], [], [], (0, 0, 0), (0, 1, 0), now, null);

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

    [Fact]
    public void Subject_workspace_live_lesson_controls_follow_running_and_paused_state()
    {
        var now = DateTimeOffset.UtcNow;
        var subject = new ContainerDefinition(Guid.NewGuid(), HavenMode.Study, "Maths", null, "A-Level Maths", string.Empty, now, now);
        var lesson = new Lesson(Guid.NewGuid(), subject.Id, "Pure", "Algebra", "{}", 0, now, now);
        using var scene = new StudySubjectScene();
        var starts = 0;
        var pauses = 0;
        var resumes = 0;
        var stops = 0;
        scene.StartSessionRequested += (_, _) => starts++;
        scene.PauseSessionRequested += (_, _) => pauses++;
        scene.ResumeSessionRequested += (_, _) => resumes++;
        scene.StopSessionRequested += (_, _) => stops++;

        scene.Render(subject, [lesson], [], [], [], (0, 0, 0), (0, 1, 0), now, null);
        Invoke(FindButton(scene, "Start Live Lesson"));
        Assert.Equal(1, starts);

        var running = new StudyLiveSessionState(now.AddSeconds(-65), now.AddSeconds(-65), 0, false);
        scene.SetLiveSessionState(running, now);
        Assert.Contains(Texts(scene), text => text.Content.Contains("Elapsed 01:05", StringComparison.Ordinal));
        Invoke(FindButton(scene, "Pause"));
        Invoke(FindButton(scene, "Stop & Save"));
        Assert.Equal(1, pauses);
        Assert.Equal(1, stops);

        var paused = running with { AccumulatedSeconds = 65, ResumedAt = null, IsPaused = true };
        scene.SetLiveSessionState(paused, now);
        Assert.Contains(Texts(scene), text => text.Content == "Paused");
        Invoke(FindButton(scene, "Resume"));
        Assert.Equal(1, resumes);
    }

    [Fact]
    public void Live_lesson_metadata_round_trips_active_state_and_completes_into_session_history()
    {
        var now = DateTimeOffset.UtcNow;
        var subjectId = Guid.NewGuid();
        var lesson = new Lesson(Guid.NewGuid(), subjectId, "Pure", "Algebra", "{}", 0, now, now);
        var live = new StudyLiveSessionState(now.AddMinutes(-2), now.AddMinutes(-1), 60, false);

        var persisted = StudyLessonMetadata.WithLiveSession(lesson, live, now);
        var restored = StudyLessonMetadata.ReadLiveSession(persisted);

        Assert.NotNull(restored);
        Assert.Equal(live.StartedAt, restored!.StartedAt);
        Assert.False(restored.IsPaused);
        Assert.True(restored.ElapsedSeconds(now) >= 120);

        var completed = StudyLessonMetadata.CompleteLiveSession(persisted, restored, now);
        Assert.Null(StudyLessonMetadata.ReadLiveSession(completed));
        Assert.Contains(StudyLessonMetadata.Read(completed).Sessions, session => session.Minutes >= 2);
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