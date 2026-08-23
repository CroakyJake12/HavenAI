using System.Collections.ObjectModel;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Pages.WorkspaceEditor;

public sealed partial class WorkspaceEditorPage
{
    private readonly Func<CodeLocation, Task> _navigateCode;
    private IAdvancedCodeIntelligenceService? _advancedCode;
    private bool _advancedResolved;
    private LanguageServerCapabilities _languageCapabilities = LanguageServerCapabilities.None;
    private string _languageFeatureStatus = "Language-server capabilities have not been inspected yet.";
    private string _renameDraft = string.Empty;
    private int _caretOffset;
    private int _selectionStart;
    private int _selectionEnd;
    private CodeRange? _requestedNavigation;
    private AsyncRelayCommand? _goToDefinitionCommand;
    private AsyncRelayCommand? _findReferencesCommand;
    private AsyncRelayCommand? _renameSymbolCommand;
    private AsyncRelayCommand? _loadCompletionsCommand;
    private AsyncRelayCommand? _loadCodeActionsCommand;
    private AsyncRelayCommand? _refreshSemanticTokensCommand;

    public event EventHandler? NavigationRequested;

    public ObservableCollection<CodeLocationItemViewModel> Definitions { get; } = [];
    public ObservableCollection<CodeLocationItemViewModel> References { get; } = [];
    public ObservableCollection<CodeCompletionItemViewModel> Completions { get; } = [];
    public ObservableCollection<CodeActionItemViewModel> CodeActions { get; } = [];
    public ObservableCollection<SemanticTokenItemViewModel> SemanticTokens { get; } = [];

    public string LanguageFeatureStatus { get => _languageFeatureStatus; private set => SetProperty(ref _languageFeatureStatus, value); }
    public string RenameDraft { get => _renameDraft; set { if (SetProperty(ref _renameDraft, value)) RenameSymbolCommand.RaiseCanExecuteChanged(); } }
    public string CaretLocation => FormatPosition(PositionAt(Content, _caretOffset));
    public string LanguageCapabilitySummary => CapabilitySummary(_languageCapabilities);
    public string SemanticTokenSummary => _languageCapabilities.SemanticTokens ? $"{SemanticTokens.Count} protocol semantic token spans applied to editor presentation state." : "Semantic tokens unavailable from this language server.";

    public AsyncRelayCommand GoToDefinitionCommand => _goToDefinitionCommand ??= new AsyncRelayCommand(GoToDefinitionAsync, () => AdvancedCode is not null && _languageCapabilities.Definition);
    public AsyncRelayCommand FindReferencesCommand => _findReferencesCommand ??= new AsyncRelayCommand(FindReferencesAsync, () => AdvancedCode is not null && _languageCapabilities.References);
    public AsyncRelayCommand RenameSymbolCommand => _renameSymbolCommand ??= new AsyncRelayCommand(RenameSymbolAsync, () => AdvancedCode is not null && _languageCapabilities.Rename && !string.IsNullOrWhiteSpace(RenameDraft));
    public AsyncRelayCommand LoadCompletionsCommand => _loadCompletionsCommand ??= new AsyncRelayCommand(LoadCompletionsAsync, () => AdvancedCode is not null && _languageCapabilities.Completion);
    public AsyncRelayCommand LoadCodeActionsCommand => _loadCodeActionsCommand ??= new AsyncRelayCommand(LoadCodeActionsAsync, () => AdvancedCode is not null && _languageCapabilities.CodeActions);
    public AsyncRelayCommand RefreshSemanticTokensCommand => _refreshSemanticTokensCommand ??= new AsyncRelayCommand(() => RefreshSemanticTokensAsync(false), () => AdvancedCode is not null && _languageCapabilities.SemanticTokens);

    private IAdvancedCodeIntelligenceService? AdvancedCode
    {
        get
        {
            if (_advancedResolved) return _advancedCode;
            _advancedResolved = true;
            _advancedCode = App.Services?.GetService<IAdvancedCodeIntelligenceService>();
            return _advancedCode;
        }
    }

    public async Task RefreshAdvancedLanguageFeaturesAsync()
    {
        if (AdvancedCode is null)
        {
            SetCapabilities(LanguageServerCapabilities.None, "Advanced language-server integration is unavailable in this runtime.");
            return;
        }
        try
        {
            var capabilities = await AdvancedCode.GetCapabilitiesAsync(_file.Root, _file.RelativePath, CancellationToken.None);
            SetCapabilities(capabilities, "Language-server capabilities negotiated from the active server.");
            if (capabilities.SemanticTokens) await RefreshSemanticTokensAsync(true);
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException or IOException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            SetCapabilities(LanguageServerCapabilities.None, exception.Message);
        }
    }

