namespace Haven.Desktop.Views.Pages.Chat;

public sealed partial class NewChatPage
{
    private readonly Queue<string> _queuedInstructions = new();
    private bool _submissionStarting;

    private void QueueInstruction(string instruction)
    {
        if (string.IsNullOrWhiteSpace(instruction)) return;
        _queuedInstructions.Enqueue(instruction.Trim());
        _scene.Instruction.Text = string.Empty;
        _activeMention = null;
        _scene.HideAddMenu();
        _scene.SetStatus($"Queued next message ({_queuedInstructions.Count}).");
    }

    private void TrySubmitQueuedInstruction()
    {
        if (_disposed || _isSending || _submissionStarting || _selectedModel is null || _queuedInstructions.Count == 0) return;
        var instruction = _queuedInstructions.Dequeue();
        _scene.Instruction.Text = instruction;
        _scene.Instruction.PlaceCaretAtEnd();
        _ = SubmitCurrentInstructionAsync();
    }

    private void RequeueInstructionAtFront(string instruction)
    {
        if (string.IsNullOrWhiteSpace(instruction)) return;
        var remaining = _queuedInstructions.ToArray();
        _queuedInstructions.Clear();
        _queuedInstructions.Enqueue(instruction.Trim());
        foreach (var queued in remaining) _queuedInstructions.Enqueue(queued);
    }

    private bool CurrentAttemptWasAcknowledged(int initialMessageCount, string instruction) =>
        _messages.Skip(initialMessageCount).Any(message =>
            message.Role == Haven.Core.MessageRole.User &&
            message.Content.Equals(instruction, StringComparison.Ordinal));

    private async Task PreserveUnacknowledgedInstructionAsync(string instruction)
    {
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (string.IsNullOrWhiteSpace(_scene.Instruction.Text))
            {
                _scene.Instruction.Text = instruction;
                _scene.Instruction.PlaceCaretAtEnd();
            }
            else
            {
                RequeueInstructionAtFront(instruction);
            }
            FocusComposer();
        });
    }
}
