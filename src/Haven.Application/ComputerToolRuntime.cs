/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/ComputerToolRuntime.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns ComputerToolRuntime, ComputerToolPass. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Represents computer tool runtime and keeps its related state and behavior together.
/// </summary>
public sealed class ComputerToolRuntime(
    IComputerToolService tools,
    IComputerUseSessionController sessions)
{
    public ComputerToolRuntime(IComputerToolService tools)
        : this(tools, new ComputerUseSessionController())
    {
    }

    /// <summary>
    /// Stores direct launch pattern locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly Regex DirectLaunchPattern = new(
        @"^\s*(?:please\s+)?(?:open|launch|start|run)\s+(.+?)\s*[.!]?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Stores computer use suffix locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly Regex ComputerUseSuffix = new(
        @"\s+(?:using|with)\s+(?:the\s+)?(?:computer\s*use|@computeruse)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Stores tool definitions locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly IReadOnlyList<OllamaToolDefinition> ToolDefinitions =
    [
        Definition("computer_snapshot", "Inspect the foreground Windows application and its useful visible UI Automation controls. Mutation tools already include a post-action inspection; use this when more state is needed.", new()),
        Definition("computer_list_windows", "List visible Windows applications and their exact window titles.", new()),
        Definition("computer_launch_app", "Launch an installed Windows application by its visible Start menu name, such as Notepad or Calculator.",
            new() { ["name"] = StringProperty("Visible application name.") }, "name"),
        Definition("computer_focus_window", "Bring one visible Windows application to the foreground by matching its title.",
            new() { ["title"] = StringProperty("Visible window title or a unique part of it.") }, "title"),
        Definition("computer_invoke", "Invoke a Windows UI Automation element inside one exact target window.",
            new()
            {
                ["window_title"] = StringProperty("Target window title."),
                ["name"] = StringProperty("Visible element name; optional when automation_id is supplied."),
                ["automation_id"] = StringProperty("Automation id; optional when name is supplied.")
            }, "window_title"),
        Definition("computer_click", "Click screen coordinates only after verifying that they are inside the named target window.",
            new()
            {
                ["window_title"] = StringProperty("Target window title."),
                ["x"] = IntegerProperty("Absolute desktop X coordinate."),
                ["y"] = IntegerProperty("Absolute desktop Y coordinate."),
                ["button"] = StringProperty("left, right, or middle; defaults to left.")
            }, "window_title", "x", "y"),
        Definition("computer_type", "Type text into the focused control of one named target window.",
            new() { ["window_title"] = StringProperty("Target window title."), ["text"] = StringProperty("Text to type.") }, "window_title", "text"),
        Definition("computer_press", "Press a safe keyboard shortcut inside one named target window. Window-closing shortcuts are blocked.",
            new() { ["window_title"] = StringProperty("Target window title."), ["keys"] = StringProperty("Shortcut such as Ctrl+S, Enter, or Tab.") }, "window_title", "keys"),
        Definition("computer_close_window", "Request that one exact non-browser window close without sending a global shortcut.",
            new() { ["title"] = StringProperty("Exact or uniquely identifying window title.") }, "title")
    ];

    /// <summary>
    /// Creates pass with the invariants required by its callers.
    /// </summary>
    public ComputerToolPass CreatePass() => new(
        tools,
        sessions,
        ToolDefinitions,
        DirectLaunchPattern,
        ComputerUseSuffix);

    /// <summary>
    /// Performs the definition step owned by this component.
    /// </summary>
    private static OllamaToolDefinition Definition(string name, string description, Dictionary<string, object> properties, params string[] required) =>
        new(name, description, properties, required);

    /// <summary>
    /// Performs the string property step owned by this component.
    /// </summary>
    private static Dictionary<string, object> StringProperty(string description) => new() { ["type"] = "string", ["description"] = description };
    /// <summary>
    /// Performs the integer property step owned by this component.
    /// </summary>
    private static Dictionary<string, object> IntegerProperty(string description) => new() { ["type"] = "integer", ["description"] = description };
}

