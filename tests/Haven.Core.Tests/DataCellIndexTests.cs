using Haven.Core;

namespace Haven.Core.Tests;

public sealed class DataCellIndexTests
{
    [Fact]
    public void Large_sparse_sheet_supports_indexed_lookup_edit_remove_and_normalize()
    {
        var sheet = DataSheet.Create(0, "Large");
        for (var row = 0; row < 10_000; row++)
            sheet.SetCell(row, row % 64, row.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(10_000, sheet.Cells.Count);
        Assert.Equal("9999", sheet.GetCell(9_999, 9_999 % 64)?.Value);
        Assert.Equal("5000", sheet.GetCell(5_000, 5_000 % 64)?.Value);

        sheet.SetCell(5_000, 5_000 % 64, "edited");
        Assert.Equal("edited", sheet.GetCell(5_000, 5_000 % 64)?.Value);

        sheet.SetCell(5_000, 5_000 % 64, string.Empty);
        Assert.Null(sheet.GetCell(5_000, 5_000 % 64));
        Assert.Equal(9_999, sheet.Cells.Count);

        sheet.Normalize(0);
        Assert.Equal("9999", sheet.GetCell(9_999, 9_999 % 64)?.Value);
        Assert.Null(sheet.GetCell(5_000, 5_000 % 64));
    }

    [Fact]
    public void Replacing_cells_collection_rebuilds_lookup_without_stale_values()
    {
        var sheet = DataSheet.Create(0);
        sheet.SetCell(0, 0, "old");
        Assert.Equal("old", sheet.GetCell(0, 0)?.Value);

        sheet.Cells =
        [
            new DataCell { Row = 0, Column = 0, Value = "new" },
            new DataCell { Row = 99, Column = 25, Value = "far" }
        ];

        Assert.Equal("new", sheet.GetCell(0, 0)?.Value);
        Assert.Equal("far", sheet.GetCell(99, 25)?.Value);
    }
}
