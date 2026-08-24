using System.Globalization;
using System.Text.Json;

namespace Haven.Desktop.Views.Pages.Data;

internal sealed record DataSpreadsheetLayoutState(
    int Version,
    int FrozenRows,
    int FrozenColumns,
    Dictionary<int, double> RowHeights,
    Dictionary<int, double> ColumnWidths)
{
    public const int CurrentVersion = 1;
    public const int MaximumFrozenRows = 100;
    public const int MaximumFrozenColumns = 50;
    public const double MinimumRowHeight = 18;
    public const double MaximumRowHeight = 240;
    public const double MinimumColumnWidth = 48;
    public const double MaximumColumnWidth = 640;

    public static DataSpreadsheetLayoutState Empty { get; } = new(
        CurrentVersion, 0, 0, new Dictionary<int, double>(), new Dictionary<int, double>());

    public DataSpreadsheetLayoutState Normalize() => new(
        CurrentVersion,
        Math.Clamp(FrozenRows, 0, MaximumFrozenRows),
        Math.Clamp(FrozenColumns, 0, MaximumFrozenColumns),
        NormalizeSizes(RowHeights, 0, DataSpreadsheetSurface.MaximumRows - 1, MinimumRowHeight, MaximumRowHeight),
        NormalizeSizes(ColumnWidths, 0, DataSpreadsheetSurface.MaximumColumns - 1, MinimumColumnWidth, MaximumColumnWidth));

    private static Dictionary<int, double> NormalizeSizes(
        Dictionary<int, double>? source,
        int minimumIndex,
        int maximumIndex,
        double minimumSize,
        double maximumSize)
    {
        var normalized = new Dictionary<int, double>();
        if (source is null) return normalized;
        foreach (var (index, size) in source)
        {
            if (index < minimumIndex || index > maximumIndex || double.IsNaN(size) || double.IsInfinity(size)) continue;
            normalized[index] = Math.Clamp(size, minimumSize, maximumSize);
        }
        return normalized;
    }
}

internal static class DataSpreadsheetLayoutMetadata
{
    private const string StateKey = "haven.data.sheetLayout.v1";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static DataSpreadsheetLayoutState Read(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || !metadata.TryGetValue(StateKey, out var json) || string.IsNullOrWhiteSpace(json))
            return DataSpreadsheetLayoutState.Empty;
        try
        {
            var parsed = JsonSerializer.Deserialize<DataSpreadsheetLayoutState>(json, Options);
            return parsed is { Version: DataSpreadsheetLayoutState.CurrentVersion }
                ? parsed.Normalize()
                : DataSpreadsheetLayoutState.Empty;
        }
        catch (JsonException)
        {
            return DataSpreadsheetLayoutState.Empty;
        }
    }

    public static void Write(IDictionary<string, string> metadata, DataSpreadsheetLayoutState state)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(state);
        var normalized = state.Normalize();
        if (normalized.FrozenRows == 0 && normalized.FrozenColumns == 0
            && normalized.RowHeights.Count == 0 && normalized.ColumnWidths.Count == 0)
        {
            metadata.Remove(StateKey);
            return;
        }
        metadata[StateKey] = JsonSerializer.Serialize(normalized, Options);
    }

    public static string Describe(DataSpreadsheetLayoutState state)
    {
        var normalized = state.Normalize();
        return string.Create(CultureInfo.InvariantCulture,
            $"Freeze {normalized.FrozenRows} row(s), {normalized.FrozenColumns} column(s) · {normalized.RowHeights.Count} custom row height(s) · {normalized.ColumnWidths.Count} custom column width(s)");
    }
}
