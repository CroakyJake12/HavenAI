using System.Text.Json;
using Haven.Application;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class GeminiToolProtocolTests
{
    [Fact]
    public void ParallelFunctionResponsesRepeatTheirMatchingFunctionCallIds()
    {
        var turns = new List<OllamaToolTurn>
        {
            new("user", "Read both files."),
            new("assistant", string.Empty,
            [
                new OllamaToolCall("read_file", Arguments("path", "one.txt")),
                new OllamaToolCall("read_file", Arguments("path", "two.txt"))
            ]),
            new("tool", "first", ToolName: "read_file"),
            new("tool", "second", ToolName: "read_file")
        };

        var payload = GeminiModelProvider.BuildToolContents(turns);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload, ProviderHttp.Json));
        var contents = document.RootElement;
        var callParts = contents[1].GetProperty("parts");
        var responseParts = contents[2].GetProperty("parts");
        var firstCall = callParts[0].GetProperty("functionCall");
        var secondCall = callParts[1].GetProperty("functionCall");
        var firstResponse = responseParts[0].GetProperty("functionResponse");
        var secondResponse = responseParts[1].GetProperty("functionResponse");

        Assert.Equal("model", contents[1].GetProperty("role").GetString());
        Assert.Equal("user", contents[2].GetProperty("role").GetString());
        Assert.Equal(firstCall.GetProperty("id").GetString(), firstResponse.GetProperty("id").GetString());
        Assert.Equal(secondCall.GetProperty("id").GetString(), secondResponse.GetProperty("id").GetString());
        Assert.Equal("read_file", firstResponse.GetProperty("name").GetString());
        Assert.Equal("read_file", secondResponse.GetProperty("name").GetString());
    }

    [Fact]
    public void ToolEnabledGeminiHistoryPreservesInlineImages()
    {
        var turns = new List<OllamaToolTurn>
        {
            new("user", "Inspect this image.", Images: ["base64-image"])
        };

        var payload = GeminiModelProvider.BuildToolContents(turns);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload, ProviderHttp.Json));
        var parts = document.RootElement[0].GetProperty("parts");

        Assert.Equal("Inspect this image.", parts[0].GetProperty("text").GetString());
        Assert.Equal("image/jpeg", parts[1].GetProperty("inlineData").GetProperty("mimeType").GetString());
        Assert.Equal("base64-image", parts[1].GetProperty("inlineData").GetProperty("data").GetString());
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
