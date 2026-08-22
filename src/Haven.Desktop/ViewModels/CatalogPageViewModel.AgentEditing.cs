using System.Text.Json;
using System.Text.Json.Nodes;
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
        NewCapabilities = ReadAgentCapabilities(source.PermissionsJson);
        NewPermissionProfile = ReadAgentString(source.PermissionsJson, "permissionProfileRef");
        NewSandboxProfile = ReadAgentString(source.PermissionsJson, "sandboxProfileRef");
        NewKnowledgeResources = ReadAgentList(source.PermissionsJson, "knowledgeResources");
        NewMemoryMode = ReadAgentString(source.PermissionsJson, "memoryMode", "session");
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
            PermissionsJson = MergeAgentConfiguration(source.PermissionsJson, NewCapabilities, NewPermissionProfile, NewSandboxProfile, NewKnowledgeResources, NewMemoryMode),
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


    internal async Task SetAgentEnabledAsync(CatalogCardViewModel? item, bool enabled)
    {
        if (item is null || Kind != CatalogPageKind.Agents) return;
        await _catalog.SetAgentEnabledAsync(item.Id, enabled, CancellationToken.None);
        await RefreshAsync();
        Status = enabled ? $"Enabled {item.Name}." : $"Disabled {item.Name}.";
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
        NewCapabilities = string.Empty;
        NewPermissionProfile = string.Empty;
        NewSandboxProfile = string.Empty;
        NewKnowledgeResources = string.Empty;
        NewMemoryMode = "session";
    }

    internal static string ReadAgentCapabilities(string? json)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("capabilities", out var capabilities) && capabilities.ValueKind == JsonValueKind.Array)
                foreach (var item in capabilities.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString())) keys.Add(item.GetString()!);
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("webSearch", out var web) && web.ValueKind == JsonValueKind.True) keys.Add("web-search");
        }
        catch (JsonException) { }
        return string.Join(", ", keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase));
    }

    internal static string MergeAgentCapabilities(string? json, string? commaSeparated) =>
        MergeAgentConfiguration(json, commaSeparated, null, null, null, null);

    internal static string ReadAgentString(string? json, string key, string fallback = "")
    {
        try
        {
            using var d = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (d.RootElement.ValueKind == JsonValueKind.Object && d.RootElement.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String) return v.GetString() ?? fallback;
        }
        catch (JsonException) { }
        return fallback;
    }

    internal static string ReadAgentList(string? json, string key)
    {
        try
        {
            using var d = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (d.RootElement.ValueKind == JsonValueKind.Object && d.RootElement.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array)
                return string.Join(", ", v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)));
        }
        catch (JsonException) { }
        return string.Empty;
    }

    internal static string MergeAgentConfiguration(string? json, string? capabilities, string? profileRef, string? sandboxRef, string? resources, string? memoryMode)
    {
        JsonObject root;
        try { root = JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json) as JsonObject ?? new JsonObject(); } catch (JsonException) { root = new JsonObject(); }
        static JsonArray Csv(string? v) { var a = new JsonArray(); foreach (var x in (v ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase)) a.Add(x); return a; }
        static void Opt(JsonObject r, string k, string? v) { if (string.IsNullOrWhiteSpace(v)) r.Remove(k); else r[k] = v.Trim(); }
        root["capabilities"] = Csv(capabilities); Opt(root, "permissionProfileRef", profileRef); Opt(root, "sandboxProfileRef", sandboxRef); root["knowledgeResources"] = Csv(resources); root["memoryMode"] = string.IsNullOrWhiteSpace(memoryMode) ? "session" : memoryMode.Trim(); return root.ToJsonString();
    }
}
