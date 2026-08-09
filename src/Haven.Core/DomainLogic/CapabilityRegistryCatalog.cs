namespace Haven.Core;

/// <summary>First-party capabilities mapped to concrete current implementation routes.</summary>
public static class CapabilityRegistryCatalog
{
    public const string GeneralOwner = "general";

    public static IReadOnlyList<CapabilityDefinition> BuiltIns { get; } =
    [
        BuiltIn("duo", "Duo", "Coordinate two agents in the current task.", GeneralOwner, "duo", "duo.configure", "[\"configure\",\"coordinate\"]", CapabilityPlatform.All, CapabilityRiskClass.Low),
        BuiltIn("attach-thread", "Attach Thread", "Attach another Haven thread as selective, provenance-preserving context.", GeneralOwner, "chat", "thread.attach", "[\"attach\",\"retrieve-context\"]", CapabilityPlatform.All, CapabilityRiskClass.ReadOnly),
        BuiltIn("web-search", "Web Search", "Research current sources through Haven Browse.", "browse", "web-search", "browser.search", "[\"search\",\"read-source\"]", CapabilityPlatform.All, CapabilityRiskClass.ReadOnly, CapabilityAvailability.PermissionRequired, provider: "haven.browser"),
        BuiltIn("browser-use", "Browser Use", "Navigate and interact through Haven's isolated browser.", "tasks", "browser-use", "browser.interactive", "[\"navigate\",\"inspect\",\"interact\"]", CapabilityPlatform.All, CapabilityRiskClass.Consequential, CapabilityAvailability.PermissionRequired, provider: "haven.browser"),
        BuiltIn("create-automation", "Create Automation", "Create a reviewable scheduled action in Tasks.", "tasks", "automation", "tasks.automation.create", "[\"create\",\"schedule\"]", CapabilityPlatform.All, CapabilityRiskClass.Consequential, CapabilityAvailability.PermissionRequired, provider: "haven.tasks"),
        BuiltIn("run-task", "Run Task", "Choose and run one or more real Tasks.", "tasks", "tasks", "tasks.run", "[\"select\",\"run\",\"stop\"]", CapabilityPlatform.All, CapabilityRiskClass.Consequential, CapabilityAvailability.PermissionRequired, provider: "haven.tasks"),
        BuiltIn("edit-task", "Edit Task", "Resolve and edit authoritative Tasks state.", "tasks", "edit", "tasks.edit", "[\"select\",\"edit\"]", CapabilityPlatform.All, CapabilityRiskClass.Low, provider: "haven.tasks"),
        BuiltIn("computer-device-use", "Computer / Device Use", "Inspect and control an explicitly targeted device surface.", "tasks", "computer-use", "device.control", "[\"inspect\",\"interact\",\"verify\"]", CapabilityPlatform.All, CapabilityRiskClass.Consequential, CapabilityAvailability.PermissionRequired, provider: "haven.device"),
        BuiltIn("open-control-app", "Open / Control App", "Open or control an installed application through a platform provider.", "tasks", "apps", "platform.app-control", "[\"open\",\"control\"]", CapabilityPlatform.All, CapabilityRiskClass.Consequential, CapabilityAvailability.PermissionRequired, provider: "haven.platform"),
        BuiltIn("run-command", "Run Command", "Run an approved command in the selected workspace.", "tasks", "terminal", "workspace.run-command", "[\"run\",\"cancel\",\"inspect-result\"]", CapabilityPlatform.Windows, CapabilityRiskClass.Restricted, CapabilityAvailability.PermissionRequired, provider: "haven.workspace"),
        BuiltIn("run-script", "Run Script", "Run an approved script in the selected workspace.", "tasks", "terminal", "workspace.run-script", "[\"run\",\"cancel\",\"inspect-result\"]", CapabilityPlatform.Windows, CapabilityRiskClass.Restricted, CapabilityAvailability.PermissionRequired, provider: "haven.workspace"),
        BuiltIn("powershell", "PowerShell", "Run permissioned PowerShell commands on Windows.", "tasks", "terminal", "workspace.powershell", "[\"run\",\"cancel\",\"inspect-result\"]", CapabilityPlatform.Windows, CapabilityRiskClass.Restricted, CapabilityAvailability.PermissionRequired, provider: "haven.workspace"),
        BuiltIn("read-file", "Read File", "Read a file inside the selected Studio workspace.", "studio", "file", "workspace.read-file", "[\"read\"]", CapabilityPlatform.All, CapabilityRiskClass.ReadOnly, provider: "haven.workspace"),
        BuiltIn("write-file", "Write File", "Create or replace a file through Studio's reviewed change path.", "studio", "file", "workspace.write-file", "[\"create\",\"replace\",\"review\"]", CapabilityPlatform.All, CapabilityRiskClass.Consequential, CapabilityAvailability.PermissionRequired, provider: "haven.workspace"),
        BuiltIn("run-tests", "Run Tests", "Run targeted tests and report observed results.", "studio", "test", "workspace.run-tests", "[\"run\",\"cancel\",\"inspect-result\"]", CapabilityPlatform.Windows, CapabilityRiskClass.Consequential, CapabilityAvailability.PermissionRequired, provider: "haven.workspace")
    ];

    private static CapabilityDefinition BuiltIn(
        string key,
        string name,
        string description,
        string owner,
        string icon,
        string implementation,
        string actions,
        CapabilityPlatform platforms,
        CapabilityRiskClass risk,
        CapabilityAvailability availability = CapabilityAvailability.Available,
        string dependencies = "[]",
        string provider = "haven.core") =>
        new(
            GuidUtility.FromStableName("haven.capability." + key),
            key,
            name,
            description,
            owner,
            icon,
            $"Use {name} only through its registered provider and report observed outcomes.",
            implementation,
            actions,
            platforms,
            risk,
            availability,
            dependencies,
            provider,
            IsAttachable: true,
            IsAgentUsable: true,
            IsBuiltIn: true,
            IsEnabled: true,
            UpdatedAt: DateTimeOffset.UnixEpoch);
}
