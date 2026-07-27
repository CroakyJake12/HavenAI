/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Desktop.Tests/BrowserDownloadFilePolicyTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns BrowserDownloadFilePolicyTests. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Browser;

namespace Haven.Desktop.Tests;

/// <summary>
/// Represents browser download file policy tests and keeps its related state and behavior together.
/// </summary>
public sealed class BrowserDownloadFilePolicyTests : IDisposable
{
    /// <summary>
    /// Stores directory locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "haven-download-policy-tests-" + Guid.NewGuid().ToString("N"));

    public BrowserDownloadFilePolicyTests() => Directory.CreateDirectory(_directory);

    /// <summary>
    /// Performs the sanitize file name removes traversal device names and direction controls step owned by this component.
    /// </summary>
    [Theory]
    [InlineData("../../outside.exe", "outside.exe")]
    [InlineData("..\\..\\outside.exe", "outside.exe")]
    [InlineData("CON.txt", "_CON.txt")]
    [InlineData("CON.data.json", "_CON.data.json")]
    [InlineData("nul", "_nul")]
    [InlineData("report\u202Ecod.exe.txt", "reportcod.exe.txt")]
    [InlineData("trailing. ", "trailing")]
    public void SanitizeFileNameRemovesTraversalDeviceNamesAndDirectionControls(string input, string expected)
    {
        Assert.Equal(expected, BrowserDownloadFilePolicy.SanitizeFileName(input));
    }

    /// <summary>
    /// Performs the sanitize file name preserves extension and rune boundaries when truncating step owned by this component.
    /// </summary>
    [Fact]
    public void SanitizeFileNamePreservesExtensionAndRuneBoundariesWhenTruncating()
    {
        var input = string.Concat(Enumerable.Repeat("😀", 120)) + ".archive.json";

        var result = BrowserDownloadFilePolicy.SanitizeFileName(input);

        Assert.NotNull(result);
        Assert.True(result.Length <= BrowserDownloadFilePolicy.MaximumFileNameLength);
        Assert.EndsWith(".json", result, StringComparison.Ordinal);
        Assert.DoesNotContain('\uFFFD', result);
        Assert.True(char.IsLowSurrogate(result[^6]) || !char.IsSurrogate(result[^6]));
    }

    /// <summary>
    /// Performs the allocate unique path stays inside root and avoids files and directories step owned by this component.
    /// </summary>
    [Fact]
    public void AllocateUniquePathStaysInsideRootAndAvoidsFilesAndDirectories()
    {
        File.WriteAllText(Path.Combine(_directory, "report.txt"), "existing");
        Directory.CreateDirectory(Path.Combine(_directory, "report (2).txt"));

        var result = BrowserDownloadFilePolicy.AllocateUniquePath(_directory, "../../report.txt");

        Assert.Equal(Path.Combine(_directory, "report (3).txt"), result);
        Assert.StartsWith(Path.GetFullPath(_directory) + Path.DirectorySeparatorChar, Path.GetFullPath(result), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Performs the allocate unique path keeps collision name within policy limit step owned by this component.
    /// </summary>
    [Fact]
    public void AllocateUniquePathKeepsCollisionNameWithinPolicyLimit()
    {
        var original = BrowserDownloadFilePolicy.SanitizeFileName(new string('a', 300) + ".json")!;
        File.WriteAllText(Path.Combine(_directory, original), "existing");

        var result = BrowserDownloadFilePolicy.AllocateUniquePath(_directory, original);

        Assert.True(Path.GetFileName(result).Length <= BrowserDownloadFilePolicy.MaximumFileNameLength);
        Assert.EndsWith(" (2).json", result, StringComparison.Ordinal);
    }

    /// <summary>
    /// Performs the cleanup stale partial files only deletes old haven partials step owned by this component.
    /// </summary>
    [Fact]
    public void CleanupStalePartialFilesOnlyDeletesOldHavenPartials()
    {
        var destination = Path.Combine(_directory, "payload.bin");
        var stale = BrowserDownloadFilePolicy.CreatePartialPath(destination);
        var recent = BrowserDownloadFilePolicy.CreatePartialPath(destination);
        var unrelated = Path.Combine(_directory, "unrelated.tmp");
        File.WriteAllText(stale, "stale");
        File.WriteAllText(recent, "recent");
        File.WriteAllText(unrelated, "keep");
        var now = DateTimeOffset.UtcNow;
        File.SetLastWriteTimeUtc(stale, now.UtcDateTime - BrowserDownloadFilePolicy.PartialFileRetention - TimeSpan.FromMinutes(1));
        File.SetLastWriteTimeUtc(recent, now.UtcDateTime);
        File.SetLastWriteTimeUtc(unrelated, now.UtcDateTime - TimeSpan.FromDays(30));

        var removed = BrowserDownloadFilePolicy.CleanupStalePartialFiles(_directory, now);

        Assert.Equal(1, removed);
        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(unrelated));
    }

    /// <summary>
    /// Creates partial path uses destination and unique marker with the invariants required by its callers.
    /// </summary>
    [Fact]
    public void CreatePartialPathUsesDestinationAndUniqueMarker()
    {
        var destination = Path.Combine(_directory, "payload.bin");

        var first = BrowserDownloadFilePolicy.CreatePartialPath(destination);
        var second = BrowserDownloadFilePolicy.CreatePartialPath(destination);

        Assert.StartsWith(destination + ".haven-download-", first, StringComparison.Ordinal);
        Assert.EndsWith(".tmp", first, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose()
    {
        try { Directory.Delete(_directory, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
