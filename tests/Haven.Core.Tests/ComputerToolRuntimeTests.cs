using Haven.Application;

namespace Haven.Core.Tests;

public sealed class ComputerToolRuntimeTests
{
    [Fact]
    public async Task DirectLaunchRequestExecutesWithoutAWorkspace()
    {
        var service = new RecordingComputerTools();
        var pass = new ComputerToolRuntime(service).CreatePass();
        var call = pass.TryCreateBootstrapCall("open notepad");

        Assert.NotNull(call);
        Assert.Equal("computer_launch_app", call.Name);
        var result = await pass.ExecuteAsync(call, CancellationToken.None);

        Assert.True(result.Activity.Succeeded);
        Assert.Equal("notepad", service.LaunchedName, ignoreCase: true);
    }

    [Fact]
    public async Task DesktopMutationsAutomaticallyInspectBetweenActions()
    {
        var tools = new RecordingComputerTools();
        var pass = new ComputerToolRuntime(tools).CreatePass();
        var first = pass.TryCreateBootstrapCall("open notepad")!;
        var second = pass.TryCreateBootstrapCall("open calculator")!;

        Assert.True((await pass.ExecuteAsync(first, CancellationToken.None)).Activity.Succeeded);
        Assert.True((await pass.ExecuteAsync(second, CancellationToken.None)).Activity.Succeeded);
        Assert.Equal(2, tools.WindowListCalls);
    }

    [Fact]
    public void CompoundDesktopRequestIsNotMisreadAsOneApplicationName()
    {
        var pass = new ComputerToolRuntime(new RecordingComputerTools()).CreatePass();

        var call = pass.TryCreateBootstrapCall("open Microsoft Edge, search for YouTube, and click the first video result");

        Assert.Null(call);
    }

    [Fact]
    public async Task ActivityPreviewIsBoundedWhileFullSnapshotRemainsAvailableToModel()
    {
        var fullSnapshot = new string('x', 900);
        var pass = new ComputerToolRuntime(new RecordingComputerTools { SnapshotOutput = fullSnapshot }).CreatePass();

        var result = await pass.ExecuteAsync(Call("computer_snapshot", new { }), CancellationToken.None);

        Assert.True(result.Activity.Detail.Length <= 320);
        Assert.Equal(fullSnapshot, result.Output);
    }

    private static OllamaToolCall Call(string name, object arguments)
    {
        using var document = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(arguments));
        return new OllamaToolCall(name, document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone()));
    }

    private sealed class RecordingComputerTools : IComputerToolService
    {
        public string SnapshotOutput { get; init; } = "snapshot";
        public string? LaunchedName { get; private set; }
        public int WindowListCalls { get; private set; }
        public Task<string> SnapshotAsync(CancellationToken cancellationToken) => Task.FromResult(SnapshotOutput);
        public Task<string> ListWindowsAsync(CancellationToken cancellationToken) { WindowListCalls++; return Task.FromResult("[]"); }
        public Task<string> LaunchAppAsync(string name, CancellationToken cancellationToken) { LaunchedName = name; return Task.FromResult($"opened {name}"); }
        public Task<string> FocusWindowAsync(string title, CancellationToken cancellationToken) => Task.FromResult($"focused {title}");
        public Task<string> InvokeAsync(string windowTitle, string name, string automationId, CancellationToken cancellationToken) => Task.FromResult("invoked");
        public Task<string> ClickAsync(string windowTitle, int x, int y, string button, CancellationToken cancellationToken) => Task.FromResult("clicked");
        public Task<string> TypeAsync(string windowTitle, string text, CancellationToken cancellationToken) => Task.FromResult("typed");
        public Task<string> PressAsync(string windowTitle, string keys, CancellationToken cancellationToken) => Task.FromResult("pressed");
        public Task<string> CloseWindowAsync(string title, CancellationToken cancellationToken) => Task.FromResult("closed");
    }
}
