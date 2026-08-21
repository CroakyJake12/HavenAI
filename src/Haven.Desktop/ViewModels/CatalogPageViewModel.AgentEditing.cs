using Haven.Core;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Keeps custom-Agent editing on the existing catalogue view-model without introducing a
/// second persistence or runtime owner. Hidden runtime metadata is preserved from the
/// persisted definition while the user-editable fields are updated in place.
/// </summary>
public sealed partial class CatalogPageViewModel
{
    private AgentDefinition? _editingAgent;

    public bool IsEditingAgent => _editingAgent is not null;

    internal async Task<bool> BeginAgentEditAsync(CatalogCardViewModel? item)
    {
        if (item is null || Kind != CatalogPageKind.Agents || item.IsBuiltIn) return false;

        var source = (await _catalog.GetAgentsAsync(CancellationToken.None))
            .FirstOrDefault(agent => agent.Id == item.Id);
        if (source is null || source.IsBuiltIn) return false;

        _editingAgent = source;
        BuilderPrompt = source.DetectionRules;
        NewName = source.Name;
        NewDescription = source.Description;
        NewInstructions = source.Instructions;
        NewModel = source.PreferredModel;
        IsCreating = true;
        Status = $"Editing {source.Name}. Save changes when you are ready.";
        RaisePropertyChanged(nameof(IsEditingAgent));
        return true;
    }

    internal async Task<bool> SaveAgentEditsAsync()
    {
        if (_editingAgent is null || Kind != CatalogPageKind.Agents || !CanCreate()) return false;

        var source = _editingAgent;
        var updatedName = NewName.Trim();
        await _catalog.UpsertAgentAsync(source with
        {
            Name = updatedName,
            Description = NewDescription.Trim(),
            Instructions = NewInstructions.Trim(),
            PreferredModel = string.IsNullOrWhiteSpace(NewModel) ? "default" : NewModel.Trim(),
            DetectionRules = BuilderPrompt.Trim(),
            UpdatedAt = DateTimeOffset.UtcNow
        }, CancellationToken.None);

        _editingAgent = null;
        ClearAgentEditDraft();
        IsCreating = false;
        RaisePropertyChanged(nameof(IsEditingAgent));
        await RefreshAsync();
        Status = $"Updated {updatedName}. Changes are ready to use in chat.";
        return true;
    }

    internal void CancelAgentEdit()
    {
        if (_editingAgent is null) return;

        var name = _editingAgent.Name;
        _editingAgent = null;
        ClearAgentEditDraft();
        IsCreating = false;
        RaisePropertyChanged(nameof(IsEditingAgent));
        Status = $"Stopped editing {name}.";
    }

    private void ClearAgentEditDraft()
    {
        BuilderPrompt = string.Empty;
        NewName = string.Empty;
        NewDescription = string.Empty;
        NewInstructions = string.Empty;
        NewModel = string.Empty;
    }
}
