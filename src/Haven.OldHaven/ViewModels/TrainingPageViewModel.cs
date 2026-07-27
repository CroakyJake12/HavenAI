/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/ViewModels/TrainingPageViewModel.cs, in the Desktop presentation-model layer, exposing bindable state and commands to Avalonia views.
 * What: This file owns TrainingPageViewModel. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Keeping UI state here makes the XAML declarative and keeps behavior testable without recreating the full window.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

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

/// <summary>
/// Represents training page view model and keeps its related state and behavior together.
/// </summary>
public sealed class TrainingPageViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Stores runner locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TrainingRunner _runner;
    /// <summary>
    /// Stores training repo locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ITrainingRepository _trainingRepo;
    /// <summary>
    /// Stores conversations locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IConversationRepository _conversations;
    /// <summary>
    /// Stores ollama locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IOllamaClient _ollama;
    /// <summary>
    /// Stores preferences locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly UserPreferencesService _preferences;
    /// <summary>
    /// Stores log locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Action<string> _log;
    /// <summary>
    /// Stores exit training locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Action _exitTraining;
    /// <summary>
    /// Stores session cts locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private CancellationTokenSource? _sessionCts;
    /// <summary>
    /// Stores attempt counter locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int _attemptCounter;
    /// <summary>
    /// Stores snapshot path locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string? _snapshotPath;
    /// <summary>
    /// Stores current run locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private TrainingRun? _currentRun;

    /// <summary>
    /// Stores task prompt locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _taskPrompt = string.Empty;
    /// <summary>
    /// Gets or updates task prompt, the bindable or domain state represented by this property.
    /// </summary>
    public string TaskPrompt { get => _taskPrompt; set => SetProperty(ref _taskPrompt, value); }

    /// <summary>
    /// Stores workspace path locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _workspacePath = string.Empty;
    /// <summary>
    /// Gets or updates workspace path, the bindable or domain state represented by this property.
    /// </summary>
    public string WorkspacePath { get => _workspacePath; set => SetProperty(ref _workspacePath, value); }

    /// <summary>
    /// Stores selected model locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _selectedModel = string.Empty;
    /// <summary>
    /// Gets or updates selected model, the bindable or domain state represented by this property.
    /// </summary>
    public string SelectedModel { get => _selectedModel; set => SetProperty(ref _selectedModel, value); }

    /// <summary>
    /// Stores duration minutes locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int _durationMinutes = 10;
    /// <summary>
    /// Gets or updates duration minutes, the bindable or domain state represented by this property.
    /// </summary>
    public int DurationMinutes { get => _durationMinutes; set => SetProperty(ref _durationMinutes, value); }

    /// <summary>
    /// Stores status message locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _statusMessage = "Ready";
    /// <summary>
    /// Gets or updates status message, the bindable or domain state represented by this property.
    /// </summary>
    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

    /// <summary>
    /// Stores is running locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isRunning;
    /// <summary>
    /// Reports whether running applies to the current state.
    /// </summary>
    public bool IsRunning { get => _isRunning; set { if (SetProperty(ref _isRunning, value)) RaisePropertyChanged(nameof(CanEditSettings)); } }

    /// <summary>
    /// Stores is awaiting feedback locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isAwaitingFeedback;
    /// <summary>
    /// Reports whether awaiting feedback applies to the current state.
    /// </summary>
    public bool IsAwaitingFeedback { get => _isAwaitingFeedback; set => SetProperty(ref _isAwaitingFeedback, value); }

    /// <summary>
    /// Stores total attempts locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int _totalAttempts;
    /// <summary>
    /// Gets or updates total attempts, the bindable or domain state represented by this property.
    /// </summary>
    public int TotalAttempts { get => _totalAttempts; set => SetProperty(ref _totalAttempts, value); }

    /// <summary>
    /// Stores current attempt locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private TrainingAttemptResult? _currentAttempt;
    /// <summary>
    /// Gets or updates current attempt, the bindable or domain state represented by this property.
    /// </summary>
    public TrainingAttemptResult? CurrentAttempt { get => _currentAttempt; set => SetProperty(ref _currentAttempt, value); }

    /// <summary>
    /// Stores current report locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _currentReport = string.Empty;
    /// <summary>
    /// Gets or updates current report, the bindable or domain state represented by this property.
    /// </summary>
    public string CurrentReport { get => _currentReport; set => SetProperty(ref _currentReport, value); }

    /// <summary>
    /// Stores live reasoning locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _liveReasoning = string.Empty;
    public string LiveReasoning
    {
        get => _liveReasoning;
        set { if (SetProperty(ref _liveReasoning, value)) RaisePropertyChanged(nameof(HasLiveReasoning)); }
    }
    /// <summary>
    /// Reports whether live reasoning applies to the current state.
    /// </summary>
    public bool HasLiveReasoning => !string.IsNullOrEmpty(_liveReasoning);

    /// <summary>
    /// Stores feedback text locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _feedbackText = string.Empty;
    /// <summary>
    /// Gets or updates feedback text, the bindable or domain state represented by this property.
    /// </summary>
    public string FeedbackText { get => _feedbackText; set => SetProperty(ref _feedbackText, value); }

    /// <summary>
    /// Stores selected attempt locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private TrainingAttemptResult? _selectedAttempt;
    /// <summary>
    /// Gets or updates selected attempt, the bindable or domain state represented by this property.
    /// </summary>
    public TrainingAttemptResult? SelectedAttempt { get => _selectedAttempt; set => SetProperty(ref _selectedAttempt, value); }

    /// <summary>
    /// Stores selected attempt report locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _selectedAttemptReport = string.Empty;
    /// <summary>
    /// Gets or updates selected attempt report, the bindable or domain state represented by this property.
    /// </summary>
    public string SelectedAttemptReport { get => _selectedAttemptReport; set => SetProperty(ref _selectedAttemptReport, value); }

    /// <summary>
    /// Stores use workspace snapshot locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _useWorkspaceSnapshot = true;
    /// <summary>
    /// Gets or updates use workspace snapshot, the bindable or domain state represented by this property.
    /// </summary>
    public bool UseWorkspaceSnapshot { get => _useWorkspaceSnapshot; set => SetProperty(ref _useWorkspaceSnapshot, value); }

    /// <summary>
    /// Stores file permission locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private PermissionMode _filePermission = PermissionMode.FullAccess;
    /// <summary>
    /// Gets or updates file permission, the bindable or domain state represented by this property.
    /// </summary>
    public PermissionMode FilePermission { get => _filePermission; set => SetProperty(ref _filePermission, value); }

    /// <summary>
    /// Stores command permission locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private PermissionMode _commandPermission = PermissionMode.FullAccess;
    /// <summary>
    /// Gets or updates command permission, the bindable or domain state represented by this property.
    /// </summary>
    public PermissionMode CommandPermission { get => _commandPermission; set => SetProperty(ref _commandPermission, value); }

    /// <summary>
    /// Stores browser permission locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private PermissionMode _browserPermission = PermissionMode.Ask;
    /// <summary>
    /// Gets or updates browser permission, the bindable or domain state represented by this property.
    /// </summary>
    public PermissionMode BrowserPermission { get => _browserPermission; set => SetProperty(ref _browserPermission, value); }

    /// <summary>
    /// Stores allow desktop tools locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _allowDesktopTools;
    /// <summary>
    /// Gets or updates allow desktop tools, the bindable or domain state represented by this property.
    /// </summary>
    public bool AllowDesktopTools { get => _allowDesktopTools; set => SetProperty(ref _allowDesktopTools, value); }

    /// <summary>
    /// Stores allow file system writes locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _allowFileSystemWrites = true;
    /// <summary>
    /// Gets or updates allow file system writes, the bindable or domain state represented by this property.
    /// </summary>
    public bool AllowFileSystemWrites { get => _allowFileSystemWrites; set => SetProperty(ref _allowFileSystemWrites, value); }

    /// <summary>
    /// Reports whether edit settings applies to the current state.
    /// </summary>
    public bool CanEditSettings => !IsRunning;

    /// <summary>
    /// Gets or updates attempt history, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<TrainingAttemptResult> AttemptHistory { get; } = [];

    /// <summary>
    /// Gets or updates permission modes, the bindable or domain state represented by this property.
    /// </summary>
    public PermissionMode[] PermissionModes { get; } = Enum.GetValues<PermissionMode>();

    /// <summary>
    /// Stores available models locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string[] _availableModels = [];
    /// <summary>
    /// Gets or updates available models, the bindable or domain state represented by this property.
    /// </summary>
    public string[] AvailableModels { get => _availableModels; set => SetProperty(ref _availableModels, value); }

    /// <summary>
    /// Gets or updates start session command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand StartSessionCommand { get; }
    /// <summary>
    /// Gets or updates stop session command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand StopSessionCommand { get; }
    /// <summary>
    /// Gets or updates submit feedback command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand SubmitFeedbackCommand { get; }
    /// <summary>
    /// Gets or updates skip feedback command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand SkipFeedbackCommand { get; }
    /// <summary>
    /// Gets or updates exit training command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand ExitTrainingCommand { get; }
    /// <summary>
    /// Gets or updates select attempt command, the bindable or domain state represented by this property.
    /// </summary>
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

    /// <summary>
    /// Performs load models asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs start session asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Stores feedback tcs locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private TaskCompletionSource? _feedbackTcs;

    /// <summary>
    /// Performs the wait for feedback or timeout step owned by this component.
    /// </summary>
    private async Task WaitForFeedbackOrTimeout(CancellationToken ct)
    {
        _feedbackTcs = new TaskCompletionSource();
        using var reg = ct.Register(() => _feedbackTcs.TrySetResult());
        await Task.WhenAny(_feedbackTcs.Task, Task.Delay(TimeSpan.FromSeconds(10), ct));
    }

    /// <summary>
    /// Performs the submit feedback step owned by this component.
    /// </summary>
    private void SubmitFeedback() => _feedbackTcs?.TrySetResult();

    /// <summary>
    /// Performs the skip feedback step owned by this component.
    /// </summary>
    private void SkipFeedback()
    {
        FeedbackText = "(skipped)";
        _feedbackTcs?.TrySetResult();
    }

    /// <summary>
    /// Performs the stop session step owned by this component.
    /// </summary>
    private void StopSession() => _sessionCts?.Cancel();

    /// <summary>
    /// Performs the exit training step owned by this component.
    /// </summary>
    private void ExitTraining() => _exitTraining();

    /// <summary>
    /// Performs the select attempt step owned by this component.
    /// </summary>
    public void SelectAttempt(TrainingAttemptResult? attempt)
    {
        if (attempt is null) return;
        SelectedAttempt = attempt;
        SelectedAttemptReport = TrainingRunner.GenerateMarkdownReport(attempt);
    }

    /// <summary>
    /// Performs save feedback asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs the truncate step owned by this component.
    /// </summary>
    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "...";

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose()
    {
        _sessionCts?.Dispose();
    }
}
