using System.Diagnostics;
using System.Linq;
using System.Text;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Services;

public sealed class TrainingRunner(
    ChatSessionService sessions,
    IConversationRepository conversations,
    IOllamaClient ollama,
    UserPreferencesService preferences)
{
    public static string CreateWorkspaceSnapshot(string sourceWorkspace)
    {
        var snapshotDir = Path.Combine(Path.GetTempPath(), "haven_training_" + Guid.NewGuid().ToString("N")[..12]);
        CopyDirectory(sourceWorkspace, snapshotDir, recursive: true);
        return snapshotDir;
    }

    public static void CleanupSnapshot(string snapshotPath)
    {
        if (Directory.Exists(snapshotPath))
        {
            try { Directory.Delete(snapshotPath, recursive: true); }
            catch { /* best effort cleanup */ }
        }
    }

    private static void CopyDirectory(string source, string destination, bool recursive)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        if (!recursive) return;
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)), true);
    }

    public async Task<TrainingAttemptResult> RunAttemptAsync(
        string taskPrompt,
        string workspaceRoot,
        string? modelOverride,
        int attemptNumber,
        IProgress<TrainingProgressEvent> progress,
        CancellationToken cancellationToken,
        PermissionMode filePermission = PermissionMode.FullAccess,
        PermissionMode commandPermission = PermissionMode.FullAccess,
        PermissionMode browserPermission = PermissionMode.FullAccess,
        bool allowDesktopTools = true,
        bool allowFileSystemWrites = true)
    {
        var conversation = new Conversation(
            Guid.NewGuid(),
            HavenMode.Do,
            ConversationKind.Training,
            $"Training #{attemptNumber}: {taskPrompt}",
            null, null, false, true,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        await conversations.UpsertConversationAsync(conversation, cancellationToken);

        var models = await ollama.GetModelsAsync(cancellationToken);
        ModelDescriptor? model = null;
        if (!string.IsNullOrWhiteSpace(modelOverride))
            model = models.FirstOrDefault(m => m.Name.Equals(modelOverride, StringComparison.OrdinalIgnoreCase));
        model ??= models.FirstOrDefault(m => m.Name.Equals(preferences.DefaultModel ?? "", StringComparison.OrdinalIgnoreCase))
                  ?? models.FirstOrDefault();

        if (model is null)
            throw new InvalidOperationException("No local Ollama model is available for training.");

        var timeline = new List<TimelineEvent>();
        var reasoningBuffer = new StringBuilder();
        var step = 0;
        var stopwatch = Stopwatch.StartNew();
        var completedBeforeTimeout = false;
        var agentName = "Haven Training Agent";
        var isEmptyWorkspace = !Directory.EnumerateFileSystemEntries(workspaceRoot).Any();
        var agentInstructions = "You are a training agent with full access to workspace tools. " +
                                (isEmptyWorkspace
                                    ? "The workspace directory is EMPTY — create everything from scratch using write_file. " +
                                      "Do NOT try to read or list files outside this directory. "
                                    : "The workspace contains existing files. Read them first to understand the project before making changes. ") +
                                "You MUST use the tools to complete the task: write_file to create/modify files, run_command to build/run, run_tests to verify. " +
                                "Do NOT just describe what you would do. Actually do it using the tools. " +
                                "Explain your reasoning before each action so the report captures your thought process. " +
                                "Workspace: " + workspaceRoot + " " +
                                "Do not ask questions — make reasonable assumptions and proceed.";
        var plugins = Array.Empty<ActivePlugin>();

        try
        {
            await foreach (var chatEvent in sessions.SendAsync(
                conversation, taskPrompt, model, preferences.DefaultEffort,
                plugins, agentName, agentInstructions, DuoMode.Solo,
                workspaceRoot, null, null, null,
                cancellationToken,
                generationOptions: new GenerationOptions(
                    preferences.GenerationOptions.Temperature,
                    preferences.GenerationOptions.ContextLimit,
                    Math.Clamp(preferences.GenerationOptions.ActionLimit, 1, 100)),
                filePermission: filePermission,
                commandPermission: commandPermission,
                browserPermission: browserPermission))
            {
                switch (chatEvent.Kind)
                {
                    case ChatStreamEventKind.AssistantDelta when chatEvent.Delta is { } delta:
                        reasoningBuffer.Append(delta);
                        progress?.Report(new TrainingProgressEvent(TrainingProgressKind.ReasoningDelta, ReasoningText: delta));
                        break;

                    case ChatStreamEventKind.ToolActivity when chatEvent.ToolActivity is { } activity:
                        if (reasoningBuffer.Length > 0)
                        {
                            timeline.Add(new TimelineEvent.Reasoning(reasoningBuffer.ToString().Trim()));
                            reasoningBuffer.Clear();
                        }
                        step++;
                        var log = new TrainingActionLog(step, activity.Title, activity.Detail,
                            activity.Succeeded, activity.Duration, activity.LinesAdded,
                            activity.LinesRemoved, activity.Timestamp);
                        timeline.Add(new TimelineEvent.ToolCall(log));
                        progress?.Report(new TrainingProgressEvent(TrainingProgressKind.ActionRecorded, log));
                        break;

                    case ChatStreamEventKind.AssistantCompleted when chatEvent.Message is { } msg:
                        if (reasoningBuffer.Length > 0)
                        {
                            timeline.Add(new TimelineEvent.Reasoning(reasoningBuffer.ToString().Trim()));
                            reasoningBuffer.Clear();
                        }
                        timeline.Add(new TimelineEvent.FinalSummary(msg.Content));
                        completedBeforeTimeout = true;
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            completedBeforeTimeout = false;
        }

        if (reasoningBuffer.Length > 0)
            timeline.Add(new TimelineEvent.Reasoning(reasoningBuffer.ToString().Trim()));

        stopwatch.Stop();

        var actions = timeline.OfType<TimelineEvent.ToolCall>().Select(t => t.Action).ToList();

        return new TrainingAttemptResult(
            attemptNumber,
            taskPrompt,
            stopwatch.Elapsed,
            timeline,
            actions,
            completedBeforeTimeout);
    }

    public static string GenerateMarkdownReport(TrainingAttemptResult attempt)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# Training Attempt #{attempt.AttemptNumber}");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"| Metric | Value |");
        sb.AppendLine($"|--------|-------|");
        sb.AppendLine($"| Task | {attempt.TaskPrompt} |");
        sb.AppendLine($"| Duration | {FormatDuration(attempt.Elapsed)} |");
        sb.AppendLine($"| Completed | {(attempt.CompletedBeforeTimeout ? "Yes" : "Timed out")} |");
        sb.AppendLine($"| Total tool calls | {attempt.TotalToolCalls} |");
        sb.AppendLine($"| Files created/modified | {attempt.FilesChanged} |");
        sb.AppendLine($"| Build attempts | {attempt.BuildAttempts} |");
        sb.AppendLine($"| Test runs | {attempt.TestRuns} |");
        sb.AppendLine($"| Tests passed | {(attempt.AllTestsPassed ? "Yes" : "No")} |");
        sb.AppendLine();

        sb.AppendLine("## Full Event Timeline");
        sb.AppendLine();
        sb.AppendLine("This section shows the agent's reasoning interleaved with every tool call, ");
        sb.AppendLine("capturing the full thought process behind each action.");
        sb.AppendLine();

        foreach (var evt in attempt.Timeline)
        {
            switch (evt)
            {
                case TimelineEvent.Reasoning r:
                    sb.AppendLine($"> {r.Text}");
                    sb.AppendLine();
                    break;

                case TimelineEvent.ToolCall tc:
                    var a = tc.Action;
                    var icon = a.Succeeded ? "PASS" : "FAIL";
                    sb.AppendLine($"### Step {a.Step}: {a.ToolName} [{icon}]");
                    sb.AppendLine();
                    sb.AppendLine($"- **Title:** {a.Summary}");
                    sb.AppendLine($"- **Summary:** {a.Summary}");
                    if (a.LinesAdded > 0 || a.LinesRemoved > 0)
                        sb.AppendLine($"- **Lines changed:** +{a.LinesAdded} / -{a.LinesRemoved}");
                    sb.AppendLine($"- **Duration:** {FormatDuration(a.Duration)}");
                    sb.AppendLine($"- **Timestamp:** {a.Timestamp:yyyy-MM-dd HH:mm:ss.fff}");
                    sb.AppendLine();
                    break;

                case TimelineEvent.FinalSummary f:
                    sb.AppendLine("## Final Agent Output");
                    sb.AppendLine();
                    sb.AppendLine(f.Message);
                    sb.AppendLine();
                    break;
            }
        }

        if (!attempt.Timeline.OfType<TimelineEvent.FinalSummary>().Any())
        {
            sb.AppendLine("## Final Agent Output");
            sb.AppendLine();
            sb.AppendLine("*No final output — agent timed out before completing.*");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string FormatDuration(TimeSpan ts) =>
        ts.TotalMinutes >= 1 ? $"{(int)ts.TotalMinutes}m {ts.Seconds:D2}s" : $"{ts.TotalSeconds:F1}s";
}

public abstract record TimelineEvent
{
    public sealed record Reasoning(string Text) : TimelineEvent;
    public sealed record ToolCall(TrainingActionLog Action) : TimelineEvent;
    public sealed record FinalSummary(string Message) : TimelineEvent;
}

public sealed record TrainingAttemptResult(
    int AttemptNumber,
    string TaskPrompt,
    TimeSpan Elapsed,
    IReadOnlyList<TimelineEvent> Timeline,
    IReadOnlyList<TrainingActionLog> Actions,
    bool CompletedBeforeTimeout)
{
    public int TotalToolCalls => Actions.Count;
    public int FilesChanged => Actions.Count(a => a.ToolName is "write_file" or "replace_in_file");
    public int BuildAttempts => Actions.Count(a => a.ToolName == "run_command" &&
        a.Summary.Contains("build", StringComparison.OrdinalIgnoreCase));
    public int TestRuns => Actions.Count(a => a.ToolName == "run_tests" ||
        (a.ToolName == "run_command" && a.Summary.Contains("test", StringComparison.OrdinalIgnoreCase)));
    public bool AllTestsPassed
    {
        get
        {
            var tests = Actions.Where(a => a.ToolName == "run_tests" ||
                (a.ToolName == "run_command" && a.Summary.Contains("test", StringComparison.OrdinalIgnoreCase))).ToList();
            return tests.Count > 0 && tests.All(a => a.Succeeded);
        }
    }
}

public sealed record TrainingActionLog(
    int Step,
    string ToolName,
    string Summary,
    bool Succeeded,
    TimeSpan Duration,
    int LinesAdded,
    int LinesRemoved,
    DateTimeOffset Timestamp);

public sealed record TrainingProgressEvent(TrainingProgressKind Kind, TrainingActionLog? Action = null, string? ReasoningText = null);

public enum TrainingProgressKind { ActionRecorded, ReasoningDelta }
