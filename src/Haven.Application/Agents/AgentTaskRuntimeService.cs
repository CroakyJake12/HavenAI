using System.Collections.Concurrent;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Executes saved Agents through Haven's existing Chat/tool loop. Agent configuration can
/// narrow discoverable capabilities, but never creates a second tool executor or approval path.
/// </summary>
public sealed class AgentTaskRuntimeService(
    ICatalogRepository catalog,
    IAgentRunRepository runs,
    IOllamaClient models,
    CapabilityRegistryService capabilityRegistry,
    ChatSessionService chat,
    IPermissionDecisionEngine permissionEngine,
    FloatingActivityStateStore? activityStore = null)
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _activeRuns = new();

    public event Action<AgentRun>? RunChanged;

    public async Task<AgentRun> RunAsync(
        Guid agentId,
        string task,
        CancellationToken cancellationToken,
        Guid? retryOfRunId = null,
        string? resourceReference = null)
    {
        if (string.IsNullOrWhiteSpace(task))
            throw new ArgumentException("An Agent task is required.", nameof(task));

        var agent = (await catalog.GetAgentsAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item => item.Id == agentId)
            ?? throw new InvalidOperationException("The selected Agent is disabled or no longer exists.");

        var installed = await models.GetModelsAsync(cancellationToken).ConfigureAwait(false);
        var model = ResolveModel(agent, installed)
            ?? throw new InvalidOperationException("No text-capable model is installed for this Agent.");

        var policy = AgentExecutionPolicy.Parse(agent.PermissionsJson);
        var discovered = await capabilityRegistry.DiscoverAsync(OperatingSystem.IsAndroid() ? CapabilityPlatform.Android : CapabilityPlatform.Windows, cancellationToken)
            .ConfigureAwait(false);
        var allowedDefinitions = discovered
            .Where(item => item.IsAgentUsable)
            .Where(item => policy.CapabilityKeys.Contains(item.Key))
            .Where(IsGloballyAllowed)
            .ToArray();
        var activeCapabilities = allowedDefinitions.Select(ActiveCapability.FromDefinition).ToArray();

        var run = new AgentRun(
            Guid.NewGuid(),
            agent.Id,
            agent.Name,
            task.Trim(),
            AgentRunStatus.Queued,
            model.Name,
            string.Empty,
            string.Empty,
            JsonSerializer.Serialize(allowedDefinitions.Select(item => item.Key).ToArray()),
            "[]",
            DateTimeOffset.UtcNow,
            null,
            null,
            retryOfRunId,
            string.IsNullOrWhiteSpace(resourceReference) ? null : resourceReference.Trim(),
            0);

        await PersistAsync(run, cancellationToken).ConfigureAwait(false);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (!_activeRuns.TryAdd(run.Id, linked))
            throw new InvalidOperationException("Could not register the Agent run.");

        var activity = new List<ToolActivity>();
        var output = new System.Text.StringBuilder();
        try
        {
            run = run with { Status = AgentRunStatus.Running, StartedAt = DateTimeOffset.UtcNow, ProgressPercent = 10 };
            await PersistAsync(run, cancellationToken).ConfigureAwait(false);

            var now = DateTimeOffset.UtcNow;
            var conversation = new Conversation(
                Guid.NewGuid(),
                HavenMode.Chat,
                ConversationKind.Chat,
                $"Agent · {agent.Name}",
                null,
                null,
                false,
                true,
                now,
                now);

            // Autonomous Agents deliberately use the strictest legacy approval mode.
            // The central PermissionDecisionEngine has already narrowed capabilities above;
            // keeping Chat's per-action gates at Ask ensures an Agent can never become more
            // permissive than the user's global sandbox/tool policy.
            const PermissionMode browserPermission = PermissionMode.Ask;

            var executionTask = BuildExecutionTask(run);
            var executionInstructions = BuildExecutionInstructions(agent.Instructions, policy);
            await foreach (var streamEvent in chat.SendAsync(
                conversation,
                executionTask,
                model,
                EffortLevel.Medium,
                activeCapabilities,
                agent.Name,
                executionInstructions,
                DuoMode.Solo,
                workspaceRoot: null,
                projectContext: null,
                projectInstructions: null,
                images: null,
                cancellationToken: linked.Token,
                generationOptions: new GenerationOptions(ActionLimit: 24),
                filePermission: PermissionMode.Ask,
                commandPermission: PermissionMode.Ask,
                browserPermission: browserPermission,
                availableCapabilities: activeCapabilities).ConfigureAwait(false))
            {
                switch (streamEvent.Kind)
                {
                    case ChatStreamEventKind.AssistantDelta when streamEvent.Delta is not null:
                        output.Append(streamEvent.Delta);
                        break;
                    case ChatStreamEventKind.ToolActivity when streamEvent.ToolActivity is not null:
                        activity.Add(streamEvent.ToolActivity);
                        run = run with
                        {
                            Result = output.ToString(),
                            ActivityJson = JsonSerializer.Serialize(activity),
                            ProgressPercent = Math.Min(90, 10 + (activity.Count * 10))
                        };
                        await PersistAsync(run, CancellationToken.None).ConfigureAwait(false);
                        break;
                    case ChatStreamEventKind.PreflightFailed:
                        var missing = streamEvent.PreflightResult?.Missing
                            .Select(item => item.Reason)
                            .Where(reason => !string.IsNullOrWhiteSpace(reason))
                            .ToArray() ?? [];
                        throw new InvalidOperationException(
                            missing.Length == 0
                                ? "The selected model cannot satisfy this Agent task."
                                : string.Join("; ", missing));
                }
            }

            run = run with
            {
                Status = AgentRunStatus.Completed,
                Result = output.ToString().Trim(),
                ActivityJson = JsonSerializer.Serialize(activity),
                CompletedAt = DateTimeOffset.UtcNow,
                ProgressPercent = 100
            };
            await PersistAsync(run, CancellationToken.None).ConfigureAwait(false);
            return run;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            run = run with
            {
                Status = AgentRunStatus.Cancelled,
                Result = output.ToString().Trim(),
                ActivityJson = JsonSerializer.Serialize(activity),
                Error = "Cancelled.",
                CompletedAt = DateTimeOffset.UtcNow
            };
            await PersistAsync(run, CancellationToken.None).ConfigureAwait(false);
            return run;
        }
        catch (Exception ex)
        {
            run = run with
            {
                Status = AgentRunStatus.Failed,
                Result = output.ToString().Trim(),
                ActivityJson = JsonSerializer.Serialize(activity),
                Error = ex.Message,
                CompletedAt = DateTimeOffset.UtcNow
            };
            await PersistAsync(run, CancellationToken.None).ConfigureAwait(false);
            return run;
        }
        finally
        {
            _activeRuns.TryRemove(run.Id, out _);
        }

        bool IsGloballyAllowed(CapabilityDefinition definition)
        {
            var requiresPermission =
                definition.Availability == CapabilityAvailability.PermissionRequired ||
                definition.RiskClass >= CapabilityRiskClass.Consequential;
            var decision = permissionEngine.Evaluate(
                $"capability:{definition.Key}",
                definition.RiskClass,
                requiresPermission,
                $"Agent '{agent.Name}' requested {definition.Name}.");
            return decision.Kind != PermissionDecisionKind.Denied;
        }
    }

    public bool Cancel(Guid runId)
    {
        if (!_activeRuns.TryGetValue(runId, out var cancellation)) return false;
        cancellation.Cancel();
        return true;
    }

    public async Task<AgentRun> RetryAsync(Guid runId, CancellationToken cancellationToken)
    {
        var previous = await runs.GetAsync(runId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The Agent run no longer exists.");
        return await RunAsync(previous.AgentId, previous.Task, cancellationToken, previous.Id, previous.ResourceReference)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AgentRun>> GetRecentAsync(int limit, CancellationToken cancellationToken)
    {
        var recent = await runs.GetRecentAsync(Math.Clamp(limit, 1, 100), cancellationToken).ConfigureAwait(false);
        var recovered = false;
        for (var index = 0; index < recent.Count; index++)
        {
            var run = recent[index];
            if (run.Status is not (AgentRunStatus.Queued or AgentRunStatus.Running) || _activeRuns.ContainsKey(run.Id))
                continue;

            run = run with
            {
                Status = AgentRunStatus.Failed,
                Error = "Interrupted because Haven stopped before this Agent run completed. Retry to continue.",
                CompletedAt = DateTimeOffset.UtcNow
            };
            await runs.UpsertAsync(run, cancellationToken).ConfigureAwait(false);
            recovered = true;
        }

        return recovered
            ? await runs.GetRecentAsync(Math.Clamp(limit, 1, 100), cancellationToken).ConfigureAwait(false)
            : recent;
    }

    private async Task PersistAsync(AgentRun run, CancellationToken cancellationToken)
    {
        await runs.UpsertAsync(run, cancellationToken).ConfigureAwait(false);
        if (activityStore is not null)
        {
            var activityState = run.Status switch
            {
                AgentRunStatus.Queued => FloatingActivityState.Created,
                AgentRunStatus.Running => FloatingActivityState.Presented,
                AgentRunStatus.Failed => FloatingActivityState.Failed,
                _ => FloatingActivityState.Dismissed
            };
            activityStore.Set(new FloatingActivitySnapshot(
                run.Id, activityState, 0, 0, 0, 0,
                run.Status == AgentRunStatus.Failed ? run.Error : null));
        }
        RunChanged?.Invoke(run);
    }

    private static ModelDescriptor? ResolveModel(
        AgentDefinition agent,
        IReadOnlyList<ModelDescriptor> installed)
    {
        var textModels = installed.Where(item => item.Supports(ToolCapability.Text)).ToArray();
        if (textModels.Length == 0) return null;

        if (!string.IsNullOrWhiteSpace(agent.PreferredModel) &&
            !agent.PreferredModel.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            var preferred = textModels.FirstOrDefault(item =>
                item.Name.Equals(agent.PreferredModel, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null) return preferred;
        }

        if (!string.IsNullOrWhiteSpace(agent.FallbackModel))
        {
            var fallback = textModels.FirstOrDefault(item =>
                item.Name.Equals(agent.FallbackModel, StringComparison.OrdinalIgnoreCase));
            if (fallback is not null) return fallback;
        }

        return textModels.OrderByDescending(item => item.ModifiedAt).First();
    }

    private static string BuildExecutionTask(AgentRun run)
    {
        if (string.IsNullOrWhiteSpace(run.ResourceReference)) return run.Task;
        return run.Task + Environment.NewLine + Environment.NewLine + "Resource reference: " + run.ResourceReference + Environment.NewLine + "Use semantic Haven operations when a compatible capability is available; do not simulate mouse/keyboard interaction for Haven-owned app data.";
    }

    private static string BuildExecutionInstructions(string instructions, AgentExecutionPolicy policy)
    {
        if (policy.KnowledgeResources.Count == 0) return instructions;
        return instructions + Environment.NewLine + Environment.NewLine + "Configured knowledge/resource references:" + Environment.NewLine + "- " + string.Join(Environment.NewLine + "- ", policy.KnowledgeResources);
    }

    internal sealed record AgentExecutionPolicy(IReadOnlySet<string> CapabilityKeys, IReadOnlyList<string> KnowledgeResources)
    {
        public static AgentExecutionPolicy Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new(new HashSet<string>(StringComparer.OrdinalIgnoreCase), []);

            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    return new(new HashSet<string>(StringComparer.OrdinalIgnoreCase), []);

                var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (document.RootElement.TryGetProperty("capabilities", out var capabilities) &&
                    capabilities.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in capabilities.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                            keys.Add(item.GetString()!);
                    }
                }

                AddLegacyFlag(document.RootElement, "webSearch", "web-search", keys);
                AddLegacyFlag(document.RootElement, "browserUse", "browser-use", keys);
                AddLegacyFlag(document.RootElement, "computerUse", "computer-device-use", keys);
                var resources = document.RootElement.TryGetProperty("knowledgeResources", out var configuredResources) && configuredResources.ValueKind == JsonValueKind.Array
                    ? configuredResources.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()).Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item!).ToArray()
                    : [];
                return new(keys, resources);
            }
            catch (JsonException)
            {
                return new(new HashSet<string>(StringComparer.OrdinalIgnoreCase), []);
            }
        }

        private static void AddLegacyFlag(JsonElement root, string propertyName, string capabilityKey, ISet<string> keys)
        {
            if (root.TryGetProperty(propertyName, out var flag) &&
                flag.ValueKind == JsonValueKind.True)
            {
                keys.Add(capabilityKey);
            }
        }
    }
}
