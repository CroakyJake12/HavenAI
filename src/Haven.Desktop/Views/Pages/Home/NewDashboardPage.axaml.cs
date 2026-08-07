using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Events;

namespace Haven.Desktop.Views.Pages.Home;

/// <summary>
/// Mockup-native New Haven dashboard. The pinned shelf comes from the pin store;
/// every later row is derived from recent mode usage and conversation activity.
/// </summary>
public sealed partial class NewDashboardPage : UserControl, IDisposable
{
    private const string PageStateKey = "dashboard.pages.v1";
    private const string HomePageId = "home";
    private readonly HavenEventBus _bus;
    private readonly IModeRegistry _modes;
    private readonly IModeUsageRepository _usage;
    private readonly IPinRepository _pins;
    private readonly IConversationRepository _conversations;
    private readonly IVersionedSettingsStore _settings;
    private readonly DispatcherTimer _clock;
    private IReadOnlyList<ModeDefinition> _cachedModes = [];
    private IReadOnlyList<ModePin> _cachedPins = [];
    private IReadOnlyList<ModeUsage> _cachedUsage = [];
    private IReadOnlyList<Conversation> _cachedConversations = [];
    private List<DashboardPageProfile> _pages = [DefaultPage()];
    private string _selectedPageId = HomePageId;
    private bool _pageStateLoaded;
    private bool _disposed;

