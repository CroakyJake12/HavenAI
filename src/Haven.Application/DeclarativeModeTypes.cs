using Haven.Core;

namespace Haven.Application;

public sealed class DeclarativeModeDefinition
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public string? Author { get; init; }
    public string Version { get; init; } = "1.0.0";
    public string[] Tags { get; init; } = [];
    public string IconKey { get; init; } = "\uE790";
    public DeclarativeSurfaces Surfaces { get; init; } = new();
    public DeclarativePermissions Permissions { get; init; } = new();
    public DeclarativeWorkflow Workflow { get; init; } = new();
    public DeclarativeUISchema Ui { get; init; } = new();
    public string[] Capabilities { get; init; } = [];
    public Dictionary<string, DeclarativeSetting> Settings { get; init; } = new();
    public DeclarativeHelp Help { get; init; } = new();
}

public sealed class DeclarativeSurfaces
{
    public bool Chat { get; init; } = true;
    public bool Do { get; init; }
    public bool Studio { get; init; }
    public bool Browse { get; init; }
    public bool Plan { get; init; }
    public bool Training { get; init; }
    public string[] Additional { get; init; } = [];
}

public sealed class DeclarativePermissions
{
    public string FilePermission { get; init; } = "Ask";
    public string CommandPermission { get; init; } = "Ask";
    public string BrowserPermission { get; init; } = "Ask";
    public bool AllowDesktopTools { get; init; }
    public bool AllowFileSystemWrites { get; init; }
    public string[] CustomCapabilities { get; init; } = [];
}

public sealed class DeclarativeWorkflow
{
    public string Type { get; init; } = "linear";
    public DeclarativeWorkflowStep[] Steps { get; init; } = [];
    public Dictionary<string, DeclarativeWorkflowEdge> Edges { get; init; } = new();
}

public sealed class DeclarativeWorkflowStep
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Kind { get; init; } = "chat";
    public string? SystemPrompt { get; init; }
    public string[]? RequiredCapabilities { get; init; }
    public string[]? Tools { get; init; }
    public Dictionary<string, object>? Config { get; init; }
}

public sealed class DeclarativeWorkflowEdge
{
    public string? Condition { get; init; }
    public string? Target { get; init; }
}

public sealed class DeclarativeUISchema
{
    public string Layout { get; init; } = "chat";
    public DeclarativeCard[] Cards { get; init; } = [];
    public DeclarativeForm[] Forms { get; init; } = [];
    public DeclarativeCommandBar CommandBar { get; init; } = new();
}

public sealed class DeclarativeCard
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Kind { get; init; } = "info";
    public string? Description { get; init; }
    public string[]? DataBindings { get; init; }
    public DeclarativeCardAction? Action { get; init; }
}

public sealed class DeclarativeCardAction
{
    public string Type { get; init; } = "navigate";
    public string? Target { get; init; }
    public string? Label { get; init; }
}

public sealed class DeclarativeForm
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public DeclarativeFormField[] Fields { get; init; } = [];
}

public sealed class DeclarativeFormField
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public string Type { get; init; } = "text";
    public string? Placeholder { get; init; }
    public bool Required { get; init; }
    public string[]? Options { get; init; }
}

public sealed class DeclarativeCommandBar
{
    public bool Enabled { get; init; } = true;
    public DeclarativeCommandItem[] Items { get; init; } = [];
}

public sealed class DeclarativeCommandItem
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public string? Icon { get; init; }
    public string? Action { get; init; }
}

public sealed class DeclarativeSetting
{
    public required string Label { get; init; }
    public string Type { get; init; } = "string";
    public string? Default { get; init; }
    public string? Description { get; init; }
    public string[]? Options { get; init; }
}

public sealed class DeclarativeHelp
{
    public string? Description { get; init; }
    public string[]? Examples { get; init; }
    public string[]? Capabilities { get; init; }
    public string[]? Limitations { get; init; }
}

public sealed class ModePackageManifest
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required DeclarativeModeDefinition Definition { get; init; }
    public ModeSource Source { get; init; } = ModeSource.Created;
    public string[] TestCases { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class ModePackageValidationResult
{
    public bool IsValid { get; init; }
    public string[] Errors { get; init; } = [];
    public string[] Warnings { get; init; } = [];
    public string[] PermissionRequirements { get; init; } = [];
    public string[] CapabilityGaps { get; init; } = [];
}

public sealed class ModePackageInstallResult
{
    public bool Succeeded { get; init; }
    public Guid? ModeId { get; init; }
    public string Message { get; init; } = string.Empty;
    public string[] Warnings { get; init; } = [];
}
