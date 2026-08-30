using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

internal sealed partial class StdioLanguageServerClient : IAdvancedLanguageServerClient
{
    internal static readonly string[] SemanticTokenClientTypes = ["namespace", "type", "class", "enum", "interface", "struct", "typeParameter", "parameter", "variable", "property", "enumMember", "event", "function", "method", "macro", "keyword", "modifier", "comment", "string", "number", "regexp", "operator", "decorator"];
    internal static readonly string[] SemanticTokenClientModifiers = ["declaration", "definition", "readonly", "static", "deprecated", "abstract", "async", "modification", "documentation", "defaultLibrary"];
    private LanguageServerCapabilities _advancedCapabilities = LanguageServerCapabilities.None;

    public LanguageServerCapabilities AdvancedCapabilities => _advancedCapabilities;

    private void CaptureAdvancedCapabilities(JsonElement initializeResult)
    {
        if (initializeResult.ValueKind != JsonValueKind.Object || !initializeResult.TryGetProperty("capabilities", out var capabilities) || capabilities.ValueKind != JsonValueKind.Object)
        {
            _advancedCapabilities = LanguageServerCapabilities.None;
            return;
        }

        var tokenTypes = Array.Empty<string>();
        var tokenModifiers = Array.Empty<string>();
        var semanticTokens = false;
        if (capabilities.TryGetProperty("semanticTokensProvider", out var semanticProvider) && semanticProvider.ValueKind == JsonValueKind.Object)
        {
            semanticTokens = semanticProvider.TryGetProperty("full", out var full) && full.ValueKind is JsonValueKind.True or JsonValueKind.Object;
            if (semanticProvider.TryGetProperty("legend", out var legend) && legend.ValueKind == JsonValueKind.Object)
            {
                tokenTypes = ReadStringArray(legend, "tokenTypes", 512);
                tokenModifiers = ReadStringArray(legend, "tokenModifiers", 64);
            }
        }

        var resolveActions = capabilities.TryGetProperty("codeActionProvider", out var codeActions)
            && codeActions.ValueKind == JsonValueKind.Object
            && codeActions.TryGetProperty("resolveProvider", out var resolveProvider)
            && resolveProvider.ValueKind == JsonValueKind.True;
        _advancedCapabilities = new LanguageServerCapabilities(
            ProviderEnabled(capabilities, "definitionProvider"),
            ProviderEnabled(capabilities, "referencesProvider"),
            ProviderEnabled(capabilities, "renameProvider"),
            ProviderEnabled(capabilities, "completionProvider"),
            ProviderEnabled(capabilities, "codeActionProvider"),
            resolveActions,
            semanticTokens && tokenTypes.Length > 0,
            tokenTypes,
            tokenModifiers);
    }

    public async Task<IReadOnlyList<CodeLocation>> GetDefinitionAsync(string documentUri, CodePosition position, string workspaceRoot, CancellationToken cancellationToken)
    {
        Require(AdvancedCapabilities.Definition, "go to definition");
        var result = await RequestAsync("textDocument/definition", PositionParams(documentUri, position), cancellationToken).ConfigureAwait(false);
        return ParseLocations(result, workspaceRoot);
    }

    public async Task<IReadOnlyList<CodeLocation>> FindReferencesAsync(string documentUri, CodePosition position, string workspaceRoot, bool includeDeclaration, CancellationToken cancellationToken)
    {
        Require(AdvancedCapabilities.References, "find references");
        var result = await RequestAsync("textDocument/references", new
        {
            textDocument = new { uri = documentUri },
            position = new { line = position.Line, character = position.Character },
            context = new { includeDeclaration }
        }, cancellationToken).ConfigureAwait(false);
        return ParseLocations(result, workspaceRoot);
    }

    public async Task<LanguageServerWorkspaceEdit> RenameSymbolAsync(string documentUri, CodePosition position, string newName, string workspaceRoot, CancellationToken cancellationToken)
    {
        Require(AdvancedCapabilities.Rename, "rename");
        var result = await RequestAsync("textDocument/rename", new
        {
            textDocument = new { uri = documentUri },
            position = new { line = position.Line, character = position.Character },
            newName
        }, cancellationToken).ConfigureAwait(false);
        return ParseWorkspaceEdit(result, workspaceRoot);
    }

