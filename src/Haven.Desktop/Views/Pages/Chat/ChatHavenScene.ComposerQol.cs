using Haven.Core;
using Haven.Desktop.Views.Shell.TopRail;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Chat;

internal sealed record ChatAttachmentChip(string Key, string Label, string IconKey, bool OpensMultipleResponses = false);

internal sealed partial class ChatHavenScene
{
    private HavenButton? _chatSettingsButton;
    private Container? _attachmentChips;
    private ChatActionMode _chatSettingsActionMode = ChatActionMode.AllowBasicActions;
    private GenerativeUiResponseMode _chatSettingsVisualMode = GenerativeUiResponseMode.Auto;

    public event EventHandler<string>? AttachmentRemoveRequested;
    public event EventHandler? MultipleResponsesRequested;
    public event EventHandler<string>? MultipleResponseModelToggled;

    public void ConfigureComposerQol()
    {
        if (_chatSettingsButton is not null) return;
        _chatSettingsButton = Chatbox.GetComponent<HavenButton>("ChatSettings");
        _attachmentChips = Chatbox.GetComponent<Container>("AttachmentChips");
        var composerRow = Chatbox.GetComponent<Container>("ComposerRow");
        var settingsIcon = Chatbox.GetComponent<Icon>("ChatSettingsIcon");
        var send = Chatbox.GetComponent<HavenButton>("Send");
        var sendIcon = Chatbox.GetComponent<Icon>("SendIcon");
        composerRow.Columns = "44px 1fr 44px 44px";
        _chatSettingsButton.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
        settingsIcon.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
        send.SetValue(HavenProperties.Column, 3);
        sendIcon.SetValue(HavenProperties.Column, 3);
        _chatSettingsButton.Invoked += (_, _) => ShowChatSettingsMenu();
        _addMenu.SetEmbeddedSearchVisible(false);
        _addMenu.SetThreadSettingsVisible(false);
    }

    public void ShowAttachSearch(string query) => _addMenu.ShowUnifiedSearch(query);

    public void SetAttachmentChips(IReadOnlyList<ChatAttachmentChip> chips)
    {
        ConfigureComposerQol();
        foreach (var child in _attachmentChips!.Children.ToArray()) _attachmentChips.Remove(child);
        foreach (var chip in chips) _attachmentChips.Add(CreateAttachmentChip(chip));
        _attachmentChips.SetValue(HavenProperties.Visibility, chips.Count == 0 ? HavenVisibility.Collapsed : HavenVisibility.Visible);
    }

