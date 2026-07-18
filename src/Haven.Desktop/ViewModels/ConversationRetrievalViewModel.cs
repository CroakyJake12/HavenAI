/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/ViewModels/ConversationRetrievalViewModel.cs, in the Desktop presentation-model layer, exposing bindable state and commands to Avalonia views.
 * What: This file owns ConversationRetrievalViewModel, RetrievalCitationViewModel. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Keeping UI state here makes the XAML declarative and keeps behavior testable without recreating the full window.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.ObjectModel;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Represents conversation retrieval view model and keeps its related state and behavior together.
/// </summary>
public sealed class ConversationRetrievalViewModel : ObservableObject
{
    /// <summary>
    /// Stores retrieval locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IRetrievalSearchService _retrieval;
    /// <summary>
    /// Stores conversation id locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private Guid _conversationId;
    /// <summary>
    /// Stores query locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _query = string.Empty;
    /// <summary>
    /// Stores context locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _context = string.Empty;
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _status = "Search indexed messages and extracted attachments in this conversation.";
    /// <summary>
    /// Stores method locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _method = string.Empty;
    /// <summary>
    /// Stores token budget locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int _tokenBudget = 3_000;
    /// <summary>
    /// Stores is busy locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isBusy;

    public ConversationRetrievalViewModel(IRetrievalSearchService retrieval)
    {
        _retrieval = retrieval;
        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !string.IsNullOrWhiteSpace(Query) && !IsBusy);
        ClearCommand = new RelayCommand(Clear);
        InsertCommand = new RelayCommand(() => InsertRequested?.Invoke(BuildInsertion()), () => Citations.Count > 0 && !IsBusy);
    }

    /// <summary>
    /// Stores insert requested locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event Action<string>? InsertRequested;
    /// <summary>
    /// Gets or updates citations, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<RetrievalCitationViewModel> Citations { get; } = [];
    /// <summary>
    /// Gets or updates search command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand SearchCommand { get; }
    /// <summary>
    /// Gets or updates clear command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand ClearCommand { get; }
    /// <summary>
    /// Gets or updates insert command, the bindable or domain state represented by this property.
    /// </summary>
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
    /// <summary>
    /// Gets or updates context, the bindable or domain state represented by this property.
    /// </summary>
    public string Context { get => _context; private set => SetProperty(ref _context, value); }
    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>
    /// Gets or updates method, the bindable or domain state represented by this property.
    /// </summary>
    public string Method { get => _method; private set => SetProperty(ref _method, value); }
    /// <summary>
    /// Gets or updates token budget, the bindable or domain state represented by this property.
    /// </summary>
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
    /// <summary>
    /// Reports whether results applies to the current state.
    /// </summary>
    public bool HasResults => Citations.Count > 0;

    /// <summary>
    /// Performs the load step owned by this component.
    /// </summary>
    public void Load(Guid conversationId)
    {
        if (_conversationId == conversationId) return;
        _conversationId = conversationId;
        Clear();
    }

    /// <summary>
    /// Performs search asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Builds insertion from the currently available inputs.
    /// </summary>
    private string BuildInsertion() =>
        "\n\nUse the following retrieved context only where relevant. Cite sources using [source N]. If the context does not answer the request, say so rather than guessing.\n\n" + Context;

    /// <summary>
    /// Performs the clear step owned by this component.
    /// </summary>
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

/// <summary>
/// Represents retrieval citation view model and keeps its related state and behavior together.
/// </summary>
public sealed record RetrievalCitationViewModel(RetrievalCitation Citation)
{
    /// <summary>
    /// Gets or updates number, the bindable or domain state represented by this property.
    /// </summary>
    public string Number => $"[{Citation.Number}]";
    /// <summary>
    /// Gets or updates title, the bindable or domain state represented by this property.
    /// </summary>
    public string Title => Citation.Title;
    /// <summary>
    /// Gets or updates source, the bindable or domain state represented by this property.
    /// </summary>
    public string Source => $"{Citation.SourceType}:{Citation.SourceId}";
    /// <summary>
    /// Gets or updates score, the bindable or domain state represented by this property.
    /// </summary>
    public string Score => Citation.Score.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
    /// <summary>
    /// Gets or updates excerpt, the bindable or domain state represented by this property.
    /// </summary>
    public string Excerpt => Citation.Excerpt;
    /// <summary>
    /// Gets or updates location, the bindable or domain state represented by this property.
    /// </summary>
    public string Location => $"Characters {Citation.StartCharacter}-{Citation.StartCharacter + Citation.Length}";
}
