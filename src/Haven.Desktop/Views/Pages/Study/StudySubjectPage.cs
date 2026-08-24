using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.ViewModels;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenContainer = Haven.UI.Components.Container;
using HavenPage = Haven.UI.Components.Page;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Study;

public sealed class StudySubjectPage : UserControl, IActivatablePage, IDisposable
{
    private readonly IContainerRepository _containers;
    private readonly IContainerResourceRepository _resources;
    private readonly IConversationRepository _conversations;
    private readonly IStudyPlannerService _studyPlanner;
    private readonly Func<Task> _backToStudy;
    private readonly Func<Lesson?, string?, Task> _newChat;
    private readonly Func<Conversation, Task> _openConversation;
    private readonly Action _openPlan;
    private readonly StudySubjectScene _scene;
    private CancellationTokenSource? _refreshCancellation;
    private ContainerDefinition _subject;
    private IReadOnlyList<Lesson> _lessons = [];
    private IReadOnlyList<ContainerResource> _subjectResources = [];
    private IReadOnlyList<PlannerStudyAssignment> _assignments = [];
    private IReadOnlyList<Conversation> _recentChats = [];
    private DateTimeOffset? _studySessionStartedAt;
    private bool _disposed;

    public StudySubjectPage(
        ContainerDefinition subject,
        IContainerRepository containers,
        IContainerResourceRepository resources,
        IConversationRepository conversations,
        IStudyPlannerService studyPlanner,
        Func<Task> backToStudy,
        Func<Lesson?, string?, Task> newChat,
        Func<Conversation, Task> openConversation,
        Action openPlan)
    {
        _subject = subject ?? throw new ArgumentNullException(nameof(subject));
        _containers = containers ?? throw new ArgumentNullException(nameof(containers));
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        _conversations = conversations ?? throw new ArgumentNullException(nameof(conversations));
        _studyPlanner = studyPlanner ?? throw new ArgumentNullException(nameof(studyPlanner));
        _backToStudy = backToStudy ?? throw new ArgumentNullException(nameof(backToStudy));
        _newChat = newChat ?? throw new ArgumentNullException(nameof(newChat));
        _openConversation = openConversation ?? throw new ArgumentNullException(nameof(openConversation));
        _openPlan = openPlan ?? throw new ArgumentNullException(nameof(openPlan));

        _scene = new StudySubjectScene();
        Scene = new HavenSceneControl { Root = _scene.Root };
        Content = Scene;
        AutomationProperties.SetAutomationId(this, "HavenStudySubjectPage");
        AutomationProperties.SetName(this, $"{subject.Name} Study workspace");

        _scene.BackRequested += OnBackRequested;
        _scene.PlanRequested += (_, _) => _openPlan();
        _scene.NewChatRequested += OnNewChatRequested;
        _scene.LessonChatRequested += OnLessonChatRequested;
        _scene.ActivityRequested += OnActivityRequested;
        _scene.RagChangeRequested += OnRagChangeRequested;
        _scene.AddTopicRequested += OnAddTopicRequested;
        _scene.AddResourceRequested += OnAddResourceRequested;
        _scene.RemoveResourceRequested += OnRemoveResourceRequested;
        _scene.SaveSubjectRequested += OnSaveSubjectRequested;
        _scene.StartSessionRequested += OnStartSessionRequested;
        _scene.StopSessionRequested += OnStopSessionRequested;
        _scene.OpenConversationRequested += OnOpenConversationRequested;
        _scene.GeneratePaperRequested += OnGeneratePaperRequested;
    }

    public HavenSceneControl Scene { get; }

    public Task ActivateAsync(CancellationToken cancellationToken) => RefreshAsync(cancellationToken);
    public void Deactivate() => Interlocked.Exchange(ref _refreshCancellation, null)?.Cancel();

    internal Task RefreshNowAsync(CancellationToken cancellationToken = default) => RefreshAsync(cancellationToken);

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (_disposed) return;
        var refresh = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var previous = Interlocked.Exchange(ref _refreshCancellation, refresh);
        previous?.Cancel();
        var token = refresh.Token;

