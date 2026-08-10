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

public sealed partial class NewChatPage
{
    private bool _androidMobileCompositionApplied;
    private Point? _androidChatbarPointerStart;
    private Flyout? _androidChatSheet;
    private Border? _androidModeSwitcher;

    public void ApplyAndroidMobileComposition()
    {
        SurfaceGrid.ColumnDefinitions = new ColumnDefinitions("*");
        TasksSidebarHost.IsVisible = false;
        SurfaceTitle.IsVisible = false;

        var main = SurfaceGrid.Children.OfType<Grid>().FirstOrDefault();
        if (main is null)
            return;

        Grid.SetColumn(main, 0);
        main.Margin = new Thickness(10, 8, 10, 12);
        main.RowSpacing = 8;

        EnsureAndroidModeSwitcher(main);

        var composerStack = main.Children
            .OfType<StackPanel>()
            .FirstOrDefault(candidate => Grid.GetRow(candidate) == 2);

        if (composerStack is null)
            return;

        var composerRow = composerStack.Children.OfType<Grid>().LastOrDefault();
        if (composerRow is not null)
        {
            composerRow.MaxWidth = double.PositiveInfinity;
            AddButton.Width = 48;
            AddButton.Height = 48;
            SendButton.Width = 52;
            SendButton.Height = 48;
            InstructionBox.MinWidth = 0;
            InstructionBox.MinHeight = 48;
            InstructionBox.Padding = new Thickness(14, 0);
        }

        EnsureAndroidChatbarGrip(composerStack);

        if (_androidMobileCompositionApplied)
        {
            RefreshAndroidModeSwitcher();
            return;
        }

        _androidMobileCompositionApplied = true;
        composerStack.PointerPressed += OnAndroidChatbarPointerPressed;
        composerStack.PointerReleased += OnAndroidChatbarPointerReleased;
        ConversationStateChanged += (_, _) => RefreshAndroidModeSwitcher();
        RefreshAndroidModeSwitcher();
    }

    private void EnsureAndroidModeSwitcher(Grid main)
    {
        if (_androidModeSwitcher is not null)
            return;

        var modes = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        modes.Children.Add(AndroidModeButton("Chat", HavenMode.Chat));
        modes.Children.Add(AndroidModeButton("Study", HavenMode.Study));
        modes.Children.Add(AndroidModeButton("Research", HavenMode.Tasks));

        _androidModeSwitcher = new Border
        {
            Name = "AndroidNewChatModeSwitcher",
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(2),
            Margin = new Thickness(0, 0, 0, 4),
            CornerRadius = new CornerRadius(16),
            Background = ResourceBrush("HavenPanelBrush", Colors.White),
            Child = modes
        };

        Grid.SetRow(_androidModeSwitcher, 0);
        main.Children.Add(_androidModeSwitcher);
    }

    private Button AndroidModeButton(string label, HavenMode mode)
    {
        var button = new Button
        {
            Tag = mode,
            Content = label,
            MinHeight = 34,
            Padding = new Thickness(11, 6),
            CornerRadius = new CornerRadius(14)
        };

        button.Click += async (_, _) => await SelectAndroidMobileModeAsync(mode);
        return button;
    }

    private async Task SelectAndroidMobileModeAsync(HavenMode mode)
    {
        _modeDefinition = null;
        _isTaskMode = mode == HavenMode.Tasks;
        TasksSidebarHost.IsVisible = false;
        SurfaceGrid.ColumnDefinitions = new ColumnDefinitions("*");

        await StartFreshConversationAsync(mode, null);

        InstructionBox.PlaceholderText = mode switch
        {
            HavenMode.Study => "Ask Haven to study, explain, quiz, or revise",
            HavenMode.Tasks => "Research, compare, investigate, or plan",
            _ => "Ask Haven Anything"
        };

        RefreshAndroidModeSwitcher();
    }

    private void RefreshAndroidModeSwitcher()
    {
        if (_androidModeSwitcher?.Child is not StackPanel modes)
            return;

        foreach (var button in modes.Children.OfType<Button>())
        {
            button.Classes.Remove("accent");
            button.Classes.Remove("chip");
            button.Classes.Add(button.Tag is HavenMode mode && mode == _conversation.Mode ? "accent" : "chip");
        }
    }

