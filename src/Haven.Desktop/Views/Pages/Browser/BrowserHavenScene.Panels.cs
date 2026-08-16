using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using Container = Haven.UI.Components.Container;

namespace Haven.Desktop.Views.Pages.Browser;

internal sealed partial class BrowserHavenScene
{
    private const string UtilityRowsLocation = "Browser.Utility.Rows";
    private Container _utilityPanel = null!;
    private Text _utilityTitle = null!;
    private Input _utilityInput1 = null!;
    private Input _utilityInput2 = null!;
    private HavenButton _utilityAction1 = null!;
    private HavenButton _utilityAction2 = null!;
    private DynamicUIRuntime _utilityRows = null!;
    private Text _utilityOutput = null!;

    private void BuildUtilityPanel(HavenDynamicUITemplateCatalog templates)
    {
        templates.Register("""
<DynamicUI Name="BrowserUtilityRow">
  <Container Layout="Horizontal">
    <Button Name="Primary" Type="Text">{{LABEL}}</Button>
    <Button Name="Secondary" Type="Text">{{SECONDARY}}</Button>
  </Container>
</DynamicUI>
""", "BrowserUtilityRow.dynamicUI.hui");

        _utilityPanel = new Container { Name = "Browser.Utility.Panel", Layout = HavenLayout.Vertical };
        _utilityPanel.SetValue(HavenProperties.Row, 3);
        _utilityPanel.SetValue(HavenProperties.Column, 1);
        _utilityPanel.SetValue(HavenProperties.Width, HavenLength.Px(340));
        _utilityPanel.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        _utilityPanel.SetValue(HavenProperties.Padding, HavenThickness.Parse("12px"));
        _utilityPanel.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        _utilityPanel.SetValue(HavenProperties.Background, "Transparent");
        _utilityPanel.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        _utilityPanel.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);

        _utilityTitle = new Text { Name = "Browser.Utility.Title", Level = TextLevel.H3 };
        _utilityInput1 = new Input { Name = "Browser.Utility.Input1" };
        _utilityInput2 = new Input { Name = "Browser.Utility.Input2" };
        var actions = new Container { Name = "Browser.Utility.Actions", Layout = HavenLayout.Horizontal };
        actions.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        _utilityAction1 = new HavenButton { Name = "Browser.Utility.Action1", Variant = ButtonVariant.Primary };
        _utilityAction2 = new HavenButton { Name = "Browser.Utility.Action2", Variant = ButtonVariant.Secondary };
        actions.Add(_utilityAction1);
        actions.Add(_utilityAction2);
        _utilityRows = new DynamicUIRuntime { Name = UtilityRowsLocation, Layout = HavenLayout.Vertical };
        _utilityRows.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        _utilityOutput = new Text { Name = "Browser.Utility.Output", Level = TextLevel.Paragraph };

        _utilityPanel.Add(_utilityTitle);
        _utilityPanel.Add(_utilityInput1);
        _utilityPanel.Add(_utilityInput2);
        _utilityPanel.Add(actions);
        _utilityPanel.Add(_utilityRows);
        _utilityPanel.Add(_utilityOutput);
        Root.Add(_utilityPanel);

