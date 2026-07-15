using System.Text.Json;
using Haven.Application;
using Haven.Browser;
using Haven.Core;

namespace Haven.Desktop.Tests;

public sealed class BrowserAutomationTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public async Task NavigationPolicyBlocksCredentialsAndNonPublicAddresses()
    {
        var policy = new BrowserNavigationPolicy();

        Assert.False((await policy.AssessAsync(new Uri("http://127.0.0.1/"), CancellationToken.None)).IsAllowed);
        Assert.False((await policy.AssessAsync(new Uri("http://10.20.30.40/"), CancellationToken.None)).IsAllowed);
        Assert.False((await policy.AssessAsync(new Uri("https://user:secret@8.8.8.8/"), CancellationToken.None)).IsAllowed);
        Assert.False((await policy.AssessAsync(new Uri("file:///C:/Windows/win.ini"), CancellationToken.None)).IsAllowed);
        Assert.True((await policy.AssessAsync(new Uri("https://8.8.8.8/"), CancellationToken.None)).IsAllowed);
    }

    [Fact]
    public async Task AutomationStorePersistsExpiresAndQuarantinesCorruptState()
    {
        var now = DateTimeOffset.UtcNow;
        var action = new BrowserPendingAction(
            Guid.NewGuid(), BrowserActionKind.Download, "https://example.test", "Download test", "https://example.test/file",
            "file.txt", BrowserActionState.Pending, now.AddMinutes(-2), now.AddMinutes(-1), now.AddMinutes(-2), null);
        using (var store = new BrowserAutomationStore(_paths))
            await store.AddPendingAsync(action, CancellationToken.None);
        using (var reopened = new BrowserAutomationStore(_paths))
        {
            Assert.Empty(await reopened.GetPendingAsync(CancellationToken.None));
            Assert.Equal(BrowserActionState.Expired, (await reopened.GetActionAsync(action.Id, CancellationToken.None))?.State);
        }

        var path = Path.Combine(_paths.DataDirectory, "browser-automation.json");
        await File.WriteAllTextAsync(path, "{ invalid json");
        using var recovered = new BrowserAutomationStore(_paths);
        Assert.Empty(await recovered.GetAuditAsync(10, CancellationToken.None));
        Assert.NotEmpty(Directory.EnumerateFiles(_paths.DataDirectory, "browser-automation.json.corrupt-*.json"));
    }

    [Fact]
    public async Task StructuredReferencesAllowSafeActionsButGateSubmissionAndSensitiveFields()
    {
        var browser = new BrowserSessionService(_paths);
        var host = new FakeBrowserHost();
        browser.Attach(host);
        var store = new MemoryAutomationStore();
        var automation = new BrowserAutomationService(browser, new AllowPolicy(), store, _paths);

        host.Elements =
        [
            Element("haven-1", "button", "Open details", false, false),
            Element("haven-2", "input", "Email", false, false, "email"),
            Element("haven-3", "input", "Password", true, false, "password"),
            Element("haven-4", "button", "Place order", false, true)
        ];

        Assert.Contains("clicked", await automation.ClickReferenceAsync("haven-1", CancellationToken.None));
        Assert.Contains("filled", await automation.FillReferenceAsync("haven-2", "not-secret@example.test", CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => automation.FillReferenceAsync("haven-3", "secret-value", CancellationToken.None));

        var pendingMessage = await automation.ClickReferenceAsync("haven-4", CancellationToken.None);
        Assert.Contains("Approval required", pendingMessage);
        var pending = Assert.Single(await automation.GetPendingAsync(CancellationToken.None));
        Assert.Equal(BrowserActionKind.SubmitElement, pending.Kind);
        Assert.Equal(1, host.ClickCount);

        var approved = await automation.ApproveAsync(pending.Id, CancellationToken.None);
        Assert.Equal(BrowserActionState.Executed, approved.State);
        Assert.Equal(2, host.ClickCount);
        Assert.DoesNotContain(store.Audit, item => item.Detail.Contains("secret-value", StringComparison.Ordinal));
        Assert.DoesNotContain(store.Audit, item => item.Detail.Contains("not-secret@example.test", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StaleReferencesAreReportedAsFailuresRatherThanSuccessfulClicks()
    {
        var browser = new BrowserSessionService(_paths);
        var host = new FakeBrowserHost { ClickResult = "stale-reference" };
        browser.Attach(host);
        var automation = new BrowserAutomationService(browser, new AllowPolicy(), new MemoryAutomationStore(), _paths);
        host.Elements = [Element("haven-1", "button", "Old control", false, false)];

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            automation.ClickReferenceAsync("haven-1", CancellationToken.None));

        Assert.Contains("page changed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DownloadsArePendingUntilExplicitlyRejectedOrApproved()
    {
        var browser = new BrowserSessionService(_paths);
        browser.Attach(new FakeBrowserHost());
        var store = new MemoryAutomationStore();
        var automation = new BrowserAutomationService(browser, new AllowPolicy(), store, _paths);

        var action = await automation.RequestDownloadAsync("https://example.test/archive.zip", "archive.zip", CancellationToken.None);
        Assert.Equal(BrowserActionState.Pending, action.State);
        Assert.Single(await automation.GetPendingAsync(CancellationToken.None));
        Assert.Empty(await automation.GetDownloadsAsync(10, CancellationToken.None));

        var rejected = await automation.RejectAsync(action.Id, CancellationToken.None);
        Assert.Equal(BrowserActionState.Rejected, rejected.State);
        Assert.Empty(await automation.GetPendingAsync(CancellationToken.None));
        Assert.Empty(await automation.GetDownloadsAsync(10, CancellationToken.None));
    }

    [Fact]
    public void ModelVisibleToolsUseReferencesInsteadOfRawSelectors()
    {
        var browser = new BrowserSessionService(_paths);
        var automation = new StubAutomationService();
        var runtime = new BrowserToolRuntime(browser, automation);
        var names = runtime.Definitions.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("browser_snapshot", names);
        Assert.Contains("browser_click_ref", names);
        Assert.Contains("browser_fill_ref", names);
        Assert.Contains("browser_download", names);
        Assert.DoesNotContain("browser_click", names);
        Assert.DoesNotContain("browser_click_text", names);
        Assert.DoesNotContain("browser_fill", names);
    }

    private static BrowserPageElement Element(string reference, string kind, string text, bool sensitive, bool submits, string? inputType = null) =>
        new(reference, kind, text, null, null, inputType, sensitive, submits);

    public void Dispose() => _paths.Dispose();

    private sealed class FakeBrowserHost : IEmbeddedBrowserHost
    {
        public IReadOnlyList<BrowserPageElement> Elements { get; set; } = [];
        public int ClickCount { get; private set; }
        public string ClickResult { get; set; } = "clicked";
        public event EventHandler<BrowserSnapshot>? StateChanged;
        public BrowserSnapshot State { get; private set; } = new(new Uri("https://example.test/page"), "Example", false, false, false, "Ready");
        public Task NavigateAsync(Uri address, CancellationToken cancellationToken) { State = State with { Address = address }; StateChanged?.Invoke(this, State); return Task.CompletedTask; }
        public Task GoBackAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task GoForwardAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ReloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenDeveloperToolsAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<string?> ExecuteScriptAsync(string script, CancellationToken cancellationToken)
        {
            if (script.Contains("maxElements", StringComparison.Ordinal))
            {
                var payload = JsonSerializer.Serialize(new
                {
                    address = State.Address?.ToString(),
                    title = State.Title,
                    text = "Visible page text",
                    headings = new[] { "Heading" },
                    elements = Elements.Select(item => new
                    {
                        item.Reference,
                        item.Kind,
                        item.Text,
                        item.Address,
                        item.Name,
                        item.InputType,
                        item.IsSensitive,
                        item.SubmitsForm
                    }),
                    wasTruncated = false
                });
                return Task.FromResult<string?>(JsonSerializer.Serialize(payload));
            }
            if (script.Contains("e.click()", StringComparison.Ordinal))
            {
                ClickCount++;
                return Task.FromResult<string?>(JsonSerializer.Serialize(ClickResult));
            }
            if (script.Contains("setter.call", StringComparison.Ordinal))
                return Task.FromResult<string?>(JsonSerializer.Serialize("filled"));
            return Task.FromResult<string?>(JsonSerializer.Serialize(string.Empty));
        }
    }

    private sealed class AllowPolicy : IBrowserNavigationPolicy
    {
        public Task<BrowserNavigationAssessment> AssessAsync(Uri address, CancellationToken cancellationToken) =>
            Task.FromResult(new BrowserNavigationAssessment(address, true, "test", ["8.8.8.8"]));
    }

    private sealed class MemoryAutomationStore : IBrowserAutomationStore
    {
        public List<BrowserPendingAction> Actions { get; } = [];
        public List<BrowserAuditEntry> Audit { get; } = [];
        public List<BrowserDownloadRecord> Downloads { get; } = [];
        public Task<IReadOnlyList<BrowserPendingAction>> GetPendingAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<BrowserPendingAction>>(Actions.Where(item => item.State == BrowserActionState.Pending).ToArray());
        public Task<IReadOnlyList<BrowserAuditEntry>> GetAuditAsync(int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<BrowserAuditEntry>>(Audit.TakeLast(limit).Reverse().ToArray());
        public Task<IReadOnlyList<BrowserDownloadRecord>> GetDownloadsAsync(int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<BrowserDownloadRecord>>(Downloads.TakeLast(limit).Reverse().ToArray());
        public Task<BrowserPendingAction> AddPendingAsync(BrowserPendingAction action, CancellationToken cancellationToken) { Actions.Add(action); return Task.FromResult(action); }
        public Task<BrowserPendingAction?> GetActionAsync(Guid actionId, CancellationToken cancellationToken) => Task.FromResult(Actions.FirstOrDefault(item => item.Id == actionId));
        public Task<BrowserPendingAction> UpdateActionAsync(BrowserPendingAction action, CancellationToken cancellationToken) { var index = Actions.FindIndex(item => item.Id == action.Id); Actions[index] = action; return Task.FromResult(action); }
        public Task AddAuditAsync(BrowserAuditEntry entry, CancellationToken cancellationToken) { Audit.Add(entry); return Task.CompletedTask; }
        public Task AddDownloadAsync(BrowserDownloadRecord download, CancellationToken cancellationToken) { Downloads.Add(download); return Task.CompletedTask; }
    }

    private sealed class StubAutomationService : IBrowserAutomationService
    {
        public Task<BrowserPageSnapshot> CapturePageAsync(CancellationToken cancellationToken) => Task.FromResult(new BrowserPageSnapshot(null, "", "", [], [], DateTimeOffset.UtcNow, false, false));
        public Task<string> NavigateAsync(string address, CancellationToken cancellationToken) => Task.FromResult(string.Empty);
        public Task<string> ClickReferenceAsync(string reference, CancellationToken cancellationToken) => Task.FromResult(string.Empty);
        public Task<string> FillReferenceAsync(string reference, string value, CancellationToken cancellationToken) => Task.FromResult(string.Empty);
        public Task<BrowserPendingAction> RequestDownloadAsync(string address, string? suggestedFileName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BrowserActionExecutionResult> ApproveAsync(Guid actionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BrowserActionExecutionResult> RejectAsync(Guid actionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<BrowserPendingAction>> GetPendingAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<BrowserPendingAction>>([]);
        public Task<IReadOnlyList<BrowserAuditEntry>> GetAuditAsync(int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<BrowserAuditEntry>>([]);
        public Task<IReadOnlyList<BrowserDownloadRecord>> GetDownloadsAsync(int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<BrowserDownloadRecord>>([]);
    }

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-browser-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
            DatabasePath = Path.Combine(DataDirectory, "test.db");
            BrowserProfileDirectory = Path.Combine(DataDirectory, "browser-profile");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LegacyStatePath = Path.Combine(DataDirectory, "legacy.json");
        }
        public string DataDirectory { get; }
        public string DatabasePath { get; }
        public string BrowserProfileDirectory { get; }
        public string AttachmentsDirectory { get; }
        public string LogsDirectory { get; }
        public string LegacyStatePath { get; }
        public void Dispose() { try { Directory.Delete(DataDirectory, true); } catch (IOException) { } }
    }
}
