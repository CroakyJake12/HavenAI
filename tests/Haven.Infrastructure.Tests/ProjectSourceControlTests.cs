using Haven.Application;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class ProjectSourceControlTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "haven-scm-tests-" + Guid.NewGuid().ToString("N"));

    public ProjectSourceControlTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        File.WriteAllText(Path.Combine(_root, "src", "A.cs"), "class A {}");
    }

    [Fact]
    public void Porcelain_status_preserves_staged_unstaged_untracked_conflict_and_rename_state()
    {
        var parsed = ProjectIntelligenceService.ParsePorcelainStatus(" M src/A.cs\0M  src/B.cs\0?? src/C.cs\0UU src/D.cs\0R  src/New.cs\0src/Old.cs\0");

        Assert.Equal(5, parsed.Count);
        Assert.True(parsed.Single(item => item.Path == "src/A.cs").HasWorkingTreeChange);
        Assert.True(parsed.Single(item => item.Path == "src/B.cs").IsStaged);
        Assert.True(parsed.Single(item => item.Path == "src/C.cs").IsUntracked);
        Assert.True(parsed.Single(item => item.Path == "src/D.cs").IsConflicted);
        Assert.Equal("src/New.cs", parsed.Single(item => item.IndexStatus == "Renamed").Path);
    }

    [Fact]
    public async Task Stage_and_unstage_use_safe_git_verbs_and_refresh_typed_state()
    {
        var tools = new RecordingWorkspaceTools(_root, hasChanges: true);
        var service = new ProjectIntelligenceService(tools);

        var staged = await service.StageAsync(_root, "src/A.cs", CancellationToken.None);
        var unstaged = await service.UnstageAsync(_root, "src/A.cs", CancellationToken.None);

        Assert.True(staged.Changes.Single().IsStaged);
        Assert.False(unstaged.Changes.Single().IsStaged);
        Assert.Contains(tools.GitArguments, value => value.StartsWith("add -- ", StringComparison.Ordinal));
        Assert.Contains(tools.GitArguments, value => value.StartsWith("restore --staged -- ", StringComparison.Ordinal));
        Assert.DoesNotContain(tools.GitArguments, value => value.Contains("reset --hard", StringComparison.OrdinalIgnoreCase) || value.StartsWith("clean", StringComparison.OrdinalIgnoreCase) || value.Contains("--force", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Checkout_refuses_dirty_tree_and_never_runs_switch()
    {
        var tools = new RecordingWorkspaceTools(_root, hasChanges: true);
        var service = new ProjectIntelligenceService(tools);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CheckoutBranchAsync(_root, "feature", CancellationToken.None));

        Assert.DoesNotContain(tools.GitArguments, value => value.StartsWith("switch ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Stage_rejects_path_outside_project_root_before_git_mutation()
    {
        var tools = new RecordingWorkspaceTools(_root, hasChanges: true);
        var service = new ProjectIntelligenceService(tools);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.StageAsync(_root, "../escape.cs", CancellationToken.None));

        Assert.DoesNotContain(tools.GitArguments, value => value.StartsWith("add -- ", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private sealed class RecordingWorkspaceTools(string root, bool hasChanges) : IWorkspaceToolService
    {
        private bool _staged;
        private bool _hasChanges = hasChanges;
        private string _branch = "main";
        public List<string> GitArguments { get; } = [];

        public string ResolveWorkspacePath(string workspaceRoot, string relativePath)
        {
            var canonical = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var candidate = Path.IsPathRooted(relativePath) ? Path.GetFullPath(relativePath) : Path.GetFullPath(Path.Combine(canonical, relativePath));
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!candidate.Equals(canonical, comparison) && !candidate.StartsWith(canonical + Path.DirectorySeparatorChar, comparison)) throw new UnauthorizedAccessException();
            return candidate;
        }
        public Task<string> ReadTextAsync(string workspaceRoot, string relativePath, CancellationToken cancellationToken) => File.ReadAllTextAsync(ResolveWorkspacePath(workspaceRoot, relativePath), cancellationToken);
        public Task WriteTextAtomicAsync(string workspaceRoot, string relativePath, string content, CancellationToken cancellationToken) => File.WriteAllTextAsync(ResolveWorkspacePath(workspaceRoot, relativePath), content, cancellationToken);
        public Task<IReadOnlyList<string>> SearchFilesAsync(string workspaceRoot, string searchPattern, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<ProcessResult> RunProcessAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            Assert.Equal("git", request.FileName);
            GitArguments.Add(request.Arguments);
            var arguments = request.Arguments;
            if (arguments.StartsWith("add -- ", StringComparison.Ordinal)) { _staged = true; _hasChanges = true; return Ok(); }
            if (arguments.StartsWith("restore --staged -- ", StringComparison.Ordinal)) { _staged = false; _hasChanges = true; return Ok(); }
            if (arguments.StartsWith("switch ", StringComparison.Ordinal)) { _branch = arguments.Contains("feature", StringComparison.Ordinal) ? "feature" : _branch; return Ok(); }
            if (arguments == "rev-parse --is-inside-work-tree") return Ok("true\n");
            if (arguments.StartsWith("status --porcelain", StringComparison.Ordinal)) return Ok(_hasChanges ? (_staged ? "M  src/A.cs\0" : " M src/A.cs\0") : string.Empty);
            if (arguments == "branch --show-current") return Ok(_branch + "\n");
            if (arguments.StartsWith("for-each-ref ", StringComparison.Ordinal)) return Ok($"main\t{(_branch == "main" ? "*" : string.Empty)}\t\t\nfeature\t{(_branch == "feature" ? "*" : string.Empty)}\t\t\n");
            if (arguments.StartsWith("stash list", StringComparison.Ordinal)) return Ok();
            if (arguments == "worktree list --porcelain") return Ok($"worktree {root}\nHEAD abc123\nbranch refs/heads/{_branch}\n\n");
            if (arguments.StartsWith("log -n 30", StringComparison.Ordinal)) return Ok("abc123def\tabc123\tHaven\t2026-08-23T12:00:00+00:00\tCommit\n");
            if (arguments.StartsWith("diff ", StringComparison.Ordinal)) return Ok();
            return Ok();
        }

        private static Task<ProcessResult> Ok(string output = "") => Task.FromResult(new ProcessResult(0, output, string.Empty, TimeSpan.Zero, false));
    }
}