    public async Task<IReadOnlyList<LanguageServerCompletion>> GetCompletionsAsync(string documentUri, CodePosition position, CancellationToken cancellationToken)
    {
        Require(AdvancedCapabilities.Completion, "completion");
        var result = await RequestAsync("textDocument/completion", PositionParams(documentUri, position), cancellationToken).ConfigureAwait(false);
        var items = result.ValueKind == JsonValueKind.Array ? result : result.ValueKind == JsonValueKind.Object && result.TryGetProperty("items", out var listed) ? listed : default;
        if (items.ValueKind != JsonValueKind.Array) return [];
        var output = new List<LanguageServerCompletion>();
        foreach (var item in items.EnumerateArray())
        {
            if (output.Count >= 500 || item.ValueKind != JsonValueKind.Object) break;
            var label = item.TryGetProperty("label", out var labelElement) ? labelElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(label)) continue;
            var detail = item.TryGetProperty("detail", out var detailElement) && detailElement.ValueKind == JsonValueKind.String ? detailElement.GetString() : null;
            var insertText = item.TryGetProperty("insertText", out var insertTextElement) && insertTextElement.ValueKind == JsonValueKind.String ? insertTextElement.GetString() ?? label : label;
            CodeRange? insertRange = null;
            CodeRange? replaceRange = null;
            if (item.TryGetProperty("textEdit", out var textEdit) && textEdit.ValueKind == JsonValueKind.Object)
            {
                if (textEdit.TryGetProperty("newText", out var newText) && newText.ValueKind == JsonValueKind.String) insertText = newText.GetString() ?? string.Empty;
                if (textEdit.TryGetProperty("range", out var range) && TryReadRange(range, out var simpleRange)) insertRange = replaceRange = simpleRange;
                else
                {
                    if (textEdit.TryGetProperty("insert", out var insert) && TryReadRange(insert, out var parsedInsert)) insertRange = parsedInsert;
                    if (textEdit.TryGetProperty("replace", out var replace) && TryReadRange(replace, out var parsedReplace)) replaceRange = parsedReplace;
                }
            }
            var additional = item.TryGetProperty("additionalTextEdits", out var additionalElement) ? ParseTextEdits(additionalElement) : [];
            output.Add(new LanguageServerCompletion(label, detail, insertText, insertRange, replaceRange, additional));
        }
        return output;
    }

    public async Task<IReadOnlyList<LanguageServerCodeAction>> GetCodeActionsAsync(string documentUri, CodeRange range, string workspaceRoot, CancellationToken cancellationToken)
    {
        Require(AdvancedCapabilities.CodeActions, "code actions");
        var result = await RequestAsync("textDocument/codeAction", new
        {
            textDocument = new { uri = documentUri },
            range = RangeParams(range),
            context = new { diagnostics = Array.Empty<object>() }
        }, cancellationToken).ConfigureAwait(false);
        if (result.ValueKind != JsonValueKind.Array) return [];
        var output = new List<LanguageServerCodeAction>();
        foreach (var raw in result.EnumerateArray())
        {
            if (output.Count >= 100 || raw.ValueKind != JsonValueKind.Object) break;
            var item = raw.Clone();
            if (!item.TryGetProperty("edit", out _) && AdvancedCapabilities.CodeActionResolve && item.TryGetProperty("data", out _))
            {
                try
                {
                    var resolved = await RequestAsync("codeAction/resolve", item, cancellationToken).ConfigureAwait(false);
                    if (resolved.ValueKind == JsonValueKind.Object) item = resolved;
                }
                catch (LanguageServerRequestException exception) when (exception.Code is -32601 or -32602) { }
            }
            var title = item.TryGetProperty("title", out var titleElement) ? titleElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(title)) continue;
            var kind = item.TryGetProperty("kind", out var kindElement) && kindElement.ValueKind == JsonValueKind.String ? kindElement.GetString() : null;
            var preferred = item.TryGetProperty("isPreferred", out var preferredElement) && preferredElement.ValueKind == JsonValueKind.True;
            LanguageServerWorkspaceEdit? edit = item.TryGetProperty("edit", out var editElement) ? ParseWorkspaceEdit(editElement, workspaceRoot) : null;
            string? command = null;
            if (item.TryGetProperty("command", out var commandElement))
            {
                if (commandElement.ValueKind == JsonValueKind.String) command = commandElement.GetString();
                else if (commandElement.ValueKind == JsonValueKind.Object && commandElement.TryGetProperty("command", out var nested) && nested.ValueKind == JsonValueKind.String) command = nested.GetString();
            }
            output.Add(new LanguageServerCodeAction(title, kind, preferred, edit, command));
        }
        return output;
    }

    public async Task<IReadOnlyList<CodeSemanticToken>> GetSemanticTokensAsync(string documentUri, CancellationToken cancellationToken)
    {
        Require(AdvancedCapabilities.SemanticTokens, "semantic tokens");
        var result = await RequestAsync("textDocument/semanticTokens/full", new { textDocument = new { uri = documentUri } }, cancellationToken).ConfigureAwait(false);
        if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return [];
        var values = data.EnumerateArray().Select(item => item.TryGetInt32(out var value) ? value : -1).ToArray();
        if (values.Length % 5 != 0 || values.Any(value => value < 0)) throw new InvalidOperationException($"{_definition.DisplayName} returned malformed semantic-token data.");
        var output = new List<CodeSemanticToken>(Math.Min(values.Length / 5, 100_000));
        var line = 0;
        var character = 0;
        for (var index = 0; index < values.Length && output.Count < 100_000; index += 5)
        {
            var deltaLine = values[index];
            var deltaStart = values[index + 1];
            var length = values[index + 2];
            var tokenTypeIndex = values[index + 3];
            var modifierBits = values[index + 4];
            line += deltaLine;
            character = deltaLine == 0 ? character + deltaStart : deltaStart;
            if (length <= 0 || tokenTypeIndex < 0 || tokenTypeIndex >= AdvancedCapabilities.SemanticTokenTypes.Count) continue;
            var modifiers = new List<string>();
            for (var bit = 0; bit < AdvancedCapabilities.SemanticTokenModifiers.Count && bit < 31; bit++)
                if ((modifierBits & (1 << bit)) != 0) modifiers.Add(AdvancedCapabilities.SemanticTokenModifiers[bit]);
            output.Add(new CodeSemanticToken(new CodeRange(new CodePosition(line, character), new CodePosition(line, character + length)), AdvancedCapabilities.SemanticTokenTypes[tokenTypeIndex], modifiers));
        }
        return output;
    }

    private static object PositionParams(string documentUri, CodePosition position) => new { textDocument = new { uri = documentUri }, position = new { line = position.Line, character = position.Character } };
    private static object RangeParams(CodeRange range) => new { start = new { line = range.Start.Line, character = range.Start.Character }, end = new { line = range.End.Line, character = range.End.Character } };
    private static void Require(bool supported, string feature) { if (!supported) throw new NotSupportedException($"The configured language server does not advertise {feature} support."); }
    private static bool ProviderEnabled(JsonElement capabilities, string name) => capabilities.TryGetProperty(name, out var provider) && provider.ValueKind is JsonValueKind.True or JsonValueKind.Object;
    private static string[] ReadStringArray(JsonElement owner, string name, int limit) => owner.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Array ? values.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).Take(limit).ToArray() : [];

    private static IReadOnlyList<CodeLocation> ParseLocations(JsonElement result, string workspaceRoot)
    {
        if (result.ValueKind == JsonValueKind.Null) return [];
        var elements = result.ValueKind == JsonValueKind.Array ? result.EnumerateArray().ToArray() : [result];
        var output = new List<CodeLocation>();
        foreach (var item in elements)
        {
            if (output.Count >= 1_000 || item.ValueKind != JsonValueKind.Object) break;
            string? uri = null;
            JsonElement rangeElement = default;
            if (item.TryGetProperty("uri", out var uriElement) && uriElement.ValueKind == JsonValueKind.String)
            {
                uri = uriElement.GetString();
                if (!item.TryGetProperty("range", out rangeElement)) continue;
            }
            else if (item.TryGetProperty("targetUri", out var targetUri) && targetUri.ValueKind == JsonValueKind.String)
            {
                uri = targetUri.GetString();
                if (!item.TryGetProperty("targetSelectionRange", out rangeElement) && !item.TryGetProperty("targetRange", out rangeElement)) continue;
            }
            if (string.IsNullOrWhiteSpace(uri) || !TryReadRange(rangeElement, out var range)) continue;
            var relative = RelativePathFromUri(uri, workspaceRoot);
            if (relative is not null) output.Add(new CodeLocation(uri, relative, range, true));
            else if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.IsFile) output.Add(new CodeLocation(uri, parsed.LocalPath, range, false));
            else output.Add(new CodeLocation(uri, uri, range, false));
        }
        return output;
    }

    private static LanguageServerWorkspaceEdit ParseWorkspaceEdit(JsonElement edit, string workspaceRoot)
    {
        if (edit.ValueKind == JsonValueKind.Null) return new LanguageServerWorkspaceEdit([]);
        if (edit.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("The language server returned a malformed WorkspaceEdit.");
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var documents = new Dictionary<string, List<LanguageServerTextEdit>>(comparer);
        void Add(string uri, JsonElement edits)
        {
            var relative = RelativePathFromUri(uri, workspaceRoot) ?? throw new InvalidOperationException("The language server attempted to edit a file outside the project root.");
            if (!documents.TryGetValue(relative, out var list)) documents[relative] = list = [];
            list.AddRange(ParseTextEdits(edits));
            if (list.Count > 10_000) throw new InvalidOperationException("The language server returned too many edits for one file.");
        }
        if (edit.TryGetProperty("changes", out var changes))
        {
            if (changes.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("The language server returned malformed WorkspaceEdit changes.");
            foreach (var property in changes.EnumerateObject()) Add(property.Name, property.Value);
        }
        if (edit.TryGetProperty("documentChanges", out var documentChanges))
        {
            if (documentChanges.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("The language server returned malformed WorkspaceEdit documentChanges.");
            foreach (var change in documentChanges.EnumerateArray())
            {
                if (change.ValueKind != JsonValueKind.Object || change.TryGetProperty("kind", out _)) throw new InvalidOperationException("Language-server file create/rename/delete operations are not applied automatically.");
                if (!change.TryGetProperty("textDocument", out var document) || document.ValueKind != JsonValueKind.Object || !document.TryGetProperty("uri", out var uriElement) || uriElement.ValueKind != JsonValueKind.String || !change.TryGetProperty("edits", out var edits)) throw new InvalidOperationException("The language server returned a malformed TextDocumentEdit.");
                Add(uriElement.GetString()!, edits);
            }
        }
        if (documents.Count > 200) throw new InvalidOperationException("The language server attempted to edit too many project files at once.");
        return new LanguageServerWorkspaceEdit(documents.Select(pair => new LanguageServerDocumentEdit(pair.Key, pair.Value)).ToArray());
    }

    private static IReadOnlyList<LanguageServerTextEdit> ParseTextEdits(JsonElement edits)
    {
        if (edits.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("The language server returned malformed text edits.");
        var output = new List<LanguageServerTextEdit>();
        foreach (var item in edits.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("range", out var rangeElement) || !TryReadRange(rangeElement, out var range) || !item.TryGetProperty("newText", out var newText) || newText.ValueKind != JsonValueKind.String) throw new InvalidOperationException("The language server returned a malformed text edit.");
            output.Add(new LanguageServerTextEdit(range, newText.GetString() ?? string.Empty));
        }
        return output;
    }
}
