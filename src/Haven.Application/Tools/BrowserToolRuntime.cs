/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/BrowserToolRuntime.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns BrowserToolRuntime. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Represents browser tool runtime and keeps its related state and behavior together.
/// </summary>
public sealed class BrowserToolRuntime
{
    /// <summary>
    /// Stores browser locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IBrowserToolService _browser;
    /// <summary>
    /// Stores automation locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IBrowserAutomationService _automation;

    public BrowserToolRuntime(IBrowserToolService browser, IBrowserAutomationService automation)
    {
        _browser = browser;
        _automation = automation;
    }

    public BrowserToolRuntime(IBrowserToolService browser)
        : this(browser, BrowserAutomationRegistry.Resolve(browser))
    {
    }

    /// <summary>
    /// Stores background tool definitions locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly IReadOnlyList<OllamaToolDefinition> BackgroundToolDefinitions =
    [
        Definition("browser_navigate", "Navigate Haven's isolated browser to a public HTTP or HTTPS URL or search query. Local and private network destinations are blocked.",
            new() { ["address"] = StringProperty("Public URL or search query.") }, "address"),
        Definition("browser_snapshot", "Capture bounded visible page text, headings, and stable references for links, buttons, and editable fields.", new()),
        Definition("browser_read_page", "Alias for browser_snapshot for compatibility with existing prompts.", new()),
        Definition("browser_download", "Request a download. Haven never downloads until the user approves the pending action in Browser safety.",
            new()
            {
                ["address"] = StringProperty("Public HTTP or HTTPS download URL."),
                ["file_name"] = StringProperty("Optional suggested file name.")
            }, "address")
    ];

    /// <summary>
    /// Stores interactive tool definitions locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly IReadOnlyList<OllamaToolDefinition> InteractiveToolDefinitions =
    [
        Definition("browser_click_ref", "Click a stable element reference returned by browser_snapshot. Form submission requires explicit user approval.",
            new() { ["reference"] = StringProperty("Element reference such as haven-12.") }, "reference"),
        Definition("browser_fill_ref", "Fill a non-sensitive input reference returned by browser_snapshot. Password, file, hidden, payment, and one-time-code fields are blocked.",
            new()
            {
                ["reference"] = StringProperty("Editable element reference such as haven-4."),
                ["value"] = StringProperty("Text to enter. The value is not written to the browser audit log.")
            }, "reference", "value"),
        Definition("browser_back", "Go back in the selected browser tab.", new()),
        Definition("browser_forward", "Go forward in the selected browser tab.", new()),
        Definition("browser_reload", "Reload the selected tab, optionally clearing page Cache Storage first.",
            new() { ["clear_cache"] = BooleanProperty("Clear Cache Storage and reload when true.") }),
        Definition("browser_scroll", "Scroll the selected page by a bounded number of pixels.",
            new() { ["x"] = NumberProperty("Horizontal pixels."), ["y"] = NumberProperty("Vertical pixels.") })
    ];

    /// <summary>
    /// Gets or updates background definitions, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<OllamaToolDefinition> BackgroundDefinitions => BackgroundToolDefinitions;
    /// <summary>
    /// Gets or updates interactive definitions, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<OllamaToolDefinition> InteractiveDefinitions => InteractiveToolDefinitions;
    /// <summary>
    /// Gets or updates definitions, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<OllamaToolDefinition> Definitions => [.. BackgroundToolDefinitions, .. InteractiveToolDefinitions];
    /// <summary>
    /// Reports whether interactive available applies to the current state.
    /// </summary>
    public bool IsInteractiveAvailable => _browser.IsInteractiveAvailable;

