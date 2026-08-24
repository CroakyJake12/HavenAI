/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/ViewModels/HomePageViewModel.cs, in the Desktop presentation-model layer, exposing bindable state and commands to Avalonia views.
 * What: This file owns HomePageViewModel, ManifestDashboardTileProvider, DashboardTileViewModel, DashboardAgendaItemViewModel, DashboardWorkItemViewModel, BuiltInDashboardTiles, DelegateDashboardTileProvider. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Keeping UI state here makes the XAML declarative and keeps behavior testable without recreating the full window.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.ObjectModel;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Represents home page view model and keeps its related state and behavior together.
/// </summary>
public sealed class HomePageViewModel : ObservableObject, IActivatablePage, IDisposable
{
    /// <summary>
    /// Stores repository locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IDashboardRepository _repository;
    /// <summary>
    /// Stores layout repository locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IDashboardLayoutRepository _layoutRepository;
    /// <summary>
    /// Stores ollama locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IOllamaClient _ollama;
    /// <summary>
    /// Stores catalog locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ICatalogRepository _catalog;
    /// <summary>
    /// Stores actions locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IReadOnlyDictionary<string, Func<Task>> _actions;
    /// <summary>
    /// Stores providers locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IReadOnlyList<IDashboardTileProvider> _providers;
    /// <summary>
    /// Stores timer locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly DispatcherTimer _timer;
    /// <summary>
    /// Stores refresh cancellation locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private CancellationTokenSource? _refreshCancellation;
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _status = "Loading your dashboard…";
    /// <summary>
    /// Stores model status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _modelStatus = "Checking local models…";
    /// <summary>
    /// Stores greeting locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _greeting = "Welcome back";
    /// <summary>
    /// Stores date label locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _dateLabel = string.Empty;
    /// <summary>
    /// Stores last updated locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private DateTimeOffset? _lastUpdated;
    /// <summary>
    /// Stores is busy locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isBusy;
    /// <summary>
    /// Stores is customizing locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isCustomizing;

