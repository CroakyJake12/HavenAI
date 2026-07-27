using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Pages.StudioProject;

namespace Haven.Desktop.Views;

/// <summary>
/// Native project landing page built entirely in code-behind. The page consumes
/// repository-backed conversations, files, and project intelligence from
/// <see cref="StudioProjectPage"/> without using Avalonia view bindings.
/// </summary>
public sealed partial class StudioProjectView : UserControl
{
    private readonly Grid _host;
    private readonly StackPanel _sidebarChats = new() { Spacing = 4 };
    private readonly StackPanel _sidebarFiles = new() { Spacing = 4 };
    private readonly StackPanel _mainResults = new() { Spacing = 10 };
    private readonly TextBlock _title = Text(string.Empty, 30, FontWeight.Bold);
    private readonly TextBlock _sidebarProjectName = Text(string.Empty, 16, FontWeight.Bold);
    private readonly TextBlock _status = Text(string.Empty, 11, FontWeight.Normal);
    private readonly TextBox _sideSearch = SearchBox("Search");
    private readonly TextBox _mainSearch = SearchBox("Search Project");
    private readonly TextBox _composer = new()
    {
        PlaceholderText = "Start New Chat",
        MinHeight = 58,
        Padding = new Thickness(18),
        CornerRadius = new CornerRadius(22),
        VerticalContentAlignment = VerticalAlignment.Center
    };
    private readonly Button _homeButton = NavigationButton("Project Home", "home");
    private readonly Button _settingsButton = NavigationButton("Project Settings", "settings");
    private StudioProjectPage? _page;
    private bool _syncingSearch;

    public StudioProjectView()
    {
        InitializeComponent();
        _host = this.FindControl<Grid>("CodeBehindHost")
            ?? throw new InvalidOperationException("Project landing host was not initialized.");
        _host.Children.Add(BuildLayout());

        _sideSearch.TextChanged += (_, _) => SynchronizeSearch(_sideSearch, _mainSearch);
        _mainSearch.TextChanged += (_, _) => SynchronizeSearch(_mainSearch, _sideSearch);
        _composer.KeyDown += OnComposerKeyDown;
        _homeButton.Click += (_, _) =>
        {
            _page?.SwitchToOverviewCommand.Execute(null);
            RefreshAll();
        };
        _settingsButton.Click += (_, _) =>
        {
            _page?.SwitchToConfigureCommand.Execute(null);
            RefreshAll();
        };

        DataContextChanged += (_, _) => AttachPage();
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(AttachPage);
        DetachedFromVisualTree += (_, _) => DetachPage();
    }