    public void SetEditorSelection(string selectedText, int caretOffset, int selectionStart, int selectionEnd)
    {
        SelectedSnippet = selectedText;
        _caretOffset = Math.Clamp(caretOffset, 0, Content.Length);
        _selectionStart = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, Content.Length);
        _selectionEnd = Math.Clamp(Math.Max(selectionStart, selectionEnd), 0, Content.Length);
        RaisePropertyChanged(nameof(CaretLocation));
    }

    public void NavigateTo(CodeRange range)
    {
        _requestedNavigation = range;
        NavigationRequested?.Invoke(this, EventArgs.Empty);
    }

    internal CodeRange? TakeRequestedNavigation()
    {
        var range = _requestedNavigation;
        _requestedNavigation = null;
        return range;
    }

    internal static int OffsetAt(string text, CodePosition position)
    {
        if (position.Line < 0 || position.Character < 0) throw new InvalidOperationException("The language server returned a negative position.");
        var line = 0;
        var lineStart = 0;
        while (line < position.Line)
        {
            var next = text.IndexOf('\n', lineStart);
            if (next < 0) throw new InvalidOperationException("The language server returned a line outside the editor document.");
            lineStart = next + 1;
            line++;
        }
        var lineBreak = text.IndexOf('\n', lineStart);
        var lineEnd = lineBreak < 0 ? text.Length : lineBreak;
        if (lineEnd > lineStart && text[lineEnd - 1] == '\r') lineEnd--;
        if (lineStart + position.Character > lineEnd) throw new InvalidOperationException("The language server returned a character outside the editor line.");
        return lineStart + position.Character;
    }

    private async Task GoToDefinitionAsync()
    {
        if (AdvancedCode is null) return;
        try
        {
            var results = await AdvancedCode.GetDefinitionAsync(_file.Root, _file.RelativePath, Content, PositionAt(Content, _caretOffset), CancellationToken.None);
            ReplaceLocations(Definitions, results);
            LanguageFeatureStatus = results.Count == 0 ? "The language server returned no definition at this position." : $"Language server returned {results.Count} definition location(s).";
            if (results.Count == 1 && results[0].IsInWorkspace) await _navigateCode(results[0]);
        }
        catch (Exception ex) { LanguageFeatureStatus = FeatureFailure("Go to definition", ex); }
    }

    private async Task FindReferencesAsync()
    {
        if (AdvancedCode is null) return;
        try
        {
            var results = await AdvancedCode.FindReferencesAsync(_file.Root, _file.RelativePath, Content, PositionAt(Content, _caretOffset), CancellationToken.None);
            ReplaceLocations(References, results);
            LanguageFeatureStatus = results.Count == 0 ? "The language server returned no references at this position." : $"Language server returned {results.Count} reference location(s).";
        }
        catch (Exception ex) { LanguageFeatureStatus = FeatureFailure("Find references", ex); }
    }

    private async Task RenameSymbolAsync()
    {
        if (AdvancedCode is null) return;
        try
        {
            var result = await AdvancedCode.RenameSymbolAsync(_file.Root, _file.RelativePath, Content, PositionAt(Content, _caretOffset), RenameDraft, CancellationToken.None);
            await RecordLanguageMutationAsync(result);
            RenameDraft = string.Empty;
            LanguageFeatureStatus = $"{result.Summary} through workspace transaction {result.TransactionId:N}; {result.Files.Count} file(s) changed.";
            await RefreshAdvancedLanguageFeaturesAsync();
        }
        catch (Exception ex) { LanguageFeatureStatus = FeatureFailure("Rename", ex); }
    }

    private async Task LoadCompletionsAsync()
    {
        if (AdvancedCode is null) return;
        try
        {
            var results = await AdvancedCode.GetCompletionsAsync(_file.Root, _file.RelativePath, Content, PositionAt(Content, _caretOffset), CancellationToken.None);
            Completions.Clear();
            foreach (var item in results.Take(200)) Completions.Add(new CodeCompletionItemViewModel(item, ApplyCompletionAsync));
            LanguageFeatureStatus = results.Count == 0 ? "The language server returned no completions at this position." : $"Loaded {Math.Min(results.Count, 200)} protocol completion item(s).";
        }
        catch (Exception ex) { LanguageFeatureStatus = FeatureFailure("Completion", ex); }
    }

    private Task ApplyCompletionAsync(LanguageServerCompletion completion)
    {
        try
        {
            Content = ApplyCompletionToText(Content, _caretOffset, completion);
            LanguageFeatureStatus = $"Inserted language-server completion '{completion.Label}' using its protocol edit range. Save to record the file version.";
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            LanguageFeatureStatus = FeatureFailure("Completion apply", ex);
            return Task.CompletedTask;
        }
    }

    private async Task LoadCodeActionsAsync()
    {
        if (AdvancedCode is null) return;
        try
        {
            var start = PositionAt(Content, _selectionStart);
            var end = PositionAt(Content, _selectionEnd);
            var results = await AdvancedCode.GetCodeActionsAsync(_file.Root, _file.RelativePath, Content, new CodeRange(start, end), CancellationToken.None);
            CodeActions.Clear();
            foreach (var item in results.Take(100)) CodeActions.Add(new CodeActionItemViewModel(item, ApplyCodeActionAsync));
            LanguageFeatureStatus = results.Count == 0 ? "The language server returned no code actions for this range." : $"Loaded {Math.Min(results.Count, 100)} server code action(s); unsafe command-only actions stay unavailable.";
        }
        catch (Exception ex) { LanguageFeatureStatus = FeatureFailure("Code actions", ex); }
    }

    private async Task ApplyCodeActionAsync(CodeActionProposal action)
    {
        if (AdvancedCode is null) return;
        try
        {
            var result = await AdvancedCode.ApplyCodeActionAsync(_file.Root, action, CancellationToken.None);
            await RecordLanguageMutationAsync(result);
            LanguageFeatureStatus = $"Applied '{action.Title}' through workspace transaction {result.TransactionId:N}.";
            await RefreshAdvancedLanguageFeaturesAsync();
        }
        catch (Exception ex) { LanguageFeatureStatus = FeatureFailure("Code action", ex); }
    }

    private async Task RefreshSemanticTokensAsync(bool silent)
    {
        if (AdvancedCode is null || !_languageCapabilities.SemanticTokens) return;
        try
        {
            var tokens = await AdvancedCode.GetSemanticTokensAsync(_file.Root, _file.RelativePath, Content, CancellationToken.None);
            SemanticTokens.Clear();
            foreach (var token in tokens.Take(5_000)) SemanticTokens.Add(new SemanticTokenItemViewModel(token));
            RaisePropertyChanged(nameof(SemanticTokenSummary));
            if (!silent) LanguageFeatureStatus = $"Applied {SemanticTokens.Count} protocol semantic token span(s) to the editor presentation model.";
        }
        catch (Exception ex)
        {
            SemanticTokens.Clear();
            RaisePropertyChanged(nameof(SemanticTokenSummary));
            if (!silent) LanguageFeatureStatus = FeatureFailure("Semantic tokens", ex);
        }
    }

    private async Task RecordLanguageMutationAsync(CodeWorkspaceMutationResult result)
    {
        WorkspaceVersion? currentVersion = null;
        foreach (var file in result.Files)
        {
            var (added, removed) = CountLineChanges(file.BeforeContent, file.AfterContent);
            var version = new WorkspaceVersion(Guid.NewGuid(), _conversationId, _container.Id, _file.Root, file.RelativePath, WorkspaceVersionKind.Edit, file.BeforeContent, file.AfterContent, result.Summary, added, removed, DateTimeOffset.UtcNow);
            await _history.AddVersionAsync(version, CancellationToken.None);
            if (file.RelativePath.Equals(_file.RelativePath, StringComparison.OrdinalIgnoreCase)) currentVersion = version;
        }
        if (currentVersion is null) return;
        _savedContent = currentVersion.AfterContent;
        Content = currentVersion.AfterContent;
        _undo.Push(currentVersion);
        _redo.Clear();
        var counts = CountLineChanges(currentVersion.BeforeContent, currentVersion.AfterContent);
        Changelog.Insert(0, $"{DateTimeOffset.Now:t} · {result.Summary} · +{counts.Added}/-{counts.Removed}");
        await RefreshVersionsAsync();
        RaiseDirtyProperties();
        RaiseHistoryCommands();
    }

    private void ReplaceLocations(ObservableCollection<CodeLocationItemViewModel> target, IReadOnlyList<CodeLocation> locations)
    {
        target.Clear();
        foreach (var item in locations.Take(500)) target.Add(new CodeLocationItemViewModel(item, _navigateCode));
    }

    private void SetCapabilities(LanguageServerCapabilities capabilities, string status)
    {
        _languageCapabilities = capabilities;
        LanguageFeatureStatus = status;
        RaisePropertyChanged(nameof(LanguageCapabilitySummary));
        RaisePropertyChanged(nameof(SemanticTokenSummary));
        GoToDefinitionCommand.RaiseCanExecuteChanged();
        FindReferencesCommand.RaiseCanExecuteChanged();
        RenameSymbolCommand.RaiseCanExecuteChanged();
        LoadCompletionsCommand.RaiseCanExecuteChanged();
        LoadCodeActionsCommand.RaiseCanExecuteChanged();
        RefreshSemanticTokensCommand.RaiseCanExecuteChanged();
    }

    private static string CapabilitySummary(LanguageServerCapabilities c)
    {
        var values = new[] { ("Definition", c.Definition), ("References", c.References), ("Rename", c.Rename), ("Completion", c.Completion), ("Code actions", c.CodeActions), ("Semantic tokens", c.SemanticTokens) };
        return string.Join(" · ", values.Select(item => $"{item.Item1}: {(item.Item2 ? "ready" : "unavailable")}"));
    }

    private static string FeatureFailure(string feature, Exception exception) => exception is NotSupportedException ? $"{feature} unavailable: {exception.Message}" : $"{feature} failed: {exception.Message}";
    private static string FormatPosition(CodePosition position) => $"Ln {position.Line + 1}, Col {position.Character + 1}";

    private static CodePosition PositionAt(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        var line = 0;
        var lineStart = 0;
        for (var index = 0; index < offset; index++)
            if (text[index] == '\n') { line++; lineStart = index + 1; }
        return new CodePosition(line, offset - lineStart);
    }

    internal static string ApplyCompletionToText(string original, int caretOffset, LanguageServerCompletion completion)
    {
        var caret = PositionAt(original, caretOffset);
        var target = completion.ReplaceRange ?? completion.InsertRange ?? new CodeRange(caret, caret);
        var edits = completion.AdditionalTextEdits.Concat([new LanguageServerTextEdit(target, completion.InsertText)]).ToArray();
        return ApplyEditorEdits(original, edits);
    }

    internal static string ApplyEditorEdits(string original, IReadOnlyList<LanguageServerTextEdit> edits)
    {
        var resolved = edits.Select(edit => (Start: OffsetAt(original, edit.Range.Start), End: OffsetAt(original, edit.Range.End), edit.NewText)).OrderByDescending(edit => edit.Start).ThenByDescending(edit => edit.End).ToArray();
        var boundary = original.Length;
        var builder = new System.Text.StringBuilder(original);
        foreach (var edit in resolved)
        {
            if (edit.End < edit.Start || edit.End > original.Length || edit.End > boundary) throw new InvalidOperationException("The completion returned overlapping or invalid text edits.");
            builder.Remove(edit.Start, edit.End - edit.Start);
            builder.Insert(edit.Start, edit.NewText);
            boundary = edit.Start;
        }
        return builder.ToString();
    }
}

