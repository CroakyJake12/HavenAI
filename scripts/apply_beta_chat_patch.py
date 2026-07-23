#!/usr/bin/env python3
from __future__ import annotations

import re
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> tuple[Path, str]:
    file = ROOT / path
    return file, file.read_text(encoding="utf-8")


def write(file: Path, text: str) -> None:
    file.write_text(text, encoding="utf-8", newline="\n")


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected one match, found {count}")
    return text.replace(old, new, 1)


def regex_once(text: str, pattern: str, replacement: str, label: str) -> str:
    result, count = re.subn(pattern, replacement, text, count=1, flags=re.DOTALL)
    if count != 1:
        raise RuntimeError(f"{label}: expected one match, found {count}")
    return result


def patch_chat_session() -> None:
    file, text = read("src/Haven.Application/ChatSessionService.cs")

    if "ChatModelInventoryCache? modelInventory = null" not in text:
        text = replace_once(
            text,
            "    ToolAvailabilityPlanner? toolAvailability = null)\n{",
            "    ToolAvailabilityPlanner? toolAvailability = null,\n"
            "    ChatModelInventoryCache? modelInventory = null)\n"
            "{\n"
            "    private readonly ChatModelInventoryCache _modelInventory =\n"
            "        modelInventory ?? new ChatModelInventoryCache(ollama);\n\n"
            "    public event Action<ChatExecutionSnapshot>? ExecutionChanged;\n\n"
            "    public ChatExecutionSnapshot? CurrentExecution { get; private set; }\n",
            "ChatSessionService constructor",
        )

    if "IReadOnlyCollection<ToolCapability>? explicitCapabilities = null)" not in text:
        text = regex_once(
            text,
            r"(public async IAsyncEnumerable<ChatStreamEvent> SendAsync\(.*?"
            r"PermissionMode browserPermission = PermissionMode\.FullAccess)\)",
            r"\1,\n"
            r"        IReadOnlyCollection<ToolCapability>? explicitCapabilities = null)",
            "SendAsync explicit capabilities",
        )

    send_start = text.index("    public async IAsyncEnumerable<ChatStreamEvent> SendAsync(")
    early_start = text.index(
        "        var computerPassCandidate = computerTools.CreatePass();",
        send_start,
    )
    system_start = text.index("        var system = BuildSystemPrompt(", early_start)

    new_early = r'''        ModelDescriptor etaModel = model;

        async Task<string?> EstimateEtaAsync(
            ChatEtaRequest request,
            CancellationToken token)
        {
            var activity = request.RecentActivity.Count == 0
                ? "No completed steps yet."
                : string.Join("; ", request.RecentActivity);
            var etaPrompt =
                $"Estimate the remaining time for this task. Current stage: {request.CurrentStatus}. " +
                $"Elapsed: {Math.Max(1, (int)request.Elapsed.TotalMinutes)} minutes. " +
                $"Recent activity: {activity}. " +
                "Return exactly one clear duration such as '8 minutes', '45 minutes', or '2 hours'. " +
                "Do not return a range, explanation, uncertainty, or refusal.";

            return await ollama.CompleteAsync(
                new OllamaChatRequest(
                    etaModel.Name,
                    [new OllamaMessage("user", etaPrompt)],
                    effort,
                    "Return one concrete remaining-time duration and nothing else."),
                token).ConfigureAwait(false);
        }

        await using var execution = new ChatExecutionTracker(
            ChatExecutionStage.Preparing,
            EstimateEtaAsync);

        void PublishExecution(ChatExecutionSnapshot snapshot)
        {
            CurrentExecution = snapshot;
            ExecutionChanged?.Invoke(snapshot);
        }

        execution.Changed += PublishExecution;

        var now = DateTimeOffset.UtcNow;
        var userMessage = new ChatMessage(
            Guid.NewGuid(),
            conversation.Id,
            MessageRole.User,
            prompt,
            null,
            null,
            null,
            now);

        // Yield before model discovery, context loading, or network preflight so the
        // user's message is visible immediately.
        yield return ChatStreamEvent.User(userMessage);

        if (!conversation.IsTemporary)
        {
            await conversations.UpsertConversationAsync(
                conversation with { UpdatedAt = now },
                cancellationToken).ConfigureAwait(false);
            await conversations.AddMessageAsync(
                userMessage,
                cancellationToken).ConfigureAwait(false);
        }

        execution.Update(ChatExecutionStage.LoadingModel, "Loading Model");
        var installed = await _modelInventory.GetAsync(
            forceRefresh: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        execution.Update(
            ChatExecutionStage.SelectingCapabilities,
            "Selecting Capabilities");

        var selectedCapabilities = explicitCapabilities is { Count: > 0 }
            ? explicitCapabilities.ToHashSet()
            : CapabilitiesFromActivePlugins(plugins);
        var capabilitySelection = ChatCapabilitySelection.Create(
            prompt,
            selectedCapabilities);
        var requiredCapabilities = capabilitySelection.Required.ToHashSet();

        if (images is { Count: > 0 })
        {
            requiredCapabilities.Add(ToolCapability.Vision);
        }

        var needsTools = NeedsToolRuntime(requiredCapabilities);
        if (needsTools)
        {
            requiredCapabilities.Add(ToolCapability.Tools);
            requiredCapabilities.Remove(ToolCapability.Streaming);
        }

        var turnModel = ChatModelFallbackSelector.Select(
                model,
                installed,
                requiredCapabilities)
            ?? model;
        etaModel = turnModel;

        var computerPassCandidate = computerTools.CreatePass();
        var availabilityPlan = CreateAvailabilityPlan(
            conversation.Mode,
            workspaceRoot,
            plugins,
            filePermission,
            commandPermission,
            browserPermission,
            computerPassCandidate.Definitions);

        var modelPlan = availabilityPlan.RestrictToModel(turnModel);
        var modelPlugins = FilterPluginsForTurn(
            modelPlan.FilterPlugins(plugins),
            requiredCapabilities);

        var check = preflight.Evaluate(
            turnModel,
            modelPlugins,
            images is { Count: > 0 },
            installed);

        if (!check.IsCompatible)
        {
            execution.Fail("Capability check failed", check.Message);
            execution.Changed -= PublishExecution;
            yield return ChatStreamEvent.Preflight(check);
            yield break;
        }

        var toolDefinitions = needsTools
            ? modelPlan.Definitions
                .Where(definition => IsToolSelectedForTurn(
                    modelPlan,
                    definition.Name,
                    requiredCapabilities))
                .ToArray()
            : [];

        var computerPass = toolDefinitions.Any(definition =>
                modelPlan.TryGetRuntime(definition.Name, out var runtime) &&
                runtime == ToolRuntimeKind.Computer)
            ? computerPassCandidate
            : null;
        var canUseTools = toolDefinitions.Length > 0;

        execution.Update(ChatExecutionStage.LoadingContext, "Loading Context");
        var history = await conversations.GetContextMessagesAsync(
            conversation.Id,
            cancellationToken).ConfigureAwait(false);

        var contextBudget = (int)Math.Clamp(
            (long)(generationOptions?.ContextLimit ?? 32768) * 4L,
            8_000L,
            131_072L);
        var contextMessages = ChatContextWindow.Build(history, contextBudget);
        var requestMessages = contextMessages
            .Where(message => message.Role is MessageRole.User or MessageRole.Assistant)
            .Select(message => new OllamaMessage(
                message.Role == MessageRole.User ? "user" : "assistant",
                message.Content))
            .ToList();

        if (requestMessages.Count == 0 ||
            requestMessages[^1].Role != "user" ||
            requestMessages[^1].Content != prompt)
        {
            requestMessages.Add(new OllamaMessage("user", prompt, images));
        }
        else if (images is { Count: > 0 })
        {
            requestMessages[^1] = new OllamaMessage("user", prompt, images);
        }

'''
    text = text[:early_start] + new_early + text[system_start:]

    helper_start = text.index(
        "    private static WorkspaceToolResult UnavailableToolResult(",
        system_start,
    )
    helpers = r'''    private static HashSet<ToolCapability> CapabilitiesFromActivePlugins(
        IReadOnlyCollection<ActivePlugin> plugins)
    {
        var result = new HashSet<ToolCapability>();
        foreach (var plugin in plugins)
        {
            switch (plugin.Name.ToLowerInvariant())
            {
                case "websearch":
                    result.Add(ToolCapability.WebSearch);
                    result.Add(ToolCapability.Browser);
                    break;
                case "browseruse":
                    result.Add(ToolCapability.Browser);
                    break;
                case "computeruse":
                    result.Add(ToolCapability.ComputerUse);
                    break;
                case "automate":
                case "macro":
                case "test":
                    result.Add(ToolCapability.Tools);
                    break;
            }
        }

        return result;
    }

    private static bool NeedsToolRuntime(
        IReadOnlySet<ToolCapability> capabilities) =>
        capabilities.Contains(ToolCapability.Tools) ||
        capabilities.Contains(ToolCapability.Browser) ||
        capabilities.Contains(ToolCapability.WebSearch) ||
        capabilities.Contains(ToolCapability.ComputerUse);

    private static IReadOnlyCollection<ActivePlugin> FilterPluginsForTurn(
        IReadOnlyCollection<ActivePlugin> plugins,
        IReadOnlySet<ToolCapability> capabilities)
    {
        if (NeedsToolRuntime(capabilities))
        {
            return plugins;
        }

        return plugins
            .Where(plugin => plugin.Name is not (
                "Automate" or
                "BrowserUse" or
                "ComputerUse" or
                "Macro" or
                "Test" or
                "WebSearch"))
            .ToArray();
    }

    private static bool IsToolSelectedForTurn(
        ToolAvailabilityPlan plan,
        string toolName,
        IReadOnlySet<ToolCapability> capabilities)
    {
        if (!plan.TryGetRuntime(toolName, out var runtime))
        {
            return false;
        }

        return runtime switch
        {
            ToolRuntimeKind.Browser =>
                capabilities.Contains(ToolCapability.Browser) ||
                capabilities.Contains(ToolCapability.WebSearch),
            ToolRuntimeKind.Computer =>
                capabilities.Contains(ToolCapability.ComputerUse),
            ToolRuntimeKind.Workspace or ToolRuntimeKind.Automation =>
                capabilities.Contains(ToolCapability.Tools),
            _ => false
        };
    }

    private static ChatExecutionStage StageForTool(
        string toolName,
        ToolRuntimeKind runtime)
    {
        if (toolName.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            return ChatExecutionStage.Testing;
        }

        if (toolName.Contains("command", StringComparison.OrdinalIgnoreCase))
        {
            return ChatExecutionStage.RunningCommand;
        }

        if (toolName.Contains("write", StringComparison.OrdinalIgnoreCase) ||
            toolName.Contains("replace", StringComparison.OrdinalIgnoreCase) ||
            toolName.Contains("change_set", StringComparison.OrdinalIgnoreCase))
        {
            return ChatExecutionStage.EditingFiles;
        }

        return runtime switch
        {
            ToolRuntimeKind.Browser => ChatExecutionStage.Browsing,
            ToolRuntimeKind.Workspace => ChatExecutionStage.InspectingCode,
            _ => ChatExecutionStage.RunningTool
        };
    }

    private static string StatusForTool(string toolName)
    {
        if (toolName.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            return "Testing";
        }

        if (toolName.Contains("command", StringComparison.OrdinalIgnoreCase))
        {
            return "Running Command";
        }

        if (toolName.Contains("write", StringComparison.OrdinalIgnoreCase) ||
            toolName.Contains("replace", StringComparison.OrdinalIgnoreCase) ||
            toolName.Contains("change_set", StringComparison.OrdinalIgnoreCase))
        {
            return "Editing Files";
        }

        if (toolName.StartsWith("browser_", StringComparison.Ordinal))
        {
            return "Using Browser";
        }

        if (toolName.StartsWith("workspace_", StringComparison.Ordinal) ||
            toolName is "read_file" or "list_files" or "search_files")
        {
            return "Inspecting Code";
        }

        return "Using Tool";
    }

    private static string DescribeTool(OllamaToolCall call)
    {
        if (call.Name.Contains("command", StringComparison.OrdinalIgnoreCase) &&
            call.Arguments.TryGetValue("command", out var command) &&
            command.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(command.GetString()))
        {
            var value = command.GetString()!
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();
            return "Running command: " +
                (value.Length <= 180 ? value : value[..180] + "…");
        }

        return "Started " + call.Name.Replace('_', ' ') + ".";
    }

'''
    text = text[:helper_start] + helpers + text[helper_start:]

    send_end = text.index(
        "    private ToolAvailabilityPlan CreateAvailabilityPlan(",
        send_start,
    )
    send_text = text[send_start:send_end]
    send_text = send_text.replace("model.Name", "turnModel.Name")
    send_text = send_text.replace(
        "TryBridgeToolCallAsync(ollama, model,",
        "TryBridgeToolCallAsync(ollama, turnModel,",
    )

    send_text = replace_once(
        send_text,
        "        yield return ChatStreamEvent.AssistantStarted(assistantId, turnModel.Name, agentName);",
        "        execution.Update(ChatExecutionStage.Thinking, \"Thinking\");\n"
        "        yield return ChatStreamEvent.AssistantStarted(assistantId, turnModel.Name, agentName);",
        "assistant start status",
    )

    send_text = replace_once(
        send_text,
        "        if (!canUseTools)\n        {\n            await foreach",
        "        if (!canUseTools)\n"
        "        {\n"
        "            var firstChunk = true;\n"
        "            await foreach",
        "stream first chunk state",
    )

    send_text = replace_once(
        send_text,
        "                buffer.Append(chunk);\n"
        "                yield return ChatStreamEvent.AssistantDelta(assistantId, chunk);",
        "                if (firstChunk)\n"
        "                {\n"
        "                    execution.Update(ChatExecutionStage.Generating, \"Writing Response\");\n"
        "                    firstChunk = false;\n"
        "                }\n\n"
        "                buffer.Append(chunk);\n"
        "                yield return ChatStreamEvent.AssistantDelta(assistantId, chunk);",
        "stream generating status",
    )

    execute_anchor = (
        "            async Task<WorkspaceToolResult> ExecuteToolAsync(OllamaToolCall call)\n"
        "            {\n"
        "                if (modelPlan.TryGetRuntime(call.Name, out var runtime))\n"
        "                {"
    )
    send_text = replace_once(
        send_text,
        execute_anchor,
        execute_anchor
        + "\n"
        "                    execution.Update(\n"
        "                        StageForTool(call.Name, runtime),\n"
        "                        StatusForTool(call.Name),\n"
        "                        DescribeTool(call));",
        "tool execution status",
    )

    send_text = replace_once(
        send_text,
        "        yield return ChatStreamEvent.AssistantCompleted(assistant);",
        "        execution.Complete();\n"
        "        execution.Changed -= PublishExecution;\n"
        "        yield return ChatStreamEvent.AssistantCompleted(assistant);",
        "execution completion",
    )

    text = text[:send_start] + send_text + text[send_end:]
    write(file, text)


