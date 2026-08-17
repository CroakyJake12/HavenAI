using Haven.Application;
using Haven.Browser;
using Haven.Core;

namespace Haven.Infrastructure.Tests;

public sealed class BrowserNativeDownloadAutomationServiceTests
{
    [Fact]
    public async Task StandardNativeDownloadUsesPersistentApprovalAndDownloadLedger()
    {
        var store = new MemoryStore();
        var inner = new FakeInner();
        await using var service = new BrowserNativeDownloadAutomationService(inner, new AllowPolicy(), store);
        var id = Guid.NewGuid();
        var execution = new FakeExecution(id);

        var pending = await service.RequestNativeDownloadAsync(
            new BrowserNativeDownloadRequest(id, new Uri("https://example.test/report?token=secret"), "report.pdf", false),
            execution, CancellationToken.None);

        Assert.Equal(BrowserActionState.Pending, pending.State);
        Assert.DoesNotContain("token=secret", pending.Summary, StringComparison.Ordinal);
        Assert.Single(await store.GetPendingAsync(CancellationToken.None));

        var result = await service.ApproveAsync(id, CancellationToken.None);

        Assert.Equal(BrowserActionState.Executed, result.State);
        Assert.Equal(1, execution.ExecuteCount);
        Assert.Equal(0, inner.ApproveCount);
        Assert.Single(await store.GetDownloadsAsync(10, CancellationToken.None));
        Assert.Equal(BrowserActionState.Executed, (await store.GetActionAsync(id, CancellationToken.None))!.State);
    }

