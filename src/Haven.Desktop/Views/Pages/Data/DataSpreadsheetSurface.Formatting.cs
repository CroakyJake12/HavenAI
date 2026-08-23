using System.Globalization;
using Haven.Core;
using Haven.UI;

namespace Haven.Desktop.Views.Pages.Data;

internal sealed partial class DataSpreadsheetSurface
{
    private void DrawFormattedCell(HavenDrawingContext context, DataCell? cell, HavenRect rect, string rawValue, bool selected, bool active, double opacity)
    {
        var fill = CellMetadata(cell, DataCellFormatMetadata.Fill);
        if (!string.IsNullOrWhiteSpace(fill)) context.Add(new HavenFillRoundedRectCommand(rect, new HavenTokenBrush(fill), 0, opacity * .72));
        if (selected) context.Add(new HavenFillRoundedRectCommand(rect, new HavenSolidBrush(34, 86, 153, 255), 0, opacity));

        var border = CellMetadata(cell, DataCellFormatMetadata.Border);
        if (string.IsNullOrWhiteSpace(border)) border = active ? "Accent" : "Border";
        context.Add(new HavenStrokeRoundedRectCommand(rect, new HavenPen(new HavenTokenBrush(border), active ? 2 : 1), 0, opacity));

        var value = FormatValue(cell, rawValue);
        if (string.IsNullOrEmpty(value)) return;
        var family = CellMetadata(cell, DataCellFormatMetadata.FontFamily);
        if (string.IsNullOrWhiteSpace(family)) family = "Montserrat";
        if (CellMetadata(cell, DataCellFormatMetadata.Italic) == "true" && !family.Contains("Italic", StringComparison.OrdinalIgnoreCase)) family += " Italic";
        var size = double.TryParse(CellMetadata(cell, DataCellFormatMetadata.FontSize), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedSize) ? Math.Clamp(parsedSize, 7, 72) : 11;
        var weight = int.TryParse(CellMetadata(cell, DataCellFormatMetadata.FontWeight), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedWeight) ? Math.Clamp(parsedWeight, 100, 900) : 450;
        var foreground = CellMetadata(cell, DataCellFormatMetadata.Foreground);
        if (string.IsNullOrWhiteSpace(foreground)) foreground = "TextPrimary";

        var available = Math.Max(8, rect.Width - 14);
        var estimated = Math.Min(available, Math.Max(8, value.Length * size * .55));
        var alignment = CellMetadata(cell, DataCellFormatMetadata.HorizontalAlignment).ToLowerInvariant();
        var left = alignment switch { "center" => rect.X + (rect.Width - estimated) / 2, "right" => rect.Right - 7 - estimated, _ => rect.X + 7 };
        var textRect = new HavenRect(left, rect.Y + 8, alignment is "center" or "right" ? estimated : available, Math.Max(8, rect.Height - 8));
        context.Add(new HavenTextCommand(textRect, new HavenTextLayout(value, family, size, weight, Math.Max(8, textRect.Width)), new HavenTokenBrush(foreground), opacity));

        if (CellMetadata(cell, DataCellFormatMetadata.Underline) == "true")
        {
            var y = Math.Min(rect.Bottom - 5, rect.Y + 11 + size);
            context.Add(new HavenLineCommand(new HavenPoint(left, y), new HavenPoint(Math.Min(rect.Right - 7, left + estimated), y), new HavenPen(new HavenTokenBrush(foreground), 1), opacity));
        }
    }

    private static string FormatValue(DataCell? cell, string value)
    {
        if (cell is null || string.IsNullOrEmpty(value)) return value;
        var format = CellMetadata(cell, DataCellFormatMetadata.NumberFormat).Trim().ToLowerInvariant();
        if (format.Length == 0) return value;
        if (format == "date" && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date)) return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (!double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var number)) return value;
        return format switch
        {
            "0" or "integer" => number.ToString("0", CultureInfo.InvariantCulture),
            "0.00" or "decimal" => number.ToString("0.00", CultureInfo.InvariantCulture),
            "percent" or "0%" => number.ToString("0%", CultureInfo.InvariantCulture),
            "currency" or "gbp" => number.ToString("£0.00", CultureInfo.InvariantCulture),
            _ => value
        };
    }

    private static string CellMetadata(DataCell? cell, string key) => cell is not null && cell.Metadata.TryGetValue(key, out var value) ? value : string.Empty;
}
