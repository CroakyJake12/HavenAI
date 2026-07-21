/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/DeveloperTools/AxamlSourceLocator.cs in the Desktop composition layer.
 * What: Maps selected runtime controls to authored AXAML and opens the best available local editor.
 * How: Exact x:Name matches take priority; unnamed controls resolve only when their type is unique.
 * Why: Source navigation must be useful without guessing or sending project data outside the machine.
 */

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;

namespace Haven.Desktop.DeveloperTools;

internal sealed record AxamlSourceLocation(string FilePath, int Line, string Snippet, bool IsExact);

/// <summary>
/// Locates an authored AXAML element from its runtime name or, as a fallback, a unique control type.
/// </summary>
internal static class AxamlSourceLocator
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    public static AxamlSourceLocation? Locate(Visual visual)
    {
        var projectRoot = FindDesktopProjectRoot(AppContext.BaseDirectory);
        if (projectRoot is null) return null;
        var name = (visual as StyledElement)?.Name;
        return Locate(projectRoot, name, visual.GetType().Name);
    }

    internal static AxamlSourceLocation? Locate(string projectRoot, string? name, string typeName)
    {
        if (!Directory.Exists(projectRoot)) return null;
        var files = Directory.EnumerateFiles(projectRoot, "*.axaml", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(name))
        {
            var namePattern = new Regex(
                $"(?:x:Name|Name)\\s*=\\s*[\"']{Regex.Escape(name)}[\"']",
                RegexOptions.CultureInvariant,
                RegexTimeout);
            AxamlSourceLocation? exact = null;
            foreach (var file in files)
            {
                foreach (var match in FindAll(file, namePattern, isExact: true))
                {
                    if (exact is not null) return null;
                    exact = match;
                }
            }
            if (exact is not null) return exact;
        }

        if (string.IsNullOrWhiteSpace(typeName)) return null;
        var typePattern = new Regex(
            $"<(?:(?:[A-Za-z_][\\w.-]*):)?{Regex.Escape(typeName)}(?=[\\s>/])",
            RegexOptions.CultureInvariant,
            RegexTimeout);
        AxamlSourceLocation? unique = null;
        foreach (var file in files)
        {
            var matches = FindAll(file, typePattern, isExact: false);
            foreach (var match in matches)
            {
                if (unique is not null) return null;
                unique = match;
            }
        }
        return unique;
    }

    internal static string? FindDesktopProjectRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Haven.Desktop.csproj")))
                return current.FullName;

            var nested = Path.Combine(current.FullName, "src", "Haven.Desktop");
            if (File.Exists(Path.Combine(nested, "Haven.Desktop.csproj")))
                return nested;

            current = current.Parent;
        }
        return null;
    }

    private static IEnumerable<AxamlSourceLocation> FindAll(string file, Regex pattern, bool isExact)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(file);
        }
        catch (IOException)
        {
            yield break;
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        for (var index = 0; index < lines.Length; index++)
        {
            bool isMatch;
            try
            {
                isMatch = pattern.IsMatch(lines[index]);
            }
            catch (RegexMatchTimeoutException)
            {
                yield break;
            }
            if (!isMatch) continue;
            yield return new AxamlSourceLocation(file, index + 1, BuildSnippet(lines, index), isExact);
        }
    }

    private static string BuildSnippet(IReadOnlyList<string> lines, int matchIndex)
    {
        var first = Math.Max(0, matchIndex - 4);
        var last = Math.Min(lines.Count - 1, matchIndex + 5);
        var width = (last + 1).ToString(CultureInfo.InvariantCulture).Length;
        var builder = new StringBuilder();
        for (var index = first; index <= last; index++)
        {
            var marker = index == matchIndex ? ">" : " ";
            builder.Append(marker)
                .Append(' ')
                .Append((index + 1).ToString(CultureInfo.InvariantCulture).PadLeft(width))
                .Append("  ")
                .AppendLine(lines[index]);
        }
        return builder.ToString();
    }

    private static bool IsBuildOutput(string path)
    {
        var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var separator = Path.DirectorySeparatorChar;
        return normalized.Contains($"{separator}bin{separator}", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains($"{separator}obj{separator}", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Opens AXAML source in the best available local editor, preserving line information where supported.
/// </summary>
internal static class SourceFileLauncher
{
    public static bool TryOpen(AxamlSourceLocation location, out string message)
    {
        try
        {
            var code = FindOnPath(OperatingSystem.IsWindows() ? "code.cmd" : "code");
            if (code is not null)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = code,
                    Arguments = $"-g \"{location.FilePath}:{location.Line}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                message = $"Opened {Path.GetFileName(location.FilePath)} at line {location.Line}.";
                return true;
            }

            var rider = FindOnPath(OperatingSystem.IsWindows() ? "rider64.exe" : "rider");
            if (rider is not null)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = rider,
                    Arguments = $"--line {location.Line} \"{location.FilePath}\"",
                    UseShellExecute = false
                });
                message = $"Opened {Path.GetFileName(location.FilePath)} at line {location.Line}.";
                return true;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = location.FilePath,
                UseShellExecute = true
            });
            message = $"Opened {Path.GetFileName(location.FilePath)}. Go to line {location.Line}.";
            return true;
        }
        catch (Exception exception)
        {
            message = $"Could not open source: {exception.Message}";
            return false;
        }
    }

    private static string? FindOnPath(string executable)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path)) return null;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory, executable);
                if (File.Exists(candidate)) return candidate;
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                // Ignore malformed PATH entries and continue to the next editor location.
            }
        }
        return null;
    }
}
