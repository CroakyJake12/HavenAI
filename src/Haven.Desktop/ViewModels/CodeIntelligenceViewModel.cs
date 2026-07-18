/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/ViewModels/CodeIntelligenceViewModel.cs, in the Desktop presentation-model layer, exposing bindable state and commands to Avalonia views.
 * What: This file owns CodeIntelligenceViewModel, CodeDiagnosticItemViewModel, CodeSymbolItemViewModel. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Keeping UI state here makes the XAML declarative and keeps behavior testable without recreating the full window.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.ObjectModel;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Represents code intelligence view model and keeps its related state and behavior together.
/// </summary>
public sealed class CodeIntelligenceViewModel : ObservableObject
{
    /// <summary>
    /// Stores intelligence locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ICodeIntelligenceService _intelligence;
    /// <summary>
    /// Stores workspace root locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _workspaceRoot = string.Empty;
    /// <summary>
    /// Stores relative path locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _relativePath = string.Empty;
    /// <summary>
    /// Stores symbol query locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _symbolQuery = string.Empty;
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _status = "Open a Studio project to use code intelligence.";
    /// <summary>
    /// Stores server status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _serverStatus = string.Empty;
    /// <summary>
    /// Stores format diff locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _formatDiff = string.Empty;
    /// <summary>
    /// Stores formatter locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _formatter = string.Empty;
    /// <summary>
    /// Stores is busy locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isBusy;
    /// <summary>
    /// Stores insert spaces locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _insertSpaces = true;
    /// <summary>
    /// Stores tab size locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int _tabSize = 4;
    /// <summary>
    /// Stores preview locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private CodeFormatPreview? _preview;

    public CodeIntelligenceViewModel(ICodeIntelligenceService intelligence)
    {
        _intelligence = intelligence;
        InspectCommand = new AsyncRelayCommand(InspectAsync, CanUseFile);
        DiagnosticsCommand = new AsyncRelayCommand(LoadDiagnosticsAsync, CanUseFile);
        SearchSymbolsCommand = new AsyncRelayCommand(SearchSymbolsAsync, () => HasWorkspace && !string.IsNullOrWhiteSpace(SymbolQuery) && !IsBusy);
        PreviewFormatCommand = new AsyncRelayCommand(PreviewFormatAsync, CanUseFile);
        ApplyFormatCommand = new AsyncRelayCommand(ApplyFormatAsync, () => _preview?.HasChanges == true && !IsBusy);
        InsertDiagnosticsCommand = new RelayCommand(InsertDiagnostics, () => Diagnostics.Count > 0 && !IsBusy);
        InsertSymbolsCommand = new RelayCommand(InsertSymbols, () => Symbols.Count > 0 && !IsBusy);
        ClearCommand = new RelayCommand(ClearResults);
    }

    /// <summary>
    /// Stores insert requested locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event Action<string>? InsertRequested;

    /// <summary>
    /// Gets or updates diagnostics, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<CodeDiagnosticItemViewModel> Diagnostics { get; } = [];
    /// <summary>
    /// Gets or updates symbols, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<CodeSymbolItemViewModel> Symbols { get; } = [];
    /// <summary>
    /// Gets or updates inspect command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand InspectCommand { get; }
    /// <summary>
    /// Gets or updates diagnostics command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand DiagnosticsCommand { get; }
    /// <summary>
    /// Gets or updates search symbols command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand SearchSymbolsCommand { get; }
    /// <summary>
    /// Gets or updates preview format command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand PreviewFormatCommand { get; }
    /// <summary>
    /// Gets or updates apply format command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand ApplyFormatCommand { get; }
    /// <summary>
    /// Gets or updates insert diagnostics command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand InsertDiagnosticsCommand { get; }
    /// <summary>
    /// Gets or updates insert symbols command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand InsertSymbolsCommand { get; }
    /// <summary>
    /// Gets or updates clear command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand ClearCommand { get; }

    public string RelativePath
    {
        get => _relativePath;
        set
        {
            if (!SetProperty(ref _relativePath, value)) return;
            _preview = null;
            FormatDiff = string.Empty;
            Formatter = string.Empty;
            RaiseCommandStates();
        }
    }

