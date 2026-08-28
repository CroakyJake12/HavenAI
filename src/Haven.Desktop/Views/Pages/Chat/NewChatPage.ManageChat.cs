using Haven.Core;
using Haven.Desktop.Views.Shell.TopRail;

namespace Haven.Desktop.Views.Pages.Chat;

public sealed partial class NewChatPage
{
    private void OnManageChatRequested(object? sender, EventArgs e)
    {
        _scene.ShowManageChatMenu(
            EffectiveChatActionMode,
            EffectiveChatVisualMode,
            actionMode => ApplyAddSelection(new AddMenuSelection(AddMenu.AddMenuAction.AllowActions, actionMode)),
            visualMode => ApplyAddSelection(new AddMenuSelection(AddMenu.AddMenuAction.VisualResponses, visualMode)));
    }
}
