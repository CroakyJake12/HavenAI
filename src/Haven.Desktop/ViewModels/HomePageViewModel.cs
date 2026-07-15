using System.Collections.ObjectModel;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

public sealed class HomePageViewModel : ObservableObject, IActivatablePage, IDisposable
{
    private readonly IDashboardRepository _repository;
    private readonly IDashboardLayoutRepository _layoutRepository;
    private readonly IOllamaClient _ollama;
    private readonly ICatalogRepository _catalog;
    private readonly IReadOnlyDictionary<string, Func<Task>> _actions;
    private readonly IReadOnlyList<IDashboardTileProvider> _providers;
    private readonly DispatcherTimer _timer;
    private CancellationTokenSource? _refreshCancellation;
    private string _status = "Loading your dashboard…";
    private string _modelStatus = "Checking local models…";
    private string _greeting = "Welcome back";
    private string _dateLabel = string.Empty;
    private DateTimeOffset? _lastUpdated;
    private bool _isBusy;
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

    public ObservableCollection<DashboardTileViewModel> Tiles { get; } = [];
    public ObservableCollection<DashboardTileViewModel> HiddenTiles { get; } = [];
    public ObservableCollection<DashboardAgendaItemViewModel> Agenda { get; } = [];
    public ObservableCollection<DashboardWorkItemViewModel> RecentWork { get; } = [];

    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string ModelStatus { get => _modelStatus; private set => SetProperty(ref _modelStatus, value); }
    public string Greeting { get => _greeting; private set => SetProperty(ref _greeting, value); }
    public string DateLabel { get => _dateLabel; private set => SetProperty(ref _dateLabel, value); }
    public string LastUpdatedLabel => _lastUpdated is null ? "Not refreshed yet" : $"Updated {_lastUpdated.Value.LocalDateTime:t}";
    public bool HasAgenda => Agenda.Count > 0;
    public bool HasRecentWork => RecentWork.Count > 0;
    public bool HasHiddenTiles => HiddenTiles.Count > 0;
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) RefreshCommand.RaiseCanExecuteChanged(); } }
    public bool IsCustomizing { get => _isCustomizing; set => SetProperty(ref _isCustomizing, value); }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand<DashboardTileViewModel> OpenTileCommand { get; }
    public AsyncRelayCommand<DashboardTileViewModel> MoveEarlierCommand { get; }
    public AsyncRelayCommand<DashboardTileViewModel> MoveLaterCommand { get; }
    public AsyncRelayCommand<DashboardTileViewModel> ToggleTileCommand { get; }
    public RelayCommand ToggleCustomizeCommand { get; }
    public AsyncRelayCommand<DashboardAgendaItemViewModel> OpenAgendaItemCommand { get; }
    public AsyncRelayCommand<DashboardWorkItemViewModel> OpenRecentItemCommand { get; }

    public async Task ActivateAsync(CancellationToken cancellationToken)
    {
        _timer.Start();
        await RefreshAsync(cancellationToken);
    }

    public void Deactivate() => _timer.Stop();

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

    private async Task<IReadOnlyList<IDashboardTileProvider>> GetManifestProvidersAsync(CancellationToken cancellationToken)
    {
        var result = new List<IDashboardTileProvider>();
        foreach (var plugin in await _catalog.GetPluginsAsync(cancellationToken))
        {
            IReadOnlyList<DashboardPluginTileManifest> manifests;
            try
            {
                manifests = System.Text.Json.JsonSerializer.Deserialize<DashboardPluginTileManifest[]>(plugin.DashboardTilesJson,
                    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true }) ?? [];
            }
            catch (System.Text.Json.JsonException) { continue; }
            var order = 1000;
            foreach (var manifest in manifests)
            {
                if (!TryCreateManifestProvider(plugin, manifest, order++, out var provider)) continue;
                result.Add(provider);
            }
        }
        return result;
    }

    private static bool TryCreateManifestProvider(PluginDefinition plugin, DashboardPluginTileManifest manifest, int order,
        out IDashboardTileProvider provider)
    {
        provider = null!;
        if (!DashboardTileManifestPolicy.IsApproved(manifest) ||
            !Enum.TryParse<DashboardTileSize>(manifest.Size, true, out var size)) return false;
        var definition = new DashboardTileDefinition(
            $"plugin:{plugin.Id:N}:{manifest.Key.Trim()}", manifest.Title.Trim(), manifest.Description.Trim(),
            string.IsNullOrWhiteSpace(manifest.IconKey) ? plugin.IconKey : manifest.IconKey.Trim(),
            manifest.ProviderKey, manifest.ActionKey, size, order, false);
        provider = new ManifestDashboardTileProvider(definition);
        return true;
    }

    private async Task OpenTileAsync(DashboardTileViewModel? item) => await RunActionAsync(item?.ActionKey);

    private async Task RunActionAsync(string? actionKey)
    {
        if (string.IsNullOrWhiteSpace(actionKey)) return;
        if (_actions.TryGetValue(actionKey, out var action)) await action();
        else Status = "This dashboard action is not available in the current build.";
    }

    private async Task MoveAsync(DashboardTileViewModel? item, int offset)
    {
        if (item is null || !item.IsVisible) return;
        var index = Tiles.IndexOf(item);
        var target = Math.Clamp(index + offset, 0, Tiles.Count - 1);
        if (target == index) return;
        Tiles.Move(index, target);
        await SaveLayoutAsync();
    }

    public async Task MoveToIndexAsync(DashboardTileViewModel item, int index)
    {
        var current = Tiles.IndexOf(item);
        if (current < 0) return;
        index = Math.Clamp(index, 0, Tiles.Count - 1);
        if (current == index) return;
        Tiles.Move(current, index);
        await SaveLayoutAsync();
    }

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

    private Task SaveLayoutAsync()
    {
        var layout = Tiles.Select((item, index) => new DashboardTileLayout(1, item.Key, index, true, item.Size))
            .Concat(HiddenTiles.Select((item, index) => new DashboardTileLayout(1, item.Key, Tiles.Count + index, false, item.Size))).ToArray();
        return _layoutRepository.SaveAsync(layout, CancellationToken.None);
    }

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

    public void Dispose()
    {
        _timer.Stop();
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
    }
}

