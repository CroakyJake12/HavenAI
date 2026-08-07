from __future__ import annotations

from pathlib import Path

ROOT = Path.cwd()

def read(rel: str) -> str:
    return (ROOT / rel).read_text(encoding="utf-8")

def write(rel: str, text: str) -> None:
    (ROOT / rel).write_text(text, encoding="utf-8")

def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one occurrence, found {count}")
    return text.replace(old, new, 1)

def replace_method(text: str, signature: str, replacement: str, label: str) -> str:
    start = text.find(signature)
    if start < 0:
        raise RuntimeError(f"{label}: signature not found")
    brace = text.find("{", start + len(signature))
    if brace < 0:
        raise RuntimeError(f"{label}: opening brace not found")
    depth = 0
    in_string = False
    verbatim = False
    escape = False
    i = brace
    while i < len(text):
        ch = text[i]
        nxt = text[i + 1] if i + 1 < len(text) else ""
        if in_string:
            if verbatim:
                if ch == '"' and nxt == '"':
                    i += 2
                    continue
                if ch == '"':
                    in_string = False
                    verbatim = False
            else:
                if escape:
                    escape = False
                elif ch == "\\":
                    escape = True
                elif ch == '"':
                    in_string = False
        else:
            if ch == '@' and nxt == '"':
                in_string = True
                verbatim = True
                i += 2
                continue
            if ch == '"':
                in_string = True
            elif ch == "{":
                depth += 1
            elif ch == "}":
                depth -= 1
                if depth == 0:
                    end = i + 1
                    return text[:start] + replacement.rstrip() + text[end:]
        i += 1
    raise RuntimeError(f"{label}: closing brace not found")

layout_path = "src/Haven.Android/MainView.Mobile.Layout.cs"
layout = read(layout_path)
layout = replace_once(
    layout,
    "    private TextBox? _mobileGoInput;\n",
    "    private TextBox? _mobileGoInput\n\n"
    "    private Button? _mobileModelSelectorButton;\n"
    "    private TextBlock? _mobileModelName;\n"
    "    private TextBlock? _mobileModelContext;\n",
    "mobile model fields",
)
layout = replace_method(
    layout,
    "    private Border BuildMobileHeader()",
    r"""    private Border BuildMobileHeader()
    {
        _mobileModelName = new TextBlock
        {
            Text = ModelNameText.Text ?? _preferences.DefaultModel ?? "Model",
            FontWeight = FontWeight.Bold,
            FontSize = 12,
            MaxWidth = 150,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _mobileModelContext = new TextBlock
        {
            Text = ContextPercentText.Text ?? string.Empty,
            FontSize = 10,
            Foreground = ResourceBrush("HavenTextSoftBrush")
        };
        var modelText = new StackPanel
        {
            Spacing = 0,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _mobileModelName, _mobileModelContext }
        };
        var modelContent = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 7
        };
        modelContent.Children.Add(new HavenIcon
        {
            IconKey = "haven",
            Width = 22,
            Height = 22,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(modelText, 1);
        modelContent.Children.Add(modelText);
        var chevron = new HavenIcon
        {
            IconKey = "chevron-down",
            Width = 14,
            Height = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.65
        };
        Grid.SetColumn(chevron, 2);
        modelContent.Children.Add(chevron);
        _mobileModelSelectorButton = new Button
        {
            Content = modelContent,
            MinHeight = 44,
            MinWidth = 0,
            MaxWidth = 220,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(9, 5),
            CornerRadius = new CornerRadius(14)
        };
        ToolTip.SetTip(_mobileModelSelectorButton, "Switch model");
        _mobileModelSelectorButton.Click += (_, _) => ShowModelSelector();

        var actions = MobileButton("Actions", "commands", ShowMobileActions, 8);
        var apps = MobileButton("Apps", "apps", () => _ = ShowMobileLauncherAsync(), 8);
        var notifications = MobileIconButton("notification", ShowMobileNotifications, "Alerts");

        var firstRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
            ColumnSpacing = 6,
            Margin = new Thickness(8, 8, 8, 4)
        };
        firstRow.Children.Add(_mobileModelSelectorButton);
        Grid.SetColumn(actions, 1);
        Grid.SetColumn(apps, 2);
        Grid.SetColumn(notifications, 3);
        firstRow.Children.Add(actions);
        firstRow.Children.Add(apps);
        firstRow.Children.Add(notifications);

        _mobileTabs = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            Margin = new Thickness(8, 0, 8, 8)
        };
        var tabsScroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _mobileTabs
        };

        return new Border
        {
            Background = ResourceBrush("HavenElevatedBrush"),
            BorderBrush = ResourceBrush("HavenLineBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new StackPanel
            {
                Spacing = 2,
                Children = { firstRow, tabsScroller }
            }
        };
    }""",
    "BuildMobileHeader",
)
write(layout_path, layout)

