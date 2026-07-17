using System.Text.Json;
using Haven.Application;
using Haven.Browser;
using Haven.Core;

namespace Haven.Desktop.Tests;

public sealed class BrowserActionAccountingTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public async Task AuditFailureAfterSubmissionDoesNotRewriteExecutedAction()
    {
        var browser = new BrowserSessionService(_paths);
        var host = new SubmitHost();
        browser.Attach(host);
        var store = new FaultingStore { ThrowOnAudit = true };
        using var automation = new BrowserAutomationService(browser, new AllowPolicy(), store, _paths);

        var request = await automation.ClickReferenceAsync("haven-submit", CancellationToken.None);
        Assert.Contains("Approval required", request);
        var action = Assert.Single(store.Actions);

        var result = await automation.ApproveAsync(action.Id, CancellationToken.None);

        Assert.Equal(BrowserActionState.Executed, result.State);
        Assert.Equal(BrowserActionState.Executed, Assert.Single(store.Actions).State);
        Assert.Equal(1, host.ClickCount);
    }

    [Fact]
    public async Task FinalStateWriteFailureAfterSubmissionWarnsThatSideEffectMayHaveCompleted()
    {
        var browser = new BrowserSessionService(_paths);
        var host = new SubmitHost();
        browser.Attach(host);
        var store = new FaultingStore { FailUpdatesAfter = 1 };
        using var automation = new BrowserAutomationService(browser, new AllowPolicy(), store, _paths);

        await automation.ClickReferenceAsync("haven-submit", CancellationToken.None);
        var action = Assert.Single(store.Actions);
        var result = await automation.ApproveAsync(action.Id, CancellationToken.None);

        Assert.Equal(BrowserActionState.Failed, result.State);
        Assert.Contains("may have completed", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, host.ClickCount);
        Assert.Equal(BrowserActionState.Approved, Assert.Single(store.Actions).State);
        Assert.Contains(store.Audit, item => item.Operation == "execution-state-uncertain");
    }

    public void Dispose() => _paths.Dispose();

    private sealed class SubmitHost : IEmbeddedBrowserHost
    {
        public int ClickCount { get; private set; }
        public event EventHandler<BrowserSnapshot>? StateChanged;
        public BrowserSnapshot State { get; private set; } = new(
            new Uri("https://example.test/checkout"),
            "Checkout",
            false,
            false,
            false,
            "Ready");

        public Task NavigateAsync(Uri address, CancellationToken cancellationToken)
        {
            State = State with { Address = address };
            StateChanged?.Invoke(this, State);
            return Task.CompletedTask;
        }

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
                    text = "Checkout form",
                    headings = Array.Empty<string>(),
                    elements = new[]
                    {
                        new
                        {
                            Reference = "haven-submit",
                            Kind = "button",
                            Text = "Place order",
                            Address = (string?)null,
                            Name = "submit",
                            InputType = (string?)null,
                            IsSensitive = false,
                            SubmitsForm = true
                        }
                    },
                    wasTruncated = false
                });
                return Task.FromResult<string?>(JsonSerializer.Serialize(payload));
            }

            if (script.Contains("e.click()", StringComparison.Ordinal))
            {
                ClickCount++;
                return Task.FromResult<string?>(JsonSerializer.Serialize("clicked"));
            }

            return Task.FromResult<string?>(JsonSerializer.Serialize(string.Empty));
        }
    }

    private sealed class AllowPolicy : IBrowserNavigationPolicy
    {
        public Task<BrowserNavigationAssessment> AssessAsync(
            Uri address,
            CancellationToken cancellationToken) =>
            Task.FromResult(new BrowserNavigationAssessment(address, true, "test", ["8.8.8.8"]));
    }

    private sealed class FaultingStore : IBrowserAutomationStore
    {
        private int _updateCalls;

        public bool ThrowOnAudit { get; init; }
        public int FailUpdatesAfter { get; init; } = int.MaxValue;
        public List<BrowserPendingAction> Actions { get; } = [];
        public List<BrowserAuditEntry> Audit { get; } = [];

        public Task<IReadOnlyList<BrowserPendingAction>> GetPendingAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BrowserPendingAction>>(
                Actions.Where(item => item.State == BrowserActionState.Pending).ToArray());

        public Task<IReadOnlyList<BrowserAuditEntry>> GetAuditAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BrowserAuditEntry>>(Audit.TakeLast(limit).ToArray());

        public Task<IReadOnlyList<BrowserDownloadRecord>> GetDownloadsAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BrowserDownloadRecord>>([]);

        public Task<BrowserPendingAction> AddPendingAsync(
            BrowserPendingAction action,
            CancellationToken cancellationToken)
        {
            Actions.Add(action);
            return Task.FromResult(action);
        }

        public Task<BrowserPendingAction?> GetActionAsync(Guid actionId, CancellationToken cancellationToken) =>
            Task.FromResult(Actions.FirstOrDefault(item => item.Id == actionId));

        public Task<BrowserPendingAction> UpdateActionAsync(
            BrowserPendingAction action,
            CancellationToken cancellationToken)
        {
            _updateCalls++;
            if (_updateCalls > FailUpdatesAfter)
                throw new IOException("Simulated state persistence failure.");
            var index = Actions.FindIndex(item => item.Id == action.Id);
            Actions[index] = action;
            return Task.FromResult(action);
        }

        public Task AddAuditAsync(BrowserAuditEntry entry, CancellationToken cancellationToken)
        {
            if (ThrowOnAudit) throw new IOException("Simulated audit failure.");
            Audit.Add(entry);
            return Task.CompletedTask;
        }

        public Task AddDownloadAsync(BrowserDownloadRecord download, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-browser-accounting-" + Guid.NewGuid().ToString("N"));
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

        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, true); }
            catch (IOException) { }
        }
    }
}
