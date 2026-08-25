/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/Persistence/ModelGovernanceStores.cs, in the Infrastructure layer.
 * What: Owns VersionedModelFallbackOrderStore, VersionedModelPersonalisationStore and
 *       VersionedModelPermissionStore — JSON-backed implementations of the shared model governance
 *       contracts persisted through IVersionedSettingsStore (atomic, versioned, exportable).
 * How: Each store serialises its document under a dedicated settings key; corrupt data falls back to defaults.
 * Why: Persistence details stay in Infrastructure so Application policy remains platform-neutral.
 * Maintenance: Preserve forward compatibility: unknown fields are ignored; null personality members mean
 *              "use Haven defaults" and must survive the round trip as explicit nulls.
 */

using System.Text.Json.Serialization;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>Persisted ordered fallback list (most preferred first).</summary>
public sealed record ModelFallbackOrderDocument(IReadOnlyList<string> Order);

public sealed class VersionedModelFallbackOrderStore : IModelFallbackOrderStore
{
    private const string Key = "models.fallback-order.v1";
    private readonly IVersionedSettingsStore _settings;

    public VersionedModelFallbackOrderStore(IVersionedSettingsStore settings) => _settings = settings;

    public async Task<IReadOnlyList<string>> GetOrderAsync(CancellationToken cancellationToken)
    {
        var document = await _settings.GetAsync<ModelFallbackOrderDocument>(Key, cancellationToken).ConfigureAwait(false);
        return document?.Order ?? [];
    }

    public Task SetOrderAsync(IReadOnlyList<string> orderedModelKeys, CancellationToken cancellationToken)
        => _settings.SetAsync(Key,
            new ModelFallbackOrderDocument(Normalise(orderedModelKeys)), cancellationToken);

    private static IReadOnlyList<string> Normalise(IReadOnlyList<string> keys)
        => keys.Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

/// <summary>Persisted shared personality defaults plus per-model entries.</summary>
public sealed record ModelPersonalisationDocument(
    [property: JsonPropertyName("shared")] ModelPersonality? Shared,
    [property: JsonPropertyName("entries")] IReadOnlyList<ModelPersonalisationEntry> Entries);

public sealed class VersionedModelPersonalisationStore : IModelPersonalisationStore
{
    private const string Key = "models.personalisation.v1";
    private readonly IVersionedSettingsStore _settings;

    public VersionedModelPersonalisationStore(IVersionedSettingsStore settings) => _settings = settings;

    public async Task<ModelPersonality> GetSharedDefaultsAsync(CancellationToken cancellationToken)
    {
        var document = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return document?.Shared ?? ModelPersonality.Defaults;
    }

    public Task SetSharedDefaultsAsync(ModelPersonality personality, CancellationToken cancellationToken)
        => SaveAsync(current => current with { Shared = personality }, cancellationToken);

    public async Task<IReadOnlyList<ModelPersonalisationEntry>> GetEntriesAsync(CancellationToken cancellationToken)
    {
        var document = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return document?.Entries ?? [];
    }

    public async Task SaveEntryAsync(ModelPersonalisationEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (string.IsNullOrWhiteSpace(entry.ModelKey)) throw new ArgumentException("Model key is required.", nameof(entry));
        var normalised = entry with
        {
            ModelKey = entry.ModelKey.Trim(),
            Nickname = string.IsNullOrWhiteSpace(entry.Nickname) ? null : entry.Nickname!.Trim()
        };
        await SaveAsync(current =>
        {
            var entries = current.Entries
                .Where(item => !item.ModelKey.Equals(normalised.ModelKey, StringComparison.OrdinalIgnoreCase))
                .Append(normalised)
                .ToArray();
            return current with { Entries = entries };
        }, cancellationToken);
    }

    public async Task RemoveEntryAsync(string modelKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(modelKey)) return;
        await SaveAsync(current => current with
        {
            Entries = current.Entries.Where(item => !item.ModelKey.Equals(modelKey.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray()
        }, cancellationToken);
    }

    private async Task<ModelPersonalisationDocument?> ReadAsync(CancellationToken cancellationToken)
        => await _settings.GetAsync<ModelPersonalisationDocument>(Key, cancellationToken).ConfigureAwait(false);

    private async Task SaveAsync(Func<ModelPersonalisationDocument, ModelPersonalisationDocument> mutate, CancellationToken cancellationToken)
    {
        var current = await ReadAsync(cancellationToken).ConfigureAwait(false) ?? new ModelPersonalisationDocument(null, []);
        var updated = mutate(current);
        await _settings.SetAsync(Key, updated, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class VersionedModelPermissionStore : IModelPermissionStore
{
    private const string Key = "models.permissions.v1";
    private readonly IVersionedSettingsStore _settings;

    public VersionedModelPermissionStore(IVersionedSettingsStore settings) => _settings = settings;

    public async Task<ModelPermissionPolicy> GetPolicyAsync(CancellationToken cancellationToken)
    {
        var policy = await _settings.GetAsync<ModelPermissionPolicy>(Key, cancellationToken).ConfigureAwait(false);
        return policy ?? ModelPermissionPolicy.Empty;
    }

    public Task SavePolicyAsync(ModelPermissionPolicy policy, CancellationToken cancellationToken)
        => _settings.SetAsync(Key, policy ?? ModelPermissionPolicy.Empty, cancellationToken);
}
