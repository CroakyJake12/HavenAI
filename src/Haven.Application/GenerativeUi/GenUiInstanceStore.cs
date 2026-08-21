using System.Collections.Concurrent;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>Thread-scoped document state with idempotent incremental patch application.</summary>
public sealed class GenUiInstanceStore
{
    private readonly ConcurrentDictionary<Guid, InstanceState> _instances = new();

    public event EventHandler<GenUiDocument>? DocumentChanged;

    public void Register(GenUiDocument document)
    {
        GenerativeUiContractValidator.ValidateAndThrow(document);
        _instances.AddOrUpdate(
            document.Origin.InstanceId,
            _ => new InstanceState(document),
            (_, existing) => existing.Document.Origin.ThreadId == document.Origin.ThreadId
                ? new InstanceState(document)
                : throw new InvalidOperationException("An instance ID cannot move between threads."));
        DocumentChanged?.Invoke(this, document);
    }

    public GenUiDocument? TryGet(Guid instanceId) =>
        _instances.TryGetValue(instanceId, out var state) ? state.Document : null;

    public IReadOnlyList<GenUiDocument> GetForThread(Guid threadId) =>
        _instances.Values.Select(state => state.Document)
            .Where(document => document.Origin.ThreadId == threadId)
            .OrderBy(document => document.UpdatedAt)
            .ToArray();

    public Task ApplyResultAsync(GenUiActionResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var patch in result.Patches) ApplyPatch(patch);
        return Task.CompletedTask;
    }

    public bool ApplyPatch(GenUiStatePatch patch)
    {
        if (!_instances.TryGetValue(patch.InstanceId, out var instance))
            throw new InvalidOperationException($"GenUI instance '{patch.InstanceId}' is not registered.");
        lock (instance.Gate)
        {
            if (!instance.AppliedPatches.Add(patch.PatchId)) return false;
            var document = patch.TargetId.Equals("state", StringComparison.Ordinal)
                ? PatchState(instance.Document, patch)
                : PatchComponent(instance.Document, patch);
            GenerativeUiContractValidator.ValidateAndThrow(document);
            instance.Document = document with { UpdatedAt = patch.Timestamp };
            DocumentChanged?.Invoke(this, instance.Document);
            return true;
        }
    }

    public IReadOnlyList<bool> ApplyPatchesAtomically(IReadOnlyList<GenUiStatePatch> patches)
    {
        if (patches.Count == 0) return [];
        var instanceId = patches[0].InstanceId;
        if (patches.Any(patch => patch.InstanceId != instanceId))
            throw new InvalidOperationException("An atomic GenUI patch batch cannot span multiple instances.");
        if (!_instances.TryGetValue(instanceId, out var instance))
            throw new InvalidOperationException($"GenUI instance '{instanceId}' is not registered.");

        GenUiDocument? changed = null;
        bool[] results;
        lock (instance.Gate)
        {
            var staged = instance.Document;
            var stagedIds = new HashSet<Guid>(instance.AppliedPatches);
            results = new bool[patches.Count];
            for (var i = 0; i < patches.Count; i++)
            {
                var patch = patches[i];
                if (!stagedIds.Add(patch.PatchId)) continue;
                staged = patch.TargetId.Equals("state", StringComparison.Ordinal)
                    ? PatchState(staged, patch)
                    : PatchComponent(staged, patch);
                GenerativeUiContractValidator.ValidateAndThrow(staged);
                staged = staged with { UpdatedAt = patch.Timestamp };
                results[i] = true;
            }

            if (results.Any(value => value))
            {
                instance.Document = staged;
                instance.AppliedPatches.Clear();
                instance.AppliedPatches.UnionWith(stagedIds);
                changed = staged;
            }
        }
        if (changed is not null) DocumentChanged?.Invoke(this, changed);
        return results;
    }
    public bool Remove(Guid instanceId) => _instances.TryRemove(instanceId, out _);

    private static GenUiDocument PatchState(GenUiDocument document, GenUiStatePatch patch)
    {
        var state = new Dictionary<string, JsonElement>(document.State, StringComparer.Ordinal);
        ApplyValue(state, patch.Path, patch);
        return document with { State = state };
    }

    private static GenUiDocument PatchComponent(GenUiDocument document, GenUiStatePatch patch)
    {
        var found = false;
        GenUiComponent Visit(GenUiComponent component)
        {
            if (component.ComponentId.Equals(patch.TargetId, StringComparison.Ordinal))
            {
                found = true;
                var properties = new Dictionary<string, JsonElement>(component.Properties, StringComparer.Ordinal);
                ApplyValue(properties, patch.Path, patch);
                component = component with { Properties = properties };
            }
            return component with { Children = component.Children.Select(Visit).ToArray() };
        }

        var root = Visit(document.Root);
        if (!found) throw new InvalidOperationException($"Patch target component '{patch.TargetId}' does not exist.");
        return document with { Root = root };
    }

    private static void ApplyValue(Dictionary<string, JsonElement> values, string key, GenUiStatePatch patch)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Patch path is required.");
        switch (patch.Operation)
        {
            case GenUiPatchOperation.Remove:
                values.Remove(key);
                break;
            case GenUiPatchOperation.Add when values.ContainsKey(key):
                throw new InvalidOperationException($"Patch cannot add existing path '{key}'.");
            case GenUiPatchOperation.Add or GenUiPatchOperation.Replace:
                values[key] = patch.Value ?? JsonSerializer.SerializeToElement<object?>(null);
                break;
            case GenUiPatchOperation.Append:
                var items = values.TryGetValue(key, out var current) && current.ValueKind == JsonValueKind.Array
                    ? current.EnumerateArray().Select(item => item.Clone()).ToList()
                    : [];
                items.Add((patch.Value ?? JsonSerializer.SerializeToElement<object?>(null)).Clone());
                values[key] = JsonSerializer.SerializeToElement(items);
                break;
        }
    }

    private sealed class InstanceState(GenUiDocument document)
    {
        public object Gate { get; } = new();
        public HashSet<Guid> AppliedPatches { get; } = [];
        public GenUiDocument Document { get; set; } = document;
    }
}
