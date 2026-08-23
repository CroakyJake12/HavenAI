using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenIcon = Haven.UI.Components.Icon;
using HavenImage = Haven.UI.Components.Image;
using HavenText = Haven.UI.Components.Text;
using HavenTabStrip = Haven.UI.Components.TabStrip;
using HavenTabStripItem = Haven.UI.Components.TabStripItem;

namespace Haven.Desktop.Views.Shell.TopRail;

internal sealed class TopRailFinalScene
{
    private const double MaximumTabStripWidth = 460d;
    private IReadOnlyList<TopRailTab> _tabs = [];
    private PopupMenu? _activeTabMenu;

    public TopRailFinalScene()
    {
        Root = new Page { Name = "TopRail.Root", Layout = HavenLayout.Horizontal };
        Root.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Height, HavenLength.Px(76));
        Root.SetValue(HavenProperties.Padding, HavenThickness.Parse("0px 24px"));
        Root.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        Root.SetValue(HavenProperties.Background, "Transparent");
        Root.SetValue(HavenProperties.Overflow, HavenOverflow.Clip);

        LogoHost = new Container { Name = "TopRail.LogoHost", Layout = HavenLayout.Overlay };
        LogoHost.SetValue(HavenProperties.Width, HavenLength.Px(54));
        LogoHost.SetValue(HavenProperties.Height, HavenLength.Px(54));
        LogoHost.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        LogoButton = new HavenButton { Name = "TopRail.Logo", Variant = ButtonVariant.Icon };
        LogoButton.Accessibility.AccessibleName = "Haven home";
        LogoButton.SetValue(HavenProperties.Width, HavenLength.Px(54));
        LogoButton.SetValue(HavenProperties.Height, HavenLength.Px(54));
        LogoButton.SetValue(HavenProperties.MinHeight, HavenLength.Px(54));
        LogoButton.SetValue(HavenProperties.Background, "Transparent");
        var logo = new HavenImage { Source = "avares://Haven/Assets/haven-192.png", Fit = HavenImageFit.Contain };
        logo.SetValue(HavenProperties.Width, HavenLength.Px(48));
        logo.SetValue(HavenProperties.Height, HavenLength.Px(48));
        logo.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        logo.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        logo.SetValue(HavenProperties.PointerEvents, HavenPointerEvents.None);
        logo.SetValue(HavenProperties.ZIndex, 1);
        LogoHost.Add(LogoButton);
        LogoHost.Add(logo);
        Root.Add(LogoHost);

        NavigationHost = new Container { Name = "TopRail.Navigation", Layout = HavenLayout.Horizontal };
        NavigationHost.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        NavigationHost.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        BackButton = IconButton("TopRail.Actions.Back", "chevron-left", "Back");
        ForwardButton = IconButton("TopRail.Actions.Forward", "chevron-right", "Forward");
        NavigationHost.Add(BackButton);
        NavigationHost.Add(ForwardButton);
        Root.Add(NavigationHost);
        SetNavigationAvailability(false, false);

        TabStrip = new HavenTabStrip { Name = "TopRail.Tabs" };
        TabStrip.SetValue(HavenProperties.MinWidth, HavenLength.Px(72));
        TabStrip.SetValue(HavenProperties.Width, HavenLength.Px(72));
        TabStrip.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        TabStrip.ItemInvoked += (_, key) => TabSelected?.Invoke(this, key);
        TabStrip.ItemSecondaryInvoked += (_, key) =>
        {
            var tab = _tabs.FirstOrDefault(x => x.Key.Equals(key, StringComparison.Ordinal));
            if (tab is not null) ShowTabContextMenu(tab);
        };
        Root.Add(TabStrip);

        TabActionsHost = new Container { Name = "TopRail.TabActions", Layout = HavenLayout.Horizontal };
        TabActionsHost.SetValue(HavenProperties.Gap, HavenLength.Px(7));
        TabActionsHost.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        AddTabButton = AccentIconButton("TopRail.Tabs.Add", "plus", "New tab");
        TabOverviewButton = AccentIconButton("TopRail.Tabs.Overview", "window", "All tabs");
        TabActionsHost.Add(AddTabButton);
        TabActionsHost.Add(TabOverviewButton);
        Root.Add(TabActionsHost);

        Spacer = new Container { Name = "TopRail.Spacer", Layout = HavenLayout.Overlay };
        Spacer.SetValue(HavenProperties.Width, HavenLength.Fr(1));
        Spacer.SetValue(HavenProperties.Height, HavenLength.Px(1));
        Spacer.SetValue(HavenProperties.PointerEvents, HavenPointerEvents.None);
        Root.Add(Spacer);

