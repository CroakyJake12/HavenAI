using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Events;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Training;

/// <summary>
/// Training page. Runs iterative training sessions with live reasoning and feedback.
/// </summary>
public sealed partial class TrainingPage : UserControl
{
    private readonly HavenEventBus _bus;
    private readonly TrainingRunner _runner;
    private readonly ITrainingRepository _trainingRepo;
    private readonly IConversationRepository _conversations;
    private readonly IOllamaClient _ollama;
    private readonly UserPreferencesService _preferences;
    private readonly Action<string> _log;
    private readonly Action _exitTraining;

    private CancellationTokenSource? _sessionCts;
    private TrainingRun? _currentRun;
    private int _attemptCounter;
    private string? _snapshotPath;
    private TaskCompletionSource? _feedbackTcs;

    public TrainingPage(
        HavenEventBus bus,
        TrainingRunner runner,
        ITrainingRepository trainingRepo,
        IConversationRepository conversations,
        IOllamaClient ollama,
        UserPreferencesService preferences,
        Action<string> log,
        Action exitTraining)
    {
        _bus = bus;
        _runner = runner;
        _trainingRepo = trainingRepo;
        _conversations = conversations;
        _ollama = ollama;
        _preferences = preferences;
        _log = log;
        _exitTraining = exitTraining;

        InitializeComponent();
        WorkspacePathBox.Text = Directory.GetCurrentDirectory();
        DurationBox.Value = 10;
        WireEvents();
        _ = LoadModelsAsync();
    }

    private async Task LoadModelsAsync()
    {
        try
        {
            var models = await _ollama.GetModelsAsync(CancellationToken.None);
            ModelCombo.ItemsSource = models.Select(m => m.Name).ToList();
            if (ModelCombo.Items.Count > 0) ModelCombo.SelectedIndex = 0;
        }
        catch { }
    }

    private void WireEvents()
    {
        _bus.RegisterElement("Training.Actions.Back", BackButton);
        _bus.WirePointerEvents("Training.Actions.Back", BackButton);
        BackButton.Click += (_, _) =>
        {
            _bus.Fire("Training.Actions.Back");
            _exitTraining();
        };

        _bus.RegisterElement("Training.Actions.Start", StartButton);
        _bus.WirePointerEvents("Training.Actions.Start", StartButton);
        StartButton.Click += async (_, _) =>
        {
            _bus.Fire("Training.Actions.Start");
            await StartSessionAsync();
        };

        _bus.RegisterElement("Training.Actions.Stop", StopButton);
        _bus.WirePointerEvents("Training.Actions.Stop", StopButton);
        StopButton.Click += (_, _) =>
        {
            _bus.Fire("Training.Actions.Stop");
            _sessionCts?.Cancel();
        };

        _bus.RegisterElement("Training.Actions.SubmitFeedback", SubmitFeedbackButton);
        _bus.WirePointerEvents("Training.Actions.SubmitFeedback", SubmitFeedbackButton);
        SubmitFeedbackButton.Click += (_, _) =>
        {
            _bus.Fire("Training.Actions.SubmitFeedback");
            _feedbackTcs?.TrySetResult();
        };

        _bus.RegisterElement("Training.Actions.SkipFeedback", SkipFeedbackButton);
        _bus.WirePointerEvents("Training.Actions.SkipFeedback", SkipFeedbackButton);
        SkipFeedbackButton.Click += (_, _) =>
        {
            _bus.Fire("Training.Actions.SkipFeedback");
            FeedbackBox.Text = "(skipped)";
            _feedbackTcs?.TrySetResult();
        };
    }

