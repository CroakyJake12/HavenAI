using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Events;

namespace Haven.Desktop.Views.Pages.WorkspaceHome;

public sealed partial class WorkspaceHomePage : UserControl
{
    private readonly HavenEventBus _bus;
    private readonly HavenMode _mode;
    private readonly IContainerRepository _containers;
    private readonly IConversationRepository _conversations;
    private readonly IAutomationRepository _automations;
    private readonly IWorkspaceStateRepository _workspaceState;
    private readonly IProjectIntelligenceService? _projectIntelligence;
    private readonly Func<ContainerDefinition, Task> _open;
    private readonly Func<Task>? _create;

    public WorkspaceHomePage(
        HavenEventBus bus,
        HavenMode mode,
        IContainerRepository containers,
        IConversationRepository conversations,
        IAutomationRepository automations,
        IWorkspaceStateRepository workspaceState,
        IProjectIntelligenceService? projectIntelligence,
        Func<ContainerDefinition, Task> open,
        Func<Task>? create)
    {
        _bus = bus;
        _mode = mode;
        _containers = containers;
        _conversations = conversations;
        _automations = automations;
        _workspaceState = workspaceState;
        _projectIntelligence = projectIntelligence;
        _open = open;
        _create = create;

        InitializeComponent();
        ApplyModeDefaults();
        WireEvents();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e) => _ = RefreshAsync();

    private static IBrush? Brush(string key) =>
        Avalonia.Application.Current?.TryFindResource(key, out var value) == true ? value as IBrush : null;

    private void ApplyModeDefaults()
    {
        var isWorkspace = _mode is HavenMode.Do or HavenMode.Studio;
        TitleText.Text = isWorkspace ? "Workspaces" : _mode == HavenMode.Teach ? "Teach" : "Projects";
        SubtitleText.Text = isWorkspace ? "Manage project workspaces with context, automations, and macros." : "Your learning subjects and conversations.";
        CollectionHeading.Text = isWorkspace ? "Workspaces" : "Subjects";
        CreateButton.Content = _create is not null ? "Create" : "+";
    }

    private void WireEvents()
    {
        _bus.RegisterElement("WorkspaceHome.Actions.Refresh", RefreshButton);
        _bus.WirePointerEvents("WorkspaceHome.Actions.Refresh", RefreshButton);
        RefreshButton.Click += async (_, _) =>
        {
            _bus.Fire("WorkspaceHome.Actions.Refresh");
            await RefreshAsync();
        };

        _bus.RegisterElement("WorkspaceHome.Actions.Create", CreateButton);
        _bus.WirePointerEvents("WorkspaceHome.Actions.Create", CreateButton);
        CreateButton.Click += async (_, _) =>
        {
            _bus.Fire("WorkspaceHome.Actions.Create");
            if (_create is not null) await _create();
        };
    }

    private async Task RefreshAsync()
    {
        ItemsPanel.Children.Clear();
        AutomationsPanel.Children.Clear();
        MacrosPanel.Children.Clear();
        StatusText.Text = "Loading…";

        try
        {
            var items = await _containers.GetByModeAsync(_mode, CancellationToken.None);
            foreach (var item in items)
                ItemsPanel.Children.Add(await CreateWorkspaceCardAsync(item));

            EmptyStateCard.IsVisible = items.Count == 0;

            var automations = await _automations.GetAllAsync(CancellationToken.None);
            foreach (var auto in automations.Where(a => a.IsEnabled))
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Avalonia.Thickness(0, 4, 0, 0) };
                row.Children.Add(new TextBlock { Text = auto.Name });
                var nextRun = new TextBlock { Text = auto.NextRunAt?.LocalDateTime.ToString("g") ?? "Not scheduled", Classes = { "muted" } };
                Grid.SetColumn(nextRun, 1);
                row.Children.Add(nextRun);
                AutomationsPanel.Children.Add(row);
            }
            NoAutomationsText.IsVisible = AutomationsPanel.Children.Count == 0;

            var macros = await _workspaceState.GetMacrosAsync(null, CancellationToken.None);
            foreach (var macro in macros)
            {
                var stack = new StackPanel { Margin = new Avalonia.Thickness(0, 4, 0, 0) };
                stack.Children.Add(new TextBlock { Text = macro.Name });
                stack.Children.Add(new TextBlock { Text = macro.Description, Classes = { "muted" }, FontSize = 11 });
                MacrosPanel.Children.Add(stack);
            }
            NoMacrosText.IsVisible = MacrosPanel.Children.Count == 0;

