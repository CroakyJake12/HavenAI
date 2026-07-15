using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

internal static class SqliteMapping
{
    public static Guid Guid(this SqliteDataReader reader, string name) => System.Guid.Parse(reader.GetString(reader.GetOrdinal(name)));
    public static Guid? NullableGuid(this SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : System.Guid.Parse(reader.GetString(ordinal));
    }
    public static string String(this SqliteDataReader reader, string name) => reader.GetString(reader.GetOrdinal(name));
    public static string? NullableString(this SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }
    public static int Int32(this SqliteDataReader reader, string name) => reader.GetInt32(reader.GetOrdinal(name));
    public static bool Boolean(this SqliteDataReader reader, string name) => reader.GetInt32(reader.GetOrdinal(name)) != 0;
    public static DateTimeOffset DateTimeOffset(this SqliteDataReader reader, string name) => System.DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal(name)), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
    public static DateTimeOffset? NullableDateTimeOffset(this SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : System.DateTimeOffset.Parse(reader.GetString(ordinal), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
    }
}
