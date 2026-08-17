using Haven.Application;
using Haven.Core;
using Haven.Desktop.Overlay;

namespace Haven.Desktop.Tests;

public sealed class OverlayContextActionCandidateServiceTests
{
    [Fact]
    public async Task Text_context_projects_safe_capability_metadata_without_executing_it()
    {
        var now = new DateTimeOffset(2026, 8, 16, 19, 0, 0, TimeSpan.Zero);
        var repository = new FakeCapabilityRepository(
            Capability("web-search"),
            Capability("create-automation"),
            Capability("computer-device-use"),
            Capability("run-command"));
        var service = new OverlayContextActionCandidateService(
            new CapabilityRegistryService(repository),
            () => now);

        var actions = await service.DiscoverAsync(TextContext(now), CapabilityPlatform.Windows, CancellationToken.None);

        var search = Assert.Single(actions, action => action.Id == "capability:web-search:search");
        Assert.True(search.IsGenerated);
        Assert.True(search.RequiresContext);
        Assert.Equal(CapabilityRiskClass.ReadOnly, search.RiskClass);
        Assert.Equal(CapabilityAvailability.PermissionRequired, search.Availability);
        Assert.Equal("haven.browser", search.ProviderId);
        Assert.Equal("browser.search", search.ImplementationKey);
        Assert.Equal("browser.search", search.ToolName);

        Assert.Contains(actions, action => action.Id == "capability:web-search:read-source");
        Assert.Contains(actions, action => action.Id == "capability:create-automation:create");
        Assert.Contains(actions, action => action.Id == "capability:create-automation:schedule");
        Assert.DoesNotContain(actions, action => action.Id.Contains("computer-device-use", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(actions, action => action.Id.Contains("run-command", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Visual_context_can_offer_inspection_but_keeps_consequential_permission_metadata()
    {
        var now = new DateTimeOffset(2026, 8, 16, 19, 0, 0, TimeSpan.Zero);
        var repository = new FakeCapabilityRepository(Capability("computer-device-use"), Capability("web-search"));
        var service = new OverlayContextActionCandidateService(
            new CapabilityRegistryService(repository),
            () => now);

        var actions = await service.DiscoverAsync(VisualContext(now), CapabilityPlatform.Windows, CancellationToken.None);

        var inspect = Assert.Single(actions, action => action.Id == "capability:computer-device-use:inspect");
        Assert.Equal(CapabilityRiskClass.Consequential, inspect.RiskClass);
        Assert.Equal(CapabilityAvailability.PermissionRequired, inspect.Availability);
        Assert.Equal("haven.device", inspect.ProviderId);
        Assert.Contains(actions, action => action.Id == "capability:web-search:search");
        Assert.DoesNotContain(actions, action => action.Id == "capability:web-search:read-source");
    }

    [Fact]
    public async Task Expired_context_and_malformed_semantic_metadata_produce_no_generated_actions()
    {
        var now = new DateTimeOffset(2026, 8, 16, 19, 0, 0, TimeSpan.Zero);
        var malformed = Capability("web-search") with
        {
            Id = Guid.NewGuid(),
            Key = "malformed",
            SemanticActionsJson = "{not-json"
        };
        var repository = new FakeCapabilityRepository(malformed);
        var service = new OverlayContextActionCandidateService(
            new CapabilityRegistryService(repository),
            () => now);

        var malformedActions = await service.DiscoverAsync(TextContext(now), CapabilityPlatform.Windows, CancellationToken.None);
        Assert.Empty(malformedActions);

        var normalRepository = new FakeCapabilityRepository(Capability("web-search"));
        var normalService = new OverlayContextActionCandidateService(
            new CapabilityRegistryService(normalRepository),
            () => now);
        var expired = TextContext(now.AddMinutes(-10)) with
        {
            Provenance = TextContext(now.AddMinutes(-10)).Provenance with { ExpiresAt = now.AddMinutes(-1) }
        };

        var expiredActions = await normalService.DiscoverAsync(expired, CapabilityPlatform.Windows, CancellationToken.None);
        Assert.Empty(expiredActions);
    }

    private static OverlayContextEnvelope TextContext(DateTimeOffset capturedAt) => new(
        OverlayContextKind.Text,
        "Selected paragraph",
        [],
        null,
        new OverlayContextProvenance(
            "Editor",
            "Document",
            null,
            capturedAt,
            capturedAt.AddMinutes(5),
            OverlayContextPermissionState.Granted,
            "Explicit user selection."));

    private static OverlayContextEnvelope VisualContext(DateTimeOffset capturedAt) => new(
        OverlayContextKind.Region,
        null,
        [],
        "screen-region",
        new OverlayContextProvenance(
            "Browser",
            "Page",
            new OverlaySelectionBounds(10, 20, 600, 400),
            capturedAt,
            capturedAt.AddMinutes(5),
            OverlayContextPermissionState.Granted,
            "Explicit bounded region selection."));

    private static CapabilityDefinition Capability(string key) => key switch
    {
        "web-search" => Definition(
            key, "Web Search", "web-search", "browser.search", "[\"search\",\"read-source\"]",
            CapabilityRiskClass.ReadOnly, CapabilityAvailability.PermissionRequired, "haven.browser"),
        "create-automation" => Definition(
            key, "Create Automation", "automation", "tasks.automation.create", "[\"create\",\"schedule\"]",
            CapabilityRiskClass.Consequential, CapabilityAvailability.PermissionRequired, "haven.tasks"),
        "computer-device-use" => Definition(
            key, "Computer / Device Use", "computer-use", "device.control", "[\"inspect\",\"interact\",\"verify\"]",
            CapabilityRiskClass.Consequential, CapabilityAvailability.PermissionRequired, "haven.device"),
        "run-command" => Definition(
            key, "Run Command", "terminal", "workspace.run-command", "[\"run\",\"inspect-result\"]",
            CapabilityRiskClass.Restricted, CapabilityAvailability.PermissionRequired, "haven.workspace"),
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null)
    };

    private static CapabilityDefinition Definition(
        string key,
        string name,
        string iconKey,
        string implementationKey,
        string semanticActionsJson,
        CapabilityRiskClass riskClass,
        CapabilityAvailability availability,
        string providerId) => new(
            Guid.NewGuid(),
            key,
            name,
            name + " description",
            CapabilityRegistryCatalog.GeneralOwner,
            iconKey,
            "Use through the registered provider.",
            implementationKey,
            semanticActionsJson,
            CapabilityPlatform.Windows,
            riskClass,
            availability,
            "[]",
            providerId,
            IsAttachable: true,
            IsAgentUsable: true,
            IsBuiltIn: true,
            IsEnabled: true,
            UpdatedAt: DateTimeOffset.UnixEpoch);

    private sealed class FakeCapabilityRepository(params CapabilityDefinition[] capabilities) : ICapabilityRepository
    {
        private readonly IReadOnlyList<CapabilityDefinition> _capabilities = capabilities;

        public Task<IReadOnlyList<CapabilityDefinition>> GetCapabilitiesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_capabilities);
        }

        public Task UpsertCapabilityAsync(CapabilityDefinition capability, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SetCapabilityEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteCustomCapabilityAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
