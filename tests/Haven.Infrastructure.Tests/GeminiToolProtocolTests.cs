/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/GeminiToolProtocolTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns GeminiToolProtocolTests. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;
using Haven.Application;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents gemini tool protocol tests and keeps its related state and behavior together.
/// </summary>
public sealed class GeminiToolProtocolTests
{
    /// <summary>
    /// Performs the parallel function responses repeat their matching function call ids step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the tool enabled gemini history preserves inline images step owned by this component.
    /// </summary>
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
