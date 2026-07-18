/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/BrowserPrivateProfileManagerTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns BrowserPrivateProfileManagerTests, TemporaryDirectory. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Browser;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents browser private profile manager tests and keeps its related state and behavior together.
/// </summary>
public sealed class BrowserPrivateProfileManagerTests
{
    /// <summary>
    /// Performs the profiles are unique and remain under the managed root step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the closing one private tab deletes only its profile step owned by this component.
    /// </summary>
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
        Assert.DoesNotContain(Directory.EnumerateDirectories(manager.RootDirectory),
            path => Path.GetFileName(path).StartsWith(".deleting-", StringComparison.Ordinal));
    }

    /// <summary>
    /// Performs the startup cleanup removes orphans but preserves active profiles step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the startup cleanup completes interrupted tombstone deletion step owned by this component.
    /// </summary>
    [Fact]
    public async Task StartupCleanupCompletesInterruptedTombstoneDeletion()
    {
        using var temp = new TemporaryDirectory();
        var manager = new BrowserPrivateProfileManager(Path.Combine(temp.Path, "standard"));
        Directory.CreateDirectory(manager.RootDirectory);
        var tombstone = Path.Combine(manager.RootDirectory, $".deleting-{Guid.NewGuid():N}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tombstone);
        await File.WriteAllTextAsync(Path.Combine(tombstone, "Cookies"), "private state");

        var removed = await manager.CleanupOrphansAsync(new HashSet<Guid>(), CancellationToken.None);

        Assert.Equal(1, removed);
        Assert.False(Directory.Exists(tombstone));
        Assert.False(Directory.Exists(manager.RootDirectory));
    }

    /// <summary>
    /// Reports whether cancelled cleanup does not move or delete active profile is true for the current state.
    /// </summary>
    [Fact]
    public async Task CancelledCleanupDoesNotMoveOrDeleteActiveProfile()
    {
        using var temp = new TemporaryDirectory();
        var manager = new BrowserPrivateProfileManager(Path.Combine(temp.Path, "standard"));
        var id = Guid.NewGuid();
        var profile = await manager.CreateAsync(id, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.CleanupAsync(id, cancellation.Token));

        Assert.True(Directory.Exists(profile));
        Assert.DoesNotContain(Directory.EnumerateDirectories(manager.RootDirectory),
            path => Path.GetFileName(path).StartsWith(".deleting-", StringComparison.Ordinal));
    }

    /// <summary>
    /// Performs the reparse point profile is removed without deleting its target step owned by this component.
    /// </summary>
    [Fact]
    public async Task ReparsePointProfileIsRemovedWithoutDeletingItsTarget()
    {
        using var temp = new TemporaryDirectory();
        var manager = new BrowserPrivateProfileManager(Path.Combine(temp.Path, "standard"));
        Directory.CreateDirectory(manager.RootDirectory);
        var outside = Path.Combine(temp.Path, "outside");
        Directory.CreateDirectory(outside);
        var marker = Path.Combine(outside, "must-remain.txt");
        await File.WriteAllTextAsync(marker, "safe");
        var link = Path.Combine(manager.RootDirectory, "not-a-profile");

        try
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }

        var removed = await manager.CleanupOrphansAsync(new HashSet<Guid>(), CancellationToken.None);

        Assert.Equal(1, removed);
        Assert.False(Directory.Exists(link));
        Assert.True(File.Exists(marker));
    }

    /// <summary>
    /// Performs the empty tab identifiers are rejected step owned by this component.
    /// </summary>
    [Fact]
    public void EmptyTabIdentifiersAreRejected()
    {
        using var temp = new TemporaryDirectory();
        var manager = new BrowserPrivateProfileManager(Path.Combine(temp.Path, "standard"));
        Assert.Throws<ArgumentException>(() => manager.GetProfileDirectory(Guid.Empty));
    }

    /// <summary>
    /// Represents temporary directory and keeps its related state and behavior together.
    /// </summary>
    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "haven-private-profile-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        /// <summary>
        /// Gets or updates path, the bindable or domain state represented by this property.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Performs the dispose step owned by this component.
        /// </summary>
        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }
}