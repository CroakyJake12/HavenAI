/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Converters/DateTimeOffsetDateConverter.cs, in the Desktop composition layer, which starts and wires the Avalonia application.
 * What: This file owns DateTimeOffsetDateConverter. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

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
    /// <summary>
    /// Performs the convert step owned by this component.
    /// </summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        DateTimeOffset offset => offset.ToLocalTime().DateTime,
        DateTime dateTime => dateTime,
        _ => null
    };

    /// <summary>
    /// Performs the convert back step owned by this component.
    /// </summary>
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