        AppsHost = Dropdown("TopRail.AppsHost", 104, "rocket", "Apps", "TopRail.Actions.Apps", out var apps);
        AppsButton = apps;
        Root.Add(AppsHost);
        ActionsHost = Dropdown("TopRail.ActionsHost", 154, "bolt", "Actions", "TopRail.Actions.Capabilities", out var actions);
        ActionsButton = actions;
        Root.Add(ActionsHost);
        ModelHost = Dropdown("TopRail.ModelHost", 236, "cpu", "Local model · 60%", "TopRail.Actions.Model", out var model);
        ModelButton = model;
        ModelButton.SetValue(HavenProperties.Background, "HavenModelReasoningBalancedBrush");
        ModelButton.SetValue(HavenProperties.Foreground, "TextOnAccent");
        Root.Add(ModelHost);

        NotificationHost = new Container { Name = "TopRail.Notifications", Layout = HavenLayout.Overlay };
        NotificationHost.SetValue(HavenProperties.Width, HavenLength.Px(42));
        NotificationHost.SetValue(HavenProperties.Height, HavenLength.Px(42));
        NotificationHost.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        NotificationsButton = AccentIconButton("TopRail.Actions.Notifications", "bell", "Notifications");
        NotificationBadge = new Container { Name = "TopRail.Notifications.Badge", Layout = HavenLayout.Overlay };
        NotificationBadge.SetValue(HavenProperties.Width, HavenLength.Px(20));
        NotificationBadge.SetValue(HavenProperties.Height, HavenLength.Px(20));
        NotificationBadge.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.End);
        NotificationBadge.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.End);
        NotificationBadge.SetValue(HavenProperties.Background, "Danger");
        NotificationBadge.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(10)));
        NotificationBadge.SetValue(HavenProperties.PointerEvents, HavenPointerEvents.None);
        NotificationBadge.SetValue(HavenProperties.ZIndex, 2);
        NotificationBadge.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        NotificationBadgeText = new HavenText("0") { Name = "TopRail.Notifications.BadgeText", Level = TextLevel.Caption };
        NotificationBadgeText.SetValue(HavenProperties.FontSize, 9d);
        NotificationBadgeText.SetValue(HavenProperties.FontWeight, 800);
        NotificationBadgeText.SetValue(HavenProperties.Foreground, "TextOnDanger");
        NotificationBadgeText.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        NotificationBadgeText.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        NotificationBadgeText.SetValue(HavenProperties.PointerEvents, HavenPointerEvents.None);
        NotificationBadge.Add(NotificationBadgeText);
        NotificationHost.Add(NotificationsButton);
        NotificationHost.Add(NotificationBadge);
        Root.Add(NotificationHost);

        SearchButton = AccentIconButton("TopRail.Actions.Search", "search", "Search Haven");
        Root.Add(SearchButton);

        LogoButton.Invoked += (_, _) => HomeRequested?.Invoke(this, EventArgs.Empty);
        AddTabButton.Invoked += (_, _) => NewTabRequested?.Invoke(this, EventArgs.Empty);
        TabOverviewButton.Invoked += (_, _) => TabOverviewRequested?.Invoke(this, EventArgs.Empty);
        BackButton.Invoked += (_, _) => BackRequested?.Invoke(this, EventArgs.Empty);
        ForwardButton.Invoked += (_, _) => ForwardRequested?.Invoke(this, EventArgs.Empty);
        AppsButton.Invoked += (_, _) => AppsRequested?.Invoke(this, EventArgs.Empty);
        ActionsButton.Invoked += (_, _) => ActionsRequested?.Invoke(this, EventArgs.Empty);
        ModelButton.Invoked += (_, _) => ModelRequested?.Invoke(this, EventArgs.Empty);
        NotificationsButton.Invoked += (_, _) => NotificationsRequested?.Invoke(this, EventArgs.Empty);
        SearchButton.Invoked += (_, _) => SearchRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? HomeRequested;
    public event EventHandler? NewTabRequested;
    public event EventHandler? TabOverviewRequested;
    public event EventHandler? BackRequested;
    public event EventHandler? ForwardRequested;
    public event EventHandler? AppsRequested;
    public event EventHandler? ActionsRequested;
    public event EventHandler? ModelRequested;
    public event EventHandler? NotificationsRequested;
    public event EventHandler? SearchRequested;
    public event EventHandler<string>? TabSelected;
    public event EventHandler<TopRailTab>? TabRenameRequested;
    public event EventHandler<string>? TabCloseRequested;
    public event EventHandler<TabCommandRequestedEventArgs>? TabCommandRequested;

    public Page Root { get; }
    public Container LogoHost { get; }
    public HavenButton LogoButton { get; }
    public Container NavigationHost { get; }
    public HavenButton BackButton { get; }
    public HavenButton ForwardButton { get; }
    public HavenTabStrip TabStrip { get; }
    public Container TabActionsHost { get; }
    public HavenButton AddTabButton { get; }
    public HavenButton TabOverviewButton { get; }
    public Container Spacer { get; }
    public Container AppsHost { get; }
    public HavenButton AppsButton { get; }
    public Container ActionsHost { get; }
    public HavenButton ActionsButton { get; }
    public Container ModelHost { get; }
    public HavenButton ModelButton { get; }
    public Container NotificationHost { get; }
    public HavenButton NotificationsButton { get; }
    public Container NotificationBadge { get; }
    public HavenText NotificationBadgeText { get; }
    public HavenButton SearchButton { get; }
    public IReadOnlyList<TopRailTab> Tabs => _tabs;
    internal PopupMenu? ActiveTabMenu => _activeTabMenu;

    public void SetTabs(IReadOnlyList<TopRailTab> tabs)
    {
        _activeTabMenu?.Dismiss();
        _tabs = tabs.ToArray();
        TabStrip.SetItems(_tabs.Select(x => new HavenTabStripItem(x.Key, x.Title, x.IsSelected, HasContextMenu: true)).ToArray());
        TabStrip.SetValue(HavenProperties.Width, HavenLength.Px(PreferredTabStripWidth(_tabs)));
    }

    private void ShowTabContextMenu(TopRailTab tab)
    {
        var index = -1;
        for (var candidate = 0; candidate < _tabs.Count; candidate++)
        {
            if (!_tabs[candidate].Key.Equals(tab.Key, StringComparison.Ordinal)) continue;
            index = candidate;
            break;
        }
        if (index < 0 || index >= TabStrip.ItemButtons.Count) return;

        _activeTabMenu?.Dismiss();
        var items = new List<PopupMenuItem>
        {
                new("Rename tab", () => TabRenameRequested?.Invoke(this, tab), IconKey: "edit"),
                new("Generate name with AI", () => RaiseCommand(tab, "generate-name"), IconKey: "sparkles"),
                new("Duplicate tab", () => RaiseCommand(tab, "duplicate"), IconKey: "copy"),
                new("Move tab left", () => RaiseCommand(tab, "move-left"), IconKey: "chevron-left", Enabled: index > 0),
                new("Move tab right", () => RaiseCommand(tab, "move-right"), IconKey: "chevron-right", Enabled: index < _tabs.Count - 1),
                new("Open in Split View", () => RaiseCommand(tab, "split"), IconKey: "window"),
                new("Open in New Window", () => RaiseCommand(tab, "new-window"), IconKey: "window"),
                new("Open in Pop-Up", () => RaiseCommand(tab, "popup"), IconKey: "window"),
                new("Create Group", () => RaiseCommand(tab, "create-group"), IconKey: "folder"),
                new("Remove from Group", () => RaiseCommand(tab, "remove-group"), IconKey: "close", Enabled: tab.GroupId is not null),
                new("Rename Group", () => RaiseCommand(tab, "rename-group"), IconKey: "edit", Enabled: tab.GroupId is not null),
                new(tab.IsGroupCollapsed ? "Expand Group" : "Collapse Group", () => RaiseCommand(tab, "toggle-group"), IconKey: "folder", Enabled: tab.GroupId is not null),
                new("Dissolve Group", () => RaiseCommand(tab, "dissolve-group"), IconKey: "close", Enabled: tab.GroupId is not null)
        };
        foreach (var group in _tabs.Where(item => item.GroupId is not null && item.GroupId != tab.GroupId)
                     .DistinctBy(item => item.GroupId))
        {
            var groupId = group.GroupId!.Value;
            items.Add(new PopupMenuItem($"Move to {group.GroupName}", () => RaiseCommand(tab, $"move-group:{groupId:D}"), IconKey: "folder"));
        }
        items.AddRange(
        [
                new PopupMenuItem("Close Other Tabs", () => RaiseCommand(tab, "close-others"), Destructive: true, IconKey: "close"),
                new PopupMenuItem("Close Tabs to the Left", () => RaiseCommand(tab, "close-left"), Destructive: true, IconKey: "close"),
                new PopupMenuItem("Close Tabs to the Right", () => RaiseCommand(tab, "close-right"), Destructive: true, IconKey: "close"),
                new PopupMenuItem("Close tab", () => TabCloseRequested?.Invoke(this, tab.Key), Destructive: true, IconKey: "close", Enabled: tab.IsCloseable)
        ]);
        var menu = new PopupMenu(
            TabStrip.ItemButtons[index],
            Root,
            items,
            270d,
            $"Options for {tab.Title}");
        _activeTabMenu = menu;
        menu.Dismissed += (_, _) =>
        {
            if (ReferenceEquals(_activeTabMenu, menu)) _activeTabMenu = null;
        };
        Root.Add(menu);
    }

    private void RaiseCommand(TopRailTab tab, string command) =>
        TabCommandRequested?.Invoke(this, new TabCommandRequestedEventArgs(tab.Key, command));

    public void SetNavigationAvailability(bool back, bool forward)
    {
        Availability(BackButton, back);
        Availability(ForwardButton, forward);
    }

    public void SetModelSummary(string? modelName, int reasoningPercent)
    {
        var name = string.IsNullOrWhiteSpace(modelName) ? "Local model" : modelName.Trim();
        var effort = Math.Clamp(reasoningPercent, 0, 100);
        ModelButton.Content = $"{DisplayModel(name)} · {effort}%";
        ModelButton.Accessibility.AccessibleName = $"Model {name}, reasoning {effort}%";
        ModelButton.SetValue(HavenProperties.Background, effort switch
        {
            < 35 => "HavenModelReasoningLowBrush",
            < 70 => "HavenModelReasoningBalancedBrush",
            < 95 => "HavenModelReasoningHighBrush",
            _ => "HavenModelReasoningMaxBrush"
        });
        ModelButton.SetValue(HavenProperties.Foreground, "TextOnAccent");
    }

    public void SetModelSelectorEnabled(bool enabled)
    {
        ModelButton.SetValue(HavenProperties.Enabled, enabled);
        ModelButton.SetState(HavenElementState.Disabled, !enabled);
    }

    public void SetNotificationCount(int unread)
    {
        var count = Math.Max(0, unread);
        NotificationBadgeText.Content = Math.Min(count, 30).ToString(System.Globalization.CultureInfo.InvariantCulture);
        NotificationBadge.SetValue(HavenProperties.Visibility, count > 0 ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        NotificationsButton.SetValue(HavenProperties.Foreground, count > 0 ? (count >= 10 ? "Danger" : "Warning") : "TextOnAccent");
        NotificationsButton.Accessibility.AccessibleName = count > 0 ? $"Notifications, {count} unread" : "Notifications";
    }

    private static double PreferredTabStripWidth(IReadOnlyList<TopRailTab> tabs)
    {
        if (tabs.Count == 0) return 72d;
        var width = tabs.Sum(tab => Math.Clamp((Math.Min(tab.Title.Length, 24) * 8d) + 24d, 72d, 230d));
        width += Math.Max(0, tabs.Count - 1) * 5d;
        return Math.Clamp(width, 72d, MaximumTabStripWidth);
    }

    private static Container Dropdown(string name, double width, string leadIcon, string content, string buttonName, out HavenButton button)
    {
        var host = new Container { Name = name, Layout = HavenLayout.Overlay };
        host.SetValue(HavenProperties.MinWidth, HavenLength.Px(width));
        host.SetValue(HavenProperties.Height, HavenLength.Px(42));
        host.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        button = new HavenButton { Name = buttonName, Variant = ButtonVariant.Primary, IconKey = leadIcon, Content = content };
        button.Accessibility.AccessibleName = content;
        button.SetValue(HavenProperties.MinWidth, HavenLength.Px(width));
        button.SetValue(HavenProperties.Height, HavenLength.Px(42));
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(42));
        button.SetValue(HavenProperties.Padding, HavenThickness.Parse("0px 34px 0px 16px"));
        button.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(21)));
        var chevron = new HavenIcon { Key = "chevron-down" };
        chevron.SetValue(HavenProperties.Width, HavenLength.Px(12));
        chevron.SetValue(HavenProperties.Height, HavenLength.Px(12));
        chevron.SetValue(HavenProperties.Margin, HavenThickness.Parse("0px 12px 0px 0px"));
        chevron.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.End);
        chevron.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        chevron.SetValue(HavenProperties.Foreground, "TextOnAccent");
        chevron.SetValue(HavenProperties.PointerEvents, HavenPointerEvents.None);
        chevron.SetValue(HavenProperties.ZIndex, 2);
        host.Add(button);
        host.Add(chevron);
        return host;
    }

    private static HavenButton IconButton(string name, string icon, string accessible)
    {
        var button = new HavenButton { Name = name, Variant = ButtonVariant.Icon, IconKey = icon };
        button.Accessibility.AccessibleName = accessible;
        button.SetValue(HavenProperties.Width, HavenLength.Px(42));
        button.SetValue(HavenProperties.Height, HavenLength.Px(42));
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(42));
        button.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        return button;
    }

    private static HavenButton AccentIconButton(string name, string icon, string accessible)
    {
        var button = IconButton(name, icon, accessible);
        button.SetValue(HavenProperties.Background, "Accent");
        button.SetValue(HavenProperties.Foreground, "TextOnAccent");
        return button;
    }

    private static void Availability(HavenButton button, bool available)
    {
        button.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
        button.SetValue(HavenProperties.Enabled, available);
        button.SetState(HavenElementState.Disabled, !available);
    }

    private static string DisplayModel(string name) => name.Length <= 20 ? name : $"{name[..19]}…";
}
