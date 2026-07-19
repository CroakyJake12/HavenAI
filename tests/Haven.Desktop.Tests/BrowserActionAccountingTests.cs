/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Desktop.Tests/BrowserActionAccountingTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns BrowserActionAccountingTests, SubmitHost, AllowPolicy, FaultingStore, TestPaths. Read the type and member comments below as a map of each responsibility.
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
/// Represents browser action accounting tests and keeps its related state and behavior together.
/// </summary>
public sealed class BrowserActionAccountingTests : IDisposable
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TestPaths _paths = new();

    /// <summary>
    /// Performs the audit failure after submission does not rewrite executed action step owned by this component.
    /// </summary>
    [Fact]
    public async Task AuditFailureAfterSubmissionDoesNotRewriteExecutedAction()
    {
        var browser = new BrowserSessionService(_paths);
        var host = new SubmitHost();
        browser.Attach(host);
        var store = new FaultingStore { ThrowOnAudit = true };
        using var automation = new BrowserAutomationService(browser, new AllowPolicy(), store, _paths);

        var request = await automation.ClickReferenceAsync("haven-1", CancellationToken.None);
        Assert.Contains("Approval required", request);
        var action = Assert.Single(store.Actions);

        var result = await automation.ApproveAsync(action.Id, CancellationToken.None);

        Assert.Equal(BrowserActionState.Executed, result.State);
        Assert.Equal(BrowserActionState.Executed, Assert.Single(store.Actions).State);
        Assert.Equal(1, host.ClickCount);
    }

    /// <summary>
    /// Performs the final state write failure after submission warns that side effect may have completed step owned by this component.
    /// </summary>
    [Fact]
    public async Task FinalStateWriteFailureAfterSubmissionWarnsThatSideEffectMayHaveCompleted()
    {
        var browser = new BrowserSessionService(_paths);
        var host = new SubmitHost();
        browser.Attach(host);
        var store = new FaultingStore { FailUpdatesAfter = 1 };
        using var automation = new BrowserAutomationService(browser, new AllowPolicy(), store, _paths);

        await automation.ClickReferenceAsync("haven-1", CancellationToken.None);
        var action = Assert.Single(store.Actions);
        var result = await automation.ApproveAsync(action.Id, CancellationToken.None);

        Assert.Equal(BrowserActionState.Failed, result.State);
        Assert.Contains("may have completed", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, host.ClickCount);
        Assert.Equal(BrowserActionState.Approved, Assert.Single(store.Actions).State);
        Assert.Contains(store.Audit, item => item.Operation == "execution-state-uncertain");
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose() => _paths.Dispose();

    /// <summary>
    /// Represents submit host and keeps its related state and behavior together.
    /// </summary>
    private sealed class SubmitHost : IEmbeddedBrowserHost
    {
        /// <summary>
        /// Gets or updates click count, the bindable or domain state represented by this property.
        /// </summary>
        public int ClickCount { get; private set; }
        /// <summary>
        /// Stores state changed locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        public event EventHandler<BrowserSnapshot>? StateChanged;
        /// <summary>
        /// Gets or updates state, the bindable or domain state represented by this property.
        /// </summary>
        public BrowserSnapshot State { get; private set; } = new(
            new Uri("https://example.test/checkout"),
            "Checkout",
            false,
            false,
            false,
            "Ready");

        /// <summary>
        /// Performs navigate asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task NavigateAsync(Uri address, CancellationToken cancellationToken)
        {
            State = State with { Address = address };
            StateChanged?.Invoke(this, State);
            return Task.CompletedTask;
        }

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
                    text = "Checkout form",
                    headings = Array.Empty<string>(),
                    elements = new[]
                    {
                        new
                        {
                            Reference = "haven-1",
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

    /// <summary>
    /// Represents allow policy and keeps its related state and behavior together.
    /// </summary>
    private sealed class AllowPolicy : IBrowserNavigationPolicy
    {
        /// <summary>
        /// Performs assess asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<BrowserNavigationAssessment> AssessAsync(
            Uri address,
            CancellationToken cancellationToken) =>
            Task.FromResult(new BrowserNavigationAssessment(address, true, "test", ["8.8.8.8"]));
    }

    /// <summary>
    /// Represents faulting store and keeps its related state and behavior together.
    /// </summary>
    private sealed class FaultingStore : IBrowserAutomationStore
    {
        /// <summary>
        /// Stores update calls locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        private int _updateCalls;

        /// <summary>
        /// Gets or updates throw on audit, the bindable or domain state represented by this property.
        /// </summary>
        public bool ThrowOnAudit { get; init; }
        /// <summary>
        /// Gets or updates fail updates after, the bindable or domain state represented by this property.
        /// </summary>
        public int FailUpdatesAfter { get; init; } = int.MaxValue;
        /// <summary>
        /// Gets or updates actions, the bindable or domain state represented by this property.
        /// </summary>
        public List<BrowserPendingAction> Actions { get; } = [];
        /// <summary>
        /// Gets or updates audit, the bindable or domain state represented by this property.
        /// </summary>
        public List<BrowserAuditEntry> Audit { get; } = [];

        /// <summary>
        /// Retrieves pending async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<BrowserPendingAction>> GetPendingAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BrowserPendingAction>>(
                Actions.Where(item => item.State == BrowserActionState.Pending).ToArray());

        /// <summary>
        /// Retrieves audit async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<BrowserAuditEntry>> GetAuditAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BrowserAuditEntry>>(Audit.TakeLast(limit).ToArray());

        /// <summary>
        /// Retrieves downloads async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<BrowserDownloadRecord>> GetDownloadsAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BrowserDownloadRecord>>([]);

        /// <summary>
        /// Performs add pending asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<BrowserPendingAction> AddPendingAsync(
            BrowserPendingAction action,
            CancellationToken cancellationToken)
        {
            Actions.Add(action);
            return Task.FromResult(action);
        }

        /// <summary>
        /// Retrieves action async for the current operation.
        /// </summary>
        public Task<BrowserPendingAction?> GetActionAsync(Guid actionId, CancellationToken cancellationToken) =>
            Task.FromResult(Actions.FirstOrDefault(item => item.Id == actionId));

        /// <summary>
        /// Performs update action asynchronously so I/O does not block the caller's thread.
        /// </summary>
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

        /// <summary>
        /// Performs add audit asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task AddAuditAsync(BrowserAuditEntry entry, CancellationToken cancellationToken)
        {
            if (ThrowOnAudit) throw new IOException("Simulated audit failure.");
            Audit.Add(entry);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Performs add download asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task AddDownloadAsync(BrowserDownloadRecord download, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    /// <summary>
    /// Represents test paths and keeps its related state and behavior together.
    /// </summary>
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
        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, true); }
            catch (IOException) { }
        }
    }
}
