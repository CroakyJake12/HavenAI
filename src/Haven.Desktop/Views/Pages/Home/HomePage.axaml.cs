using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Events;

namespace Haven.Desktop.Views.Pages.Home;

/// <summary>
/// Home dashboard page. Loads tiles, agenda, and recent work directly from repositories.
/// All pointer events are wired through the HavenEventBus.
/// </summary>
public sealed partial class HomePage : UserControl
{
    private readonly HavenEventBus _bus;
    private readonly IDashboardRepository _dashboard;
    private readonly IDashboardLayoutRepository _layout;
    private readonly IOllamaClient _ollama;
    private readonly ICatalogRepository _catalog;
    private readonly IReadOnlyList<IDashboardTileProvider> _providers;
    private readonly IReadOnlyDictionary<string, Func<Task>> _actions;

    private readonly DispatcherTimer _timer;
    private CancellationTokenSource? _refreshCancellation;
    private readonly List<TileEntry> _tiles = [];
    private readonly List<TileEntry> _hiddenTiles = [];
    private bool _isCustomizing;
    private DateTimeOffset? _lastUpdated;
    private bool _isRefreshing;
    private bool _refreshQueued;

    public HomePage(
        HavenEventBus bus,
        IDashboardRepository dashboard,
        IDashboardLayoutRepository layout,
        IOllamaClient ollama,
        ICatalogRepository catalog,
        IEnumerable<IDashboardTileProvider> providers,
        IReadOnlyDictionary<string, Func<Task>> actions)
    {
        _bus = bus;
        _dashboard = dashboard;
        _layout = layout;
        _ollama = ollama;
        _catalog = catalog;
        _providers = BuiltInDashboardTiles.Create()
            .Concat(providers)
            .GroupBy(p => p.Definition.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToArray();
        _actions = actions;

        InitializeComponent();
        WireEvents();

        _timer = new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background,
            async (_, _) => await RefreshAsync(CancellationToken.None));
    }

    public async Task ActivateAsync(CancellationToken ct)
    {
        _timer.Start();
        await RefreshAsync(ct);
    }

    public void Deactivate()
    {
        _timer.Stop();
        _refreshCancellation?.Cancel();
    }

    private void WireEvents()
    {
        _bus.RegisterElement("Home.Header.CustomizeClick", CustomizeButton);
        _bus.WirePointerEvents("Home.Header.CustomizeClick", CustomizeButton);
        CustomizeButton.Click += (_, _) =>
        {
            _isCustomizing = !_isCustomizing;
            CustomizePanel.IsVisible = _isCustomizing;
            _bus.Fire("Home.Header.CustomizeClick");
        };

        _bus.RegisterElement("Home.Header.RefreshClick", RefreshButton);
        _bus.WirePointerEvents("Home.Header.RefreshClick", RefreshButton);
        RefreshButton.Click += async (_, _) =>
        {
            await RefreshAsync(CancellationToken.None);
            _bus.Fire("Home.Header.RefreshClick");
        };
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        if (_isRefreshing)
        {
            _refreshQueued = true;
            return;
        }

        _isRefreshing = true;
        if (_refreshCancellation is { } existing) existing.Cancel();
        _refreshCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _refreshCancellation.Token;

        try
        {
            StatusText.Text = "Loading your dashboard\u2026";

            var snapshot = await _dashboard.GetSnapshotAsync(DateTimeOffset.UtcNow, token);
            var modelReady = await _ollama.IsAvailableAsync(token);
            var layout = await _layout.GetAsync(token);
            var manifestProviders = await GetManifestProvidersAsync(token);

            var allProviders = _providers.Concat(manifestProviders)
                .GroupBy(p => p.Definition.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Last())
                .OrderBy(p => p.Definition.DefaultOrder)
                .ToArray();

            await PopulateTilesAsync(allProviders, snapshot, layout, token);
            PopulateAgenda(snapshot.Agenda);
            PopulateRecentWork(snapshot.RecentWork);

            UpdateClock(snapshot.CapturedAt);
            ModelStatusText.Text = modelReady ? "Local models ready" : "Ollama is not reachable";
            _lastUpdated = DateTimeOffset.Now;
            LastUpdatedText.Text = $"Updated {_lastUpdated:HH:mm}";

            var overdue = snapshot.OverdueTasks;
            StatusText.Text = overdue > 0
                ? $"{overdue} overdue item{(overdue == 1 ? "" : "s")} need attention"
                : "Everything is up to date";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusText.Text = "Refresh failed";
            System.Diagnostics.Debug.WriteLine($"[Home] Refresh failed: {ex.Message}");
        }
        finally
        {
            _isRefreshing = false;
            if (_refreshQueued)
            {
                _refreshQueued = false;
                _ = RefreshAsync(CancellationToken.None);
            }
        }
    }

