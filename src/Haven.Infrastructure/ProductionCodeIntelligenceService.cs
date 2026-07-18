/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/ProductionCodeIntelligenceService.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns ProductionCodeIntelligenceService. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Represents production code intelligence service and keeps its related state and behavior together.
/// </summary>
public sealed class ProductionCodeIntelligenceService(
    IWorkspaceToolService workspaceTools,
    IWorkspaceTransactionService transactions,
    ILanguageServerConfigurationStore configurations,
    ILanguageServerClientFactory languageServers) : ICodeIntelligenceService
{
    /// <summary>
    /// Builds diagnostic pattern from the currently available inputs.
    /// </summary>
    private static readonly Regex BuildDiagnosticPattern = new(
        "^(?<file>.+?)\\((?<line>\\d+),(?<column>\\d+)(?:,(?<endLine>\\d+),(?<endColumn>\\d+))?\\):\\s*(?<severity>error|warning)\\s+(?<code>[^: ]+):\\s*(?<message>.*?)(?:\\s+\\[[^]]+\\])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// Retrieves status async for the current operation.
    /// </summary>
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
                available
                    ? $"{definition.DisplayName} is configured and its command is available."
                    : $"{definition.DisplayName} is enabled, but '{definition.Command}' is not available on PATH.",
                DateTimeOffset.UtcNow);
        }
        var cliFallback = FindDotNetBuildTarget(root) is not null && ExecutableLocator.IsAvailable("dotnet");
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

    /// <summary>
    /// Retrieves diagnostics async for the current operation.
    /// </summary>
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
            throw new InvalidOperationException(
                "The .NET diagnostics fallback failed without parseable compiler diagnostics. " + detail,
                serverFailure);
        }

        if (serverFailure is not null)
            throw new InvalidOperationException("The configured language server failed and no .NET diagnostics fallback was available.", serverFailure);
        throw new InvalidOperationException("No enabled language server or .NET diagnostics fallback is available for this workspace.");
    }

    /// <summary>
    /// Performs search symbols async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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
            if (!LexicalSymbolSearch.WorkspaceContainsExtension(root, definition.Extensions)) continue;
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
        foreach (var fallback in await Task.Run(
                     () => LexicalSymbolSearch.Search(root, normalizedQuery, cancellationToken),
                     cancellationToken).ConfigureAwait(false))
            if (known.Add(SymbolKey(fallback))) symbols.Add(fallback);
        return symbols
            .OrderByDescending(item => item.Name.Equals(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(item => item.Name.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Take(500)
            .ToArray();
    }

    /// <summary>
    /// Performs preview format async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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
        try
        {
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
        catch (Exception exception) when (exception is IOException or InvalidOperationException or LanguageServerRequestException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException($"{definition.DisplayName} could not produce a formatting preview.", exception);
        }
    }

    /// <summary>
    /// Performs apply format async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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
        return new CodeFormatApplyResult(
            preview.PreviewId,
            result.TransactionId,
            preview.RelativePath,
            true,
            $"Applied formatting through workspace transaction {result.TransactionId:N}.");
    }

    /// <summary>
    /// Performs the parse build diagnostics step owned by this component.
    /// </summary>
    internal static IReadOnlyList<CodeDiagnostic> ParseBuildDiagnostics(string output, string workspaceRoot)
    {
        var root = Path.GetFullPath(workspaceRoot);
        var result = new List<CodeDiagnostic>();
        foreach (var line in output.ReplaceLineEndings("\n").Split('\n'))
        {
            var match = BuildDiagnosticPattern.Match(line.Trim());
            if (!match.Success) continue;
            var rawPath = match.Groups["file"].Value.Trim();
            var fullPath = Path.IsPathRooted(rawPath)
                ? Path.GetFullPath(rawPath)
                : Path.GetFullPath(Path.Combine(root, rawPath));
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
                match.Groups["severity"].Value.Equals("error", StringComparison.OrdinalIgnoreCase)
                    ? CodeDiagnosticSeverity.Error
                    : CodeDiagnosticSeverity.Warning,
                match.Groups["code"].Value,
                "dotnet build",
                match.Groups["message"].Value.Trim()));
        }
        return result;
    }

    /// <summary>
    /// Performs the resolve root step owned by this component.
    /// </summary>
    private static string ResolveRoot(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var root = Path.GetFullPath(workspaceRoot.Trim());
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        return root;
    }

    /// <summary>
    /// Performs the find dot net build target step owned by this component.
    /// </summary>
    private static string? FindDotNetBuildTarget(string root)
    {
        var solution = Directory.EnumerateFiles(root, "*.sln", SearchOption.TopDirectoryOnly)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (solution is not null) return solution;
        var project = Directory.EnumerateFiles(root, "*.*proj", SearchOption.TopDirectoryOnly)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (project is not null) return project;
        return LexicalSymbolSearch.EnumerateFiles(root, 400, CancellationToken.None)
            .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                           || path.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
                           || path.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path.Count(character => character is '/' or '\\'))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    /// <summary>
    /// Performs the symbol key step owned by this component.
    /// </summary>
    private static string SymbolKey(CodeSymbol symbol) =>
        $"{symbol.RelativePath}\n{symbol.Range.Start.Line}\n{symbol.Range.Start.Character}\n{symbol.Name}";

    /// <summary>
    /// Performs the infer language id step owned by this component.
    /// </summary>
    private static string InferLanguageId(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".cs" => "csharp", ".fs" => "fsharp", ".vb" => "vb", ".ts" => "typescript", ".tsx" => "typescriptreact",
        ".js" => "javascript", ".jsx" => "javascriptreact", ".py" => "python", ".rs" => "rust", ".go" => "go",
        ".java" => "java", ".cpp" or ".cc" or ".cxx" => "cpp", ".c" or ".h" => "c", _ => "plaintext"
    };

    /// <summary>
    /// Performs the content hash step owned by this component.
    /// </summary>
    private static string ContentHash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    /// <summary>
    /// Reports whether is within root is true for the current state.
    /// </summary>
    private static bool IsWithinRoot(string root, string path)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedPath = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return normalizedPath.Equals(normalizedRoot, comparison)
               || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
    }
}
