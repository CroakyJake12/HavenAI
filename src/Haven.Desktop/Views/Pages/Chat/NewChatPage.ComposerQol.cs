using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Shell.TopRail;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Pages.Chat;

public sealed partial class NewChatPage
{
    private readonly List<string> _multipleResponseModels = [];
    private readonly Dictionary<string, string> _modelDisplayNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _queuedInstructions = new();
    private bool _attachInvocationActive;
    private int _attachInvocationStart = -1;
    private int _attachInvocationEnd = -1;

    private void ConfigureChatQolInteractions()
    {
        _scene.ConfigureComposerQol();
        _scene.Instruction.TextChanged += OnComposerTextChangedForAttach;
        _scene.Instruction.Invalidated += OnComposerInputInvalidatedForAttach;
        _scene.AttachmentRemoveRequested += OnAttachmentRemoveRequested;
        _scene.MultipleResponsesRequested += async (_, _) => await ShowMultipleResponsesSelectorAsync();
        _scene.MultipleResponseModelToggled += OnMultipleResponseModelToggled;
    }

    private void OnComposerTextChangedForAttach(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(UpdateAttachSearchFromComposer, DispatcherPriority.Background);

    private void OnComposerInputInvalidatedForAttach(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(UpdateAttachSearchFromComposer, DispatcherPriority.Background);

    private void UpdateAttachSearchFromComposer()
    {
        if (_disposed) return;
        var text = _scene.Instruction.Text;
        var caret = _scene.Instruction.CaretIndex;
        if (!TryGetAttachInvocation(text, caret, out var start, out var query))
        {
            if (_attachInvocationActive) _scene.HideAddMenu();
            _attachInvocationActive = false;
            _attachInvocationStart = _attachInvocationEnd = -1;
            return;
        }

        _attachInvocationActive = true;
        _attachInvocationStart = start;
        _attachInvocationEnd = caret;
        _scene.ShowAttachSearch(query);
    }

    internal static bool TryGetAttachInvocation(string text, int caret, out int start, out string query)
    {
        start = -1;
        query = string.Empty;
        if (string.IsNullOrEmpty(text)) return false;
        caret = Math.Clamp(caret, 0, text.Length);
        if (caret == 0) return false;
        var at = text.LastIndexOf('@', caret - 1);
        if (at < 0) return false;
        if (at > 0 && !IsAttachBoundary(text[at - 1])) return false;
        var segment = text[(at + 1)..caret];
        if (segment.Any(char.IsWhiteSpace) || segment.Contains('@')) return false;
        start = at;
        query = segment;
        return true;
    }

    private static bool IsAttachBoundary(char value) =>
        char.IsWhiteSpace(value) || value is '(' or '[' or '{' or ',' or ';' or ':';

    private void ConsumeAttachInvocation()
    {
        if (!_attachInvocationActive || _attachInvocationStart < 0) return;
        var text = _scene.Instruction.Text;
        var end = Math.Clamp(_attachInvocationEnd, _attachInvocationStart + 1, text.Length);
        var updated = text.Remove(_attachInvocationStart, end - _attachInvocationStart);
        _scene.Instruction.Text = updated;
        _scene.Instruction.SetSelection(_attachInvocationStart, _attachInvocationStart);
        _attachInvocationActive = false;
        _attachInvocationStart = _attachInvocationEnd = -1;
    }

    private void OnSceneAddActionSelected(AddMenu.AddMenuAction action)
    {
        if (_attachInvocationActive) ConsumeAttachInvocation();
        if (action == AddMenu.AddMenuAction.MultipleResponses)
        {
            _ = ShowMultipleResponsesSelectorAsync();
            return;
        }
        AddActionSelected?.Invoke(this, action);
        FocusComposer();
    }

    private void OnSceneCatalogItemSelected(AddMenuSelection selection)
    {
        if (_attachInvocationActive) ConsumeAttachInvocation();
        ApplyAddSelection(selection);
        AddCatalogItemSelected?.Invoke(this, selection);
        RefreshInlineAttachmentChips();
        FocusComposer();
    }

    private void RefreshInlineAttachmentChips()
    {
        var chips = new List<ChatAttachmentChip>();
        if (_activeAgent is { } agent)
            chips.Add(new ChatAttachmentChip("agent:" + agent.Id, agent.Name, "agents"));
        foreach (var instruction in _activeInstructions)
            chips.Add(new ChatAttachmentChip("instruction:" + instruction.Id, instruction.Name, instruction.IconKey));
        foreach (var capability in _taskAttachments.Capabilities)
            chips.Add(new ChatAttachmentChip("capability:" + capability.Id, capability.Name, capability.IconKey));
        foreach (var app in _taskAttachments.Apps)
            chips.Add(new ChatAttachmentChip("app:" + app.Id, app.Name, "rocket"));
        foreach (var path in _taskAttachments.Files)
            chips.Add(new ChatAttachmentChip("file:" + path, Path.GetFileName(path), FileChipIcon(path)));
        if (_multipleResponseModels.Count > 0)
        {
            var names = _multipleResponseModels.Select(ResolveModelDisplayName).ToArray();
            var label = names.Length <= 3
                ? "Multiple Responses: " + string.Join(", ", names)
                : "Multiple Responses: " + string.Join(", ", names.Take(2)) + $" +{names.Length - 2}";
            chips.Add(new ChatAttachmentChip("multiple-responses", label, "agents", true));
        }
        _scene.SetAttachmentChips(chips);
    }

    private static string FileChipIcon(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp" or ".svg") return "image";
        if (extension is ".mp4" or ".mov" or ".mkv" or ".webm" or ".avi") return "play";
        if (extension is ".mp3" or ".wav" or ".m4a" or ".aac" or ".flac" or ".ogg") return "mic";
        return "paperclip";
    }

    private void OnAttachmentRemoveRequested(object? sender, string key)
    {
        if (key == "multiple-responses")
        {
            _multipleResponseModels.Clear();
            RefreshInlineAttachmentChips();
            return;
        }
        if (key.StartsWith("file:", StringComparison.Ordinal))
        {
            var path = key[5..];
            _taskAttachments.RemoveFile(path);
            _attachedImages.RemoveAll(item => item.Equals(path, StringComparison.OrdinalIgnoreCase));
            _attachedContext.Remove(path);
        }
        else if (key.StartsWith("capability:", StringComparison.Ordinal) && Guid.TryParse(key[11..], out var capabilityId))
            _taskAttachments.RemoveCapability(capabilityId);
        else if (key.StartsWith("app:", StringComparison.Ordinal) && Guid.TryParse(key[4..], out var appId))
            _taskAttachments.RemoveApp(appId);
        else if (key.StartsWith("instruction:", StringComparison.Ordinal) && Guid.TryParse(key[12..], out var promptId))
            _activeInstructions.RemoveAll(item => item.Id == promptId);
        else if (key.StartsWith("agent:", StringComparison.Ordinal) && Guid.TryParse(key[6..], out var agentId) && _activeAgent?.Id == agentId)
            _activeAgent = null;
        RefreshInlineAttachmentChips();
        RefreshResponseControls();
        FocusComposer();
    }

    private async Task ShowMultipleResponsesSelectorAsync()
    {
        try
        {
            var models = await _ollama.GetModelsAsync(CancellationToken.None);
            var names = models.Select(model => model.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (names.Length == 0)
            {
                _scene.SetStatus("No models are currently available for Multiple Responses.");
                return;
            }
            await CacheModelDisplayNamesAsync(models);
            _scene.ShowMultipleResponseChoices(names, _multipleResponseModels);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException)
        {
            _scene.SetStatus("Models could not be listed: " + exception.Message);
        }
    }

    private void OnMultipleResponseModelToggled(object? sender, string modelName)
    {
        var existing = _multipleResponseModels.FindIndex(item => item.Equals(modelName, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0) _multipleResponseModels.RemoveAt(existing);
        else _multipleResponseModels.Add(modelName);
        RefreshInlineAttachmentChips();
        _ = ShowMultipleResponsesSelectorAsync();
        FocusComposer();
    }

    private async Task CacheModelDisplayNamesAsync(IEnumerable<ModelDescriptor> models)
    {
        var personalities = App.Services?.GetService<ModelPersonalityService>();
        foreach (var model in models)
        {
            var nickname = personalities is null ? null : await personalities.ResolveNicknameAsync(model.Name, CancellationToken.None);
            _modelDisplayNames[model.Name] = string.IsNullOrWhiteSpace(nickname) ? model.Name : nickname;
        }
    }

    private string ResolveModelDisplayName(string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return "Model";
        return _modelDisplayNames.TryGetValue(modelName, out var display) ? display : modelName;
    }

    private bool QueueInstructionWhileSending(string instruction)
    {
        if (!_isSending || string.IsNullOrWhiteSpace(instruction)) return false;
        _queuedInstructions.Enqueue(instruction.Trim());
        _scene.Instruction.Text = string.Empty;
        _scene.SetStatus(_queuedInstructions.Count == 1 ? "Message queued." : $"{_queuedInstructions.Count} messages queued.");
        FocusComposer();
        return true;
    }

    private void TrySubmitQueuedInstruction()
    {
        if (_isSending || _queuedInstructions.Count == 0) return;
        var next = _queuedInstructions.Dequeue();
        SetDraft(next);
        _ = SubmitCurrentInstructionAsync();
    }

    private async Task RunMultipleResponsesAsync(string instruction)
    {
        if (_multipleResponseModels.Count < 2)
        {
            _scene.SetStatus("Choose at least two models for Multiple Responses.");
            await ShowMultipleResponsesSelectorAsync();
            return;
        }

        _pendingInstruction = null;
        _redoMessages.Clear();
        _scene.Instruction.Text = string.Empty;
        _bus.Fire("Chat.Composer.Send.Click");
        _isSending = true;
        _sendStartTick = Environment.TickCount64;
        _sendProgressTimer.Start();
        _sendCancellation = new CancellationTokenSource();
        RefreshVisualState();
        FocusComposer();

        var now = DateTimeOffset.UtcNow;
        if (_messages.Count == 0)
        {
            var title = instruction.Length > 56 ? instruction[..53] + "…" : instruction;
            _conversation = _conversation with { Title = title, UpdatedAt = now };
        }
        UpsertMessage(new ChatMessage(Guid.NewGuid(), _conversation.Id, MessageRole.User, instruction, null, null, null, now));
        RefreshMessages();

        try
        {
            var token = _sendCancellation.Token;
            var effort = _effortOverride ?? _preferences.DefaultEffort;
            var images = _attachedImages.Count == 0 ? null : _attachedImages.ToArray();
            var tasks = _multipleResponseModels.Select(async modelName =>
            {
                try
                {
                    var response = await _ollama.CompleteAsync(
                        new OllamaChatRequest(modelName, [new OllamaMessage("user", instruction, images)], effort,
                            "Respond directly to the user. This is one response in Haven Multiple Responses; do not refer to other models unless the user asks.",
                            Options: _preferences.GenerationOptions),
                        token);
                    return (Model: modelName, Content: response, Error: (string?)null);
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
                {
                    return (Model: modelName, Content: string.Empty, Error: (string?)ex.Message);
                }
            }).ToArray();

            var results = await Task.WhenAll(tasks);
            foreach (var result in results)
            {
                var content = result.Error is null ? result.Content : "This response failed: " + result.Error;
                var message = new ChatMessage(Guid.NewGuid(), _conversation.Id, MessageRole.Assistant, content,
                    ResolveModelDisplayName(result.Model), result.Model, null, DateTimeOffset.UtcNow);
                UpsertMessage(message);
                RefreshMessage(message);
            }
            _scene.SetStatus("Multiple Responses complete.");
        }
        catch (OperationCanceledException)
        {
            _scene.SetStatus("Multiple Responses stopped.");
        }
        finally
        {
            _sendProgressTimer.Stop();
            _sendCancellation?.Dispose();
            _sendCancellation = null;
            _isSending = false;
            RefreshVisualState();
            FocusComposer();
            TrySubmitQueuedInstruction();
        }
    }
}
