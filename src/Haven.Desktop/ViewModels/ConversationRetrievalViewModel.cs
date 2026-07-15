using System.Collections.ObjectModel;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

public sealed class ConversationRetrievalViewModel : ObservableObject
{
    private readonly IRetrievalSearchService _retrieval;
    private Guid _conversationId;
    private string _query = string.Empty;
    private string _context = string.Empty;
    private string _status = "Search indexed messages and extracted attachments in this conversation.";
    private string _method = string.Empty;
    private int _tokenBudget = 3_000;
    private bool _isBusy;

    public ConversationRetrievalViewModel(IRetrievalSearchService retrieval)
    {
        _retrieval = retrieval;
        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !string.IsNullOrWhiteSpace(Query) && !IsBusy);
        ClearCommand = new RelayCommand(Clear);
        InsertCommand = new RelayCommand(() => InsertRequested?.Invoke(BuildInsertion()), () => Citations.Count > 0 && !IsBusy);
    }

    public event Action<string>? InsertRequested;
    public ObservableCollection<RetrievalCitationViewModel> Citations { get; } = [];
    public AsyncRelayCommand SearchCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand InsertCommand { get; }

    public string Query
    {
        get => _query;
        set
        {
            if (!SetProperty(ref _query, value)) return;
            SearchCommand.RaiseCanExecuteChanged();
        }
    }
    public string Context { get => _context; private set => SetProperty(ref _context, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string Method { get => _method; private set => SetProperty(ref _method, value); }
    public int TokenBudget { get => _tokenBudget; set => SetProperty(ref _tokenBudget, Math.Clamp(value, 128, 16_000)); }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            SearchCommand.RaiseCanExecuteChanged();
            InsertCommand.RaiseCanExecuteChanged();
        }
    }
    public bool HasResults => Citations.Count > 0;

    public void Load(Guid conversationId)
    {
        if (_conversationId == conversationId) return;
        _conversationId = conversationId;
        Clear();
    }

    private async Task SearchAsync()
    {
        if (_conversationId == Guid.Empty) return;
        try
        {
            IsBusy = true;
            var result = await _retrieval.SearchAsync(new RetrievalQuery(
                Query.Trim(),
                [new RetrievalScope(RetrievalScopeKind.Conversation, _conversationId)],
                MaximumResults: 10,
                TokenBudget: TokenBudget), CancellationToken.None);
            Context = result.Context;
            Method = result.Method;
            Citations.Clear();
            foreach (var citation in result.Citations) Citations.Add(new RetrievalCitationViewModel(citation));
            RaisePropertyChanged(nameof(HasResults));
            InsertCommand.RaiseCanExecuteChanged();
            Status = Citations.Count == 0
                ? result.Method
                : $"Selected {Citations.Count} cited chunk{(Citations.Count == 1 ? string.Empty : "s")} using about {result.EstimatedTokens:N0} tokens.";
        }
        catch (Exception ex)
        {
            Status = "Retrieval failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string BuildInsertion() =>
        "\n\nUse the following retrieved context only where relevant. Cite sources using [source N]. If the context does not answer the request, say so rather than guessing.\n\n" + Context;

    private void Clear()
    {
        Query = string.Empty;
        Context = string.Empty;
        Method = string.Empty;
        Citations.Clear();
        RaisePropertyChanged(nameof(HasResults));
        InsertCommand.RaiseCanExecuteChanged();
        Status = "Search indexed messages and extracted attachments in this conversation.";
    }
}

public sealed record RetrievalCitationViewModel(RetrievalCitation Citation)
{
    public string Number => $"[{Citation.Number}]";
    public string Title => Citation.Title;
    public string Source => $"{Citation.SourceType}:{Citation.SourceId}";
    public string Score => Citation.Score.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
    public string Excerpt => Citation.Excerpt;
    public string Location => $"Characters {Citation.StartCharacter}-{Citation.StartCharacter + Citation.Length}";
}