    public HomePageViewModel(
        IDashboardRepository repository,
        IDashboardLayoutRepository layoutRepository,
        IOllamaClient ollama,
        ICatalogRepository catalog,
        IEnumerable<IDashboardTileProvider> providers,
        IReadOnlyDictionary<string, Func<Task>> actions)
    {
        _repository = repository;
        _layoutRepository = layoutRepository;
        _ollama = ollama;
        _catalog = catalog;
        _actions = actions;
        _providers = BuiltInDashboardTiles.Create().Concat(providers).GroupBy(item => item.Definition.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last()).ToArray();
        RefreshCommand = new AsyncRelayCommand(() => RefreshAsync(CancellationToken.None), () => !IsBusy);
        OpenTileCommand = new AsyncRelayCommand<DashboardTileViewModel>(OpenTileAsync);
        MoveEarlierCommand = new AsyncRelayCommand<DashboardTileViewModel>(item => MoveAsync(item, -1));
        MoveLaterCommand = new AsyncRelayCommand<DashboardTileViewModel>(item => MoveAsync(item, 1));
        ToggleTileCommand = new AsyncRelayCommand<DashboardTileViewModel>(ToggleTileAsync);
        ToggleCustomizeCommand = new RelayCommand(() => IsCustomizing = !IsCustomizing);
        OpenAgendaItemCommand = new AsyncRelayCommand<DashboardAgendaItemViewModel>(item => RunActionAsync(item?.ActionKey));
        OpenRecentItemCommand = new AsyncRelayCommand<DashboardWorkItemViewModel>(item => RunActionAsync(item?.ActionKey));
        _timer = new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background,
            async (_, _) => await RefreshAsync(CancellationToken.None));
        UpdateClock(DateTimeOffset.Now);
    }

    /// <summary>
    /// Gets or updates tiles, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<DashboardTileViewModel> Tiles { get; } = [];
    /// <summary>
    /// Gets or updates hidden tiles, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<DashboardTileViewModel> HiddenTiles { get; } = [];
    /// <summary>
    /// Gets or updates agenda, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<DashboardAgendaItemViewModel> Agenda { get; } = [];
    /// <summary>
    /// Gets or updates recent work, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<DashboardWorkItemViewModel> RecentWork { get; } = [];

    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>
    /// Gets or updates model status, the bindable or domain state represented by this property.
    /// </summary>
    public string ModelStatus { get => _modelStatus; private set => SetProperty(ref _modelStatus, value); }
    /// <summary>
    /// Gets or updates greeting, the bindable or domain state represented by this property.
    /// </summary>
    public string Greeting { get => _greeting; private set => SetProperty(ref _greeting, value); }
    /// <summary>
    /// Gets or updates date label, the bindable or domain state represented by this property.
    /// </summary>
    public string DateLabel { get => _dateLabel; private set => SetProperty(ref _dateLabel, value); }
    /// <summary>
    /// Gets or updates last updated label, the bindable or domain state represented by this property.
    /// </summary>
    public string LastUpdatedLabel => _lastUpdated is null ? "Not refreshed yet" : $"Updated {_lastUpdated.Value.LocalDateTime:t}";
    /// <summary>
    /// Reports whether agenda applies to the current state.
    /// </summary>
    public bool HasAgenda => Agenda.Count > 0;
    /// <summary>
    /// Reports whether recent work applies to the current state.
    /// </summary>
    public bool HasRecentWork => RecentWork.Count > 0;
    /// <summary>
    /// Reports whether hidden tiles applies to the current state.
    /// </summary>
    public bool HasHiddenTiles => HiddenTiles.Count > 0;
    /// <summary>
    /// Reports whether busy applies to the current state.
    /// </summary>
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) RefreshCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Reports whether customizing applies to the current state.
    /// </summary>
    public bool IsCustomizing { get => _isCustomizing; set => SetProperty(ref _isCustomizing, value); }

    /// <summary>
    /// Gets or updates refresh command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand RefreshCommand { get; }
    /// <summary>
    /// Gets or updates open tile command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<DashboardTileViewModel> OpenTileCommand { get; }
    /// <summary>
    /// Gets or updates move earlier command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<DashboardTileViewModel> MoveEarlierCommand { get; }
    /// <summary>
    /// Gets or updates move later command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<DashboardTileViewModel> MoveLaterCommand { get; }
    /// <summary>
    /// Gets or updates toggle tile command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<DashboardTileViewModel> ToggleTileCommand { get; }
    /// <summary>
    /// Gets or updates toggle customize command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand ToggleCustomizeCommand { get; }
    /// <summary>
    /// Gets or updates open agenda item command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<DashboardAgendaItemViewModel> OpenAgendaItemCommand { get; }
    /// <summary>
    /// Gets or updates open recent item command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<DashboardWorkItemViewModel> OpenRecentItemCommand { get; }

    /// <summary>
    /// Performs activate asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task ActivateAsync(CancellationToken cancellationToken)
    {
        _timer.Start();
        await RefreshAsync(cancellationToken);
    }

    /// <summary>
    /// Performs the deactivate step owned by this component.
    /// </summary>
    public void Deactivate() => _timer.Stop();

    /// <summary>
    /// Performs refresh asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        _refreshCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _refreshCancellation.Token;
        IsBusy = true;
        Status = "Refreshing dashboard…";
        try
        {
            var now = DateTimeOffset.Now;
            UpdateClock(now);
            var snapshotTask = _repository.GetSnapshotAsync(now, token);
            var modelTask = _ollama.IsAvailableAsync(token);
            var layoutTask = _layoutRepository.GetAsync(token);
            var manifestProvidersTask = GetManifestProvidersAsync(token);
            await Task.WhenAll(snapshotTask, modelTask, layoutTask, manifestProvidersTask);
            token.ThrowIfCancellationRequested();

            var snapshot = await snapshotTask;
            ModelStatus = await modelTask ? "Local models ready" : "Ollama is not reachable";
            var activeProviders = _providers.Concat(await manifestProvidersTask)
                .GroupBy(item => item.Definition.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First()).ToArray();
            await PopulateTilesAsync(snapshot, await layoutTask, activeProviders, token);
            Replace(Agenda, snapshot.Agenda.Select(item => new DashboardAgendaItemViewModel(item)));
            Replace(RecentWork, snapshot.RecentWork.Select(item => new DashboardWorkItemViewModel(item)));
            RaisePropertyChanged(nameof(HasAgenda));
            RaisePropertyChanged(nameof(HasRecentWork));
            _lastUpdated = DateTimeOffset.Now;
            RaisePropertyChanged(nameof(LastUpdatedLabel));
            Status = snapshot.OverdueTasks > 0 ? $"{snapshot.OverdueTasks} overdue item{(snapshot.OverdueTasks == 1 ? string.Empty : "s")} need attention" : "Everything is up to date";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Status = $"Dashboard refresh failed: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// Performs populate tiles asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task PopulateTilesAsync(DashboardSnapshot snapshot, IReadOnlyList<DashboardTileLayout> stored,
        IReadOnlyList<IDashboardTileProvider> providers, CancellationToken token)
    {
        var layouts = stored.ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);
        var all = new List<DashboardTileViewModel>();
        foreach (var provider in providers)
        {
            var definition = provider.Definition;
            var layout = layouts.GetValueOrDefault(definition.Key) ?? new DashboardTileLayout(1, definition.Key, definition.DefaultOrder, true, definition.DefaultSize);
            var data = await provider.GetDataAsync(snapshot, token);
            all.Add(new DashboardTileViewModel(definition, data, layout.Order, layout.IsVisible, layout.Size));
        }
        Tiles.Clear();
        HiddenTiles.Clear();
        foreach (var tile in all.OrderBy(item => item.Order).ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase))
        {
            if (tile.IsVisible) Tiles.Add(tile); else HiddenTiles.Add(tile);
        }
        RaisePropertyChanged(nameof(HasHiddenTiles));
    }

    /// <summary>
    /// Retrieves manifest providers async for the current operation.
    /// </summary>
    private async Task<IReadOnlyList<IDashboardTileProvider>> GetManifestProvidersAsync(CancellationToken cancellationToken)
    {
        var result = new List<IDashboardTileProvider>();
        foreach (var capability in Array.Empty<CapabilityDefinition>())
        {
            IReadOnlyList<DashboardPluginTileManifest> manifests;
            try
            {
                manifests = System.Text.Json.JsonSerializer.Deserialize<DashboardPluginTileManifest[]>("[]",
                    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true }) ?? [];
            }
            catch (System.Text.Json.JsonException) { continue; }
            var order = 1000;
            foreach (var manifest in manifests)
            {
                if (!TryCreateManifestProvider(capability, manifest, order++, out var provider)) continue;
                result.Add(provider);
            }
        }
        return result;
    }

    /// <summary>
    /// Attempts to create manifest provider and reports the result without using failure for normal control flow.
    /// </summary>
    private static bool TryCreateManifestProvider(CapabilityDefinition capability, DashboardPluginTileManifest manifest, int order,
        out IDashboardTileProvider provider)
    {
        provider = null!;
        if (!DashboardTileManifestPolicy.IsApproved(manifest) ||
            !Enum.TryParse<DashboardTileSize>(manifest.Size, true, out var size)) return false;
        var definition = new DashboardTileDefinition(
            $"capability:{capability.Id:N}:{manifest.Key.Trim()}", manifest.Title.Trim(), manifest.Description.Trim(),
            string.IsNullOrWhiteSpace(manifest.IconKey) ? capability.IconKey : manifest.IconKey.Trim(),
            manifest.ProviderKey, manifest.ActionKey, size, order, false);
        provider = new ManifestDashboardTileProvider(definition);
        return true;
    }

    /// <summary>
    /// Performs open tile asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task OpenTileAsync(DashboardTileViewModel? item) => await RunActionAsync(item?.ActionKey);

    /// <summary>
    /// Runs run action async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    private async Task RunActionAsync(string? actionKey)
    {
        if (string.IsNullOrWhiteSpace(actionKey)) return;
        if (_actions.TryGetValue(actionKey, out var action)) await action();
        else Status = "This dashboard action is not available in the current build.";
    }

    /// <summary>
    /// Performs move asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task MoveAsync(DashboardTileViewModel? item, int offset)
    {
        if (item is null || !item.IsVisible) return;
        var index = Tiles.IndexOf(item);
        var target = Math.Clamp(index + offset, 0, Tiles.Count - 1);
        if (target == index) return;
        Tiles.Move(index, target);
        await SaveLayoutAsync();
    }

    /// <summary>
    /// Performs move to index asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task MoveToIndexAsync(DashboardTileViewModel item, int index)
    {
        var current = Tiles.IndexOf(item);
        if (current < 0) return;
        index = Math.Clamp(index, 0, Tiles.Count - 1);
        if (current == index) return;
        Tiles.Move(current, index);
        await SaveLayoutAsync();
    }

    /// <summary>
    /// Performs toggle tile asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ToggleTileAsync(DashboardTileViewModel? item)
    {
        if (item is null) return;
        item.IsVisible = !item.IsVisible;
        if (item.IsVisible)
        {
            HiddenTiles.Remove(item);
            Tiles.Add(item);
        }
        else
        {
            Tiles.Remove(item);
            HiddenTiles.Add(item);
        }
        RaisePropertyChanged(nameof(HasHiddenTiles));
        await SaveLayoutAsync();
    }

    /// <summary>
    /// Performs save layout asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private Task SaveLayoutAsync()
    {
        var layout = Tiles.Select((item, index) => new DashboardTileLayout(1, item.Key, index, true, item.Size))
            .Concat(HiddenTiles.Select((item, index) => new DashboardTileLayout(1, item.Key, Tiles.Count + index, false, item.Size))).ToArray();
        return _layoutRepository.SaveAsync(layout, CancellationToken.None);
    }

    /// <summary>
    /// Performs the update clock step owned by this component.
    /// </summary>
    private void UpdateClock(DateTimeOffset now)
    {
        Greeting = now.Hour switch { < 12 => "Good morning", < 18 => "Good afternoon", _ => "Good evening" };
        DateLabel = now.ToString("dddd, d MMMM yyyy");
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source) target.Add(item);
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose()
    {
        _timer.Stop();
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
    }
}

