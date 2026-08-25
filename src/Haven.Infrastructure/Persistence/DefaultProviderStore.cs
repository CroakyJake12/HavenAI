/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/Persistence/DefaultProviderStore.cs, in the Infrastructure layer.
 * What: Owns VersionedDefaultProviderStore — JSON-backed persistence for per-category default
 *       provider assignments through IVersionedSettingsStore (atomic, versioned, exportable).
 * How: One settings key holds the whole assignment map; corrupt or partial data falls back to empty.
 * Why: Persistence details stay in Infrastructure so Application policy remains platform-neutral.
 * Maintenance: Unknown categories are preserved verbatim so future categories survive round trips.
 */

using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed record DefaultProviderDocument(IReadOnlyDictionary<string, string> Assignments);

public sealed class VersionedDefaultProviderStore : IDefaultProviderStore
{
    private const string Key = "actions.default-providers.v1";
    private readonly IVersionedSettingsStore _settings;

    public VersionedDefaultProviderStore(IVersionedSettingsStore settings) => _settings = settings;

    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken)
    {
        var document = await _settings.GetAsync<DefaultProviderDocument>(Key, cancellationToken).ConfigureAwait(false);
        return document?.Assignments ?? new Dictionary<string, string>();
    }

    public async Task SetAsync(string categoryKey, string appKeyOrAsk, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(categoryKey)) throw new ArgumentException("Category key is required.", nameof(categoryKey));
        if (string.IsNullOrWhiteSpace(appKeyOrAsk)) throw new ArgumentException("Assignment is required.", nameof(appKeyOrAsk));
        var current = new Dictionary<string, string>(await GetAllAsync(cancellationToken).ConfigureAwait(false), StringComparer.OrdinalIgnoreCase);
        current[categoryKey.Trim()] = appKeyOrAsk.Trim();
        await _settings.SetAsync(Key, new DefaultProviderDocument(current), cancellationToken).ConfigureAwait(false);
    }
}
