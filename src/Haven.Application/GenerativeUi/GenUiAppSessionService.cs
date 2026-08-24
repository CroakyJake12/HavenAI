using System.Collections.Concurrent;
using Haven.Core;

namespace Haven.Application;

/// <summary>Owns the durable lifecycle of generated app instances across process restarts.</summary>
public sealed class GenUiAppSessionService(IGenUiAppRepository repository, GenUiInstanceStore instances)
{
    private readonly ConcurrentDictionary<Guid, GenUiAppDefinition> _openDefinitions = new();

    public async Task<GenUiAppDefinition> OpenAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        var definition = await repository.GetAsync(instanceId, cancellationToken)
            ?? throw new KeyNotFoundException($"Generated UI instance '{instanceId}' was not found.");
        var validated = RequireValid(definition);
        instances.Register(validated.Document);
        _openDefinitions[instanceId] = validated;
        return validated;
    }

    public async Task SaveAsync(GenUiAppDefinition definition, CancellationToken cancellationToken)
    {
        var validated = RequireValid(definition);
        await repository.UpsertAsync(validated, cancellationToken);
        instances.Register(validated.Document);
        _openDefinitions[validated.Document.Origin.InstanceId] = validated;
    }

    public async Task<bool> PersistCurrentStateAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        var document = instances.TryGet(instanceId);
        if (document is null) return false;
        if (!_openDefinitions.TryGetValue(instanceId, out var definition))
        {
            definition = await repository.GetAsync(instanceId, cancellationToken);
            if (definition is null) return false;
        }
        var updated = RequireValid(definition with { Document = document });
        await repository.UpsertAsync(updated, cancellationToken);
        _openDefinitions[instanceId] = updated;
        return true;
    }

    public async Task CloseAsync(Guid instanceId, bool persist, CancellationToken cancellationToken)
    {
        if (persist) await PersistCurrentStateAsync(instanceId, cancellationToken);
        _openDefinitions.TryRemove(instanceId, out _);
        instances.Remove(instanceId);
    }

    private static GenUiAppDefinition RequireValid(GenUiAppDefinition definition)
    {
        var result = GenUiSemanticValidator.ValidateAndRepair(definition);
        if (!result.IsValid) throw new InvalidOperationException(string.Join(" ", result.Errors));
        return result.Definition;
    }
}
