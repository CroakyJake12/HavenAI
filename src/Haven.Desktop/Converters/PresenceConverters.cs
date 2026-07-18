/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Converters/PresenceConverters.cs, in the Desktop composition layer, which starts and wires the Avalonia application.
 * What: This file owns NonEmptyStringToBoolConverter, PositiveCountToBoolConverter. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Globalization;
using Avalonia.Data.Converters;

namespace Haven.Desktop.Converters;

/// <summary>
/// Represents non empty string to bool converter and keeps its related state and behavior together.
/// </summary>
public sealed class NonEmptyStringToBoolConverter : IValueConverter
{
    /// <summary>
    /// Performs the convert step owned by this component.
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text && !string.IsNullOrWhiteSpace(text);

    /// <summary>
    /// Performs the convert back step owned by this component.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Represents positive count to bool converter and keeps its related state and behavior together.
/// </summary>
public sealed class PositiveCountToBoolConverter : IValueConverter
{
    /// <summary>
    /// Performs the convert step owned by this component.
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        int count => count > 0,
        long count => count > 0,
        System.Collections.ICollection collection => collection.Count > 0,
        _ => false
    };

    /// <summary>
    /// Performs the convert back step owned by this component.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
