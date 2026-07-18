/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/ChatSessionService.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns ChatSessionService, ChatStreamEvent, ChatStreamEventKind. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Represents chat session service and keeps its related state and behavior together.
/// </summary>
public sealed class ChatSessionService(
    IConversationRepository conversations,
    IOllamaClient ollama,
    CapabilityPreflightService preflight,
    WorkspaceToolRuntime workspaceTools,
    ComputerToolRuntime computerTools,
    BrowserToolRuntime? browserTools = null,
    AutomationToolRuntime? automationTools = null,
    ToolAvailabilityPlanner? toolAvailability = null)
{
    /// <summary>
    /// Retrieves tool availability for the current operation.
    /// </summary>
    public ToolAvailabilityPlan GetToolAvailability(
        HavenMode mode,
        string? workspaceRoot,
        IReadOnlyCollection<ActivePlugin> plugins,
        PermissionMode filePermission,
        PermissionMode commandPermission,
        PermissionMode browserPermission)
    {
        var computerPass = computerTools.CreatePass();
        return CreateAvailabilityPlan(mode, workspaceRoot, plugins, filePermission, commandPermission, browserPermission,
            computerPass.Definitions);
    }

    /// <summary>
    /// Reports whether activate plugin applies to the current state.
    /// </summary>
    public bool CanActivatePlugin(
        string pluginName,
        HavenMode mode,
        string? workspaceRoot,
        PermissionMode filePermission,
        PermissionMode commandPermission,
        PermissionMode browserPermission) =>
        GetToolAvailability(mode, workspaceRoot, [new ActivePlugin(pluginName, pluginName, false)],
            Approvable(filePermission), Approvable(commandPermission), Approvable(browserPermission)).IsPluginAvailable(pluginName);

    /// <summary>
    /// Performs send asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async IAsyncEnumerable<ChatStreamEvent> SendAsync(
        Conversation conversation,
        string prompt,
        ModelDescriptor model,
        EffortLevel effort,
        IReadOnlyCollection<ActivePlugin> plugins,
        string agentName,
        string agentInstructions,
        DuoMode duoMode,
        string? workspaceRoot,
        string? projectContext,
        string? projectInstructions,
        IReadOnlyList<string>? images,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken,
        IReadOnlyCollection<ActivePrompt>? prompts = null,
        string? registeredContext = null,
        GenerationOptions? generationOptions = null,
        PermissionMode filePermission = PermissionMode.FullAccess,
        PermissionMode commandPermission = PermissionMode.FullAccess,
        PermissionMode browserPermission = PermissionMode.FullAccess)
    {
        var computerPassCandidate = computerTools.CreatePass();
        var availabilityPlan = CreateAvailabilityPlan(
            conversation.Mode,
            workspaceRoot,
            plugins,
            filePermission,
            commandPermission,
            browserPermission,
            computerPassCandidate.Definitions);
        var availablePlugins = availabilityPlan.FilterPlugins(plugins);
        var installed = await ollama.GetModelsAsync(cancellationToken).ConfigureAwait(false);
        var check = preflight.Evaluate(model, availablePlugins, images is { Count: > 0 }, installed);
        if (!check.IsCompatible)
        {
            yield return ChatStreamEvent.Preflight(check);
            yield break;
        }

        var now = DateTimeOffset.UtcNow;
        var userMessage = new ChatMessage(Guid.NewGuid(), conversation.Id, MessageRole.User, prompt, null, null, null, now);
        if (!conversation.IsTemporary)
        {
            await conversations.UpsertConversationAsync(conversation with { UpdatedAt = now }, cancellationToken).ConfigureAwait(false);
            await conversations.AddMessageAsync(userMessage, cancellationToken).ConfigureAwait(false);
        }
        yield return ChatStreamEvent.User(userMessage);

        var history = await conversations.GetContextMessagesAsync(conversation.Id, cancellationToken).ConfigureAwait(false);
        var requestMessages = history
            .Where(message => message.Role is MessageRole.User or MessageRole.Assistant)
            .Select(message => new OllamaMessage(message.Role == MessageRole.User ? "user" : "assistant", message.Content))
            .ToList();
        if (images is { Count: > 0 } && requestMessages.Count > 0 && requestMessages[^1].Role == "user" && requestMessages[^1].Content == prompt)
            requestMessages[^1] = new OllamaMessage("user", prompt, images);
        else if (conversation.IsTemporary || requestMessages.Count == 0 || requestMessages[^1].Content != prompt)
            requestMessages.Add(new OllamaMessage("user", prompt, images));

        var modelPlan = availabilityPlan.RestrictToModel(model);
        var modelPlugins = modelPlan.FilterPlugins(availablePlugins);
        var toolDefinitions = modelPlan.Definitions;
        var computerPass = modelPlan.HasRuntime(ToolRuntimeKind.Computer) ? computerPassCandidate : null;
        var canUseTools = toolDefinitions.Count > 0;
        var system = BuildSystemPrompt(
            conversation, modelPlugins, prompts ?? [], agentName, agentInstructions, duoMode,
            modelPlan.HasRuntime(ToolRuntimeKind.Workspace) ? workspaceRoot : null,
            projectContext, projectInstructions, registeredContext, computerPass is not null);
        var assistantId = Guid.NewGuid();
        var buffer = new StringBuilder();
        yield return ChatStreamEvent.AssistantStarted(assistantId, model.Name, agentName);

        if (canUseTools)
        {
            var turns = requestMessages.Select(message => new OllamaToolTurn(message.Role, message.Content, Images: message.Images)).ToList();
            var toolCallLimit = Math.Clamp(generationOptions?.ActionLimit ?? 24, 1, 100);
            var callsUsed = 0;
            var bridgeAttempted = false;
            WorkspaceToolResult? lastToolResult = null;
            OllamaToolCall? lastToolCall = null;

            async Task<WorkspaceToolResult> ExecuteToolAsync(OllamaToolCall call)
            {
                if (modelPlan.TryGetRuntime(call.Name, out var runtime))
                {
                    if (runtime == ToolRuntimeKind.Computer && computerPass is not null)
                        return await computerPass.ExecuteAsync(call, cancellationToken).ConfigureAwait(false);
                    if (runtime == ToolRuntimeKind.Browser && browserTools is not null)
                        return await browserTools.ExecuteAsync(call, cancellationToken).ConfigureAwait(false);
                    if (runtime == ToolRuntimeKind.Automation && automationTools is not null)
                        return await automationTools.ExecuteAsync(call, conversation.Mode, conversation.ContainerId, cancellationToken).ConfigureAwait(false);
                    if (runtime == ToolRuntimeKind.Workspace && workspaceRoot is not null)
                        return await workspaceTools.ExecuteAsync(workspaceRoot, call, cancellationToken, conversation.Id, conversation.ContainerId).ConfigureAwait(false);
                }
                var detail = modelPlan.GetUnavailableReason(call.Name);
                return new WorkspaceToolResult(
                    new ToolActivity(Guid.NewGuid(), call.Name.Replace('_', ' '), detail, false, TimeSpan.Zero, DateTimeOffset.UtcNow),
                    "Tool error: " + detail);
            }

            var bootstrapCall = computerPass?.TryCreateBootstrapCall(prompt);
            if (bootstrapCall is not null)
            {
                callsUsed++;
                lastToolCall = bootstrapCall;
                lastToolResult = await ExecuteToolAsync(bootstrapCall).ConfigureAwait(false);
                yield return ChatStreamEvent.Activity(lastToolResult.Activity);
                var directResult = lastToolResult.Activity.Succeeded
                    ? CompletedActionMessage(bootstrapCall)
                    : $"The tool action could not complete: {lastToolResult.Activity.Detail}";
                buffer.Append(directResult);
                yield return ChatStreamEvent.AssistantDelta(assistantId, directResult);
            }

            while (bootstrapCall is null && callsUsed < toolCallLimit)
            {
                cancellationToken.ThrowIfCancellationRequested();
                OllamaToolResponse? response = null;
                var unsupportedToolSchema = false;
                try
                {
                    response = await ollama.ChatWithToolsAsync(new OllamaToolRequest(
                        model.Name, turns, toolDefinitions, effort, system, generationOptions), cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException ex) when (IsUnsupportedToolSchema(ex))
                {
                    unsupportedToolSchema = true;
                }

                if (unsupportedToolSchema)
                {
                    bridgeAttempted = true;
                    var bridged = LooksLikeToolRequest(prompt)
                        ? await TryBridgeToolCallAsync(ollama, model, effort, prompt, toolDefinitions, generationOptions, cancellationToken).ConfigureAwait(false)
                        : null;
                    if (bridged is not null)
                    {
                        callsUsed++;
                        lastToolCall = bridged;
                        lastToolResult = await ExecuteToolAsync(bridged).ConfigureAwait(false);
                        yield return ChatStreamEvent.Activity(lastToolResult.Activity);
                        var bridgedResult = lastToolResult.Activity.Succeeded
                            ? CompletedActionMessage(bridged)
                            : $"The tool action could not complete: {lastToolResult.Activity.Detail}";
                        buffer.Append(bridgedResult);
                        yield return ChatStreamEvent.AssistantDelta(assistantId, bridgedResult);
                    }
                    else
                    {
                        const string unsupported = "This local model cannot emit tool calls for that request. Choose a tool-capable model, or use a directly supported Computer Use launch request.";
                        buffer.Append(unsupported);
                        yield return ChatStreamEvent.AssistantDelta(assistantId, unsupported);
                    }
                    break;
                }

                if (response is null)
                    throw new InvalidOperationException("Ollama returned no tool response.");
                if (response.ToolCalls.Count == 0)
                {
                    if (!bridgeAttempted && callsUsed == 0 && LooksLikeToolRequest(prompt))
                    {
                        bridgeAttempted = true;
                        var bridged = await TryBridgeToolCallAsync(ollama, model, effort, prompt, toolDefinitions, generationOptions, cancellationToken).ConfigureAwait(false);
                        if (bridged is not null)
                        {
                            callsUsed++;
                            lastToolCall = bridged;
                            lastToolResult = await ExecuteToolAsync(bridged).ConfigureAwait(false);
                            yield return ChatStreamEvent.Activity(lastToolResult.Activity);
                            turns.Add(new OllamaToolTurn("assistant", string.Empty, [bridged]));
                            turns.Add(new OllamaToolTurn("tool", lastToolResult.Output, ToolName: bridged.Name));
                            continue;
                        }
                    }
                    var content = string.IsNullOrWhiteSpace(response.Content)
                        ? "The tool pass completed without a final model response. Review the activity above."
                        : response.Content;
                    if (lastToolResult?.Activity.Succeeded == true && lastToolCall is not null && ResponseContradictsCompletedAction(content))
                        content = CompletedActionMessage(lastToolCall);
                    buffer.Append(content);
                    yield return ChatStreamEvent.AssistantDelta(assistantId, content);
                    break;
                }

                turns.Add(new OllamaToolTurn("assistant", response.Content, response.ToolCalls));
                if (!string.IsNullOrWhiteSpace(response.Content))
                {
                    buffer.Append(response.Content);
                    yield return ChatStreamEvent.AssistantDelta(assistantId, response.Content);
                }
                foreach (var call in response.ToolCalls)
                {
                    if (callsUsed >= toolCallLimit) break;
                    callsUsed++;
                    lastToolCall = call;
                    var result = await ExecuteToolAsync(call).ConfigureAwait(false);
                    lastToolResult = result;
                    yield return ChatStreamEvent.Activity(result.Activity);
                    turns.Add(new OllamaToolTurn("tool", result.Output, ToolName: call.Name));
                }
            }

            if (buffer.Length == 0)
            {
                var limitMessage = $"Stopped after reaching the tool-call safety limit of {toolCallLimit}. Review the activity before continuing.";
                buffer.Append(limitMessage);
                yield return ChatStreamEvent.AssistantDelta(assistantId, limitMessage);
            }
        }
        else
        {
            await foreach (var chunk in ollama.StreamChatAsync(new(model.Name, requestMessages, effort, system, Options: generationOptions), cancellationToken).ConfigureAwait(false))
            {
                buffer.Append(chunk);
                yield return ChatStreamEvent.AssistantDelta(assistantId, chunk);
            }
        }

        var assistant = new ChatMessage(assistantId, conversation.Id, MessageRole.Assistant, buffer.ToString(), agentName, model.Name, null, DateTimeOffset.UtcNow);
        if (!conversation.IsTemporary)
            await conversations.AddMessageAsync(assistant, cancellationToken).ConfigureAwait(false);
        yield return ChatStreamEvent.AssistantCompleted(assistant);
    }

    /// <summary>
    /// Creates availability plan with the invariants required by its callers.
    /// </summary>
    private ToolAvailabilityPlan CreateAvailabilityPlan(
        HavenMode mode,
        string? workspaceRoot,
        IReadOnlyCollection<ActivePlugin> plugins,
        PermissionMode filePermission,
        PermissionMode commandPermission,
        PermissionMode browserPermission,
        IReadOnlyList<OllamaToolDefinition> computerDefinitions) =>
        (toolAvailability ?? ToolAvailabilityPlanner.Default).Create(
            new ToolAvailabilityContext(
                mode,
                workspaceRoot,
                plugins,
                filePermission,
                commandPermission,
                browserPermission,
                OperatingSystem.IsWindows(),
                browserTools is not null,
                browserTools?.IsInteractiveAvailable == true,
                automationTools is not null),
            new ToolDefinitionSources(
                workspaceTools.Definitions,
                computerDefinitions,
                browserTools?.BackgroundDefinitions ?? [],
                browserTools?.InteractiveDefinitions ?? [],
                automationTools?.GetDefinitions(true, false) ?? [],
                automationTools?.GetDefinitions(false, true) ?? []));

    /// <summary>
    /// Performs the approvable step owned by this component.
    /// </summary>
    private static PermissionMode Approvable(PermissionMode permission) =>
        permission == PermissionMode.Ask ? PermissionMode.FullAccess : permission;

    /// <summary>
    /// Performs the looks like tool request step owned by this component.
    /// </summary>
    private static bool LooksLikeToolRequest(string prompt)
    {
        var value = prompt.TrimStart();
        return new[] { "open ", "launch ", "start ", "click ", "type ", "press ", "focus ", "close ", "run " }
            .Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Reports whether unsupported tool schema applies to the current state.
    /// </summary>
    private static bool IsUnsupportedToolSchema(HttpRequestException exception) =>
        exception.Message.Contains("does not support tools", StringComparison.OrdinalIgnoreCase) ||
        exception.Message.Contains("tool support", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Performs the response contradicts completed action step owned by this component.
    /// </summary>
    private static bool ResponseContradictsCompletedAction(string response)
    {
        var value = response.ToLowerInvariant();
        return value.Contains("i can't directly", StringComparison.Ordinal) ||
               value.Contains("i cannot directly", StringComparison.Ordinal) ||
               value.Contains("i can guide you", StringComparison.Ordinal) ||
               value.Contains("how to open", StringComparison.Ordinal) ||
               value.Contains("press the windows key", StringComparison.Ordinal) ||
               value.Contains("unable to control", StringComparison.Ordinal) ||
               value.Contains("cannot open applications", StringComparison.Ordinal);
    }

    /// <summary>
    /// Performs the completed action message step owned by this component.
    /// </summary>
    private static string CompletedActionMessage(OllamaToolCall call) => call.Name switch
    {
        "computer_launch_app" => $"Done — opened {ArgumentText(call, "name", "the application")}.",
        "computer_focus_window" => $"Done — focused {ArgumentText(call, "title", "the requested window")}.",
        "computer_close_window" => $"Done — requested that {ArgumentText(call, "title", "the requested window")} close.",
        "computer_invoke" or "computer_click" => "Done — used the requested desktop control.",
        "computer_type" => "Done — typed into the requested window.",
        "computer_press" => $"Done — pressed {ArgumentText(call, "keys", "the requested keys")}.",
        _ => "Done — the requested tool action completed."
    };

    /// <summary>
    /// Performs the argument text step owned by this component.
    /// </summary>
    private static string ArgumentText(OllamaToolCall call, string name, string fallback) =>
        call.Arguments.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : fallback;

    /// <summary>
    /// Attempts to bridge tool call async and reports the result without using failure for normal control flow.
    /// </summary>
    private static async Task<OllamaToolCall?> TryBridgeToolCallAsync(
        IOllamaClient ollama,
        ModelDescriptor model,
        EffortLevel effort,
        string prompt,
        IReadOnlyList<OllamaToolDefinition> definitions,
        GenerationOptions? generationOptions,
        CancellationToken cancellationToken)
    {
        try
        {
            var allowed = definitions.Select(definition => definition.Name).ToHashSet(StringComparer.Ordinal);
            var system = "You are Haven's compatibility tool router. Choose exactly one appropriate tool for the user's action request. " +
                         "Return only one JSON object in the form {\"name\":\"tool_name\",\"arguments\":{}}. " +
                         "Use an empty name when no tool is appropriate. Available tools: " + JsonSerializer.Serialize(definitions);
            var response = await ollama.CompleteAsync(new OllamaChatRequest(
                model.Name, [new OllamaMessage("user", prompt)], effort, system, Options: generationOptions), cancellationToken).ConfigureAwait(false);
            var start = response.IndexOf('{');
            var end = response.LastIndexOf('}');
            if (start < 0 || end <= start) return null;
            using var document = JsonDocument.Parse(response[start..(end + 1)]);
            var root = document.RootElement;
            var name = root.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(name) && root.TryGetProperty("tool", out var toolElement)) name = toolElement.GetString();
            if (string.IsNullOrWhiteSpace(name) || !allowed.Contains(name)) return null;
            var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (root.TryGetProperty("arguments", out var argumentElement) && argumentElement.ValueKind == JsonValueKind.Object)
                foreach (var property in argumentElement.EnumerateObject()) arguments[property.Name] = property.Value.Clone();
            return new OllamaToolCall(name, arguments);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Builds system prompt from the currently available inputs.
    /// </summary>
    private static string BuildSystemPrompt(
        Conversation conversation,
        IReadOnlyCollection<ActivePlugin> plugins,
        IReadOnlyCollection<ActivePrompt> prompts,
        string agentName,
        string agentInstructions,
        DuoMode duoMode,
        string? workspaceRoot,
        string? projectContext,
        string? projectInstructions,
        string? registeredContext,
        bool computerUseEnabled)
    {
        var mode = conversation.Mode switch
        {
            HavenMode.Chat => "You are Haven Chat, a local private assistant.",
            HavenMode.Teach => "You are Haven Teach. Explain clearly, check factual teaching claims with enabled research tools, and adapt to the learner.",
            HavenMode.Do => "You are Haven Do. Complete tasks safely, request approval for risky or irreversible actions, and keep an audit trail.",
            HavenMode.Studio => "You are Haven Studio. Inspect, edit, test, observe failures, repair, explain, and validate local software projects before finishing.",
            _ => "You are Haven."
        };
        var builder = new StringBuilder(mode);
        builder.Append(" Active agent: ").Append(agentName).Append('.');
        if (!string.IsNullOrWhiteSpace(agentInstructions))
            builder.Append("\nAgent instructions:\n").Append(agentInstructions.Trim());
        if (duoMode == DuoMode.PingPong)
            builder.Append("\nDuo mode is Ping Pong. Take one clear turn, state what changed or what the other participant should do next, then hand over instead of silently completing both sides.");
        else if (duoMode == DuoMode.Collaborate)
            builder.Append("\nDuo mode is Collaborate. Treat the user as a live collaborator, make shared workspace changes explicit, call out assumptions, and leave concise review points for the next human turn.");
        else if (duoMode == DuoMode.Supervise)
            builder.Append("\nDuo mode is Supervise. The user does most of the work. Watch for mistakes, risky assumptions, spaghetti-code trends, and repeated tedious work; offer concise, timely suggestions and propose automation without taking over unless asked.");
        if (!string.IsNullOrWhiteSpace(projectContext))
            builder.Append("\nShared project context:\n").Append(projectContext.Trim());
        if (!string.IsNullOrWhiteSpace(projectInstructions))
            builder.Append("\nProject instructions and golden rules:\n").Append(projectInstructions.Trim());
        if (!string.IsNullOrWhiteSpace(registeredContext))
            builder.Append("\nRegistered conversation context and prior compact summaries:\n").Append(registeredContext.Trim());
        foreach (var plugin in plugins.Where(plugin => !string.IsNullOrWhiteSpace(plugin.Instructions)))
            builder.Append("\nPlugin @").Append(plugin.Name).Append(":\n").Append(plugin.Instructions.Trim());
        foreach (var prompt in prompts.Where(prompt => !string.IsNullOrWhiteSpace(prompt.Instructions)))
            builder.Append("\nPrompt >").Append(prompt.Name).Append(":\n").Append(prompt.Instructions.Trim());
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            builder.Append("\nYou are connected to real Haven workspace tools rooted at: ").Append(workspaceRoot)
                .Append("\nUse the tools instead of pretending to inspect or modify files. Inspect first, make the minimum necessary changes, examine each result, run relevant validation, and continue until complete or genuinely blocked. Never claim an action succeeded unless a tool result confirms it. Do not access paths outside the selected workspace.");
            if (conversation.Mode is HavenMode.Do or HavenMode.Studio)
                builder.Append("\nBefore a material edit, give a concise impact estimate (scope, risk, affected surfaces). During edits, keep steps and change counts explicit. After every edit, provide a short changelog. Explain errors in plain English and point to their likely cause. Gather only relevant logs and recent actions. Critical correctness or security faults may be fixed immediately; ask before unrelated cleanup, style-only rewrites, dependency modernisation, or scope expansion. Keep deliverables easy to find, with a desktop executable at the requested top level when packaging permits it.");
            if (conversation.Mode == HavenMode.Studio)
                builder.Append("\nUse project decisions as constraints and warn before reversing one. Convert rough requests into requirements, constraints, and acceptance checks before broad changes. Generate targeted tests from those checks. Before release or publish, assess changed files, dependencies, past failures, and test coverage, then run the highest-risk tests first. Recommend smart initial settings and existing features when they materially help, explain why once without nagging, and wait for approval before changing settings.");
        }
        if (computerUseEnabled)
        {
            builder.Append("\nComputer Use is active and controls the real Windows desktop. Complete multi-step desktop requests with tools rather than treating the whole sentence as an application name. Use computer_launch_app with only the exact app name. Every mutation tool includes a post-action inspection in its result, so use that verification to choose the next step; call computer_snapshot or computer_list_windows separately only when more state is needed or a verification failed. Bind every input action to an exact visible target window and stop if verification fails.");
        }
        builder.Append("\nWhen a short multiple-choice clarification is genuinely required, end with exactly one tag in this form: <haven-question>{\"question\":\"...\",\"options\":[\"First\",\"Second\"]}</haven-question>. Provide two or three mutually exclusive options and do not invent an Other option.");
        builder.Append("\nNever claim a tool or browser action happened unless a tool result confirms it.");
        return builder.ToString();
    }
}

/// <summary>
/// Represents chat stream event and keeps its related state and behavior together.
/// </summary>
public sealed record ChatStreamEvent(
    ChatStreamEventKind Kind,
    ChatMessage? Message = null,
    Guid? MessageId = null,
    string? Delta = null,
    string? Model = null,
    string? Agent = null,
    CapabilityPreflightResult? PreflightResult = null,
    ToolActivity? ToolActivity = null)
{
    /// <summary>
    /// Performs the user step owned by this component.
    /// </summary>
    public static ChatStreamEvent User(ChatMessage message) => new(ChatStreamEventKind.UserMessage, Message: message);
    /// <summary>
    /// Performs the assistant started step owned by this component.
    /// </summary>
    public static ChatStreamEvent AssistantStarted(Guid id, string model, string agent) => new(ChatStreamEventKind.AssistantStarted, MessageId: id, Model: model, Agent: agent);
    /// <summary>
    /// Performs the assistant delta step owned by this component.
    /// </summary>
    public static ChatStreamEvent AssistantDelta(Guid id, string delta) => new(ChatStreamEventKind.AssistantDelta, MessageId: id, Delta: delta);
    /// <summary>
    /// Performs the assistant completed step owned by this component.
    /// </summary>
    public static ChatStreamEvent AssistantCompleted(ChatMessage message) => new(ChatStreamEventKind.AssistantCompleted, Message: message);
    /// <summary>
    /// Performs the preflight step owned by this component.
    /// </summary>
    public static ChatStreamEvent Preflight(CapabilityPreflightResult result) => new(ChatStreamEventKind.PreflightFailed, PreflightResult: result);
    /// <summary>
    /// Performs the activity step owned by this component.
    /// </summary>
    public static ChatStreamEvent Activity(ToolActivity activity) => new(ChatStreamEventKind.ToolActivity, ToolActivity: activity);
}

/// <summary>
/// Lists the supported chat stream event kind values used to make state explicit and type-safe.
/// </summary>
public enum ChatStreamEventKind { UserMessage, AssistantStarted, AssistantDelta, AssistantCompleted, ToolActivity, PreflightFailed }
