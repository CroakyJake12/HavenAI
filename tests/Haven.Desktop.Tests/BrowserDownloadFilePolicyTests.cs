using Haven.Browser;

namespace Haven.Desktop.Tests;

public sealed class BrowserDownloadFilePolicyTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "haven-download-policy-tests-" + Guid.NewGuid().ToString("N"));

    public BrowserDownloadFilePolicyTests() => Directory.CreateDirectory(_directory);

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

    [Fact]
    public void AllocateUniquePathStaysInsideRootAndAvoidsFilesAndDirectories()
    {
        File.WriteAllText(Path.Combine(_directory, "report.txt"), "existing");
        Directory.CreateDirectory(Path.Combine(_directory, "report (2).txt"));

        var result = BrowserDownloadFilePolicy.AllocateUniquePath(_directory, "../../report.txt");

        Assert.Equal(Path.Combine(_directory, "report (3).txt"), result);
        Assert.StartsWith(Path.GetFullPath(_directory) + Path.DirectorySeparatorChar, Path.GetFullPath(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AllocateUniquePathKeepsCollisionNameWithinPolicyLimit()
    {
        var original = BrowserDownloadFilePolicy.SanitizeFileName(new string('a', 300) + ".json")!;
        File.WriteAllText(Path.Combine(_directory, original), "existing");

        var result = BrowserDownloadFilePolicy.AllocateUniquePath(_directory, original);

        Assert.True(Path.GetFileName(result).Length <= BrowserDownloadFilePolicy.MaximumFileNameLength);
        Assert.EndsWith(" (2).json", result, StringComparison.Ordinal);
    }

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

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
