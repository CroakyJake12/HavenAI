/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/WorkspaceToolRuntimeTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns WorkspaceToolRuntimeTests, FailOnSecondWriteService. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;
using Haven.Application;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents workspace tool runtime tests and keeps its related state and behavior together.
/// </summary>
public sealed class WorkspaceToolRuntimeTests : IDisposable
{
    /// <summary>
    /// Stores root locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "haven-runtime-tests", Guid.NewGuid().ToString("N"));

    public WorkspaceToolRuntimeTests() => Directory.CreateDirectory(_root);

    /// <summary>
    /// Performs the write read and replace remain inside workspace step owned by this component.
    /// </summary>
    [Fact]
    public async Task WriteReadAndReplaceRemainInsideWorkspace()
    {
        var runtime = new WorkspaceToolRuntime(new WorkspaceToolService());
        var write = await runtime.ExecuteAsync(_root, Call("write_file", new { path = "notes/item.txt", content = "before" }), CancellationToken.None);
        var replace = await runtime.ExecuteAsync(_root, Call("replace_in_file", new { path = "notes/item.txt", old_text = "before", new_text = "after" }), CancellationToken.None);
        var read = await runtime.ExecuteAsync(_root, Call("read_file", new { path = "notes/item.txt" }), CancellationToken.None);
        Assert.True(write.Activity.Succeeded);
        Assert.True(replace.Activity.Succeeded);
        Assert.True(read.Activity.Succeeded);
        Assert.Equal("after", read.Output);
    }