    public string SymbolQuery
    {
        get => _symbolQuery;
        set
        {
            if (!SetProperty(ref _symbolQuery, value)) return;
            SearchSymbolsCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>
    /// Gets or updates server status, the bindable or domain state represented by this property.
    /// </summary>
    public string ServerStatus { get => _serverStatus; private set => SetProperty(ref _serverStatus, value); }
    /// <summary>
    /// Gets or updates format diff, the bindable or domain state represented by this property.
    /// </summary>
    public string FormatDiff { get => _formatDiff; private set { if (SetProperty(ref _formatDiff, value)) RaisePropertyChanged(nameof(HasFormatPreview)); } }
    /// <summary>
    /// Gets or updates formatter, the bindable or domain state represented by this property.
    /// </summary>
    public string Formatter { get => _formatter; private set => SetProperty(ref _formatter, value); }
    /// <summary>
    /// Reports whether has format preview is true for the current state.
    /// </summary>
    public bool HasFormatPreview => !string.IsNullOrWhiteSpace(FormatDiff);
    /// <summary>
    /// Reports whether has workspace is true for the current state.
    /// </summary>
    public bool HasWorkspace => !string.IsNullOrWhiteSpace(_workspaceRoot) && Directory.Exists(_workspaceRoot);
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            RaiseCommandStates();
        }
    }
    /// <summary>
    /// Gets or updates insert spaces, the bindable or domain state represented by this property.
    /// </summary>
    public bool InsertSpaces { get => _insertSpaces; set => SetProperty(ref _insertSpaces, value); }
    /// <summary>
    /// Gets or updates tab size, the bindable or domain state represented by this property.
    /// </summary>
    public int TabSize { get => _tabSize; set => SetProperty(ref _tabSize, Math.Clamp(value, 1, 16)); }

    /// <summary>
    /// Performs the set chat step owned by this component.
    /// </summary>
    public void SetChat(ChatPageViewModel? chat)
    {
        _workspaceRoot = chat?.Mode == HavenMode.Studio && chat.SelectedContainer?.Definition.RootPath is { Length: > 0 } root
            ? Path.GetFullPath(root)
            : string.Empty;
        RaisePropertyChanged(nameof(HasWorkspace));
        ClearResults();
        Status = HasWorkspace
            ? $"Code intelligence is scoped to {chat!.SelectedContainer!.Name}. Paths must stay inside this project."
            : "Open a Studio project to use code intelligence.";
        RaiseCommandStates();
    }

    /// <summary>
    /// Performs inspect async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task InspectAsync()
    {
        await ExecuteAsync(async () =>
        {
            var status = await _intelligence.GetStatusAsync(_workspaceRoot, RelativePath.Trim(), CancellationToken.None);
            ServerStatus = status.LanguageServer is null
                ? "No enabled language server matches this file."
                : status.LanguageServer.Message;
            Status = status.Message + $" Language: {status.LanguageId}.";
        });
    }

    /// <summary>
    /// Performs load diagnostics async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task LoadDiagnosticsAsync()
    {
        await ExecuteAsync(async () =>
        {
            Diagnostics.Clear();
            foreach (var diagnostic in await _intelligence.GetDiagnosticsAsync(_workspaceRoot, RelativePath.Trim(), CancellationToken.None))
                Diagnostics.Add(new CodeDiagnosticItemViewModel(diagnostic));
            RaiseDiagnosticState();
            Status = Diagnostics.Count == 0
                ? "No diagnostics were reported."
                : $"{Diagnostics.Count} diagnostic{(Diagnostics.Count == 1 ? string.Empty : "s")}: {Diagnostics.Count(item => item.Severity == CodeDiagnosticSeverity.Error)} errors, {Diagnostics.Count(item => item.Severity == CodeDiagnosticSeverity.Warning)} warnings.";
        });
    }

    /// <summary>
    /// Performs search symbols async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task SearchSymbolsAsync()
    {
        await ExecuteAsync(async () =>
        {
            Symbols.Clear();
            foreach (var symbol in await _intelligence.SearchSymbolsAsync(_workspaceRoot, SymbolQuery.Trim(), CancellationToken.None))
                Symbols.Add(new CodeSymbolItemViewModel(symbol));
            RaiseSymbolState();
            Status = Symbols.Count == 0
                ? "No matching symbols were found in the selected project."
                : $"Found {Symbols.Count} matching symbol{(Symbols.Count == 1 ? string.Empty : "s")}.";
        });
    }

    /// <summary>
    /// Performs preview format async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task PreviewFormatAsync()
    {
        await ExecuteAsync(async () =>
        {
            _preview = await _intelligence.PreviewFormatAsync(
                _workspaceRoot,
                RelativePath.Trim(),
                TabSize,
                InsertSpaces,
                CancellationToken.None);
            FormatDiff = _preview.UnifiedDiff;
            Formatter = _preview.Formatter;
            Status = _preview.HasChanges
                ? $"Review the {_preview.Formatter} diff, then apply it explicitly. The file hash will be rechecked first."
                : $"{_preview.Formatter} proposed no changes.";
            ApplyFormatCommand.RaiseCanExecuteChanged();
        });
    }

    /// <summary>
    /// Performs apply format async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ApplyFormatAsync()
    {
        if (_preview is null) return;
        await ExecuteAsync(async () =>
        {
            var result = await _intelligence.ApplyFormatAsync(_workspaceRoot, _preview, CancellationToken.None);
            Status = result.Message;
            _preview = null;
            FormatDiff = string.Empty;
            Formatter = string.Empty;
            ApplyFormatCommand.RaiseCanExecuteChanged();
        });
    }

