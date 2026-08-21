using System.Text.Json;

namespace Haven.Desktop.Views.Pages.Data;

internal sealed record DataSpreadsheetTableState(
    int Version,
    int StartRow,
    int StartColumn,
    int EndRow,
    int EndColumn,
    bool HasHeaders,
    int? FilterColumn,
    string FilterText)
{
    public const int CurrentVersion = 1;

    public DataSpreadsheetTableState Normalize()
    {
        var startRow = Math.Clamp(Math.Min(StartRow, EndRow), 0, DataSpreadsheetSurface.MaximumRows - 1);
        var endRow = Math.Clamp(Math.Max(StartRow, EndRow), startRow, DataSpreadsheetSurface.MaximumRows - 1);
        var startColumn = Math.Clamp(Math.Min(StartColumn, EndColumn), 0, DataSpreadsheetSurface.MaximumColumns - 1);
        var endColumn = Math.Clamp(Math.Max(StartColumn, EndColumn), startColumn, DataSpreadsheetSurface.MaximumColumns - 1);
        var filterColumn = FilterColumn is >= 0 && FilterColumn >= startColumn && FilterColumn <= endColumn ? FilterColumn : null;
        return this with { Version = CurrentVersion, StartRow = startRow, EndRow = endRow, StartColumn = startColumn, EndColumn = endColumn, FilterColumn = filterColumn, FilterText = (FilterText ?? string.Empty).Trim() };
    }

    public bool Contains(int row, int column) => row >= StartRow && row <= EndRow && column >= StartColumn && column <= EndColumn;
    public int DataStartRow => HasHeaders && StartRow < EndRow ? StartRow + 1 : StartRow;
}

internal static class DataSpreadsheetTableMetadata
{
    private const string StateKey = "haven.data.table.v1";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static DataSpreadsheetTableState? Read(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || !metadata.TryGetValue(StateKey, out var json) || string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var state = JsonSerializer.Deserialize<DataSpreadsheetTableState>(json, Options);
            return state is { Version: DataSpreadsheetTableState.CurrentVersion } ? state.Normalize() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static void Write(IDictionary<string, string> metadata, DataSpreadsheetTableState? state)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (state is null) { metadata.Remove(StateKey); return; }
        metadata[StateKey] = JsonSerializer.Serialize(state.Normalize(), Options);
    }
}