/// <summary>
/// Represents computer tool pass and keeps its related state and behavior together.
/// </summary>
public sealed class ComputerToolPass(
    IComputerToolService tools,
    IComputerUseSessionController sessions,
    IReadOnlyList<OllamaToolDefinition> definitions,
    Regex directLaunchPattern,
    Regex computerUseSuffix) : IDisposable
{
    /// <summary>
    /// Stores mutation limit locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int MutationLimit = 20;
    /// <summary>
    /// Stores state gate locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly object _stateGate = new();
    /// <summary>
    /// Stores needs verification locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _needsVerification;
    /// <summary>
    /// Stores mutation count locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int _mutationCount;
    private IDisposable? _session;

    /// <summary>
    /// Gets or updates definitions, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<OllamaToolDefinition> Definitions => definitions;

    /// <summary>
    /// Attempts to create bootstrap call and reports the result without using failure for normal control flow.
    /// </summary>
    public OllamaToolCall? TryCreateBootstrapCall(string prompt)
    {
        if (Regex.IsMatch(
                prompt,
                @"[,;]|\b(?:and|then)\s+(?:search|find|click|type|press|navigate|browse|go|select|choose|play|open|close|focus)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return null;
        var match = directLaunchPattern.Match(prompt.Trim());
        if (!match.Success) return null;
        var name = computerUseSuffix.Replace(match.Groups[1].Value, string.Empty).Trim();
        name = Regex.Replace(name, @"\s+(?:app|application)\s*$", string.Empty, RegexOptions.IgnoreCase).Trim();
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['\r', '\n', ';']) >= 0) return null;
        return Call("computer_launch_app", new Dictionary<string, object> { ["name"] = name });
    }

    /// <summary>
    /// Runs execute async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    public async Task<WorkspaceToolResult> ExecuteAsync(OllamaToolCall call, CancellationToken cancellationToken)
    {
        _session ??= sessions.BeginSession();
        int? cursorX = call.Name == "computer_click" ? Integer(call, "x", -1) : null;
        int? cursorY = call.Name == "computer_click" ? Integer(call, "y", -1) : null;
        sessions.UpdateAction(
            HumanLabel(call.Name),
            cursorX is >= 0 ? cursorX : null,
            cursorY is >= 0 ? cursorY : null);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            sessions.StopToken);
        await sessions.WaitIfPausedAsync(operation.Token).ConfigureAwait(false);

        var started = Stopwatch.GetTimestamp();
        try
        {
            BeforeAction(call.Name);
            var output = call.Name switch
            {
                "computer_snapshot" => await tools.SnapshotAsync(operation.Token).ConfigureAwait(false),
                "computer_list_windows" => await tools.ListWindowsAsync(operation.Token).ConfigureAwait(false),
                "computer_launch_app" => await tools.LaunchAppAsync(RequiredText(call, "name"), operation.Token).ConfigureAwait(false),
                "computer_focus_window" => await tools.FocusWindowAsync(RequiredText(call, "title"), operation.Token).ConfigureAwait(false),
                "computer_invoke" => await tools.InvokeAsync(RequiredText(call, "window_title"), Text(call, "name"), Text(call, "automation_id"), operation.Token).ConfigureAwait(false),
                "computer_click" => await tools.ClickAsync(RequiredText(call, "window_title"), Integer(call, "x", -1), Integer(call, "y", -1), Text(call, "button", "left"), operation.Token).ConfigureAwait(false),
                "computer_type" => await tools.TypeAsync(RequiredText(call, "window_title"), RequiredText(call, "text"), operation.Token).ConfigureAwait(false),
                "computer_press" => await tools.PressAsync(RequiredText(call, "window_title"), RequiredText(call, "keys"), operation.Token).ConfigureAwait(false),
                "computer_close_window" => await tools.CloseWindowAsync(RequiredText(call, "title"), operation.Token).ConfigureAwait(false),
                _ => throw new InvalidOperationException($"Unknown computer tool '{call.Name}'.")
            };
            AfterAction(call.Name, true);
            if (IsMutation(call.Name))
            {
                try
                {
                    var verificationTool = call.Name is "computer_launch_app" or "computer_close_window"
                        ? "computer_list_windows"
                        : "computer_snapshot";
                    var verification = verificationTool == "computer_list_windows"
                        ? await tools.ListWindowsAsync(operation.Token).ConfigureAwait(false)
                        : await tools.SnapshotAsync(operation.Token).ConfigureAwait(false);
                    AfterAction(verificationTool, true);
                    output += "\nPost-action verification:\n" + verification;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    throw new InvalidOperationException($"The desktop action completed, but post-action verification failed: {ex.Message}", ex);
                }
            }
            return new WorkspaceToolResult(
                new ToolActivity(Guid.NewGuid(), HumanLabel(call.Name), FirstLine(output), true, Stopwatch.GetElapsedTime(started), DateTimeOffset.UtcNow),
                output);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AfterAction(call.Name, false);
            var output = $"Tool error: {ex.Message}";
            return new WorkspaceToolResult(
                new ToolActivity(Guid.NewGuid(), HumanLabel(call.Name), ex.Message, false, Stopwatch.GetElapsedTime(started), DateTimeOffset.UtcNow),
                output);
        }
    }

    /// <summary>
    /// Performs the before action step owned by this component.
    /// </summary>
    private void BeforeAction(string name)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Computer Use currently requires Windows.");
        if (!IsMutation(name)) return;
        lock (_stateGate)
        {
            if (_mutationCount >= MutationLimit)
                throw new InvalidOperationException($"Computer Use stopped after {MutationLimit} desktop changes in one pass.");
            if (_needsVerification)
                throw new InvalidOperationException("Computer Use safety pause: inspect the desktop with computer_snapshot or computer_list_windows before performing another action.");
        }
    }

    /// <summary>
    /// Performs the after action step owned by this component.
    /// </summary>
    private void AfterAction(string name, bool succeeded)
    {
        if (!succeeded) return;
        lock (_stateGate)
        {
            if (IsInspection(name)) _needsVerification = false;
            if (IsMutation(name))
            {
                _mutationCount++;
                _needsVerification = true;
            }
        }
    }

    /// <summary>
    /// Reports whether inspection applies to the current state.
    /// </summary>
    private static bool IsInspection(string name) => name is "computer_snapshot" or "computer_list_windows";
    /// <summary>
    /// Reports whether mutation applies to the current state.
    /// </summary>
    private static bool IsMutation(string name) => name is "computer_launch_app" or "computer_invoke" or "computer_click" or "computer_type" or "computer_press" or "computer_close_window";

    /// <summary>
    /// Performs the human label step owned by this component.
    /// </summary>
    private static string HumanLabel(string name) => name switch
    {
        "computer_snapshot" => "Inspecting the desktop",
        "computer_list_windows" => "Listing open windows",
        "computer_launch_app" => "Opening an application",
        "computer_focus_window" => "Focusing a window",
        "computer_invoke" or "computer_click" => "Using a desktop control",
        "computer_type" => "Typing on the desktop",
        "computer_press" => "Pressing a desktop shortcut",
        "computer_close_window" => "Closing a specific window",
        _ => name.Replace('_', ' ')
    };

    /// <summary>
    /// Performs the call step owned by this component.
    /// </summary>
    private static OllamaToolCall Call(string name, IReadOnlyDictionary<string, object> arguments)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
        return new OllamaToolCall(name, document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone()));
    }

    /// <summary>
    /// Performs the required text step owned by this component.
    /// </summary>
    private static string RequiredText(OllamaToolCall call, string key)
    {
        var value = Text(call, key);
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{key} is required.") : value;
    }

    /// <summary>
    /// Performs the text step owned by this component.
    /// </summary>
    private static string Text(OllamaToolCall call, string key, string fallback = "") =>
        call.Arguments.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;

    /// <summary>
    /// Performs the integer step owned by this component.
    /// </summary>
    private static int Integer(OllamaToolCall call, string key, int fallback) =>
        call.Arguments.TryGetValue(key, out var value) && value.TryGetInt32(out var result) ? result : fallback;

    /// <summary>
    /// Performs the first line step owned by this component.
    /// </summary>
    private static string FirstLine(string value)
    {
        var line = value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "Completed";
        return line.Length <= 320 ? line : line[..317] + "…";
    }

    public void Dispose()
    {
        _session?.Dispose();
        _session = null;
    }
}
