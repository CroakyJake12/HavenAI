using System.Text;
using System.Text.RegularExpressions;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

internal static class LanguageServerTextEditApplicator
{
    public static string Apply(string original, IReadOnlyList<LanguageServerTextEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(edits);
        if (edits.Count == 0) return original;
        var lineStarts = BuildLineStarts(original);
        var resolved = edits.Select(edit => new ResolvedEdit(
                Offset(edit.Range.Start, original, lineStarts),
                Offset(edit.Range.End, original, lineStarts),
                edit.NewText))
            .OrderByDescending(edit => edit.Start)
            .ThenByDescending(edit => edit.End)
            .ToArray();
        var nextBoundary = original.Length;
        var builder = new StringBuilder(original);
        foreach (var edit in resolved)
        {
            if (edit.Start < 0 || edit.End < edit.Start || edit.End > original.Length)
                throw new InvalidOperationException("The language server returned an out-of-range text edit.");
            if (edit.End > nextBoundary)
                throw new InvalidOperationException("The language server returned overlapping text edits.");
            builder.Remove(edit.Start, edit.End - edit.Start);
            builder.Insert(edit.Start, edit.NewText);
            nextBoundary = edit.Start;
        }
        return builder.ToString();
    }

    private static int[] BuildLineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var index = 0; index < text.Length; index++)
            if (text[index] == '\n') starts.Add(index + 1);
        return starts.ToArray();
    }

    private static int Offset(CodePosition position, string text, int[] lineStarts)
    {
        if (position.Line < 0 || position.Line >= lineStarts.Length)
            throw new InvalidOperationException("The language server returned a line outside the document.");
        var lineStart = lineStarts[position.Line];
        var lineEnd = position.Line + 1 < lineStarts.Length ? lineStarts[position.Line + 1] - 1 : text.Length;
        if (lineEnd > lineStart && lineEnd <= text.Length && text[lineEnd - 1] == '\r') lineEnd--;
        if (position.Character < 0 || lineStart + position.Character > lineEnd)
            throw new InvalidOperationException("The language server returned a character outside the document line.");
        return lineStart + position.Character;
    }

    private sealed record ResolvedEdit(int Start, int End, string NewText);
}

internal static class UnifiedDiffBuilder
{
    public static string Build(string relativePath, string original, string updated)
    {
        if (string.Equals(original, updated, StringComparison.Ordinal)) return "No changes.";
        var before = original.ReplaceLineEndings("\n").Split('\n');
        var after = updated.ReplaceLineEndings("\n").Split('\n');
        var operations = Diff(before, after);
        var builder = new StringBuilder()
            .Append("--- a/").AppendLine(relativePath.Replace('\\', '/'))
            .Append("+++ b/").AppendLine(relativePath.Replace('\\', '/'));
        foreach (var operation in operations)
        {
            builder.Append(operation.Kind switch { DiffKind.Equal => ' ', DiffKind.Remove => '-', _ => '+' })
                .AppendLine(operation.Line);
        }
        return builder.ToString();
    }

    private static IReadOnlyList<DiffOperation> Diff(string[] before, string[] after)
    {
        if ((long)before.Length * after.Length > 4_000_000)
            return before.Select(line => new DiffOperation(DiffKind.Remove, line))
                .Concat(after.Select(line => new DiffOperation(DiffKind.Add, line)))
                .ToArray();
        var lengths = new int[before.Length + 1, after.Length + 1];
        for (var left = before.Length - 1; left >= 0; left--)
            for (var right = after.Length - 1; right >= 0; right--)
                lengths[left, right] = string.Equals(before[left], after[right], StringComparison.Ordinal)
                    ? lengths[left + 1, right + 1] + 1
                    : Math.Max(lengths[left + 1, right], lengths[left, right + 1]);
        var result = new List<DiffOperation>();
        var i = 0;
        var j = 0;
        while (i < before.Length && j < after.Length)
        {
            if (string.Equals(before[i], after[j], StringComparison.Ordinal))
            {
                result.Add(new DiffOperation(DiffKind.Equal, before[i++]));
                j++;
            }
            else if (lengths[i + 1, j] >= lengths[i, j + 1]) result.Add(new DiffOperation(DiffKind.Remove, before[i++]));
            else result.Add(new DiffOperation(DiffKind.Add, after[j++]));
        }
        while (i < before.Length) result.Add(new DiffOperation(DiffKind.Remove, before[i++]));
        while (j < after.Length) result.Add(new DiffOperation(DiffKind.Add, after[j++]));
        return result;
    }

    private enum DiffKind { Equal, Remove, Add }
    private sealed record DiffOperation(DiffKind Kind, string Line);
}

internal static class ExecutableLocator
{
    public static bool IsAvailable(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        var trimmed = command.Trim().Trim('"');
        if (Path.IsPathRooted(trimmed) || trimmed.Contains(Path.DirectorySeparatorChar) || trimmed.Contains(Path.AltDirectorySeparatorChar))
            return File.Exists(Path.GetFullPath(trimmed));
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [string.Empty];
        foreach (var directory in paths)
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(
                    directory.Trim('"'),
                    trimmed.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? trimmed : trimmed + extension);
                if (File.Exists(candidate)) return true;
            }
        }
        return false;
    }
}

