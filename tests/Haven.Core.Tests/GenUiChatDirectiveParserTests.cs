using Haven.Application;

namespace Haven.Core.Tests;

public sealed class GenUiChatDirectiveParserTests
{
    [Fact]
    public void Parses_one_bounded_registered_template_request_and_removes_it_from_display_text()
    {
        var result = GenUiChatDirectiveParser.Parse("Try this.\n```haven-ui\n{\"version\":1,\"template\":\"calculator\",\"inputs\":{\"expression\":\"6 * 7\"}}\n```");

        Assert.True(result.HasDirective);
        Assert.Null(result.Error);
        Assert.Equal("Try this.", result.DisplayContent);
        Assert.Equal("calculator", result.Request?.TemplateKey);
        Assert.Equal("6 * 7", result.Request?.Expression);
    }

    [Fact]
    public void Parses_live_structured_form_without_allowing_arbitrary_controls()
    {
        var result = GenUiChatDirectiveParser.Parse("""
            ```haven-ui
            {"version":1,"template":"structured-form","inputs":{"title":"Expense entry","schema":[{"id":"amount","label":"Amount","type":"text"},{"id":"category","label":"Category","type":"select","options":["Travel","Food"]}]}}
            ```
            """);

        Assert.True(result.HasDirective);
        Assert.Null(result.Error);
        Assert.Equal("structured-form", result.Request?.TemplateKey);
        Assert.Equal(2, result.Request?.Inputs["schema"].GetArrayLength());
    }

    [Fact]
    public void Haven_question_is_removed_from_display_text_and_routed_to_choice_prompt()
    {
        var result = GenUiChatDirectiveParser.Parse("""
            Choose one:
            <haven-question>{"question":"Continue?","options":["Yes","No"]}</haven-question>
            """);

        Assert.True(result.HasDirective);
        Assert.Null(result.Error);
        Assert.Equal("Choose one:", result.DisplayContent);
        Assert.DoesNotContain("haven-question", result.DisplayContent, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("choice-prompt", result.Request?.TemplateKey);
        Assert.Equal("Continue?", result.Request?.Inputs["question"].GetString());
    }

    [Theory]
    [InlineData("{\"version\":1,\"template\":\"whiteboard\",\"inputs\":{}}", "not available")]
    [InlineData("{\"version\":1,\"template\":\"calculator\",\"inputs\":{},\"xaml\":\"<Button/>\"}", "unsupported field")]
    [InlineData("{\"version\":1,\"template\":\"calculator\",\"inputs\":{\"command\":\"rm\"}}", "unsupported input")]
    [InlineData("{\"version\":1,\"template\":\"structured-form\",\"inputs\":{\"schema\":[{\"id\":\"x\",\"label\":\"X\",\"type\":\"xaml\"}]}}", "type must be")]
    public void Rejects_unreleased_templates_and_arbitrary_or_executable_fields(string payload, string expected)
    {
        var result = GenUiChatDirectiveParser.Parse($"```haven-ui\n{payload}\n```");

        Assert.Null(result.Request);
        Assert.Contains(expected, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ordinary_markdown_is_unchanged()
    {
        const string content = "A normal response with `code`.";
        var result = GenUiChatDirectiveParser.Parse(content);

        Assert.False(result.HasDirective);
        Assert.Equal(content, result.DisplayContent);
    }

    [Theory]
    [InlineData("can you generate ui?")]
    [InlineData("can you generate ui yet?")]
    [InlineData("How is Generative UI progress?")]
    [InlineData("Is interactive UI available yet?")]
    public void Availability_questions_describe_capability_without_emitting_demo_ui(string prompt)
    {
        var handled = GenUiChatDirectiveParser.TryCreateAvailabilityResponse(prompt, out var response);

        Assert.True(handled);
        Assert.Contains("interactive UI", response, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("card", response, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(GenUiChatDirectiveParser.Parse(response).Requests);
    }

    [Fact]
    public void Model_instruction_describes_live_templates_and_spatial_custom_primitives()
    {
        var instruction = GenUiChatDirectiveParser.ModelInstruction;

        Assert.Contains("calculator", instruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("structured-form", instruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("checklist", instruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("haven-ui", instruction, StringComparison.Ordinal);
        Assert.Contains("HavenCanvas", instruction, StringComparison.Ordinal);
        Assert.Contains("responsive", instruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fullscreen", instruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("patches", instruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("declarative", instruction, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("you do not need to define the logic", instruction, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("flashcards")]
    [InlineData("questions")]
    public void CardDeckNormalizesCommonArrayAliases(string alias)
    {
        var content = $"```haven-ui\n{{\"version\":1,\"template\":\"card-deck\",\"inputs\":{{\"{alias}\":[{{\"question\":\"Capital of France?\",\"answer\":\"Paris\"}}]}}}}\n```";

        var result = GenUiChatDirectiveParser.Parse(content);

        Assert.Null(result.Error);
        Assert.Equal("card-deck", result.Request?.TemplateKey);
        Assert.True(result.Request!.Inputs.ContainsKey("cards"));
        Assert.Single(result.Request.Inputs["cards"].EnumerateArray());
    }

    [Fact]
    public void CardDeckAcceptsItemsAliasAndObjectTextCards()
    {
        const string content = """
            ```haven-ui
            {"version":1,"template":"card-deck","inputs":{"items":[{"id":1,"front":{"text":"2 + 3"},"back":{"text":"5"}}]}}
            ```
            """;

        var result = GenUiChatDirectiveParser.Parse(content);

        Assert.Null(result.Error);
        Assert.Equal("card-deck", result.Request?.TemplateKey);
        Assert.True(result.Request!.Inputs.ContainsKey("cards"));
    }

    [Fact]
    public void CustomCraftingSurfaceAcceptsNestedCardButtonActionTree()
    {
        const string content = """
            ```haven-ui
            {"version":1,"template":"custom","title":"Crafting Table","accent":"green","components":[{"id":"crafting","type":"HavenGrid","props":{"columns":3,"responsive":true},"children":[{"id":"slot-1","type":"HavenCard","props":{},"children":[{"id":"slot-1-label","type":"HavenText","props":{"text":"Oak Plank"}},{"id":"slot-1-place","type":"HavenButton","props":{"label":"Place"},"actions":[{"id":"slot.place.1"}]}]}]},{"id":"status","type":"HavenStatus","props":{"text":"Place items to craft"}}]}
            ```
            """;

        var result = GenUiChatDirectiveParser.Parse(content);

        Assert.True(result.HasDirective);
        Assert.Null(result.Error);
        Assert.Equal("custom", result.Request?.TemplateKey);
        Assert.Equal(2, result.Request?.Inputs["components"].GetArrayLength());
    }

    [Fact]
    public void Specific_generated_ui_requests_are_left_for_the_model_and_registry_router()
    {
        var handled = GenUiChatDirectiveParser.TryCreateAvailabilityResponse(
            "Can you generate UI for tracking my expenses?",
            out var response);

        Assert.False(handled);
        Assert.Empty(response);
    }
}
