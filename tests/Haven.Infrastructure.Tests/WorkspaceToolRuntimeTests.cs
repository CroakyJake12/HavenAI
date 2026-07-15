using System.Text.Json;
using Haven.Application;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class WorkspaceToolRuntimeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "haven-runtime-tests", Guid.NewGuid().ToString("N"));

    public WorkspaceToolRuntimeTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task WriteReadAndReplaceRemainInsideWorkspace()
    {
        var runtime = new WorkspaceToolRuntime(new WorkspaceToolService());
        var write = await runtime.ExecuteAsync(_root, Call("write_file", new { path = "notes/item.txt", content = "before" }), CancellationToken.None);
        var replace = await runtime.ExecuteAsync(_root, Call("replace_in_file", new { path = "notes/item.txt", old_text = "before", new_text = "after" }), CancellationToken.None);
        var read = await runtime.ExecuteAsync(_root, Call("read_file", new { path = "notes/item.txt" }), CancellationToken.None);

        Assert.True(write.Activity.Succeeded);
        Assert.True(replace.Activity.Succeeded);
        Assert.True(read.Activity.Succeeded);
        Assert.Equal("after", read.Output);
    }

    [Fact]
    public async Task TraversalIsReportedAsFailedToolResult()
    {
        var runtime = new WorkspaceToolRuntime(new WorkspaceToolService());
        var result = await runtime.ExecuteAsync(_root, Call("read_file", new { path = "../outside.txt" }), CancellationToken.None);

        Assert.False(result.Activity.Succeeded);
        Assert.Contains("outside", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    private static OllamaToolCall Call(string name, object arguments)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
        var values = document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone());
        return new OllamaToolCall(name, values);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
