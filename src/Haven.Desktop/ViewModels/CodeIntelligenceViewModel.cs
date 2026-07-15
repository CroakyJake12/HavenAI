using System.Collections.ObjectModel;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

public sealed class CodeIntelligenceViewModel : ObservableObject
{
    private readonly ICodeIntelligenceService _intelligence;
    private ChatPageViewModel? _chat;
    private string _workspaceRoot = string.Empty;
    private string _relativePath = string.Empty;
    private string _symbolQuery = string.Empty;
    private string _status = "Open a Studio project to use code intelligence.";
    private string _serverStatus = string.Empty;
    private string _formatDiff = string.Empty;
    private string _formatter = string.Empty;
    private bool _isBusy;
    private bool _insertSpaces = true;
    private int _tabSize = 4;
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

    public event Action<string>? InsertRequested;

    public ObservableCollection<CodeDiagnosticItemViewModel> Diagnostics { get; } = [];
    public ObservableCollection<CodeSymbolItemViewModel> Symbols { get; } = [];
    public AsyncRelayCommand InspectCommand { get; }
    public AsyncRelayCommand DiagnosticsCommand { get; }
    public AsyncRelayCommand SearchSymbolsCommand { get; }
    public AsyncRelayCommand PreviewFormatCommand { get; }
    public AsyncRelayCommand ApplyFormatCommand { get; }
    public RelayCommand InsertDiagnosticsCommand { get; }
    public RelayCommand InsertSymbolsCommand { get; }
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

    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string ServerStatus { get => _serverStatus; private set => SetProperty(ref _serverStatus, value); }
    public string FormatDiff { get => _formatDiff; private set { if (SetProperty(ref _formatDiff, value)) RaisePropertyChanged(nameof(HasFormatPreview)); } }
    public string Formatter { get => _formatter; private set => SetProperty(ref _formatter, value); }
    public bool HasFormatPreview => !string.IsNullOrWhiteSpace(FormatDiff);
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
    public bool InsertSpaces { get => _insertSpaces; set => SetProperty(ref _insertSpaces, value); }
    public int TabSize { get => _tabSize; set => SetProperty(ref _tabSize, Math.Clamp(value, 1, 16)); }

    public void SetChat(ChatPageViewModel? chat)
    {
        _chat = chat;
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

    private void InsertDiagnostics()
    {
        var text = string.Join("\n", Diagnostics.Take(100).Select(item =>
            $"- {item.Severity}: {item.Location} {item.CodeLabel}{item.Message}"));
        InsertRequested?.Invoke("\n\nReview and fix these project diagnostics. Inspect the relevant files before proposing changes, preserve unrelated edits, and use the reviewed transactional change-set flow:\n" + text);
    }

    private void InsertSymbols()
    {
        var text = string.Join("\n", Symbols.Take(100).Select(item => $"- {item.Kind} {item.Name} — {item.Location}"));
        InsertRequested?.Invoke("\n\nUse these code-intelligence symbol results as navigation hints. Read the source before editing and do not assume lexical fallback results are semantically complete:\n" + text);
    }

    private async Task ExecuteAsync(Func<Task> operation)
    {
        try
        {
            IsBusy = true;
            await operation();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            Status = "Code intelligence failed: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanUseFile() => HasWorkspace && !string.IsNullOrWhiteSpace(RelativePath) && !IsBusy;

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

    private void RaiseDiagnosticState()
    {
        RaisePropertyChanged(nameof(Diagnostics));
        InsertDiagnosticsCommand.RaiseCanExecuteChanged();
    }

    private void RaiseSymbolState()
    {
        RaisePropertyChanged(nameof(Symbols));
        InsertSymbolsCommand.RaiseCanExecuteChanged();
    }

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

public sealed record CodeDiagnosticItemViewModel(CodeDiagnostic Diagnostic)
{
    public CodeDiagnosticSeverity Severity => Diagnostic.Severity;
    public string SeverityLabel => Diagnostic.Severity.ToString();
    public string CodeLabel => string.IsNullOrWhiteSpace(Diagnostic.Code) ? string.Empty : Diagnostic.Code + ": ";
    public string Message => Diagnostic.Message;
    public string Location => $"{Diagnostic.RelativePath}:{Diagnostic.Range.Start.Line + 1}:{Diagnostic.Range.Start.Character + 1}";
    public string Source => Diagnostic.Source ?? "diagnostic";
}

public sealed record CodeSymbolItemViewModel(CodeSymbol Symbol)
{
    public string Name => Symbol.Name;
    public string Kind => Symbol.Kind;
    public string Location => $"{Symbol.RelativePath}:{Symbol.Range.Start.Line + 1}:{Symbol.Range.Start.Character + 1}";
    public string Source => Symbol.Source;
    public string Container => Symbol.ContainerName ?? string.Empty;
}