    private Container CreateAttachmentChip(ChatAttachmentChip chip)
    {
        var row = new Container { Layout = HavenLayout.Horizontal, Name = "ChatAttachmentChip" };
        row.SetValue(HavenProperties.Background, "AccentMuted");
        row.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(14)));
        row.SetValue(HavenProperties.Padding, HavenThickness.Parse("5px 8px"));
        row.SetValue(HavenProperties.Gap, HavenLength.Px(5));
        var icon = new Icon { Key = chip.IconKey };
        icon.SetValue(HavenProperties.Width, HavenLength.Px(14));
        icon.SetValue(HavenProperties.Height, HavenLength.Px(14));
        row.Add(icon);
        var label = new HavenButton { Content = chip.Label, Variant = ButtonVariant.Text };
        label.SetValue(HavenProperties.MinHeight, HavenLength.Px(24));
        label.SetValue(HavenProperties.FontSize, 11d);
        label.SetValue(HavenProperties.FontWeight, 600);
        label.Accessibility.AccessibleName = chip.Label;
        if (chip.OpensMultipleResponses) label.Invoked += (_, _) => MultipleResponsesRequested?.Invoke(this, EventArgs.Empty);
        row.Add(label);
        var remove = new HavenButton { Content = "×", Variant = ButtonVariant.Text };
        remove.SetValue(HavenProperties.Width, HavenLength.Px(24));
        remove.SetValue(HavenProperties.Height, HavenLength.Px(24));
        remove.SetValue(HavenProperties.MinHeight, HavenLength.Px(24));
        remove.Accessibility.AccessibleName = "Remove " + chip.Label;
        remove.Invoked += (_, _) => AttachmentRemoveRequested?.Invoke(this, chip.Key);
        row.Add(remove);
        return row;
    }

    public void SetChatSettingsState(ChatActionMode actionMode, GenerativeUiResponseMode visualMode)
    {
        _chatSettingsActionMode = actionMode;
        _chatSettingsVisualMode = visualMode;
    }

    private void ShowChatSettingsMenu()
    {
        if (_chatSettingsButton is null) return;
        IReadOnlyList<PopupMenuItem> items =
        [
            
            new PopupMenuItem("Actions: All", () => CatalogItemSelected?.Invoke(this, new AddMenuSelection(AddMenu.AddMenuAction.AllowActions, ChatActionMode.AllowAllActions)), Enabled: _chatSettingsActionMode != ChatActionMode.AllowAllActions),
            new PopupMenuItem("Actions: Basic", () => CatalogItemSelected?.Invoke(this, new AddMenuSelection(AddMenu.AddMenuAction.AllowActions, ChatActionMode.AllowBasicActions)), Enabled: _chatSettingsActionMode != ChatActionMode.AllowBasicActions),
            new PopupMenuItem("Actions: Just Chat", () => CatalogItemSelected?.Invoke(this, new AddMenuSelection(AddMenu.AddMenuAction.AllowActions, ChatActionMode.JustChat)), Enabled: _chatSettingsActionMode != ChatActionMode.JustChat),
            new PopupMenuItem("Responses: Auto", () => CatalogItemSelected?.Invoke(this, new AddMenuSelection(AddMenu.AddMenuAction.VisualResponses, GenerativeUiResponseMode.Auto)), Enabled: _chatSettingsVisualMode != GenerativeUiResponseMode.Auto),
            new PopupMenuItem("Responses: Prefer Visual", () => CatalogItemSelected?.Invoke(this, new AddMenuSelection(AddMenu.AddMenuAction.VisualResponses, GenerativeUiResponseMode.PreferVisual)), Enabled: _chatSettingsVisualMode != GenerativeUiResponseMode.PreferVisual),
            new PopupMenuItem("Responses: Prefer Text", () => CatalogItemSelected?.Invoke(this, new AddMenuSelection(AddMenu.AddMenuAction.VisualResponses, GenerativeUiResponseMode.PreferText)), Enabled: _chatSettingsVisualMode != GenerativeUiResponseMode.PreferText)
        ];
        ShowQolPopup(_chatSettingsButton, items, 300d, "Manage Chat");
    }

    public void ShowMultipleResponseChoices(IReadOnlyList<string> modelNames, IReadOnlyCollection<string> selected)
    {
        ConfigureComposerQol();
        var anchor = _attachmentChips!.Children.OfType<Container>().LastOrDefault() as HavenElement ?? AddButton;
        var selectedSet = selected.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var items = modelNames.Select(name => new PopupMenuItem(
            (selectedSet.Contains(name) ? "✓ " : string.Empty) + name,
            () => MultipleResponseModelToggled?.Invoke(this, name))).ToArray();
        ShowQolPopup(anchor, items, 320d, "Multiple Responses");
    }

    private void ShowQolPopup(HavenElement anchor, IReadOnlyList<PopupMenuItem> items, double width, string name)
    {
        _activeMessagePopup?.Dismiss();
        foreach (var existing in Root.Children.OfType<PopupMenu>().ToArray()) existing.Dismiss();
        var popup = new PopupMenu(anchor, Root, items, width, name);
        popup.Dismissed += (_, _) => { if (ReferenceEquals(_activeMessagePopup, popup)) _activeMessagePopup = null; };
        _activeMessagePopup = popup;
        Root.Add(popup);
    }
}
