using System.Text.Json;
using Haven.Application;
using Haven.Browser;

namespace Haven.Infrastructure.Tests;

public sealed class BrowserSitePermissionCapacityTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public void LoadingMoreThanCapacityKeepsNewestPermissions()
    {
        var start = DateTimeOffset.UtcNow.AddDays(-10);
        var permissions = Enumerable.Range(0, 501)
            .Select(index => new
            {
                origin = $"https://site-{index}.example",
                kind = (int)BrowserSitePermissionKind.Notifications,
                decision = (int)BrowserSitePermissionDecision.Allow,
                updatedAt = start.AddMinutes(index)
            })
            .ToArray();
        File.WriteAllText(
            Path.Combine(_paths.DataDirectory, "browser-site-permissions.json"),
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                permissions,
                audit = Array.Empty<object>()
            }));

        using var store = new BrowserSitePermissionStore(_paths);

        Assert.Equal(500, store.Permissions.Count);
        Assert.DoesNotContain(store.Permissions, item => item.Origin == "https://site-0.example");
        Assert.Contains(store.Permissions, item => item.Origin == "https://site-500.example");
    }

    public void Dispose() => _paths.Dispose();

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(
                Path.GetTempPath(),
                "haven-browser-permission-capacity-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
            DatabasePath = Path.Combine(DataDirectory, "test.db");
            BrowserProfileDirectory = Path.Combine(DataDirectory, "browser");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LegacyStatePath = Path.Combine(DataDirectory, "missing.json");
        }

        public string DataDirectory { get; }
        public string DatabasePath { get; }
        public string BrowserProfileDirectory { get; }
        public string AttachmentsDirectory { get; }
        public string LogsDirectory { get; }
        public string LegacyStatePath { get; }

        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, true); }
            catch (IOException) { }
        }
    }
}