interactions_path = "src/Haven.Android/MainView.Mobile.Interactions.cs"
interactions = read(interactions_path)
interactions = replace_once(
    interactions,
    "    public Task ApplyMobileStartupSurfaceAsync() => OpenNewChatAsync();",
    "    public Task ApplyMobileStartupSurfaceAsync() => OpenHomeAsync();",
    "mobile startup routes to Go",
)
interactions = replace_once(
    interactions,
    """        if (string.Equals(surface, "dashboard", StringComparison.OrdinalIgnoreCase))
        {
            await OpenDashboardAsync();
            return;
        }
""",
    """        if (string.Equals(surface, "dashboard", StringComparison.OrdinalIgnoreCase)
            || string.Equals(surface, "home", StringComparison.OrdinalIgnoreCase)
            || string.Equals(surface, "go", StringComparison.OrdinalIgnoreCase))
        {
            await OpenHomeAsync();
            return;
        }
""",
    "mobile home request routes to Go",
)
interactions = replace_method(
    interactions,
    "    private void RefreshMobileChrome()",
    r"""    private void RefreshMobileChrome()
    {
        if (!_mobileLayoutApplied)
            return;

        var isHome = CurrentSurface == HavenSurface.Home;
        var showChatAffordance = CurrentSurface == HavenSurface.Chat;

        SidebarControl.IsVisible = false;
        NativeSidebarHost.IsVisible = false;
        ShellContextBar.IsVisible = false;

        if (_mobileHeader is not null)
            _mobileHeader.IsVisible = !isHome;
        if (_mobileBottomAffordance is not null)
            _mobileBottomAffordance.IsVisible = showChatAffordance;
        if (_mobileHomeFooter is not null)
            _mobileHomeFooter.IsVisible = false;

        PageContent.Margin = showChatAffordance
            ? new Thickness(0, 0, 0, 106)
            : new Thickness(0);

        if (_mobileModelName is not null)
            _mobileModelName.Text = ModelNameText.Text ?? _preferences.DefaultModel ?? "Model";
        if (_mobileModelContext is not null)
            _mobileModelContext.Text = ContextPercentText.Text ?? string.Empty;

        RefreshMobileTabs();
    }""",
    "RefreshMobileChrome",
)
interactions = replace_method(
    interactions,
    "    private void RefreshMobileTabs()",
    r"""    private void RefreshMobileTabs()
    {
        if (_mobileTabs is null)
            return;

        _mobileTabs.Children.Clear();
        foreach (var tab in OpenTabs)
        {
            var selected = tab;
            var isSelected = ReferenceEquals(tab, SelectedTab);

            var title = new TextBlock
            {
                Text = tab.Title,
                FontSize = 14,
                FontWeight = isSelected ? FontWeight.Bold : FontWeight.SemiBold,
                MaxWidth = 180,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            var label = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new HavenIcon
                    {
                        IconKey = IconForSurface(tab.Surface),
                        Width = 16,
                        Height = 16,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    title
                }
            };
            var underline = new Border
            {
                Height = 3,
                Margin = new Thickness(8, 0),
                CornerRadius = new CornerRadius(2),
                Background = isSelected
                    ? ResourceBrush("HavenAccentBrush")
                    : Brushes.Transparent
            };
            var content = new Grid
            {
                RowDefinitions = new RowDefinitions("*,3")
            };
            content.Children.Add(label);
            Grid.SetRow(underline, 1);
            content.Children.Add(underline);

            var button = new Button
            {
                Content = content,
                MinWidth = 92,
                MaxWidth = 230,
                Height = 48,
                Padding = new Thickness(12, 5),
                CornerRadius = new CornerRadius(11),
                BorderThickness = new Thickness(0),
                Background = isSelected
                    ? ResourceBrush("HavenAccentSoftBrush")
                    : Brushes.Transparent
            };
            button.Click += (_, _) => SelectedTab = selected;

            if (tab.IsCloseable)
            {
                var menu = new MenuFlyout();
                var close = new MenuItem { Header = "Close tab" };
                close.Click += (_, _) => CloseTabhselected);
                menu.Items.Add(close);
                button.ContextFlyout = menu;
            }

            _mobileTabs.Children.Add(button);
        }

        var add = MobileIconButton("plus", () =>
        {
            if (AddNewTabCommand.CanExecute(null))
                AddNewTabCommand.Execute(null);
        }, "New tab");
        add.MinHeight = 42;
        add.MinWidth = 42;
        add.CornerRadius = new CornerRadius(11);
        add.BorderBrush = ResourceBrush("HavenAccentBorderBrush");
        add.BorderThickness = new Thickness(1);
        _mobileTabs.Children.Add(add);
    }""",
    "RefreshMobileTabs",
)
write(interactions_path, interactions)

