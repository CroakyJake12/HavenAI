using System;
using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Desktop.Views.Shell;

namespace Haven.Desktop.Views.Pages.Chat;

public sealed partial class ChatPage
{
    private bool _finalUiBuilt;
    private INotifyPropertyChanged? _finalStateNotifier;
    private TextBox? _finalSearchBox;
    private TextBox? _finalComposer;
    private StackPanel? _finalSidebarSections;
    private StackPanel? _finalMessages;
    private ScrollViewer? _finalMessageScroll;
    private Button? _finalSendButton;
    private Button? _finalStopButton;
    private Button? _finalResolveButton;
    private TextBlock? _finalModelLabel;
    private TextBlock? _finalReasoningLabel;
    private TextBlock? _finalEmptyTitle;
    private int _finalReasoningPercent = 25;

    private void OnFinalCodeBehindAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (!_finalUiBuilt)
        {
            _finalUiBuilt = true;
            BuildFinalChatUi();
            DataContextChanged += OnFinalDataContextChanged;
        }

        AttachFinalState();
        RefreshFinalChatUi();
    }

    private void OnFinalCodeBehindDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        DetachFinalState();
    }

    private void OnFinalDataContextChanged(object? sender, EventArgs e)
    {
        AttachFinalState();
        RefreshFinalChatUi();
    }

    private void AttachFinalState()
    {
        DetachFinalState();
        _finalStateNotifier = DataContext as INotifyPropertyChanged;
        if (_finalStateNotifier is not null)
        {
            _finalStateNotifier.PropertyChanged += OnFinalStatePropertyChanged;
        }
    }

    private void DetachFinalState()
    {
        if (_finalStateNotifier is not null)
        {
            _finalStateNotifier.PropertyChanged -= OnFinalStatePropertyChanged;
            _finalStateNotifier = null;
        }
    }

    private void OnFinalStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(RefreshFinalChatUi);
    }

    private void BuildFinalChatUi()
    {
        _finalSearchBox = new TextBox
        {
            PlaceholderText = "Search chats",
            MinHeight = 38,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _finalSearchBox.TextChanged += (_, _) => RefreshFinalSidebar();

        var newChatButton = new Button
        {
            Content = "New chat",
            HorizontalContentAlignment = HorizontalAlignment.Left,
            MinHeight = 40
        };
        newChatButton.Classes.Add("accent");
        newChatButton.Click += (_, _) =>
        {
            FinalExecuteFirstCommand(
                null,
                "NewChatCommand",
                "CreateConversationCommand",
                "NewConversationCommand");
            _finalComposer?.Focus();
        };

        var modeButton = new Button
        {
            Content = "Chat",
            HorizontalContentAlignment = HorizontalAlignment.Left,
            MinHeight = 38
        };
        modeButton.Classes.Add("chip");

        _finalSidebarSections = new StackPanel { Spacing = 12 };

        var sidebarStack = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(16)
        };
        sidebarStack.Children.Add(modeButton);
        sidebarStack.Children.Add(_finalSearchBox);
        sidebarStack.Children.Add(newChatButton);
        sidebarStack.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _finalSidebarSections
        });

        var sidebar = new Border
        {
            Width = 282,
            Background = FinalBrush("HavenPanelBrush"),
            BorderBrush = FinalBrush("HavenLineBrush"),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = sidebarStack
        };

        _finalModelLabel = FinalSmallLabel("Local model");
        _finalReasoningLabel = FinalSmallLabel("25%");

        var modelButton = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    _finalModelLabel,
                    new TextBlock { Text = "·", Opacity = 0.55 },
                    _finalReasoningLabel,
                    new TextBlock { Text = "⌄", Opacity = 0.62 }
                }
            },
            Padding = new Thickness(12, 6)
        };
        modelButton.Classes.Add("chip");
        modelButton.Click += (_, _) => ShowFinalModelMenu(modelButton);

        var title = new TextBlock
        {
            Text = "Chat",
            FontSize = 22,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        var headerActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { modelButton }
        };

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12,
            Margin = new Thickness(24, 18, 24, 10)
        };
        header.Children.Add(title);
        Grid.SetColumn(headerActions, 1);
        header.Children.Add(headerActions);

        _finalMessages = new StackPanel
        {
            Spacing = 16,
            Margin = new Thickness(28, 16, 28, 26)
        };
        _finalMessageScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _finalMessages
        };

        _finalEmptyTitle = new TextBlock
        {
            Text = "How can Haven help?",
            FontSize = 26,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.72
        };

        var conversationLayer = new Grid();
        conversationLayer.Children.Add(_finalEmptyTitle);
        conversationLayer.Children.Add(_finalMessageScroll);

        _finalResolveButton = new Button
        {
            Content = "Resolve Problems",
            HorizontalAlignment = HorizontalAlignment.Center,
            IsVisible = false
        };
        _finalResolveButton.Classes.Add("chip");
        _finalResolveButton.Click += (_, _) =>
            FinalExecuteFirstCommand(null, "ResolveErrorsCommand", "ResolveProblemsCommand");

        _finalComposer = new TextBox
        {
            PlaceholderText = "Ask Haven anything",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeight.Bold,
            MinHeight = 58,
            MaxHeight = 180,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _finalComposer.KeyDown += OnFinalComposerKeyDown;

        var addButton = new Button
        {
            Content = "+",
            MinWidth = 46,
            MinHeight = 46,
            FontSize = 22
        };
        addButton.Classes.Add("icon");
        addButton.Click += (_, _) => ShowFinalAddMenu(addButton);

        _finalStopButton = new Button
        {
            Content = "Stop",
            IsVisible = false,
            MinWidth = 68
        };
        _finalStopButton.Click += (_, _) =>
            FinalExecuteFirstCommand(null, "StopCommand", "CancelCommand");

        _finalSendButton = new Button
        {
            Content = "➤",
            MinWidth = 52,
            MinHeight = 46,
            FontSize = 20
        };
        _finalSendButton.Classes.Add("send");
        _finalSendButton.Click += (_, _) => SendFinalMessage();

        var composerRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
            ColumnSpacing = 10
        };
        composerRow.Children.Add(addButton);
        Grid.SetColumn(_finalComposer, 1);
        composerRow.Children.Add(_finalComposer);
        Grid.SetColumn(_finalStopButton, 2);
        composerRow.Children.Add(_finalStopButton);
        Grid.SetColumn(_finalSendButton, 3);
        composerRow.Children.Add(_finalSendButton);

        var composerSurface = new Border
        {
            Background = FinalBrush("HavenPanelBrush"),
            BorderBrush = FinalBrush("HavenLineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(12),
            Child = composerRow
        };

        var composerStack = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(24, 8, 24, 20),
            MaxWidth = 980,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        composerStack.Children.Add(_finalResolveButton);
        composerStack.Children.Add(composerSurface);

        var main = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto")
        };
        main.Children.Add(header);
        Grid.SetRow(conversationLayer, 1);
        main.Children.Add(conversationLayer);
        Grid.SetRow(composerStack, 2);
        main.Children.Add(composerStack);

        var root = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Background = FinalBrush("HavenBackgroundBrush")
        };
        root.Children.Add(sidebar);
        Grid.SetColumn(main, 1);
        root.Children.Add(main);

        CodeBehindHost.Children.Clear();
        CodeBehindHost.Children.Add(root);
    }

    private void RefreshFinalChatUi()
    {
        if (!_finalUiBuilt)
        {
            return;
        }

        RefreshFinalSidebar();
        RefreshFinalMessages();

        var model = FinalReadString(
            DataContext,
            "SelectedModel",
            "ModelName",
            "SelectedModelName");
        _finalModelLabel!.Text = string.IsNullOrWhiteSpace(model)
            ? "Local model"
            : model;

        _finalReasoningPercent = FinalNormalizeReasoning(FinalReadInt(
            DataContext,
            "ReasoningPercent",
            "ContextPercent",
            "SelectedReasoningPercent",
            "ReasoningLevel"));
        _finalReasoningLabel!.Text = $"{_finalReasoningPercent}%";

        var isSending = FinalReadBool(
            DataContext,
            false,
            "IsSending",
            "IsBusy",
            "IsGenerating");

        _finalSendButton!.IsVisible = !isSending;
        _finalStopButton!.IsVisible = isSending;
        _finalSendButton.IsEnabled = !string.IsNullOrWhiteSpace(_finalComposer?.Text);

        _finalResolveButton!.IsVisible = FinalReadBool(
            DataContext,
            false,
            "HasErrorsToResolve",
            "HasProblemsToResolve");
    }

    private void RefreshFinalSidebar()
    {
        if (_finalSidebarSections is null)
        {
            return;
        }

        _finalSidebarSections.Children.Clear();

        var query = _finalSearchBox?.Text?.Trim();
        var chats = FinalReadItems(DataContext, "Conversations", "Chats", "ChatItems")
            .Where(item => FinalMatches(item, query))
            .ToArray();

        var pinned = chats.Where(item => FinalReadBool(item, false, "IsPinned")).ToArray();
        var unread = chats.Where(item => FinalReadBool(item, false, "IsUnread", "HasUnread")).ToArray();
        var regular = chats.Except(pinned).Except(unread).ToArray();

        AddFinalSidebarSection(
            "Pinned",
            pinned,
            "SelectConversationCommand",
            "OpenConversationCommand");

        AddFinalSidebarSection(
            "Unread notifications",
            unread,
            "SelectConversationCommand",
            "OpenConversationCommand");

        AddFinalSidebarSection(
            "Chat groups",
            FinalReadItems(DataContext, "Containers", "ChatGroups")
                .Where(item => FinalMatches(item, query)),
            "SelectContainerCommand",
            "SelectChatGroupCommand");

        AddFinalSidebarSection(
            "Chats",
            regular,
            "SelectConversationCommand",
            "OpenConversationCommand");

        AddFinalSidebarSection(
            "Lessons",
            FinalReadItems(DataContext, "Lessons")
                .Where(item => FinalMatches(item, query)),
            "SelectLessonCommand");
    }

    private void AddFinalSidebarSection(
        string title,
        System.Collections.Generic.IEnumerable<object> items,
        params string[] commandNames)
    {
        var materialized = items.ToArray();
        if (materialized.Length == 0 || _finalSidebarSections is null)
        {
            return;
        }

        var section = new StackPanel { Spacing = 4 };
        section.Children.Add(new TextBlock
        {
            Text = title.ToUpperInvariant(),
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            Opacity = 0.58,
            Margin = new Thickness(4, 0, 0, 2)
        });

        foreach (var item in materialized)
        {
            var label = FinalReadString(item, "Title", "Name", "DisplayName");
            if (string.IsNullOrWhiteSpace(label))
            {
                continue;
            }

            var button = new Button
            {
                Content = label,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(9, 7)
            };
            button.Classes.Add("sidebar");
            button.Click += (_, _) => FinalExecuteFirstCommand(item, commandNames);
            section.Children.Add(button);
        }

        if (section.Children.Count > 1)
        {
            _finalSidebarSections.Children.Add(section);
        }
    }

    private void RefreshFinalMessages()
    {
        if (_finalMessages is null ||
            _finalMessageScroll is null ||
            _finalEmptyTitle is null)
        {
            return;
        }

        _finalMessages.Children.Clear();
        var messages = FinalReadItems(DataContext, "Messages").ToArray();
        _finalEmptyTitle.IsVisible = messages.Length == 0;
        _finalMessageScroll.IsVisible = messages.Length > 0;

        foreach (var message in messages)
        {
            var role = FinalReadString(message, "Role", "DisplayName");
            var content = FinalReadString(message, "Content", "Text", "Message");
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            var isUser = role.Contains("user", StringComparison.OrdinalIgnoreCase) ||
                         role.Contains("you", StringComparison.OrdinalIgnoreCase);

            var bubble = new Border
            {
                MaxWidth = 780,
                HorizontalAlignment = isUser
                    ? HorizontalAlignment.Right
                    : HorizontalAlignment.Left,
                Background = isUser
                    ? FinalBrush("HavenBlueSoftBrush")
                    : FinalBrush("HavenPanelBrush"),
                BorderBrush = FinalBrush("HavenLineBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = isUser
                    ? new CornerRadius(18, 18, 4, 18)
                    : new CornerRadius(18, 18, 18, 4),
                Padding = new Thickness(15, 12),
                Child = new TextBlock
                {
                    Text = content,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 14,
                    LineHeight = 21
                }
            };

            _finalMessages.Children.Add(bubble);
        }

    }

    private void OnFinalComposerKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter ||
            (e.KeyModifiers & KeyModifiers.Control) == 0)
        {
            return;
        }

        e.Handled = true;
        SendFinalMessage();
    }

    private void SendFinalMessage()
    {
        var text = _finalComposer?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        FinalSetFirstWritableProperty(
            DataContext,
            text,
            "Composer",
            "ComposerText",
            "InputText");

        if (FinalExecuteFirstCommand(null, "SendCommand", "SubmitCommand"))
        {
            _finalComposer!.Text = string.Empty;
        }
    }

    private void ShowFinalAddMenu(Control target)
    {
        var panel = new StackPanel
        {
            Width = 220,
            Spacing = 4,
            Margin = new Thickness(8)
        };
        panel.Children.Add(new TextBlock
        {
            Text = "Add",
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(4, 2)
        });

        AddFinalMenuButton(panel, "File", "AttachCommand", "OpenAttachmentPickerCommand");
        AddFinalMenuButton(panel, "Agent", "OpenAgentPickerCommand");
        AddFinalMenuButton(panel, "Plugin", "OpenPluginPickerCommand");
        AddFinalMenuButton(panel, "Instruction", "OpenPromptPickerCommand");
        AddFinalMenuButton(panel, "App", "OpenAppPickerCommand");

        new Flyout
        {
            Placement = PlacementMode.TopEdgeAlignedLeft,
            Content = panel
        }.ShowAt(target);
    }

    private void AddFinalMenuButton(
        StackPanel panel,
        string title,
        params string[] commandNames)
    {
        var button = new Button
        {
            Content = title,
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
        button.Classes.Add("sidebar");
        button.Click += (_, _) => FinalExecuteFirstCommand(null, commandNames);
        panel.Children.Add(button);
    }

    private void ShowFinalModelMenu(Control target)
    {
        var stack = new StackPanel
        {
            Width = 300,
            Spacing = 10,
            Margin = new Thickness(12)
        };
        stack.Children.Add(new TextBlock
        {
            Text = _finalModelLabel?.Text ?? "Local model",
            FontWeight = FontWeight.SemiBold
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Reasoning",
            FontSize = 11,
            Opacity = 0.64
        });

        var choices = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6
        };

        foreach (var percent in new[] { 25, 50, 75, 100 })
        {
            var button = new Button
            {
                Content = $"{percent}%",
                MinWidth = 58
            };
            if (percent == _finalReasoningPercent)
            {
                button.Classes.Add("accent");
            }

            button.Click += (_, _) =>
            {
                _finalReasoningPercent = percent;
                FinalSetFirstWritableProperty(
                    DataContext,
                    percent,
                    "ReasoningPercent",
                    "ContextPercent",
                    "SelectedReasoningPercent",
                    "ReasoningLevel");
                RefreshFinalChatUi();
            };
            choices.Children.Add(button);
        }

        stack.Children.Add(choices);

        new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            Content = stack
        }.ShowAt(target);
    }

    private bool FinalExecuteFirstCommand(object? parameter, params string[] commandNames)
    {
        foreach (var name in commandNames)
        {
            var command = FinalReadValue(DataContext, name) as ICommand;
            if (command is null || !command.CanExecute(parameter))
            {
                continue;
            }

            command.Execute(parameter);
            return true;
        }

        return false;
    }

    private static object? FinalReadValue(object? source, params string[] names)
    {
        if (source is null)
        {
            return null;
        }

        var type = source.GetType();
        foreach (var name in names)
        {
            var property = type.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public);
            if (property is not null)
            {
                return property.GetValue(source);
            }
        }

        return null;
    }

    private static string FinalReadString(object? source, params string[] names)
    {
        var value = FinalReadValue(source, names);
        return value?.ToString() ?? string.Empty;
    }

    private static bool FinalReadBool(object? source, bool fallback, params string[] names)
    {
        var value = FinalReadValue(source, names);
        return value is bool flag ? flag : fallback;
    }

    private static int FinalReadInt(object? source, params string[] names)
    {
        var value = FinalReadValue(source, names);
        return value switch
        {
            int integer => integer,
            double number => (int)Math.Round(number),
            float number => (int)Math.Round(number),
            decimal number => (int)Math.Round(number),
            _ when int.TryParse(value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 25
        };
    }

    private static System.Collections.Generic.IEnumerable<object> FinalReadItems(
        object? source,
        params string[] names)
    {
        var value = FinalReadValue(source, names);
        if (value is not IEnumerable enumerable || value is string)
        {
            return [];
        }

        return enumerable.Cast<object>();
    }

    private static bool FinalMatches(object item, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return FinalReadString(item, "Title", "Name", "DisplayName")
            .Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static bool FinalSetFirstWritableProperty(
        object? source,
        object value,
        params string[] names)
    {
        if (source is null)
        {
            return false;
        }

        var type = source.GetType();
        foreach (var name in names)
        {
            var property = type.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public);
            if (property is null || !property.CanWrite)
            {
                continue;
            }

            try
            {
                var targetType = Nullable.GetUnderlyingType(property.PropertyType) ??
                                 property.PropertyType;
                var converted = targetType.IsEnum
                    ? Enum.ToObject(targetType, value)
                    : Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
                property.SetValue(source, converted);
                return true;
            }
            catch
            {
                // Try the next compatible property name.
            }
        }

        return false;
    }

    private static int FinalNormalizeReasoning(int value)
    {
        var choices = new[] { 25, 50, 75, 100 };
        return choices.OrderBy(choice => Math.Abs(choice - value)).First();
    }

    private static TextBlock FinalSmallLabel(string text) =>
        new()
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

    private static IBrush? FinalBrush(string key) =>
        Avalonia.Application.Current?.TryFindResource(key, out var value) == true
            ? value as IBrush
            : null;
}