    private async Task PopulateTilesAsync(
        IReadOnlyList<IDashboardTileProvider> providers,
        DashboardSnapshot snapshot,
        IReadOnlyList<DashboardTileLayout> layout,
        CancellationToken ct)
    {
        _tiles.Clear();
        _hiddenTiles.Clear();

        var layoutMap = layout.ToDictionary(l => l.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var provider in providers)
        {
            var def = provider.Definition;
            DashboardTileData data;
            try
            {
                data = await provider.GetDataAsync(snapshot, ct);
            }
            catch
            {
                data = new DashboardTileData("--", "--");
            }

            bool isVisible = !layoutMap.TryGetValue(def.Key, out var lay) || lay.IsVisible;
            int order = layoutMap.TryGetValue(def.Key, out var o) ? o.Order : def.DefaultOrder;

            var entry = new TileEntry(def, data, order);
            if (isVisible)
                _tiles.Add(entry);
            else
                _hiddenTiles.Add(entry);
        }

        _tiles.Sort((a, b) => a.Order.CompareTo(b.Order));
        _hiddenTiles.Sort((a, b) => a.Order.CompareTo(b.Order));

        UiBatcher.RebuildChildren(TilesPanel, panel =>
        {
            for (int i = 0; i < _tiles.Count; i++)
                panel.Children.Add(CreateTileBorder(_tiles[i], i));
        });
        UiBatcher.RebuildChildren(HiddenTilesPanel, panel =>
        {
            for (int i = 0; i < _hiddenTiles.Count; i++)
                panel.Children.Add(CreateHiddenTileChip(_hiddenTiles[i]));
        });

        NoHiddenTilesText.IsVisible = _hiddenTiles.Count == 0;
    }

