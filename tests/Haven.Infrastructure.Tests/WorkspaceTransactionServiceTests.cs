using Haven.Application;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class WorkspaceTransactionServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "haven-transaction-tests-" + Guid.NewGuid().ToString("N"));
    private readonly WorkspaceTransactionService _service;

    public WorkspaceTransactionServiceTests()
    {
        Directory.CreateDirectory(_root);
        _service = new WorkspaceTransactionService(new WorkspaceToolService());
    }

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

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }
}
