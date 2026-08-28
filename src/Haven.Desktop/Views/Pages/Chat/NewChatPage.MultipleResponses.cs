using System.Text.Json;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Pages.Chat;

public sealed partial class NewChatPage
{
    private readonly List<string> _multipleResponseModels = [];
    private IReadOnlyList<string> _multipleResponseAvailableModels = [];
    private MultipleResponseService? _multipleResponses;

    private bool MultipleResponsesActive =>
        _multipleResponses is not null && _multipleResponseModels.Count >= 2;

    private void WireMultipleResponses()
    {
        if (App.Services?.GetService<MultipleResponseService>() is not { } service) return;
        _multipleResponses = service;
        _scene.MultipleResponsesRequested += OnMultipleResponsesRequested;
        _scene.MultipleResponseModelToggled += OnMultipleResponseModelToggled;
        _scene.AttachmentInvoked += OnAttachmentInvoked;
    }

    private void UnwireMultipleResponses()
    {
        _scene.MultipleResponsesRequested -= OnMultipleResponsesRequested;
        _scene.MultipleResponseModelToggled -= OnMultipleResponseModelToggled;
        _scene.AttachmentInvoked -= OnAttachmentInvoked;
    }

    private async void OnMultipleResponsesRequested(object? sender, EventArgs e)
    {
        if (_activeMention is not null) ConsumeActiveMention();
        await ShowMultipleResponsesSelectorAsync();
    }

    private async void OnAttachmentInvoked(object? sender, string id)
    {
        if (!id.Equals("multiple-responses", StringComparison.Ordinal)) return;
        await ShowMultipleResponsesSelectorAsync();
    }

