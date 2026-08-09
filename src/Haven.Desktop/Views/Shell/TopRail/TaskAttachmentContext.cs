using Haven.Core;

namespace Haven.Desktop.Views.Shell.TopRail;

/// <summary>
/// Holds Apps and local files attached to one active input/task. It deliberately
/// owns no navigation behaviour: attaching context must never launch another App
/// or move the user to Chat.
/// </summary>
public sealed class TaskAttachmentContext
{
    private readonly List<string> _files = [];
    private readonly List<ModeDefinition> _apps = [];
    private readonly List<CapabilityDefinition> _capabilities = [];
    private readonly HashSet<Guid> _explicitAppIds = [];

    public IReadOnlyList<string> Files => _files;
    public IReadOnlyList<ModeDefinition> Apps => _apps;
    public IReadOnlyList<CapabilityDefinition> Capabilities => _capabilities;
    public bool IsEmpty => _files.Count == 0 && _apps.Count == 0 && _capabilities.Count == 0;

    public void AttachFiles(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            var normalized = Path.GetFullPath(path.Trim());
            if (_files.Contains(normalized, StringComparer.OrdinalIgnoreCase)) continue;
            _files.Add(normalized);
        }
    }

    public void AttachApp(ModeDefinition app)
    {
        ArgumentNullException.ThrowIfNull(app);
        _explicitAppIds.Add(app.Id);
        EnsureAppAttached(app);
    }

    /// <summary>
    /// Attaches a capability as task relevance only. This does not request,
    /// approve, or allow execution. App-owned capabilities also bring their
    /// owning App into the task context without turning it into an explicit App
    /// attachment.
    /// </summary>
    public void AttachCapability(CapabilityDefinition capability, ModeDefinition? ownerApp = null)
    {
        ArgumentNullException.ThrowIfNull(capability);
        if (!capability.IsEnabled || !capability.IsAttachable)
            throw new InvalidOperationException($"Capability '{capability.Name}' cannot be attached.");

        if (!capability.OwnerAppKey.Equals(CapabilityRegistryCatalog.GeneralOwner, StringComparison.OrdinalIgnoreCase))
        {
            if (ownerApp is null || !ownerApp.Key.Equals(capability.OwnerAppKey, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Capability '{capability.Name}' requires owning App '{capability.OwnerAppKey}'.", nameof(ownerApp));
            EnsureAppAttached(ownerApp);
        }

        if (_capabilities.Any(item => item.Id == capability.Id)) return;
        _capabilities.Add(capability);
    }

    public bool RemoveCapability(Guid capabilityId)
    {
        var capability = _capabilities.FirstOrDefault(item => item.Id == capabilityId);
        if (capability is null) return false;
        _capabilities.Remove(capability);

        if (!capability.OwnerAppKey.Equals(CapabilityRegistryCatalog.GeneralOwner, StringComparison.OrdinalIgnoreCase)
            && !_capabilities.Any(item => item.OwnerAppKey.Equals(capability.OwnerAppKey, StringComparison.OrdinalIgnoreCase)))
        {
            _apps.RemoveAll(app => app.Key.Equals(capability.OwnerAppKey, StringComparison.OrdinalIgnoreCase)
                                   && !_explicitAppIds.Contains(app.Id));
        }
        return true;
    }

    public bool IsCapabilityAttached(Guid capabilityId) =>
        _capabilities.Any(item => item.Id == capabilityId);

    public void AttachSnapshot(TaskAttachmentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        AttachFiles(snapshot.Files);

        foreach (var app in snapshot.Apps.Where(app => snapshot.ExplicitAppIds.Contains(app.Id)))
            AttachApp(app);
        foreach (var capability in snapshot.Capabilities)
        {
            var owner = snapshot.Apps.FirstOrDefault(app =>
                app.Key.Equals(capability.OwnerAppKey, StringComparison.OrdinalIgnoreCase));
            AttachCapability(capability, owner);
        }
    }

    public TaskAttachmentSnapshot TakeSnapshot()
    {
        var snapshot = new TaskAttachmentSnapshot(
            _files.ToArray(),
            _apps.ToArray(),
            _capabilities.ToArray(),
            _explicitAppIds.ToHashSet());
        _files.Clear();
        _apps.Clear();
        _capabilities.Clear();
        _explicitAppIds.Clear();
        return snapshot;
    }

    public string? BuildAppContext()
    {
        if (_apps.Count == 0) return null;
        return string.Join(
            "\n\n",
            _apps.Select(app =>
            {
                var context = $"Attached Haven app: {app.Name} ({app.Key}).\nPurpose: {app.Description}";
                if (!string.IsNullOrWhiteSpace(app.SystemPromptSuffix))
                    context += "\nApp instructions: " + app.SystemPromptSuffix.Trim();
                return context;
            }));
    }

    public string? BuildCapabilityContext()
    {
        if (_capabilities.Count == 0) return null;
        return "Attached capabilities are relevance signals only; they do not grant permission or require execution.\n" +
               string.Join("\n", _capabilities.Select(capability =>
                   $"- {capability.Name} ({capability.Key}), owned by {capability.OwnerAppKey}: {capability.Description}"));
    }

    private void EnsureAppAttached(ModeDefinition app)
    {
        if (_apps.Any(item => item.Id == app.Id)) return;
        _apps.Add(app);
    }
}

public sealed record TaskAttachmentSnapshot(
    IReadOnlyList<string> Files,
    IReadOnlyList<ModeDefinition> Apps,
    IReadOnlyList<CapabilityDefinition> Capabilities,
    IReadOnlySet<Guid> ExplicitAppIds);
