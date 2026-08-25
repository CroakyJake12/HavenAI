using System.Text;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Supplies active Learn Me records for prompt injection. Implemented by Haven Library storage so
/// chat orchestration depends on the contract, never on SQLite details.
/// </summary>
public interface IMemoryQuerySource
{
    /// <summary>
    /// Returns active, unexpired Learn Me records ordered pinned first, then confidence and
    /// freshness. Returns an empty list when nothing qualifies.
    /// </summary>
    Task<IReadOnlyList<KnowledgeRecord>> GetActiveLearnMeAsync(int limit, CancellationToken cancellationToken);
}

/// <summary>
/// Renders stored Learn Me memory into a compact system directive and gates how much is included
/// per the user's personality Memory References level. All logic is pure and deterministic so the
/// frequency policy stays testable without storage or model dependencies.
/// </summary>
public static class MemoryInjection
{
    /// <summary>Header rendered above remembered lines inside the directive.</summary>
    public const string DirectiveHeader = "Remembered about this user (from Haven Library):";

    /// <summary>Highest number of records that can ever reach one prompt.</summary>
    public const int MaximumRecords = 8;

    private const int MaximumLineCharacters = 160;
    private const int ModerateCap = 5;
    private const int PinnedOnlyCap = 2;

    /// <summary>
    /// Selects which records may be injected for a personality level: VeryLow/Low inject pinned
    /// records only (at most two), Moderate caps at five records, High/VeryHigh caps at eight.
    /// Input order (pinned first from the source) is preserved.
    /// </summary>
    public static IReadOnlyList<KnowledgeRecord> SelectForLevel(
        PersonalityLevel memoryReferences,
        IReadOnlyList<KnowledgeRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        return memoryReferences switch
        {
            PersonalityLevel.VeryLow or PersonalityLevel.Low => records
                .Where(record => record.IsPinned)
                .Take(PinnedOnlyCap)
                .ToArray(),
            PersonalityLevel.Moderate => records.Take(ModerateCap).ToArray(),
            _ => records.Take(MaximumRecords).ToArray()
        };
    }

    /// <summary>
    /// Reports whether any remembered text should enter the prompt. Pass the count of records that
    /// survived <see cref="SelectForLevel"/>; VeryLow/Low therefore stay silent unless pinned-only
    /// records exist, while any surviving record allows Moderate and above to include memory.
    /// </summary>
    public static bool ShouldInclude(PersonalityLevel memoryReferences, int recordCount) => recordCount > 0;

    /// <summary>
    /// Renders the compact "Remembered" directive with one trimmed bullet line per record; returns
    /// an empty string when there are no records and never emits more than eight bullets.
    /// </summary>
    public static string BuildDirective(IReadOnlyList<KnowledgeRecord> records)
    {
        if (records is null || records.Count == 0) return string.Empty;
        var builder = new StringBuilder();
        builder.Append(DirectiveHeader).Append('\n');
        foreach (var record in records.Take(MaximumRecords))
        {
            var line = LineFor(record);
            if (line.Length == 0) continue;
            builder.Append("• ").Append(line).Append('\n');
        }
        return builder.ToString().TrimEnd();
    }

    private static string LineFor(KnowledgeRecord record)
    {
        var value = (string.IsNullOrWhiteSpace(record.Title) ? record.Summary : record.Title).Trim();
        return value.Length <= MaximumLineCharacters ? value : value[..MaximumLineCharacters];
    }
}