/// <summary>
/// Represents manifest dashboard tile provider and keeps its related state and behavior together.
/// </summary>
internal sealed class ManifestDashboardTileProvider(DashboardTileDefinition definition) : IDashboardTileProvider
{
    /// <summary>
    /// Gets or updates definition, the bindable or domain state represented by this property.
    /// </summary>
    public DashboardTileDefinition Definition { get; } = definition;

    /// <summary>
    /// Retrieves data async for the current operation.
    /// </summary>
    public Task<DashboardTileData> GetDataAsync(DashboardSnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var data = Definition.ProviderKey.ToLowerInvariant() switch
        {
            "calls" => new DashboardTileData(snapshot.CallsThisWeek.ToString(), $"{snapshot.CallDurationThisWeek.TotalMinutes:0} minutes this week"),
            "plan" => new DashboardTileData(snapshot.TasksDueToday.ToString(), snapshot.OverdueTasks > 0 ? $"{snapshot.OverdueTasks} overdue" : "Due today", null, snapshot.OverdueTasks > 0),
            "projects" => new DashboardTileData(snapshot.ActiveProjects.ToString(), "Active projects"),
            "study" => new DashboardTileData(snapshot.StudySubjects.ToString(), "Study subjects"),
            "groups" => new DashboardTileData(snapshot.ChatGroups.ToString(), "Chat Groups"),
            "automations" => new DashboardTileData(snapshot.EnabledAutomations.ToString(), "Enabled automations"),
            "conversations" => new DashboardTileData(snapshot.ConversationsToday.ToString(), "Conversations today"),
            _ => new DashboardTileData("Open", "Haven action")
        };
        return Task.FromResult(data);
    }
}

