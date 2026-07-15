using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop.ViewModels;

public sealed class TrainingPageViewModel : ObservableObject, IDisposable
{
    private readonly TrainingRunner _runner;
    private readonly ITrainingRepository _trainingRepo;
    private readonly IConversationRepository _conversations;
    private readonly IOllamaClient _ollama;
    private readonly UserPreferencesService _preferences;
    private readonly Action<string> _log;
    private readonly Action _exitTraining;
    private CancellationTokenSource? _sessionCts;
    private int _attemptCounter;
    private string? _snapshotPath;
    private TrainingRun? _currentRun;

    private string _taskPrompt = string.Empty;
    public string TaskPrompt { get => _taskPrompt; set => SetProperty(ref _taskPrompt, value); }

    private string _workspacePath = string.Empty;
    public string WorkspacePath { get => _workspacePath; set => SetProperty(ref _workspacePath, value); }

    private string _selectedModel = string.Empty;
    public string SelectedModel { get => _selectedModel; set => SetProperty(ref _selectedModel, value); }

    private int _durationMinutes = 10;
    public int DurationMinutes { get => _durationMinutes; set => SetProperty(ref _durationMinutes, value); }

    private string _statusMessage = "Ready";
    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

    private bool _isRunning;
    public bool IsRunning { get => _isRunning; set { if (SetProperty(ref _isRunning, value)) RaisePropertyChanged(nameof(CanEditSettings)); } }

    private bool _isAwaitingFeedback;
    public bool IsAwaitingFeedback { get => _isAwaitingFeedback; set => SetProperty(ref _isAwaitingFeedback, value); }

    private int _totalAttempts;
    public int TotalAttempts { get => _totalAttempts; set => SetProperty(ref _totalAttempts, value); }

    private TrainingAttemptResult? _currentAttempt;
    public TrainingAttemptResult? CurrentAttempt { get => _currentAttempt; set => SetProperty(ref _currentAttempt, value); }

    private string _currentReport = string.Empty;
    public string CurrentReport { get => _currentReport; set => SetProperty(ref _currentReport, value); }

    private string _liveReasoning = string.Empty;
    public string LiveReasoning
    {
        get => _liveReasoning;
        set { if (SetProperty(ref _liveReasoning, value)) RaisePropertyChanged(nameof(HasLiveReasoning)); }
    }
    public bool HasLiveReasoning => !string.IsNullOrEmpty(_liveReasoning);

    private string _feedbackText = string.Empty;
    public string FeedbackText { get => _feedbackText; set => SetProperty(ref _feedbackText, value); }

    private TrainingAttemptResult? _selectedAttempt;
    public TrainingAttemptResult? SelectedAttempt { get => _selectedAttempt; set => SetProperty(ref _selectedAttempt, value); }

    private string _selectedAttemptReport = string.Empty;
    public string SelectedAttemptReport { get => _selectedAttemptReport; set => SetProperty(ref _selectedAttemptReport, value); }

    private bool _useWorkspaceSnapshot = true;
    public bool UseWorkspaceSnapshot { get => _useWorkspaceSnapshot; set => SetProperty(ref _useWorkspaceSnapshot, value); }

    private PermissionMode _filePermission = PermissionMode.FullAccess;
    public PermissionMode FilePermission { get => _filePermission; set => SetProperty(ref _filePermission, value); }

    private PermissionMode _commandPermission = PermissionMode.FullAccess;
    public PermissionMode CommandPermission { get => _commandPermission; set => SetProperty(ref _commandPermission, value); }

    private PermissionMode _browserPermission = PermissionMode.Ask;
    public PermissionMode BrowserPermission { get => _browserPermission; set => SetProperty(ref _browserPermission, value); }

    private bool _allowDesktopTools;
    public bool AllowDesktopTools { get => _allowDesktopTools; set => SetProperty(ref _allowDesktopTools, value); }

    private bool _allowFileSystemWrites = true;
    public bool AllowFileSystemWrites { get => _allowFileSystemWrites; set => SetProperty(ref _allowFileSystemWrites, value); }

    public bool CanEditSettings => !IsRunning;

