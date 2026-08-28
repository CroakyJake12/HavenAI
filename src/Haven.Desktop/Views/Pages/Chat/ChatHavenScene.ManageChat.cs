using Haven.Core;

namespace Haven.Desktop.Views.Pages.Chat;

internal sealed partial class ChatHavenScene
{
    public event EventHandler? ManageChatRequested;

    public void ShowManageChatMenu(
        ChatActionMode actionMode,
        GenerativeUiResponseMode visualMode,
        Action<ChatActionMode> setActionMode,
        Action<GenerativeUiResponseMode> setVisualMode)
    {
        ArgumentNullException.ThrowIfNull(setActionMode);
        ArgumentNullException.ThrowIfNull(setVisualMode);
        var choices = new List<(string Label, Action Action)>
        {
            (Prefix(actionMode == ChatActionMode.AllowAllActions, "Actions · Allow All"), () => setActionMode(ChatActionMode.AllowAllActions)),
            (Prefix(actionMode == ChatActionMode.AllowBasicActions, "Actions · Allow Basic"), () => setActionMode(ChatActionMode.AllowBasicActions)),
            (Prefix(actionMode == ChatActionMode.JustChat, "Actions · Just Chat"), () => setActionMode(ChatActionMode.JustChat)),
            (Prefix(visualMode == GenerativeUiResponseMode.AlwaysVisual, "Responses · Always Visual"), () => setVisualMode(GenerativeUiResponseMode.AlwaysVisual)),
            (Prefix(visualMode == GenerativeUiResponseMode.PreferVisual, "Responses · Prefer Visual"), () => setVisualMode(GenerativeUiResponseMode.PreferVisual)),
            (Prefix(visualMode == GenerativeUiResponseMode.Auto, "Responses · Auto"), () => setVisualMode(GenerativeUiResponseMode.Auto)),
            (Prefix(visualMode == GenerativeUiResponseMode.PreferText, "Responses · Prefer Text"), () => setVisualMode(GenerativeUiResponseMode.PreferText)),
            (Prefix(visualMode == GenerativeUiResponseMode.AlwaysText, "Responses · Always Text"), () => setVisualMode(GenerativeUiResponseMode.AlwaysText))
        };
        ShowAnchoredChoiceMenu(ManageChat, "Manage Chat", choices, 288d);
    }

    private void OnManageChatInvoked(object? sender, EventArgs e) =>
        ManageChatRequested?.Invoke(this, EventArgs.Empty);

    private static string Prefix(bool selected, string label) => selected ? "✓ " + label : label;
}
