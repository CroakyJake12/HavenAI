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

    [Theory]
    [InlineData("{\"version\":1,\"template\":\"whiteboard\",\"inputs\":{}}", "not available")]
    [InlineData("{\"version\":1,\"template\":\"calculator\",\"inputs\":{},\"xaml\":\"<Button/>\"}", "unsupported field")]
    [InlineData("{\"version\":1,\"template\":\"calculator\",\"inputs\":{\"command\":\"rm\"}}", "unsupported input")]
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
    [InlineData("can you generate ui yet?")]
    [InlineData("How is Generative UI progress?")]
    [InlineData("Is interactive UI available yet?")]
    public void Availability_questions_get_a_truthful_live_production_demo(string prompt)
    {
        var handled = GenUiChatDirectiveParser.TryCreateAvailabilityResponse(prompt, out var response);

        Assert.True(handled);
        Assert.Contains("Calculator", response, StringComparison.Ordinal);
        Assert.Contains("foundations", response, StringComparison.Ordinal);
        Assert.NotNull(GenUiChatDirectiveParser.Parse(response).Request);
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