def patch_chat_view_model() -> None:
    file, text = read("src/Haven.Desktop/ViewModels/ChatPageViewModel.cs")

    pattern = (
        r"var check = _preflight\.Evaluate\(SelectedModel, active, "
        r"prepared\.Images is \{ Count: > 0 \}, Models\); "
        r"if \(!check\.IsCompatible && _preferences\.AutoSwitchCompatibleModels "
        r"&& check\.SuggestedModel is not null\) \{.*?\} "
        r"var model = SelectedModel \?\? throw new InvalidOperationException"
        r"\(\"No compatible local model is selected\.\"\);"
    )
    replacement = (
        "var model = SelectedModel ?? throw new InvalidOperationException"
        "(\"No compatible local model is selected.\");"
    )
    text, count = re.subn(pattern, replacement, text, count=1, flags=re.DOTALL)
    if count != 1 and "AutoSwitchCompatibleModels" in text:
        raise RuntimeError(
            "ChatPageViewModel temporary fallback: expected one auto-switch block"
        )

    text = text.replace(
        'Status = $"{(IsAgentPluginActive ? SelectedAgent?.Name : "Default") ?? "Default"} is working…";',
        'Status = "Preparing…";',
        1,
    )

    write(file, text)


def patch_tracker_cleanup() -> None:
    file, text = read("src/Haven.Application/ChatExecutionTracker.cs")
    old = (
        "    public ValueTask DisposeAsync()\n"
        "    {\n"
        "        _lifetime.Cancel();"
    )
    if old in text and "if (!_finished) Cancel();" not in text:
        text = text.replace(
            old,
            "    public ValueTask DisposeAsync()\n"
            "    {\n"
            "        if (!_finished) Cancel();\n"
            "        _lifetime.Cancel();",
            1,
        )
    write(file, text)


def main() -> None:
    patch_chat_session()
    patch_chat_view_model()
    patch_tracker_cleanup()
    print("Applied New Haven beta chat orchestration patch.")


if __name__ == "__main__":
    main()
