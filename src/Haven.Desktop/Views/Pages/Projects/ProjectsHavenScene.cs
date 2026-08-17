using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Projects;

internal sealed record ProjectsHavenItem(
    Guid Id,
    string Name,
    string Path,
    string LastTask,
    string Branch,
    string WorkState,
    string BuildState,
    string RecommendedAction,
    DateTimeOffset UpdatedAt,
    bool IsPinned,
    bool IsUnread);

internal sealed class ProjectActionEventArgs(Guid projectId) : EventArgs
{
    public Guid ProjectId { get; } = projectId;
}

internal sealed class ProjectToggleActionEventArgs(Guid projectId, bool value) : EventArgs
{
    public Guid ProjectId { get; } = projectId;
    public bool Value { get; } = value;
}

/// <summary>Haven-owned Projects information architecture and interaction surface.</summary>
internal sealed class ProjectsHavenScene : IDisposable
{
    private readonly List<ProjectsHavenItem> _items = [];
    private string _appliedQuery = string.Empty;

    public ProjectsHavenScene()
    {
        Root = new Page { Name = "Projects.Root", Layout = HavenLayout.Vertical };
        Root.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Padding, HavenThickness.Parse("24px 28px 40px 28px"));
        Root.SetValue(HavenProperties.Gap, HavenLength.Px(16));
        Root.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);

        WideHeader = BuildWideHeader();
        WideHeader.Conditions.Add(new HavenScreenRangeCondition(HavenScreenAxis.Width, HavenLength.Px(760)));
        Root.Add(WideHeader);

        CompactHeader = BuildCompactHeader();
        CompactHeader.Conditions.Add(new HavenScreenRangeCondition(HavenScreenAxis.Width, maximum: HavenLength.Px(759.999)));
        Root.Add(CompactHeader);

        SearchInput = new Input { Name = "Projects.Search", Placeholder = "Search projects, folders, branches or recent work" };
        SearchInput.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        SearchInput.SetValue(HavenProperties.MaxWidth, HavenLength.Px(900));
        SearchInput.Invalidated += OnSearchInvalidated;
        Root.Add(SearchInput);

        StatusText = Muted("Projects.Status", string.Empty);
        StatusText.SetValue(HavenProperties.MinHeight, HavenLength.Px(20));
        Root.Add(StatusText);

        WideGroups = BuildGroups(compact: false);
        WideGroups.Conditions.Add(new HavenScreenRangeCondition(HavenScreenAxis.Width, HavenLength.Px(720)));
        Root.Add(WideGroups);

        CompactGroups = BuildGroups(compact: true);
        CompactGroups.Conditions.Add(new HavenScreenRangeCondition(HavenScreenAxis.Width, maximum: HavenLength.Px(719.999)));
        Root.Add(CompactGroups);

        EmptyState = BuildEmptyState();
        Root.Add(EmptyState);
        RenderItems();
    }

    public Page Root { get; }
    public Container WideHeader { get; }
    public Container CompactHeader { get; }
    public Input SearchInput { get; }
    public HavenText StatusText { get; }
    public Container WideGroups { get; }
    public Container CompactGroups { get; }
    public Container EmptyState { get; }
    public IReadOnlyList<Guid> VisibleItemIds { get; private set; } = [];
    public int PinnedCount { get; private set; }
    public int UnreadCount { get; private set; }
    public int ProjectCount { get; private set; }

    public event EventHandler? RefreshRequested;
    public event EventHandler? CreateRequested;
    public event EventHandler? ConnectRequested;
    public event EventHandler<ProjectActionEventArgs>? OpenRequested;
    public event EventHandler<ProjectToggleActionEventArgs>? PinRequested;
    public event EventHandler<ProjectToggleActionEventArgs>? ReadStateRequested;
    public event EventHandler<ProjectActionEventArgs>? ArchiveRequested;

    public void SetItems(IEnumerable<ProjectsHavenItem> items)
    {
        _items.Clear();
        _items.AddRange(items.OrderByDescending(item => item.UpdatedAt).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase));
        RenderItems();
    }

    public void SetStatus(string text, bool isError = false)
    {
        StatusText.Content = text ?? string.Empty;
        StatusText.SetValue(HavenProperties.Foreground, isError ? "Danger" : "TextSecondary");
    }

    private Container BuildWideHeader()
    {
        var header = new Container { Name = "Projects.Header.Wide", Layout = HavenLayout.Grid, Columns = "1fr Auto Auto Auto", Rows = "Auto" };
        header.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        header.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        var copy = new Container { Layout = HavenLayout.Vertical };
        copy.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        copy.Add(Heading("Projects.Title.Wide", "Projects", TextLevel.H1));
        copy.Add(Muted("Projects.Subtitle.Wide", "Your local workspaces, live project state, and the next useful step."));
        header.Add(copy);
        var refresh = ActionButton("Projects.Refresh.Wide", "Refresh", ButtonVariant.Ghost, (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty));
        refresh.SetValue(HavenProperties.Column, 1);
        header.Add(refresh);
        var connect = ActionButton("Projects.Connect.Wide", "Connect folder", ButtonVariant.Secondary, (_, _) => ConnectRequested?.Invoke(this, EventArgs.Empty));
        connect.SetValue(HavenProperties.Column, 2);
        header.Add(connect);
        var create = ActionButton("Projects.Create.Wide", "New project", ButtonVariant.Primary, (_, _) => CreateRequested?.Invoke(this, EventArgs.Empty));
        create.SetValue(HavenProperties.Column, 3);
        header.Add(create);
        return header;
    }

    private Container BuildCompactHeader()
    {
        var header = new Container { Name = "Projects.Header.Compact", Layout = HavenLayout.Vertical };
        header.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        header.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        header.Add(Heading("Projects.Title.Compact", "Projects", TextLevel.H1));
        header.Add(Muted("Projects.Subtitle.Compact", "Local workspaces and their current state."));
        var actions = new Container { Layout = HavenLayout.Wrap };
        actions.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        actions.Add(ActionButton("Projects.Refresh.Compact", "Refresh", ButtonVariant.Ghost, (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty)));
        actions.Add(ActionButton("Projects.Connect.Compact", "Connect folder", ButtonVariant.Secondary, (_, _) => ConnectRequested?.Invoke(this, EventArgs.Empty)));
        actions.Add(ActionButton("Projects.Create.Compact", "New project", ButtonVariant.Primary, (_, _) => CreateRequested?.Invoke(this, EventArgs.Empty)));
        header.Add(actions);
        return header;
    }

    private Container BuildGroups(bool compact)
    {
        var groups = new Container { Name = compact ? "Projects.Groups.Compact" : "Projects.Groups.Wide", Layout = HavenLayout.Vertical };
        groups.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        groups.SetValue(HavenProperties.Gap, HavenLength.Px(22));
        groups.Add(GroupShell(compact, "Pinned", "Pinned projects"));
        groups.Add(GroupShell(compact, "Unread", "Unread changes"));
        groups.Add(GroupShell(compact, "All", "All projects"));
        return groups;
    }

    private static Container GroupShell(bool compact, string key, string title)
    {
        var group = new Container { Name = $"Projects.Group.{key}.{(compact ? "Compact" : "Wide")}", Layout = HavenLayout.Vertical };
        group.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        group.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        group.Add(Heading($"Projects.Group.{key}.{(compact ? "Compact" : "Wide")}.Heading", title, TextLevel.H3));
        var cards = new Container { Name = $"Projects.Group.{key}.{(compact ? "Compact" : "Wide")}.Cards", Layout = compact ? HavenLayout.Vertical : HavenLayout.Wrap };
        cards.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        cards.SetValue(HavenProperties.Gap, HavenLength.Px(12));
        group.Add(cards);
        return group;
    }

    private Container BuildEmptyState()
    {
        var empty = Card("Projects.Empty");
        empty.SetValue(HavenProperties.MaxWidth, HavenLength.Px(720));
        empty.Add(Heading("Projects.Empty.Title", "No projects found", TextLevel.H3));
        empty.Add(Muted("Projects.Empty.Description", "Create a project or connect an existing local folder."));
        var actions = new Container { Layout = HavenLayout.Wrap };
        actions.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        actions.Add(ActionButton("Projects.Empty.Create", "Create project", ButtonVariant.Primary, (_, _) => CreateRequested?.Invoke(this, EventArgs.Empty)));
        actions.Add(ActionButton("Projects.Empty.Connect", "Connect existing folder", ButtonVariant.Secondary, (_, _) => ConnectRequested?.Invoke(this, EventArgs.Empty)));
        empty.Add(actions);
        return empty;
    }

    private void RenderItems()
    {
        var query = _appliedQuery.Trim();
        var filtered = _items.Where(item => Matches(item, query)).ToArray();
        var pinned = filtered.Where(item => item.IsPinned).ToArray();
        var unread = filtered.Where(item => !item.IsPinned && item.IsUnread).ToArray();
        var remaining = filtered.Where(item => !item.IsPinned && !item.IsUnread).ToArray();

        VisibleItemIds = filtered.Select(item => item.Id).ToArray();
        PinnedCount = pinned.Length;
        UnreadCount = unread.Length;
        ProjectCount = remaining.Length;

        RenderGroup(WideGroups, compact: false, "Pinned", pinned);
        RenderGroup(WideGroups, compact: false, "Unread", unread);
        RenderGroup(WideGroups, compact: false, "All", remaining);
        RenderGroup(CompactGroups, compact: true, "Pinned", pinned);
        RenderGroup(CompactGroups, compact: true, "Unread", unread);
        RenderGroup(CompactGroups, compact: true, "All", remaining);
        EmptyState.SetValue(HavenProperties.Visibility, filtered.Length == 0 ? HavenVisibility.Visible : HavenVisibility.Collapsed);
    }

    private void RenderGroup(Container groups, bool compact, string key, IReadOnlyList<ProjectsHavenItem> items)
    {
        var groupName = $"Projects.Group.{key}.{(compact ? "Compact" : "Wide")}";
        var group = groups.Children.OfType<Container>().Single(item => item.Name == groupName);
        var cards = group.Children.OfType<Container>().Single(item => item.Name == groupName + ".Cards");
        foreach (var child in cards.Children.ToArray()) cards.Remove(child);
        foreach (var item in items) cards.Add(BuildProjectCard(item, compact));
        group.SetValue(HavenProperties.Visibility, items.Count == 0 ? HavenVisibility.Collapsed : HavenVisibility.Visible);
    }

    private Container BuildProjectCard(ProjectsHavenItem item, bool compact)
    {
        var layoutKey = compact ? "Compact" : "Wide";
        var prefix = $"Projects.Card.{item.Id:N}.{layoutKey}";
        var shell = new Container { Name = prefix, Layout = HavenLayout.Overlay };
        shell.SetValue(HavenProperties.Width, compact ? HavenLength.Percent(100) : HavenLength.Px(340));
        shell.SetValue(HavenProperties.MinHeight, HavenLength.Px(compact ? 190 : 220));

        var menu = BuildProjectMenu(item, prefix, compact);

        var tile = new ProjectTileContainer($"Open project {item.Name}") { Name = prefix + ".Tile", Layout = HavenLayout.Vertical };
        tile.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        tile.SetValue(HavenProperties.MinHeight, HavenLength.Px(compact ? 190 : 220));
        tile.SetValue(HavenProperties.Background, "AccentMuted");
        tile.SetValue(HavenProperties.BorderColor, "AccentSecondary");
        tile.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        tile.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(22)));
        tile.SetValue(HavenProperties.Shadow, "Card");
        tile.SetValue(HavenProperties.Padding, HavenThickness.Parse("18px 60px 18px 18px"));
        tile.SetValue(HavenProperties.Gap, HavenLength.Px(14));
        tile.Invoked += (_, _) =>
        {
            menu.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
            OpenRequested?.Invoke(this, new ProjectActionEventArgs(item.Id));
        };

        var content = new Container { Layout = HavenLayout.Vertical };
        content.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        content.SetValue(HavenProperties.Gap, HavenLength.Px(14));
        content.SetValue(HavenProperties.PointerEvents, HavenPointerEvents.None);

        var identity = new Container { Layout = HavenLayout.Grid, Columns = "54px 1fr", Rows = "Auto" };
        identity.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        identity.SetValue(HavenProperties.Gap, HavenLength.Px(12));

        var iconSurface = new Container { Layout = HavenLayout.Overlay };
        iconSurface.SetValue(HavenProperties.Width, HavenLength.Px(54));
        iconSurface.SetValue(HavenProperties.Height, HavenLength.Px(54));
        iconSurface.SetValue(HavenProperties.Background, "AccentSecondary");
        iconSurface.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(18)));
        var icon = new Icon { Key = "studio" };
        icon.SetValue(HavenProperties.Width, HavenLength.Px(26));
        icon.SetValue(HavenProperties.Height, HavenLength.Px(26));
        icon.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        icon.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        icon.SetValue(HavenProperties.Foreground, "TextOnAccent");
        iconSurface.Add(icon);
        identity.Add(iconSurface);

        var copy = new Container { Layout = HavenLayout.Vertical };
        copy.SetValue(HavenProperties.Column, 1);
        copy.SetValue(HavenProperties.Gap, HavenLength.Px(3));
        copy.Add(Heading(prefix + ".Name", item.Name, TextLevel.H3));
        copy.Add(Muted(prefix + ".Path", item.Path));
        identity.Add(copy);
        content.Add(identity);

        if (!string.IsNullOrWhiteSpace(item.LastTask))
        {
            var recent = new Container { Layout = HavenLayout.Vertical };
            recent.SetValue(HavenProperties.Background, "SurfaceRaised");
            recent.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(14)));
            recent.SetValue(HavenProperties.Padding, HavenThickness.Parse("10px 12px"));
            recent.SetValue(HavenProperties.Gap, HavenLength.Px(3));
            var recentLabel = new HavenText("RECENT WORK") { Level = TextLevel.Caption };
            recentLabel.SetValue(HavenProperties.Foreground, "AccentSecondary");
            recent.Add(recentLabel);
            recent.Add(new HavenText(item.LastTask) { Level = TextLevel.Paragraph });
            content.Add(recent);
        }

        var footer = new Container { Layout = HavenLayout.Grid, Columns = "1fr Auto", Rows = "Auto" };
        footer.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        footer.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        var stateHint = new HavenText(item.IsUnread ? "Unread changes" : "Ready to open") { Level = TextLevel.Caption };
        stateHint.SetValue(HavenProperties.Foreground, item.IsUnread ? "Danger" : "TextSecondary");
        footer.Add(stateHint);
        var openHint = new HavenText("Open project ›") { Level = TextLevel.Caption };
        openHint.SetValue(HavenProperties.Column, 1);
        openHint.SetValue(HavenProperties.Foreground, "AccentSecondary");
        footer.Add(openHint);
        content.Add(footer);
        tile.Add(content);
        shell.Add(tile);

        shell.Add(menu);

        var more = new HavenButton { Name = prefix + ".More", Content = "•••", Variant = ButtonVariant.Icon };
        more.Accessibility.AccessibleName = $"More options for {item.Name}";
        more.SetValue(HavenProperties.Width, HavenLength.Px(46));
        more.SetValue(HavenProperties.Height, HavenLength.Px(46));
        more.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.End);
        more.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Start);
        more.SetValue(HavenProperties.Margin, HavenThickness.Parse("10px"));
        more.SetValue(HavenProperties.ZIndex, 7);
        more.Invoked += (_, _) =>
        {
            var open = menu.GetValue(HavenProperties.Visibility) != HavenVisibility.Visible;
            menu.SetValue(HavenProperties.Visibility, open ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        };
        shell.Add(more);
        return shell;
    }

    private Container BuildProjectMenu(ProjectsHavenItem item, string cardPrefix, bool compact)
    {
        var prefix = cardPrefix + ".Menu";
        var menu = new Container { Name = prefix, Layout = HavenLayout.Vertical };
        menu.SetValue(HavenProperties.Width, HavenLength.Px(compact ? 238 : 258));
        menu.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.End);
        menu.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Start);
        menu.SetValue(HavenProperties.Margin, HavenThickness.Parse("58px 10px 0px 10px"));
        menu.SetValue(HavenProperties.Background, "SurfaceRaised");
        menu.SetValue(HavenProperties.BorderColor, "AccentSecondary");
        menu.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        menu.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(18)));
        menu.SetValue(HavenProperties.Shadow, "Card");
        menu.SetValue(HavenProperties.Padding, HavenThickness.Parse("12px"));
        menu.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        menu.SetValue(HavenProperties.ZIndex, 6);
        menu.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);

        var title = new HavenText("PROJECT DETAILS") { Name = prefix + ".Title", Level = TextLevel.Caption };
        title.SetValue(HavenProperties.Foreground, "AccentSecondary");
        menu.Add(title);
        menu.Add(DetailLine(prefix + ".Branch", "Branch", item.Branch));
        menu.Add(DetailLine(prefix + ".State", "State", item.WorkState));
        menu.Add(DetailLine(prefix + ".Build", "Build", item.BuildState));
        if (!string.IsNullOrWhiteSpace(item.RecommendedAction))
            menu.Add(Muted(prefix + ".Recommendation", item.RecommendedAction));

        var pin = ActionButton(prefix + ".Pin", item.IsPinned ? "Unpin project" : "Pin project", ButtonVariant.Ghost, (_, _) =>
        {
            menu.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
            PinRequested?.Invoke(this, new ProjectToggleActionEventArgs(item.Id, !item.IsPinned));
        });
        pin.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        menu.Add(pin);

        var read = ActionButton(prefix + ".Read", item.IsUnread ? "Mark as read" : "Mark as unread", ButtonVariant.Ghost, (_, _) =>
        {
            menu.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
            ReadStateRequested?.Invoke(this, new ProjectToggleActionEventArgs(item.Id, !item.IsUnread));
        });
        read.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        menu.Add(read);

        var archive = ActionButton(prefix + ".Archive", "Archive project", ButtonVariant.Danger, (_, _) =>
        {
            menu.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
            ArchiveRequested?.Invoke(this, new ProjectActionEventArgs(item.Id));
        });
        archive.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        menu.Add(archive);
        return menu;
    }

    private static Container DetailLine(string name, string label, string value)
    {
        var line = new Container { Name = name, Layout = HavenLayout.Vertical };
        line.SetValue(HavenProperties.Padding, HavenThickness.Parse("3px 2px"));
        line.SetValue(HavenProperties.Gap, HavenLength.Px(1));
        var key = new HavenText(label.ToUpperInvariant()) { Level = TextLevel.Caption };
        key.SetValue(HavenProperties.Foreground, "TextSecondary");
        line.Add(key);
        line.Add(new HavenText(value) { Level = TextLevel.Caption });
        return line;
    }

    private sealed class ProjectTileContainer : Container
    {
        public ProjectTileContainer(string accessibleName)
        {
            Accessibility.Role = HavenAccessibleRole.Button;
            Accessibility.Focusable = true;
            Accessibility.AccessibleName = accessibleName;
            SetValue(HavenProperties.Hover, true, HavenValueSource.Default);
            SetValue(HavenProperties.Cursor, HavenCursor.Pointer, HavenValueSource.Default);
            SetValue(HavenProperties.Transition, ButtonDefaults.HoverTransition, HavenValueSource.Default);
        }

        protected override void OnStateChanged()
        {
            ClearValue(HavenProperties.Background, HavenValueSource.State);
            ClearValue(HavenProperties.BorderColor, HavenValueSource.State);
            ClearValue(HavenProperties.Glow, HavenValueSource.State);
            ClearValue(HavenProperties.Scale, HavenValueSource.State);

            if (State.HasFlag(HavenElementState.Hover))
            {
                SetValue(HavenProperties.Background, "AccentTertiaryHover", HavenValueSource.State);
                SetValue(HavenProperties.Glow, "AccentTertiaryGlow", HavenValueSource.State);
                SetValue(HavenProperties.Scale, 1.012d, HavenValueSource.State);
            }
            if (State.HasFlag(HavenElementState.Focused))
                SetValue(HavenProperties.BorderColor, "AccentSecondary", HavenValueSource.State);
            if (State.HasFlag(HavenElementState.Pressed))
                SetValue(HavenProperties.Scale, .985d, HavenValueSource.State);
        }
    }

    private static bool Matches(ProjectsHavenItem item, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        return item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               item.Path.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               item.LastTask.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               item.Branch.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               item.WorkState.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               item.BuildState.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               item.RecommendedAction.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void OnSearchInvalidated(object? sender, EventArgs e)
    {
        var query = SearchInput.Text;
        if (string.Equals(query, _appliedQuery, StringComparison.Ordinal)) return;
        _appliedQuery = query;
        RenderItems();
    }

    private static Container Card(string name)
    {
        var card = new Container { Name = name, Layout = HavenLayout.Vertical };
        card.SetValue(HavenProperties.Background, "SurfaceRaised");
        card.SetValue(HavenProperties.BorderColor, "Border");
        card.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(18)));
        card.SetValue(HavenProperties.Shadow, "Card");
        card.SetValue(HavenProperties.Padding, HavenThickness.Parse("16px"));
        card.SetValue(HavenProperties.Gap, HavenLength.Px(9));
        return card;
    }

    private static HavenText Heading(string? name, string content, TextLevel level) => new(content) { Name = name, Level = level };

    private static HavenText Muted(string? name, string content)
    {
        var text = new HavenText(content) { Name = name, Level = TextLevel.Caption };
        text.SetValue(HavenProperties.Foreground, "TextSecondary");
        return text;
    }

    private static HavenButton ActionButton(string name, string content, ButtonVariant variant, EventHandler handler)
    {
        var button = new HavenButton { Name = name, Content = content, Variant = variant };
        button.Invoked += handler;
        return button;
    }

    public void Dispose()
    {
        SearchInput.Invalidated -= OnSearchInvalidated;
    }
}