drawers_path = "src/Haven.Android/MainView.Mobile.Drawers.cs"
drawers = read(drawers_path)
drawers = replace_method(
    drawers,
    "    private void ShowMobileActions()",
    r"""    private void ShowMobileActions()
    {
        if (_mobileDrawerContent is null)
            return;

        _mobileDrawerContent.Children.Clear();
        AddDrawerHeading([_mobileDrawerContent, "Actions");
        _mobileDrawerContent.Children.Add(new TextBlock
        {
            Text = "The same action catalogue used by Haven desktop.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Foreground = ResourceBrush("HavenTextSoftBrush"),
            Margin = new Thickness(4, 0, 4, 8)
        });

        AddMobileAction(
            "Voice session",
            "Start a live voice session in Chat.",
            "call",
            () =>
            {
                CloseMobileDrawer();
                _ = OpenVoiceSessionFromActionAsync();
            });
        AddMobileAction(
            "Open notifications",
            "Review priority and unread notifications.",
            "notification",
            () =>
            {
                CloseMobileDrawer();
                ShowMobileNotifications();
            });
        AddMobileAction(
            "Open App (In New Tab)",
            "Choose a Haven app without replacing this tab.",
            "apps",
            () =>
            {
                CloseMobileDrawer();
                _ = ShowAppLauncherAsync(true);
            });
        AddMobileAction(
            "Open App (Current Tab)",
            "Choose a Haven app for the current tab.",
            "apps",
            () =>
            {
                CloseMobileDrawer();
                _ = ShowAppLauncherAsync(false);
            });

        foreach (var item in AllCommandItems)
        {
            var selected = item;
            var detail = string.IsNullOrWhiteSpace(selected.Shortcut)
                ? selected.Description
                : $"{selected.Description} µ {selected.Shortcut}";
            AddMobileAction(
                selected.Name,
                detail,
                ActionIcon(selected.Name),
                () =>
                {
                    CloseMobileDrawer();
                    if (selected.RunCommand.CanExecute(null))
                        selected.RunCommand.Execute(null);
                });
        }

        OpenMobileDrawer();
    }""",
    "ShowMobileActions",
)
write(drawers_path, drawers)

main_path = "src/Haven.Desktop/Views/Shell/MainView.axaml.cs"
main = read(main_path)
main = replace_once(
    main,
    "        ModelSelectorButton.IsEnabled = false;\n",
    "spacer",
    "model selector anchor initialization",
)