    private static void EnsureAndroidChatbarGrip(StackPanel composerStack)
    {
        if (composerStack.Children
            .OfType<Border>()
            .Any(item => string.Equals(item.Name, "AndroidNewChatbarGrip", StringComparison.Ordinal)))
        {
            return;
        }

        var grip = new Border
        {
            Name = "AndroidNewChatbarGrip",
            Width = 42,
            Height = 4,
            CornerRadius = new CornerRadius(3),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 2),
            Background = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)),
            IsHitTestVisible = false
        };

        composerStack.Children.Insert(0, grip);
    }

    private void OnAndroidChatbarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control target)
            _androidChatbarPointerStart = e.GetPosition(target);
    }

    private async void OnAndroidChatbarPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not Control target || _androidChatbarPointerStart is not { } start)
            return;

        var end = e.GetPosition(target);
        _androidChatbarPointerStart = null;

        if (start.Y - end.Y < 36)
            return;

        e.Handled = true;
        await ShowAndroidChatSheetAsync(target);
    }

    private async Task ShowAndroidChatSheetAsync(Control target)
    {
        _androidChatSheet?.Hide();

        IEnumerable<Conversation> recent = Array.Empty<Conversation>();
        try
        {
            recent = await _conversations.GetRecentAsync(_conversation.Mode, 24, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusText.Text = "Recent chats could not be loaded.";
        }

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
            Background = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0))
        });

        panel.Children.Add(new TextBlock
        {
            Text = "Chats",
            FontSize = 18,
            FontWeight = FontWeight.ExtraBold
        });

        var quick = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 8
        };

        quick.Children.Add(AndroidSheetButton("New chat", async () =>
        {
            _androidChatSheet?.Hide();
            await StartFreshConversationAsync(_conversation.Mode, null);
        }));

        var groupLabel = _conversation.Mode switch
        {
            HavenMode.Study => "New subject",
            HavenMode.Tasks => "New research group",
            _ => "New chat group"
        };
        var newGroup = AndroidSheetButton(groupLabel, () =>
        {
            _androidChatSheet?.Hide();
            if (this.FindAncestorOfType<MainView>() is { } shell
                && shell.NewContainerCommand.CanExecute(null))
            {
                shell.NewContainerCommand.Execute(null);
            }
            return Task.CompletedTask;
        });

        Grid.SetColumn(newGroup, 1);
        quick.Children.Add(newGroup);
        panel.Children.Add(quick);

        var visibleRecent = recent
            .Where(item => !item.IsArchived)
            .OrderByDescending(item => item.UpdatedAt)
            .Take(16)
            .ToArray();

        if (visibleRecent.Length > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "RECENT CHATS",
                FontSize = 10,
                FontWeight = FontWeight.SemiBold,
                Opacity = 0.58,
                Margin = new Thickness(3, 6, 0, 0)
            });

            foreach (var conversation in visibleRecent)
            {
                var captured = conversation;
                var button = new Button
                {
                    Content = string.IsNullOrWhiteSpace(captured.Title) ? "Untitled chat" : captured.Title,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(10, 8)
                };
                button.Classes.Add("sidebar");
                button.Click += async (_, _) =>
                {
                    _androidChatSheet?.Hide();
                    await LoadConversationAsync(captured);
                };
                panel.Children.Add(button);
            }
        }

        var topLevel = TopLevel.GetTopLevel(this);
        var availableWidth = topLevel?.Bounds.Width > 0 ? topLevel.Bounds.Width - 16 : 344;
        var availableHeight = topLevel?.Bounds.Height > 0 ? topLevel.Bounds.Height * 0.72 : 520;

        var content = new Border
        {
            Width = Math.Clamp(availableWidth, 280, 420),
            MaxHeight = Math.Max(300, availableHeight),
            Background = ResourceBrush("HavenElevatedBrush", Colors.White),
            BorderBrush = ResourceBrush("HavenLineBrush", Color.FromArgb(24, 0, 0, 0)),
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
            Content = content
        };

        _androidChatSheet.ShowAt(target);
    }

    private static Button AndroidSheetButton(string label, Func<Task> action)
    {
        var button = new Button
        {
            Content = label,
            MinHeight = 42,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };

        button.Classes.Add("sidebar");
        button.Click += async (_, _) => await action();
        return button;
    }
}
