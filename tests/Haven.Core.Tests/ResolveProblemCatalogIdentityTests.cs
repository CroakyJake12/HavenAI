using Haven.Core;

namespace Haven.Core.Tests;

public sealed class ResolveProblemCatalogIdentityTests
{
    [Fact]
    public void Build_AssignsAUniqueStableKeyToEveryVisibleProblem()
    {
        var result = ResolveProblemCatalog.Build(new ResolveProblemSignals(
            ModelFailed: true,
            ToolFailed: true,
            PluginUnavailable: true,
            AttachmentFailed: true,
            ContextLimitReached: true,
            ModelUnavailable: true,
            PermissionRequired: true,
            ResponseStopped: true,
            RepetitionDetected: true));

        Assert.Equal(result.Count, result.Select(item => item.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(result, item =>
            item.Key == "attachment_failed" &&
            item.Action == ResolveProblemAction.RemoveFailedAttachment);
    }
}