    private Control BuildLayout()
    {
        var root = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("300,*"),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#FFFFFF"), 0),
                    new GradientStop(Color.Parse("#E8FFF0"), 0.65),
                    new GradientStop(Color.Parse("#CBFAFB"), 1)
                }
            }
        };

        root.Children.Add(BuildSidebar());
        var main = BuildMain();
        Grid.SetColumn(main, 1);
        root.Children.Add(main);
        return root;
    }

    private Control BuildSidebar()
    {
        var back = new Button
        {
            Content = "â†  All Projects",
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeight.Bold
        };
        back.Click += (_, _) => Execute(_page?.BackToProjectsCommand);

        var scrollContent = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                _homeButton,
                Heading("Project Chats"),
                _sidebarChats,
                Heading("Project Files"),
                _sidebarFiles
            }
        };

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"),
            RowSpacing = 10
        };
        layout.Children.Add(back);
        Grid.SetRow(_sidebarProjectName, 1);
        layout.Children.Add(_sidebarProjectName);
        Grid.SetRow(_sideSearch, 2);
        layout.Children.Add(_sideSearch);
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = scrollContent
        };
        Grid.SetRow(scroll, 3);
        layout.Children.Add(scroll);
        Grid.SetRow(_settingsButton, 4);
        layout.Children.Add(_settingsButton);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(225, 255, 255, 255)),
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(18),
            Child = layout
        };
    }

    private Control BuildMain()
    {
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12
        };
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        header.Children.Add(_title);
        var modelChip = new Border
        {
            CornerRadius = new CornerRadius(18),
            Background = PanelBrush,
            Padding = new Thickness(18, 9),
            Child = Text("Project context", 11, FontWeight.Bold)
        };
        Grid.SetColumn(modelChip, 1);
        header.Children.Add(modelChip);

        var resultsScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = _mainResults
        };

        var add = RoundIconButton("plus", "Show included project context");
        add.Click += (_, _) => ShowContextFlyout(add);
        var send = RoundIconButton("send", "Start project chat (Ctrl+Enter)");
        send.Background = new SolidColorBrush(Color.Parse("#62E6EF"));
        send.Click += async (_, _) => await SubmitComposerAsync();
        var composer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 10
        };
        composer.Children.Add(add);
        Grid.SetColumn(_composer, 1);
        composer.Children.Add(_composer);
        Grid.SetColumn(send, 2);
        composer.Children.Add(send);

        var body = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto"),
            RowSpacing = 14,
            Margin = new Thickness(48, 28, 48, 26)
        };
        body.Children.Add(header);
        Grid.SetRow(_mainSearch, 1);
        body.Children.Add(_mainSearch);
        Grid.SetRow(resultsScroll, 2);
        body.Children.Add(resultsScroll);
        Grid.SetRow(_status, 3);
        body.Children.Add(_status);
        Grid.SetRow(composer, 4);
        body.Children.Add(composer);
        return body;
    }

    private void AttachPage()
    {
        if (ReferenceEquals(_page, DataContext))
        {
            RefreshAll();
            return;
        }

        DetachPage();
        _page = DataContext as StudioProjectPage;
        if (_page is not null)
        {
            ((INotifyPropertyChanged)_page).PropertyChanged += OnPagePropertyChanged;
            _page.Files.CollectionChanged += OnCollectionChanged;
            _page.ProjectConversations.CollectionChanged += OnCollectionChanged;
        }

        RefreshAll();
    }

    private void DetachPage()
    {
        if (_page is null)
        {
            return;
        }

        ((INotifyPropertyChanged)_page).PropertyChanged -= OnPagePropertyChanged;
        _page.Files.CollectionChanged -= OnCollectionChanged;
        _page.ProjectConversations.CollectionChanged -= OnCollectionChanged;
        _page = null;
    }

    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        Dispatcher.UIThread.Post(RefreshAll);

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.UIThread.Post(RefreshAll);

    private void SynchronizeSearch(TextBox source, TextBox target)
    {
        if (_syncingSearch)
        {
            return;
        }

        _syncingSearch = true;
        try
        {
            target.Text = source.Text;
        }
        finally
        {
            _syncingSearch = false;
        }

        RenderNavigationLists();
        RenderMainResults();
    }

    private void RefreshAll()
    {
        var page = _page;
        if (page is null)
        {
            _title.Text = "Project unavailable";
            _status.Text = "The selected project could not be loaded.";
            _mainResults.Children.Clear();
            return;
        }

        _title.Text = page.IsInConfigureMode ? "Project Settings" : page.ProjectName;
        _status.Text = page.Status;
        _sidebarProjectName.Text = page.ProjectName;

        ApplyNavigationSelection(_homeButton, !page.IsInConfigureMode);
        ApplyNavigationSelection(_settingsButton, page.IsInConfigureMode);
        _mainSearch.IsVisible = !page.IsInConfigureMode;
        _composer.IsEnabled = !page.IsInConfigureMode;
        RenderNavigationLists();
        RenderMainResults();
    }

    private void RenderNavigationLists()
    {
        _sidebarChats.Children.Clear();
        _sidebarFiles.Children.Clear();
        var page = _page;
        if (page is null)
        {
            return;
        }

        var query = (_mainSearch.Text ?? string.Empty).Trim();
        foreach (var conversation in page.ProjectConversations
                     .Where(item => Matches(item.Title, query))
                     .Take(10))
        {
            var button = NavigationButton(conversation.Title, "chat");
            button.Click += (_, _) => Execute(page.OpenConversationCommand, conversation);
            _sidebarChats.Children.Add(button);
        }
        if (_sidebarChats.Children.Count == 0)
            _sidebarChats.Children.Add(Muted("No project chats yet."));

        foreach (var file in page.Files.Where(item => Matches(item.RelativePath, query)).Take(14))
        {
            var button = NavigationButton(file.Name, "file");
            ToolTip.SetTip(button, file.RelativePath);
            button.Click += (_, _) => Execute(page.OpenFileCommand, file);
            _sidebarFiles.Children.Add(button);
        }
        if (_sidebarFiles.Children.Count == 0)
            _sidebarFiles.Children.Add(Muted("No matching files."));
    }

    private void RenderMainResults()
    {
        _mainResults.Children.Clear();
        var page = _page;
        if (page is null)
        {
            return;
        }

        if (page.IsInConfigureMode)
        {
            RenderSettings(page);
            return;
        }

        var query = (_mainSearch.Text ?? string.Empty).Trim();
        foreach (var conversation in page.ProjectConversations.Where(item => Matches(item.Title, query)))
        {
            _mainResults.Children.Add(ResultRow(
                conversation.Title,
                $"Last active {RelativeTime(conversation.UpdatedAt)}",
                "chat",
                () => Execute(page.OpenConversationCommand, conversation)));
        }

        if (page.ProjectConversations.Count > 0 && page.Files.Count > 0)
            _mainResults.Children.Add(new Border { Height = 1, Background = LineBrush, Margin = new Thickness(80, 4) });

        foreach (var file in page.Files.Where(item => Matches(item.RelativePath, query)).Take(120))
        {
            _mainResults.Children.Add(ResultRow(
                file.Name,
                FileSize(file.FullPath),
                "file",
                () => Execute(page.OpenFileCommand, file)));
        }

        if (_mainResults.Children.Count == 0)
            _mainResults.Children.Add(EmptyCard(query.Length == 0
                ? "Start a project chat or connect files to this project."
                : "No project chats or files match that search."));

        var settings = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children =
                {
                    new HavenIcon { IconKey = "settings", Width = 20, Height = 20 },
                    Text("Manage Project Settings and Context", 13, FontWeight.Bold)
                }
            },
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Color.Parse("#C9F7F4")),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(18, 13)
        };
        settings.Click += (_, _) =>
        {
            page.SwitchToConfigureCommand.Execute(null);
            RefreshAll();
        };
        _mainResults.Children.Add(settings);
    }

    private void RenderSettings(StudioProjectPage page)
    {
        _mainResults.Children.Add(InfoCard("Project folder", page.RootPath));
        _mainResults.Children.Add(InfoCard("Repository", $"{page.Branch} Â· {page.WorkState}"));
        _mainResults.Children.Add(InfoCard("Last build", page.LastBuild));
        _mainResults.Children.Add(InfoCard(
            "Included project context",
            $"{page.Files.Count} files Â· {page.ProjectConversations.Count} chats"));

        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        AddCommandButton(actions, "Refresh", page.RefreshCommand);
        AddCommandButton(actions, "Build", page.BuildCommand);
        AddCommandButton(actions, "Test", page.TestCommand);
        AddCommandButton(actions, "Open editor", page.OpenEditorCommand);
        AddCommandButton(actions, "Open terminal", page.OpenTerminalCommand);
        _mainResults.Children.Add(Card(actions));

        var home = new Button { Content = "Back to Project Home", Padding = new Thickness(18, 11) };
        home.Click += (_, _) =>
        {
            page.SwitchToOverviewCommand.Execute(null);
            RefreshAll();
        };
        _mainResults.Children.Add(home);
    }

    private async Task SubmitComposerAsync()
    {
        var page = _page;
        if (page is null)
        {
            return;
        }

        var prompt = (_composer.Text ?? string.Empty).Trim();
        if (prompt.Length == 0)
            await page.StartChatCommand.ExecuteAsync();
        else
            await page.StartChatWithPromptCommand.ExecuteAsync(prompt);
        _composer.Text = string.Empty;
    }

    private async void OnComposerKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            await SubmitComposerAsync();
        }
    }

    private void ShowContextFlyout(Control target)
    {
        var page = _page;
        var panel = new StackPanel
        {
            Width = 280,
            Spacing = 6,
            Margin = new Thickness(10),
            Children =
            {
                Text("Project context is included", 13, FontWeight.Bold),
                Muted(page is null
                    ? "No active project."
                    : $"Haven will use {page.Files.Count} indexed project files, repository state, and this project's chats.")
            }
        };
        new Flyout { Content = panel }.ShowAt(target);
    }

    private static Border ResultRow(string title, string detail, string icon, Action action)
    {
        var button = new Button
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                ColumnSpacing = 12,
                Children =
                {
                    new HavenIcon { IconKey = icon, Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center },
                    Column(Text(title, 14, FontWeight.Bold), 1),
                    Column(Text(detail, 11, FontWeight.Bold), 2)
                }
            }
        };
        button.Click += (_, _) => action();
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(215, 255, 255, 255)),
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(18, 14),
            Child = button
        };
    }

    private static Border InfoCard(string title, string value) => Card(new StackPanel
    {
        Spacing = 4,
        Children = { Text(title, 12, FontWeight.Bold), Muted(value) }
    });

    private static Border EmptyCard(string message) => Card(Muted(message));

    private static Border Card(Control content) => new()
    {
        Background = new SolidColorBrush(Color.FromArgb(215, 255, 255, 255)),
        BorderBrush = LineBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(18),
        Padding = new Thickness(18),
        Child = content
    };

    private static void AddCommandButton(Panel panel, string label, ICommand command)
    {
        var button = new Button
        {
            Content = label,
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(16, 9)
        };
        button.Click += (_, _) => Execute(command);
        panel.Children.Add(button);
    }

    private static Button NavigationButton(string label, string icon) => new()
    {
        Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 9,
            Children =
            {
                new HavenIcon { IconKey = icon, Width = 20, Height = 20 },
                new TextBlock
                {
                    Text = label,
                    FontWeight = FontWeight.Bold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 210
                }
            }
        },
        HorizontalContentAlignment = HorizontalAlignment.Left,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(12, 9),
        CornerRadius = new CornerRadius(12)
    };

    private static void ApplyNavigationSelection(Button button, bool selected)
    {
        button.Background = selected
            ? new SolidColorBrush(Color.Parse("#C8F7F6"))
            : Brushes.Transparent;
    }

    private static TextBox SearchBox(string placeholder) => new()
    {
        PlaceholderText = placeholder,
        MinHeight = 48,
        Padding = new Thickness(14),
        CornerRadius = new CornerRadius(14)
    };

    private static Button RoundIconButton(string icon, string automationName)
    {
        var button = new Button
        {
            Content = new HavenIcon { IconKey = icon, Width = 24, Height = 24 },
            Width = 58,
            Height = 58,
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(29),
            Background = PanelBrush
        };
        AutomationProperties.SetName(button, automationName);
        return button;
    }

    private static TextBlock Heading(string value) => Text(value, 12, FontWeight.Bold);

    private static TextBlock Text(string value, double size, FontWeight weight) => new()
    {
        Text = value,
        FontSize = size,
        FontWeight = weight,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static TextBlock Muted(string value) => new()
    {
        Text = value,
        FontSize = 11,
        Opacity = 0.68,
        TextWrapping = TextWrapping.Wrap
    };

    private static Control Column(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private static bool Matches(string value, string query) =>
        query.Length == 0 || value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static string RelativeTime(DateTimeOffset time)
    {
        var elapsed = DateTimeOffset.UtcNow - time;
        if (elapsed.TotalMinutes < 1) return "just now";
        if (elapsed.TotalHours < 1) return $"{(int)elapsed.TotalMinutes}m ago";
        if (elapsed.TotalDays < 1) return $"{(int)elapsed.TotalHours}h ago";
        if (elapsed.TotalDays < 7) return $"{(int)elapsed.TotalDays}d ago";
        return time.LocalDateTime.ToString("d MMM");
    }

    private static string FileSize(string path)
    {
        try
        {
            var bytes = new FileInfo(path).Length;
            return bytes switch
            {
                < 1024 => $"{bytes} B",
                < 1024 * 1024 => $"{bytes / 1024d:0.#} KB",
                _ => $"{bytes / 1024d / 1024d:0.#} MB"
            };
        }
        catch
        {
            return "Size unavailable";
        }
    }

    private static void Execute(ICommand? command)
    {
        if (command?.CanExecute(null) == true)
            command.Execute(null);
    }

    private static void Execute<T>(ICommand command, T parameter)
    {
        if (command.CanExecute(parameter))
            command.Execute(parameter);
    }

    private static IBrush PanelBrush { get; } = new SolidColorBrush(Color.Parse("#F4F4F2"));
    private static IBrush LineBrush { get; } = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0));
}
