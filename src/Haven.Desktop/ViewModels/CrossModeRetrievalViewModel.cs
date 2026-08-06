/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/ViewModels/CrossModeRetrievalViewModel.cs, in the Desktop presentation-model layer, exposing bindable state and commands to Avalonia views.
 * What: This file owns CrossModeRetrievalViewModel. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Keeping UI state here makes the XAML declarative and keeps behavior testable without recreating the full window.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.ObjectModel;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Represents cross mode retrieval view model and keeps its related state and behavior together.
/// </summary>
public sealed class CrossModeRetrievalViewModel : ObservableObject
{
    /// <summary>
    /// Stores indexer locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IWorkspaceRetrievalIndexer _indexer;
    /// <summary>
    /// Stores search locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IRetrievalSearchService _search;
    /// <summary>
    /// Stores containers locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IContainerRepository _containers;
    /// <summary>
    /// Stores chat locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private ChatPageViewModel? _chat;
    /// <summary>
    /// Stores scope locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private RetrievalScope? _scope;
    /// <summary>
    /// Stores context label locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _contextLabel = "Open a Studio project or Study subject to use cross-mode retrieval.";
    /// <summary>
    /// Stores query locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _query = string.Empty;
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _status = "No project or subject scope is active.";
    /// <summary>
    /// Stores is busy locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isBusy;

    public CrossModeRetrievalViewModel(
        IRetrievalSearchService search,
        IContainerRepository containers,
        RetrievalIndexService retrievalIndex)
    {
        _search = search;
        _containers = containers;
        _indexer = new WorkspaceRetrievalIndexer(retrievalIndex);
        IndexCommand = new AsyncRelayCommand(IndexAsync, () => ScopeAvailable && !IsBusy);
        SearchCommand = new AsyncRelayCommand(SearchAsync, () => ScopeAvailable && !string.IsNullOrWhiteSpace(Query) && !IsBusy);
        InsertCommand = new RelayCommand(() => InsertRequested?.Invoke(BuildInsertion()), () => Results.Count > 0 && !IsBusy);
        ClearCommand = new RelayCommand(ClearResults);
    }

    /// <summary>
    /// Stores insert requested locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event Action<string>? InsertRequested;
    /// <summary>
    /// Gets or updates results, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<RetrievalCitationViewModel> Results { get; } = [];
    /// <summary>
    /// Gets or updates index command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand IndexCommand { get; }
    /// <summary>
    /// Gets or updates search command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand SearchCommand { get; }
    /// <summary>
    /// Gets or updates insert command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand InsertCommand { get; }
    /// <summary>
    /// Gets or updates clear command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand ClearCommand { get; }

    /// <summary>
    /// Gets or updates context label, the bindable or domain state represented by this property.
    /// </summary>
    public string ContextLabel { get => _contextLabel; private set => SetProperty(ref _contextLabel, value); }
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
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
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
    /// Gets or updates scope available, the bindable or domain state represented by this property.
    /// </summary>
    public bool ScopeAvailable => _scope is not null;
    /// <summary>
    /// Gets or updates index label, the bindable or domain state represented by this property.
    /// </summary>
    public string IndexLabel => _scope?.Kind switch
    {
        RetrievalScopeKind.Project => "Index project",
        RetrievalScopeKind.Subject => "Index subject",
        _ => "Index scope"
    };

    /// <summary>
    /// Performs the set chat step owned by this component.
    /// </summary>
    public void SetChat(ChatPageViewModel? chat)
    {
        _chat = chat;
        _scope = chat?.Mode switch
        {
            HavenMode.Studio when chat.SelectedContainer is not null => new RetrievalScope(RetrievalScopeKind.Project, chat.SelectedContainer.Id),
            HavenMode.Study when chat.SelectedContainer is not null => new RetrievalScope(RetrievalScopeKind.Subject, chat.SelectedContainer.Id),
            _ => null
        };
        ContextLabel = _scope?.Kind switch
        {
            RetrievalScopeKind.Project => $"Studio project: {chat!.SelectedContainer!.Name}",
            RetrievalScopeKind.Subject => $"Study subject: {chat!.SelectedContainer!.Name}",
            _ => "Open a Studio project or Study subject to use cross-mode retrieval."
        };
        Status = ScopeAvailable ? "Index the active scope, then search it with citations." : "No project or subject scope is active.";
        ClearResults();
        RaisePropertyChanged(nameof(ScopeAvailable));
        RaisePropertyChanged(nameof(IndexLabel));
        RaiseCommandStates();
    }

    /// <summary>
    /// Performs index asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task IndexAsync()
    {
        if (_chat?.SelectedContainer is null || _scope is null) return;
        try
        {
            IsBusy = true;
            RetrievalIndexReport report;
            if (_scope.Kind == RetrievalScopeKind.Project)
            {
                var root = _chat.SelectedContainer.Definition.RootPath;
                if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("The selected Studio project has no local root folder.");
                report = await _indexer.IndexProjectAsync(_scope.Id, root, CancellationToken.None);
            }
            else
            {
                var lessons = await ReadLessonsAsync(_scope.Id, CancellationToken.None);
                report = await _indexer.IndexSubjectAsync(_chat.SelectedContainer.Definition, lessons, CancellationToken.None);
            }
            var notices = report.Notices.Count == 0 ? string.Empty : " " + string.Join(" ", report.Notices.Take(3));
            Status = $"Indexed {report.Indexed}, unchanged {report.Unchanged}, removed {report.Removed}, skipped {report.Skipped}.{notices}";
        }
        catch (Exception ex)
        {
            Status = "Indexing failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Performs search asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task SearchAsync()
    {
        if (_scope is null) return;
        try
        {
            IsBusy = true;
            var result = await _search.SearchAsync(new RetrievalQuery(
                Query.Trim(), [_scope], MaximumResults: 10, TokenBudget: 3_500), CancellationToken.None);
            Results.Clear();
            foreach (var citation in result.Citations) Results.Add(new RetrievalCitationViewModel(citation));
            InsertCommand.RaiseCanExecuteChanged();
            Status = Results.Count == 0
                ? "No indexed matches in the active scope."
                : $"Selected {Results.Count} cited chunk{(Results.Count == 1 ? string.Empty : "s")} using about {result.EstimatedTokens:N0} tokens. {result.Method}";
        }
        catch (Exception ex)
        {
            Status = "Scoped retrieval failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Builds insertion from the currently available inputs.
    /// </summary>
    private string BuildInsertion()
    {
        var context = string.Join("\n\n", Results.Select(result =>
            $"{result.Number} {result.Title} ({result.Source}, {result.Location})\n{result.Excerpt}"));
        return "\n\nUse this cited context from the active " + (_scope?.Kind == RetrievalScopeKind.Project ? "Studio project" : "Study subject") +
               ". Cite [source N] and do not infer beyond the cited text.\n\n" + context;
    }

    /// <summary>
    /// Performs the clear results step owned by this component.
    /// </summary>
    private void ClearResults()
    {
        Results.Clear();
        InsertCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Performs read lessons asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private Task<IReadOnlyList<Lesson>> ReadLessonsAsync(Guid subjectId, CancellationToken cancellationToken) =>
        _containers.GetLessonsAsync(subjectId, cancellationToken);

    /// <summary>
    /// Performs the raise command states step owned by this component.
    /// </summary>
    private void RaiseCommandStates()
    {
        IndexCommand.RaiseCanExecuteChanged();
        SearchCommand.RaiseCanExecuteChanged();
        InsertCommand.RaiseCanExecuteChanged();
    }
}
