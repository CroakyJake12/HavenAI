using System.Globalization;
using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed class DataWorkbookQueryService : IDataWorkbookQueryService
{
    public async Task<DataQueryResult> ExecuteReadOnlyAsync(DataWorkbook workbook, string sql, int maxRows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        if (maxRows is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(maxRows), "Query previews support 1-1000 rows.");
        var safety = DataSqlSafety.Analyze(sql);
        if (!safety.IsReadOnly) throw new InvalidOperationException(safety.Message);

        workbook.Normalize();
        SqliteProviderBootstrap.EnsureInitialized();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await MaterializeWorkbookAsync(connection, workbook, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        var rows = new List<IReadOnlyList<string>>();
        var truncated = false;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (rows.Count >= maxRows) { truncated = true; break; }
            var values = new string[reader.FieldCount];
            for (var column = 0; column < reader.FieldCount; column++)
                values[column] = reader.IsDBNull(column) ? string.Empty : Convert.ToString(reader.GetValue(column), CultureInfo.InvariantCulture) ?? string.Empty;
            rows.Add(values);
        }
        return new DataQueryResult(columns, rows, truncated, "Local workbook preview");
    }

    private static async Task MaterializeWorkbookAsync(SqliteConnection connection, DataWorkbook workbook, CancellationToken cancellationToken)
    {
        foreach (var sheet in workbook.Sheets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var maxColumn = Math.Min(Math.Max(0, sheet.Cells.Count == 0 ? 0 : sheet.Cells.Max(cell => cell.Column)), 255);
            var columnDefinitions = Enumerable.Range(0, maxColumn + 1).Select(index => $"{Quote(ColumnName(index))} NUMERIC");
            await using (var create = connection.CreateCommand())
            {
                create.CommandText = $"CREATE TABLE {Quote(sheet.Name)} ({Quote("_row")} INTEGER NOT NULL, {string.Join(", ", columnDefinitions)});";
                await create.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var maxRow = Math.Min(sheet.Cells.Count == 0 ? -1 : sheet.Cells.Max(cell => cell.Row), 99_999);
            for (var row = 0; row <= maxRow; row++)
            {
                await using var insert = connection.CreateCommand();
                var names = new List<string> { Quote("_row") };
                var parameters = new List<string> { "$row" };
                insert.Parameters.AddWithValue("$row", row + 1);
                for (var column = 0; column <= maxColumn; column++)
                {
                    names.Add(Quote(ColumnName(column)));
                    var parameter = $"$c{column}";
                    parameters.Add(parameter);
                    var cell = sheet.GetCell(row, column);
                    insert.Parameters.AddWithValue(parameter, ToSqlValue(cell) ?? DBNull.Value);
                }
                insert.CommandText = $"INSERT INTO {Quote(sheet.Name)} ({string.Join(", ", names)}) VALUES ({string.Join(", ", parameters)});";
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static object? ToSqlValue(DataCell? cell)
    {
        if (cell is null || string.IsNullOrEmpty(cell.Value)) return null;
        return cell.Kind switch
        {
            DataCellKind.Number when double.TryParse(cell.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => number,
            DataCellKind.Boolean when bool.TryParse(cell.Value, out var boolean) => boolean ? 1L : 0L,
            _ => cell.Value
        };
    }

    internal static string ColumnName(int column)
    {
        if (column < 0) throw new ArgumentOutOfRangeException(nameof(column));
        var value = column + 1; var result = string.Empty;
        while (value > 0) { value--; result = (char)('A' + value % 26) + result; value /= 26; }
        return result;
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