internal static class LexicalSymbolSearch
{
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", ".haven", "bin", "obj", "node_modules", "packages", "artifacts", "dist", "build", ".next", ".nuxt", "coverage"
    };

    private static readonly IReadOnlyList<LexicalPattern> JavaScriptPatterns =
    [
        new("Symbol", new Regex("\\b(?:class|interface|type|enum|function)\\s+(?<name>[A-Za-z_$][A-Za-z0-9_$]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant)),
        new("Symbol", new Regex("\\b(?:const|let|var)\\s+(?<name>[A-Za-z_$][A-Za-z0-9_$]*)\\s*=", RegexOptions.Compiled | RegexOptions.CultureInvariant))
    ];

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<LexicalPattern>> Patterns =
        new Dictionary<string, IReadOnlyList<LexicalPattern>>(StringComparer.OrdinalIgnoreCase)
        {
            [".cs"] =
            [
                new("Type", new Regex("\\b(?:class|struct|interface|enum|record)\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant)),
                new("Method", new Regex("\\b(?:public|private|protected|internal|static|virtual|override|async|sealed|partial|new|extern|unsafe|readonly|abstract|\\s)+[A-Za-z_][A-Za-z0-9_<>,.?\\[\\] ]*\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s*\\(", RegexOptions.Compiled | RegexOptions.CultureInvariant)),
                new("Namespace", new Regex("\\bnamespace\\s+(?<name>[A-Za-z_][A-Za-z0-9_.]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant))
            ],
            [".fs"] = [new("Symbol", new Regex("^\\s*(?:type|module|namespace|let|member)\\s+(?<name>[A-Za-z_][A-Za-z0-9_'.]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline))],
            [".vb"] = [new("Symbol", new Regex("^\\s*(?:Public|Private|Friend|Protected|Shared|Partial|MustInherit|NotInheritable|Overridable|Overrides|Async|\\s)*(?:Class|Structure|Interface|Enum|Module|Sub|Function|Property)\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Multiline))],
            [".py"] = [new("Symbol", new Regex("^\\s*(?:async\\s+)?(?:def|class)\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline))],
            [".ts"] = JavaScriptPatterns,
            [".tsx"] = JavaScriptPatterns,
            [".js"] = JavaScriptPatterns,
            [".jsx"] = JavaScriptPatterns,
            [".rs"] = [new("Symbol", new Regex("^\\s*(?:pub(?:\\([^)]*\\))?\\s+)?(?:async\\s+)?(?:fn|struct|enum|trait|mod|type|const|static)\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline))],
            [".go"] = [new("Symbol", new Regex("^\\s*(?:func|type|var|const)\\s+(?:\\([^)]*\\)\\s*)?(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline))],
            [".java"] = [new("Symbol", new Regex("\\b(?:class|interface|enum|record|@interface)\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)|\\b(?:public|private|protected|static|final|abstract|synchronized|native|strictfp|\\s)+[A-Za-z_][A-Za-z0-9_<>,.?\\[\\] ]*\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s*\\(", RegexOptions.Compiled | RegexOptions.CultureInvariant))]
        };

    public static bool WorkspaceContainsExtension(string root, IReadOnlyList<string> extensions)
    {
        var allowed = extensions.Select(NormalizeExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return EnumerateFiles(root, 300, CancellationToken.None).Any(path => allowed.Contains(Path.GetExtension(path)));
    }

    public static IReadOnlyList<CodeSymbol> Search(string root, string query, CancellationToken cancellationToken)
    {
        var result = new List<CodeSymbol>();
        foreach (var path in EnumerateFiles(root, 2_000, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > 2 * 1024 * 1024) continue;
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (!Patterns.TryGetValue(extension, out var patterns)) continue;
            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException) { continue; }
            var lineStarts = BuildLineStarts(text);
            foreach (var pattern in patterns)
            {
                foreach (Match match in pattern.Pattern.Matches(text))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var name = match.Groups["name"].Value;
                    if (name.Length == 0 || !name.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                    var position = PositionAt(match.Groups["name"].Index, lineStarts);
                    result.Add(new CodeSymbol(
                        name,
                        pattern.Kind,
                        Path.GetRelativePath(root, path),
                        new CodeRange(position, new CodePosition(position.Line, position.Character + name.Length)),
                        null,
                        "Haven lexical fallback"));
                    if (result.Count >= 1_000) return result;
                }
            }
        }
        return result;
    }

    public static IEnumerable<string> EnumerateFiles(string root, int maximum, CancellationToken cancellationToken)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        var count = 0;
        while (stack.Count > 0 && count < maximum)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = stack.Pop();
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(directory); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { continue; }
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileAttributes attributes;
                try { attributes = File.GetAttributes(entry); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { continue; }
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (!ExcludedDirectories.Contains(Path.GetFileName(entry))) stack.Push(entry);
                    continue;
                }
                yield return entry;
                count++;
                if (count >= maximum) yield break;
            }
        }
    }

    private static int[] BuildLineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var index = 0; index < text.Length; index++) if (text[index] == '\n') starts.Add(index + 1);
        return starts.ToArray();
    }

    private static CodePosition PositionAt(int offset, int[] lineStarts)
    {
        var index = Array.BinarySearch(lineStarts, offset);
        var line = index >= 0 ? index : ~index - 1;
        line = Math.Max(0, line);
        return new CodePosition(line, offset - lineStarts[line]);
    }

    private static string NormalizeExtension(string value) => value.StartsWith('.') ? value : "." + value;
    private sealed record LexicalPattern(string Kind, Regex Pattern);
}
