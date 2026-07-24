using Haven.Core;

namespace Haven.Core.Tests;

public sealed class ResolveProblemCatalogTests
{
    [Fact]
    public void CommonProblems_AreAlwaysVisible()
    {
        var result = ResolveProblemCatalog.Build(new(
            false, false, false, false, false, false, false, false, false));

        Assert.Contains(result, item => item.Key == "looping" && item.IsAlwaysVisible);
        Assert.Contains(result, item => item.Key == "hallucinating" && item.IsAlwaysVisible);
        Assert.Contains(result, item => item.Key == "other" && item.IsAlwaysVisible);
    }

    [Fact]
    public void AdaptiveProblems_AppearOnlyWhenSignalled()
    {
        var result = ResolveProblemCatalog.Build(new(
            false, true, false, false, false, false, true, false, false));

        Assert.Contains(result, item => item.Key == "tool_failed" && !item.IsAlwaysVisible);
        Assert.Contains(result, item => item.Key == "permission_required" && !item.IsAlwaysVisible);
        Assert.DoesNotContain(result, item => item.Key == "model_failed");
    }
}