            StatusText.Text = $"{items.Count} workspace{(items.Count == 1 ? "" : "s")}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to load: {ex.Message}";
        }
    }

    private async Task<Border> CreateWorkspaceCardAsync(ContainerDefinition item)
    {
        var qName = $"WorkspaceHome.List.Item{ItemsPanel.Children.Count}";

        var nameBlock = new TextBlock { Text = item.Name, FontSize = 18, FontWeight = FontWeight.SemiBold };
        var accentBadge = new TextBlock { Text = _mode.ToString(), Classes = { "eyebrow" } };

        var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        headerGrid.Children.Add(nameBlock);
        Grid.SetColumn(accentBadge, 1);
        headerGrid.Children.Add(accentBadge);

        var pathBlock = new TextBlock { Text = item.RootPath ?? "", Classes = { "muted" }, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
        var contextBlock = new TextBlock { Text = item.Context, Classes = { "soft" }, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };

        var infoBorder = new Border
        {
            Background = Brush("HavenPanel2Brush"),
            CornerRadius = new CornerRadius(9), Padding = new Avalonia.Thickness(9)
        };
        var infoStack = new StackPanel();
        infoStack.Children.Add(new TextBlock { Text = "CREATED", Classes = { "eyebrow" } });
        infoStack.Children.Add(new TextBlock { Text = item.CreatedAt.ToLocalTime().ToString("d"), FontSize = 11 });
        infoBorder.Child = infoStack;

        var updatedBorder = new Border
        {
            Background = Brush("HavenPanel2Brush"),
            CornerRadius = new CornerRadius(9), Padding = new Avalonia.Thickness(9)
        };
        var updatedStack = new StackPanel();
        updatedStack.Children.Add(new TextBlock { Text = "UPDATED", Classes = { "eyebrow" } });
        updatedStack.Children.Add(new TextBlock { Text = item.UpdatedAt.ToLocalTime().ToString("d"), FontSize = 11 });
        updatedBorder.Child = updatedStack;

        var pillsGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8 };
        pillsGrid.Children.Add(infoBorder);
        Grid.SetColumn(updatedBorder, 1);
        pillsGrid.Children.Add(updatedBorder);

        var openButton = new Button { Content = "Open", Classes = { "accent" }, HorizontalContentAlignment = HorizontalAlignment.Center };
        var archiveButton = new Button { Content = "Archive", Classes = { "ghost" } };

        openButton.RegisterWithEvents($"{qName}.Open", _bus);
        openButton.Click += async (_, _) =>
        {
            _bus.Fire($"{qName}.Open");
            await _open(item);
        };

        archiveButton.RegisterWithEvents($"{qName}.Archive", _bus);
        archiveButton.Click += async (_, _) =>
        {
            _bus.Fire($"{qName}.Archive");
            await ArchiveAsync(item);
        };

        var buttonGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 7 };
        buttonGrid.Children.Add(openButton);
        Grid.SetColumn(archiveButton, 1);
        buttonGrid.Children.Add(archiveButton);

        var contentStack = new StackPanel { Spacing = 9 };
        contentStack.Children.Add(headerGrid);
        contentStack.Children.Add(pathBlock);
        contentStack.Children.Add(contextBlock);
        contentStack.Children.Add(pillsGrid);
        contentStack.Children.Add(buttonGrid);

        var border = new Border
        {
            Classes = { "card" }, Width = 330, Margin = new Avalonia.Thickness(0, 0, 12, 12),
            Child = contentStack
        };
        border.PointerEntered += (_, _) => _bus.Fire($"{qName}.Hover");
        border.PointerExited += (_, _) => _bus.Fire($"{qName}.Leave");
        return border;
    }

    private async Task ArchiveAsync(ContainerDefinition item)
    {
        try
        {
            await _containers.UpsertAsync(item with { IsArchived = true, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
            await RefreshAsync();
            StatusText.Text = $"Archived \"{item.Name}\".";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Archive failed: {ex.Message}";
        }
    }
}