    private async Task StartSessionAsync()
    {
        var taskPrompt = TaskPromptBox.Text?.Trim();
        var workspacePath = WorkspacePathBox.Text?.Trim();
        var selectedModel = ModelCombo.SelectedItem as string;
        var duration = (int)(DurationBox.Value ?? 10);

        if (string.IsNullOrWhiteSpace(taskPrompt)) { StatusText.Text = "Enter a task"; return; }
        if (!Directory.Exists(workspacePath)) { StatusText.Text = "Workspace directory not found"; return; }
        if (string.IsNullOrWhiteSpace(selectedModel)) { StatusText.Text = "Select a model"; return; }

        SetupPanel.IsVisible = false;
        ProgressPanel.IsVisible = true;
        _attemptCounter = 0;
        _sessionCts = new CancellationTokenSource();

        StatusText.Text = "Starting training session...";
        BottomStatus.Text = StatusText.Text;

        _currentRun = new TrainingRun(
            Guid.NewGuid(), taskPrompt, workspacePath, "", selectedModel,
            0, duration, PermissionMode.FullAccess, PermissionMode.FullAccess, PermissionMode.Ask,
            false, true, DateTimeOffset.UtcNow, null);
        await _trainingRepo.UpsertRunAsync(_currentRun, _sessionCts.Token);

        StatusText.Text = "Creating workspace snapshot...";
        _snapshotPath = TrainingRunner.CreateWorkspaceSnapshot(workspacePath);

        var workspaceRoot = _snapshotPath ?? workspacePath;
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(duration);

        try
        {
            while (DateTimeOffset.UtcNow < deadline && !_sessionCts.Token.IsCancellationRequested)
            {
                _attemptCounter++;
                AttemptLabel.Text = $"Attempt {_attemptCounter}";
                BottomAttemptCount.Text = $"{_attemptCounter} attempt(s)";
                StatusText.Text = $"Attempt {_attemptCounter} running...";
                BottomStatus.Text = StatusText.Text;

                var remaining = deadline - DateTimeOffset.UtcNow;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_sessionCts.Token);
                timeout.CancelAfter(remaining);

                var progress = new Progress<TrainingProgressEvent>(e =>
                {
                    if (e.ReasoningText is { } delta)
                        Dispatcher.UIThread.Post(() =>
                        {
                            ReasoningText.Text += delta;
                            ReasoningBorder.IsVisible = true;
                        });
                });

                var result = await _runner.RunAttemptAsync(
                    taskPrompt, workspaceRoot, selectedModel, _attemptCounter, progress, timeout.Token,
                    PermissionMode.FullAccess, PermissionMode.FullAccess, PermissionMode.Ask, false, true);

                var report = TrainingRunner.GenerateMarkdownReport(result);
                ReportText.Text = report;
                StatusText.Text = $"Attempt {_attemptCounter} complete - {result.TotalToolCalls} actions, {result.FilesChanged} files changed";
                BottomStatus.Text = StatusText.Text;

                var dbAttempt = new TrainingAttempt(
                    Guid.NewGuid(), _currentRun!.Id, _attemptCounter, report,
                    null, "", result.CompletedBeforeTimeout, result.Elapsed, DateTimeOffset.UtcNow);
                await _trainingRepo.UpsertAttemptAsync(dbAttempt, _sessionCts.Token);

                FeedbackPanel.IsVisible = true;
                FeedbackBox.Text = string.Empty;
                _feedbackTcs = new TaskCompletionSource();
                using var reg = _sessionCts.Token.Register(() => _feedbackTcs.TrySetResult());
                await Task.WhenAny(_feedbackTcs.Task, Task.Delay(TimeSpan.FromSeconds(10), _sessionCts.Token));
                FeedbackPanel.IsVisible = false;
                ReasoningText.Text = string.Empty;
                ReasoningBorder.IsVisible = false;
            }

            if (_currentRun is { } run)
            {
                var updatedRun = run with { CompletedAt = DateTimeOffset.UtcNow };
                await _trainingRepo.UpsertRunAsync(updatedRun, _sessionCts.Token);
            }
            StatusText.Text = $"Training complete - {_attemptCounter} attempts over {duration} minutes";
            BottomStatus.Text = StatusText.Text;
        }
        catch (OperationCanceledException) { StatusText.Text = "Training session cancelled"; BottomStatus.Text = StatusText.Text; }
        catch (Exception ex) { StatusText.Text = $"Error: {ex.Message}"; BottomStatus.Text = StatusText.Text; }
        finally
        {
            if (_snapshotPath is { } snap) { TrainingRunner.CleanupSnapshot(snap); _snapshotPath = null; }
            SetupPanel.IsVisible = true;
            ProgressPanel.IsVisible = false;
            _sessionCts?.Dispose();
            _sessionCts = null;
        }
    }
}
