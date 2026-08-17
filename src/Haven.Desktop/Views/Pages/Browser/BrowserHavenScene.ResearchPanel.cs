namespace Haven.Desktop.Views.Pages.Browser;

internal sealed partial class BrowserHavenScene
{
    private bool RunResearchUtilityAction(bool primary)
    {
        if (!_page.IsResearchOpen) return false;
        if (primary) _page.CaptureResearchSourceCommand.Execute(null);
        else _page.RunResearchCommand.Execute(null);
        return true;
    }

    private bool RefreshResearchUtilityPanel()
    {
        if (!_page.IsResearchOpen) return false;

        ConfigureUtility(
            "Research",
            _page.ResearchInput,
            "",
            true,
            false,
            "Add current page",
            "Run research",
            true,
            true,
            _page.ResearchOutput);
        _utilityInput1.Placeholder = "What do you want to find out across these sources?";
        _utilityInput1.Multiline = true;

        for (var index = 0; index < _page.ResearchSources.Count; index++)
        {
            var source = _page.ResearchSources[index];
            var privacy = source.IsPrivate ? "Private - " : string.Empty;
            AddUtilityRow(
                $"[S{index + 1}] {privacy}{source.Title}",
                "Remove",
                () => _page.OpenResearchSourceCommand.Execute(source),
                () => _page.RemoveResearchSourceCommand.Execute(source));
        }

        if (_page.ResearchSources.Count > 0)
            AddUtilityRow("Clear research session", "", () => _page.ClearResearchCommand.Execute(null), null);

        return true;
    }
}
