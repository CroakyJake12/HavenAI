using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Extended incremental GenUI operations beyond basic patches.
/// Supports structured state mutations that preserve user selections,
/// input state, scroll position, focus and ongoing interactions.
/// </summary>
public enum GenUiIncrementalOperation
{
    SetState,
    PatchState,
    AddComponent,
    RemoveComponent,
    ReplaceComponent,
    MoveComponent,
    UpdateProperties,
    SetSelection,
    SetError,
    SetProgress,
    DismissSurface
}

/// <summary>
/// A structured incremental operation on a GenUI instance.
/// Unlike raw patches, these operations carry semantic meaning
/// that the renderer can use to animate transitions smoothly.
/// </summary>
public sealed record GenUiIncrementalChange(
    Guid ChangeId,
    Guid InstanceId,
    GenUiIncrementalOperation Operation,
    string? TargetComponentId,
    string? SourceContainerId,
    string? DestinationContainerId,
    int? Position,
    string? PropertyKey,
    JsonElement? Value,
    string? ErrorMessage,
    double? ProgressValue,
    DateTimeOffset Timestamp)
{
    /// <summary>Converts to the underlying patch representation for store application.</summary>
    public GenUiStatePatch ToPatch() => Operation switch
    {
        GenUiIncrementalOperation.SetState or GenUiIncrementalOperation.PatchState =>
            new GenUiStatePatch(ChangeId, InstanceId, GenUiPatchOperation.Replace,
                "state", PropertyKey ?? string.Empty, Value, Timestamp),
        GenUiIncrementalOperation.UpdateProperties =>
            new GenUiStatePatch(ChangeId, InstanceId, GenUiPatchOperation.Replace,
                TargetComponentId ?? string.Empty, PropertyKey ?? string.Empty, Value, Timestamp),
        GenUiIncrementalOperation.SetError =>
            new GenUiStatePatch(ChangeId, InstanceId, GenUiPatchOperation.Replace,
                TargetComponentId ?? string.Empty, "error", Value, Timestamp),
        GenUiIncrementalOperation.SetProgress =>
            new GenUiStatePatch(ChangeId, InstanceId, GenUiPatchOperation.Replace,
                TargetComponentId ?? string.Empty, "value",
                ProgressValue.HasValue ? JsonSerializer.SerializeToElement(ProgressValue.Value) : Value, Timestamp),
        _ => new GenUiStatePatch(ChangeId, InstanceId, GenUiPatchOperation.Replace,
                TargetComponentId ?? string.Empty, PropertyKey ?? string.Empty, Value, Timestamp)
    };
}

/// <summary>
/// Applies incremental changes to GenUI instances while preserving
/// unrelated user state. Changes are idempotent and ordered.
/// </summary>
public sealed class GenUiIncrementalUpdater
{
    private readonly GenUiInstanceStore _instances;

    public GenUiIncrementalUpdater(GenUiInstanceStore instances) => _instances = instances;

    public event EventHandler<GenUiIncrementalChange>? ChangeApplied;

    /// <summary>
    /// Applies a single incremental change. Returns true if the change
    /// was applied (idempotent; duplicate change IDs are skipped).
    /// </summary>
    public bool Apply(GenUiIncrementalChange change)
    {
        switch (change.Operation)
        {
            case GenUiIncrementalOperation.DismissSurface:
                return _instances.Remove(change.InstanceId);

            case GenUiIncrementalOperation.AddComponent:
            case GenUiIncrementalOperation.RemoveComponent:
            case GenUiIncrementalOperation.ReplaceComponent:
            case GenUiIncrementalOperation.MoveComponent:
                return ApplyStructuralChange(change);

            default:
                var applied = _instances.ApplyPatch(change.ToPatch());
                if (applied) ChangeApplied?.Invoke(this, change);
                return applied;
        }
    }

