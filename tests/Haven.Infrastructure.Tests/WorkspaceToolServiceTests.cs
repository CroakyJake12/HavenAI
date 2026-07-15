using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class WorkspaceToolServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "haven-tests-" + Guid.NewGuid().ToString("N"));
    private readonly WorkspaceToolService _service = new();

    public WorkspaceToolServiceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void ResolvesWorkspaceRootItself()
    {
        Assert.Equal(Path.GetFullPath(_root), _service.ResolveWorkspacePath(_root, "."));
    }

    [Fact]
    public void AcceptsAbsolutePathsInsideWorkspace()
    {
        var child = Path.Combine(_root, "src", "inside.txt");
        Assert.Equal(Path.GetFullPath(child), _service.ResolveWorkspacePath(_root, child));
    }

    [Fact]
    public void RejectsTraversalOutsideWorkspace()
    {
        Assert.Throws<UnauthorizedAccessException>(() => _service.ResolveWorkspacePath(_root, "../outside.txt"));
    }

    [Fact]
    public async Task AtomicWriteAndReadStayInsideWorkspace()
    {
        await _service.WriteTextAtomicAsync(_root, "src/test.txt", "hello", CancellationToken.None);
        Assert.Equal("hello", await _service.ReadTextAsync(_root, "src/test.txt", CancellationToken.None));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_root, "src"), "*.haven.tmp.*"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }
}