    private Border CreateTileBorder(TileEntry entry, int index)
    {
        var def = entry.Definition;
        var data = entry.Data;

        var icon = new HavenIcon { IconKey = def.IconKey, Width = 20, Height = 20 };
        var titleBlock = new TextBlock { Text = def.Title, FontWeight = Avalonia.Media.FontWeight.SemiBold, FontSize = 16, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };

        var badgeBorder = new Border { Classes = { "warningPill" }, IsVisible = data.Badge is not null };
        if (data.Badge is not null)
            badgeBorder.Child = new TextBlock { Text = data.Badge, FontSize = 10 };

        var iconRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
        iconRow.Children.Add(new Border { Classes = { "iconPlate" }, Child = icon });
        Grid.SetColumn(titleBlock, 1);
        iconRow.Children.Add(titleBlock);
        Grid.SetColumn(badgeBorder, 2);
        iconRow.Children.Add(badgeBorder);

        var primaryText = new TextBlock { Text = data.Primary, FontSize = 28, FontWeight = Avalonia.Media.FontWeight.SemiBold };
        var secondaryText = new TextBlock { Text = data.Secondary, Classes = { "muted" } };
        var descriptionText = new TextBlock { Text = def.Description, Classes = { "muted2" }, FontSize = 11 };

        var valuesStack = new StackPanel { Spacing = 3 };
        valuesStack.Children.Add(primaryText);
        valuesStack.Children.Add(secondaryText);
        valuesStack.Children.Add(descriptionText);

        var openButton = new Button { Classes = { "subtle" }, Content = "Open" };
        var moveEarlier = new Button { Classes = { "icon", "compact" }, Content = new HavenIcon { IconKey = "chevron-left", Width = 14, Height = 14 } };
        ToolTip.SetTip(moveEarlier, "Move earlier");
        var moveLaterIcon = new HavenIcon { IconKey = "chevron-left", Width = 14, Height = 14 };
        moveLaterIcon.RenderTransform = new Avalonia.Media.RotateTransform(180);
        var moveLater = new Button { Classes = { "icon", "compact" }, Content = moveLaterIcon };
        ToolTip.SetTip(moveLater, "Move later");
        var hideButton = new Button { Classes = { "icon", "compact" }, Content = new HavenIcon { IconKey = "close", Width = 13, Height = 13 } };
        ToolTip.SetTip(hideButton, "Hide tile");

        var buttonRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), ColumnSpacing = 4 };
        buttonRow.Children.Add(openButton);
        Grid.SetColumn(moveEarlier, 1);
        buttonRow.Children.Add(moveEarlier);
        Grid.SetColumn(moveLater, 2);
        buttonRow.Children.Add(moveLater);
        Grid.SetColumn(hideButton, 3);
        buttonRow.Children.Add(hideButton);

        var contentGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 10 };
        contentGrid.Children.Add(iconRow);
        Grid.SetRow(valuesStack, 1);
        contentGrid.Children.Add(valuesStack);
        Grid.SetRow(buttonRow, 2);
        contentGrid.Children.Add(buttonRow);

        var border = new Border
        {
            Classes = { "dashboardTile" },
            Width = 270,
            MinHeight = 150,
            Margin = new Avalonia.Thickness(0, 0, 12, 12),
            Tag = entry,
            Child = contentGrid
        };

        var qName = $"Home.Dashboard.Tile{index}";
        border.RegisterWithEvents(qName, _bus);

        openButton.Click += async (_, _) =>
        {
            _bus.Fire($"{qName}.Open");
            await RunActionAsync(def.ActionKey);
        };
        moveEarlier.Click += async (_, _) =>
        {
            _bus.Fire($"{qName}.MoveEarlier");
            await MoveAsync(entry, -1);
        };
        moveLater.Click += async (_, _) =>
        {
            _bus.Fire($"{qName}.MoveLater");
            await MoveAsync(entry, 1);
        };
        hideButton.Click += async (_, _) =>
        {
            _bus.Fire($"{qName}.Toggle");
            await ToggleTileAsync(entry);
        };

        return border;
    }

    private Button CreateHiddenTileChip(TileEntry entry)
    {
        var def = entry.Definition;
        var chip = new Button
        {
            Classes = { "chip" },
            Margin = new Avalonia.Thickness(0, 0, 6, 6),
            Content = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new HavenIcon { IconKey = def.IconKey, Width = 14, Height = 14 },
                    new TextBlock { Text = def.Title }
                }
            }
        };
        chip.Click += async (_, _) => await ToggleTileAsync(entry);
        return chip;
    }

    private void PopulateAgenda(IReadOnlyList<DashboardAgendaItem> agenda)
    {
        UiBatcher.RebuildChildren(AgendaPanel, panel =>
        {
            foreach (var item in agenda)
            {
                var dot = new Border { Classes = { "agendaDot" } };
                var titleBlock = new TextBlock { Text = item.Title, FontWeight = Avalonia.Media.FontWeight.SemiBold };
                var detailBlock = new TextBlock { Text = item.Detail, Classes = { "muted" }, FontSize = 11 };
                var stack = new StackPanel { Children = { titleBlock, detailBlock } };
                var timeLabel = new TextBlock { Text = FormatTimeLabel(item.StartsAt, item.IsOverdue), Classes = { "muted" } };

                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
                grid.Children.Add(dot);
                Grid.SetColumn(stack, 1);
                grid.Children.Add(stack);
                Grid.SetColumn(timeLabel, 2);
                grid.Children.Add(timeLabel);

                var button = new Button { Classes = { "sidebar" }, Content = grid };
                var qName = $"Home.Agenda.Item{panel.Children.Count}";
                button.RegisterWithEvents(qName, _bus);
                button.Click += async (_, _) =>
                {
                    _bus.Fire($"{qName}.Click");
                    await RunActionAsync(item.ActionKey);
                };

                panel.Children.Add(button);
            }
        });
        NoAgendaText.IsVisible = agenda.Count == 0;
    }

    private void PopulateRecentWork(IReadOnlyList<DashboardWorkItem> recentWork)
    {
        UiBatcher.RebuildChildren(RecentWorkPanel, panel =>
        {
            foreach (var item in recentWork)
            {
                var icon = new HavenIcon { IconKey = item.IconKey, Width = 18, Height = 18 };
                var titleBlock = new TextBlock { Text = item.Title, FontWeight = Avalonia.Media.FontWeight.SemiBold };
                var detailBlock = new TextBlock { Text = item.Detail, Classes = { "muted" }, FontSize = 11 };
                var stack = new StackPanel { Children = { titleBlock, detailBlock } };
                var updatedLabel = new TextBlock { Text = FormatUpdated(item.UpdatedAt), Classes = { "muted2" }, FontSize = 10 };

                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
                grid.Children.Add(icon);
                Grid.SetColumn(stack, 1);
                grid.Children.Add(stack);
                Grid.SetColumn(updatedLabel, 2);
                grid.Children.Add(updatedLabel);

                var button = new Button { Classes = { "sidebar" }, Content = grid };
                var qName = $"Home.RecentWork.Item{panel.Children.Count}";
                button.RegisterWithEvents(qName, _bus);
                button.Click += async (_, _) =>
                {
                    _bus.Fire($"{qName}.Click");
                    await RunActionAsync(item.ActionKey);
                };

                panel.Children.Add(button);
            }
        });
        NoRecentWorkText.IsVisible = recentWork.Count == 0;
    }

    private async Task MoveAsync(TileEntry entry, int offset)
    {
        var list = _tiles;
        var index = list.IndexOf(entry);
        if (index < 0) return;
        var newIndex = Math.Clamp(index + offset, 0, list.Count - 1);
        if (newIndex == index) return;
        list.RemoveAt(index);
        list.Insert(newIndex, entry);
        await RebuildTilesAsync();
        await SaveLayoutAsync();
    }

    private async Task ToggleTileAsync(TileEntry entry)
    {
        if (_tiles.Remove(entry))
        {
            _hiddenTiles.Add(entry);
            _hiddenTiles.Sort((a, b) => a.Order.CompareTo(b.Order));
        }
        else if (_hiddenTiles.Remove(entry))
        {
            _tiles.Add(entry);
            _tiles.Sort((a, b) => a.Order.CompareTo(b.Order));
        }
        await RebuildTilesAsync();
        await SaveLayoutAsync();
    }

    private async Task RebuildTilesAsync()
    {
        UiBatcher.RebuildChildren(TilesPanel, panel =>
        {
            for (int i = 0; i < _tiles.Count; i++)
                panel.Children.Add(CreateTileBorder(_tiles[i], i));
        });
        UiBatcher.RebuildChildren(HiddenTilesPanel, panel =>
        {
            for (int i = 0; i < _hiddenTiles.Count; i++)
                panel.Children.Add(CreateHiddenTileChip(_hiddenTiles[i]));
        });

        NoHiddenTilesText.IsVisible = _hiddenTiles.Count == 0;
    }

    private async Task SaveLayoutAsync()
    {
        var layout = _tiles.Concat(_hiddenTiles)
            .Select((entry, index) => new DashboardTileLayout(
                1,
                entry.Definition.Key,
                index,
                _tiles.Contains(entry),
                DashboardTileSize.Standard))
            .ToArray();
        await _layout.SaveAsync(layout, CancellationToken.None);
    }

    private async Task RunActionAsync(string? actionKey)
    {
        if (string.IsNullOrWhiteSpace(actionKey)) return;
        if (_actions.TryGetValue(actionKey, out var action))
            await action();
    }

    private async Task<IReadOnlyList<IDashboardTileProvider>> GetManifestProvidersAsync(CancellationToken ct)
    {
        var result = new List<IDashboardTileProvider>();
        try
        {
            var plugins = await _catalog.GetPluginsAsync(ct);
            foreach (var plugin in plugins)
            {
                if (string.IsNullOrWhiteSpace(plugin.DashboardTilesJson)) continue;
                try
                {
                    var manifests = System.Text.Json.JsonSerializer.Deserialize<List<DashboardPluginTileManifest>>(
                        plugin.DashboardTilesJson,
                        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
                    if (manifests is null) continue;
                    foreach (var manifest in manifests)
                    {
                        if (!DashboardTileManifestPolicy.IsApproved(manifest)) continue;
                        result.Add(new ManifestTileProvider(manifest));
                    }
                }
                catch { }
            }
        }
        catch { }
        return result;
    }

    private void UpdateClock(DateTimeOffset now)
    {
        var hour = now.Hour;
        GreetingText.Text = hour switch
        {
            < 12 => "Good morning",
            < 18 => "Good afternoon",
            _ => "Good evening"
        };
        DateText.Text = now.ToString("dddd, d MMMM yyyy");
    }

    private static string FormatTimeLabel(DateTimeOffset? startsAt, bool isOverdue)
    {
        if (isOverdue) return "Overdue";
        if (startsAt is null) return "";
        var diff = startsAt.Value - DateTimeOffset.UtcNow;
        if (diff.TotalMinutes < 0) return "Now";
        if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes}m";
        if (diff.TotalDays < 1) return $"{(int)diff.TotalHours}h";
        return $"{(int)diff.TotalDays}d";
    }

    private static string FormatUpdated(DateTimeOffset updatedAt)
    {
        var diff = DateTimeOffset.UtcNow - updatedAt;
        if (diff.TotalMinutes < 1) return "Just now";
        if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalDays < 1) return $"{(int)diff.TotalHours}h ago";
        return $"{(int)diff.TotalDays}d ago";
    }

    private sealed class TileEntry(DashboardTileDefinition definition, DashboardTileData data, int order)
    {
        public DashboardTileDefinition Definition { get; } = definition;
        public DashboardTileData Data { get; } = data;
        public int Order { get; set; } = order;
    }

    private sealed class ManifestTileProvider(DashboardPluginTileManifest manifest) : IDashboardTileProvider
    {
        public DashboardTileDefinition Definition { get; } = new(
            manifest.Key, manifest.Title, manifest.Description, manifest.IconKey,
            manifest.ProviderKey, manifest.ActionKey);

        public Task<DashboardTileData> GetDataAsync(DashboardSnapshot snapshot, CancellationToken ct)
        {
            var primary = manifest.ProviderKey.ToLowerInvariant() switch
            {
                "calls" => snapshot.CallsThisWeek.ToString(),
                "plan" => snapshot.TasksDueToday.ToString(),
                "projects" => snapshot.ActiveProjects.ToString(),
                "teaching" => snapshot.TeachingSubjects.ToString(),
                "groups" => snapshot.ChatGroups.ToString(),
                "automations" => snapshot.EnabledAutomations.ToString(),
                "conversations" => snapshot.ConversationsToday.ToString(),
                _ => "--"
            };
            var secondary = manifest.ProviderKey.ToLowerInvariant() switch
            {
                "calls" => $"{(int)snapshot.CallDurationThisWeek.TotalMinutes} minutes",
                "plan" => snapshot.OverdueTasks > 0 ? $"{snapshot.OverdueTasks} overdue" : "Due today",
                _ => manifest.Title
            };
            return Task.FromResult(new DashboardTileData(primary, secondary));
        }
    }

    private static class BuiltInDashboardTiles
    {
        public static IReadOnlyList<IDashboardTileProvider> Create() => new IDashboardTileProvider[]
        {
            new DelegateTileProvider(new("new-chat", "New chat", "Start a conversation", "chat", "action", "new-chat", DefaultOrder: 0),
                _ => Task.FromResult(new DashboardTileData("Start", "Saved locally"))),
            new DelegateTileProvider(new("call", "Call", "Voice call with Haven", "call", "calls", "call", DefaultOrder: 1),
                s => Task.FromResult(new DashboardTileData(s.CallsThisWeek.ToString(), $"{(int)s.CallDurationThisWeek.TotalMinutes} minutes"))),
            new DelegateTileProvider(new("plan", "Plan", "Tasks and schedule", "calendar", "plan", "plan", DefaultOrder: 2),
                s => Task.FromResult(new DashboardTileData(s.TasksDueToday.ToString(), s.OverdueTasks > 0 ? $"{s.OverdueTasks} overdue" : "Due today"))),
            new DelegateTileProvider(new("browse", "Browse", "Private browser workspace", "globe", "action", "browse", DefaultOrder: 3),
                _ => Task.FromResult(new DashboardTileData("Open", "Private browser workspace"))),
            new DelegateTileProvider(new("studio", "Studio", "Project workspace", "code", "projects", "studio", DefaultOrder: 4),
                s => Task.FromResult(new DashboardTileData(s.ActiveProjects.ToString(), "Active projects"))),
            new DelegateTileProvider(new("teaching", "Teaching", "Learning subjects", "book", "teaching", "teach", DefaultOrder: 5),
                s => Task.FromResult(new DashboardTileData(s.TeachingSubjects.ToString(), "Subjects"))),
            new DelegateTileProvider(new("groups", "Chat Groups", "Context workspaces", "folder", "groups", "chat", DefaultOrder: 6),
                s => Task.FromResult(new DashboardTileData(s.ChatGroups.ToString(), "Context workspaces"))),
            new DelegateTileProvider(new("automations", "Scheduled Actions", "Automated tasks", "zap", "automations", "automations", DefaultOrder: 7),
                s => Task.FromResult(new DashboardTileData(s.EnabledAutomations.ToString(), "Enabled automations")))
        };
    }

    private sealed class DelegateTileProvider(DashboardTileDefinition def, Func<DashboardSnapshot, Task<DashboardTileData>> dataFunc) : IDashboardTileProvider
    {
        public DashboardTileDefinition Definition => def;
        public Task<DashboardTileData> GetDataAsync(DashboardSnapshot snapshot, CancellationToken ct) => dataFunc(snapshot);
    }
}
