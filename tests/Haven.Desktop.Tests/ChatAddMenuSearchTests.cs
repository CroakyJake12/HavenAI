using Haven.Desktop.Prefabs;

namespace Haven.Desktop.Tests;

public sealed class ChatAddMenuSearchTests
{
    [Fact]
    public void Composer_attach_search_ranks_exact_prefix_contains_description_then_typo()
    {
        var exact = ChatAddMenuSurface.SearchScore("Research", "", "research");
        var prefix = ChatAddMenuSurface.SearchScore("Research Agent", "", "research");
        var contains = ChatAddMenuSurface.SearchScore("Deep Research Agent", "", "research");
        var description = ChatAddMenuSurface.SearchScore("Analyst", "Research specialist", "research");
        var typo = ChatAddMenuSurface.SearchScore("Research", "", "reserch");

        Assert.True(exact > prefix);
        Assert.True(prefix > contains);
        Assert.True(contains > description);
        Assert.True(description > typo);
        Assert.True(typo >= 0);
    }

    [Fact]
    public void Composer_attach_search_rejects_unrelated_terms()
    {
        Assert.Equal(-1, ChatAddMenuSurface.SearchScore("Calculator", "Arithmetic helper", "research"));
    }
}
