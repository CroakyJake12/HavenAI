using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class DefaultProviderResolverTests
{
    [Fact]
    public void ExplicitProviderWinsOverEverything()
    {
        var outcome = DefaultProviderResolver.Resolve(ProviderResolutionInput.For(
            ProviderCategory.Email,
            explicitProvider: "mail-app",
            attached: ["gmail"],
            userDefaultAssignment: "gmail"));

        Assert.Equal("mail-app", outcome.ResolvedAppKey);
        Assert.False(outcome.RequiresUserChoice);
    }

    [Fact]
    public void SingleUnambiguousAttachedAppResolvesWithoutAsking()
    {
        var outcome = DefaultProviderResolver.Resolve(ProviderResolutionInput.For(
            ProviderCategory.Calendar,
            attached: ["google-calendar"]));

        Assert.Equal("google-calendar", outcome.ResolvedAppKey);
    }

    [Fact]
    public void TwoAttachedProvidersRequireConsequenceSpecificChoice()
    {
        var outcome = DefaultProviderResolver.Resolve(ProviderResolutionInput.For(
            ProviderCategory.Calendar,
            attached: ["google-calendar", "microsoft-calendar"]));

        Assert.True(outcome.RequiresUserChoice);
        Assert.Contains("google-calendar", outcome.Options);
        Assert.Contains("microsoft-calendar", outcome.Options);
    }

    [Fact]
    public void AlwaysAskDefaultNeverGuesses()
    {
        var outcome = DefaultProviderResolver.Resolve(ProviderResolutionInput.For(
            ProviderCategory.Email,
            userDefaultAssignment: DefaultProviderAssignments.AlwaysAsk));

        Assert.True(outcome.RequiresUserChoice);
    }

    [Fact]
    public void SoleCompatibleAvailableProviderResolvesWhenNoDefaultSet()
    {
        var outcome = DefaultProviderResolver.Resolve(ProviderResolutionInput.For(
            ProviderCategory.ImageGeneration,
            available: ["imagine"]));

        Assert.Equal("imagine", outcome.ResolvedAppKey);
    }

    [Fact]
    public void UserDefaultBeatsSoleAvailableOnlyWhenItMatchesCategory()
    {
        var resolved = DefaultProviderResolver.Resolve(ProviderResolutionInput.For(
            ProviderCategory.ImageGeneration,
            available: ["imagine"],
            userDefaultAssignment: "imagine"));
        var mismatch = DefaultProviderResolver.Resolve(ProviderResolutionInput.For(
            ProviderCategory.Maps,
            available: ["browse"],
            userDefaultAssignment: "imagine"));

        Assert.Equal("imagine", resolved.ResolvedAppKey);
        // 'imagine' does not provide Maps; falls through and asks rather than guessing.
        Assert.True(mismatch.RequiresUserChoice);
    }

    [Fact]
    public void DirectiveTextUsesHumanReadableLabels()
    {
        var text = DefaultProviderDirectives.Describe(new Dictionary<string, string>
        {
            [ProviderCategory.Email.ToString()] = DefaultProviderAssignments.AlwaysAsk,
            [ProviderCategory.ImageGeneration.ToString()] = "imagine"
        });

        Assert.Contains("Email → Always Ask", text);
        Assert.Contains("Image Generation → imagine", text);
        Assert.Contains("consequence-specific", text);
    }
}

public sealed class ChatPlanArtifactTests
{
    [Fact]
    public void ParsesTagAndCleansContent()
    {
        var artifact = ChatPlanArtifact.TryParse("## Plan\nDo things.\n<haven-plan>Weather app</haven-plan>");

        Assert.NotNull(artifact);
        Assert.Equal("Weather app", artifact!.Title);
        Assert.DoesNotContain("<haven-plan>", artifact.CleanedContent);
        Assert.Contains("Do things.", artifact.CleanedContent);
    }

    [Fact]
    public void ReturnsNullWithoutTagOrOversizedTitle()
    {
        Assert.Null(ChatPlanArtifact.TryParse("Just a normal answer."));
        Assert.Null(ChatPlanArtifact.TryParse($"<haven-plan>{new string('x', 500)}</haven-plan>"));
    }
}

public sealed class SuggestedActionEngineTests
{
    [Fact]
    public void ConversationalTurnsProduceNoSuggestions()
    {
        Assert.Empty(SuggestedActionEngine.ForTurn("hi", "Hello!", workspaceAttached: false, studyMode: false));
        Assert.Empty(SuggestedActionEngine.ForTurn("", "Something long enough.", false, false));
    }

    [Fact]
    public void ReminderMentionSuggestsPlannerEntry()
    {
        var suggestions = SuggestedActionEngine.ForTurn(
            "I keep forgetting about mum's birthday on the 14th",
            "That sounds important — you could mark it down.",
            workspaceAttached: false,
            studyMode: false);

        Assert.Contains(suggestions, item => item.Label == "Add to Planner");
    }

    [Fact]
    public void WorkspaceCodeChangeSuggestsRunningTestsAtMostTwoSuggestions()
    {
        var content = "Here is the fix.\n```csharp\ncode\n```\nThe tests should cover it.";
        var suggestions = SuggestedActionEngine.ForTurn(
            "fix the calculator bug in the workspace",
            content,
            workspaceAttached: true,
            studyMode: true);

        Assert.True(suggestions.Count <= 2);
        Assert.Contains(suggestions, item => item.Label == "Run tests now");
    }
}
