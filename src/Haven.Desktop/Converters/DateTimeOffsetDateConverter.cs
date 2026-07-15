using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Haven.Desktop.Converters;

/// <summary>
/// Adapts Haven's offset-aware planner values to Avalonia's date-only picker,
/// whose SelectedDate contract is DateTime?. The conversion deliberately uses
/// the local calendar day and restores the current local UTC offset.
/// </summary>
public sealed class DateTimeOffsetDateConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        DateTimeOffset offset => offset.ToLocalTime().DateTime,
        DateTime dateTime => dateTime,
        _ => null
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
            return string.Equals(parameter as string, "Required", StringComparison.OrdinalIgnoreCase)
                ? BindingOperations.DoNothing
                : null;

        if (value is DateTimeOffset offset)
            return offset;

        if (value is not DateTime dateTime)
            return BindingOperations.DoNothing;

        var local = DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }
}