        _utilityInput1.TextChanged += (_, _) =>
        {
            if (_syncing) return;
            if (_page.IsAssistantOpen) _page.AssistantInput = _utilityInput1.Text;
            else if (_page.IsSettingsOpen) _page.HomePage = _utilityInput1.Text;
        };
        _utilityInput2.TextChanged += (_, _) =>
        {
            if (!_syncing && _page.IsSettingsOpen) _page.SearchTemplate = _utilityInput2.Text;
        };
        _utilityAction1.Invoked += (_, _) => RunUtilityAction(primary: true);
        _utilityAction2.Invoked += (_, _) => RunUtilityAction(primary: false);
    }

    private void RunUtilityAction(bool primary)
    {
        if (_page.IsBookmarksOpen)
        {
            if (primary) _page.ToggleBookmarkCommand.Execute(null);
            return;
        }
        if (_page.IsHistoryOpen)
        {
            if (primary) _page.ClearHistoryCommand.Execute(null);
            return;
        }
        if (_page.IsAssistantOpen)
        {
            if (primary) _page.AskAssistantCommand.Execute(null);
            else _page.SummariseCommand.Execute(null);
            return;
        }
        if (_page.IsSettingsOpen)
        {
            if (primary) _page.SaveBrowserSettingsCommand.Execute(null);
            return;
        }
        if (_page.IsExtensionsOpen)
        {
            if (primary) _page.ImportExtensionRequestedCommand.Execute(null);
            else _page.ConvertChromeExtensionRequestedCommand.Execute(null);
        }
    }

    private void RefreshUtilityPanel()
    {
        var open = _page.IsAnyPanelOpen;
        _utilityPanel.SetValue(HavenProperties.Visibility, open ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        _utilityRows.ClearItems();
        if (!open) return;

        if (_page.IsBookmarksOpen)
        {
            ConfigureUtility("Bookmarks", "", "", false, false, "Bookmark current", "", true, false, "");
            foreach (var bookmark in _page.Bookmarks)
                AddUtilityRow(bookmark.Title, "Remove",
                    () => _page.OpenBookmarkCommand.Execute(bookmark),
                    () => _page.RemoveBookmarkCommand.Execute(bookmark));
            return;
        }

        if (_page.IsHistoryOpen)
        {
            ConfigureUtility("History", "", "", false, false, "Clear history", "", true, false, "");
            foreach (var entry in _page.History.Take(80))
                AddUtilityRow(entry.Title, "",
                    () => _page.OpenHistoryCommand.Execute(entry), null);
            return;
        }

        if (_page.IsAssistantOpen)
        {
            ConfigureUtility("Ask Haven", _page.AssistantInput, "", true, false,
                "Ask", "Summarise", true, true, _page.AssistantOutput);
            _utilityInput1.Placeholder = "Ask about this page";
            _utilityInput1.Multiline = true;
            return;
        }

        if (_page.IsSettingsOpen)
        {
            ConfigureUtility("Browser settings", _page.HomePage, _page.SearchTemplate, true, true,
                "Save settings", "", true, false, "");
            _utilityInput1.Placeholder = "Home page";
            _utilityInput1.Multiline = false;
            _utilityInput2.Placeholder = "Search URL template";
            AddUtilityRow($"Save history: {OnOff(_page.SaveHistory)}", "", () => _page.SaveHistory = !_page.SaveHistory, null);
            AddUtilityRow($"Offer to save logins: {OnOff(_page.OfferToSaveLogins)}", "", () => _page.OfferToSaveLogins = !_page.OfferToSaveLogins, null);
            AddUtilityRow($"Restore tabs: {OnOff(_page.RestoreTabs)}", "", () => _page.RestoreTabs = !_page.RestoreTabs, null);
            AddUtilityRow($"Enable extensions: {OnOff(_page.EnableExtensions)}", "", () => _page.EnableExtensions = !_page.EnableExtensions, null);
            AddUtilityRow($"Vertical tabs: {OnOff(_page.VerticalTabs)}", "", () => _page.VerticalTabs = !_page.VerticalTabs, null);
            return;
        }

        if (_page.IsExtensionsOpen)
        {
            ConfigureUtility("Extensions", "", "", false, false,
                "Import Haven", "Import Chrome", true, true, "");
            foreach (var extension in _page.Extensions)
                AddUtilityRow($"{extension.Name} ({OnOff(extension.IsEnabled)})", "Remove",
                    () => _page.ToggleExtensionCommand.Execute(extension),
                    () => _page.DeleteExtensionCommand.Execute(extension));
            return;
        }

        ConfigureUtility("Saved logins", "", "", false, false, "", "", false, false,
            "Saved credentials can be filled or removed here. Creating new saved logins requires Haven.UI secure-input support.");
        foreach (var login in _page.Logins)
            AddUtilityRow($"{login.Username} - {login.Origin}", "Remove",
                () => _page.AutofillLoginCommand.Execute(login),
                () => _page.DeleteLoginCommand.Execute(login));
    }

    private void ConfigureUtility(
        string title, string input1, string input2, bool showInput1, bool showInput2,
        string action1, string action2, bool showAction1, bool showAction2, string output)
    {
        _utilityTitle.Content = title;
        _utilityInput1.Text = input1;
        _utilityInput2.Text = input2;
        SetVisible(_utilityInput1, showInput1);
        SetVisible(_utilityInput2, showInput2);
        _utilityAction1.Content = action1;
        _utilityAction2.Content = action2;
        SetVisible(_utilityAction1, showAction1);
        SetVisible(_utilityAction2, showAction2);
        _utilityOutput.Content = output;
        SetVisible(_utilityOutput, !string.IsNullOrWhiteSpace(output));
    }

    private void AddUtilityRow(string label, string secondary, Action primary, Action? secondaryAction)
    {
        var item = _dynamic.CreateItem("BrowserUtilityRow", UtilityRowsLocation,
            Guid.NewGuid().ToString("N"),
            new Dictionary<string, object?> { ["LABEL"] = label, ["SECONDARY"] = secondary });
        var first = item.GetComponent<HavenButton>("Primary");
        first.Invoked += (_, _) => primary();
        var second = item.GetComponent<HavenButton>("Secondary");
        if (secondaryAction is null) SetVisible(second, false);
        else second.Invoked += (_, _) => secondaryAction();
    }

    private static string OnOff(bool value) => value ? "On" : "Off";
    private static void SetVisible(HavenElement element, bool visible) =>
        element.SetValue(HavenProperties.Visibility, visible ? HavenVisibility.Visible : HavenVisibility.Collapsed);
}
