/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/FilesystemActionService.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns FilesystemActionService, FilesystemActionResult, CommandExecutionResult. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Represents filesystem action service and keeps its related state and behavior together.
/// </summary>
public sealed class FilesystemActionService
{
    /// <summary>
    /// Stores workspace tools locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IWorkspaceToolService _workspaceTools;
    /// <summary>
    /// Stores activity log locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IActivityLogRepository _activityLog;

    public FilesystemActionService(IWorkspaceToolService workspaceTools, IActivityLogRepository activityLog)
    {
        _workspaceTools = workspaceTools;
        _activityLog = activityLog;
    }

    /// <summary>
    /// Performs read file asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs write file asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs search files asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Runs run command async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
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

    /// <summary>
    /// Performs the escape json step owned by this component.
    /// </summary>
    private static string EscapeJson(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
}

/// <summary>
/// Represents filesystem action result and keeps its related state and behavior together.
/// </summary>
public sealed record FilesystemActionResult(bool Succeeded, string? Content, string Message);
/// <summary>
/// Represents command execution result and keeps its related state and behavior together.
/// </summary>
public sealed record CommandExecutionResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut);
