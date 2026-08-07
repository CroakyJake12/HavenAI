using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Haven.Core;
using Haven.Desktop.Views.Shell;

namespace Haven.Desktop.Views.Pages.Chat;

public sealed partial class ChatPage
{
    private bool _androidMobileCompositionApplied;
    private Point? _androidChatbarPointerStart;
    private Flyout? _androidChatSheet;

    public void ApplyAndroidMobileComposition()
    {
        if (!_finalUiBuilt || _finalComposer is null)
        {
            Dispatcher.UIThread.Post(ApplyAndroidMobileComposition);
            return;
        }

        if (_finalComposer.Parent is not Grid composerRow
            || composerRow.Parent is not Border composerSurface
            || composerSurface.Parent is not StackPanel composerStack
            || composerStack.Parent is not Grid main)
        {
            return;
        }

        if (main.Parent is Grid root)
        {
            root.ColumnDefinitions = new ColumnDefinitions("*");
            foreach (var child in root.Children)
            {
                if (!ReferenceEquals(child, main))
                    child.IsVisible = false;
            }
            Grid.SetColumn((main), 0);
        }

        var header = main.Children
            .OfType<Grid>()
            .FirstOrDefault(child => Grid.GetRow(child) == 0);
        if (header is not null)
            EnsureAndroidModeSwitcher(header);

        composerStack.Margin = new Thickness(8, 4, 8, 8);
        composerSurface.Padding = new Thickness(8);

        if (_androidMobileCompositionApplied)
            return;
        _androidMobileCompositionApplied = true;
        EnsureAndroidChatbarGrip(composerSurface, composerRow);
        composerSurface.PointerPressed += OnAndroidChatbarPointerPressed;
        composerSurface.PointerReleased += OnAndroidChatbarPointerReleased;
    }

    private void EnsureAndroidModeSwitcher(Grid header)
    {
        var existing = header.Children
            .OfType<Border>()
            .FirstOrDefault(item => string.Equals(
                item.Name,
                "AndroidChatModeSwitcher",
                StringComparison.Ordinal));

        if (existing is not null)
            return;

        foreach (var text in header.Children.OfType<TextBlock>())
            text.IsVisible = false;

        var modes = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        modes.Children.Add(AndroidModeButton("Chat", HavenMode.Chat, "chat"));
        modes.Children.Add(AndroidModeButton("Study", HavenMode.Study, "study"));
        modes.Children.Add(AndroidModeButton("Research", HavenMode.Tasks, "research"));

        header.Children.Add(new Border
        {
            Name = "AndroidChatModeSwitcher",
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(2),
            CornerRadius = new CornerRadius(16),
            Background = FinalBrush("HavenPanelBrush"),
            Child = modes
        });
    }

    private Button AndroidModeButton(string label, HavenMode mode, string route)
    {
        var button = new Button
        {
            Content = label,
            MinHeight = 34,
            Padding = new Thickness(11, 6),
            CornerRadius = new CornerRadius(14)
        };
        button.Classes.Add(Mode == mode ? "accent" : "chip");
        button.Click += async (_, _) =>
        {
            if (this.FindAncestorOfType<MainView>() is { } shell)
                await shell.SelectAndroidMobileConversationModeAsync(route);
        };
        return button;
    }

