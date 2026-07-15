using System.Collections;
using System.Collections.ObjectModel;
using System.Reflection;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Desktop.ViewModels;

public sealed class CrossModeRetrievalViewModel : ObservableObject
{
    private readonly IWorkspaceRetrievalIndexer _indexer;
    private readonly IRetrievalSearchService _search;
    private readonly IContainerRepository _containers;
    private ChatPageViewModel? _chat;
    private RetrievalScope? _scope;
    private string _contextLabel = "Open a Studio project or Teach subject to use cross-mode retrieval.";
    private string _query = string.Empty;
    private string _status = "No project or subject scope is active.";
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

    public event Action<string>? InsertRequested;
    public ObservableCollection<RetrievalCitationViewModel> Results { get; } = [];
    public AsyncRelayCommand IndexCommand { get; }
    public AsyncRelayCommand SearchCommand { get; }
    public RelayCommand InsertCommand { get; }
    public RelayCommand ClearCommand { get; }

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
    public bool ScopeAvailable => _scope is not null;
    public string IndexLabel => _scope?.Kind switch
    {
        RetrievalScopeKind.Project => "Index project",
        RetrievalScopeKind.Subject => "Index subject",
        _ => "Index scope"
    };

    public void SetChat(ChatPageViewModel? chat)
    {
        _chat = chat;
        _scope = chat?.Mode switch
        {
            HavenMode.Studio when chat.SelectedContainer is not null => new RetrievalScope(RetrievalScopeKind.Project, chat.SelectedContainer.Id),
            HavenMode.Teach when chat.SelectedContainer is not null => new RetrievalScope(RetrievalScopeKind.Subject, chat.SelectedContainer.Id),
            _ => null
        };
        ContextLabel = _scope?.Kind switch
        {
            RetrievalScopeKind.Project => $"Studio project: {chat!.SelectedContainer!.Name}",
            RetrievalScopeKind.Subject => $"Teach subject: {chat!.SelectedContainer!.Name}",
            _ => "Open a Studio project or Teach subject to use cross-mode retrieval."
        };
        Status = ScopeAvailable ? "Index the active scope, then search it with citations." : "No project or subject scope is active.";
        ClearResults();
        RaisePropertyChanged(nameof(ScopeAvailable));
        RaisePropertyChanged(nameof(IndexLabel));
        RaiseCommandStates();
    }

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

    private string BuildInsertion()
    {
        var context = string.Join("\n\n", Results.Select(result =>
            $"{result.Number} {result.Title} ({result.Source}, {result.Location})\n{result.Excerpt}"));
        return "\n\nUse this cited context from the active " + (_scope?.Kind == RetrievalScopeKind.Project ? "Studio project" : "Teach subject") +
               ". Cite [source N] and do not infer beyond the cited text.\n\n" + context;
    }

    private void ClearResults()
    {
        Results.Clear();
        InsertCommand.RaiseCanExecuteChanged();
    }

    private async Task<IReadOnlyList<LessonDefinition>> ReadLessonsAsync(Guid subjectId, CancellationToken cancellationToken)
    {
        var method = _containers.GetType().GetMethod(
            "GetLessonsAsync",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: [typeof(Guid), typeof(CancellationToken)],
            modifiers: null)
            ?? typeof(IContainerRepository).GetMethod(
                "GetLessonsAsync",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: [typeof(Guid), typeof(CancellationToken)],
                modifiers: null)
            ?? throw new InvalidOperationException("The container repository does not expose lesson retrieval.");
        var invocation = method.Invoke(_containers, [subjectId, cancellationToken])
                         ?? throw new InvalidOperationException("Lesson retrieval returned no task.");
        if (invocation is not Task task) throw new InvalidOperationException("Lesson retrieval did not return a task.");
        await task.ConfigureAwait(false);
        var result = task.GetType().GetProperty("Result")?.GetValue(task);
        return result is IEnumerable values
            ? values.Cast<object>().OfType<LessonDefinition>().ToArray()
            : [];
    }

    private void RaiseCommandStates()
    {
        IndexCommand.RaiseCanExecuteChanged();
        SearchCommand.RaiseCanExecuteChanged();
        InsertCommand.RaiseCanExecuteChanged();
    }
}
