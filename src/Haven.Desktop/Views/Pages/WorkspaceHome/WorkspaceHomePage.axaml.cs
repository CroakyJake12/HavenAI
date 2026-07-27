using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Application;
using Haven.Core;
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

    private readonly List<ContainerDefinition> _items = [];
    private TextBox? _searchBox;
    private StackPanel? _sections;
    private TextBlock? _statusText;
    private Border? _emptyState;
    private HashSet<Guid> _pinnedIds = [];

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
        BuildProjectsUi();

        Loaded += async (_, _) => await RefreshAsync();
    }

    private void BuildProjectsUi()
    {
        var title = new TextBlock
        {
            Text = "Projects",
            FontSize = 30,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var subtitle = new TextBlock
        {
            Text = "Pick up where you left off or start something new.",
            FontSize = 13,
            Opacity = 0.66,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        _searchBox = new TextBox
        {
            PlaceholderText = "Search projects",
            MinHeight = 42,
            MaxWidth = 720,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _searchBox.TextChanged += (_, _) => RenderProjects();

        var refreshButton = new Button { Content = "Refresh" };
        refreshButton.Classes.Add("ghost");
        refreshButton.Click += async (_, _) => await RefreshAsync();

        var createButton = new Button
        {
            Content = "Create New Project",
            IsVisible = _create is not null,
            MinHeight = 42
        };
        createButton.Classes.Add("accent");
        createButton.Click += async (_, _) =>
        {
            if (_create is not null)
            {
                await _create();
            }
        };

        var actionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 8,
            Children = { refreshButton, createButton }
        };

        _sections = new StackPanel { Spacing = 24 };

        _emptyState = new Border
        {
            IsVisible = false,
            Padding = new Thickness(24),
            CornerRadius = new CornerRadius(18),
            Background = Brush("HavenPanelBrush"),
            BorderBrush = Brush("HavenLineBrush"),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = "No projects match this search.",
                HorizontalAlignment = HorizontalAlignment.Center,
                Opacity = 0.66
            }
        };

        _statusText = new TextBlock
        {
            FontSize = 11,
            Opacity = 0.58,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var content = new StackPanel
        {
            MaxWidth = 1120,
            Margin = new Thickness(34, 28),
            Spacing = 16
        };
        content.Children.Add(title);
        content.Children.Add(subtitle);
        content.Children.Add(_searchBox);
        content.Children.Add(actionRow);
        content.Children.Add(_sections);
        content.Children.Add(_emptyState);
        content.Children.Add(_statusText);

        CodeBehindHost.Children.Clear();
        CodeBehindHost.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = content
        });
    }

    private async Task RefreshAsync()
    {
        if (_statusText is null)
        {
            return;
        }

        _statusText.Text = "Loading projects…";

        try
        {
            var items = await _containers.GetByModeAsync(
                _mode,
                CancellationToken.None);

            _items.Clear();
            _items.AddRange(items.Where(item => !item.IsArchived));
            _pinnedIds = await LoadPinnedContainerIdsAsync();

            RenderProjects();
            _statusText.Text = _items.Count == 1
                ? "1 project"
                : $"{_items.Count} projects";
        }
        catch (Exception exception)
        {
            _statusText.Text = $"Could not load projects: {exception.Message}";
            _items.Clear();
            RenderProjects();
        }
    }

    private void RenderProjects()
    {
        if (_sections is null || _emptyState is null)
        {
            return;
        }

        _sections.Children.Clear();

        var query = _searchBox?.Text?.Trim();
        var filtered = _items
            .Where(item => string.IsNullOrWhiteSpace(query) ||
                           item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           (item.RootPath?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                           item.Context.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.UpdatedAt)
            .ToArray();

        var pinned = filtered
            .Where(item => _pinnedIds.Contains(item.Id))
            .ToArray();

        var regular = filtered
            .Where(item => !_pinnedIds.Contains(item.Id))
            .ToArray();

        AddProjectSection("Pinned", pinned, "Projects you marked for quick access.");
        AddProjectSection("Projects", regular, null);

        _emptyState.IsVisible = filtered.Length == 0;
    }

    private void AddProjectSection(
        string title,
        IReadOnlyList<ContainerDefinition> items,
        string? subtitle)
    {
        if (items.Count == 0 || _sections is null)
        {
            return;
        }

        var heading = new StackPanel { Spacing = 2 };
        heading.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 19,
            FontWeight = FontWeight.SemiBold
        });

        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            heading.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 11,
                Opacity = 0.6
            });
        }

        var wrap = new WrapPanel
        {
            ItemWidth = 350,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        foreach (var item in items)
        {
            wrap.Children.Add(CreateProjectCard(item));
        }

        var section = new StackPanel { Spacing = 10 };
        section.Children.Add(heading);
        section.Children.Add(wrap);
        _sections.Children.Add(section);
    }

    private Control CreateProjectCard(ContainerDefinition item)
    {
        var title = new TextBlock
        {
            Text = item.Name,
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var badge = new Border
        {
            Padding = new Thickness(8, 3),
            CornerRadius = new CornerRadius(999),
            Background = Brush("HavenBlueSoftBrush"),
            Child = new TextBlock
            {
                Text = _mode == HavenMode.Studio ? "STUDIO" : _mode.ToString().ToUpperInvariant(),
                FontSize = 9,
                FontWeight = FontWeight.SemiBold,
                Opacity = 0.72
            }
        };

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };
        header.Children.Add(title);
        Grid.SetColumn(badge, 1);
        header.Children.Add(badge);

        var path = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(item.RootPath)
                ? "No folder selected"
                : item.RootPath,
            FontSize = 11,
            Opacity = 0.6,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var context = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(item.Context)
                ? "No project description yet."
                : item.Context,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 3,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var updated = new TextBlock
        {
            Text = $"Updated {item.UpdatedAt.ToLocalTime():g}",
            FontSize = 10,
            Opacity = 0.56
        };

        var openButton = new Button
        {
            Content = "Open",
            MinWidth = 84
        };
        openButton.Classes.Add("accent");
        openButton.Click += async (_, _) => await _open(item);

        var archiveButton = new Button
        {
            Content = "Archive"
        };
        archiveButton.Classes.Add("ghost");
        archiveButton.Click += async (_, _) => await ArchiveAsync(item);

        var actions = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };
        actions.Children.Add(openButton);
        Grid.SetColumn(archiveButton, 1);
        actions.Children.Add(archiveButton);

        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(header);
        stack.Children.Add(path);
        stack.Children.Add(context);
        stack.Children.Add(updated);
        stack.Children.Add(actions);

        var card = new Border
        {
            Width = 338,
            MinHeight = 210,
            Margin = new Thickness(0, 0, 12, 12),
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(20),
            Background = Brush("HavenPanelBrush"),
            BorderBrush = Brush("HavenLineBrush"),
            BorderThickness = new Thickness(1),
            Child = stack
        };

        card.PointerPressed += async (_, eventArgs) =>
        {
            if (eventArgs.GetCurrentPoint(card).Properties.PointerUpdateKind ==
                Avalonia.Input.PointerUpdateKind.LeftButtonPressed)
            {
                await _open(item);
            }
        };

        return card;
    }

    private async Task ArchiveAsync(ContainerDefinition item)
    {
        try
        {
            await _containers.UpsertAsync(
                item with
                {
                    IsArchived = true,
                    UpdatedAt = DateTimeOffset.UtcNow
                },
                CancellationToken.None);

            await RefreshAsync();
            if (_statusText is not null)
            {
                _statusText.Text = $"Archived “{item.Name}”.";
            }
        }
        catch (Exception exception)
        {
            if (_statusText is not null)
            {
                _statusText.Text = $"Archive failed: {exception.Message}";
            }
        }
    }

    private async Task<HashSet<Guid>> LoadPinnedContainerIdsAsync()
    {
        var ids = new HashSet<Guid>();

        try
        {
            var repositoryType = _conversations.GetType();
            var method = repositoryType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(candidate =>
                    candidate.Name == "GetByModeAsync" &&
                    candidate.GetParameters().Length == 2);

            object? taskObject;
            if (method is not null)
            {
                taskObject = method.Invoke(
                    _conversations,
                    [_mode, CancellationToken.None]);
            }
            else
            {
                method = repositoryType
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(candidate =>
                        candidate.Name == "GetAllAsync" &&
                        candidate.GetParameters().Length == 1);

                taskObject = method?.Invoke(
                    _conversations,
                    [CancellationToken.None]);
            }

            if (taskObject is not Task task)
            {
                return ids;
            }

            await task.ConfigureAwait(true);
            var result = taskObject.GetType()
                .GetProperty("Result")
                ?.GetValue(taskObject);

            if (result is not IEnumerable conversations)
            {
                return ids;
            }

            foreach (var conversation in conversations.Cast<object>())
            {
                var type = conversation.GetType();
                var pinned = type.GetProperty("IsPinned")?.GetValue(conversation) as bool? ?? false;
                var containerId = type.GetProperty("ContainerId")?.GetValue(conversation);

                if (pinned && containerId is Guid id)
                {
                    ids.Add(id);
                }
            }
        }
        catch
        {
            // Pinned data is optional; the section remains hidden when unavailable.
        }

        return ids;
    }

    private static IBrush? Brush(string key) =>
        Avalonia.Application.Current?.TryFindResource(key, out var value) == true
            ? value as IBrush
            : null;
}