    public NewDashboardPage(
        HavenEventBus bus,
        IModeRegistry modes,
        IModeUsageRepository usage,
        IPinRepository pins,
        IConversationRepository conversations,
        IVersionedSettingsStore settings)
    {
        _bus = bus;
        _modes = modes;
        _usage = usage;
        _pins = pins;
        _conversations = conversations;
        _settings = settings;
        InitializeComponent();

        _clock = new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background, (_, _) => RefreshClock());
        EditPinnedButton.Click += (_, _) => ManageAppsRequested?.Invoke(this, EventArgs.Empty);
        EditWithHavenButton.Click += (_, _) => EditWithHavenRequested?.Invoke(this, EventArgs.Empty);
        AddPageButton.Click += (_, _) => ShowPageEditor(null);
        ConfigurePageButton.Click += (_, _) => ShowPageEditor(SelectedPage);
        Register("Dashboard.Pinned.Edit", EditPinnedButton);
        Register("Dashboard.EditWithHaven", EditWithHavenButton);
        Register("Dashboard.Pages.Add", AddPageButton);
        Register("Dashboard.Pages.Configure", ConfigurePageButton);
        RefreshClock();
    }

    public event EventHandler<ModeDefinition>? ModeRequested;
    public event EventHandler<Conversation>? ConversationRequested;
    public event EventHandler? ManageAppsRequested;
    public event EventHandler? EditWithHavenRequested;

    public async Task ActivateAsync(CancellationToken cancellationToken)
    {
        if (_disposed) return;
        _clock.Start();
        await EnsurePageStateLoadedAsync(cancellationToken);
        await RefreshAsync(cancellationToken);
    }

    public void Deactivate() => _clock.Stop();

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var modesTask = _modes.GetModesAsync(cancellationToken);
        var pinsTask = _pins.GetPinsAsync(cancellationToken);
        var usageTask = _usage.GetRecentUsageAsync(90, cancellationToken);
        var conversationsTask = _conversations.GetRecentAsync(null, 120, cancellationToken);
        await Task.WhenAll(modesTask, pinsTask, usageTask, conversationsTask);

        _cachedModes = (await modesTask).Where(mode => mode.IsEnabled).ToArray();
        _cachedPins = await pinsTask;
        _cachedUsage = await usageTask;
        _cachedConversations = (await conversationsTask).Where(item => !item.IsArchived).ToArray();
        RenderPage();
    }

    private void RenderPage()
    {
        RebuildPageTabs();
        var page = SelectedPage;
        var modeById = _cachedModes.ToDictionary(mode => mode.Id);
        var visibleModeIds = page.IncludeAllPinned
            ? _cachedPins.OrderBy(pin => pin.SortOrder).Select(pin => pin.ModeId).ToHashSet()
            : page.ModeIds.ToHashSet();

        PinnedPanel.Children.Clear();
        foreach (var modeId in page.IncludeAllPinned
                     ? _cachedPins.OrderBy(pin => pin.SortOrder).Select(pin => pin.ModeId)
                     : page.ModeIds)
        {
            if (modeById.TryGetValue(modeId, out var mode))
                PinnedPanel.Children.Add(BuildModeCard(mode, page.IncludeAllPinned ? "Pinned app" : $"On {page.Title}"));
        }
        PinnedEmptyText.IsVisible = PinnedPanel.Children.Count == 0;
        PinnedEmptyText.Text = page.IncludeAllPinned
            ? "Pin apps from the App launcher to keep them here."
            : "Use Edit page to choose the Apps shown on this dashboard page.";

        var usageByMode = _cachedUsage
            .GroupBy(item => item.ModeId)
            .ToDictionary(group => group.Key, group => group.Sum(item =>
                item.TurnCount * 4d + item.CompletionCount * 6d + Math.Min(item.TotalDuration.TotalMinutes, 240d) / 4d));
        var recentByBaseMode = _cachedConversations
            .GroupBy(item => item.Mode)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.UpdatedAt).ToArray());

        var ranked = _cachedModes
            .Where(mode => page.IncludeAllPinned || visibleModeIds.Contains(mode.Id))
            .Select(mode => new
            {
                Mode = mode,
                Score = usageByMode.GetValueOrDefault(mode.Id)
                        + (ShowsBaseModeHistory(mode)
                            ? recentByBaseMode.GetValueOrDefault(mode.BaseMode, []).Length * 3d
                            : 0d)
            })
            .Where(item => item.Score > 0 || visibleModeIds.Contains(item.Mode.Id))
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Mode.Name, StringComparer.OrdinalIgnoreCase)
            .Take(page.IncludeAllPinned ? 4 : 8)
            .ToArray();

        DynamicRowsPanel.Children.Clear();
        foreach (var item in ranked)
        {
            var row = new StackPanel { Spacing = 12 };
            row.Children.Add(new TextBlock
            {
                Text = $"Continue with {item.Mode.Name}",
                FontSize = 16,
                FontWeight = FontWeight.ExtraBold
            });

            var cards = new WrapPanel { Orientation = Orientation.Horizontal, ItemWidth = 220, ItemHeight = 148 };
            cards.Children.Add(BuildModeCard(item.Mode, $"{Math.Round(item.Score):0} recent activity points"));
            if (ShowsBaseModeHistory(item.Mode))
            {
                foreach (var conversation in recentByBaseMode.GetValueOrDefault(item.Mode.BaseMode, []).Take(4))
                    cards.Children.Add(BuildConversationCard(conversation, item.Mode));
            }
            row.Children.Add(cards);
            DynamicRowsPanel.Children.Add(row);
        }

        if (ranked.Length == 0)
        {
            DynamicRowsPanel.Children.Add(new TextBlock
            {
                Text = page.IncludeAllPinned
                    ? "Your personalised rows will appear as you use Haven."
                    : "Choose Apps for this page to build a focused dashboard.",
                Classes = { "muted" },
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 28)
            });
        }

    }

    private DashboardPageProfile SelectedPage =>
        _pages.FirstOrDefault(page => page.Id.Equals(_selectedPageId, StringComparison.OrdinalIgnoreCase))
        ?? _pages[0];

    private static DashboardPageProfile DefaultPage() =>
        new(HomePageId, "Home", [], IncludeAllPinned: true, Order: 0);

    private async Task EnsurePageStateLoadedAsync(CancellationToken cancellationToken)
    {
        if (_pageStateLoaded) return;
        var stored = await _settings.GetAsync<DashboardPageState>(PageStateKey, cancellationToken);
        if (stored is { Version: 1, Pages.Count: > 0 })
        {
            var valid = stored.Pages
                .Where(page => !string.IsNullOrWhiteSpace(page.Id)
                               && !string.IsNullOrWhiteSpace(page.Title)
                               && page.Title.Trim().Length <= 60)
                .GroupBy(page => page.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last() with
                {
                    Title = group.Last().Title.Trim(),
                    ModeIds = group.Last().ModeIds.Distinct().ToList()
                })
                .OrderBy(page => page.Order)
                .ToList();
            if (valid.Count > 0) _pages = valid;
            if (_pages.All(page => !page.Id.Equals(HomePageId, StringComparison.OrdinalIgnoreCase)))
                _pages.Insert(0, DefaultPage());
            _selectedPageId = _pages.Any(page => page.Id.Equals(stored.SelectedPageId, StringComparison.OrdinalIgnoreCase))
                ? stored.SelectedPageId
                : _pages[0].Id;
        }
        _pageStateLoaded = true;
    }

    private Task SavePageStateAsync(CancellationToken cancellationToken)
    {
        var normalized = _pages.Select((page, index) => page with
        {
            Title = page.Title.Trim(),
            ModeIds = page.ModeIds.Distinct().ToList(),
            Order = index
        }).ToList();
        _pages = normalized;
        return _settings.SetAsync(PageStateKey,
            new DashboardPageState(1, _selectedPageId, normalized), cancellationToken);
    }

    private void RebuildPageTabs()
    {
        PageTabsPanel.Children.Clear();
        foreach (var page in _pages.OrderBy(page => page.Order))
        {
            var selected = page.Id.Equals(_selectedPageId, StringComparison.OrdinalIgnoreCase);
            var button = new Button
            {
                Content = page.Title,
                MinWidth = 82,
                Height = 38,
                Padding = new Thickness(15, 7),
                CornerRadius = new CornerRadius(14),
                FontWeight = selected ? FontWeight.ExtraBold : FontWeight.Bold,
                Background = selected
                    ? ResourceBrush("HavenAccentSoftBrush", Color.Parse("#FFDDF7F5"))
                    : Brushes.Transparent
            };
            button.Classes.Add("sidebar");
            button.Click += async (_, _) =>
            {
                if (_disposed || page.Id.Equals(_selectedPageId, StringComparison.OrdinalIgnoreCase)) return;
                _selectedPageId = page.Id;
                await SavePageStateAsync(CancellationToken.None);
                RenderPage();
            };
            PageTabsPanel.Children.Add(button);
        }
        ConfigurePageButton.IsEnabled = _pages.Count > 0;
    }

    private void ShowPageEditor(DashboardPageProfile? existing)
    {
        var isNew = existing is null;
        var source = existing ?? new DashboardPageProfile(
            Guid.NewGuid().ToString("N"), "New page", [], IncludeAllPinned: false, _pages.Count);
        var selectedModes = source.ModeIds.ToHashSet();
        var titleBox = new TextBox
        {
            Text = source.Title,
            PlaceholderText = "Page name",
            MaxLength = 60
        };
        var includePinned = new CheckBox
        {
            Content = "Follow my globally pinned Apps",
            IsChecked = source.IncludeAllPinned,
            Margin = new Thickness(2, 4)
        };
        var choices = new StackPanel { Spacing = 3 };
        foreach (var mode in _cachedModes.OrderBy(mode => mode.Name, StringComparer.OrdinalIgnoreCase))
        {
            var option = new CheckBox
            {
                Content = mode.Name,
                IsChecked = selectedModes.Contains(mode.Id),
                IsEnabled = includePinned.IsChecked != true,
                Margin = new Thickness(3, 2),
                Tag = mode.Id
            };
            option.IsCheckedChanged += (_, _) =>
            {
                if (option.Tag is not Guid modeId) return;
                if (option.IsChecked == true) selectedModes.Add(modeId);
                else selectedModes.Remove(modeId);
            };
            choices.Children.Add(option);
        }
        includePinned.IsCheckedChanged += (_, _) =>
        {
            foreach (var option in choices.Children.OfType<CheckBox>())
                option.IsEnabled = includePinned.IsChecked != true;
        };

        var save = new Button
        {
            Content = isNew ? "Create page" : "Save page",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 8, 0, 0)
        };
        save.Classes.Add("primary");
        var remove = new Button
        {
            Content = "Delete page",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsVisible = !isNew && !source.Id.Equals(HomePageId, StringComparison.OrdinalIgnoreCase)
        };
        remove.Classes.Add("danger");
        var editor = new StackPanel
        {
            Width = 430,
            Spacing = 8,
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock
                {
                    Text = isNew ? "Create dashboard page" : "Edit dashboard page",
                    FontSize = 21,
                    FontWeight = FontWeight.ExtraBold
                },
                new TextBlock
                {
                    Text = "Give this page a purpose, then choose the Apps it should keep close.",
                    Classes = { "muted" },
                    TextWrapping = TextWrapping.Wrap
                },
                titleBox,
                includePinned,
                new TextBlock { Text = "Apps on this page", FontWeight = FontWeight.ExtraBold, Margin = new Thickness(2, 7, 2, 0) },
                new ScrollViewer
                {
                    MaxHeight = 235,
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    Content = choices
                },
                save,
                remove
            }
        };
        var flyout = new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            FlyoutPresenterTheme = Avalonia.Application.Current?.TryFindResource(
                "HavenFloatingFlyoutPresenterTheme", out var presenterTheme) == true
                    ? presenterTheme as ControlTheme
                    : null,
            Content = new Border
            {
                Background = ResourceBrush("HavenElevatedBrush", Colors.White),
                BorderBrush = ResourceBrush("HavenLineBrush", Color.FromArgb(32, 0, 0, 0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(22),
                Child = editor
            }
        };
        save.Click += async (_, _) =>
        {
            var title = titleBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                titleBox.Focus();
                return;
            }
            var updated = source with
            {
                Title = title,
                IncludeAllPinned = includePinned.IsChecked == true,
                ModeIds = selectedModes.ToList()
            };
            if (isNew) _pages.Add(updated);
            else _pages[_pages.FindIndex(page => page.Id.Equals(source.Id, StringComparison.OrdinalIgnoreCase))] = updated;
            _selectedPageId = updated.Id;
            await SavePageStateAsync(CancellationToken.None);
            RenderPage();
            flyout.Hide();
        };
        var deleteArmed = false;
        remove.Click += async (_, _) =>
        {
            if (!deleteArmed)
            {
                deleteArmed = true;
                remove.Content = "Confirm delete";
                return;
            }
            _pages.RemoveAll(page => page.Id.Equals(source.Id, StringComparison.OrdinalIgnoreCase));
            _selectedPageId = _pages[0].Id;
            await SavePageStateAsync(CancellationToken.None);
            RenderPage();
            flyout.Hide();
        };
        flyout.ShowAt(isNew ? AddPageButton : ConfigurePageButton);
        titleBox.SelectAll();
        titleBox.Focus();
    }

    private static bool ShowsBaseModeHistory(ModeDefinition mode) =>
        mode.Key.Equals(mode.BaseMode switch
        {
            HavenMode.Chat => "chat",
            HavenMode.Study => "study",
            HavenMode.Tasks => "tasks",
            HavenMode.Studio => "studio",
            _ => string.Empty
        }, StringComparison.OrdinalIgnoreCase);

    private DashboardCard BuildModeCard(ModeDefinition mode, string detail)
    {
        var card = new DashboardCard
        {
            IconKey = mode.IconKey,
            TitleText = mode.Name,
            DetailText = detail
        };
        card.Click += (_, _) =>
        {
            _bus.Fire($"Dashboard.Mode.{mode.Key}.Click");
            ModeRequested?.Invoke(this, mode);
        };
        return card;
    }

    private DashboardCard BuildConversationCard(Conversation conversation, ModeDefinition mode)
    {
        var detail = conversation.UpdatedAt.ToLocalTime().ToString("ddd d MMM, HH:mm");
        var card = new DashboardCard
        {
            IconKey = "chat",
            TitleText = conversation.Title,
            DetailText = detail
        };
        card.Click += (_, _) =>
        {
            _bus.Fire("Dashboard.Conversation.Click");
            ConversationRequested?.Invoke(this, conversation);
        };
        ToolTip.SetTip(card, $"Open in {mode.Name}");
        return card;
    }

    private void RefreshClock()
    {
        var now = DateTimeOffset.Now;
        WelcomeText.Text = now.Hour < 12 ? "Good morning" : now.Hour < 18 ? "Good afternoon" : "Good evening";
        DateText.Text = $"{now:h:mmtt} · {now:dddd d MMMM}";
    }

    private void Register(string name, Control control)
    {
        _bus.RegisterElement(name, control);
        _bus.WirePointerEvents(name, control);
    }

    private static IBrush ResourceBrush(string key, Color fallback) =>
        Avalonia.Application.Current?.TryFindResource(key, out var value) == true && value is IBrush brush
            ? brush
            : new SolidColorBrush(fallback);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _clock.Stop();
    }
}
