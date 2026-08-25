using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.ViewModels;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;
using HavenPage = Haven.UI.Components.Page;
using HavenContainer = Haven.UI.Components.Container;

namespace Haven.Desktop.Views.Pages.Study;

public sealed class StudyHomePage : UserControl, IActivatablePage, IDisposable
{
    private readonly IContainerRepository _containers;
    private readonly IStudyPlannerService _studyPlanner;
    private readonly Func<ContainerDefinition, Task> _openSubject;
    private readonly Func<Task> _newStudyChat;
    private readonly Action _openPlan;
    private readonly StudyHomeScene _scene;
    private CancellationTokenSource? _refreshCancellation;
    private IReadOnlyList<ContainerDefinition> _subjects = [];
    private IReadOnlyDictionary<Guid, IReadOnlyList<Lesson>> _lessons = new Dictionary<Guid, IReadOnlyList<Lesson>>();
    private IReadOnlyDictionary<Guid, IReadOnlyList<PlannerStudyAssignment>> _assignments = new Dictionary<Guid, IReadOnlyList<PlannerStudyAssignment>>();
    private bool _disposed;

    public StudyHomePage(
        IContainerRepository containers,
        IStudyPlannerService studyPlanner,
        Func<ContainerDefinition, Task> openSubject,
        Func<Task> newStudyChat,
        Action openPlan)
    {
        _containers = containers ?? throw new ArgumentNullException(nameof(containers));
        _studyPlanner = studyPlanner ?? throw new ArgumentNullException(nameof(studyPlanner));
        _openSubject = openSubject ?? throw new ArgumentNullException(nameof(openSubject));
        _newStudyChat = newStudyChat ?? throw new ArgumentNullException(nameof(newStudyChat));
        _openPlan = openPlan ?? throw new ArgumentNullException(nameof(openPlan));
        _scene = new StudyHomeScene();
        Scene = new HavenSceneControl { Root = _scene.Root };
        Content = Scene;
        AutomationProperties.SetAutomationId(this, "HavenStudyHomePage");
        AutomationProperties.SetName(this, "Study");
        _scene.SubjectRequested += OnSubjectRequested;
        _scene.AddSubjectRequested += OnAddSubjectRequested;
        _scene.NewChatRequested += OnNewChatRequested;
        _scene.PlanRequested += OnPlanRequested;
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
            var subjects = await _containers.GetByModeAsync(HavenMode.Study, token).ConfigureAwait(false);
            var active = subjects.Where(item => !item.IsArchived).OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            var lessonTasks = active.ToDictionary(item => item.Id, item => _containers.GetLessonsAsync(item.Id, token));
            var assignmentTasks = active.ToDictionary(item => item.Id, item => _studyPlanner.GetAssignmentsAsync(item.Id, true, token));
            await Task.WhenAll(lessonTasks.Values.Cast<Task>().Concat(assignmentTasks.Values.Cast<Task>())).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            _subjects = active;
            _lessons = lessonTasks.ToDictionary(pair => pair.Key, pair => pair.Value.Result);
            _assignments = assignmentTasks.ToDictionary(pair => pair.Key, pair => pair.Value.Result);

            var allLessons = _lessons.Values.SelectMany(items => items).ToArray();
            var allAssignments = _assignments.Values.SelectMany(items => items).ToArray();
            var now = DateTimeOffset.Now;
            var minutes = StudyLessonMetadata.StudyMinutes(allLessons, now);
            var completed = allAssignments.Count(item => item.Task.Status == PlannerTaskStatus.Completed);
            var level = StudyLessonMetadata.LearningLevel(allLessons, completed);

            await Dispatcher.UIThread.InvokeAsync(() => _scene.Render(
                now,
                minutes,
                level,
                _subjects,
                _lessons,
                _assignments));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => _scene.SetStatus($"Study could not refresh: {exception.Message}"));
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _refreshCancellation, null, refresh), refresh)) refresh.Dispose();
            else refresh.Dispose();
        }
    }

    private async void OnSubjectRequested(object? sender, Guid subjectId)
    {
        var subject = _subjects.FirstOrDefault(item => item.Id == subjectId);
        if (subject is null) return;
        try { await _openSubject(subject); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        { _scene.SetStatus($"Could not open subject: {ex.Message}"); }
    }

    private async void OnAddSubjectRequested(object? sender, string name)
    {
        var clean = name.Trim();
        if (clean.Length == 0) return;
        var now = DateTimeOffset.Now;
        var subject = new ContainerDefinition(
            Guid.NewGuid(),
            HavenMode.Study,
            clean,
            null,
            string.Empty,
            string.Empty,
            now,
            now);
        try
        {
            await _containers.CreateSubjectAsync(subject, CancellationToken.None);
            _scene.SubjectName.Text = string.Empty;
            await RefreshAsync(CancellationToken.None);
            await _openSubject(subject);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        { _scene.SetStatus($"Could not add subject: {ex.Message}"); }
    }

    private async void OnNewChatRequested(object? sender, EventArgs e)
    {
        try { await _newStudyChat(); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        { _scene.SetStatus($"Could not start Study chat: {ex.Message}"); }
    }

    private void OnPlanRequested(object? sender, EventArgs e) => _openPlan();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Deactivate();
        _scene.SubjectRequested -= OnSubjectRequested;
        _scene.AddSubjectRequested -= OnAddSubjectRequested;
        _scene.NewChatRequested -= OnNewChatRequested;
        _scene.PlanRequested -= OnPlanRequested;
        _scene.Dispose();
    }
}

internal sealed class StudyHomeScene : IDisposable
{
    private readonly HavenContainer _content;
    private readonly HavenText _status;

    public StudyHomeScene()
    {
        Root = new HavenPage { Name = "StudyHomeRoot", Layout = HavenLayout.Grid, Rows = "Auto 1fr Auto" };
        Root.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Background, "Transparent");

        var top = new HavenContainer { Layout = HavenLayout.Grid, Columns = "1fr Auto Auto" };
        top.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        top.SetValue(HavenProperties.Padding, HavenThickness.Parse("24px 28px 10px 28px"));
        top.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        top.Add(Heading("Study", 0));
        var plan = Action("Planner", 1, ButtonVariant.Ghost);
        var chat = Action("New Study Chat", 2, ButtonVariant.Tertiary);
        plan.Invoked += (_, _) => PlanRequested?.Invoke(this, EventArgs.Empty);
        chat.Invoked += (_, _) => NewChatRequested?.Invoke(this, EventArgs.Empty);
        top.Add(plan);
        top.Add(chat);
        top.SetValue(HavenProperties.Row, 0);
        Root.Add(top);

        _content = new HavenContainer { Name = "StudyHomeContent", Layout = HavenLayout.Vertical };
        _content.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        _content.SetValue(HavenProperties.Padding, HavenThickness.Parse("8px 28px 28px 28px"));
        _content.SetValue(HavenProperties.Gap, HavenLength.Px(16));
        _content.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        _content.SetValue(HavenProperties.Row, 1);
        Root.Add(_content);

        _status = new HavenText { Name = "StudyStatus", Content = "" };
        _status.SetValue(HavenProperties.Foreground, "TextSecondary");
        _status.SetValue(HavenProperties.FontSize, 11d);
        _status.SetValue(HavenProperties.Padding, HavenThickness.Parse("0px 28px 10px 28px"));
        _status.SetValue(HavenProperties.Row, 2);
        Root.Add(_status);

        SubjectName = new Input { Name = "StudySubjectName", Placeholder = "Subject or course name", SubmitOnEnter = true };
        SubjectName.TextChanged += (_, _) => SetStatus(null);
        SubjectName.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        SubjectName.SetValue(HavenProperties.MaxWidth, HavenLength.Px(520));
        SubjectName.SetValue(HavenProperties.MinHeight, HavenLength.Px(42));
    }

    public HavenPage Root { get; }
    public Input SubjectName { get; }
    public event EventHandler<Guid>? SubjectRequested;
    public event EventHandler<string>? AddSubjectRequested;
    public event EventHandler? NewChatRequested;
    public event EventHandler? PlanRequested;

    public void Render(
        DateTimeOffset now,
        (int CurrentWeekMinutes, int WeeklyAverageMinutes, int TotalMinutes) minutes,
        (int Points, int Level, int PointsToNext) level,
        IReadOnlyList<ContainerDefinition> subjects,
        IReadOnlyDictionary<Guid, IReadOnlyList<Lesson>> lessonsBySubject,
        IReadOnlyDictionary<Guid, IReadOnlyList<PlannerStudyAssignment>> assignmentsBySubject)
    {
        foreach (var child in _content.Children.ToArray()) _content.Remove(child);
        SetStatus(null);

        _content.Add(Muted(now.LocalDateTime.ToString("dddd d MMMM yyyy")));
        _content.Add(Heading("This Week's Study Stats"));

        var stats = new HavenContainer { Layout = HavenLayout.Wrap };
        stats.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        stats.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        var avgPct = minutes.WeeklyAverageMinutes <= 0
            ? "No previous weekly average yet"
            : $"{Math.Round(minutes.CurrentWeekMinutes * 100d / minutes.WeeklyAverageMinutes):0}% of weekly average";
        stats.Add(StatCard(FormatMinutes(minutes.CurrentWeekMinutes), "Time Studied This Week", avgPct));
        stats.Add(StatCard($"Level {level.Level}", "Learning Level", $"{level.PointsToNext} points to next level"));
        stats.Add(StatCard(level.Points.ToString(), "Learning Points", $"{FormatMinutes(minutes.TotalMinutes)} tracked overall"));
        _content.Add(stats);

        var assignmentRows = assignmentsBySubject
            .SelectMany(pair => pair.Value.Select(item => (SubjectId: pair.Key, Assignment: item)))
            .Where(item => item.Assignment.Task.Status != PlannerTaskStatus.Completed)
            .OrderBy(item => item.Assignment.Task.DueAt ?? DateTimeOffset.MaxValue)
            .ToArray();
        _content.Add(Heading($"{assignmentRows.Length} Outstanding Assignments"));
        if (assignmentRows.Length == 0) _content.Add(Muted("Nothing outstanding right now."));
        foreach (var row in assignmentRows)
        {
            var subject = subjects.FirstOrDefault(item => item.Id == row.SubjectId)?.Name ?? "Study";
            var due = row.Assignment.Task.DueAt is { } date ? $"Due {FriendlyDue(date, now)}" : "No due date";
            _content.Add(InfoCard(row.Assignment.Task.Title, $"{subject} • {due}"));
        }

        var difficult = subjects
            .SelectMany(subject => lessonsBySubject.GetValueOrDefault(subject.Id, [])
                .Select(lesson => (subject, lesson, state: StudyLessonMetadata.Read(lesson))))
            .Where(item => item.state.Rag == "red")
            .OrderBy(item => item.subject.Name)
            .ThenBy(item => item.lesson.SortOrder)
            .ToArray();
        _content.Add(Heading("You Found These Topics Hard"));
        if (difficult.Length == 0) _content.Add(Muted("Topics marked Red will appear here."));
        foreach (var item in difficult)
            _content.Add(InfoCard(item.lesson.Name, item.subject.Name));

        _content.Add(Heading("My Subjects / Courses"));
        if (subjects.Count == 0) _content.Add(Muted("Add your first subject to build a Study workspace."));
        foreach (var subject in subjects)
        {
            var lessons = lessonsBySubject.GetValueOrDefault(subject.Id, []);
            var assignments = assignmentsBySubject.GetValueOrDefault(subject.Id, []);
            var progress = lessons.Count == 0 ? 0 : (int)Math.Round(lessons.Select(StudyLessonMetadata.Read).Average(item => item.ProgressPercent));
            var subjectMinutes = StudyLessonMetadata.StudyMinutes(lessons, now).CurrentWeekMinutes;
            var open = new HavenButton
            {
                Name = $"StudySubject-{subject.Id:N}",
                Content = $"{subject.Name}\n{progress}% complete • {FormatMinutes(subjectMinutes)} this week",
                Variant = ButtonVariant.Navigation
            };
            open.SetValue(HavenProperties.Width, HavenLength.Percent(100));
            open.SetValue(HavenProperties.MinHeight, HavenLength.Px(64));
            var id = subject.Id;
            open.Invoked += (_, _) => SubjectRequested?.Invoke(this, id);
            _content.Add(open);
        }

        var add = Card();
        add.Add(Heading("Add Subject"));
        if (SubjectName.Parent is not null) SubjectName.Parent.Remove(SubjectName);
        add.Add(SubjectName);
        var addButton = new HavenButton { Name = "StudyAddSubject", Content = "Add Subject", Variant = ButtonVariant.Primary };
        addButton.SetValue(HavenProperties.MaxWidth, HavenLength.Px(220));
        addButton.Invoked += (_, _) =>
        {
            var name = SubjectName.Text.Trim();
            if (name.Length == 0) { SetStatus("Enter a subject or course name first."); return; }
            AddSubjectRequested?.Invoke(this, name);
        };
        add.Add(addButton);
        _content.Add(add);
    }

    public void SetStatus(string? text)
    {
        _status.Content = text ?? "";
        _status.SetValue(HavenProperties.Visibility, string.IsNullOrWhiteSpace(text) ? HavenVisibility.Collapsed : HavenVisibility.Visible);
    }

    private static HavenText Heading(string text, int column = -1)
    {
        var value = new HavenText { Content = text };
        value.SetValue(HavenProperties.FontSize, 20d);
        value.SetValue(HavenProperties.FontWeight, 800);
        if (column >= 0) value.SetValue(HavenProperties.Column, column);
        return value;
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

    private static HavenContainer StatCard(string value, string label, string detail)
    {
        var card = Card();
        card.SetValue(HavenProperties.Width, HavenLength.Px(245));
        card.Add(Heading(value));
        card.Add(new HavenText { Content = label });
        card.Add(Muted(detail));
        return card;
    }

    private static HavenContainer InfoCard(string title, string subtitle)
    {
        var card = Card();
        card.Add(new HavenText { Content = title });
        card.Add(Muted(subtitle));
        return card;
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

    private static string FormatMinutes(int minutes)
    {
        if (minutes < 60) return $"{minutes} min";
        var hours = minutes / 60d;
        return $"{hours:0.#} h";
    }

    public void Dispose() { }
}
