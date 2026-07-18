/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/WorkspaceToolService.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns WorkspaceToolService. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Diagnostics;
using System.Text;
using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>
/// Represents workspace tool service and keeps its related state and behavior together.
/// </summary>
public sealed class WorkspaceToolService : IWorkspaceToolService
{
    /// <summary>
    /// Stores max output characters locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int MaxOutputCharacters = 1_000_000;

    /// <summary>
    /// Performs the resolve workspace path step owned by this component.
    /// </summary>
    public string ResolveWorkspacePath(string workspaceRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot)) throw new ArgumentException("A workspace root is required.", nameof(workspaceRoot));
        if (string.IsNullOrWhiteSpace(relativePath)) throw new ArgumentException("A workspace-relative path is required.", nameof(relativePath));

        var root = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (Path.IsPathRooted(relativePath))
        {
            var absoluteCandidate = Path.GetFullPath(relativePath);
            if (!IsWithinRoot(root, absoluteCandidate, comparison))
                throw new UnauthorizedAccessException("The requested path is outside the selected workspace.");
            return absoluteCandidate;
        }

        var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!IsWithinRoot(root, candidate, comparison))
            throw new UnauthorizedAccessException("The requested path is outside the selected workspace.");
        return candidate;
    }

    /// <summary>
    /// Performs read text asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<string> ReadTextAsync(string workspaceRoot, string relativePath, CancellationToken cancellationToken)
    {
        var path = ResolveWorkspacePath(workspaceRoot, relativePath);
        return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs write text atomic asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task WriteTextAtomicAsync(string workspaceRoot, string relativePath, string content, CancellationToken cancellationToken)
    {
        var path = ResolveWorkspacePath(workspaceRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".haven.tmp." + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Performs search files asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<IReadOnlyList<string>> SearchFilesAsync(string workspaceRoot, string searchPattern, CancellationToken cancellationToken)
    {
        var root = ResolveWorkspacePath(workspaceRoot, ".");
        return Task.Run<IReadOnlyList<string>>(() =>
        {
            var results = new List<string>();
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(root, path);
                if (relative.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                if (Path.GetFileName(path).Contains(searchPattern, StringComparison.OrdinalIgnoreCase)) results.Add(relative);
                if (results.Count >= 500) break;
            }
            return results;
        }, cancellationToken);
    }

    /// <summary>
    /// Runs run process async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    public async Task<ProcessResult> RunProcessAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(request.WorkingDirectory)) throw new DirectoryNotFoundException(request.WorkingDirectory);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = request.FileName,
                Arguments = request.Arguments,
                WorkingDirectory = request.WorkingDirectory,
                UseShellExecute = request.DetachGui,
                CreateNoWindow = !request.DetachGui,
                WindowStyle = request.DetachGui ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden,
                RedirectStandardOutput = !request.DetachGui,
                RedirectStandardError = !request.DetachGui
            },
            EnableRaisingEvents = true
        };
        if (!request.DetachGui && Path.GetFileName(request.FileName).Equals("powershell.exe", StringComparison.OrdinalIgnoreCase))
        {
            process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            process.StartInfo.StandardErrorEncoding = Encoding.UTF8;
        }
        if (request.Environment is not null)
            foreach (var pair in request.Environment) process.StartInfo.Environment[pair.Key] = pair.Value;

        var started = Stopwatch.GetTimestamp();
        if (!process.Start()) throw new InvalidOperationException($"Could not start {request.FileName}.");
        if (request.DetachGui) return new ProcessResult(0, string.Empty, string.Empty, Stopwatch.GetElapsedTime(started), false);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Timeout);
        var stdoutTask = ReadLimitedAsync(process.StandardOutput, timeout.Token);
        var stderrTask = ReadLimitedAsync(process.StandardError, timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return new ProcessResult(process.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false), Stopwatch.GetElapsedTime(started), false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return new ProcessResult(-1, await SafeResultAsync(stdoutTask).ConfigureAwait(false), await SafeResultAsync(stderrTask).ConfigureAwait(false), Stopwatch.GetElapsedTime(started), true);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
    }

    /// <summary>
    /// Reports whether within root applies to the current state.
    /// </summary>
    private static bool IsWithinRoot(string root, string candidate, StringComparison comparison)
    {
        if (candidate.Equals(root, comparison)) return true;
        var prefix = root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, comparison);
    }

    /// <summary>
    /// Performs read limited asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task<string> ReadLimitedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var builder = new StringBuilder();
        var truncated = false;
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            var remaining = MaxOutputCharacters - builder.Length;
            if (remaining > 0) builder.Append(buffer, 0, Math.Min(read, remaining));
            if (read > remaining) truncated = true;
        }
        if (truncated) builder.AppendLine("\n[output truncated]");
        return builder.ToString();
    }

    /// <summary>
    /// Performs safe result asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task<string> SafeResultAsync(Task<string> task)
    {
        try { return await task.ConfigureAwait(false); }
        catch { return string.Empty; }
    }

    /// <summary>
    /// Attempts to kill and reports the result without using failure for normal control flow.
    /// </summary>
    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
    }
}
