using System.Text.Json;
using System.Text.RegularExpressions;
using Haven.Core;

namespace Haven.Application;

public sealed class FilesystemActionService
{
    private static readonly Regex SensitiveOptionRegex = new(
        """(?ix)(?<name>--?(?:api[-_]?key|token|access[-_]?token|auth[-_]?token|secret|password|passwd|pwd|client[-_]?secret|connection[-_]?string))(?:\s+|=)(?<value>"[^"]*"|'[^']*'|\S+)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SensitiveAssignmentRegex = new(
        """(?ix)\b(?<name>[A-Z0-9_]*(?:API_KEY|TOKEN|SECRET|PASSWORD|PASSWD|PWD)[A-Z0-9_]*)=(?<value>"[^"]*"|'[^']*'|\S+)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AuthorizationRegex = new(
        """(?ix)\b(?<scheme>Bearer|Basic)\s+\S+""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TokenPrefixRegex = new(
        """(?ix)\b(?:gh[pousr]_[A-Z0-9]{20,}|github_pat_[A-Z0-9_]{20,}|sk-[A-Z0-9_-]{16,})\b""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IWorkspaceToolService _workspaceTools;
    private readonly IActivityLogRepository _activityLog;

    public FilesystemActionService(
        IWorkspaceToolService workspaceTools,
        IActivityLogRepository activityLog)
    {
        _workspaceTools = workspaceTools;
        _activityLog = activityLog;
    }

    public async Task<FilesystemActionResult> ReadFileAsync(
        string workspaceRoot,
        string relativePath,
        CancellationToken cancellationToken)
    {
        try
        {
            var content = await _workspaceTools.ReadTextAsync(
                workspaceRoot, relativePath, cancellationToken).ConfigureAwait(false);

            await AddActivityAsync(
                ActivityEventKind.FilesystemAction,
                $"Read file: {relativePath}",
                new { action = "read", path = relativePath, size = content.Length },
                cancellationToken).ConfigureAwait(false);

            return new(true, content, $"Read {content.Length} characters from {relativePath}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new(false, null, $"Failed to read {relativePath}: {ex.Message}");
        }
    }

    public async Task<FilesystemActionResult> WriteFileAsync(
        string workspaceRoot,
        string relativePath,
        string content,
        CancellationToken cancellationToken)
    {
        try
        {
            await _workspaceTools.WriteTextAtomicAsync(
                workspaceRoot, relativePath, content, cancellationToken).ConfigureAwait(false);

            await AddActivityAsync(
                ActivityEventKind.FilesystemAction,
                $"Wrote file: {relativePath}",
                new { action = "write", path = relativePath, size = content.Length },
                cancellationToken).ConfigureAwait(false);

            return new(true, null, $"Wrote {content.Length} characters to {relativePath}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new(false, null, $"Failed to write {relativePath}: {ex.Message}");
        }
    }

    public async Task<FilesystemActionResult> SearchFilesAsync(
        string workspaceRoot,
        string pattern,
        CancellationToken cancellationToken)
    {
        try
        {
            var files = await _workspaceTools.SearchFilesAsync(
                workspaceRoot, pattern, cancellationToken).ConfigureAwait(false);

            await AddActivityAsync(
                ActivityEventKind.FilesystemAction,
                $"Searched files: {pattern}",
                new { action = "search", pattern, count = files.Count },
                cancellationToken).ConfigureAwait(false);

            return new(true, string.Join('\n', files), $"Found {files.Count} files matching '{pattern}'");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new(false, null, $"Search failed: {ex.Message}");
        }
    }

    public async Task<CommandExecutionResult> RunCommandAsync(
        string command,
        string arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _workspaceTools.RunProcessAsync(
                new ProcessRequest(command, arguments, workingDirectory, timeout),
                cancellationToken).ConfigureAwait(false);

            var safeCommand = RedactSensitiveData(command);
            var safeArguments = RedactSensitiveData(arguments);
            await AddActivityAsync(
                ActivityEventKind.CommandRun,
                $"Ran command: {safeCommand} {safeArguments}".TrimEnd(),
                new
                {
                    command = safeCommand,
                    arguments = safeArguments,
                    exitCode = result.ExitCode,
                    duration = result.Duration.TotalMilliseconds
                },
                cancellationToken).ConfigureAwait(false);

            return new(result.ExitCode, result.StandardOutput, result.StandardError, result.TimedOut);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new(-1, string.Empty, "Command execution failed.", false);
        }
    }

    internal static string RedactSensitiveData(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var redacted = SensitiveOptionRegex.Replace(
            value, match => $"{match.Groups["name"].Value} [REDACTED]");
        redacted = SensitiveAssignmentRegex.Replace(
            redacted, match => $"{match.Groups["name"].Value}=[REDACTED]");
        redacted = AuthorizationRegex.Replace(
            redacted, match => $"{match.Groups["scheme"].Value} [REDACTED]");
        return TokenPrefixRegex.Replace(redacted, "[REDACTED]");
    }

    private Task AddActivityAsync(
        ActivityEventKind kind,
        string summary,
        object details,
        CancellationToken cancellationToken) =>
        _activityLog.AddEventAsync(
            new ActivityEvent(
                Guid.NewGuid(),
                kind,
                null,
                null,
                summary,
                JsonSerializer.Serialize(details),
                DateTimeOffset.UtcNow),
            cancellationToken);
}

public sealed record FilesystemActionResult(bool Succeeded, string? Content, string Message);

public sealed record CommandExecutionResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut);
