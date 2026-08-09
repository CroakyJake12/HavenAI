using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Haven.Core;

namespace Haven.Desktop.Controls;

/// <summary>
/// ChatGPT-style message view. Clean text-based rendering with avatar and name.
/// No bubble chrome — content flows naturally in the chat stream.
/// </summary>
public sealed partial class MessageView : UserControl
{
    public static readonly StyledProperty<MessageRole> RoleProperty =
        AvaloniaProperty.Register<MessageView, MessageRole>(nameof(Role));

    public static readonly StyledProperty<string> MessageContentProperty =
        AvaloniaProperty.Register<MessageView, string>(nameof(MessageContent), string.Empty);

    public static readonly StyledProperty<string?> AgentNameProperty =
        AvaloniaProperty.Register<MessageView, string?>(nameof(AgentName));

    public static readonly StyledProperty<bool> IsStreamingProperty =
        AvaloniaProperty.Register<MessageView, bool>(nameof(IsStreaming));

    public MessageView()
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

        RoleLabel.Text = isUser ? "You" : (AgentName ?? "Haven");
        AvatarLetter.Text = isUser ? "Y" : "H";

        Avatar.Background = isUser
            ? ResourceBrush("HavenAccentTertiaryBrush", Color.Parse("#FF202750"))
            : ResourceBrush("HavenAccentPrimaryBrush", Color.Parse("#FF3527FF"));

        Body.Text = string.IsNullOrEmpty(MessageContent) && IsStreaming ? "" : MessageContent;

        StreamingIndicator.IsVisible = IsStreaming && string.IsNullOrEmpty(MessageContent);
        StreamingText.Text = "Thinking\u2026";
    }

    public void ShowGenUIProgress(string templateName)
    {
        GenUIProgress.IsVisible = true;
        GenUIProgressText.Text = $"Preparing {templateName}\u2026";
    }

    public void HideGenUIProgress()
    {
        GenUIProgress.IsVisible = false;
    }

    private static IBrush ResourceBrush(string key, Color fallback) =>
        Avalonia.Application.Current?.TryFindResource(key, out var value) == true && value is IBrush brush
            ? brush
            : new SolidColorBrush(fallback);
}
