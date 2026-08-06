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
    private readonly TextBlock _title = Text(string.Empty, 38, FontWeight.Bold);
    private readonly TextBlock _sidebarProjectName = Text(string.Empty, 18, FontWeight.Bold);
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
    private Grid _root = null!;
    private Border _settingsOverlay = null!;
    private Border _settingsDialog = null!;
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
        KeyDown += OnViewKeyDown;

        DataContextChanged += (_, _) => AttachPage();
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(AttachPage);
        DetachedFromVisualTree += (_, _) => DetachPage();
    }

    private Control BuildLayout()
    {
        _root = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("300,*"),
            Background = Brushes.Transparent
        };

        _root.Children.Add(BuildSidebar());
        var main = BuildMain();
        Grid.SetColumn(main, 1);
        _root.Children.Add(main);

        _settingsDialog = new Border
        {
            MinWidth = 720,
            MaxWidth = 1120,
            MaxHeight = 760,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = PanelBrush,
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(36),
            Padding = new Thickness(46)
        };
        _settingsOverlay = new Border
        {
            IsVisible = false,
            Background = new SolidColorBrush(Color.FromArgb(88, 18, 24, 27)),
            Padding = new Thickness(54, 34),
            Child = _settingsDialog
        };
        Grid.SetColumnSpan(_settingsOverlay, 2);
        Panel.SetZIndex(_settingsOverlay, 100);
        _root.Children.Add(_settingsOverlay);
        return _root;
    }

    private Control BuildSidebar()
    {
        var back = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new HavenIcon { IconKey = "chevron-left", Width = 20, Height = 20 },
                    Text("All Projects", 14, FontWeight.Bold)
                }
            },
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
            Background = PanelBrush,
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

        var resultsScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = _mainResults
        };

        var add = RoundIconButton("plus", "Show included project context");
        add.Click += (_, _) => ShowContextFlyout(add);
        var send = RoundIconButton("send", "Start project chat (Ctrl+Enter)");
        send.Background = AccentBrush;
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

        _title.Text = page.ProjectName;
        _status.Text = page.Status;
        _sidebarProjectName.Text = page.ProjectName;

        ApplyNavigationSelection(_homeButton, !page.IsInConfigureMode);
        ApplyNavigationSelection(_settingsButton, page.IsInConfigureMode);
        _mainSearch.IsVisible = true;
        _composer.IsEnabled = !page.IsInConfigureMode;
        RenderNavigationLists();
        RenderMainResults();
        RenderSettingsOverlay(page);
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
                    Text("Manage Project Settings and Context.", 13, FontWeight.Bold)
                }
            },
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = AccentSoftBrush,
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

    private void RenderSettingsOverlay(StudioProjectPage page)
    {
        _settingsOverlay.IsVisible = page.IsInConfigureMode;
        if (!page.IsInConfigureMode)
        {
            return;
        }

        var name = new TextBox
        {
            Text = page.ProjectNameDraft,
            MinHeight = 54,
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(16),
            Background = PanelBrush,
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(1),
            FontSize = 16,
            FontWeight = FontWeight.Bold
        };
        name.TextChanged += (_, _) => page.ProjectNameDraft = name.Text ?? string.Empty;

        var context = new TextBox
        {
            Text = page.ProjectContextDraft,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 190,
            MaxHeight = 300,
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(16),
            Background = PanelBrush,
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(1),
            FontSize = 15
        };
        context.TextChanged += (_, _) => page.ProjectContextDraft = context.Text ?? string.Empty;

        var generate = new Button
        {
            Content = "Generate Context from Chats",
            Background = AccentSoftBrush,
            Foreground = TextBrush,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(20, 12),
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        generate.Click += async (_, _) =>
        {
            await page.GenerateProjectContextCommand.ExecuteAsync();
            context.Text = page.ProjectContextDraft;
            _status.Text = page.ConfigureStatus;
        };

        var cancel = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(24, 12),
            CornerRadius = new CornerRadius(22),
            Background = Panel2Brush,
            Foreground = DangerBrush,
            FontWeight = FontWeight.Bold
        };
        cancel.Click += (_, _) =>
        {
            page.CancelProjectSettingsCommand.Execute(null);
            RefreshAll();
        };
        var save = new Button
        {
            Content = "Save",
            Padding = new Thickness(28, 12),
            CornerRadius = new CornerRadius(22),
            Background = AccentBrush,
            Foreground = AccentInkBrush,
            FontWeight = FontWeight.Bold
        };
        save.Click += async (_, _) =>
        {
            await page.SaveProjectSettingsCommand.ExecuteAsync();
            RefreshAll();
        };

        var dialogTitle = Text("Project Settings", 38, FontWeight.Bold);
        dialogTitle.HorizontalAlignment = HorizontalAlignment.Center;

        var nameLabel = Text("Name", 14, FontWeight.Bold);
        nameLabel.VerticalAlignment = VerticalAlignment.Center;
        var nameRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("96,*"),
            ColumnSpacing = 14,
            Children = { nameLabel, Column(name, 1) }
        };

        var contextLabel = Text("Context", 14, FontWeight.Bold);
        contextLabel.VerticalAlignment = VerticalAlignment.Top;
        contextLabel.Margin = new Thickness(0, 16, 0, 0);
        var contextColumn = new StackPanel
        {
            Spacing = 10,
            Children = { context, generate }
        };
        var contextRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("96,*"),
            ColumnSpacing = 14,
            Children = { contextLabel, Column(contextColumn, 1) }
        };

        var fields = new StackPanel
        {
            Spacing = 16,
            Children =
            {
                dialogTitle,
                nameRow,
                contextRow,
                Muted($"Folder: {page.RootPath}\nRepository: {page.Branch} · {page.WorkState}\nIncluded: {page.Files.Count} files · {page.ProjectConversations.Count} chats")
            }
        };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Children = { cancel, save }
        };
        Grid.SetRow(actions, 1);

        _settingsDialog.Child = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            RowSpacing = 18,
            Children =
            {
                new ScrollViewer
                {
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                    Content = fields
                },
                actions
            }
        };
    }

    private void OnViewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _page?.IsInConfigureMode != true)
        {
            return;
        }

        _page.CancelProjectSettingsCommand.Execute(null);
        RefreshAll();
        e.Handled = true;
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
            Background = PanelBrush,
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
        Background = PanelBrush,
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
            ? AccentSoftBrush
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

    private static IBrush PanelBrush => ResourceBrush("HavenPanelBrush", Color.Parse("#FFF4F4F2"));
    private static IBrush Panel2Brush => ResourceBrush("HavenPanel2Brush", Color.Parse("#FFF8F9F7"));
    private static IBrush LineBrush => ResourceBrush("HavenLineBrush", Color.FromArgb(30, 0, 0, 0));
    private static IBrush AccentBrush => ResourceBrush("HavenAccentBrush", Color.Parse("#FF00A7B3"));
    private static IBrush AccentSoftBrush => ResourceBrush("HavenAccentSoftBrush", Color.Parse("#FFDCF7F8"));
    private static IBrush AccentInkBrush => ResourceBrush("HavenAccentInkBrush", Colors.White);
    private static IBrush TextBrush => ResourceBrush("HavenTextBrush", Colors.Black);
    private static IBrush DangerBrush => ResourceBrush("HavenDangerBrush", Color.Parse("#FF9B1212"));

    private static IBrush ResourceBrush(string key, Color fallback) =>
        Avalonia.Application.Current?.Resources[key] as IBrush ?? new SolidColorBrush(fallback);
}
