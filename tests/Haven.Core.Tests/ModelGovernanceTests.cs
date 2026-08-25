using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class ModelPermissionEvaluatorTests
{
    private static ProviderModelDescriptor LocalModel(string name = "qwen3.8:27b", string family = "qwen3", string parameters = "27B") =>
        new("ollama", true, new ModelDescriptor(name, 1_000_000, family, parameters, "Q8",
            new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UtcNow));

    private static ProviderModelDescriptor CloudModel(string provider = "openai", string name = "gpt-4o") =>
        new(provider, false, new ModelDescriptor(name, 0, "gpt", string.Empty, string.Empty,
            new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UtcNow));

    [Fact]
    public void EmptyPolicyAllowsEverything()
    {
        var decision = ModelPermissionEvaluator.Evaluate(ModelPermissionPolicy.Empty, LocalModel(), RestrictedModelCapability.EditFiles);

        Assert.True(decision.Allowed);
    }

    [Fact]
    public void ExactModelRuleDeniesMatchedModelOnly()
    {
        var policy = new ModelPermissionPolicy(
        [
            ModelPermissionRule.Create(ModelPermissionTargetKind.ExactModel, "qwen3.8:27b", ModelPermissionScope.ThisDevice,
                RestrictedModelCapability.EditFiles, RestrictedModelCapability.RunCommands)
        ]);

        Assert.False(ModelPermissionEvaluator.Evaluate(policy, LocalModel(), RestrictedModelCapability.EditFiles).Allowed);
        Assert.True(ModelPermissionEvaluator.Evaluate(policy, LocalModel("other-model"), RestrictedModelCapability.EditFiles).Allowed);
    }

    [Fact]
    public void ParameterSizeBelowRuleIgnoresUnknownParameterCounts()
    {
        var policy = new ModelPermissionPolicy(
        [
            new ModelPermissionRule(Guid.NewGuid(), ModelPermissionTargetKind.ParameterSizeBelow, string.Empty,
                27, ModelPermissionScope.ThisDevice, new HashSet<RestrictedModelCapability> { RestrictedModelCapability.RunCommands })
        ]);

        // Unknown parameter size must NOT be governed by a size rule; a more specific rule is required.
        Assert.True(ModelPermissionEvaluator.Evaluate(policy, CloudModel(), RestrictedModelCapability.RunCommands).Allowed);
        // Known smaller parameter count is denied.
        Assert.False(ModelPermissionEvaluator.Evaluate(policy, LocalModel(parameters: "7B"), RestrictedModelCapability.RunCommands).Allowed);
        // Equal-or-larger parameter counts are unaffected ("below" is strict).
        Assert.True(ModelPermissionEvaluator.Evaluate(policy, LocalModel(parameters: "27B"), RestrictedModelCapability.RunCommands).Allowed);
    }

    [Fact]
    public void MoreSpecificAllowingContextStillDeniedByMatchingDeny()
    {
        var policy = new ModelPermissionPolicy(
        [
            ModelPermissionRule.Create(ModelPermissionTargetKind.Provider, "ollama", ModelPermissionScope.ThisDevice,
                RestrictedModelCapability.ComputerUse),
            ModelPermissionRule.Create(ModelPermissionTargetKind.ExactModel, "qwen3.8:27b", ModelPermissionScope.ThisDevice,
                RestrictedModelCapability.BrowserAutomation)
        ]);

        var computerDecision = ModelPermissionEvaluator.Evaluate(policy, LocalModel(), RestrictedModelCapability.ComputerUse);
        Assert.False(computerDecision.Allowed);
        Assert.Equal(ModelPermissionTargetKind.Provider, computerDecision.DenyingRule!.Target);
    }

    [Fact]
    public void DeviceScopeRulesDoNotApplyAcrossMesh()
    {
        var policy = new ModelPermissionPolicy(
        [
            ModelPermissionRule.Create(ModelPermissionTargetKind.ModelFamily, "qwen3", ModelPermissionScope.ThisDevice,
                RestrictedModelCapability.EditFiles)
        ]);

        Assert.False(ModelPermissionEvaluator.Evaluate(policy, LocalModel(), RestrictedModelCapability.EditFiles, acrossMesh: false).Allowed);
        Assert.True(ModelPermissionEvaluator.Evaluate(policy, LocalModel(), RestrictedModelCapability.EditFiles, acrossMesh: true).Allowed);
    }

    [Fact]
    public void MeshWideRuleAppliesEverywhere()
    {
        var policy = new ModelPermissionPolicy(
        [
            ModelPermissionRule.Create(ModelPermissionTargetKind.CloudModels, string.Empty, ModelPermissionScope.AcrossMesh,
                RestrictedModelCapability.RunCommands)
        ]);

        Assert.False(ModelPermissionEvaluator.Evaluate(policy, CloudModel(), RestrictedModelCapability.RunCommands, acrossMesh: false).Allowed);
        Assert.False(ModelPermissionEvaluator.Evaluate(policy, CloudModel(), RestrictedModelCapability.RunCommands, acrossMesh: true).Allowed);
        Assert.True(ModelPermissionEvaluator.Evaluate(policy, LocalModel(), RestrictedModelCapability.RunCommands).Allowed);
    }

    [Fact]
    public void ToolMapCoversMutationTools()
    {
        Assert.Equal(RestrictedModelCapability.EditFiles, ModelToolPermissionMap.Map("write_file"));
        Assert.Equal(RestrictedModelCapability.EditFiles, ModelToolPermissionMap.Map("apply_change_set"));
        Assert.Equal(RestrictedModelCapability.RunCommands, ModelToolPermissionMap.Map("run_command"));
        Assert.Equal(RestrictedModelCapability.ComputerUse, ModelToolPermissionMap.Map("computer_snapshot"));
        Assert.Null(ModelToolPermissionMap.Map("list_files"));
        Assert.Null(ModelToolPermissionMap.Map(null));
    }
}

