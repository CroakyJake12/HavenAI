using System.Text;
using System.Text.RegularExpressions;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed partial class ProjectIntelligenceService(IWorkspaceToolService processes) : IProjectIntelligenceService
{
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", "bin", "obj", "node_modules", ".idea", ".cache", "packages", "dist", "artifacts"
    };
    private static readonly HashSet<string> ProjectExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".sln", ".slnx", ".csproj", ".fsproj", ".vbproj", ".vcxproj"
    };
    private readonly Dictionary<string, string> _buildResults = new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<ProjectDiscoveryItem>> ScanAsync(string root, CancellationToken cancellationToken) => Task.Run<IReadOnlyList<ProjectDiscoveryItem>>(() =>
    {
        var canonicalRoot = Path.GetFullPath(root);
        if (!Directory.Exists(canonicalRoot)) throw new DirectoryNotFoundException(canonicalRoot);
        var results = new Dictionary<string, ProjectDiscoveryItem>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(canonicalRoot);
        while (pending.Count > 0 && results.Count < 1000)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            try
            {
                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    var info = new DirectoryInfo(child);
                    if (IgnoredDirectories.Contains(info.Name) || IsUnsafeLink(info)) continue;
                    pending.Push(child);
                }
                foreach (var path in Directory.EnumerateFiles(directory))
                {
                    if (!ProjectExtensions.Contains(Path.GetExtension(path))) continue;
                    var canonical = Path.GetFullPath(path);
                    var projectRoot = Path.GetDirectoryName(canonical)!;
                    var relative = Path.GetRelativePath(canonicalRoot, canonical);
                    var firstSegment = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).FirstOrDefault() ?? "Uncategorised";
                    var kind = Path.GetExtension(path).ToLowerInvariant() switch
                    {
                        ".sln" or ".slnx" => "Solution",
                        ".vcxproj" => "C++ project",
                        _ => ".NET project"
                    };
                    results[canonical] = new ProjectDiscoveryItem(Path.GetFileNameWithoutExtension(path), projectRoot, canonical, kind, firstSegment);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        var solutionRoots = results.Values.Where(item => item.Kind == "Solution").Select(item => item.RootPath).ToArray();
        return results.Values
            .Where(item => item.Kind == "Solution" || !solutionRoots.Any(rootPath => IsWithin(item.EntryPath, rootPath)))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }, cancellationToken);

    public async Task<ProjectStateSnapshot> GetStateAsync(string root, CancellationToken cancellationToken)
    {
        var branch = await GitTextAsync(root, "branch --show-current", cancellationToken).ConfigureAwait(false);
        var status = await GitTextAsync(root, "status --porcelain", cancellationToken).ConfigureAwait(false);
        var commit = await GitTextAsync(root, "log -1 --pretty=%h%x20%s", cancellationToken).ConfigureAwait(false);
        var counts = await GitTextAsync(root, "rev-list --left-right --count HEAD...@{upstream}", cancellationToken).ConfigureAwait(false);
        var parts = counts.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var ahead = parts.Length > 0 && int.TryParse(parts[0], out var parsedAhead) ? parsedAhead : 0;
        var behind = parts.Length > 1 && int.TryParse(parts[1], out var parsedBehind) ? parsedBehind : 0;
        var error = FindRecentError(root);
        var build = _buildResults.TryGetValue(Path.GetFullPath(root), out var value) ? value : "Not run in this Haven session";
        var recommendation = !string.IsNullOrWhiteSpace(error) ? "Explain and reproduce the most recent error"
            : !string.IsNullOrWhiteSpace(status) ? "Review and test the uncommitted work"
            : behind > 0 ? "Review upstream changes before starting new work"
            : "Open the latest project conversation or run a targeted build";
        return new ProjectStateSnapshot(Path.GetFullPath(root), string.IsNullOrWhiteSpace(branch) ? "No Git branch" : branch,
            !string.IsNullOrWhiteSpace(status), ahead, behind, string.IsNullOrWhiteSpace(commit) ? "No Git commit found" : commit,
            build, string.IsNullOrWhiteSpace(error) ? "No recent error found" : error, recommendation, DateTimeOffset.UtcNow);
    }

    public async Task<ReleaseRiskReport> ForecastReleaseRiskAsync(string root, CancellationToken cancellationToken)
    {
        var filesText = await GitTextAsync(root, "diff --name-only HEAD", cancellationToken).ConfigureAwait(false);
        var files = filesText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var riskAreas = new List<string>();
        var tests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var critical = new List<string>();
        var score = Math.Min(35, files.Length * 2);
        foreach (var file in files)
        {
            var lower = file.ToLowerInvariant();
            if (lower.EndsWith(".csproj") || lower.EndsWith(".props") || lower.EndsWith(".targets") || lower.Contains("package"))
            {
                score += 14; riskAreas.Add($"Dependency/build configuration changed: {file}"); tests.Add("Restore and build every affected target framework");
            }
            if (lower.Contains("migration") || lower.Contains("database") || lower.Contains("sqlite"))
            {
                score += 15; riskAreas.Add($"Persistent data path changed: {file}"); tests.Add("Test upgrade from the previous database schema and a clean install");
            }
            if (lower.Contains("auth") || lower.Contains("credential") || lower.Contains("permission") || lower.Contains("security"))
            {
                score += 20; riskAreas.Add($"Security-sensitive code changed: {file}"); tests.Add("Run permission-boundary and credential-storage tests");
                critical.Add($"Security-sensitive change requires explicit review: {file}");
            }
            if (lower.EndsWith(".axaml") || lower.Contains("view"))
            {
                score += 4; tests.Add("Smoke-test affected UI flows at minimum and maximum window sizes");
            }
            if (lower.Contains("browser") || lower.Contains("webview"))
            {
                score += 10; riskAreas.Add($"Native browser surface changed: {file}"); tests.Add("Open, navigate, reload, close, and reopen the embedded browser");
            }
        }
        if (files.Length > 20) { score += 15; riskAreas.Add("Wide change set crosses many files"); }
        if (files.Length > 0 && !files.Any(path => path.Contains("test", StringComparison.OrdinalIgnoreCase)))
        {
            score += 10; riskAreas.Add("No test files changed alongside the implementation");
        }
        score = Math.Clamp(score, 0, 100);
        var level = score >= 75 ? "Critical" : score >= 50 ? "High" : score >= 25 ? "Moderate" : "Low";
        if (files.Length == 0) riskAreas.Add("No uncommitted Git changes were found; risk is based on the current working tree only.");
        return new ReleaseRiskReport(score, level, files, riskAreas.Distinct().ToArray(), tests.ToArray(), critical);
    }

    public Task<string> FindIntentMatchesAsync(string root, string intent, CancellationToken cancellationToken) => Task.Run(() =>
    {
        var words = System.Text.RegularExpressions.Regex.Matches(intent.ToLowerInvariant(), "[a-z0-9_]{3,}")
            .Select(match => match.Value).Distinct().Take(12).ToArray();
        if (words.Length == 0) return "Describe what you are trying to accomplish with a few specific words.";
        var matches = new List<(int Score, string Path, string Evidence)>();
        foreach (var path in EnumerateTextFiles(root, 1800, cancellationToken))
        {
            var relative = Path.GetRelativePath(root, path);
            var score = words.Count(word => relative.Contains(word, StringComparison.OrdinalIgnoreCase)) * 5;
            string content;
            try { content = File.ReadAllText(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            if (content.Length > 120_000) content = content[..120_000];
            score += words.Count(word => content.Contains(word, StringComparison.OrdinalIgnoreCase));
            if (score == 0) continue;
            var evidence = words.FirstOrDefault(word => content.Contains(word, StringComparison.OrdinalIgnoreCase)) ?? "filename match";
            matches.Add((score, relative, evidence));
        }
        return matches.Count == 0 ? "No strong intent-aware matches were found."
            : string.Join(Environment.NewLine, matches.OrderByDescending(item => item.Score).ThenBy(item => item.Path).Take(25)
                .Select(item => $"{item.Path} — relevance {item.Score}; matched {item.Evidence}"));
    }, cancellationToken);

    public async Task<ProcessResult> RunBuildAsync(string root, CancellationToken cancellationToken)
    {
        var result = await RunPowerShellAsync(root, "dotnet build", TimeSpan.FromMinutes(12), cancellationToken).ConfigureAwait(false);
        _buildResults[Path.GetFullPath(root)] = result.ExitCode == 0
            ? $"Passed at {DateTimeOffset.Now:t} ({result.Duration.TotalSeconds:0.0}s)"
            : $"Failed at {DateTimeOffset.Now:t} (exit {result.ExitCode})";
        return result;
    }

    public Task<ProcessResult> RunTestsAsync(string root, CancellationToken cancellationToken)
    {
        if (File.Exists(Path.Combine(root, "package.json")))
            return processes.RunProcessAsync(new ProcessRequest("npm.cmd", "test", root, TimeSpan.FromMinutes(15)), cancellationToken);
        if (File.Exists(Path.Combine(root, "Cargo.toml")))
            return processes.RunProcessAsync(new ProcessRequest("cargo.exe", "test", root, TimeSpan.FromMinutes(20)), cancellationToken);
        if (File.Exists(Path.Combine(root, "pyproject.toml")) || File.Exists(Path.Combine(root, "pytest.ini")))
            return processes.RunProcessAsync(new ProcessRequest("python.exe", "-m pytest", root, TimeSpan.FromMinutes(20)), cancellationToken);
        return processes.RunProcessAsync(new ProcessRequest("dotnet.exe", "test", root, TimeSpan.FromMinutes(20)), cancellationToken);
    }

    public Task<ProcessResult> InitializeGitAsync(string root, CancellationToken cancellationToken) =>
        RunGitAsync(root, "init", TimeSpan.FromMinutes(2), cancellationToken);

    public async Task<ProcessResult> ConnectGitRemoteAsync(string root, string remoteUrl, CancellationToken cancellationToken)
    {
        var url = remoteUrl.Trim();
        if (url.Length is < 4 or > 2048 || url.Any(char.IsControl) || url.Contains('"') || url.Any(char.IsWhiteSpace) ||
            !(Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" or "ssh" or "git") && !GitScpUrlPattern().IsMatch(url))
            throw new ArgumentException("Use a valid HTTPS, SSH, git, or git@host:path remote URL.", nameof(remoteUrl));
        var current = await RunGitAsync(root, "remote get-url origin", TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        var operation = current.ExitCode == 0 ? "remote set-url origin" : "remote add origin";
        return await RunGitAsync(root, $"{operation} \"{url}\"", TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProcessResult> RunBugTimeMachineAsync(string root, string reproductionCommand, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reproductionCommand)) throw new ArgumentException("A deterministic reproduction command is required.", nameof(reproductionCommand));
        var dirty = await GitTextAsync(root, "status --porcelain", cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(dirty))
            throw new InvalidOperationException("Bug Time Machine requires a clean Git working tree so it cannot overwrite uncommitted work.");
        var first = (await GitTextAsync(root, "rev-list --max-parents=0 HEAD", cancellationToken).ConfigureAwait(false))
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(first)) throw new InvalidOperationException("No Git history was found for this project.");

        var start = await RunGitAsync(root, $"bisect start HEAD {first}", TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
        if (start.ExitCode != 0) return start;
        ProcessResult bisect;
        try
        {
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(reproductionCommand));
            bisect = await RunGitAsync(root, $"bisect run powershell.exe -NoProfile -NonInteractive -EncodedCommand {encoded}", TimeSpan.FromMinutes(45), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try { await RunGitAsync(root, "bisect reset", TimeSpan.FromMinutes(2), CancellationToken.None).ConfigureAwait(false); }
            catch { }
        }

        if (bisect.ExitCode != 0) return bisect;
        var match = Regex.Match(bisect.StandardOutput, @"# first bad commit:\s*\[(?<hash>[0-9a-f]{7,40})\b", RegexOptions.IgnoreCase);
        if (!match.Success) return bisect with
        {
            StandardOutput = bisect.StandardOutput.TrimEnd() + "\n\nHaven restored the clean working tree, but Git did not report a first failing commit."
        };

        var hash = match.Groups["hash"].Value;
        var details = await RunGitAsync(root, $"show --stat --format=fuller --no-renames {hash}", TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
        var patch = await RunGitAsync(root, $"show --format= --no-ext-diff --unified=3 --no-renames {hash}", TimeSpan.FromMinutes(3), cancellationToken).ConfigureAwait(false);
        var output = new StringBuilder(bisect.StandardOutput.TrimEnd())
            .Append("\n\nHaven restored the clean working tree.\nFirst failing commit details:\n")
            .Append(string.IsNullOrWhiteSpace(details.StandardOutput) ? details.StandardError.Trim() : details.StandardOutput.Trim())
            .Append("\n\nMeaningful patch introduced by the first failing commit:\n")
            .Append(Truncate(string.IsNullOrWhiteSpace(patch.StandardOutput) ? patch.StandardError.Trim() : patch.StandardOutput.Trim(), 80_000))
            .ToString();
        return bisect with
        {
            StandardOutput = output,
            StandardError = string.Join(Environment.NewLine, new[] { bisect.StandardError, details.StandardError, patch.StandardError }.Where(value => !string.IsNullOrWhiteSpace(value))),
            Duration = bisect.Duration + details.Duration + patch.Duration
        };
    }

    public async Task LaunchEditorAsync(string root, CancellationToken cancellationToken)
    {
        var entry = Directory.EnumerateFiles(root, "*.sln*", SearchOption.TopDirectoryOnly).FirstOrDefault()
                    ?? Directory.EnumerateFiles(root, "*.*proj", SearchOption.TopDirectoryOnly).FirstOrDefault();
        var target = entry ?? root;
        var vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer", "vswhere.exe");
        string? editor = null;
        if (File.Exists(vswhere))
        {
            var lookup = await processes.RunProcessAsync(new ProcessRequest(vswhere, "-latest -products * -requires Microsoft.Component.MSBuild -find Common7\\IDE\\devenv.exe", root, TimeSpan.FromSeconds(20)), cancellationToken).ConfigureAwait(false);
            editor = lookup.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(File.Exists);
        }
        if (editor is not null)
            await processes.RunProcessAsync(new ProcessRequest(editor, $"\"{target}\"", root, TimeSpan.FromSeconds(5), DetachGui: true), cancellationToken).ConfigureAwait(false);
        else
            await processes.RunProcessAsync(new ProcessRequest("explorer.exe", $"\"{target}\"", root, TimeSpan.FromSeconds(5), DetachGui: true), cancellationToken).ConfigureAwait(false);
    }

    public async Task LaunchTerminalAsync(string root, CancellationToken cancellationToken)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var terminal = Path.Combine(local, "Microsoft", "WindowsApps", "wt.exe");
        if (File.Exists(terminal))
            await processes.RunProcessAsync(new ProcessRequest(terminal, $"-d \"{root}\"", root, TimeSpan.FromSeconds(5), DetachGui: true), cancellationToken).ConfigureAwait(false);
        else
            await processes.RunProcessAsync(new ProcessRequest("powershell.exe", "-NoExit", root, TimeSpan.FromSeconds(5), DetachGui: true), cancellationToken).ConfigureAwait(false);
    }

    public async Task LaunchLocalServerAsync(string root, CancellationToken cancellationToken)
    {
        var command = File.Exists(Path.Combine(root, "package.json")) ? "npm run dev" : "dotnet run";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes($"Set-Location -LiteralPath '{root.Replace("'", "''", StringComparison.Ordinal)}'; {command}"));
        await processes.RunProcessAsync(new ProcessRequest("powershell.exe", $"-NoExit -EncodedCommand {encoded}", root, TimeSpan.FromSeconds(5), DetachGui: true), cancellationToken).ConfigureAwait(false);
    }

    private Task<ProcessResult> RunGitAsync(string root, string arguments, TimeSpan timeout, CancellationToken cancellationToken) =>
        processes.RunProcessAsync(new ProcessRequest("git.exe", arguments, root, timeout), cancellationToken);

    private async Task<string> GitTextAsync(string root, string arguments, CancellationToken cancellationToken)
    {
        try
        {
            var result = await RunGitAsync(root, arguments, TimeSpan.FromSeconds(40), cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0 ? result.StandardOutput.Trim() : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException) { return string.Empty; }
    }

    private Task<ProcessResult> RunPowerShellAsync(string root, string command, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
        return processes.RunProcessAsync(new ProcessRequest("powershell.exe", $"-NoProfile -NonInteractive -EncodedCommand {encoded}", root, timeout), cancellationToken);
    }

    private static IEnumerable<string> EnumerateTextFiles(string root, int limit, CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(root));
        var count = 0;
        while (pending.Count > 0 && count < limit)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            IEnumerable<string> directories;
            IEnumerable<string> files;
            try { directories = Directory.EnumerateDirectories(directory); files = Directory.EnumerateFiles(directory); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var child in directories)
            {
                var info = new DirectoryInfo(child);
                if (!IgnoredDirectories.Contains(info.Name) && !IsUnsafeLink(info)) pending.Push(child);
            }
            foreach (var file in files)
            {
                if (++count > limit) yield break;
                var info = new FileInfo(file);
                if (info.Length <= 2_000_000 && IsTextExtension(info.Extension)) yield return file;
            }
        }
    }

    private static bool IsTextExtension(string extension) => new[]
    {
        ".cs", ".fs", ".vb", ".cpp", ".h", ".axaml", ".xaml", ".xml", ".json", ".md", ".txt", ".log", ".ps1", ".js", ".ts", ".tsx", ".jsx", ".css", ".html", ".py", ".go", ".rs", ".yaml", ".yml", ".toml", ".props", ".targets", ".csproj", ".sln"
    }.Contains(extension, StringComparer.OrdinalIgnoreCase);

    private static string FindRecentError(string root)
    {
        try
        {
            var candidates = EnumerateTextFiles(root, 1200, CancellationToken.None)
                .Where(path => Path.GetExtension(path).Equals(".log", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path).Contains("error", StringComparison.OrdinalIgnoreCase))
                .Select(path => new FileInfo(path)).OrderByDescending(info => info.LastWriteTimeUtc).Take(8);
            foreach (var info in candidates)
            {
                var line = File.ReadLines(info.FullName).Reverse().FirstOrDefault(value => value.Contains("error", StringComparison.OrdinalIgnoreCase) || value.Contains("exception", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(line)) return $"{Path.GetRelativePath(root, info.FullName)}: {Truncate(line.Trim(), 240)}";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return string.Empty;
    }

    private static bool IsUnsafeLink(DirectoryInfo info)
    {
        try { return info.Attributes.HasFlag(FileAttributes.ReparsePoint) && !string.IsNullOrWhiteSpace(info.LinkTarget); }
        catch (IOException) { return true; }
    }

    private static bool IsWithin(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return !relative.Equals("..", StringComparison.Ordinal) && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length] + "…";

    [GeneratedRegex(@"^git@[A-Za-z0-9.-]+:[A-Za-z0-9_./-]+(?:\.git)?$")]
    private static partial Regex GitScpUrlPattern();
}
