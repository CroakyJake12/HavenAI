using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class GenUiQualityValidatorTests
{
    [Fact]
    public void CustomEmptyContainerAndUnboundButtonAreReported()
    {
        var document = new GenUiDocument(
            Guid.NewGuid(),
            GenerativeUiContractValidator.CurrentContractVersion,
            new GenUiOrigin(Guid.NewGuid(), "chat", null, Guid.NewGuid()),
            "Custom", "chat",
            new GenUiComponent("root", "HavenStack", new Dictionary<string, JsonElement>(), [],
            [
                new GenUiComponent("empty", "HavenGrid", new Dictionary<string, JsonElement>(), [], []),
                new GenUiComponent("button", "HavenButton", new Dictionary<string, JsonElement>(), [], [])
            ]),
            new Dictionary<string, JsonElement>(), DateTimeOffset.UtcNow);

        var issues = GenUiDocumentQualityValidator.Validate(document);

        Assert.Contains(issues, issue => issue.ComponentId == "empty");
        Assert.Contains(issues, issue => issue.ComponentId == "button");
    }
}
