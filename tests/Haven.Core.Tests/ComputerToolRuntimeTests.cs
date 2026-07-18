/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Core.Tests/ComputerToolRuntimeTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns ComputerToolRuntimeTests, RecordingComputerTools. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;

namespace Haven.Core.Tests;

/// <summary>
/// Represents computer tool runtime tests and keeps its related state and behavior together.
/// </summary>
public sealed class ComputerToolRuntimeTests
{
    /// <summary>
    /// Performs the direct launch request executes without a workspace step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the desktop mutations automatically inspect between actions step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the compound desktop request is not misread as one application name step owned by this component.
    /// </summary>
    [Fact]
    public void CompoundDesktopRequestIsNotMisreadAsOneApplicationName()
    {
        var pass = new ComputerToolRuntime(new RecordingComputerTools()).CreatePass();

        var call = pass.TryCreateBootstrapCall("open Microsoft Edge, search for YouTube, and click the first video result");

        Assert.Null(call);
    }

    /// <summary>
    /// Performs the activity preview is bounded while full snapshot remains available to model step owned by this component.
    /// </summary>
    [Fact]
    public async Task ActivityPreviewIsBoundedWhileFullSnapshotRemainsAvailableToModel()
    {
        var fullSnapshot = new string('x', 900);
        var pass = new ComputerToolRuntime(new RecordingComputerTools { SnapshotOutput = fullSnapshot }).CreatePass();

        var result = await pass.ExecuteAsync(Call("computer_snapshot", new { }), CancellationToken.None);

        Assert.True(result.Activity.Detail.Length <= 320);
        Assert.Equal(fullSnapshot, result.Output);
    }

    /// <summary>
    /// Performs the call step owned by this component.
    /// </summary>
    private static OllamaToolCall Call(string name, object arguments)
    {
        using var document = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(arguments));
        return new OllamaToolCall(name, document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone()));
    }

    /// <summary>
    /// Represents recording computer tools and keeps its related state and behavior together.
    /// </summary>
    private sealed class RecordingComputerTools : IComputerToolService
    {
        /// <summary>
        /// Gets or updates snapshot output, the bindable or domain state represented by this property.
        /// </summary>
        public string SnapshotOutput { get; init; } = "snapshot";
        /// <summary>
        /// Gets or updates launched name, the bindable or domain state represented by this property.
        /// </summary>
        public string? LaunchedName { get; private set; }
        /// <summary>
        /// Gets or updates window list calls, the bindable or domain state represented by this property.
        /// </summary>
        public int WindowListCalls { get; private set; }
        /// <summary>
        /// Performs snapshot async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> SnapshotAsync(CancellationToken cancellationToken) => Task.FromResult(SnapshotOutput);
        /// <summary>
        /// Performs list windows async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> ListWindowsAsync(CancellationToken cancellationToken) { WindowListCalls++; return Task.FromResult("[]"); }
        /// <summary>
        /// Performs launch app async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> LaunchAppAsync(string name, CancellationToken cancellationToken) { LaunchedName = name; return Task.FromResult($"opened {name}"); }
        /// <summary>
        /// Performs focus window async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> FocusWindowAsync(string title, CancellationToken cancellationToken) => Task.FromResult($"focused {title}");
        /// <summary>
        /// Performs invoke async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> InvokeAsync(string windowTitle, string name, string automationId, CancellationToken cancellationToken) => Task.FromResult("invoked");
        /// <summary>
        /// Performs click async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> ClickAsync(string windowTitle, int x, int y, string button, CancellationToken cancellationToken) => Task.FromResult("clicked");
        /// <summary>
        /// Performs type async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> TypeAsync(string windowTitle, string text, CancellationToken cancellationToken) => Task.FromResult("typed");
        /// <summary>
        /// Performs press async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> PressAsync(string windowTitle, string keys, CancellationToken cancellationToken) => Task.FromResult("pressed");
        /// <summary>
        /// Performs close window async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> CloseWindowAsync(string title, CancellationToken cancellationToken) => Task.FromResult("closed");
    }
}
