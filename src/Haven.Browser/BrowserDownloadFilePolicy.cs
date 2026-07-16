using System.Globalization;
using System.Text;

namespace Haven.Browser;

public static class BrowserDownloadFilePolicy
{
    public const int MaximumFileNameLength = 180;
    public static readonly TimeSpan PartialFileRetention = TimeSpan.FromHours(24);
    private const string PartialMarker = ".haven-download-";

    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string? SanitizeFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var candidate = value.Normalize(NormalizationForm.FormKC).Trim().Trim('"', '\'');
        candidate = Path.GetFileName(candidate.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(candidate)) return null;

        var builder = new StringBuilder(candidate.Length);
        foreach (var rune in candidate.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate)
                continue;
            if (rune.Value is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|')
            {
                builder.Append('_');
                continue;
            }
            builder.Append(rune.ToString());
        }

        var name = builder.ToString().Trim().TrimEnd('.', ' ');
        if (name is "" or "." or "..") return null;

        var firstSegment = name.Split('.', 2)[0];
        if (ReservedWindowsNames.Contains(firstSegment)) name = "_" + name;
        name = TruncatePreservingExtension(name, MaximumFileNameLength).Trim().TrimEnd('.', ' ');
        return name is "" or "." or ".." ? null : name;
    }

    public static string AllocateUniquePath(string directory, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var root = Path.GetFullPath(directory);
        var safeName = SanitizeFileName(fileName) ?? throw new InvalidDataException("The download file name is invalid.");
        var candidate = EnsureConfined(root, Path.Combine(root, safeName));
        if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;

        var stem = Path.GetFileNameWithoutExtension(safeName);
        var extension = Path.GetExtension(safeName);
        for (var index = 2; index < 10_000; index++)
        {
            var suffix = $" ({index})";
            var allowedStemLength = Math.Max(1, MaximumFileNameLength - extension.Length - suffix.Length);
            var collisionName = TruncateRunes(stem, allowedStemLength) + suffix + extension;
            candidate = EnsureConfined(root, Path.Combine(root, collisionName));
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
        throw new IOException("Could not allocate a unique download file name.");
    }

    public static string CreatePartialPath(string destination) =>
        destination + PartialMarker + Guid.NewGuid().ToString("N") + ".tmp";

    public static int CleanupStalePartialFiles(string directory, DateTimeOffset now)
    {
        if (!Directory.Exists(directory)) return 0;
        var root = Path.GetFullPath(directory);
        var removed = 0;
        foreach (var path in Directory.EnumerateFiles(root, "*.tmp", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(path);
            if (!name.Contains(PartialMarker, StringComparison.Ordinal)) continue;
            try
            {
                var modified = File.GetLastWriteTimeUtc(path);
                if (now.UtcDateTime - modified < PartialFileRetention) continue;
                File.Delete(EnsureConfined(root, path));
                removed++;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return removed;
    }

    private static string TruncatePreservingExtension(string name, int maximumLength)
    {
        if (name.Length <= maximumLength) return name;
        var extension = Path.GetExtension(name);
        if (extension.Length > 24) extension = extension[^24..];
        var stem = Path.GetFileNameWithoutExtension(name);
        return TruncateRunes(stem, Math.Max(1, maximumLength - extension.Length)) + extension;
    }

    private static string TruncateRunes(string value, int maximumUtf16Length)
    {
        if (value.Length <= maximumUtf16Length) return value;
        var builder = new StringBuilder(maximumUtf16Length);
        foreach (var rune in value.EnumerateRunes())
        {
            if (builder.Length + rune.Utf16SequenceLength > maximumUtf16Length) break;
            builder.Append(rune.ToString());
        }
        return builder.ToString();
    }

    private static string EnsureConfined(string root, string candidate)
    {
        var full = Path.GetFullPath(candidate);
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The download destination escaped Haven's download directory.");
        return full;
    }
}
