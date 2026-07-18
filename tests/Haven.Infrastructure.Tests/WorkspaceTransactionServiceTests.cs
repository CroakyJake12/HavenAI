/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/WorkspaceTransactionServiceTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns WorkspaceTransactionServiceTests. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents workspace transaction service tests and keeps its related state and behavior together.
/// </summary>
public sealed class WorkspaceTransactionServiceTests : IDisposable
{
    /// <summary>
    /// Stores root locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "haven-transaction-tests-" + Guid.NewGuid().ToString("N"));
    /// <summary>
    /// Stores service locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly WorkspaceTransactionService _service;

    public WorkspaceTransactionServiceTests()
    {
        Directory.CreateDirectory(_root);
        _service = new WorkspaceTransactionService(new WorkspaceToolService());
    }

    /// <summary>
    /// Performs the applies all mutations and reports changed paths step owned by this component.
    /// </summary>
    [Fact]
    public async Task AppliesAllMutationsAndReportsChangedPaths()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "existing.txt"), "old");

        var result = await _service.ApplyAsync(
            _root,
            [
                new WorkspaceFileMutation("existing.txt", "replacement"),
                new WorkspaceFileMutation("src/new.txt", "new file")
            ],
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.TransactionId);
        Assert.Equal(["existing.txt", "src/new.txt"], result.ChangedPaths);
        Assert.Equal("replacement", await File.ReadAllTextAsync(Path.Combine(_root, "existing.txt")));
        Assert.Equal("new file", await File.ReadAllTextAsync(Path.Combine(_root, "src", "new.txt")));
        Assert.False(Directory.Exists(Path.Combine(_root, ".haven", "transactions", result.TransactionId.ToString("N"))));
    }

    /// <summary>
    /// Performs the rejects duplicate targets before changing any file step owned by this component.
    /// </summary>
    [Fact]
    public async Task RejectsDuplicateTargetsBeforeChangingAnyFile()
    {
        var path = Path.Combine(_root, "same.txt");
        await File.WriteAllTextAsync(path, "original");

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => _service.ApplyAsync(
            _root,
            [
                new WorkspaceFileMutation("same.txt", "first"),
                new WorkspaceFileMutation("same.txt", "second")
            ],
            CancellationToken.None));

        Assert.Contains("same target", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("original", await File.ReadAllTextAsync(path));
    }

    /// <summary>
    /// Performs the rejects traversal before changing any file step owned by this component.
    /// </summary>
    [Fact]
    public async Task RejectsTraversalBeforeChangingAnyFile()
    {
        var safePath = Path.Combine(_root, "safe.txt");
        await File.WriteAllTextAsync(safePath, "original");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.ApplyAsync(
            _root,
            [
                new WorkspaceFileMutation("safe.txt", "changed"),
                new WorkspaceFileMutation("../outside.txt", "forbidden")
            ],
            CancellationToken.None));

        Assert.Equal("original", await File.ReadAllTextAsync(safePath));
    }

    /// <summary>
    /// Performs the rejects directory targets before changing any file step owned by this component.
    /// </summary>
    [Fact]
    public async Task RejectsDirectoryTargetsBeforeChangingAnyFile()
    {
        Directory.CreateDirectory(Path.Combine(_root, "folder"));
        var safePath = Path.Combine(_root, "safe.txt");
        await File.WriteAllTextAsync(safePath, "original");

        await Assert.ThrowsAsync<IOException>(() => _service.ApplyAsync(
            _root,
            [
                new WorkspaceFileMutation("safe.txt", "changed"),
                new WorkspaceFileMutation("folder", "not a file")
            ],
            CancellationToken.None));

        Assert.Equal("original", await File.ReadAllTextAsync(safePath));
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }
}
