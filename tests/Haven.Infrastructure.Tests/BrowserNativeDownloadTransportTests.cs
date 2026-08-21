using System.Security.Cryptography;
using Haven.Application;
using Haven.Browser;
using Haven.Core;

namespace Haven.Infrastructure.Tests;

public sealed class BrowserNativeDownloadTransportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "haven-native-download-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PrepareNativeDownloadUsesExistingFilePolicyAndBrowserPolicy()
    {
        var policy = new AllowPolicy();
        var transport = new BrowserDownloadTransport(policy, _root);

        var plan = await transport.PrepareNativeDownloadAsync(
            Guid.NewGuid(), new Uri("https://example.test/export"), new Uri("https://example.test/report"),
            "../CON.txt", "attachment; filename*=UTF-8''ignored.txt", CancellationToken.None);

        Assert.Single(policy.Assessed);
        Assert.Equal("example.test", policy.Assessed[0].Host);
        Assert.Equal("_CON.txt", plan.FileName);
        Assert.Equal(Path.GetFullPath(_root), Path.GetDirectoryName(plan.FinalPath));
        Assert.StartsWith(plan.FinalPath + ".haven-download-", plan.PartialPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".tmp", plan.PartialPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PrepareBrowserLocalDownloadUsesHttpInitiatorWithoutPersistingBlobIdentifier()
    {
        var policy = new AllowPolicy();
        var transport = new BrowserDownloadTransport(policy, _root);
        var blob = new Uri("blob:https://example.test/80c4db92-12ee-4d75-b50c-111111111111");
        var initiator = new Uri("https://example.test/private/report?token=secret#section");

        var plan = await transport.PrepareNativeDownloadAsync(
            Guid.NewGuid(), blob, initiator, "report.pdf", null, CancellationToken.None);

        Assert.Single(policy.Assessed);
        Assert.Equal(initiator, policy.Assessed[0]);
        Assert.Equal("https://example.test/private/report", plan.RecordAddress);
        Assert.DoesNotContain("80c4db92", plan.RecordAddress, StringComparison.Ordinal);
        Assert.DoesNotContain("token", plan.RecordAddress, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FinalizeNativeDownloadHashesAndAtomicallyMovesCompletedPartial()
    {
        var transport = new BrowserDownloadTransport(new AllowPolicy(), _root);
        var actionId = Guid.NewGuid();
        var plan = await transport.PrepareNativeDownloadAsync(
            actionId, new Uri("https://example.test/file.bin"), null, "file.bin", null, CancellationToken.None);
        Directory.CreateDirectory(_root);
        var bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7 };
        await File.WriteAllBytesAsync(plan.PartialPath, bytes);

        var record = await transport.FinalizeNativeDownloadAsync(plan, "application/octet-stream", CancellationToken.None);

        Assert.Equal(actionId, record.ActionId);
        Assert.Equal(bytes.LongLength, record.SizeBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), record.Sha256);
        Assert.True(File.Exists(plan.FinalPath));
        Assert.False(File.Exists(plan.PartialPath));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(plan.FinalPath));
    }

    [Fact]
    public async Task FinalizeNativeDownloadRejectsOversizedNativeFileAndRemovesPartial()
    {
        var transport = new BrowserDownloadTransport(new AllowPolicy(), _root);
        var plan = await transport.PrepareNativeDownloadAsync(
            Guid.NewGuid(), new Uri("https://example.test/huge.bin"), null, "huge.bin", null, CancellationToken.None);
        Directory.CreateDirectory(_root);
        await using (var stream = new FileStream(plan.PartialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            stream.SetLength(BrowserDownloadTransport.MaximumDownloadBytes + 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            transport.FinalizeNativeDownloadAsync(plan, null, CancellationToken.None));

        Assert.False(File.Exists(plan.PartialPath));
        Assert.False(File.Exists(plan.FinalPath));
    }

    [Fact]
    public async Task PrepareNativeDownloadRejectsBrowserLocalSourceWithoutHttpInitiator()
    {
        var transport = new BrowserDownloadTransport(new AllowPolicy(), _root);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => transport.PrepareNativeDownloadAsync(
            Guid.NewGuid(), new Uri("data:text/plain,hello"), null, "hello.txt", null, CancellationToken.None));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class AllowPolicy : IBrowserNavigationPolicy
    {
        public List<Uri> Assessed { get; } = [];

        public Task<BrowserNavigationAssessment> AssessAsync(Uri address, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assessed.Add(address);
            return Task.FromResult(new BrowserNavigationAssessment(address, true, "Allowed for test.", ["203.0.113.10"]));
        }
    }
}
