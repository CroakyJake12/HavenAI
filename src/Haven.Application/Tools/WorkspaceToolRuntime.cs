/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/WorkspaceToolRuntime.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns WorkspaceToolResult, WorkspaceToolRuntime, WorkspaceMutation. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Represents workspace tool result and keeps its related state and behavior together.
/// </summary>
public sealed record WorkspaceToolResult(ToolActivity Activity, string Output);

/// <summary>
/// Represents workspace tool runtime and keeps its related state and behavior together.
/// </summary>
public sealed class WorkspaceToolRuntime(IWorkspaceToolService tools, IWorkspaceStateRepository? history = null)
{
    /// <summary>
    /// Stores max file characters locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int MaxFileCharacters = 1_000_000;
    /// <summary>
    /// Stores max tool output characters locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int MaxToolOutputCharacters = 120_000;
    /// <summary>
    /// Stores change sets locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly WorkspaceChangeSetService _changeSets = new(tools);
    /// <summary>
    /// Stores ignored directories locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", ".vs", ".idea", "node_modules", "bin", "obj", "dist", "build", "target", ".venv", "venv"
    };

    /// <summary>
    /// Gets or updates definitions, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<OllamaToolDefinition> Definitions =>
    [
        Definition("list_files", "List files and folders inside the selected Haven workspace.",
            new() { ["path"] = StringProperty("Workspace-relative folder, or . for the root."), ["max_depth"] = IntegerProperty("Recursion depth from 1 to 10.") }),
        Definition("read_file", "Read a UTF-8 text file inside the selected workspace.",
            new() { ["path"] = StringProperty("Workspace-relative file path.") }, "path"),
        Definition("search_files", "Search text files in the workspace for a literal query.",
            new() { ["query"] = StringProperty("Text to find."), ["path"] = StringProperty("Optional workspace-relative folder."), ["max_results"] = IntegerProperty("Maximum matches from 1 to 200.") }, "query"),
        Definition("write_file", "Create or completely replace a UTF-8 text file in the selected workspace using an atomic write.",
            new() { ["path"] = StringProperty("Workspace-relative file path."), ["content"] = StringProperty("Complete new file contents.") }, "path", "content"),
        Definition("replace_in_file", "Replace exact text in a workspace file. Prefer this for focused edits.",
            new() { ["path"] = StringProperty("Workspace-relative file path."), ["old_text"] = StringProperty("Exact text to replace."), ["new_text"] = StringProperty("Replacement text."), ["replace_all"] = BooleanProperty("Replace every match when true.") }, "path", "old_text", "new_text"),
        Definition("preview_change_set", "Preflight a transactional multi-file change set without writing files. Returns hashes and line impact for review.",
            new() { ["changes_json"] = StringProperty("JSON array of objects with path, content, and optional expectedSha256.") }, "changes_json"),
        Definition("apply_change_set", "Apply a preflightable multi-file change set transactionally. If any write fails, earlier writes are rolled back.",
            new() { ["changes_json"] = StringProperty("JSON array of objects with path, content, and optional expectedSha256.") }, "changes_json"),
        Definition("run_command", "Run a PowerShell command in the selected workspace and return its exit code, output, and errors.",
            new() { ["command"] = StringProperty("PowerShell command."), ["timeout_seconds"] = IntegerProperty("Timeout from 1 to 900 seconds.") }, "command"),
        Definition("run_tests", "Detect and run this workspace's tests, or run a supplied test command.",
            new() { ["command"] = StringProperty("Optional explicit PowerShell test command."), ["timeout_seconds"] = IntegerProperty("Timeout from 1 to 1800 seconds.") })
    ];

    /// <summary>
    /// Runs execute async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    public async Task<WorkspaceToolResult> ExecuteAsync(string workspaceRoot, OllamaToolCall call, CancellationToken cancellationToken, Guid? conversationId = null, Guid? containerId = null)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            IReadOnlyList<WorkspaceMutation> mutations = [];
            string output;
            switch (call.Name)
            {
                case "write_file":
                    mutations = [await WriteFileAsync(workspaceRoot, RequiredText(call, "path"), Text(call, "content"), cancellationToken).ConfigureAwait(false)];
                    output = mutations[0].Output;
                    break;
                case "replace_in_file":
                    mutations = [await ReplaceInFileAsync(workspaceRoot, RequiredText(call, "path"), RequiredText(call, "old_text"), Text(call, "new_text"), Boolean(call, "replace_all"), cancellationToken).ConfigureAwait(false)];
                    output = mutations[0].Output;
                    break;
                case "preview_change_set":
                    output = await PreviewChangeSetAsync(workspaceRoot, RequiredText(call, "changes_json"), cancellationToken).ConfigureAwait(false);
                    break;
                case "apply_change_set":
                    var applied = await _changeSets.ApplyAsync(workspaceRoot, RequiredText(call, "changes_json"), cancellationToken).ConfigureAwait(false);
                    mutations = applied.Changes.Select(change => new WorkspaceMutation(change.Path, change.Before, change.After,
                        $"Applied change-set entry {change.Path} (+{change.LinesAdded}/-{change.LinesRemoved} lines).", change.LinesAdded, change.LinesRemoved)).ToArray();
                    output = applied.Summary;
                    break;
                default:
                    output = call.Name switch
                    {
                        "list_files" => await ListFilesAsync(workspaceRoot, Text(call, "path", "."), Integer(call, "max_depth", 5), cancellationToken).ConfigureAwait(false),
                        "read_file" => await ReadFileAsync(workspaceRoot, RequiredText(call, "path"), cancellationToken).ConfigureAwait(false),
                        "search_files" => await SearchFilesAsync(workspaceRoot, Text(call, "path", "."), RequiredText(call, "query"), Integer(call, "max_results", 100), cancellationToken).ConfigureAwait(false),
                        "run_command" => await RunCommandAsync(workspaceRoot, RequiredText(call, "command"), Integer(call, "timeout_seconds", 120), cancellationToken).ConfigureAwait(false),
                        "run_tests" => await RunTestsAsync(workspaceRoot, Text(call, "command"), Integer(call, "timeout_seconds", 600), cancellationToken).ConfigureAwait(false),
                        _ => throw new InvalidOperationException($"Unknown workspace tool '{call.Name}'.")
                    };
                    break;
            }

            if (history is not null)
            {
                foreach (var mutation in mutations)
                {
                    await history.AddVersionAsync(new WorkspaceVersion(Guid.NewGuid(), conversationId, containerId, Path.GetFullPath(workspaceRoot),
                        mutation.Path, WorkspaceVersionKind.Edit, mutation.Before, mutation.After, mutation.Output,
                        mutation.LinesAdded, mutation.LinesRemoved, DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
                }
            }

            output = Truncate(output, MaxToolOutputCharacters);
            var added = mutations.Sum(item => item.LinesAdded);
            var removed = mutations.Sum(item => item.LinesRemoved);
            return new WorkspaceToolResult(
                new ToolActivity(Guid.NewGuid(), HumanLabel(call.Name), FirstLine(output), true, Stopwatch.GetElapsedTime(started), DateTimeOffset.UtcNow, added, removed),
                output);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var output = $"Tool error: {ex.Message}";
            return new WorkspaceToolResult(
                new ToolActivity(Guid.NewGuid(), HumanLabel(call.Name), ex.Message, false, Stopwatch.GetElapsedTime(started), DateTimeOffset.UtcNow),
                output);
        }
    }

    /// <summary>
    /// Performs preview change set asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<string> PreviewChangeSetAsync(string root, string json, CancellationToken cancellationToken)
    {
        var preview = await _changeSets.PreviewAsync(root, json, cancellationToken).ConfigureAwait(false);
        var builder = new StringBuilder();
        builder.Append("Change set preview: ").Append(preview.Count).AppendLine(preview.Count == 1 ? " file" : " files");
        foreach (var item in preview)
        {
            builder.Append("- ").Append(item.Path)
                .Append(item.Existed ? " [modify]" : " [create]")
                .Append(" +").Append(item.LinesAdded).Append("/-").Append(item.LinesRemoved)
                .Append(" before=").Append(item.BeforeSha256)
                .Append(" after=").Append(item.AfterSha256).AppendLine();
        }
        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Performs list files asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<string> ListFilesAsync(string root, string relativePath, int maxDepth, CancellationToken cancellationToken)
    {
        var start = tools.ResolveWorkspacePath(root, relativePath);
        if (!Directory.Exists(start)) throw new DirectoryNotFoundException(relativePath);
        maxDepth = Math.Clamp(maxDepth, 1, 10);
        var baseDepth = start.Count(character => character == Path.DirectorySeparatorChar || character == Path.AltDirectorySeparatorChar);
        var results = new List<string>();
        var pending = new Stack<string>();
        pending.Push(start);
        while (pending.Count > 0 && results.Count < 2000)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            var depth = current.Count(character => character == Path.DirectorySeparatorChar || character == Path.AltDirectorySeparatorChar) - baseDepth;
            foreach (var entry in Directory.EnumerateFileSystemEntries(current).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(entry);
                var relative = Path.GetRelativePath(root, entry).Replace(Path.DirectorySeparatorChar, '/');
                if (Directory.Exists(entry))
                {
                    if (IgnoredDirectories.Contains(name)) continue;
                    results.Add(relative + "/");
                    if (depth + 1 < maxDepth) pending.Push(entry);
                }
                else results.Add($"{relative} ({new FileInfo(entry).Length} bytes)");
                if (results.Count >= 2000) break;
            }
        }
        await Task.CompletedTask;
        return results.Count == 0 ? "Workspace folder is empty." : string.Join('\n', results);
    }

    /// <summary>
    /// Performs read file asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<string> ReadFileAsync(string root, string path, CancellationToken cancellationToken)
    {
        var resolved = tools.ResolveWorkspacePath(root, path);
        var info = new FileInfo(resolved);
        if (!info.Exists) throw new FileNotFoundException("Workspace file was not found.", path);
        if (info.Length > 4 * 1024 * 1024) throw new InvalidOperationException("File is larger than Haven's 4 MB text-read limit.");
        var content = await tools.ReadTextAsync(root, path, cancellationToken).ConfigureAwait(false);
        if (content.IndexOf('\0') >= 0) throw new InvalidOperationException("File appears to be binary.");
        return Truncate(content, MaxFileCharacters);
    }

    /// <summary>
    /// Performs search files asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<string> SearchFilesAsync(string root, string relativePath, string query, int maxResults, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("Search query is required.");
        var start = tools.ResolveWorkspacePath(root, relativePath);
        if (!Directory.Exists(start)) throw new DirectoryNotFoundException(relativePath);
        maxResults = Math.Clamp(maxResults, 1, 200);
        var matches = new List<string>();
        foreach (var path in Directory.EnumerateFiles(start, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ContainsIgnoredDirectory(root, path)) continue;
            var info = new FileInfo(path);
            if (info.Length > 2 * 1024 * 1024) continue;
            string[] lines;
            try { lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException) { continue; }
            for (var index = 0; index < lines.Length; index++)
            {
                if (!lines[index].Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                matches.Add($"{Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/')}:{index + 1}: {Truncate(lines[index].Trim(), 300)}");
                if (matches.Count >= maxResults) return string.Join('\n', matches) + "\n[search result limit reached]";
            }
        }
        return matches.Count == 0 ? "No matches found." : string.Join('\n', matches);
    }

    /// <summary>
    /// Performs write file asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<WorkspaceMutation> WriteFileAsync(string root, string path, string content, CancellationToken cancellationToken)
    {
        string before;
        try { before = await tools.ReadTextAsync(root, path, cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { before = string.Empty; }
        await tools.WriteTextAtomicAsync(root, path, content, cancellationToken).ConfigureAwait(false);
        var (added, removed) = CountLineChanges(before, content);
        return new WorkspaceMutation(path, before, content, $"Wrote {path} ({content.Length} characters, +{added}/-{removed} lines).", added, removed);
    }

    /// <summary>
    /// Performs replace in file asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<WorkspaceMutation> ReplaceInFileAsync(string root, string path, string oldText, string newText, bool replaceAll, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(oldText)) throw new ArgumentException("old_text is required.");
        var content = await tools.ReadTextAsync(root, path, cancellationToken).ConfigureAwait(false);
        var first = content.IndexOf(oldText, StringComparison.Ordinal);
        if (first < 0) throw new InvalidOperationException("Exact old_text was not found in the file.");
        int replacements;
        string updated;
        if (replaceAll)
        {
            replacements = CountOccurrences(content, oldText);
            updated = content.Replace(oldText, newText, StringComparison.Ordinal);
        }
        else
        {
            replacements = 1;
            updated = string.Concat(content.AsSpan(0, first), newText, content.AsSpan(first + oldText.Length));
        }
        await tools.WriteTextAtomicAsync(root, path, updated, cancellationToken).ConfigureAwait(false);
        var (added, removed) = CountLineChanges(content, updated);
        return new WorkspaceMutation(path, content, updated,
            $"Updated {path} ({replacements} replacement{(replacements == 1 ? string.Empty : "s")}, +{added}/-{removed} lines).", added, removed);
    }

    /// <summary>
    /// Runs run command async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    private async Task<string> RunCommandAsync(string root, string command, int timeoutSeconds, CancellationToken cancellationToken)
    {
        timeoutSeconds = Math.Clamp(timeoutSeconds, 1, 900);
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
        var result = await tools.RunProcessAsync(new ProcessRequest(
            "powershell.exe", $"-NoProfile -NonInteractive -EncodedCommand {encoded}", root, TimeSpan.FromSeconds(timeoutSeconds)), cancellationToken).ConfigureAwait(false);
        return FormatProcess(result);
    }

    /// <summary>
    /// Runs run tests async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    private async Task<string> RunTestsAsync(string root, string command, int timeoutSeconds, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            command = Directory.EnumerateFiles(root, "*.sln", SearchOption.TopDirectoryOnly).Any() ? "dotnet test"
                : File.Exists(Path.Combine(root, "package.json")) ? "npm test"
                : File.Exists(Path.Combine(root, "go.mod")) ? "go test ./..."
                : throw new InvalidOperationException("No supported test project was detected. Supply a test command.");
        }
        return await RunCommandAsync(root, command, Math.Clamp(timeoutSeconds, 1, 1800), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs the format process step owned by this component.
    /// </summary>
    private static string FormatProcess(ProcessResult result)
    {
        var builder = new StringBuilder();
        builder.Append("Exit code: ").Append(result.ExitCode).Append(" · Duration: ").Append(result.Duration.TotalSeconds.ToString("0.0")).AppendLine("s");
        if (result.TimedOut) builder.AppendLine("Command timed out.");
        if (!string.IsNullOrWhiteSpace(result.StandardOutput)) builder.AppendLine("STDOUT:").AppendLine(result.StandardOutput.TrimEnd());
        if (!string.IsNullOrWhiteSpace(result.StandardError)) builder.AppendLine("STDERR:").AppendLine(result.StandardError.TrimEnd());
        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Performs the definition step owned by this component.
    /// </summary>
    private static OllamaToolDefinition Definition(string name, string description, Dictionary<string, object> properties, params string[] required) => new(name, description, properties, required);
    /// <summary>
    /// Performs the string property step owned by this component.
    /// </summary>
    private static Dictionary<string, object> StringProperty(string description) => new() { ["type"] = "string", ["description"] = description };
    /// <summary>
    /// Performs the integer property step owned by this component.
    /// </summary>
    private static Dictionary<string, object> IntegerProperty(string description) => new() { ["type"] = "integer", ["description"] = description };
    /// <summary>
    /// Performs the boolean property step owned by this component.
    /// </summary>
    private static Dictionary<string, object> BooleanProperty(string description) => new() { ["type"] = "boolean", ["description"] = description };

    /// <summary>
    /// Performs the required text step owned by this component.
    /// </summary>
    private static string RequiredText(OllamaToolCall call, string key)
    {
        var value = Text(call, key);
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{key} is required.") : value;
    }

    /// <summary>
    /// Performs the text step owned by this component.
    /// </summary>
    private static string Text(OllamaToolCall call, string key, string fallback = "") =>
        call.Arguments.TryGetValue(key, out var value) ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : value.ToString() : fallback;
    /// <summary>
    /// Performs the integer step owned by this component.
    /// </summary>
    private static int Integer(OllamaToolCall call, string key, int fallback) =>
        call.Arguments.TryGetValue(key, out var value) && value.TryGetInt32(out var result) ? result : fallback;
    /// <summary>
    /// Performs the boolean step owned by this component.
    /// </summary>
    private static bool Boolean(OllamaToolCall call, string key) =>
        call.Arguments.TryGetValue(key, out var value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var result) && result);

    /// <summary>
    /// Performs the contains ignored directory step owned by this component.
    /// </summary>
    private static bool ContainsIgnoredDirectory(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(IgnoredDirectories.Contains);
    }

    /// <summary>
    /// Performs the count occurrences step owned by this component.
    /// </summary>
    private static int CountOccurrences(string content, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = content.IndexOf(value, offset, StringComparison.Ordinal)) >= 0) { count++; offset += value.Length; }
        return count;
    }

    private static (int Added, int Removed) CountLineChanges(string before, string after)
    {
        var oldLines = before.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var newLines = after.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var prefix = 0;
        while (prefix < oldLines.Length && prefix < newLines.Length && oldLines[prefix] == newLines[prefix]) prefix++;
        var suffix = 0;
        while (suffix < oldLines.Length - prefix && suffix < newLines.Length - prefix && oldLines[oldLines.Length - 1 - suffix] == newLines[newLines.Length - 1 - suffix]) suffix++;
        return (Math.Max(0, newLines.Length - prefix - suffix), Math.Max(0, oldLines.Length - prefix - suffix));
    }

    /// <summary>
    /// Performs the human label step owned by this component.
    /// </summary>
    private static string HumanLabel(string name) => name switch
    {
        "list_files" => "Listed project files",
        "read_file" => "Read a file",
        "search_files" => "Searched project files",
        "write_file" => "Wrote a file",
        "replace_in_file" => "Edited a file",
        "preview_change_set" => "Previewed a change set",
        "apply_change_set" => "Applied a change set",
        "run_command" => "Ran a command",
        "run_tests" => "Ran tests",
        _ => name.Replace('_', ' ')
    };

    /// <summary>
    /// Performs the first line step owned by this component.
    /// </summary>
    private static string FirstLine(string value) => value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "Completed";
    /// <summary>
    /// Performs the truncate step owned by this component.
    /// </summary>
    private static string Truncate(string value, int limit) => value.Length <= limit ? value : value[..limit] + "\n[truncated]";
    /// <summary>
    /// Represents workspace mutation and keeps its related state and behavior together.
    /// </summary>
    private sealed record WorkspaceMutation(string Path, string Before, string After, string Output, int LinesAdded, int LinesRemoved);
}