/// <summary>
/// Represents dashboard tile view model and keeps its related state and behavior together.
/// </summary>
public sealed class DashboardTileViewModel : ObservableObject
{
    /// <summary>
    /// Stores is visible locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isVisible;
    public DashboardTileViewModel(DashboardTileDefinition definition, DashboardTileData data, int order, bool isVisible, DashboardTileSize size)
    {
        Definition = definition; Data = data; Order = order; _isVisible = isVisible; Size = size;
    }
    /// <summary>
    /// Gets or updates definition, the bindable or domain state represented by this property.
    /// </summary>
    public DashboardTileDefinition Definition { get; }
    /// <summary>
    /// Gets or updates data, the bindable or domain state represented by this property.
    /// </summary>
    public DashboardTileData Data { get; }
    /// <summary>
    /// Gets or updates key, the bindable or domain state represented by this property.
    /// </summary>
    public string Key => Definition.Key;
    /// <summary>
    /// Gets or updates title, the bindable or domain state represented by this property.
    /// </summary>
    public string Title => Definition.Title;
    /// <summary>
    /// Gets or updates description, the bindable or domain state represented by this property.
    /// </summary>
    public string Description => Definition.Description;
    /// <summary>
    /// Gets or updates icon key, the bindable or domain state represented by this property.
    /// </summary>
    public string IconKey => Definition.IconKey;
    /// <summary>
    /// Gets or updates action key, the bindable or domain state represented by this property.
    /// </summary>
    public string ActionKey => Definition.ActionKey;
    /// <summary>
    /// Gets or updates primary, the bindable or domain state represented by this property.
    /// </summary>
    public string Primary => Data.Primary;
    /// <summary>
    /// Gets or updates secondary, the bindable or domain state represented by this property.
    /// </summary>
    public string Secondary => Data.Secondary;
    /// <summary>
    /// Gets or updates badge, the bindable or domain state represented by this property.
    /// </summary>
    public string? Badge => Data.Badge;
    /// <summary>
    /// Reports whether badge applies to the current state.
    /// </summary>
    public bool HasBadge => !string.IsNullOrWhiteSpace(Badge);
    /// <summary>
    /// Reports whether warning applies to the current state.
    /// </summary>
    public bool HasWarning => Data.HasWarning;
    /// <summary>
    /// Gets or updates order, the bindable or domain state represented by this property.
    /// </summary>
    public int Order { get; }
    /// <summary>
    /// Gets or updates size, the bindable or domain state represented by this property.
    /// </summary>
    public DashboardTileSize Size { get; }
    /// <summary>
    /// Reports whether wide applies to the current state.
    /// </summary>
    public bool IsWide => Size == DashboardTileSize.Wide;
    /// <summary>
    /// Reports whether visible applies to the current state.
    /// </summary>
    public bool IsVisible { get => _isVisible; set => SetProperty(ref _isVisible, value); }
}