    private async Task ShowMultipleResponsesSelectorAsync()
    {
        try
        {
            var models = await _ollama.GetModelsAsync(CancellationToken.None);
            _multipleResponseAvailableModels = models
                .Select(model => model.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            _multipleResponseModels.RemoveAll(model =>
                !_multipleResponseAvailableModels.Contains(model, StringComparer.OrdinalIgnoreCase));
            if (_multipleResponseModels.Count == 0
                && _selectedModel is { } selected
                && _multipleResponseAvailableModels.Contains(selected.Name, StringComparer.OrdinalIgnoreCase))
                _multipleResponseModels.Add(selected.Name);

            _scene.ShowMultipleResponseModels(_multipleResponseAvailableModels, _multipleResponseModels);
            if (_multipleResponseAvailableModels.Count < 2)
                _scene.SetStatus("Multiple Responses needs at least two installed models.");
            else if (_multipleResponseModels.Count < 2)
                _scene.SetStatus("Select at least two models for Multiple Responses.");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException)
        {
            _scene.SetStatus("Installed models could not be listed: " + exception.Message);
        }
    }

    private void OnMultipleResponseModelToggled(object? sender, string modelKey)
    {
        if (string.IsNullOrWhiteSpace(modelKey)) return;
        var existing = _multipleResponseModels.FindIndex(item =>
            item.Equals(modelKey, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0)
            _multipleResponseModels.RemoveAt(existing);
        else
            _multipleResponseModels.Add(modelKey);

        RefreshAttachmentStatus();
        _scene.ShowMultipleResponseModels(_multipleResponseAvailableModels, _multipleResponseModels);
        _scene.SetStatus(_multipleResponseModels.Count switch
        {
            0 => "Multiple Responses is not attached.",
            1 => "Select at least one more model for Multiple Responses.",
            _ => $"Multiple Responses attached with {_multipleResponseModels.Count} models."
        });
    }

    private ChatAttachmentChip? BuildMultipleResponseChip()
    {
        if (_multipleResponseModels.Count < 2) return null;
        var first = string.Join(", ", _multipleResponseModels.Take(2));
        var remainder = _multipleResponseModels.Count > 2 ? $" +{_multipleResponseModels.Count - 2}" : string.Empty;
        return new ChatAttachmentChip(
            "multiple-responses",
            $"Multiple Responses: {first}{remainder}",
            "copy",
            Invokable: true);
    }

    private bool ClearMultipleResponses()
    {
        if (_multipleResponseModels.Count == 0) return false;
        _multipleResponseModels.Clear();
        return true;
    }

    private async Task RunMultipleResponsesAsync(string instruction)
    {
        if (_multipleResponses is null || _multipleResponseModels.Count < 2) return;
        var models = _multipleResponseModels.ToArray();
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

        try
        {
            var now = DateTimeOffset.UtcNow;
            if (_messages.Count == 0)
            {
                var title = instruction.Length > 56 ? instruction[..53] + "…" : instruction;
                _conversation = _conversation with { Title = title, UpdatedAt = now };
                if (!_conversation.IsTemporary)
                    await _conversations.UpsertConversationAsync(_conversation, _sendCancellation.Token);
            }

            var userMessage = new ChatMessage(
                Guid.NewGuid(), _conversation.Id, MessageRole.User, instruction,
                null, null, null, now);
            UpsertMessage(userMessage);
            if (!_conversation.IsTemporary)
                await _conversations.AddMessageAsync(userMessage, _sendCancellation.Token);
            RefreshMessages();

            var run = await _multipleResponses.RunAsync(
                instruction,
                models,
                _effortOverride ?? _preferences.DefaultEffort,
                _sendCancellation.Token);
            var personalities = App.Services?.GetService<ModelPersonalityService>();
            var succeeded = 0;
            var persistenceFailures = 0;
            foreach (var response in run.Responses)
            {
                string? nickname = null;
                if (personalities is not null)
                {
                    try
                    {
                        nickname = await personalities.ResolveNicknameAsync(response.ModelKey, _sendCancellation.Token);
                    }
                    catch (Exception exception) when (exception is IOException or InvalidOperationException)
                    {
                        // Nickname metadata is optional; the actual model key still identifies the response.
                    }
                }

                var metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["multipleResponses"] = true,
                    ["multipleResponseSucceeded"] = response.Succeeded
                };
                if (!string.IsNullOrWhiteSpace(nickname)) metadata["modelNickname"] = nickname;
                var content = response.Succeeded
                    ? response.Content
                    : $"This response failed: {response.Error}";
                if (response.Succeeded) succeeded++;
                var message = new ChatMessage(
                    Guid.NewGuid(),
                    _conversation.Id,
                    MessageRole.Assistant,
                    content,
                    null,
                    response.ModelKey,
                    JsonSerializer.Serialize(metadata),
                    DateTimeOffset.UtcNow);
                UpsertMessage(message);
                RefreshMessage(message);
                ScrollToEndIfFollowing(true);

                if (_conversation.IsTemporary) continue;
                try
                {
                    await _conversations.AddMessageAsync(message, _sendCancellation.Token);
                }
                catch (Exception exception) when (exception is IOException or InvalidOperationException)
                {
                    persistenceFailures++;
                }
            }

            var failed = run.Responses.Count - succeeded;
            var persistenceNote = persistenceFailures == 0
                ? string.Empty
                : $" {persistenceFailures} response(s) could not be persisted.";
            await SetStatusAsync((failed == 0
                ? $"Multiple Responses completed with {succeeded} models."
                : $"Multiple Responses completed: {succeeded} succeeded, {failed} failed.") + persistenceNote);
        }
        catch (OperationCanceledException)
        {
            await SetStatusAsync("Multiple Responses stopped.");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException)
        {
            await SetStatusAsync("Haven could not complete Multiple Responses: " + exception.Message);
        }
        finally
        {
            _sendProgressTimer.Stop();
            _sendCancellation?.Dispose();
            _sendCancellation = null;
            _isSending = false;
            await RefreshSafetyStateAsync();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                RefreshVisualState();
                TrySubmitPendingInstruction();
                TrySubmitQueuedInstruction();
                FocusComposer();
            });
        }
    }
}
