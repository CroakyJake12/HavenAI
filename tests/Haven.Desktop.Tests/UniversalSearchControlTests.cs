using Haven.Desktop.Views.Shell.TopRail;

namespace Haven.Desktop.Tests;

public sealed class UniversalSearchControlTests
{
    [Fact]
    public void FilterItems_MatchesAcrossLauncherMetadata()
    {
        var items = new[]
        {
            Item("Projects", "Haven AI", "Updated today", "Project", keywords: "workspace source"),
            Item("Documents", "Revision plan", "1,200 words", "Document"),
            Item("Commands", "Save", "Save the current workspace", "Command", keywords: "Ctrl+S")
        };

        Assert.Single(UniversalSearchControl.FilterItems(items, "workspace source"));
        Assert.Single(UniversalSearchControl.FilterItems(items, "document"));
        Assert.Single(UniversalSearchControl.FilterItems(items, "Ctrl+S"));
        Assert.Equal(3, UniversalSearchControl.FilterItems(items, string.Empty).Count);
    }

    [Fact]
    public void MoveSelectionIndex_SkipsUnavailableResultsAndWraps()
    {
        var items = new[]
        {
            Item("Commands", "Unavailable", "Denied", "Command", enabled: false),
            Item("Tabs", "Chat", "Chat", "Tab"),
            Item("Documents", "Notes", "Saved", "Document")
        };

        Assert.Equal(1, UniversalSearchControl.MoveSelectionIndex(items, -1, 1));
        Assert.Equal(2, UniversalSearchControl.MoveSelectionIndex(items, 1, 1));
        Assert.Equal(1, UniversalSearchControl.MoveSelectionIndex(items, 2, 1));
        Assert.Equal(2, UniversalSearchControl.MoveSelectionIndex(items, 1, -1));
    }

    [Fact]
    public void MoveSelectionIndex_ReturnsMinusOneWhenEverythingIsUnavailable()
    {
        var items = new[]
        {
            Item("Commands", "One", "Denied", "Command", enabled: false),
            Item("Commands", "Two", "Denied", "Command", enabled: false)
        };

        Assert.Equal(-1, UniversalSearchControl.MoveSelectionIndex(items, -1, 1));
    }

    private static UniversalSearchItem Item(
        string group,
        string title,
        string detail,
        string kind,
        bool enabled = true,
        string? keywords = null) =>
        new(group, title, detail, "search", kind, () => { }, enabled, enabled ? null : "Unavailable", keywords);
}