public sealed class ModelPersonalityServiceTests
{
    private sealed class FakePersonalisationStore : IModelPersonalisationStore
    {
        public ModelPersonality Shared { get; private set; } = new(PersonalityLevel.High, PersonalityLevel.VeryLow, PersonalityLevel.Low, PersonalityLevel.Moderate, PersonalityLevel.Moderate, PersonalityLevel.High);
        public List<ModelPersonalisationEntry> Entries { get; } = [];

        public Task<ModelPersonality> GetSharedDefaultsAsync(CancellationToken cancellationToken) => Task.FromResult(Shared);
        public Task SetSharedDefaultsAsync(ModelPersonality personality, CancellationToken cancellationToken)
        {
            Shared = personality;
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<ModelPersonalisationEntry>> GetEntriesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ModelPersonalisationEntry>>(Entries.ToArray());
        public Task SaveEntryAsync(ModelPersonalisationEntry entry, CancellationToken cancellationToken)
        {
            Entries.RemoveAll(item => item.ModelKey.Equals(entry.ModelKey, StringComparison.OrdinalIgnoreCase));
            Entries.Add(entry);
            return Task.CompletedTask;
        }
        public Task RemoveEntryAsync(string modelKey, CancellationToken cancellationToken)
        {
            Entries.RemoveAll(item => item.ModelKey.Equals(modelKey, StringComparison.OrdinalIgnoreCase));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task WithoutOverrideSharedDefaultsApply()
    {
        var store = new FakePersonalisationStore();
        var service = new ModelPersonalityService(store);

        var resolved = await service.ResolveEffectiveAsync("smol2.1", CancellationToken.None);

        Assert.Equal(PersonalityLevel.High, resolved.Friendliness);
    }

    [Fact]
    public async Task OverrideReplacesWholeProfileWithoutCopyingDefaults()
    {
        var store = new FakePersonalisationStore();
        await store.SaveEntryAsync(new ModelPersonalisationEntry("Big Bad Model",
            Nickname: "Big Bad Model",
            Personality: new ModelPersonality(Seriousness: PersonalityLevel.VeryHigh)), CancellationToken.None);
        var service = new ModelPersonalityService(store);

        var resolved = await service.ResolveEffectiveAsync("big bad model", CancellationToken.None);
        var nickname = await service.ResolveNicknameAsync("BIG BAD MODEL", CancellationToken.None);

        Assert.Equal(PersonalityLevel.VeryHigh, resolved.Seriousness);
        Assert.Equal("Big Bad Model", nickname);
    }

    [Fact]
    public void PromptTextUsesHumanLabels()
    {
        var text = ModelPersonalityPrompt.Describe(new ModelPersonality(Friendliness: PersonalityLevel.VeryHigh, MemoryReferences: PersonalityLevel.VeryLow));

        Assert.Contains("Very warm and friendly", text);
        Assert.Contains("Rarely mention remembered information", text);
        Assert.DoesNotContain("(4)", text);
    }

    [Fact]
    public void ProviderQualifiedKeysMatchBareNames()
    {
        Assert.True(ModelPersonalityService.MatchesKey("openai:gpt-4o", "gpt-4o"));
        Assert.False(ModelPersonalityService.MatchesKey("openai:gpt-4o", "gpt-4o-mini"));
    }
}
