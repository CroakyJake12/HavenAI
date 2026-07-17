using Haven.Browser;

namespace Haven.Infrastructure.Tests;

public sealed class BrowserPrivateProfileManagerTests
{
    [Fact]
    public async Task ProfilesAreUniqueAndRemainUnderTheManagedRoot()
    {
        using var temp = new TemporaryDirectory();
        var manager = new BrowserPrivateProfileManager(Path.Combine(temp.Path, "standard"));
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        var first = await manager.CreateAsync(firstId, CancellationToken.None);
        var second = await manager.CreateAsync(secondId, CancellationToken.None);

        Assert.NotEqual(first, second);
        Assert.StartsWith(Path.GetFullPath(manager.RootDirectory), Path.GetFullPath(first), StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(Path.GetFullPath(manager.RootDirectory), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(first));
        Assert.True(Directory.Exists(second));
    }

    [Fact]
    public async Task ClosingOnePrivateTabDeletesOnlyItsProfile()
    {
        using var temp = new TemporaryDirectory();
        var manager = new BrowserPrivateProfileManager(Path.Combine(temp.Path, "standard"));
        var closedId = Guid.NewGuid();
        var activeId = Guid.NewGuid();
        var closed = await manager.CreateAsync(closedId, CancellationToken.None);
        var active = await manager.CreateAsync(activeId, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(closed, "cookie.db"), "private");
        await File.WriteAllTextAsync(Path.Combine(active, "cookie.db"), "active");

        await manager.CleanupAsync(closedId, CancellationToken.None);

        Assert.False(Directory.Exists(closed));
        Assert.True(Directory.Exists(active));
        Assert.True(File.Exists(Path.Combine(active, "cookie.db")));
    }

    [Fact]
    public async Task StartupCleanupRemovesOrphansButPreservesActiveProfiles()
    {
        using var temp = new TemporaryDirectory();
        var manager = new BrowserPrivateProfileManager(Path.Combine(temp.Path, "standard"));
        var activeId = Guid.NewGuid();
        var orphanId = Guid.NewGuid();
        var active = await manager.CreateAsync(activeId, CancellationToken.None);
        var orphan = await manager.CreateAsync(orphanId, CancellationToken.None);
        var malformed = Path.Combine(manager.RootDirectory, "not-a-profile");
        Directory.CreateDirectory(malformed);

        var removed = await manager.CleanupOrphansAsync(new HashSet<Guid> { activeId }, CancellationToken.None);

        Assert.Equal(2, removed);
        Assert.True(Directory.Exists(active));
        Assert.False(Directory.Exists(orphan));
        Assert.False(Directory.Exists(malformed));
    }

    [Fact]
    public async Task CancelledCleanupDoesNotDeleteProfiles()
    {
        using var temp = new TemporaryDirectory();
        var manager = new BrowserPrivateProfileManager(Path.Combine(temp.Path, "standard"));
        var id = Guid.NewGuid();
        var profile = await manager.CreateAsync(id, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.CleanupAsync(id, cancellation.Token));
        Assert.True(Directory.Exists(profile));
    }

    [Fact]
    public void EmptyTabIdentifiersAreRejected()
    {
        using var temp = new TemporaryDirectory();
        var manager = new BrowserPrivateProfileManager(Path.Combine(temp.Path, "standard"));
        Assert.Throws<ArgumentException>(() => manager.GetProfileDirectory(Guid.Empty));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "haven-private-profile-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }
}
