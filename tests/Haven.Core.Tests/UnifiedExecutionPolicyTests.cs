using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class UnifiedExecutionPolicyTests
{
    [Fact]
    public void Low_risk_recovery_is_bounded_and_preserves_each_stage()
    {
        var service = new AutonomousRecoveryService();
        var execution = Guid.NewGuid();
        var risk = new RecoveryRiskAssessment(true, true, false, false, false, false, false, .95);

        var first = service.Plan(execution, Guid.NewGuid(), "HTTP 404: wrong safe path", risk, "Generated path was wrong", "Use discovered endpoint");
        var second = service.Plan(execution, Guid.NewGuid(), "HTTP 404: wrong safe path", risk, "Generated path was wrong", "Use discovered endpoint");
        var third = service.Plan(execution, Guid.NewGuid(), "HTTP 404: wrong safe path", risk, "Generated path was wrong", "Use discovered endpoint");
        var fourth = service.Plan(execution, Guid.NewGuid(), "HTTP 404: wrong safe path", risk, "Generated path was wrong", "Use discovered endpoint");

        Assert.Equal(RecoveryClass.Autonomous, first.Classification);
        Assert.Equal(RecoveryStage.SafeAutomaticRetry, first.Stage);
        Assert.Equal(RecoveryStage.SafeAutonomousRepair, second.Stage);
        Assert.Equal(RecoveryStage.SafeAutonomousRepair, third.Stage);
        Assert.Equal(RecoveryStage.Exhausted, fourth.Stage);
        Assert.Equal(4, fourth.Attempt);
    }

    [Fact]
    public void Destructive_or_permission_expanding_recovery_requires_approval()
    {
        var service = new AutonomousRecoveryService();
        var destructive = new RecoveryRiskAssessment(true, false, true, false, false, false, true, .99);
        var expanded = new RecoveryRiskAssessment(true, true, false, false, true, false, false, .99);

        Assert.Equal(RecoveryStage.RequestRiskyApproval, service.Plan(Guid.NewGuid(), Guid.NewGuid(), "delete", destructive, "Reset requested").Stage);
        Assert.Equal(RecoveryStage.RequestRiskyApproval, service.Plan(Guid.NewGuid(), Guid.NewGuid(), "elevate", expanded, "Elevation requested").Stage);
    }

    [Fact]
    public void Repository_manifest_supports_multiple_packages_and_rejects_escape_or_permission_drift()
    {
        var validator = new ExtensionManifestValidator();
        var permission = ExtensionPermission.ProcessExecution | ExtensionPermission.ProjectRead;
        var capability = new ExtensionCapabilityManifest("build.run", "Run build", "Runs an authorised build", "bin/plugin.exe", ["build"], permission);
        var plugin = new ExtensionPackageManifest("example.plugin", "packages/plugin", "Example", ExtensionPackageType.Plugin, "1.2.3", ">=0.2", "Example package", "Author", "Publisher", null, "MIT", permission, [], [capability], [], null);
        var skill = new ExtensionPackageManifest("example.skill", "packages/skill", "Example Skill", ExtensionPackageType.Skill, "1.0.0", ">=0.2", "Skill", "Author", "Publisher", null, "MIT", ExtensionPermission.None, [], [], [new ExtensionSkillManifest("example.instructions", "Example", "Instructions", "SKILL.md", true)], null);

        Assert.True(validator.Validate(new ExtensionManifestDocument(1, [plugin, skill])).IsValid);

        var escaped = plugin with { PackagePath = "../outside", RequestedPermissions = ExtensionPermission.ProjectRead };
        var result = validator.Validate(new ExtensionManifestDocument(1, [escaped]));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("safe relative path", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("not declared", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Task_locators_are_user_friendly_but_not_credentials()
    {
        Assert.True(HavenTaskLocator.TryParse("HAV-ABCD-EFGH-JK23", out var parsed));
        Assert.Equal("HAV-ABCD-EFGH-JK23", parsed.Value);
        Assert.False(HavenTaskLocator.TryParse("HAV-secret-token", out _));
    }

    [Fact]
    public async Task Host_owned_secret_remediation_preserves_the_failed_node_and_never_emits_the_secret()
    {
        var repository = new MemoryRemediationRepository();
        var secrets = new MemorySecretStore();
        var sink = new RecordingSink();
        var coordinator = new RemediationCoordinator(repository, secrets, sink);
        var executionId = Guid.NewGuid();
        var failedActionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var request = new RemediationRequest(
            Guid.NewGuid(), executionId, failedActionId, RemediationType.SecretInput,
            "Weather connection", "A provider key is required.", "weather.plugin", "Weather plugin", "Weather",
            [new RemediationInput("apiKey", "API key", "password", true, RemediationSensitivity.Secret)],
            ["Save Securely & Retry", "Cancel"], RemediationSensitivity.Secret, true, true,
            RecoveryPolicyDefaults.InitialUserInteractionTimeout, RecoveryPolicyDefaults.MaximumInteractiveWait,
            RemediationState.Waiting, now, now);

        await coordinator.RequestAsync(request, CancellationToken.None);
        var blocker = Assert.Single(sink.Events);
        Assert.NotEqual(failedActionId, blocker.ActionId);
        Assert.Equal(failedActionId, blocker.ParentActionId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            coordinator.SaveSecretAndResolveAsync(request.Id, "unrequested-secret", "must-not-store", CancellationToken.None));
        Assert.Null(secrets.Value);

        const string secret = "not-for-graph-123";
        var completed = await coordinator.SaveSecretAndResolveAsync(request.Id, "apiKey", secret, CancellationToken.None);
        Assert.StartsWith("credential-ref:", completed.CredentialReference, StringComparison.Ordinal);
        Assert.Equal(secret, secrets.Value);
        Assert.DoesNotContain(secret, System.Text.Json.JsonSerializer.Serialize(sink.Events), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, System.Text.Json.JsonSerializer.Serialize(repository.Value), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Event_collection_survives_a_repository_failure_after_bounded_retries()
    {
        var repository = new RecoveringEventRepository(3);
        await using var hub = new ExecutionEventHub(repository);
        hub.TryPublish(CreateEvent("First"));
        for (var attempt = 0; attempt < 40 && hub.PersistenceFailureCount < 3; attempt++)
            await Task.Delay(25);

        hub.TryPublish(CreateEvent("Second"));
        for (var attempt = 0; attempt < 40 && repository.Saved.All(item => item.Name != "Second"); attempt++)
            await Task.Delay(25);

        Assert.Equal(3, hub.PersistenceFailureCount);
        Assert.Contains(repository.Saved, item => item.Name == "Second");
    }

    private static ExecutionEvent CreateEvent(string name) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, ExecutionOrigin.Haven,
        ExecutionActionType.ToolCall, ExecutionActionStatus.Completed, name, null, null, "test",
        DateTimeOffset.UtcNow);

    private sealed class MemoryRemediationRepository : IRemediationRepository
    {
        public RemediationRequest? Value { get; private set; }
        public Task UpsertAsync(RemediationRequest request, CancellationToken cancellationToken) { Value = request; return Task.CompletedTask; }
        public Task<RemediationRequest?> GetAsync(Guid remediationId, CancellationToken cancellationToken) => Task.FromResult(Value?.Id == remediationId ? Value : null);
        public Task<IReadOnlyList<RemediationRequest>> GetWaitingAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RemediationRequest>>(Value is { State: RemediationState.Waiting or RemediationState.InProgress } value ? [value] : []);
    }

    private sealed class MemorySecretStore : IProviderSecretStore
    {
        public string? Value { get; private set; }
        public Task SetAsync(string providerId, string secretName, string secret, CancellationToken cancellationToken) { Value = secret; return Task.CompletedTask; }
        public Task<string?> GetAsync(string providerId, string secretName, CancellationToken cancellationToken) => Task.FromResult(Value);
        public Task DeleteAsync(string providerId, string secretName, CancellationToken cancellationToken) { Value = null; return Task.CompletedTask; }
    }

    private sealed class RecordingSink : IExecutionEventSink
    {
        public List<ExecutionEvent> Events { get; } = [];
        public bool TryPublish(ExecutionEvent executionEvent) { Events.Add(executionEvent); return true; }
    }

    private sealed class RecoveringEventRepository(int failuresBeforeSuccess) : IExecutionEventRepository
    {
        private readonly object _gate = new();
        private int _failuresRemaining = failuresBeforeSuccess;
        private readonly List<ExecutionEvent> _saved = [];
        public IReadOnlyList<ExecutionEvent> Saved { get { lock (_gate) return _saved.ToArray(); } }

        public Task AppendAsync(IReadOnlyList<ExecutionEvent> events, CancellationToken cancellationToken)
        {
            if (Interlocked.Decrement(ref _failuresRemaining) >= 0)
                throw new IOException("Temporary persistence failure.");
            lock (_gate) _saved.AddRange(events);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ExecutionEvent>> GetExecutionAsync(Guid executionId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExecutionEvent>>(Saved.Where(item => item.ExecutionId == executionId).ToArray());

        public Task<IReadOnlyList<ExecutionSummary>> SearchExecutionsAsync(string? query, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExecutionSummary>>([]);
    }
}
