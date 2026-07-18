/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/AutomationToolRuntime.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns AutomationToolRuntime. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Diagnostics;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Represents automation tool runtime and keeps its related state and behavior together.
/// </summary>
public sealed class AutomationToolRuntime(IAutomationRepository automations, IWorkspaceStateRepository workspaceState)
{
    /// <summary>
    /// Retrieves definitions for the current operation.
    /// </summary>
    public IReadOnlyList<OllamaToolDefinition> GetDefinitions(bool enableAutomations, bool enableMacros)
    {
        var result = new List<OllamaToolDefinition>();
        if (enableAutomations)
        {
            result.Add(Definition("automation_create", "Create a reviewable Haven Scheduled Action after the user has supplied or confirmed its schedule.",
                new()
                {
                    ["name"] = StringProperty("Short action name."),
                    ["instruction"] = StringProperty("Complete action instruction."),
                    ["schedule_kind"] = StringProperty("Once, Hourly, Daily, Weekly, or ConditionWatch."),
                    ["schedule_json"] = StringProperty("Schedule configuration JSON, for example {\"time\":\"08:00\"}.")
                }, "name", "instruction", "schedule_kind", "schedule_json"));
        }
        if (enableMacros)
        {
            result.Add(Definition("macro_create", "Create a click-to-run Haven macro. Macros are inert until the user invokes them.",
                new()
                {
                    ["name"] = StringProperty("Short macro button name."),
                    ["description"] = StringProperty("What the macro does."),
                    ["instruction"] = StringProperty("Instruction executed when clicked.")
                }, "name", "instruction"));
            result.Add(Definition("macro_list", "List enabled Haven macros available to this task group or project.", new()));
        }
        return result;
    }

    /// <summary>
    /// Runs execute async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    public async Task<WorkspaceToolResult> ExecuteAsync(OllamaToolCall call, HavenMode mode, Guid? containerId, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var output = call.Name switch
            {
                "automation_create" => await CreateAutomationAsync(call, mode, containerId, cancellationToken).ConfigureAwait(false),
                "macro_create" => await CreateMacroAsync(call, containerId, cancellationToken).ConfigureAwait(false),
                "macro_list" => await ListMacrosAsync(containerId, cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException($"Unknown automation tool '{call.Name}'.")
            };
            return new WorkspaceToolResult(new ToolActivity(Guid.NewGuid(), Label(call.Name), output.Split('\n')[0], true,
                Stopwatch.GetElapsedTime(started), DateTimeOffset.UtcNow), output);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new WorkspaceToolResult(new ToolActivity(Guid.NewGuid(), Label(call.Name), ex.Message, false,
                Stopwatch.GetElapsedTime(started), DateTimeOffset.UtcNow), "Automation tool error: " + ex.Message);
        }
    }

    /// <summary>
    /// Creates automation async with the invariants required by its callers.
    /// </summary>
    private async Task<string> CreateAutomationAsync(OllamaToolCall call, HavenMode mode, Guid? containerId, CancellationToken cancellationToken)
    {
        var name = RequiredText(call, "name");
        var instruction = RequiredText(call, "instruction");
        if (!Enum.TryParse<AutomationScheduleKind>(RequiredText(call, "schedule_kind"), true, out var kind))
            throw new ArgumentException("schedule_kind must be Once, Hourly, Daily, Weekly, or ConditionWatch.");
        var scheduleJson = RequiredText(call, "schedule_json");
        using var schedule = JsonDocument.Parse(scheduleJson);
        var now = DateTimeOffset.UtcNow;
        var next = CalculateInitialRun(kind, schedule.RootElement, now);
        var item = new AutomationDefinition(Guid.NewGuid(), name, mode, instruction, kind, scheduleJson, next, containerId, true, now, now);
        await automations.UpsertAsync(item, cancellationToken).ConfigureAwait(false);
        return $"Created Scheduled Action '{name}'. Next run: {next?.LocalDateTime:g}. It can be reviewed or paused from Scheduled Actions.";
    }

    /// <summary>
    /// Creates macro async with the invariants required by its callers.
    /// </summary>
    private async Task<string> CreateMacroAsync(OllamaToolCall call, Guid? containerId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var item = new MacroDefinition(Guid.NewGuid(), RequiredText(call, "name"), Text(call, "description"), RequiredText(call, "instruction"),
            containerId, true, now, now);
        await workspaceState.UpsertMacroAsync(item, cancellationToken).ConfigureAwait(false);
        return $"Created macro '{item.Name}'. It will run only when the user clicks or explicitly invokes it.";
    }

    /// <summary>
    /// Performs list macros async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<string> ListMacrosAsync(Guid? containerId, CancellationToken cancellationToken)
    {
        var items = await workspaceState.GetMacrosAsync(containerId, cancellationToken).ConfigureAwait(false);
        return items.Count == 0 ? "No enabled macros are available." : string.Join('\n', items.Select(item => $"{item.Name}: {item.Description}\nInstruction: {item.Instruction}"));
    }

    /// <summary>
    /// Performs the calculate initial run step owned by this component.
    /// </summary>
    private static DateTimeOffset? CalculateInitialRun(AutomationScheduleKind kind, JsonElement schedule, DateTimeOffset now)
    {
        if (kind == AutomationScheduleKind.ConditionWatch) return now.AddMinutes(5);
        if (kind == AutomationScheduleKind.Hourly) return now.AddHours(1);
        if (kind == AutomationScheduleKind.Once)
        {
            if (schedule.TryGetProperty("at", out var at) && DateTimeOffset.TryParse(at.GetString(), out var timestamp)) return timestamp;
            return now.AddMinutes(5);
        }
        var time = schedule.TryGetProperty("time", out var timeElement) && TimeOnly.TryParse(timeElement.GetString(), out var parsed) ? parsed : new TimeOnly(8, 0);
        var local = now.ToLocalTime();
        var candidate = new DateTimeOffset(local.Year, local.Month, local.Day, time.Hour, time.Minute, 0, local.Offset);
        if (candidate <= local) candidate = candidate.AddDays(kind == AutomationScheduleKind.Weekly ? 7 : 1);
        return candidate.ToUniversalTime();
    }

    /// <summary>
    /// Performs the definition step owned by this component.
    /// </summary>
    private static OllamaToolDefinition Definition(string name, string description, Dictionary<string, object> properties, params string[] required) => new(name, description, properties, required);
    /// <summary>
    /// Performs the string property step owned by this component.
    /// </summary>
    private static Dictionary<string, object> StringProperty(string description) => new() { ["type"] = "string", ["description"] = description };
    /// <summary>
    /// Performs the required text step owned by this component.
    /// </summary>
    private static string RequiredText(OllamaToolCall call, string name) => string.IsNullOrWhiteSpace(Text(call, name)) ? throw new ArgumentException($"{name} is required.") : Text(call, name);
    /// <summary>
    /// Performs the text step owned by this component.
    /// </summary>
    private static string Text(OllamaToolCall call, string name) => call.Arguments.TryGetValue(name, out var value) ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString() : string.Empty;
    /// <summary>
    /// Performs the label step owned by this component.
    /// </summary>
    private static string Label(string name) => name switch { "automation_create" => "Created Scheduled Action", "macro_create" => "Created macro", "macro_list" => "Listed macros", _ => name };
}
