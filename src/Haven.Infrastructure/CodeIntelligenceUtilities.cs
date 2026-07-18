/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/CodeIntelligenceUtilities.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns LanguageServerTextEditApplicator, ResolvedEdit, UnifiedDiffBuilder, DiffKind, DiffOperation, ExecutableLocator, LexicalSymbolSearch, LexicalPattern. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text;
using System.Text.RegularExpressions;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Represents language server text edit applicator and keeps its related state and behavior together.
/// </summary>
internal static class LanguageServerTextEditApplicator
{
    /// <summary>
    /// Performs the apply step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Builds line starts from the currently available inputs.
    /// </summary>
    private static int[] BuildLineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var index = 0; index < text.Length; index++)
            if (text[index] == '\n') starts.Add(index + 1);
        return starts.ToArray();
    }

    /// <summary>
    /// Performs the offset step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Represents resolved edit and keeps its related state and behavior together.
    /// </summary>
    private sealed record ResolvedEdit(int Start, int End, string NewText);
}

/// <summary>
/// Represents unified diff builder and keeps its related state and behavior together.
/// </summary>
internal static class UnifiedDiffBuilder
{
    /// <summary>
    /// Builds this member from the currently available inputs.
    /// </summary>
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

    /// <summary>
    /// Performs the diff step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Lists the supported diff kind values used to make state explicit and type-safe.
    /// </summary>
    private enum DiffKind { Equal, Remove, Add }
    /// <summary>
    /// Represents diff operation and keeps its related state and behavior together.
    /// </summary>
    private sealed record DiffOperation(DiffKind Kind, string Line);
}

/// <summary>
/// Represents executable locator and keeps its related state and behavior together.
/// </summary>
internal static class ExecutableLocator
{
    /// <summary>
    /// Reports whether is available is true for the current state.
    /// </summary>
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

/// <summary>
/// Represents lexical symbol search and keeps its related state and behavior together.
/// </summary>
internal static class LexicalSymbolSearch
{
    /// <summary>
    /// Stores excluded directories locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", ".haven", "bin", "obj", "node_modules", "packages", "artifacts", "dist", "build", ".next", ".nuxt", "coverage"
    };

    /// <summary>
    /// Stores java script patterns locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly IReadOnlyList<LexicalPattern> JavaScriptPatterns =
    [
        new("Symbol", new Regex("\\b(?:class|interface|type|enum|function)\\s+(?<name>[A-Za-z_$][A-Za-z0-9_$]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant)),
        new("Symbol", new Regex("\\b(?:const|let|var)\\s+(?<name>[A-Za-z_$][A-Za-z0-9_$]*)\\s*=", RegexOptions.Compiled | RegexOptions.CultureInvariant))
    ];

    /// <summary>
    /// Stores patterns locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
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

    /// <summary>
    /// Performs the workspace contains extension step owned by this component.
    /// </summary>
    public static bool WorkspaceContainsExtension(string root, IReadOnlyList<string> extensions)
    {
        var allowed = extensions.Select(NormalizeExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return EnumerateFiles(root, 300, CancellationToken.None).Any(path => allowed.Contains(Path.GetExtension(path)));
    }

    /// <summary>
    /// Performs the search step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the enumerate files step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Builds line starts from the currently available inputs.
    /// </summary>
    private static int[] BuildLineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var index = 0; index < text.Length; index++) if (text[index] == '\n') starts.Add(index + 1);
        return starts.ToArray();
    }

    /// <summary>
    /// Performs the position at step owned by this component.
    /// </summary>
    private static CodePosition PositionAt(int offset, int[] lineStarts)
    {
        var index = Array.BinarySearch(lineStarts, offset);
        var line = index >= 0 ? index : ~index - 1;
        line = Math.Max(0, line);
        return new CodePosition(line, offset - lineStarts[line]);
    }

    /// <summary>
    /// Performs the normalize extension step owned by this component.
    /// </summary>
    private static string NormalizeExtension(string value) => value.StartsWith('.') ? value : "." + value;
    /// <summary>
    /// Represents lexical pattern and keeps its related state and behavior together.
    /// </summary>
    private sealed record LexicalPattern(string Kind, Regex Pattern);
}