        try
        {
            var subjectsTask = _containers.GetByModeAsync(HavenMode.Study, token);
            var lessonsTask = _containers.GetLessonsAsync(_subject.Id, token);
            var resourcesTask = _resources.GetByContainerAsync(_subject.Id, token);
            var assignmentsTask = _studyPlanner.GetAssignmentsAsync(_subject.Id, true, token);
            var chatsTask = _conversations.GetRecentAsync(HavenMode.Study, 500, token);
            await Task.WhenAll(subjectsTask, lessonsTask, resourcesTask, assignmentsTask, chatsTask).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            var current = subjectsTask.Result.FirstOrDefault(item => item.Id == _subject.Id && !item.IsArchived);
            if (current is not null) _subject = current;
            _lessons = lessonsTask.Result.OrderBy(item => item.SortOrder).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            _subjectResources = resourcesTask.Result.OrderByDescending(item => item.CreatedAt).ToArray();
            _assignments = assignmentsTask.Result;
            _recentChats = chatsTask.Result
                .Where(item => item.ContainerId == _subject.Id && item.Kind == ConversationKind.LessonChat && !item.IsArchived)
                .OrderByDescending(item => item.UpdatedAt)
                .Take(20)
                .ToArray();

            var now = DateTimeOffset.Now;
            var minutes = StudyLessonMetadata.StudyMinutes(_lessons, now);
            var completedAssignments = _assignments.Count(item => item.Task.Status == PlannerTaskStatus.Completed);
            var level = StudyLessonMetadata.LearningLevel(_lessons, completedAssignments);

            await Dispatcher.UIThread.InvokeAsync(() =>
                _scene.Render(_subject, _lessons, _subjectResources, _assignments, _recentChats, minutes, level, now, _studySessionStartedAt is not null));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => _scene.SetStatus($"Subject could not refresh: {ex.Message}"));
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _refreshCancellation, null, refresh), refresh))
                refresh.Dispose();
            else
                refresh.Dispose();
        }
    }

    private async void OnBackRequested(object? sender, EventArgs e)
    {
        try { await _backToStudy(); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        { _scene.SetStatus($"Could not return to Study: {ex.Message}"); }
    }

    private async void OnNewChatRequested(object? sender, EventArgs e)
    {
        var lesson = DefaultLesson();
        if (lesson is null)
        {
            _scene.SetStatus("Add a topic before starting a subject chat.");
            return;
        }

        try { await _newChat(lesson, null); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        { _scene.SetStatus($"Could not start chat: {ex.Message}"); }
    }

    private async void OnLessonChatRequested(object? sender, Guid lessonId)
    {
        var lesson = _lessons.FirstOrDefault(item => item.Id == lessonId);
        if (lesson is null) return;
        try { await _newChat(lesson, null); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        { _scene.SetStatus($"Could not start topic chat: {ex.Message}"); }
    }

    private async void OnActivityRequested(object? sender, StudyActivityRequest request)
    {
        var lesson = _lessons.FirstOrDefault(item => item.Id == request.LessonId);
        if (lesson is null) return;
        var prompt = BuildActivityPrompt(lesson, request.Kind);
        try { await _newChat(lesson, prompt); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        { _scene.SetStatus($"Could not start study activity: {ex.Message}"); }
    }

    private string BuildActivityPrompt(Lesson lesson, StudyActivityKind kind)
    {
        var context = $"Subject: {_subject.Name}. Topic: {lesson.Name}. Topic group: {lesson.TopicGroup}. Use attached subject resources and Study context where relevant.";        return kind switch
        {
            StudyActivityKind.Flashcards => $"{context} Create an interactive flashcard lesson. Use concise front/back cards covering key facts, definitions, methods and examples. Render real flip-card or flashcard components when supported, not a long prose list. Include enough cards for meaningful retrieval practice and let the learner work through them.",
            StudyActivityKind.Quiz => $"{context} Create an interactive quiz for this topic. Mix question types where useful, use quiz or knowledge-check components when supported, collect answers before revealing explanations, then give targeted feedback and a short summary of weak areas.",
            _ => $"{context} Run a short interactive knowledge check. Ask focused questions that test understanding rather than recognition, use interactive form or knowledge-check components when supported, explain mistakes after answers, and finish with a clear next-step recommendation based on performance."
        };
    }
    private async void OnRagChangeRequested(object? sender, Guid lessonId)
    {
        var lesson = _lessons.FirstOrDefault(item => item.Id == lessonId);
        if (lesson is null) return;
        var current = StudyLessonMetadata.Read(lesson);
        var updated = StudyLessonMetadata.WithRag(lesson, StudyLessonMetadata.NextRag(current.Rag), DateTimeOffset.Now);
        try
        {
            await _containers.UpsertLessonAsync(updated, CancellationToken.None);
            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        { _scene.SetStatus($"Could not update RAG rating: {ex.Message}"); }
    }

    private async void OnAddTopicRequested(object? sender, StudyTopicDraft draft)
    {
        var name = draft.Name.Trim();
        if (name.Length == 0) { _scene.SetStatus("Enter a topic name first."); return; }
        var now = DateTimeOffset.Now;
        var lesson = new Lesson(
            Guid.NewGuid(),
            _subject.Id,
            string.IsNullOrWhiteSpace(draft.TopicGroup) ? "General" : draft.TopicGroup.Trim(),
            name,
            "{}",
            _lessons.Count == 0 ? 0 : _lessons.Max(item => item.SortOrder) + 1,
            now,
            now);
        lesson = StudyLessonMetadata.WithPaperMetadata(lesson, draft.Paper, draft.Section, now);

        try
        {
            await _containers.UpsertLessonAsync(lesson, CancellationToken.None);
            _scene.ClearTopicDraft();
            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        { _scene.SetStatus($"Could not add topic: {ex.Message}"); }
    }

    private async void OnAddResourceRequested(object? sender, EventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null)
        {
            _scene.SetStatus("File selection is not available on this platform surface.");
            return;
        }

        try
        {
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = $"Add resources to {_subject.Name}",
                AllowMultiple = true
            });
            var paths = files.Select(item => item.TryGetLocalPath()).OfType<string>().Where(File.Exists).ToArray();
            foreach (var path in paths)
                await _resources.AddAsync(_subject.Id, path, CancellationToken.None);
            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        { _scene.SetStatus($"Could not add resource: {ex.Message}"); }
    }

    private async void OnRemoveResourceRequested(object? sender, Guid resourceId)
    {
        if (_subjectResources.All(item => item.Id != resourceId)) return;
        try
        {
            await _resources.DeleteAsync(resourceId, CancellationToken.None);
            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        { _scene.SetStatus($"Could not remove resource: {ex.Message}"); }
    }

    private async void OnSaveSubjectRequested(object? sender, StudySubjectDraft draft)
    {
        var name = draft.Name.Trim();
        if (name.Length == 0) { _scene.SetStatus("Subject name cannot be empty."); return; }
        var updated = _subject with
        {
            Name = name,
            Context = draft.Context.Trim(),
            Instructions = draft.Instructions.Trim(),
            UpdatedAt = DateTimeOffset.Now
        };

        try
        {
            await _containers.UpsertAsync(updated, CancellationToken.None);
            _subject = updated;
            _scene.SetStatus("Subject saved.");
            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        { _scene.SetStatus($"Could not save subject: {ex.Message}"); }
    }

    private void OnStartSessionRequested(object? sender, EventArgs e)
    {
        if (_studySessionStartedAt is not null) return;
        _studySessionStartedAt = DateTimeOffset.Now;
        _scene.SetSessionState(true);
        _scene.SetStatus("Study session started.");
    }

    private async void OnStopSessionRequested(object? sender, EventArgs e)
    {
        if (_studySessionStartedAt is not { } started) return;
        var lesson = DefaultLesson();
        if (lesson is null)
        {
            _scene.SetStatus("Add a topic before recording study time.");
            return;
        }

        var now = DateTimeOffset.Now;
        var minutes = Math.Max(1, (int)Math.Round((now - started).TotalMinutes));
        try
        {
            await _containers.UpsertLessonAsync(StudyLessonMetadata.AddSession(lesson, started, minutes, now), CancellationToken.None);
            _studySessionStartedAt = null;
            await RefreshAsync(CancellationToken.None);
            _scene.SetStatus($"Recorded {minutes} minute{(minutes == 1 ? string.Empty : "s")} of study.");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        { _scene.SetStatus($"Could not record study session: {ex.Message}"); }
    }

    private async void OnOpenConversationRequested(object? sender, Guid conversationId)
    {
        var conversation = _recentChats.FirstOrDefault(item => item.Id == conversationId);
        if (conversation is null) return;
        try { await _openConversation(conversation); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        { _scene.SetStatus($"Could not open Study chat: {ex.Message}"); }
    }

    private async void OnGeneratePaperRequested(object? sender, StudyPaperRequest request)
    {
        var selected = _lessons.Where(item => request.LessonIds.Contains(item.Id)).ToArray();
        if (selected.Length == 0)
        {
            _scene.SetStatus("Select at least one topic for the paper.");
            return;
        }
        if (request.QuestionCount is < 1 or > 100)
        {
            _scene.SetStatus("Question count must be between 1 and 100.");
            return;
        }

        var metadata = selected.Select(item =>
        {
            var state = StudyLessonMetadata.Read(item);
            var paper = string.IsNullOrWhiteSpace(state.Paper) ? string.Empty : $", paper {state.Paper}";
            var section = string.IsNullOrWhiteSpace(state.Section) ? string.Empty : $", section {state.Section}";
            return $"- {item.Name} ({item.TopicGroup}{paper}{section})";
        });

        var prompt = $"""
            Build a {request.QuestionCount}-question practice paper for {_subject.Name}.
            Use these selected curriculum topics:
            {string.Join(Environment.NewLine, metadata)}

            Use the subject's attached resources and Study context where relevant. Make this a structured assessment, not a generic chat answer. Include marks for each question, a total mark, and a separate mark scheme after the questions. Use interactive knowledge-check / form components where they improve the lesson experience.
            {(string.IsNullOrWhiteSpace(request.Instructions) ? string.Empty : $"Additional instructions: {request.Instructions.Trim()}")}
            """;

        try
        {
            await _newChat(selected[0], prompt);
            _scene.SetStatus("Paper Builder sent the structured paper specification to Study.");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        { _scene.SetStatus($"Could not generate paper: {ex.Message}"); }
    }

    private Lesson? DefaultLesson() =>
        _lessons.FirstOrDefault(item => item.TopicGroup.Equals("General", StringComparison.OrdinalIgnoreCase))
        ?? _lessons.FirstOrDefault();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Deactivate();
        _scene.Dispose();
    }
}

internal sealed record StudyTopicDraft(string Name, string TopicGroup, string Paper, string Section);
internal sealed record StudySubjectDraft(string Name, string Context, string Instructions);
internal sealed record StudyPaperRequest(IReadOnlySet<Guid> LessonIds, int QuestionCount, string Instructions);
internal sealed record StudyActivityRequest(Guid LessonId, StudyActivityKind Kind);
internal enum StudyActivityKind { Flashcards, Quiz, KnowledgeCheck }

internal enum StudySubjectTab { Dashboard, Topics, Resources, Manage, PaperBuilder }

internal sealed class StudySubjectScene : IDisposable
{
    private readonly HavenContainer _content;
    private readonly HavenContainer _nav;
    private readonly HavenText _status;
    private readonly Input _topicName = new() { Placeholder = "Topic name" };
    private readonly Input _topicGroup = new() { Placeholder = "Topic group / curriculum section" };
    private readonly Input _topicPaper = new() { Placeholder = "Paper (optional)" };
    private readonly Input _topicSection = new() { Placeholder = "Section (optional)" };
    private readonly Input _subjectName = new() { Placeholder = "Subject name" };
    private readonly Input _subjectContext = new() { Placeholder = "Subject context / course details", Multiline = true };
    private readonly Input _subjectInstructions = new() { Placeholder = "Study instructions", Multiline = true };
    private readonly Input _paperCount = new() { Placeholder = "Question count" };
    private readonly Input _paperInstructions = new() { Placeholder = "Paper instructions (optional)", Multiline = true };
    private readonly HashSet<Guid> _paperTopics = [];
    private StudySubjectTab _tab = StudySubjectTab.Dashboard;
    private ContainerDefinition? _subject;
    private IReadOnlyList<Lesson> _lessons = [];
    private IReadOnlyList<ContainerResource> _resources = [];
    private IReadOnlyList<PlannerStudyAssignment> _assignments = [];
    private IReadOnlyList<Conversation> _chats = [];
    private (int CurrentWeekMinutes, int WeeklyAverageMinutes, int TotalMinutes) _minutes;
    private (int Points, int Level, int PointsToNext) _level;
    private DateTimeOffset _now;
    private bool _sessionActive;

    public StudySubjectScene()
    {
        Root = new HavenPage { Name = "StudySubjectRoot", Layout = HavenLayout.Grid, Rows = "Auto Auto 1fr Auto" };
        Root.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Background, "Surface");

        var header = new HavenContainer { Layout = HavenLayout.Grid, Columns = "Auto 1fr Auto Auto" };
        header.SetValue(HavenProperties.Row, 0);
        header.SetValue(HavenProperties.Padding, HavenThickness.Parse("18px 26px 10px 26px"));
        header.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        var back = Action("Back to Study", 0, ButtonVariant.Ghost);
        back.Invoked += (_, _) => BackRequested?.Invoke(this, EventArgs.Empty);
        _title = Heading("Subject", 1);
        var plan = Action("Planner", 2, ButtonVariant.Ghost);
        plan.Invoked += (_, _) => PlanRequested?.Invoke(this, EventArgs.Empty);
        var chat = Action("New Chat", 3, ButtonVariant.Primary);
        chat.Invoked += (_, _) => NewChatRequested?.Invoke(this, EventArgs.Empty);
        header.Add(back); header.Add(_title); header.Add(plan); header.Add(chat);
        Root.Add(header);

        _nav = new HavenContainer { Layout = HavenLayout.Horizontal };
        _nav.SetValue(HavenProperties.Row, 1);
        _nav.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        _nav.SetValue(HavenProperties.Padding, HavenThickness.Parse("0px 26px 10px 26px"));
        _nav.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        _nav.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        Root.Add(_nav);

        _content = new HavenContainer { Layout = HavenLayout.Vertical };
        _content.SetValue(HavenProperties.Row, 2);
        _content.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        _content.SetValue(HavenProperties.Padding, HavenThickness.Parse("10px 26px 28px 26px"));
        _content.SetValue(HavenProperties.Gap, HavenLength.Px(12));
        _content.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        Root.Add(_content);

        _status = new HavenText { Content = string.Empty };
        _status.SetValue(HavenProperties.Row, 3);
        _status.SetValue(HavenProperties.Padding, HavenThickness.Parse("0px 26px 10px 26px"));
        _status.SetValue(HavenProperties.Foreground, "TextSecondary");
        _status.SetValue(HavenProperties.FontSize, 11d);
        Root.Add(_status);

        BuildNav();
    }

    private readonly HavenText _title;

    public HavenPage Root { get; }
    public event EventHandler? BackRequested;
    public event EventHandler? PlanRequested;
    public event EventHandler? NewChatRequested;
    public event EventHandler<Guid>? LessonChatRequested;
    public event EventHandler<StudyActivityRequest>? ActivityRequested;
    public event EventHandler<Guid>? RagChangeRequested;
    public event EventHandler<StudyTopicDraft>? AddTopicRequested;
    public event EventHandler? AddResourceRequested;
    public event EventHandler<Guid>? RemoveResourceRequested;
    public event EventHandler<StudySubjectDraft>? SaveSubjectRequested;
    public event EventHandler? StartSessionRequested;
    public event EventHandler? StopSessionRequested;
    public event EventHandler<Guid>? OpenConversationRequested;
    public event EventHandler<StudyPaperRequest>? GeneratePaperRequested;

    public void Render(
        ContainerDefinition subject,
        IReadOnlyList<Lesson> lessons,
        IReadOnlyList<ContainerResource> resources,
        IReadOnlyList<PlannerStudyAssignment> assignments,
        IReadOnlyList<Conversation> chats,
        (int CurrentWeekMinutes, int WeeklyAverageMinutes, int TotalMinutes) minutes,
        (int Points, int Level, int PointsToNext) level,
        DateTimeOffset now,
        bool sessionActive)
    {
        _subject = subject;
        _lessons = lessons;
        _resources = resources;
        _assignments = assignments;
        _chats = chats;
        _minutes = minutes;
        _level = level;
        _now = now;
        _sessionActive = sessionActive;
        _title.Content = subject.Name;
        _subjectName.Text = subject.Name;
        _subjectContext.Text = subject.Context;
        _subjectInstructions.Text = subject.Instructions;
        if (string.IsNullOrWhiteSpace(_paperCount.Text)) _paperCount.Text = "10";
        BuildNav();
        RebuildContent();
    }

    public void SetStatus(string? value)
    {
        _status.Content = value ?? string.Empty;
        _status.SetValue(HavenProperties.Visibility, string.IsNullOrWhiteSpace(value) ? HavenVisibility.Collapsed : HavenVisibility.Visible);
    }

    public void SetSessionState(bool active)
    {
        _sessionActive = active;
        if (_tab == StudySubjectTab.Dashboard) RebuildContent();
    }

    public void ClearTopicDraft()
    {
        _topicName.Text = string.Empty;
        _topicGroup.Text = string.Empty;
        _topicPaper.Text = string.Empty;
        _topicSection.Text = string.Empty;
    }

    private void BuildNav()
    {
        foreach (var child in _nav.Children.ToArray()) _nav.Remove(child);
        AddTab(StudySubjectTab.Dashboard, "Dashboard");
        AddTab(StudySubjectTab.Topics, "Topics");
        AddTab(StudySubjectTab.Resources, "Resources");
        AddTab(StudySubjectTab.Manage, "Manage Subject");
        AddTab(StudySubjectTab.PaperBuilder, "Paper Builder");
    }

    private void AddTab(StudySubjectTab tab, string text)
    {
        var button = new HavenButton { Content = text, Variant = _tab == tab ? ButtonVariant.Secondary : ButtonVariant.Ghost };
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(38));
        button.Invoked += (_, _) =>
        {
            _tab = tab;
            BuildNav();
            RebuildContent();
        };
        _nav.Add(button);
    }

    private void RebuildContent()
    {
        foreach (var child in _content.Children.ToArray()) _content.Remove(child);
        if (_subject is null) return;
        switch (_tab)
        {
            case StudySubjectTab.Dashboard: BuildDashboard(); break;
            case StudySubjectTab.Topics: BuildTopics(); break;
            case StudySubjectTab.Resources: BuildResources(); break;
            case StudySubjectTab.Manage: BuildManage(); break;
            case StudySubjectTab.PaperBuilder: BuildPaperBuilder(); break;
        }
    }

    private void BuildDashboard()
    {
        _content.Add(Heading("Welcome Back"));
        var stats = new HavenContainer { Layout = HavenLayout.Wrap };
        stats.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        stats.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        var average = _minutes.WeeklyAverageMinutes <= 0
            ? "No previous weekly average yet"
            : $"{Math.Round(_minutes.CurrentWeekMinutes * 100d / _minutes.WeeklyAverageMinutes):0}% of weekly average";
        stats.Add(Stat(FormatMinutes(_minutes.CurrentWeekMinutes), "Spent Studying", average));
        stats.Add(Stat($"Level {_level.Level}", $"{_subject!.Name} Learning Level", $"{_level.PointsToNext} points to next level"));
        var remaining = _assignments.Count(item => item.Task.Status != PlannerTaskStatus.Completed);
        var total = Math.Max(1, _assignments.Count);
        var completePct = _assignments.Count == 0 ? 0 : (int)Math.Round(_assignments.Count(item => item.Task.Status == PlannerTaskStatus.Completed) * 100d / total);
        stats.Add(Stat(remaining.ToString(), "Remaining Assignments", $"This week: {completePct}% complete"));
        _content.Add(stats);

        var session = new HavenButton
        {
            Content = _sessionActive ? "End Study Session" : "Start Study Session",
            Variant = _sessionActive ? ButtonVariant.Secondary : ButtonVariant.Primary
        };
        session.SetValue(HavenProperties.MaxWidth, HavenLength.Px(240));
        session.Invoked += (_, _) =>
        {
            if (_sessionActive) StopSessionRequested?.Invoke(this, EventArgs.Empty);
            else StartSessionRequested?.Invoke(this, EventArgs.Empty);
        };
        _content.Add(session);

        _content.Add(Heading("Outstanding Assignments"));
        var outstanding = _assignments.Where(item => item.Task.Status != PlannerTaskStatus.Completed)
            .OrderBy(item => item.Task.DueAt ?? DateTimeOffset.MaxValue).ToArray();
        if (outstanding.Length == 0) _content.Add(Muted("No outstanding assignments."));
        foreach (var assignment in outstanding)
        {
            var due = assignment.Task.DueAt is { } d ? $"Due {FriendlyDue(d, _now)}" : "No due date";
            _content.Add(Info(assignment.Task.Title, due));
        }

        _content.Add(Heading("Continue with These Topics"));
        var recommendations = _lessons.Select(item => (lesson: item, state: StudyLessonMetadata.Read(item)))
            .OrderBy(item => item.state.Rag == "red" ? 0 : item.state.LastReviewedAt is { } r && r < _now.AddDays(-7) ? 1 : item.state.Rag == "none" ? 2 : 3)
            .ThenBy(item => item.lesson.SortOrder)
            .ToArray();
        if (recommendations.Length == 0) _content.Add(Muted("Add topics to get contextual Study recommendations."));
        foreach (var item in recommendations)
        {
            var card = Card();
            card.Add(new HavenText { Content = item.lesson.Name });
            card.Add(Muted(StudyLessonMetadata.RecommendationReason(item.state, _now)));
            var study = new HavenButton { Content = "Study topic", Variant = ButtonVariant.Tertiary };
            var id = item.lesson.Id;
            study.Invoked += (_, _) => LessonChatRequested?.Invoke(this, id);
            card.Add(study);
            _content.Add(card);
        }

        _content.Add(Heading("Subject Chats"));
        if (_chats.Count == 0) _content.Add(Muted("No subject chats yet."));
        foreach (var chat in _chats)
        {
            var open = new HavenButton { Content = $"{chat.Title}\nUpdated {chat.UpdatedAt.LocalDateTime:g}", Variant = ButtonVariant.Navigation };
            var id = chat.Id;
            open.Invoked += (_, _) => OpenConversationRequested?.Invoke(this, id);
            open.SetValue(HavenProperties.Width, HavenLength.Percent(100));
            _content.Add(open);
        }
    }

    private void BuildTopics()
    {
        _content.Add(Heading($"{_subject!.Name} Topics"));
        if (_lessons.Count == 0) _content.Add(Muted("No topics yet."));
        foreach (var lesson in _lessons)
        {
            var state = StudyLessonMetadata.Read(lesson);
            var card = Card();
            var paper = string.IsNullOrWhiteSpace(state.Paper) ? string.Empty : $" • Paper {state.Paper}";
            var section = string.IsNullOrWhiteSpace(state.Section) ? string.Empty : $" • {state.Section}";
            card.Add(new HavenText { Content = lesson.Name });
            card.Add(Muted($"{lesson.TopicGroup}{paper}{section} • {state.ProgressPercent}% proficiency"));
            var actions = new HavenContainer { Layout = HavenLayout.Horizontal };
            actions.SetValue(HavenProperties.Gap, HavenLength.Px(6));
            var rag = new HavenButton { Content = $"RAG: {StudyLessonMetadata.RagLabel(state.Rag)}", Variant = ButtonVariant.Tertiary };
            var study = new HavenButton { Content = "Study", Variant = ButtonVariant.Primary };
            var flashcards = new HavenButton { Content = "Flashcards", Variant = ButtonVariant.Tertiary };
            var quiz = new HavenButton { Content = "Quiz", Variant = ButtonVariant.Tertiary };
            var check = new HavenButton { Content = "Knowledge check", Variant = ButtonVariant.Tertiary };
            var id = lesson.Id;
            rag.Invoked += (_, _) => RagChangeRequested?.Invoke(this, id);
            study.Invoked += (_, _) => LessonChatRequested?.Invoke(this, id);
            flashcards.Invoked += (_, _) => ActivityRequested?.Invoke(this, new StudyActivityRequest(id, StudyActivityKind.Flashcards));
            quiz.Invoked += (_, _) => ActivityRequested?.Invoke(this, new StudyActivityRequest(id, StudyActivityKind.Quiz));
            check.Invoked += (_, _) => ActivityRequested?.Invoke(this, new StudyActivityRequest(id, StudyActivityKind.KnowledgeCheck));
            actions.Add(rag); actions.Add(study); actions.Add(flashcards); actions.Add(quiz); actions.Add(check);
            card.Add(actions);
            _content.Add(card);
        }

        var add = Card();
        add.Add(Heading("Add Topic"));
        add.Add(_topicName); add.Add(_topicGroup); add.Add(_topicPaper); add.Add(_topicSection);
        foreach (var input in new[] { _topicName, _topicGroup, _topicPaper, _topicSection })
        {
            input.SetValue(HavenProperties.Width, HavenLength.Percent(100));
            input.SetValue(HavenProperties.MinHeight, HavenLength.Px(40));
        }
        var button = new HavenButton { Content = "Add Topic", Variant = ButtonVariant.Primary };
        button.SetValue(HavenProperties.MaxWidth, HavenLength.Px(220));
        button.Invoked += (_, _) => AddTopicRequested?.Invoke(this,
            new StudyTopicDraft(_topicName.Text, _topicGroup.Text, _topicPaper.Text, _topicSection.Text));
        add.Add(button);
        _content.Add(add);
    }

    private void BuildResources()
    {
        var top = new HavenContainer { Layout = HavenLayout.Grid, Columns = "1fr Auto" };
        top.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        top.Add(Heading("Resources", 0));
        var add = Action("Add Resource", 1, ButtonVariant.Primary);
        add.Invoked += (_, _) => AddResourceRequested?.Invoke(this, EventArgs.Empty);
        top.Add(add);
        _content.Add(top);
        _content.Add(Muted("Resources are attached to this subject and available to Study chats and Paper Builder."));
        if (_resources.Count == 0) _content.Add(Muted("No resources attached."));
        foreach (var resource in _resources)
        {
            var row = Card();
            row.Add(new HavenText { Content = resource.Name });
            row.Add(Muted($"{resource.MediaType} • {FormatBytes(resource.SizeBytes)}"));
            var remove = new HavenButton { Content = "Remove", Variant = ButtonVariant.Danger };
            remove.SetValue(HavenProperties.MaxWidth, HavenLength.Px(150));
            var id = resource.Id;
            remove.Invoked += (_, _) => RemoveResourceRequested?.Invoke(this, id);
            row.Add(remove);
            _content.Add(row);
        }
    }

    private void BuildManage()
    {
        _content.Add(Heading("Manage Subject"));
        _content.Add(Muted("Edit the Study identity and context without leaving the subject workspace."));
        foreach (var input in new[] { _subjectName, _subjectContext, _subjectInstructions })
        {
            input.SetValue(HavenProperties.Width, HavenLength.Percent(100));
            input.SetValue(HavenProperties.MinHeight, HavenLength.Px(input.Multiline ? 100 : 42));
            _content.Add(input);
        }
        var save = new HavenButton { Content = "Save Subject", Variant = ButtonVariant.Primary };
        save.SetValue(HavenProperties.MaxWidth, HavenLength.Px(220));
        save.Invoked += (_, _) => SaveSubjectRequested?.Invoke(this,
            new StudySubjectDraft(_subjectName.Text, _subjectContext.Text, _subjectInstructions.Text));
        _content.Add(save);
    }

    private void BuildPaperBuilder()
    {
        _content.Add(Heading("Paper Builder"));
        _content.Add(Muted("Build a structured practice paper from selected subject topics and resources."));
        _content.Add(Heading("Choose Topics"));
        if (_lessons.Count == 0) _content.Add(Muted("Add topics before building a paper."));
        foreach (var lesson in _lessons)
        {
            var selected = _paperTopics.Contains(lesson.Id);
            var button = new HavenButton
            {
                Content = $"{(selected ? "Selected" : "Select")} • {lesson.Name}",
                Variant = selected ? ButtonVariant.Secondary : ButtonVariant.Navigation
            };
            var id = lesson.Id;
            button.Invoked += (_, _) =>
            {
                if (!_paperTopics.Add(id)) _paperTopics.Remove(id);
                RebuildContent();
            };
            button.SetValue(HavenProperties.Width, HavenLength.Percent(100));
            _content.Add(button);
        }

        _paperCount.SetValue(HavenProperties.Width, HavenLength.Px(220));
        _paperCount.SetValue(HavenProperties.MinHeight, HavenLength.Px(40));
        _paperInstructions.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        _paperInstructions.SetValue(HavenProperties.MinHeight, HavenLength.Px(100));
        _content.Add(_paperCount);
        _content.Add(_paperInstructions);

        var generate = new HavenButton { Content = "Build Practice Paper", Variant = ButtonVariant.Primary };
        generate.SetValue(HavenProperties.MaxWidth, HavenLength.Px(260));
        generate.SetValue(HavenProperties.Enabled, _paperTopics.Count > 0 && _lessons.Count > 0);
        generate.Invoked += (_, _) =>
        {
            if (!int.TryParse(_paperCount.Text.Trim(), out var count)) count = 0;
            GeneratePaperRequested?.Invoke(this, new StudyPaperRequest(new HashSet<Guid>(_paperTopics), count, _paperInstructions.Text));
        };
        _content.Add(generate);
    }

    private static HavenContainer Card()
    {
        var card = new HavenContainer { Layout = HavenLayout.Vertical };
        card.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        card.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(14)));
        card.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        card.SetValue(HavenProperties.Background, "SurfaceRaised");
        card.SetValue(HavenProperties.BorderColor, "Border");
        card.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(14)));
        return card;
    }

    private static HavenContainer Stat(string value, string label, string detail)
    {
        var card = Card();
        card.SetValue(HavenProperties.Width, HavenLength.Px(245));
        card.Add(Heading(value));
        card.Add(new HavenText { Content = label });
        card.Add(Muted(detail));
        return card;
    }

    private static HavenContainer Info(string title, string detail)
    {
        var card = Card();
        card.Add(new HavenText { Content = title });
        card.Add(Muted(detail));
        return card;
    }

    private static HavenText Heading(string text, int column = -1)
    {
        var heading = new HavenText { Content = text };
        heading.SetValue(HavenProperties.FontSize, 20d);
        heading.SetValue(HavenProperties.FontWeight, 800);
        if (column >= 0) heading.SetValue(HavenProperties.Column, column);
        return heading;
    }

    private static HavenText Muted(string text)
    {
        var value = new HavenText { Content = text };
        value.SetValue(HavenProperties.Foreground, "TextSecondary");
        value.SetValue(HavenProperties.FontSize, 12d);
        return value;
    }

    private static HavenButton Action(string text, int column, ButtonVariant variant)
    {
        var button = new HavenButton { Content = text, Variant = variant };
        button.SetValue(HavenProperties.Column, column);
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(38));
        return button;
    }

    private static string FriendlyDue(DateTimeOffset due, DateTimeOffset now)
    {
        var days = (due.LocalDateTime.Date - now.LocalDateTime.Date).Days;
        return days switch
        {
            < 0 => "overdue",
            0 => "today",
            1 => "tomorrow",
            <= 6 => due.LocalDateTime.ToString("dddd"),
            _ => due.LocalDateTime.ToString("d MMM")
        };
    }

    private static string FormatMinutes(int minutes) =>
        minutes < 60 ? $"{minutes} min" : $"{minutes / 60d:0.#} h";

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024d:0.#} KB";
        return $"{bytes / (1024d * 1024d):0.#} MB";
    }

    public void Dispose() { }
}
