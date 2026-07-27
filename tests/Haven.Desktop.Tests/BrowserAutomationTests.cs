/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Desktop.Tests/BrowserAutomationTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns BrowserAutomationTests, FakeBrowserHost, AllowPolicy, MemoryAutomationStore, StubAutomationService, TestPaths. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;
using Haven.Application;
using Haven.Browser;
using Haven.Core;

namespace Haven.Desktop.Tests;

/// <summary>
/// Represents browser automation tests and keeps its related state and behavior together.
/// </summary>
public sealed class BrowserAutomationTests : IDisposable
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TestPaths _paths = new();

    /// <summary>
    /// Performs the navigation policy blocks credentials and non public addresses step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the automation store persists expires and quarantines corrupt state step owned by this component.
    /// </summary>
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
        await File.WriteAllTextAsync(path, "{ invalid json", TestContext.Current.CancellationToken);
        using var recovered = new BrowserAutomationStore(_paths);
        Assert.Empty(await recovered.GetAuditAsync(10, CancellationToken.None));
        Assert.NotEmpty(Directory.EnumerateFiles(_paths.DataDirectory, "browser-automation.json.corrupt-*.json"));
    }

    /// <summary>
    /// Performs the structured references allow safe actions but gate submission and sensitive fields step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the stale references are reported as failures rather than successful clicks step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the downloads are pending until explicitly rejected or approved step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the model visible tools use references instead of raw selectors step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the element step owned by this component.
    /// </summary>
    private static BrowserPageElement Element(string reference, string kind, string text, bool sensitive, bool submits, string? inputType = null) =>
        new(reference, kind, text, null, null, inputType, sensitive, submits);

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose() => _paths.Dispose();

    /// <summary>
    /// Represents fake browser host and keeps its related state and behavior together.
    /// </summary>
    private sealed class FakeBrowserHost : IEmbeddedBrowserHost
    {
        /// <summary>
        /// Gets or updates elements, the bindable or domain state represented by this property.
        /// </summary>
        public IReadOnlyList<BrowserPageElement> Elements { get; set; } = [];
        /// <summary>
        /// Gets or updates click count, the bindable or domain state represented by this property.
        /// </summary>
        public int ClickCount { get; private set; }
        /// <summary>
        /// Gets or updates click result, the bindable or domain state represented by this property.
        /// </summary>
        public string ClickResult { get; set; } = "clicked";
        /// <summary>
        /// Stores state changed locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public event EventHandler<BrowserSnapshot>? StateChanged;
        /// <summary>
        /// Gets or updates state, the bindable or domain state represented by this property.
        /// </summary>
        public BrowserSnapshot State { get; private set; } = new(new Uri("https://example.test/page"), "Example", false, false, false, "Ready");
        /// <summary>
        /// Performs navigate asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task NavigateAsync(Uri address, CancellationToken cancellationToken) { State = State with { Address = address }; StateChanged?.Invoke(this, State); return Task.CompletedTask; }
        /// <summary>
        /// Performs go back asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task GoBackAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// Performs go forward asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task GoForwardAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// Performs reload asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task ReloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// Performs stop asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// Performs open developer tools asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task OpenDeveloperToolsAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>
        /// Runs execute script async while preserving the surrounding cancellation and error-handling contract.
        /// </summary>
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

    /// <summary>
    /// Represents allow policy and keeps its related state and behavior together.
    /// </summary>
    private sealed class AllowPolicy : IBrowserNavigationPolicy
    {
        /// <summary>
        /// Performs assess asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<BrowserNavigationAssessment> AssessAsync(Uri address, CancellationToken cancellationToken) =>
            Task.FromResult(new BrowserNavigationAssessment(address, true, "test", ["8.8.8.8"]));
    }

    /// <summary>
    /// Represents memory automation store and keeps its related state and behavior together.
    /// </summary>
    private sealed class MemoryAutomationStore : IBrowserAutomationStore
    {
        /// <summary>
        /// Gets or updates actions, the bindable or domain state represented by this property.
        /// </summary>
        public List<BrowserPendingAction> Actions { get; } = [];
        /// <summary>
        /// Gets or updates audit, the bindable or domain state represented by this property.
        /// </summary>
        public List<BrowserAuditEntry> Audit { get; } = [];
        /// <summary>
        /// Gets or updates downloads, the bindable or domain state represented by this property.
        /// </summary>
        public List<BrowserDownloadRecord> Downloads { get; } = [];
        /// <summary>
        /// Retrieves pending async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<BrowserPendingAction>> GetPendingAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<BrowserPendingAction>>(Actions.Where(item => item.State == BrowserActionState.Pending).ToArray());
        /// <summary>
        /// Retrieves audit async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<BrowserAuditEntry>> GetAuditAsync(int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<BrowserAuditEntry>>(Audit.TakeLast(limit).Reverse().ToArray());
        /// <summary>
        /// Retrieves downloads async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<BrowserDownloadRecord>> GetDownloadsAsync(int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<BrowserDownloadRecord>>(Downloads.TakeLast(limit).Reverse().ToArray());
        /// <summary>
        /// Performs add pending asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<BrowserPendingAction> AddPendingAsync(BrowserPendingAction action, CancellationToken cancellationToken) { Actions.Add(action); return Task.FromResult(action); }
        /// <summary>
        /// Retrieves action async for the current operation.
        /// </summary>
        public Task<BrowserPendingAction?> GetActionAsync(Guid actionId, CancellationToken cancellationToken) => Task.FromResult(Actions.FirstOrDefault(item => item.Id == actionId));
        /// <summary>
        /// Performs update action asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<BrowserPendingAction> UpdateActionAsync(BrowserPendingAction action, CancellationToken cancellationToken) { var index = Actions.FindIndex(item => item.Id == action.Id); Actions[index] = action; return Task.FromResult(action); }
        /// <summary>
        /// Performs add audit asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task AddAuditAsync(BrowserAuditEntry entry, CancellationToken cancellationToken) { Audit.Add(entry); return Task.CompletedTask; }
        /// <summary>
        /// Performs add download asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task AddDownloadAsync(BrowserDownloadRecord download, CancellationToken cancellationToken) { Downloads.Add(download); return Task.CompletedTask; }
    }

    /// <summary>
    /// Represents stub automation service and keeps its related state and behavior together.
    /// </summary>
    private sealed class StubAutomationService : IBrowserAutomationService
    {
        /// <summary>
        /// Performs capture page asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<BrowserPageSnapshot> CapturePageAsync(CancellationToken cancellationToken) => Task.FromResult(new BrowserPageSnapshot(null, "", "", [], [], DateTimeOffset.UtcNow, false, false));
        /// <summary>
        /// Performs navigate asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> NavigateAsync(string address, CancellationToken cancellationToken) => Task.FromResult(string.Empty);
        /// <summary>
        /// Performs click reference asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> ClickReferenceAsync(string reference, CancellationToken cancellationToken) => Task.FromResult(string.Empty);
        /// <summary>
        /// Performs fill reference asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> FillReferenceAsync(string reference, string value, CancellationToken cancellationToken) => Task.FromResult(string.Empty);
        /// <summary>
        /// Performs request download asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<BrowserPendingAction> RequestDownloadAsync(string address, string? suggestedFileName, CancellationToken cancellationToken) => throw new NotSupportedException();
        /// <summary>
        /// Performs approve asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<BrowserActionExecutionResult> ApproveAsync(Guid actionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        /// <summary>
        /// Performs reject asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<BrowserActionExecutionResult> RejectAsync(Guid actionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        /// <summary>
        /// Retrieves pending async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<BrowserPendingAction>> GetPendingAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<BrowserPendingAction>>([]);
        /// <summary>
        /// Retrieves audit async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<BrowserAuditEntry>> GetAuditAsync(int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<BrowserAuditEntry>>([]);
        /// <summary>
        /// Retrieves downloads async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<BrowserDownloadRecord>> GetDownloadsAsync(int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<BrowserDownloadRecord>>([]);
    }

    /// <summary>
    /// Represents test paths and keeps its related state and behavior together.
    /// </summary>
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
        /// <summary>
        /// Gets or updates data directory, the bindable or domain state represented by this property.
        /// </summary>
        public string DataDirectory { get; }
        /// <summary>
        /// Gets or updates database path, the bindable or domain state represented by this property.
        /// </summary>
        public string DatabasePath { get; }
        /// <summary>
        /// Gets or updates browser profile directory, the bindable or domain state represented by this property.
        /// </summary>
        public string BrowserProfileDirectory { get; }
        /// <summary>
        /// Gets or updates attachments directory, the bindable or domain state represented by this property.
        /// </summary>
        public string AttachmentsDirectory { get; }
        /// <summary>
        /// Gets or updates logs directory, the bindable or domain state represented by this property.
        /// </summary>
        public string LogsDirectory { get; }
        /// <summary>
        /// Gets or updates legacy state path, the bindable or domain state represented by this property.
        /// </summary>
        public string LegacyStatePath { get; }
        /// <summary>
        /// Performs the dispose step owned by this component.
        /// </summary>
        public void Dispose() { try { Directory.Delete(DataDirectory, true); } catch (IOException) { } }
    }
}
