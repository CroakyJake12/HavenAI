/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/ProviderToolProtocolTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns ProviderToolProtocolTests. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;
using Haven.Application;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents provider tool protocol tests and keeps its related state and behavior together.
/// </summary>
public sealed class ProviderToolProtocolTests
{
    /// <summary>
    /// Performs the open ai tool results reference their exact parallel call ids step owned by this component.
    /// </summary>
    [Fact]
    public void OpenAiToolResultsReferenceTheirExactParallelCallIds()
    {
        var arguments = Arguments("path", "one.txt");
        var secondArguments = Arguments("path", "two.txt");
        var turns = new List<OllamaToolTurn>
        {
            new("user", "Read both files."),
            new("assistant", string.Empty,
            [
                new OllamaToolCall("read_file", arguments),
                new OllamaToolCall("read_file", secondArguments)
            ]),
            new("tool", "first", ToolName: "read_file"),
            new("tool", "second", ToolName: "read_file")
        };

        var payload = OpenAiCompatibleModelProviderBase.BuildToolMessages(turns, "system");
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload, ProviderHttp.Json));
        var messages = document.RootElement;
        var calls = messages[2].GetProperty("tool_calls");
        var firstId = calls[0].GetProperty("id").GetString();
        var secondId = calls[1].GetProperty("id").GetString();

        Assert.False(string.IsNullOrWhiteSpace(firstId));
        Assert.False(string.IsNullOrWhiteSpace(secondId));
        Assert.NotEqual(firstId, secondId);
        Assert.Equal(firstId, messages[3].GetProperty("tool_call_id").GetString());
        Assert.Equal(secondId, messages[4].GetProperty("tool_call_id").GetString());
    }

    /// <summary>
    /// Performs the open ai tool history preserves vision input step owned by this component.
    /// </summary>
    [Fact]
    public void OpenAiToolHistoryPreservesVisionInput()
    {
        var turns = new List<OllamaToolTurn>
        {
            new("user", "Inspect this image.", Images: ["base64-image"])
        };

        var payload = OpenAiCompatibleModelProviderBase.BuildToolMessages(turns, null);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload, ProviderHttp.Json));
        var content = document.RootElement[0].GetProperty("content");

        Assert.Equal("text", content[0].GetProperty("type").GetString());
        Assert.Equal("image_url", content[1].GetProperty("type").GetString());
        Assert.Equal(
            "data:image/jpeg;base64,base64-image",
            content[1].GetProperty("image_url").GetProperty("url").GetString());
    }

    /// <summary>
    /// Performs the anthropic parallel results immediately follow tool use in one user message step owned by this component.
    /// </summary>
    [Fact]
    public void AnthropicParallelResultsImmediatelyFollowToolUseInOneUserMessage()
    {
        var turns = new List<OllamaToolTurn>
        {
            new("user", "Read both files."),
            new("assistant", "I will inspect both.",
            [
                new OllamaToolCall("read_file", Arguments("path", "one.txt")),
                new OllamaToolCall("read_file", Arguments("path", "two.txt"))
            ]),
            new("tool", "first", ToolName: "read_file"),
            new("tool", "second", ToolName: "read_file")
        };

        var payload = AnthropicModelProvider.BuildToolMessages(turns);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload, ProviderHttp.Json));
        var messages = document.RootElement;
        var assistantContent = messages[1].GetProperty("content");
        var resultContent = messages[2].GetProperty("content");
        var firstCallId = assistantContent[1].GetProperty("id").GetString();
        var secondCallId = assistantContent[2].GetProperty("id").GetString();

        Assert.Equal("assistant", messages[1].GetProperty("role").GetString());
        Assert.Equal("text", assistantContent[0].GetProperty("type").GetString());
        Assert.Equal("tool_use", assistantContent[1].GetProperty("type").GetString());
        Assert.Equal("tool_use", assistantContent[2].GetProperty("type").GetString());
        Assert.Equal("user", messages[2].GetProperty("role").GetString());
        Assert.Equal(2, resultContent.GetArrayLength());
        Assert.Equal("tool_result", resultContent[0].GetProperty("type").GetString());
        Assert.Equal(firstCallId, resultContent[0].GetProperty("tool_use_id").GetString());
        Assert.Equal(secondCallId, resultContent[1].GetProperty("tool_use_id").GetString());
    }

    /// <summary>
    /// Performs the anthropic tool history preserves vision input step owned by this component.
    /// </summary>
    [Fact]
    public void AnthropicToolHistoryPreservesVisionInput()
    {
        var turns = new List<OllamaToolTurn>
        {
            new("user", "Inspect this image.", Images: ["base64-image"])
        };

        var payload = AnthropicModelProvider.BuildToolMessages(turns);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload, ProviderHttp.Json));
        var content = document.RootElement[0].GetProperty("content");

        Assert.Equal("image", content[0].GetProperty("type").GetString());
        Assert.Equal("base64", content[0].GetProperty("source").GetProperty("type").GetString());
        Assert.Equal("text", content[1].GetProperty("type").GetString());
    }

    /// <summary>
    /// Performs the orphan tool result is rejected before provider request step owned by this component.
    /// </summary>
    [Fact]
    public void OrphanToolResultIsRejectedBeforeProviderRequest()
    {
        var turns = new List<OllamaToolTurn>
        {
            new("tool", "unexpected", ToolName: "read_file")
        };

        var error = Assert.Throws<InvalidDataException>(() =>
            ProviderToolTurnCorrelation.Correlate(turns, "call_haven"));

        Assert.Contains("no preceding unmatched tool call", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Performs the arguments step owned by this component.
    /// </summary>
    private static IReadOnlyDictionary<string, JsonElement> Arguments(string name, string value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, string>
        {
            [name] = value
        }));
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.OrdinalIgnoreCase);
    }
}