    [Fact]
    public async Task PrivateNativeDownloadStaysOutOfPersistentStoreAndHistory()
    {
        var store = new MemoryStore();
        var inner = new FakeInner();
        await using var service = new BrowserNativeDownloadAutomationService(inner, new AllowPolicy(), store);
        var id = Guid.NewGuid();
        var execution = new FakeExecution(id);

        await service.RequestNativeDownloadAsync(
            new BrowserNativeDownloadRequest(id, new Uri("https://private.example.test/report?token=secret"), "private.pdf", true),
            execution, CancellationToken.None);

        Assert.Null(await store.GetActionAsync(id, CancellationToken.None));
        Assert.Single(await service.GetPendingAsync(CancellationToken.None));

        var result = await service.ApproveAsync(id, CancellationToken.None);

        Assert.Equal(BrowserActionState.Executed, result.State);
        Assert.Equal(1, execution.ExecuteCount);
        Assert.Empty(await store.GetDownloadsAsync(10, CancellationToken.None));
        Assert.Empty(await store.GetAuditAsync(20, CancellationToken.None));
        Assert.Empty(await service.GetPendingAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RejectingNativeDownloadCancelsLiveExecution()
    {
        var store = new MemoryStore();
        var inner = new FakeInner();
        await using var service = new BrowserNativeDownloadAutomationService(inner, new AllowPolicy(), store);
        var id = Guid.NewGuid();
        var execution = new FakeExecution(id);
        await service.RequestNativeDownloadAsync(
            new BrowserNativeDownloadRequest(id, new Uri("https://example.test/file.bin"), "file.bin", false),
            execution, CancellationToken.None);

        var result = await service.RejectAsync(id, CancellationToken.None);

        Assert.Equal(BrowserActionState.Rejected, result.State);
        Assert.Equal(1, execution.CancelCount);
        Assert.Equal(0, execution.ExecuteCount);
        Assert.Equal(0, inner.RejectCount);
        Assert.Equal(BrowserActionState.Rejected, (await store.GetActionAsync(id, CancellationToken.None))!.State);
    }

    [Fact]
    public async Task NonNativeApprovalStillDelegatesToExistingAutomationService()
    {
        var store = new MemoryStore();
        var inner = new FakeInner();
        await using var service = new BrowserNativeDownloadAutomationService(inner, new AllowPolicy(), store);
        var id = Guid.NewGuid();

        var result = await service.ApproveAsync(id, CancellationToken.None);

        Assert.Equal(BrowserActionState.Executed, result.State);
        Assert.Equal(1, inner.ApproveCount);
    }

    private sealed class AllowPolicy : IBrowserNavigationPolicy
    {
        public Task<BrowserNavigationAssessment> AssessAsync(Uri address, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new BrowserNavigationAssessment(address, true, "Allowed for test.", ["203.0.113.10"]));
        }
    }

    private sealed class FakeExecution(Guid actionId) : IBrowserNativeDownloadExecution
    {
        public int ExecuteCount { get; private set; }
        public int CancelCount { get; private set; }

        public Task<BrowserDownloadRecord> ExecuteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExecuteCount++;
            return Task.FromResult(new BrowserDownloadRecord(
                Guid.NewGuid(), actionId, "https://example.test/file.bin", "file.bin", "C:\\Downloads\\Haven\\file.bin",
                7, "0123456789abcdef", "application/octet-stream", DateTimeOffset.UtcNow));
        }

        public Task CancelAsync(CancellationToken cancellationToken)
        {
            CancelCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeInner : IBrowserAutomationService
    {
        public int ApproveCount { get; private set; }
        public int RejectCount { get; private set; }

        public Task<BrowserPageSnapshot> CapturePageAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> NavigateAsync(string address, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> ClickReferenceAsync(string reference, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> FillReferenceAsync(string reference, string value, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BrowserPendingAction> RequestDownloadAsync(string address, string? suggestedFileName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<BrowserActionExecutionResult> ApproveAsync(Guid actionId, CancellationToken cancellationToken)
        {
            ApproveCount++;
            return Task.FromResult(new BrowserActionExecutionResult(actionId, BrowserActionState.Executed, "delegated"));
        }

        public Task<BrowserActionExecutionResult> RejectAsync(Guid actionId, CancellationToken cancellationToken)
        {
            RejectCount++;
            return Task.FromResult(new BrowserActionExecutionResult(actionId, BrowserActionState.Rejected, "delegated"));
        }

        public Task<IReadOnlyList<BrowserPendingAction>> GetPendingAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BrowserPendingAction>>([]);

        public Task<IReadOnlyList<BrowserAuditEntry>> GetAuditAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BrowserAuditEntry>>([]);

        public Task<IReadOnlyList<BrowserDownloadRecord>> GetDownloadsAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BrowserDownloadRecord>>([]);
    }

    private sealed class MemoryStore : IBrowserAutomationStore
    {
        private readonly Dictionary<Guid, BrowserPendingAction> _actions = [];
        private readonly List<BrowserAuditEntry> _audit = [];
        private readonly List<BrowserDownloadRecord> _downloads = [];

        public Task<IReadOnlyList<BrowserPendingAction>> GetPendingAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BrowserPendingAction>>(_actions.Values.Where(item => item.State == BrowserActionState.Pending).ToArray());

        public Task<IReadOnlyList<BrowserAuditEntry>> GetAuditAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BrowserAuditEntry>>(_audit.Take(limit).ToArray());

        public Task<IReadOnlyList<BrowserDownloadRecord>> GetDownloadsAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BrowserDownloadRecord>>(_downloads.Take(limit).ToArray());

        public Task<BrowserPendingAction> AddPendingAsync(BrowserPendingAction action, CancellationToken cancellationToken)
        {
            _actions.Add(action.Id, action);
            return Task.FromResult(action);
        }

        public Task<BrowserPendingAction?> GetActionAsync(Guid actionId, CancellationToken cancellationToken) =>
            Task.FromResult(_actions.GetValueOrDefault(actionId));

        public Task<BrowserPendingAction> UpdateActionAsync(BrowserPendingAction action, CancellationToken cancellationToken)
        {
            _actions[action.Id] = action;
            return Task.FromResult(action);
        }

        public Task AddAuditAsync(BrowserAuditEntry entry, CancellationToken cancellationToken)
        {
            _audit.Add(entry);
            return Task.CompletedTask;
        }

        public Task AddDownloadAsync(BrowserDownloadRecord download, CancellationToken cancellationToken)
        {
            _downloads.Add(download);
            return Task.CompletedTask;
        }
    }
}
