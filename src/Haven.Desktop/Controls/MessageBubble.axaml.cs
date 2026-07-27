using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Haven.Core;

namespace Haven.Desktop.Controls;

/// <summary>
/// Reusable chat message bubble. Displays role label and markdown content.
/// </summary>
public sealed partial class MessageBubble : UserControl
{
    public static readonly StyledProperty<MessageRole> RoleProperty =
        AvaloniaProperty.Register<MessageBubble, MessageRole>(nameof(Role));

    public static readonly StyledProperty<string> MessageContentProperty =
        AvaloniaProperty.Register<MessageBubble, string>(nameof(MessageContent), string.Empty);

    public static readonly StyledProperty<string?> AgentNameProperty =
        AvaloniaProperty.Register<MessageBubble, string?>(nameof(AgentName));

    public static readonly StyledProperty<bool> IsStreamingProperty =
        AvaloniaProperty.Register<MessageBubble, bool>(nameof(IsStreaming));

    public MessageBubble()
    {
        InitializeComponent();
        UpdateVisualState();
    }

    public MessageRole Role
    {
        get => GetValue(RoleProperty);
        set => SetValue(RoleProperty, value);
    }

    public string MessageContent
    {
        get => GetValue(MessageContentProperty);
        set => SetValue(MessageContentProperty, value);
    }

    public string? AgentName
    {
        get => GetValue(AgentNameProperty);
        set => SetValue(AgentNameProperty, value);
    }

    public bool IsStreaming
    {
        get => GetValue(IsStreamingProperty);
        set => SetValue(IsStreamingProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RoleProperty ||
            change.Property == MessageContentProperty ||
            change.Property == AgentNameProperty ||
            change.Property == IsStreamingProperty)
        {
            UpdateVisualState();
        }
    }

    private void UpdateVisualState()
    {
        var isUser = Role == MessageRole.User;

        RoleLabel.Text = isUser ? "You" : "Haven";
        Body.Markdown = string.IsNullOrEmpty(MessageContent) && IsStreaming ? "Thinking…" : MessageContent;

        Bubble.MaxWidth = isUser ? 680 : 900;
        Bubble.HorizontalAlignment = isUser ? global::Avalonia.Layout.HorizontalAlignment.Right : global::Avalonia.Layout.HorizontalAlignment.Stretch;
        Bubble.Background = isUser
            ? ResourceBrush("HavenAccentSoftBrush", Color.Parse("#FFE0F7FA"))
            : new SolidColorBrush(Color.FromArgb(210, 255, 255, 255));
    }

    private static IBrush ResourceBrush(string key, Color fallback) =>
        Avalonia.Application.Current?.TryFindResource(key, out var value) == true && value is IBrush brush
            ? brush
            : new SolidColorBrush(fallback);
}