/// <summary>
/// Represents dashboard agenda item view model and keeps its related state and behavior together.
/// </summary>
public sealed record DashboardAgendaItemViewModel(DashboardAgendaItem Item)
{
    /// <summary>
    /// Gets or updates title, the bindable or domain state represented by this property.
    /// </summary>
    public string Title => Item.Title;
    /// <summary>
    /// Gets or updates detail, the bindable or domain state represented by this property.
    /// </summary>
    public string Detail => Item.Detail;
    /// <summary>
    /// Gets or updates kind, the bindable or domain state represented by this property.
    /// </summary>
    public string Kind => Item.Kind;
    /// <summary>
    /// Gets or updates action key, the bindable or domain state represented by this property.
    /// </summary>
    public string ActionKey => Item.ActionKey;
    /// <summary>
    /// Reports whether overdue applies to the current state.
    /// </summary>
    public bool IsOverdue => Item.IsOverdue;
    /// <summary>
    /// Gets or updates time label, the bindable or domain state represented by this property.
    /// </summary>
    public string TimeLabel => Item.StartsAt?.LocalDateTime.ToString("t") ?? string.Empty;
}

/// <summary>
/// Represents dashboard work item view model and keeps its related state and behavior together.
/// </summary>
public sealed record DashboardWorkItemViewModel(DashboardWorkItem Item)
{
    /// <summary>
    /// Gets or updates title, the bindable or domain state represented by this property.
    /// </summary>
    public string Title => Item.Title;
    /// <summary>
    /// Gets or updates detail, the bindable or domain state represented by this property.
    /// </summary>
    public string Detail => Item.Detail;
    /// <summary>
    /// Gets or updates icon key, the bindable or domain state represented by this property.
    /// </summary>
    public string IconKey => Item.IconKey;
    /// <summary>
    /// Gets or updates action key, the bindable or domain state represented by this property.
    /// </summary>
    public string ActionKey => Item.ActionKey;
    /// <summary>
    /// Gets or updates updated label, the bindable or domain state represented by this property.
    /// </summary>
    public string UpdatedLabel => Item.UpdatedAt.LocalDateTime.ToString("g");
}