    private void EnsureAndroidChatbarGrip(Border composerSurface, Grid composerRow)
    {
        if (composerSurface.Child is Grid existing
            && existing.Name == "AndroidChatbarWrapper")
        {
            return;
        }

        composerSurface.Child = null;

        var grip = new Border
        {
            Width = 42,
            Height = 4,
            CornerRadius = new CornerRadius(3),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 5),
            Background = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)),
            IsHitTestVisible = false
        };

        var wrapper = new Grid
        {
            Name = "AndroidChatbarWrapper",
            RowDefinitions = new RowDefinitions("Auto,*")
        };
        wrapper.Children.Add(grip);
        Grid.SetRow(composerRow, 1);
        wrapper.Children.Add(composerRow);
        composerSurface.Child = wrapper;
    }

    private void OnAndroidChatbarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control target)
            _androidChatbarPointerStart = e.GetPosition(target);
    }

    private void OnAndroidChatbarPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not Control target || _androidChatbarPointerStart is not { } start)
            return;

        var end = e.GetPosition(target);
        _androidChatbarPointerStart = null;

        if (start.Y - end.Y >= 36)
        {
            e.Handled = true;
            ShowAndroidChatSheet(target);
        }
    }

    private void ShowAndroidChatSheet(Control target)
    {
        _androidChatSheet?.Hide();

        var panel = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(12)
        };
        panel.Children.Add(new Border
        {
            Width = 42,
            Height = 4,
            CornerRadius = new CornerRadius(3),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4),
            Background = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0))
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Chats",
            FontSize = 18,
            FontWeight = FontWeight.ExtraBold,
            Margin = new Thickness(2, 0, 0, 2)
        });

        var quickActions = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 8
        };
        quickActions.Children.Add(AndroidSheetButton("New chat", () =>
        {
            _androidChatSheet?.Hide();
            FinalExecuteFirstCommand(
                null,
                "NewChatCommand",
                "CreateConversationCommand",
                "NewConversationCommand");
            _finalComposer?.Focus();
        }));

        var containerLabel = FinalReadString(DataContext, "NewContainerLabel");
        if (string.IsNullOrWhiteSpace(containerLabel))
            containerLabel = "+ Chat group";
        containerLabel = containerLabel.Trim().TrimStart('+').Trim();
        var newGroup = AndroidSheetButton("New " + containerLabel.TrimStart('+').Trim().ToLowerInvariant(), () =>
        {
            _androidChatSheet?.Hide();
            FinalExecuteFirstCommand(
                null,
                "NewContainerCommand",
                "CreateContainerCommand",
                "NewChatGroupCommand");
        });
        Grid.SetColumn(newGroup, 1);
        quickActions.Children.Add(newGroup);
        panel.Children.Add(quickActions);

        AddAndroidSheetSection(
            panel,
            "Recent chats",
            FinalReadItems(DataContext, "Conversations", "Chats", "ChatItems").Take(12),
            "SelectConversationCommand",
            "OpenConversationCommand");

        AddAndroidSheetSection(
            panel,
            Mode == HavenMode.Study ? "Subjects" : Mode == HavenMode.Tasks ? "Research groups" : "Chat groups",
            FinalReadItems(DataContext, "Containers", "ChatGroups").Take(12),
            "SelectContainerCommand",
            "SelectChatGroupCommand");

        if (Mode == HavenMode.Study)
        {
            AddAndroidSheetSection(
                panel,
                "Lessons",
                FinalReadItems(DataContext, "Lessons").Take(12),
                "SelectLessonCommand");
        }

        var availableWidth = Bounds.Width > 0 ? Bounds.Width - 16 : 344;
        var border = new Border
        {
            Width = Math.Clamp(availableWidth, 280, 420),
            MaxHeight = Bounds.Height > 0 ? Math.Max(300, Bounds.Height * 0.70) : 520,
            Background = FinalBrush("HavenElevatedBrush"),
            BorderBrush = FinalBrush("HavenLineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(22),
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = panel
            }
        };

        _androidChatSheet = new Flyout
        {
            Placement = PlacementMode.TopEdgeAlignedLeft,
            Content = border
        };
        _androidChatSheet.ShowAt(target);
    }

    private Button AndroidSheetButton(string label, Action action)
    {
        var button = new Button
        {
            Content = label,
            MinHeight = 42,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        button.Classes.Add("sidebar");
        button.Click += (_, _) => action();
        return button;
    }

    private void AddAndroidSheetSection(
        StackPanel panel,
        string title,
        IEnumerable<object> items,
        params string[] commandNames)
    {
        var materialized = items.ToArray();
        if (materialized.Length == 0)
            return;

        panel.Children.Add(new TextBlock
        {
            Text = title.ToUpperInvariant(),
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            Opacity = 0.58,
            Margin = new Thickness(3, 6, 0, 0)
        });

        foreach (var item in materialized)
        {
            var label = FinalReadString(item, "Title", "Name", "DisplayName");
            if (string.IsNullOrWhiteSpace(label))
                continue;

            var button = new Button
            {
                Content = label,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 8)
            };
            button.Classes.Add("sidebar");
            button.Click += (_, _) =>
            {
                if (FinalExecuteFirstCommand(item, commandNames))
                    _androidChatSheet?.Hide();
            };
            panel.Children.Add(button);
        }
    }
}