internal sealed class ManifestDashboardTileProvider(DashboardTileDefinition definition) : IDashboardTileProvider
{
    public DashboardTileDefinition Definition { get; } = definition;

    public Task<DashboardTileData> GetDataAsync(DashboardSnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var data = Definition.ProviderKey.ToLowerInvariant() switch
        {
            "calls" => new DashboardTileData(snapshot.CallsThisWeek.ToString(), $"{snapshot.CallDurationThisWeek.TotalMinutes:0} minutes this week"),
            "plan" => new DashboardTileData(snapshot.TasksDueToday.ToString(), snapshot.OverdueTasks > 0 ? $"{snapshot.OverdueTasks} overdue" : "Due today", null, snapshot.OverdueTasks > 0),
            "projects" => new DashboardTileData(snapshot.ActiveProjects.ToString(), "Active projects"),
            "teaching" => new DashboardTileData(snapshot.TeachingSubjects.ToString(), "Teaching subjects"),
            "groups" => new DashboardTileData(snapshot.ChatGroups.ToString(), "Chat Groups"),
            "automations" => new DashboardTileData(snapshot.EnabledAutomations.ToString(), "Enabled automations"),
            "conversations" => new DashboardTileData(snapshot.ConversationsToday.ToString(), "Conversations today"),
            _ => new DashboardTileData("Open", "Haven action")
        };
        return Task.FromResult(data);
    }
}

public sealed class DashboardTileViewModel : ObservableObject
{
    private bool _isVisible;
    public DashboardTileViewModel(DashboardTileDefinition definition, DashboardTileData data, int order, bool isVisible, DashboardTileSize size)
    {
        Definition = definition; Data = data; Order = order; _isVisible = isVisible; Size = size;
    }
    public DashboardTileDefinition Definition { get; }
    public DashboardTileData Data { get; }
    public string Key => Definition.Key;
    public string Title => Definition.Title;
    public string Description => Definition.Description;
    public string IconKey => Definition.IconKey;
    public string ActionKey => Definition.ActionKey;
    public string Primary => Data.Primary;
    public string Secondary => Data.Secondary;
    public string? Badge => Data.Badge;
    public bool HasBadge => !string.IsNullOrWhiteSpace(Badge);
    public bool HasWarning => Data.HasWarning;
    public int Order { get; }
    public DashboardTileSize Size { get; }
    public bool IsWide => Size == DashboardTileSize.Wide;
    public bool IsVisible { get => _isVisible; set => SetProperty(ref _isVisible, value); }
}

public sealed record DashboardAgendaItemViewModel(DashboardAgendaItem Item)
{
    public string Title => Item.Title;
    public string Detail => Item.Detail;
    public string Kind => Item.Kind;
    public string ActionKey => Item.ActionKey;
    public bool IsOverdue => Item.IsOverdue;
    public string TimeLabel => Item.StartsAt?.LocalDateTime.ToString("t") ?? string.Empty;
}

public sealed record DashboardWorkItemViewModel(DashboardWorkItem Item)
{
    public string Title => Item.Title;
    public string Detail => Item.Detail;
    public string IconKey => Item.IconKey;
    public string ActionKey => Item.ActionKey;
    public string UpdatedLabel => Item.UpdatedAt.LocalDateTime.ToString("g");
}

internal static class BuiltInDashboardTiles
{
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
        Tile("teaching", "Teaching", "Continue a subject or lesson", "teach", "teaching", "teach", 5,
            s => new(s.TeachingSubjects.ToString(), "Subjects")),
        Tile("groups", "Chat Groups", "Open a context workspace", "folder", "groups", "chat", 6,
            s => new(s.ChatGroups.ToString(), "Context workspaces")),
        Tile("automations", "Scheduled Actions", "Review enabled local jobs", "automation", "automations", "automations", 7,
            s => new(s.EnabledAutomations.ToString(), "Enabled automations"))
    ];

    private static IDashboardTileProvider Tile(string key, string title, string description, string icon, string provider, string action,
        int order, Func<DashboardSnapshot, DashboardTileData> value) =>
        new DelegateDashboardTileProvider(new DashboardTileDefinition(key, title, description, icon, provider, action, DashboardTileSize.Standard, order), value);

    private sealed class DelegateDashboardTileProvider(DashboardTileDefinition definition, Func<DashboardSnapshot, DashboardTileData> value) : IDashboardTileProvider
    {
        public DashboardTileDefinition Definition { get; } = definition;
        public Task<DashboardTileData> GetDataAsync(DashboardSnapshot snapshot, CancellationToken cancellationToken) => Task.FromResult(value(snapshot));
    }
}
