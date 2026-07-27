/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/SqliteMapping.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns SqliteMapping. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

/// <summary>
/// Represents sqlite mapping and keeps its related state and behavior together.
/// </summary>
internal static class SqliteMapping
{
    /// <summary>
    /// Performs the guid step owned by this component.
    /// </summary>
    public static Guid Guid(this SqliteDataReader reader, string name) => System.Guid.Parse(reader.GetString(reader.GetOrdinal(name)));
    /// <summary>
    /// Performs the nullable guid step owned by this component.
    /// </summary>
    public static Guid? NullableGuid(this SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : System.Guid.Parse(reader.GetString(ordinal));
    }
    /// <summary>
    /// Performs the string step owned by this component.
    /// </summary>
    public static string String(this SqliteDataReader reader, string name) => reader.GetString(reader.GetOrdinal(name));
    /// <summary>
    /// Performs the nullable string step owned by this component.
    /// </summary>
    public static string? NullableString(this SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }
    /// <summary>
    /// Performs the int32 step owned by this component.
    /// </summary>
    public static int Int32(this SqliteDataReader reader, string name) => reader.GetInt32(reader.GetOrdinal(name));
    /// <summary>
    /// Performs the boolean step owned by this component.
    /// </summary>
    public static bool Boolean(this SqliteDataReader reader, string name) => reader.GetInt32(reader.GetOrdinal(name)) != 0;
    /// <summary>
    /// Performs the date time offset step owned by this component.
    /// </summary>
    public static DateTimeOffset DateTimeOffset(this SqliteDataReader reader, string name) => System.DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal(name)), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
    /// <summary>
    /// Performs the nullable date time offset step owned by this component.
    /// </summary>
    public static DateTimeOffset? NullableDateTimeOffset(this SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : System.DateTimeOffset.Parse(reader.GetString(ordinal), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
    }
}
