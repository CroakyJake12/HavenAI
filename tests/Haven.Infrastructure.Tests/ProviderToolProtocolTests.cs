using System.Text.Json;
using Haven.Application;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class ProviderToolProtocolTests
{
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
