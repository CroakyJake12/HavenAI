using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class CodeIntelligenceService(
    IWorkspaceToolService workspaceTools,
    IWorkspaceTransactionService transactions,
    ILanguageServerConfigurationStore configurations,
    ILanguageServerClientFactory languageServers) : ICodeIntelligenceService
{
    private static readonly Regex BuildDiagnosticPattern = new(
        "^(?<file>.+?)\\((?<line>\\d+),(?<column>\\d+)(?:,(?<endLine>\\d+),(?<endColumn>\\d+))?\\):\\s*(?<severity>error|warning)\\s+(?<code>[^: ]+):\\s*(?<message>.*?)(?:\\s+\\[[^]]+\\])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public async Task<CodeIntelligenceStatus> GetStatusAsync(
        string workspaceRoot,
        string relativePath,
        CancellationToken cancellationToken)
    {
        var root = ResolveRoot(workspaceRoot);
        var path = workspaceTools.ResolveWorkspacePath(root, relativePath);
        var definition = await configurations.FindForPathAsync(path, cancellationToken).ConfigureAwait(false);
        LanguageServerHealth? health = null;
        if (definition is not null)
        {
            var available = ExecutableLocator.IsAvailable(definition.Command);
            health = new LanguageServerHealth(
                definition.Id,
                true,
                available,
                available ? $"{definition.DisplayName} is configured and its command is available." : $"{definition.DisplayName} is enabled, but '{definition.Command}' is not available on PATH.",
                DateTimeOffset.UtcNow);
        }
        var buildTarget = FindDotNetBuildTarget(root);
        var cliFallback = buildTarget is not null && ExecutableLocator.IsAvailable("dotnet");
        var languageId = definition?.LanguageId ?? InferLanguageId(path);
        var message = health?.IsExecutableAvailable == true
            ? "Language-server diagnostics, workspace symbols, and formatting are available."
            : cliFallback
                ? "No usable language server is configured for this file. .NET build diagnostics and lexical symbol search remain available."
                : "No usable language server or .NET diagnostics fallback is available. Lexical symbol search remains available.";
        return new CodeIntelligenceStatus(
            Path.GetRelativePath(root, path),
            languageId,
            health,
            cliFallback,
            health?.IsExecutableAvailable == true,
            message);
    }

    public async Task<IReadOnlyList<CodeDiagnostic>> GetDiagnosticsAsync(
        string workspaceRoot,
        string relativePath,
        CancellationToken cancellationToken)
    {
        var root = ResolveRoot(workspaceRoot);
        var path = workspaceTools.ResolveWorkspacePath(root, relativePath);
        if (!File.Exists(path)) throw new FileNotFoundException("The requested source file does not exist.", path);
        var definition = await configurations.FindForPathAsync(path, cancellationToken).ConfigureAwait(false);
        Exception? serverFailure = null;
        if (definition is not null && ExecutableLocator.IsAvailable(definition.Command))
        {
            try
            {
                var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                await using var server = await languageServers.StartAsync(definition, root, cancellationToken).ConfigureAwait(false);
                var uri = StdioLanguageServerClient.ToDocumentUri(path);
                await server.OpenDocumentAsync(uri, definition.LanguageId, text, 1, cancellationToken).ConfigureAwait(false);
                var diagnostics = await server.GetDiagnosticsAsync(
                    uri,
                    root,
                    TimeSpan.FromSeconds(definition.RequestTimeoutSeconds),
                    cancellationToken).ConfigureAwait(false);
                await server.ShutdownAsync(cancellationToken).ConfigureAwait(false);
                return diagnostics
                    .OrderBy(item => item.Severity)
                    .ThenBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.Range.Start.Line)
                    .ThenBy(item => item.Range.Start.Character)
                    .ToArray();
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or LanguageServerRequestException or TimeoutException or System.ComponentModel.Win32Exception)
            {
                serverFailure = exception;
            }
        }

        var target = FindDotNetBuildTarget(root);
        if (target is not null && ExecutableLocator.IsAvailable("dotnet"))
        {
            var result = await workspaceTools.RunProcessAsync(new ProcessRequest(
                "dotnet",
                $"build \"{target}\" --no-restore -nologo -v:minimal",
                root,
                TimeSpan.FromMinutes(5)), cancellationToken).ConfigureAwait(false);
            var diagnostics = ParseBuildDiagnostics(result.StandardOutput + Environment.NewLine + result.StandardError, root);
            if (diagnostics.Count > 0) return diagnostics;
            if (result.ExitCode == 0) return [];
            var detail = (result.StandardError + Environment.NewLine + result.StandardOutput).Trim();
            if (detail.Length > 4_000) detail = detail[..4_000] + "…";
            throw new InvalidOperationException("The .NET diagnostics fallback failed without parseable compiler diagnostics. " + detail, serverFailure);
        }

        if (serverFailure is not null)
            throw new InvalidOperationException("The configured language server failed and no .NET diagnostics fallback was available.", serverFailure);
        throw new InvalidOperationException("No enabled language server or .NET diagnostics fallback is available for this workspace.");
    }

    public async Task<IReadOnlyList<CodeSymbol>> SearchSymbolsAsync(
        string workspaceRoot,
        string query,
        CancellationToken cancellationToken)
    {
        var root = ResolveRoot(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var normalizedQuery = query.Trim();
        var symbols = new List<CodeSymbol>();
        var definitions = await configurations.GetAllAsync(cancellationToken).ConfigureAwait(false);
        foreach (var definition in definitions.Where(item => item.IsEnabled && ExecutableLocator.IsAvailable(item.Command)).Take(4))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!WorkspaceContainsExtension(root, definition.Extensions)) continue;
            try
            {
                await using var server = await languageServers.StartAsync(definition, root, cancellationToken).ConfigureAwait(false);
                symbols.AddRange(await server.SearchWorkspaceSymbolsAsync(normalizedQuery, root, cancellationToken).ConfigureAwait(false));
                await server.ShutdownAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or LanguageServerRequestException or TimeoutException or System.ComponentModel.Win32Exception)
            {
                System.Diagnostics.Debug.WriteLine($"Workspace symbol search failed for {definition.Id}: {exception.Message}");
            }
        }

        var known = symbols.Select(SymbolKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var fallback in await Task.Run(() => SearchLexically(root, normalizedQuery, cancellationToken), cancellationToken).ConfigureAwait(false))
            if (known.Add(SymbolKey(fallback))) symbols.Add(fallback);
        return symbols
            .OrderByDescending(item => item.Name.Equals(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(item => item.Name.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Take(500)
            .ToArray();
    }

    public async Task<CodeFormatPreview> PreviewFormatAsync(
        string workspaceRoot,
        string relativePath,
        int tabSize,
        bool insertSpaces,
        CancellationToken cancellationToken)
    {
        var root = ResolveRoot(workspaceRoot);
        var path = workspaceTools.ResolveWorkspacePath(root, relativePath);
        if (!File.Exists(path)) throw new FileNotFoundException("The requested source file does not exist.", path);
        var definition = await configurations.FindForPathAsync(path, cancellationToken).ConfigureAwait(false)
                         ?? throw new InvalidOperationException("No enabled language server is configured for this file type. Formatting is unavailable rather than silently using an unrelated formatter.");
        if (!ExecutableLocator.IsAvailable(definition.Command))
            throw new InvalidOperationException($"{definition.DisplayName} is configured, but '{definition.Command}' is unavailable.");

        var original = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        await using var server = await languageServers.StartAsync(definition, root, cancellationToken).ConfigureAwait(false);
        var uri = StdioLanguageServerClient.ToDocumentUri(path);
        await server.OpenDocumentAsync(uri, definition.LanguageId, original, 1, cancellationToken).ConfigureAwait(false);
        var edits = await server.FormatDocumentAsync(uri, tabSize, insertSpaces, cancellationToken).ConfigureAwait(false);
        await server.ShutdownAsync(cancellationToken).ConfigureAwait(false);
        var formatted = LanguageServerTextEditApplicator.Apply(original, edits);
        var relative = Path.GetRelativePath(root, path);
        return new CodeFormatPreview(
            Guid.NewGuid(),
            relative,
            ContentHash(original),
            original,
            formatted,
            UnifiedDiffBuilder.Build(relative, original, formatted),
            definition.DisplayName,
            DateTimeOffset.UtcNow);
    }

    public async Task<CodeFormatApplyResult> ApplyFormatAsync(
        string workspaceRoot,
        CodeFormatPreview preview,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var root = ResolveRoot(workspaceRoot);
        var path = workspaceTools.ResolveWorkspacePath(root, preview.RelativePath);
        if (!File.Exists(path)) throw new FileNotFoundException("The formatted file no longer exists.", path);
        var current = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        if (!ContentHash(current).Equals(preview.OriginalContentHash, StringComparison.Ordinal))
            throw new InvalidOperationException("The file changed after the formatting preview was created. Refresh the preview before applying it.");
        if (!preview.HasChanges)
            return new CodeFormatApplyResult(preview.PreviewId, null, preview.RelativePath, false, "The formatter proposed no changes.");
        var result = await transactions.ApplyAsync(
            root,
            [new WorkspaceFileMutation(preview.RelativePath, preview.FormattedContent)],
            cancellationToken).ConfigureAwait(false);
        return new CodeFormatApplyResult(preview.PreviewId, result.TransactionId, preview.RelativePath, true, $"Applied formatting through workspace transaction {result.TransactionId:N}.");
    }

    internal static IReadOnlyList<CodeDiagnostic> ParseBuildDiagnostics(string output, string workspaceRoot)
    {
        var root = Path.GetFullPath(workspaceRoot);
        var result = new List<CodeDiagnostic>();
        foreach (var line in output.ReplaceLineEndings("\n").Split('\n'))
        {
            var match = BuildDiagnosticPattern.Match(line.Trim());
            if (!match.Success) continue;
            var rawPath = match.Groups["file"].Value.Trim();
            var fullPath = Path.IsPathRooted(rawPath) ? Path.GetFullPath(rawPath) : Path.GetFullPath(Path.Combine(root, rawPath));
            var relative = IsWithinRoot(root, fullPath) ? Path.GetRelativePath(root, fullPath) : rawPath;
            var lineNumber = Math.Max(0, int.Parse(match.Groups["line"].Value, System.Globalization.CultureInfo.InvariantCulture) - 1);
            var column = Math.Max(0, int.Parse(match.Groups["column"].Value, System.Globalization.CultureInfo.InvariantCulture) - 1);
            var endLine = match.Groups["endLine"].Success
                ? Math.Max(lineNumber, int.Parse(match.Groups["endLine"].Value, System.Globalization.CultureInfo.InvariantCulture) - 1)
                : lineNumber;
            var endColumn = match.Groups["endColumn"].Success
                ? Math.Max(0, int.Parse(match.Groups["endColumn"].Value, System.Globalization.CultureInfo.InvariantCulture) - 1)
                : column + 1;
            result.Add(new CodeDiagnostic(
                relative,
                new CodeRange(new CodePosition(lineNumber, column), new CodePosition(endLine, endColumn)),
                match.Groups["severity"].Value.Equals("error", StringComparison.OrdinalIgnoreCase) ? CodeDiagnosticSeverity.Error : CodeDiagnosticSeverity.Warning,
                match.Groups["code"].Value,
                "dotnet build",
                match.Groups["message"].Value.Trim()));
        }
        return result;
    }

    private static string ResolveRoot(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var root = Path.GetFullPath(workspaceRoot.Trim());
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        return root;
    }

    private static string? FindDotNetBuildTarget(string root)
    {
        var solution = Directory.EnumerateFiles(root, "*.sln", SearchOption.TopDirectoryOnly).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        if (solution is not null) return solution;
        var project = Directory.EnumerateFiles(root, "*.*proj", SearchOption.TopDirectoryOnly).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        if (project is not null) return project;
        return EnumerateSourceFiles(root, 400)
            .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path.Count(character => character is '/' or '\\'))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static bool WorkspaceContainsExtension(string root, IReadOnlyList<string> extensions)
    {
        var allowed = extensions.Select(NormalizeExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return EnumerateSourceFiles(root, 300).Any(path => allowed.Contains(Path.GetExtension(path)));
    }

    private static IReadOnlyList<CodeSymbol> SearchLexically(string root, string query, CancellationToken cancellationToken)
    {
        var result = new List<CodeSymbol>();
        foreach (var path in EnumerateSourceFiles(root, 2_000))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > 2 * 1024 * 1024) continue;
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (!LexicalPatterns.TryGetValue(extension, out var patterns)) continue;
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
                    if (!name.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                    var position = PositionAt(match.Groups["name"].Index, lineStarts);
                    result.Add(new CodeSymbol(
                        name,
                        match.Kind,
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

    private static IEnumerable<string> EnumerateSourceFiles(string root, int maximum)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        var count = 0;
        while (stack.Count > 0 && count < maximum)
        {
            var directory = stack.Pop();
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(directory); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { continue; }
            foreach (var entry in entries)
            {
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
        return new CodePosition(Math.Max(0, line), offset - lineStarts[Math.Max(0, line)]);
    }

    private static string SymbolKey(CodeSymbol symbol) => $"{symbol.RelativePath}\n{symbol.Range.Start.Line}\n{symbol.Range.Start.Character}\n{symbol.Name}";
    private static string NormalizeExtension(string value) => value.StartsWith('.') ? value : "." + value;
    private static string InferLanguageId(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".cs" => "csharp", ".fs" => "fsharp", ".vb" => "vb", ".ts" => "typescript", ".tsx" => "typescriptreact",
        ".js" => "javascript", ".jsx" => "javascriptreact", ".py" => "python", ".rs" => "rust", ".go" => "go",
        ".java" => "java", ".cpp" or ".cc" or ".cxx" => "cpp", ".c" or ".h" => "c", _ => "plaintext"
    };

    private static string ContentHash(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private static bool IsWithinRoot(string root, string path)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedPath = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return normalizedPath.Equals(normalizedRoot, comparison) || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
    }

    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", ".haven", "bin", "obj", "node_modules", "packages", "artifacts", "dist", "build", ".next", ".nuxt", "coverage"
    };

    private sealed record LexicalPattern(string Kind, Regex Pattern);

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<LexicalPattern>> LexicalPatterns =
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

    private static readonly IReadOnlyList<LexicalPattern> JavaScriptPatterns =
    [
        new("Symbol", new Regex("\\b(?:class|interface|type|enum|function)\\s+(?<name>[A-Za-z_$][A-Za-z0-9_$]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant)),
        new("Symbol", new Regex("\\b(?:const|let|var)\\s+(?<name>[A-Za-z_$][A-Za-z0-9_$]*)\\s*=", RegexOptions.Compiled | RegexOptions.CultureInvariant))
    ];
}

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
            if (edit.Start < 0 || edit.End < edit.Start || edit.End > original.Length) throw new InvalidOperationException("The language server returned an out-of-range text edit.");
            if (edit.End > nextBoundary) throw new InvalidOperationException("The language server returned overlapping text edits.");
            builder.Remove(edit.Start, edit.End - edit.Start);
            builder.Insert(edit.Start, edit.NewText);
            nextBoundary = edit.Start;
        }
        return builder.ToString();
    }

    private static int[] BuildLineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var index = 0; index < text.Length; index++) if (text[index] == '\n') starts.Add(index + 1);
        return starts.ToArray();
    }

    private static int Offset(CodePosition position, string text, int[] lineStarts)
    {
        if (position.Line < 0 || position.Line >= lineStarts.Length) throw new InvalidOperationException("The language server returned a line outside the document.");
        var lineStart = lineStarts[position.Line];
        var lineEnd = position.Line + 1 < lineStarts.Length ? lineStarts[position.Line + 1] - 1 : text.Length;
        if (position.Character < 0 || lineStart + position.Character > lineEnd) throw new InvalidOperationException("The language server returned a character outside the document line.");
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
                .Concat(after.Select(line => new DiffOperation(DiffKind.Add, line))).ToArray();
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
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [string.Empty];
        foreach (var directory in paths)
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory.Trim('"'), trimmed.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? trimmed : trimmed + extension);
                if (File.Exists(candidate)) return true;
            }
        }
        return false;
    }
}
