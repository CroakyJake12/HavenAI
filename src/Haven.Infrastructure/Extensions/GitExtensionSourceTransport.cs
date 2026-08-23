using System.Diagnostics;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>Uses the user's connected/system Git credential helper; Haven never receives a raw GitHub password or token.</summary>
public sealed class GitExtensionSourceTransport : IExtensionSourceTransport
{
    public async Task<string> MaterializeAsync(ExtensionSource source, string destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw new IOException("Extension source destination must be empty.");
        Directory.CreateDirectory(destination);
        if (source.Type == ExtensionSourceType.LocalRepository)
        {
            var local = Path.GetFullPath(source.RepositoryUri);
            if (!Directory.Exists(local)) throw new DirectoryNotFoundException("Local extension source was not found.");
            Copy(local, destination);
            return destination;
        }
        var arguments = new List<string> { "clone", "--depth", "1", "--single-branch" };
        if (!string.IsNullOrWhiteSpace(source.Branch)) { arguments.Add("--branch"); arguments.Add(source.Branch); }
        arguments.Add("--"); arguments.Add(source.RepositoryUri); arguments.Add(destination);
        var start = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Git could not be started.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try { await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Git repository refresh failed: {SensitiveTextRedactor.Redact(error.Length > 0 ? error : output, 4_000)}");
        return destination;
    }

    private static void Copy(string source, string destination)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            if (relative.Split(Path.DirectorySeparatorChar).Any(part => part.Equals(".git", StringComparison.OrdinalIgnoreCase))) continue;
            if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("Extension sources cannot contain linked directories.");
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (relative.Split(Path.DirectorySeparatorChar).Any(part => part.Equals(".git", StringComparison.OrdinalIgnoreCase))) continue;
            if (File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("Extension sources cannot contain linked files.");
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
