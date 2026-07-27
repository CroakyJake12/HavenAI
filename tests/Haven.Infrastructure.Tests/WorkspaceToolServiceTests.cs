/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/WorkspaceToolServiceTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns WorkspaceToolServiceTests. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents workspace tool service tests and keeps its related state and behavior together.
/// </summary>
public sealed class WorkspaceToolServiceTests : IDisposable
{
    /// <summary>
    /// Stores root locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "haven-tests-" + Guid.NewGuid().ToString("N"));
    /// <summary>
    /// Stores service locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly WorkspaceToolService _service = new();

    public WorkspaceToolServiceTests() => Directory.CreateDirectory(_root);

    /// <summary>
    /// Performs the resolves workspace root itself step owned by this component.
    /// </summary>
    [Fact]
    public void ResolvesWorkspaceRootItself()
    {
        Assert.Equal(Path.GetFullPath(_root), _service.ResolveWorkspacePath(_root, "."));
    }

    /// <summary>
    /// Performs the accepts absolute paths inside workspace step owned by this component.
    /// </summary>
    [Fact]
    public void AcceptsAbsolutePathsInsideWorkspace()
    {
        var child = Path.Combine(_root, "src", "inside.txt");
        Assert.Equal(Path.GetFullPath(child), _service.ResolveWorkspacePath(_root, child));
    }

    /// <summary>
    /// Performs the rejects traversal outside workspace step owned by this component.
    /// </summary>
    [Fact]
    public void RejectsTraversalOutsideWorkspace()
    {
        Assert.Throws<UnauthorizedAccessException>(() => _service.ResolveWorkspacePath(_root, "../outside.txt"));
    }

    /// <summary>
    /// Performs the atomic write and read stay inside workspace step owned by this component.
    /// </summary>
    [Fact]
    public async Task AtomicWriteAndReadStayInsideWorkspace()
    {
        await _service.WriteTextAtomicAsync(_root, "src/test.txt", "hello", CancellationToken.None);
        Assert.Equal("hello", await _service.ReadTextAsync(_root, "src/test.txt", CancellationToken.None));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_root, "src"), "*.haven.tmp.*"));
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }
}