/// <summary>
/// Represents built in dashboard tiles and keeps its related state and behavior together.
/// </summary>
internal static class BuiltInDashboardTiles
{
    /// <summary>
    /// Creates this member with the invariants required by its callers.
    /// </summary>
    public static IReadOnlyList<IDashboardTileProvider> Create() =>
    [
        Tile("new-chat", "New chat", "Start a private local conversation", "chat", "action", "new-chat", 0,
            _ => new("Start", "Saved locally")),
        Tile("call", "Call", "Talk with Haven hands-free", "call", "calls", "call", 1,
            s => new(s.CallsThisWeek.ToString(), $"{s.CallDurationThisWeek.TotalMinutes:0} minutes this week")),
        Tile("plan", "Plan", "Tasks, calendar and AI planning", "plan", "plan", "plan", 2,
            s => new(s.TasksDueToday.ToString(), s.OverdueTasks > 0 ? $"{s.OverdueTasks} overdue" : "Due today", s.OverdueTasks > 0 ? "Needs attention" : null, s.OverdueTasks > 0)),
        Tile("browse", "Browse", "Open the isolated Haven browser", "browse", "action", "browse", 3,
            _ => new("Open", "Private browser workspace")),
        Tile("studio", "Studio", "Continue a local project", "studio", "projects", "studio", 4,
            s => new(s.ActiveProjects.ToString(), "Active projects")),
        Tile("study", "Study", "Continue a subject or lesson", "study", "study", "study", 5,
            s => new(s.StudySubjects.ToString(), "Subjects")),
        Tile("groups", "Chat Groups", "Open a context workspace", "folder", "groups", "chat", 6,
            s => new(s.ChatGroups.ToString(), "Context workspaces")),
        Tile("automations", "Automations", "Review reusable and scheduled workflows", "automation", "automations", "automations", 7,
            s => new(s.EnabledAutomations.ToString(), "Enabled automations"))
    ];

    /// <summary>
    /// Performs the tile step owned by this component.
    /// </summary>
    private static IDashboardTileProvider Tile(string key, string title, string description, string icon, string provider, string action,
        int order, Func<DashboardSnapshot, DashboardTileData> value) =>
        new DelegateDashboardTileProvider(new DashboardTileDefinition(key, title, description, icon, provider, action, DashboardTileSize.Standard, order), value);

    /// <summary>
    /// Represents delegate dashboard tile provider and keeps its related state and behavior together.
    /// </summary>
    private sealed class DelegateDashboardTileProvider(DashboardTileDefinition definition, Func<DashboardSnapshot, DashboardTileData> value) : IDashboardTileProvider
    {
        /// <summary>
        /// Gets or updates definition, the bindable or domain state represented by this property.
        /// </summary>
        public DashboardTileDefinition Definition { get; } = definition;
        /// <summary>
        /// Retrieves data async for the current operation.
        /// </summary>
        public Task<DashboardTileData> GetDataAsync(DashboardSnapshot snapshot, CancellationToken cancellationToken) => Task.FromResult(value(snapshot));
    }
}