    /// <summary>
    /// Runs execute async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    public async Task<WorkspaceToolResult> ExecuteAsync(OllamaToolCall call, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var output = call.Name switch
            {
                "browser_navigate" => await _automation.NavigateAsync(RequiredText(call, "address"), cancellationToken).ConfigureAwait(false),
                "browser_snapshot" or "browser_read_page" => FormatSnapshot(await _automation.CapturePageAsync(cancellationToken).ConfigureAwait(false)),
                "browser_click_ref" => await _automation.ClickReferenceAsync(RequiredText(call, "reference"), cancellationToken).ConfigureAwait(false),
                "browser_fill_ref" => await _automation.FillReferenceAsync(RequiredText(call, "reference"), Text(call, "value") ?? string.Empty, cancellationToken).ConfigureAwait(false),
                "browser_download" => FormatPending(await _automation.RequestDownloadAsync(
                    RequiredText(call, "address"), Text(call, "file_name", null), cancellationToken).ConfigureAwait(false)),
                "browser_back" => await _browser.BackAsync(cancellationToken).ConfigureAwait(false),
                "browser_forward" => await _browser.ForwardAsync(cancellationToken).ConfigureAwait(false),
                "browser_reload" => await _browser.ReloadAsync(Boolean(call, "clear_cache"), cancellationToken).ConfigureAwait(false),
                "browser_scroll" => await _browser.ScrollAsync(
                    Math.Clamp(Number(call, "x"), -10_000, 10_000),
                    Math.Clamp(Number(call, "y", 650), -10_000, 10_000),
                    cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException($"Unknown browser tool '{call.Name}'. Refresh the page snapshot and use the currently advertised browser tools.")
            };
            if (output.Length > 120_000) output = output[..120_000] + "\n[page output truncated]";
            return new WorkspaceToolResult(new ToolActivity(Guid.NewGuid(), HumanLabel(call.Name), FirstLine(output), true,
                Stopwatch.GetElapsedTime(started), DateTimeOffset.UtcNow), output);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new WorkspaceToolResult(new ToolActivity(Guid.NewGuid(), HumanLabel(call.Name), exception.Message, false,
                Stopwatch.GetElapsedTime(started), DateTimeOffset.UtcNow), "Browser tool error: " + exception.Message);
        }
    }

    /// <summary>
    /// Performs the format snapshot step owned by this component.
    /// </summary>
    private static string FormatSnapshot(BrowserPageSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.Append("Page: ").AppendLine(snapshot.Title)
            .Append("URL: ").AppendLine(snapshot.Address?.ToString() ?? "unknown")
            .Append("Interactive: ").AppendLine(snapshot.IsInteractive ? "yes" : "no")
            .Append("Truncated: ").AppendLine(snapshot.WasTruncated ? "yes" : "no");
        if (snapshot.Headings.Count > 0)
        {
            builder.AppendLine("\nHeadings:");
            foreach (var heading in snapshot.Headings.Take(100)) builder.Append("- ").AppendLine(heading);
        }
        if (snapshot.Elements.Count > 0)
        {
            builder.AppendLine("\nVisible elements:");
            foreach (var element in snapshot.Elements.Take(400))
            {
                builder.Append('[').Append(element.Reference).Append("] ").Append(element.Kind).Append(' ')
                    .Append(string.IsNullOrWhiteSpace(element.Text) ? "(unlabelled)" : element.Text.ReplaceLineEndings(" "));
                if (!string.IsNullOrWhiteSpace(element.Address)) builder.Append(" -> ").Append(element.Address);
                if (element.IsSensitive) builder.Append(" [sensitive-blocked]");
                if (element.SubmitsForm) builder.Append(" [approval-required]");
                builder.AppendLine();
            }
        }
        builder.AppendLine("\nVisible text:").Append(snapshot.Text);
        return builder.ToString();
    }

    /// <summary>
    /// Performs the format pending step owned by this component.
    /// </summary>
    private static string FormatPending(BrowserPendingAction action) =>
        $"Approval required. Pending browser action {action.Id} expires at {action.ExpiresAt:O}. Open Browser safety to approve or reject: {action.Summary}";

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
    /// Performs the boolean property step owned by this component.
    /// </summary>
    private static Dictionary<string, object> BooleanProperty(string description) => new() { ["type"] = "boolean", ["description"] = description };
    /// <summary>
    /// Performs the number property step owned by this component.
    /// </summary>
    private static Dictionary<string, object> NumberProperty(string description) => new() { ["type"] = "number", ["description"] = description };
    /// <summary>
    /// Performs the required text step owned by this component.
    /// </summary>
    private static string RequiredText(OllamaToolCall call, string key) => string.IsNullOrWhiteSpace(Text(call, key)) ? throw new ArgumentException($"{key} is required.") : Text(call, key)!;
    /// <summary>
    /// Performs the text step owned by this component.
    /// </summary>
    private static string? Text(OllamaToolCall call, string key, string? fallback = "") => call.Arguments.TryGetValue(key, out var value) ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : value.ToString() : fallback;
    /// <summary>
    /// Performs the boolean step owned by this component.
    /// </summary>
    private static bool Boolean(OllamaToolCall call, string key) => call.Arguments.TryGetValue(key, out var value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var result) && result);
    /// <summary>
    /// Performs the number step owned by this component.
    /// </summary>
    private static double Number(OllamaToolCall call, string key, double fallback = 0) => call.Arguments.TryGetValue(key, out var value) && value.TryGetDouble(out var result) ? result : fallback;
    /// <summary>
    /// Performs the first line step owned by this component.
    /// </summary>
    private static string FirstLine(string value) => value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "Completed";
    /// <summary>
    /// Performs the human label step owned by this component.
    /// </summary>
    private static string HumanLabel(string name) => name switch
    {
        "browser_navigate" => "Navigated browser",
        "browser_snapshot" or "browser_read_page" => "Captured page snapshot",
        "browser_click_ref" => "Clicked page reference",
        "browser_fill_ref" => "Filled page reference",
        "browser_download" => "Requested download approval",
        "browser_back" => "Went back",
        "browser_forward" => "Went forward",
        "browser_reload" => "Reloaded page",
        "browser_scroll" => "Scrolled page",
        _ => name.Replace('_', ' ')
    };
}