    /// <summary>
    /// Applies a batch of incremental changes atomically.
    /// </summary>
    public IReadOnlyList<bool> ApplyBatch(IEnumerable<GenUiIncrementalChange> changes)
    {
       var batch = changes.ToArray();
       if (batch.Length == 0) return [];
       if (batch.Any(change => change.Operation is GenUiIncrementalOperation.AddComponent
          or GenUiIncrementalOperation.RemoveComponent
          or GenUiIncrementalOperation.ReplaceComponent
          or GenUiIncrementalOperation.MoveComponent
          or GenUiIncrementalOperation.DismissSurface))
          throw new InvalidOperationException("Atomic batches currently support state/property/progress/error changes only.");

       var results = _instances.ApplyPatchesAtomically(batch.Select(change => change.ToPatch()).ToArray());
       for (var i = 0; i < batch.Length; i++)
          if (results[i]) ChangeApplied?.Invoke(this, batch[i]);
       return results;
    }
    private bool ApplyStructuralChange(GenUiIncrementalChange change)
    {
        var document = _instances.TryGet(change.InstanceId);
        if (document is null) return false;

        GenUiDocument updated = change.Operation switch
        {
            GenUiIncrementalOperation.AddComponent => AddComponentToDocument(document, change),
            GenUiIncrementalOperation.RemoveComponent => RemoveComponentFromDocument(document, change),
            GenUiIncrementalOperation.ReplaceComponent => ReplaceComponentInDocument(document, change),
            GenUiIncrementalOperation.MoveComponent => MoveComponentInDocument(document, change),
            _ => document
        };

        if (updated == document) return false;
        _instances.Register(updated with { UpdatedAt = change.Timestamp });
        ChangeApplied?.Invoke(this, change);
        return true;
    }

    private static GenUiDocument AddComponentToDocument(GenUiDocument document, GenUiIncrementalChange change)
    {
        if (change.Value is null || change.Value?.ValueKind != JsonValueKind.Object) return document;
        var component = JsonSerializer.Deserialize<GenUiComponent>(change.Value!.Value.GetRawText());
        if (component is null) return document;

        if (change.DestinationContainerId is null)
        {
            return document with
            {
                Root = document.Root with
                {
                    Children = [.. document.Root.Children, component]
                }
            };
        }

        return document with { Root = AddChildToContainer(document.Root, change.DestinationContainerId, component, change.Position) };
    }

    private static GenUiDocument RemoveComponentFromDocument(GenUiDocument document, GenUiIncrementalChange change)
    {
        if (change.TargetComponentId is null) return document;
        return document with { Root = RemoveChild(document.Root, change.TargetComponentId) };
    }

    private static GenUiDocument ReplaceComponentInDocument(GenUiDocument document, GenUiIncrementalChange change)
    {
        if (change.TargetComponentId is null || change.Value is null || change.Value?.ValueKind != JsonValueKind.Object) return document;
        var replacement = JsonSerializer.Deserialize<GenUiComponent>(change.Value!.Value.GetRawText());
        if (replacement is null) return document;
        return document with { Root = ReplaceChild(document.Root, change.TargetComponentId, replacement) };
    }

    private static GenUiDocument MoveComponentInDocument(GenUiDocument document, GenUiIncrementalChange change)
    {
        if (change.TargetComponentId is null || change.DestinationContainerId is null) return document;
        var root = RemoveChild(document.Root, change.TargetComponentId);
        var document2 = document with { Root = root };
        var component = FindComponent(document.Root, change.TargetComponentId);
        if (component is null) return document;
        return document2 with { Root = AddChildToContainer(root, change.DestinationContainerId, component, change.Position) };
    }

    private static GenUiComponent AddChildToContainer(GenUiComponent root, string containerId, GenUiComponent child, int? position)
    {
        if (root.ComponentId.Equals(containerId, StringComparison.Ordinal))
        {
            var children = root.Children.ToList();
            if (position.HasValue && position.Value >= 0 && position.Value <= children.Count)
                children.Insert(position.Value, child);
            else
                children.Add(child);
            return root with { Children = children };
        }
        return root with { Children = root.Children.Select(c => AddChildToContainer(c, containerId, child, position)).ToArray() };
    }

    private static GenUiComponent RemoveChild(GenUiComponent root, string targetId)
    {
        var children = root.Children
            .Where(c => !c.ComponentId.Equals(targetId, StringComparison.Ordinal))
            .Select(c => RemoveChild(c, targetId))
            .ToArray();
        return root with { Children = children };
    }

    private static GenUiComponent ReplaceChild(GenUiComponent root, string targetId, GenUiComponent replacement)
    {
        if (root.ComponentId.Equals(targetId, StringComparison.Ordinal)) return replacement;
        return root with { Children = root.Children.Select(c => ReplaceChild(c, targetId, replacement)).ToArray() };
    }

    private static GenUiComponent? FindComponent(GenUiComponent root, string targetId)
    {
        if (root.ComponentId.Equals(targetId, StringComparison.Ordinal)) return root;
        foreach (var child in root.Children)
        {
            var found = FindComponent(child, targetId);
            if (found is not null) return found;
        }
        return null;
    }
}
