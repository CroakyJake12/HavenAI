using System.Diagnostics;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

public sealed class BrowserToolRuntime(IBrowserToolService browser)
{
    private static readonly IReadOnlyList<OllamaToolDefinition> BackgroundToolDefinitions =
    [
        Definition("browser_navigate", "Navigate Haven's isolated browser to a URL or search query.",
            new() { ["address"] = StringProperty("URL or search query.") }, "address"),
        Definition("browser_read_page", "Read the currently visible page text from Haven Browse.", new())
    ];

    private static readonly IReadOnlyList<OllamaToolDefinition> InteractiveToolDefinitions =
    [
        Definition("browser_click", "Click an element using a CSS selector in Haven Browse.",
            new() { ["selector"] = StringProperty("CSS selector for one visible element.") }, "selector"),
        Definition("browser_click_text", "Click the first visible button or link whose text contains the supplied text.",
            new() { ["text"] = StringProperty("Visible link or button text.") }, "text"),
        Definition("browser_fill", "Fill an input using a CSS selector.",
            new() { ["selector"] = StringProperty("CSS selector for the input."), ["value"] = StringProperty("Text to enter.") }, "selector", "value"),
        Definition("browser_back", "Go back in the selected browser tab.", new()),
        Definition("browser_forward", "Go forward in the selected browser tab.", new()),
        Definition("browser_reload", "Reload the selected tab, optionally clearing page caches first.",
            new() { ["clear_cache"] = BooleanProperty("Clear Cache Storage and reload when true.") }),
        Definition("browser_scroll", "Scroll the selected page by a number of pixels.",
            new() { ["x"] = NumberProperty("Horizontal pixels."), ["y"] = NumberProperty("Vertical pixels.") })
    ];

    public IReadOnlyList<OllamaToolDefinition> BackgroundDefinitions => BackgroundToolDefinitions;
    public IReadOnlyList<OllamaToolDefinition> InteractiveDefinitions => InteractiveToolDefinitions;
    public IReadOnlyList<OllamaToolDefinition> Definitions => [.. BackgroundToolDefinitions, .. InteractiveToolDefinitions];
    public bool IsInteractiveAvailable => browser.IsInteractiveAvailable;

    public async Task<WorkspaceToolResult> ExecuteAsync(OllamaToolCall call, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var output = call.Name switch
            {
                "browser_navigate" => await browser.NavigateAsync(RequiredText(call, "address"), cancellationToken).ConfigureAwait(false),
                "browser_read_page" => await browser.ReadVisibleTextAsync(cancellationToken).ConfigureAwait(false),
                "browser_click" => await browser.ClickAsync(RequiredText(call, "selector"), cancellationToken).ConfigureAwait(false),
                "browser_click_text" => await browser.ClickTextAsync(RequiredText(call, "text"), cancellationToken).ConfigureAwait(false),
                "browser_fill" => await browser.FillAsync(RequiredText(call, "selector"), Text(call, "value"), cancellationToken).ConfigureAwait(false),
                "browser_back" => await browser.BackAsync(cancellationToken).ConfigureAwait(false),
                "browser_forward" => await browser.ForwardAsync(cancellationToken).ConfigureAwait(false),
                "browser_reload" => await browser.ReloadAsync(Boolean(call, "clear_cache"), cancellationToken).ConfigureAwait(false),
                "browser_scroll" => await browser.ScrollAsync(Number(call, "x"), Number(call, "y", 650), cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException($"Unknown browser tool '{call.Name}'.")
            };
            if (output.Length > 120_000) output = output[..120_000] + "\n[page output truncated]";
            return new WorkspaceToolResult(new ToolActivity(Guid.NewGuid(), HumanLabel(call.Name), FirstLine(output), true,
                Stopwatch.GetElapsedTime(started), DateTimeOffset.UtcNow), output);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new WorkspaceToolResult(new ToolActivity(Guid.NewGuid(), HumanLabel(call.Name), ex.Message, false,
                Stopwatch.GetElapsedTime(started), DateTimeOffset.UtcNow), "Browser tool error: " + ex.Message);
        }
    }

    private static OllamaToolDefinition Definition(string name, string description, Dictionary<string, object> properties, params string[] required) =>
        new(name, description, properties, required);
    private static Dictionary<string, object> StringProperty(string description) => new() { ["type"] = "string", ["description"] = description };
    private static Dictionary<string, object> BooleanProperty(string description) => new() { ["type"] = "boolean", ["description"] = description };
    private static Dictionary<string, object> NumberProperty(string description) => new() { ["type"] = "number", ["description"] = description };
    private static string RequiredText(OllamaToolCall call, string key) => string.IsNullOrWhiteSpace(Text(call, key)) ? throw new ArgumentException($"{key} is required.") : Text(call, key);
    private static string Text(OllamaToolCall call, string key, string fallback = "") => call.Arguments.TryGetValue(key, out var value) ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : value.ToString() : fallback;
    private static bool Boolean(OllamaToolCall call, string key) => call.Arguments.TryGetValue(key, out var value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var result) && result);
    private static double Number(OllamaToolCall call, string key, double fallback = 0) => call.Arguments.TryGetValue(key, out var value) && value.TryGetDouble(out var result) ? result : fallback;
    private static string FirstLine(string value) => value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "Completed";
    private static string HumanLabel(string name) => name switch
    {
        "browser_navigate" => "Navigated browser",
        "browser_read_page" => "Read page",
        "browser_click" or "browser_click_text" => "Clicked page element",
        "browser_fill" => "Filled page input",
        "browser_back" => "Went back",
        "browser_forward" => "Went forward",
        "browser_reload" => "Reloaded page",
        "browser_scroll" => "Scrolled page",
        _ => name.Replace('_', ' ')
    };
}