    /// <summary>
    /// Performs the preview change set does not write and returns review hashes step owned by this component.
    /// </summary>
    [Fact]
    public async Task PreviewChangeSetDoesNotWriteAndReturnsReviewHashes()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "one.txt"), "before");
        var runtime = new WorkspaceToolRuntime(new WorkspaceToolService());
        var changes = JsonSerializer.Serialize(new[]
        {
            new { path = "one.txt", content = "after" },
            new { path = "two.txt", content = "created" }
        });

        var result = await runtime.ExecuteAsync(_root, Call("preview_change_set", new { changes_json = changes }), CancellationToken.None);

        Assert.True(result.Activity.Succeeded);
        Assert.Contains("one.txt [modify]", result.Output, StringComparison.Ordinal);
        Assert.Contains("two.txt [create]", result.Output, StringComparison.Ordinal);
        Assert.Equal("before", await File.ReadAllTextAsync(Path.Combine(_root, "one.txt")));
        Assert.False(File.Exists(Path.Combine(_root, "two.txt")));
    }

    /// <summary>
    /// Performs the apply change set writes all files through real tool entry point step owned by this component.
    /// </summary>
    [Fact]
    public async Task ApplyChangeSetWritesAllFilesThroughRealToolEntryPoint()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "one.txt"), "before");
        var runtime = new WorkspaceToolRuntime(new WorkspaceToolService());
        var changes = JsonSerializer.Serialize(new[]
        {
            new { path = "one.txt", content = "after" },
            new { path = "folder/two.txt", content = "created" }
        });

        var result = await runtime.ExecuteAsync(_root, Call("apply_change_set", new { changes_json = changes }), CancellationToken.None);

        Assert.True(result.Activity.Succeeded);
        Assert.Contains("Applied 2 workspace file changes transactionally", result.Output, StringComparison.Ordinal);
        Assert.Equal("after", await File.ReadAllTextAsync(Path.Combine(_root, "one.txt")));
        Assert.Equal("created", await File.ReadAllTextAsync(Path.Combine(_root, "folder", "two.txt")));
    }

    /// <summary>
    /// Performs the stale expected hash rejects whole set before writing step owned by this component.
    /// </summary>
    [Fact]
    public async Task StaleExpectedHashRejectsWholeSetBeforeWriting()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "one.txt"), "current");
        var runtime = new WorkspaceToolRuntime(new WorkspaceToolService());
        var changes = JsonSerializer.Serialize(new[]
        {
            new { path = "one.txt", content = "after", expectedSha256 = (string?)new string('0', 64) },
            new { path = "two.txt", content = "created", expectedSha256 = (string?)null }
        });

        var result = await runtime.ExecuteAsync(_root, Call("apply_change_set", new { changes_json = changes }), CancellationToken.None);

        Assert.False(result.Activity.Succeeded);
        Assert.Contains("changed after inspection", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("current", await File.ReadAllTextAsync(Path.Combine(_root, "one.txt")));
        Assert.False(File.Exists(Path.Combine(_root, "two.txt")));
    }

    /// <summary>
    /// Performs the failed later write rolls back earlier writes and created files step owned by this component.
    /// </summary>
    [Fact]
    public async Task FailedLaterWriteRollsBackEarlierWritesAndCreatedFiles()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "one.txt"), "before");
        var runtime = new WorkspaceToolRuntime(new FailOnSecondWriteService(new WorkspaceToolService()));
        var changes = JsonSerializer.Serialize(new[]
        {
            new { path = "created.txt", content = "temporary" },
            new { path = "one.txt", content = "after" }
        });

        var result = await runtime.ExecuteAsync(_root, Call("apply_change_set", new { changes_json = changes }), CancellationToken.None);

        Assert.False(result.Activity.Succeeded);
        Assert.False(File.Exists(Path.Combine(_root, "created.txt")));
        Assert.Equal("before", await File.ReadAllTextAsync(Path.Combine(_root, "one.txt")));
    }

    /// <summary>
    /// Performs the traversal is reported as failed tool result step owned by this component.
    /// </summary>
    [Fact]
    public async Task TraversalIsReportedAsFailedToolResult()
    {
        var runtime = new WorkspaceToolRuntime(new WorkspaceToolService());
        var result = await runtime.ExecuteAsync(_root, Call("read_file", new { path = "../outside.txt" }), CancellationToken.None);
        Assert.False(result.Activity.Succeeded);
        Assert.Contains("outside", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Performs the change set traversal and duplicate targets are rejected before writing step owned by this component.
    /// </summary>
    [Fact]
    public async Task ChangeSetTraversalAndDuplicateTargetsAreRejectedBeforeWriting()
    {
        var runtime = new WorkspaceToolRuntime(new WorkspaceToolService());
        var traversal = JsonSerializer.Serialize(new[] { new { path = "../outside.txt", content = "no" } });
        var duplicate = JsonSerializer.Serialize(new[]
        {
            new { path = "same.txt", content = "one" },
            new { path = "SAME.txt", content = "two" }
        });

        var traversalResult = await runtime.ExecuteAsync(_root, Call("apply_change_set", new { changes_json = traversal }), CancellationToken.None);
        var duplicateResult = await runtime.ExecuteAsync(_root, Call("apply_change_set", new { changes_json = duplicate }), CancellationToken.None);

        Assert.False(traversalResult.Activity.Succeeded);
        Assert.False(duplicateResult.Activity.Succeeded);
        Assert.False(File.Exists(Path.Combine(_root, "same.txt")));
    }

    /// <summary>
    /// Performs the call step owned by this component.
    /// </summary>
    private static OllamaToolCall Call(string name, object arguments)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
        var values = document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone());
        return new OllamaToolCall(name, values);
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    /// <summary>
    /// Represents fail on second write service and keeps its related state and behavior together.
    /// </summary>
    private sealed class FailOnSecondWriteService(IWorkspaceToolService inner) : IWorkspaceToolService
    {
        /// <summary>
        /// Stores writes locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        private int _writes;
        /// <summary>
        /// Performs the resolve workspace path step owned by this component.
        /// </summary>
        public string ResolveWorkspacePath(string workspaceRoot, string relativePath) => inner.ResolveWorkspacePath(workspaceRoot, relativePath);
        /// <summary>
        /// Performs read text async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> ReadTextAsync(string workspaceRoot, string relativePath, CancellationToken cancellationToken) => inner.ReadTextAsync(workspaceRoot, relativePath, cancellationToken);
        /// <summary>
        /// Performs search files async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<IReadOnlyList<string>> SearchFilesAsync(string workspaceRoot, string searchPattern, CancellationToken cancellationToken) => inner.SearchFilesAsync(workspaceRoot, searchPattern, cancellationToken);
        /// <summary>
        /// Runs run process async while preserving the surrounding cancellation and error-handling contract.
        /// </summary>
        public Task<ProcessResult> RunProcessAsync(ProcessRequest request, CancellationToken cancellationToken) => inner.RunProcessAsync(request, cancellationToken);
        /// <summary>
        /// Performs write text atomic async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task WriteTextAtomicAsync(string workspaceRoot, string relativePath, string content, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _writes) == 2) throw new IOException("Injected second-write failure.");
            return inner.WriteTextAtomicAsync(workspaceRoot, relativePath, content, cancellationToken);
        }
    }
}
