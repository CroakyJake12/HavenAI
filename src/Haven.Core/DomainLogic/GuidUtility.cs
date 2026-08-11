namespace Haven.Core;

/// <summary>Creates deterministic GUIDs for stable built-in catalogue identities.</summary>
public static class GuidUtility
{
    public static Guid FromStableName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
