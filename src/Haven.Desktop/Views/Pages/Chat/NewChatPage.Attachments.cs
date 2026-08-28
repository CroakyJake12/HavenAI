using Haven.Core;

namespace Haven.Desktop.Views.Pages.Chat;

public sealed partial class NewChatPage
{
    private IReadOnlyList<ChatAttachmentChip> BuildAttachmentChips()
    {
        var chips = new List<ChatAttachmentChip>();
        if (_activeAgent is { } agent)
            chips.Add(new ChatAttachmentChip($"agent:{agent.Id:D}", agent.Name, string.IsNullOrWhiteSpace(agent.IconKey) ? "agents" : agent.IconKey));
        chips.AddRange(_activeInstructions.Select(item =>
            new ChatAttachmentChip($"instruction:{item.Id:D}", item.Name, string.IsNullOrWhiteSpace(item.IconKey) ? "prompt" : item.IconKey)));
        if (_messageAttachments is not null)
        {
            chips.AddRange(_persistedAttachments.Select(item =>
                new ChatAttachmentChip($"attachment:{item.Id:D}", item.OriginalName, AttachmentIconForKind(item.Kind))));
        }
        else
        {
            chips.AddRange(_taskAttachments.Files.Select(path =>
                new ChatAttachmentChip("file:" + path, Path.GetFileName(path), AttachmentIconForFile(path))));
        }
        chips.AddRange(_taskAttachments.Capabilities.Select(item =>
            new ChatAttachmentChip($"capability:{item.Id:D}", item.Name, "plugin")));
        chips.AddRange(_taskAttachments.Apps.Select(item =>
            new ChatAttachmentChip($"app:{item.Id:D}", item.Name, string.IsNullOrWhiteSpace(item.IconKey) ? "all-modes" : item.IconKey)));
        if (BuildMultipleResponseChip() is { } multipleResponses) chips.Add(multipleResponses);
        return chips;
    }

    private async void OnAttachmentRemoveRequested(object? sender, string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        var changed = false;
        if (TryReadGuid(id, "attachment:", out var attachmentId) && _messageAttachments is not null)
        {
            try
            {
                await _messageAttachments.DeleteAsync(attachmentId, CancellationToken.None);
                changed = _persistedAttachments.RemoveAll(item => item.Id == attachmentId) > 0;
                if (_attachmentSourcePaths.Remove(attachmentId, out var sourcePath))
                    _taskAttachments.RemoveFile(sourcePath);
                await RefreshPersistedAttachmentPromptContextAsync();
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                _scene.SetStatus("The attachment could not be removed: " + exception.Message);
                return;
            }
        }
        else if (id.StartsWith("file:", StringComparison.Ordinal))
        {
            var path = id[5..];
            changed = _taskAttachments.RemoveFile(path);
            if (changed)
            {
                _attachedImages.RemoveAll(item => item.Equals(path, StringComparison.OrdinalIgnoreCase));
                _attachedContext.Remove(path);
            }
        }
        else if (TryReadGuid(id, "capability:", out var capabilityId))
            changed = _taskAttachments.RemoveCapability(capabilityId);
        else if (TryReadGuid(id, "app:", out var appId))
            changed = _taskAttachments.RemoveApp(appId);
        else if (TryReadGuid(id, "instruction:", out var instructionId))
            changed = _activeInstructions.RemoveAll(item => item.Id == instructionId) > 0;
        else if (TryReadGuid(id, "agent:", out var agentId) && _activeAgent?.Id == agentId)
        {
            _activeAgent = null;
            changed = true;
        }
        else if (id.Equals("multiple-responses", StringComparison.Ordinal))
            changed = ClearMultipleResponses();

        if (!changed) return;
        RefreshAttachmentStatus();
        RefreshResponseControls();
        _scene.SetStatus("Attachment removed from this chat.");
        FocusComposer();
    }

    private static bool TryReadGuid(string value, string prefix, out Guid id)
    {
        id = Guid.Empty;
        return value.StartsWith(prefix, StringComparison.Ordinal)
               && Guid.TryParse(value[prefix.Length..], out id);
    }

    private static string AttachmentIconForKind(MessageAttachmentKind kind) => kind switch
    {
        MessageAttachmentKind.Image => "image",
        MessageAttachmentKind.Video => "play",
        MessageAttachmentKind.Audio => "mic",
        _ => "file"
    };

    private static string AttachmentIconForFile(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" => "image",
            ".mp4" or ".mov" or ".m4v" or ".webm" or ".avi" => "play",
            ".mp3" or ".wav" or ".m4a" or ".aac" or ".flac" or ".ogg" => "mic",
            _ => "file"
        };
    }

    private IReadOnlyCollection<ToolCapability> ExplicitToolCapabilitiesForCurrentChat()
    {
        IEnumerable<CapabilityDefinition> attached = EffectiveChatActionMode switch
        {
            ChatActionMode.JustChat => [],
            ChatActionMode.AllowBasicActions => _taskAttachments.Capabilities.Where(item =>
                item.RiskClass is CapabilityRiskClass.ReadOnly or CapabilityRiskClass.Low),
            _ => _taskAttachments.Capabilities
        };

        var result = new HashSet<ToolCapability>();
        foreach (var capability in attached)
        {
            if (ExternalConnectionNaming.IsConnectionCapability(capability.Key))
            {
                result.Add(ToolCapability.Tools);
                continue;
            }

            switch (capability.Key)
            {
                case "web-search":
                    result.Add(ToolCapability.WebSearch);
                    result.Add(ToolCapability.Browser);
                    break;
                case "browser-use":
                    result.Add(ToolCapability.Browser);
                    break;
                case "computer-device-use":
                case "open-control-app":
                    result.Add(ToolCapability.ComputerUse);
                    break;
                case "create-automation":
                case "run-task":
                case "edit-task":
                case "run-command":
                case "run-script":
                case "powershell":
                case "read-file":
                case "write-file":
                case "run-tests":
                    result.Add(ToolCapability.Tools);
                    break;
            }
        }
        return result;
    }
}