    public ObservableCollection<TrainingAttemptResult> AttemptHistory { get; } = [];

    public PermissionMode[] PermissionModes { get; } = Enum.GetValues<PermissionMode>();

    private string[] _availableModels = [];
    public string[] AvailableModels { get => _availableModels; set => SetProperty(ref _availableModels, value); }

    public AsyncRelayCommand StartSessionCommand { get; }
    public RelayCommand StopSessionCommand { get; }
    public RelayCommand SubmitFeedbackCommand { get; }
    public RelayCommand SkipFeedbackCommand { get; }
    public RelayCommand ExitTrainingCommand { get; }
    public RelayCommand<TrainingAttemptResult> SelectAttemptCommand { get; }

    public TrainingPageViewModel(
        TrainingRunner runner,
        ITrainingRepository trainingRepo,
        IConversationRepository conversations,
        IOllamaClient ollama,
        UserPreferencesService preferences,
        Action<string> log,
        Action exitTraining)
    {
        _runner = runner;
        _trainingRepo = trainingRepo;
        _conversations = conversations;
        _ollama = ollama;
        _preferences = preferences;
        _log = log;
        _exitTraining = exitTraining;
        WorkspacePath = Directory.GetCurrentDirectory();
        AvailableModels = Array.Empty<string>();
        StartSessionCommand = new AsyncRelayCommand(StartSessionAsync, () => !IsRunning);
        StopSessionCommand = new RelayCommand(StopSession, () => IsRunning);
        SubmitFeedbackCommand = new RelayCommand(SubmitFeedback);
        SkipFeedbackCommand = new RelayCommand(SkipFeedback);
        ExitTrainingCommand = new RelayCommand(ExitTraining);
        SelectAttemptCommand = new RelayCommand<TrainingAttemptResult>(SelectAttempt);
        _ = LoadModelsAsync();
    }

    private async Task LoadModelsAsync()
    {
        try
        {
            var models = await _ollama.GetModelsAsync(CancellationToken.None);
            AvailableModels = models.Select(m => m.Name).ToArray();
            if (AvailableModels.Length > 0 && string.IsNullOrEmpty(SelectedModel))
                SelectedModel = AvailableModels[0];
        }
        catch { /* ollama not running */ }
    }

