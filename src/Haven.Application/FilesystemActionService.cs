using Haven.Core;

namespace Haven.Application;

public sealed class FilesystemActionService
{
    private readonly IWorkspaceToolService _workspaceTools;
    private readonly IActivityLogRepository _activityLog;

    public FilesystemActionService(IWorkspaceToolService workspaceTools, IActivityLogRepository activityLog)
    {
        _workspaceTools = workspaceTools;
        _activityLog = activityLog;
    }

    public async Task<FilesystemActionResult> ReadFileAsync(string workspaceRoot, string relativePath, CancellationToken cancellationToken)
    {
        try
        {
            var content = await _workspaceTools.ReadTextAsync(workspaceRoot, relativePath, cancellationToken).ConfigureAwait(false);
            await _activityLog.AddEventAsync(new ActivityEvent(
                Guid.NewGuid(),
                ActivityEventKind.FilesystemAction,
                null, null,
                $"Read file: {relativePath}",
                $"{{\"action\":\"read\",\"path\":\"{EscapeJson(relativePath)}\",\"size\":{content.Length}}}",
                DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
            return new FilesystemActionResult(true, content, $"Read {content.Length} characters from {relativePath}");
        }
        catch (Exception ex)
        {
            return new FilesystemActionResult(false, null, $"Failed to read {relativePath}: {ex.Message}");
        }
    }

    public async Task<FilesystemActionResult> WriteFileAsync(string workspaceRoot, string relativePath, string content, CancellationToken cancellationToken)
    {
        try
        {
            await _workspaceTools.WriteTextAtomicAsync(workspaceRoot, relativePath, content, cancellationToken).ConfigureAwait(false);
            await _activityLog.AddEventAsync(new ActivityEvent(
                Guid.NewGuid(),
                ActivityEventKind.FilesystemAction,
                null, null,
                $"Wrote file: {relativePath}",
                $"{{\"action\":\"write\",\"path\":\"{EscapeJson(relativePath)}\",\"size\":{content.Length}}}",
                DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
            return new FilesystemActionResult(true, null, $"Wrote {content.Length} characters to {relativePath}");
        }
        catch (Exception ex)
        {
            return new FilesystemActionResult(false, null, $"Failed to write {relativePath}: {ex.Message}");
        }
    }

    public async Task<FilesystemActionResult> SearchFilesAsync(string workspaceRoot, string pattern, CancellationToken cancellationToken)
    {
        try
        {
            var files = await _workspaceTools.SearchFilesAsync(workspaceRoot, pattern, cancellationToken).ConfigureAwait(false);
            await _activityLog.AddEventAsync(new ActivityEvent(
                Guid.NewGuid(),
                ActivityEventKind.FilesystemAction,
                null, null,
                $"Searched files: {pattern}",
                $"{{\"action\":\"search\",\"pattern\":\"{EscapeJson(pattern)}\",\"count\":{files.Count}}}",
                DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
            return new FilesystemActionResult(true, string.Join("\n", files), $"Found {files.Count} files matching '{pattern}'");
        }
        catch (Exception ex)
        {
            return new FilesystemActionResult(false, null, $"Search failed: {ex.Message}");
        }
    }

    public async Task<CommandExecutionResult> RunCommandAsync(string command, string arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            var request = new ProcessRequest(command, arguments, workingDirectory, timeout);
            var result = await _workspaceTools.RunProcessAsync(request, cancellationToken).ConfigureAwait(false);
            await _activityLog.AddEventAsync(new ActivityEvent(
                Guid.NewGuid(),
                ActivityEventKind.CommandRun,
                null, null,
                $"Ran command: {command} {arguments}",
                $"{{\"command\":\"{EscapeJson(command)}\",\"exitCode\":{result.ExitCode},\"duration\":{result.Duration.TotalMilliseconds}}}",
                DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
            return new CommandExecutionResult(result.ExitCode, result.StandardOutput, result.StandardError, result.TimedOut);
        }
        catch (Exception ex)
        {
            return new CommandExecutionResult(-1, "", ex.Message, false);
        }
    }

    private static string EscapeJson(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
}

public sealed record FilesystemActionResult(bool Succeeded, string? Content, string Message);
public sealed record CommandExecutionResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut);