public sealed class CodeLocationItemViewModel
{
    public CodeLocationItemViewModel(CodeLocation definition, Func<CodeLocation, Task> open)
    {
        Definition = definition;
        OpenCommand = new AsyncRelayCommand(() => definition.IsInWorkspace ? open(definition) : Task.CompletedTask, () => definition.IsInWorkspace);
    }
    public CodeLocation Definition { get; }
    public string Location => $"{Definition.DisplayPath}:{Definition.Range.Start.Line + 1}:{Definition.Range.Start.Character + 1}";
    public string Scope => Definition.IsInWorkspace ? "Project" : "External";
    public AsyncRelayCommand OpenCommand { get; }
}

public sealed class CodeCompletionItemViewModel
{
    public CodeCompletionItemViewModel(LanguageServerCompletion definition, Func<LanguageServerCompletion, Task> apply) { Definition = definition; ApplyCommand = new AsyncRelayCommand(() => apply(definition)); }
    public LanguageServerCompletion Definition { get; }
    public string Label => Definition.Label;
    public string Detail => Definition.Detail ?? "Protocol completion";
    public string RangeMode => Definition.ReplaceRange is not null ? "replace range" : Definition.InsertRange is not null ? "insert range" : "caret";
    public AsyncRelayCommand ApplyCommand { get; }
}

public sealed class CodeActionItemViewModel
{
    public CodeActionItemViewModel(CodeActionProposal definition, Func<CodeActionProposal, Task> apply) { Definition = definition; ApplyCommand = new AsyncRelayCommand(() => apply(definition), () => definition.IsApplicable); }
    public CodeActionProposal Definition { get; }
    public string Title => Definition.Title;
    public string Detail => Definition.IsApplicable ? $"{Definition.Files.Count} file edit(s){(Definition.IsPreferred ? " · preferred" : string.Empty)}" : Definition.UnavailableReason ?? "Unavailable";
    public AsyncRelayCommand ApplyCommand { get; }
}

public sealed class SemanticTokenItemViewModel(CodeSemanticToken definition)
{
    public CodeSemanticToken Definition { get; } = definition;
    public string Display => $"{definition.Range.Start.Line + 1}:{definition.Range.Start.Character + 1} · {definition.TokenType}{(definition.Modifiers.Count == 0 ? string.Empty : " · " + string.Join(", ", definition.Modifiers))}";
}