    private async Task StartSessionAsync()
    {
        if (string.IsNullOrWhiteSpace(TaskPrompt)) { StatusMessage = "Enter a task"; return; }
        if (!Directory.Exists(WorkspacePath)) { StatusMessage = "Workspace directory not found"; return; }

        IsRunning = true;
        IsAwaitingFeedback = false;
        _attemptCounter = 0;
        AttemptHistory.Clear();
        StatusMessage = "Starting training session...";
        _sessionCts = new CancellationTokenSource();

        _currentRun = new TrainingRun(
            Guid.NewGuid(), TaskPrompt, WorkspacePath, "", SelectedModel,
            0, DurationMinutes, FilePermission, CommandPermission, BrowserPermission,
            AllowDesktopTools, AllowFileSystemWrites,
            DateTimeOffset.UtcNow, null);
        await _trainingRepo.UpsertRunAsync(_currentRun, _sessionCts.Token);

        if (UseWorkspaceSnapshot)
        {
            StatusMessage = "Creating workspace snapshot...";
            _snapshotPath = TrainingRunner.CreateWorkspaceSnapshot(WorkspacePath);
            _log($"Workspace snapshot created at {_snapshotPath}");
        }

        var workspaceRoot = _snapshotPath ?? WorkspacePath;
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(DurationMinutes);

        try
        {
            while (DateTimeOffset.UtcNow < deadline && !_sessionCts.Token.IsCancellationRequested)
            {
                _attemptCounter++;
                TotalAttempts = _attemptCounter;
                StatusMessage = $"Attempt {_attemptCounter} running...";

                var remaining = deadline - DateTimeOffset.UtcNow;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_sessionCts.Token);
                timeout.CancelAfter(remaining);

                var progress = new Progress<TrainingProgressEvent>(e =>
                {
                    if (e.Action is { } a)
                    {
                        _log($"#{a.Step} {a.ToolName}: {Truncate(a.Summary, 80)}");
                        LiveReasoning = string.Empty;
                    }
                    if (e.ReasoningText is { } delta)
                        LiveReasoning += delta;
                });

                var result = await _runner.RunAttemptAsync(
                    TaskPrompt, workspaceRoot, SelectedModel, _attemptCounter, progress, timeout.Token,
                    FilePermission, CommandPermission, BrowserPermission, AllowDesktopTools, AllowFileSystemWrites);

                CurrentAttempt = result;
                CurrentReport = TrainingRunner.GenerateMarkdownReport(result);
                AttemptHistory.Add(result);
                StatusMessage = $"Attempt {_attemptCounter} complete — {result.TotalToolCalls} actions, " +
                    $"{result.FilesChanged} files changed{(result.AllTestsPassed ? ", tests passed" : "")}";

                var dbAttempt = new TrainingAttempt(
                    Guid.NewGuid(), _currentRun!.Id, _attemptCounter, CurrentReport,
                    null, "", result.CompletedBeforeTimeout, result.Elapsed, DateTimeOffset.UtcNow);
                await _trainingRepo.UpsertAttemptAsync(dbAttempt, _sessionCts.Token);

                IsAwaitingFeedback = true;
                FeedbackText = string.Empty;
                await WaitForFeedbackOrTimeout(_sessionCts.Token);
                if (!string.IsNullOrWhiteSpace(FeedbackText))
                {
                    dbAttempt = dbAttempt with { Feedback = FeedbackText };
                    await _trainingRepo.UpsertAttemptAsync(dbAttempt, _sessionCts.Token);
                    await SaveFeedbackAsync(result, FeedbackText);
                }
                IsAwaitingFeedback = false;
                LiveReasoning = string.Empty;
            }

            if (_currentRun is { } run)
            {
                var updatedRun = run with { CompletedAt = DateTimeOffset.UtcNow };
                await _trainingRepo.UpsertRunAsync(updatedRun, _sessionCts.Token);
            }

            StatusMessage = $"Training complete — {TotalAttempts} attempts over {DurationMinutes} minutes";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Training session cancelled";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            _log(ex.ToString());
        }
        finally
        {
            if (_snapshotPath is { } snap)
            {
                _log("Cleaning up workspace snapshot...");
                TrainingRunner.CleanupSnapshot(snap);
                _snapshotPath = null;
            }
            IsRunning = false;
            _sessionCts?.Dispose();
            _sessionCts = null;
        }
    }

    private TaskCompletionSource? _feedbackTcs;

    private async Task WaitForFeedbackOrTimeout(CancellationToken ct)
    {
        _feedbackTcs = new TaskCompletionSource();
        using var reg = ct.Register(() => _feedbackTcs.TrySetResult());
        await Task.WhenAny(_feedbackTcs.Task, Task.Delay(TimeSpan.FromSeconds(10), ct));
    }

    private void SubmitFeedback() => _feedbackTcs?.TrySetResult();

    private void SkipFeedback()
    {
        FeedbackText = "(skipped)";
        _feedbackTcs?.TrySetResult();
    }

    private void StopSession() => _sessionCts?.Cancel();

    private void ExitTraining() => _exitTraining();

    public void SelectAttempt(TrainingAttemptResult? attempt)
    {
        if (attempt is null) return;
        SelectedAttempt = attempt;
        SelectedAttemptReport = TrainingRunner.GenerateMarkdownReport(attempt);
    }

    private async Task SaveFeedbackAsync(TrainingAttemptResult attempt, string feedback)
    {
        var dir = Path.Combine(WorkspacePath, ".haven", "training");
        Directory.CreateDirectory(dir);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var path = Path.Combine(dir, $"attempt_{attempt.AttemptNumber}_{timestamp}.md");
        var report = TrainingRunner.GenerateMarkdownReport(attempt);
        var content = report + "\n\n---\n\n## Feedback\n\n" + feedback + "\n";
        await File.WriteAllTextAsync(path, content);
        _log($"Saved training data to {path}");
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "...";

    public void Dispose()
    {
        _sessionCts?.Dispose();
    }
}