    /// <summary>
    /// Performs the insert diagnostics step owned by this component.
    /// </summary>
    private void InsertDiagnostics()
    {
        var text = string.Join("\n", Diagnostics.Take(100).Select(item =>
            $"- {item.Severity}: {item.Location} {item.CodeLabel}{item.Message}"));
        InsertRequested?.Invoke("\n\nReview and fix these project diagnostics. Inspect the relevant files before proposing changes, preserve unrelated edits, and use the reviewed transactional change-set flow:\n" + text);
    }

    /// <summary>
    /// Performs the insert symbols step owned by this component.
    /// </summary>
    private void InsertSymbols()
    {
        var text = string.Join("\n", Symbols.Take(100).Select(item => $"- {item.Kind} {item.Name} — {item.Location}"));
        InsertRequested?.Invoke("\n\nUse these code-intelligence symbol results as navigation hints. Read the source before editing and do not assume lexical fallback results are semantically complete:\n" + text);
    }

    /// <summary>
    /// Runs execute async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    private async Task ExecuteAsync(Func<Task> operation)
    {
        try
        {
            IsBusy = true;
            await operation();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Status = "Code intelligence failed: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Reports whether can use file is true for the current state.
    /// </summary>
    private bool CanUseFile() => HasWorkspace && !string.IsNullOrWhiteSpace(RelativePath) && !IsBusy;

    /// <summary>
    /// Performs the clear results step owned by this component.
    /// </summary>
    private void ClearResults()
    {
        Diagnostics.Clear();
        Symbols.Clear();
        _preview = null;
        FormatDiff = string.Empty;
        Formatter = string.Empty;
        ServerStatus = string.Empty;
        RaiseDiagnosticState();
        RaiseSymbolState();
        RaiseCommandStates();
    }

    /// <summary>
    /// Performs the raise diagnostic state step owned by this component.
    /// </summary>
    private void RaiseDiagnosticState()
    {
        RaisePropertyChanged(nameof(Diagnostics));
        InsertDiagnosticsCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Performs the raise symbol state step owned by this component.
    /// </summary>
    private void RaiseSymbolState()
    {
        RaisePropertyChanged(nameof(Symbols));
        InsertSymbolsCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Performs the raise command states step owned by this component.
    /// </summary>
    private void RaiseCommandStates()
    {
        InspectCommand.RaiseCanExecuteChanged();
        DiagnosticsCommand.RaiseCanExecuteChanged();
        SearchSymbolsCommand.RaiseCanExecuteChanged();
        PreviewFormatCommand.RaiseCanExecuteChanged();
        ApplyFormatCommand.RaiseCanExecuteChanged();
        InsertDiagnosticsCommand.RaiseCanExecuteChanged();
        InsertSymbolsCommand.RaiseCanExecuteChanged();
    }
}

/// <summary>
/// Represents code diagnostic item view model and keeps its related state and behavior together.
/// </summary>
public sealed record CodeDiagnosticItemViewModel(CodeDiagnostic Diagnostic)
{
    /// <summary>
    /// Gets or updates severity, the bindable or domain state represented by this property.
    /// </summary>
    public CodeDiagnosticSeverity Severity => Diagnostic.Severity;
    /// <summary>
    /// Gets or updates severity label, the bindable or domain state represented by this property.
    /// </summary>
    public string SeverityLabel => Diagnostic.Severity.ToString();
    /// <summary>
    /// Gets or updates code label, the bindable or domain state represented by this property.
    /// </summary>
    public string CodeLabel => string.IsNullOrWhiteSpace(Diagnostic.Code) ? string.Empty : Diagnostic.Code + ": ";
    /// <summary>
    /// Gets or updates message, the bindable or domain state represented by this property.
    /// </summary>
    public string Message => Diagnostic.Message;
    /// <summary>
    /// Gets or updates location, the bindable or domain state represented by this property.
    /// </summary>
    public string Location => $"{Diagnostic.RelativePath}:{Diagnostic.Range.Start.Line + 1}:{Diagnostic.Range.Start.Character + 1}";
    /// <summary>
    /// Gets or updates source, the bindable or domain state represented by this property.
    /// </summary>
    public string Source => Diagnostic.Source ?? "diagnostic";
}

/// <summary>
/// Represents code symbol item view model and keeps its related state and behavior together.
/// </summary>
public sealed record CodeSymbolItemViewModel(CodeSymbol Symbol)
{
    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public string Name => Symbol.Name;
    /// <summary>
    /// Gets or updates kind, the bindable or domain state represented by this property.
    /// </summary>
    public string Kind => Symbol.Kind;
    /// <summary>
    /// Gets or updates location, the bindable or domain state represented by this property.
    /// </summary>
    public string Location => $"{Symbol.RelativePath}:{Symbol.Range.Start.Line + 1}:{Symbol.Range.Start.Character + 1}";
    /// <summary>
    /// Gets or updates source, the bindable or domain state represented by this property.
    /// </summary>
    public string Source => Symbol.Source;
    /// <summary>
    /// Gets or updates container, the bindable or domain state represented by this property.
    /// </summary>
    public string Container => Symbol.ContainerName ?? string.Empty;
}
