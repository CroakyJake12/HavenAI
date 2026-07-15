using System.Diagnostics;
using System.Text;
using Haven.Application;

namespace Haven.Infrastructure;

public sealed class WorkspaceToolService : IWorkspaceToolService
{
    private const int MaxOutputCharacters = 1_000_000;

    public string ResolveWorkspacePath(string workspaceRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot)) throw new ArgumentException("A workspace root is required.", nameof(workspaceRoot));
        var root = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        // If the model passes an absolute path that is already inside the workspace, strip the root prefix.
        if (Path.IsPathRooted(relativePath))
        {
            var absCandidate = Path.GetFullPath(relativePath);
            if (absCandidate.StartsWith(root, cmp))
            {
                relativePath = absCandidate[root.Length..];
            }
            else
            {
                throw new UnauthorizedAccessException("The requested path is outside the selected workspace.");
            }
        }
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!candidate.StartsWith(root, cmp))
            throw new UnauthorizedAccessException("The requested path is outside the selected workspace.");
        return candidate;
    }

    public async Task<string> ReadTextAsync(string workspaceRoot, string relativePath, CancellationToken cancellationToken)
    {
        var path = ResolveWorkspacePath(workspaceRoot, relativePath);
        return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteTextAtomicAsync(string workspaceRoot, string relativePath, string content, CancellationToken cancellationToken)
    {
        var path = ResolveWorkspacePath(workspaceRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".haven.tmp." + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, true);
    }

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

    private static async Task<string> SafeResultAsync(Task<string> task)
    {
        try { return await task.ConfigureAwait(false); }
        catch { return string.Empty; }
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
    }
}
