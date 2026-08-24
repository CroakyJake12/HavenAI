using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Views.Pages.Chat;

internal static class ChatGeneratedContentRecovery
{
    internal const string RetryLabel = "Retry interactive response";

    public static Button CreateRetryButton(Action retry)
    {
        ArgumentNullException.ThrowIfNull(retry);
        var button = new Button
        {
            Name = "Chat.GenUi.Retry",
            Content = RetryLabel,
            Variant = ButtonVariant.Secondary
        };
        button.Accessibility.AccessibleName = RetryLabel;
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(34));
        button.Invoked += (_, _) => retry();
        return button;
    }
}
