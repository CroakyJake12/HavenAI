/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/DeclarativeModeTypes.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns DeclarativeModeDefinition, DeclarativeSurfaces, DeclarativePermissions, DeclarativeWorkflow, DeclarativeWorkflowStep, DeclarativeWorkflowEdge, DeclarativeUISchema, DeclarativeCard, DeclarativeCardAction, DeclarativeForm, DeclarativeFormField, DeclarativeCommandBar, DeclarativeCommandItem, DeclarativeSetting, DeclarativeHelp, ModePackageManifest, ModePackageValidationResult, ModePackageInstallResult. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Represents declarative mode definition and keeps its related state and behavior together.
/// </summary>
public sealed class DeclarativeModeDefinition
{
    /// <summary>
    /// Gets or updates key, the bindable or domain state represented by this property.
    /// </summary>
    public required string Key { get; init; }
    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public required string Name { get; init; }
    /// <summary>
    /// Gets or updates description, the bindable or domain state represented by this property.
    /// </summary>
    public required string Description { get; init; }
    /// <summary>
    /// Gets or updates author, the bindable or domain state represented by this property.
    /// </summary>
    public string? Author { get; init; }
    /// <summary>
    /// Gets or updates version, the bindable or domain state represented by this property.
    /// </summary>
    public string Version { get; init; } = "1.0.0";
    /// <summary>
    /// Gets or updates tags, the bindable or domain state represented by this property.
    /// </summary>
    public string[] Tags { get; init; } = [];
    /// <summary>
    /// Gets or updates icon key, the bindable or domain state represented by this property.
    /// </summary>
    public string IconKey { get; init; } = "\uE790";
    /// <summary>
    /// Gets or updates surfaces, the bindable or domain state represented by this property.
    /// </summary>
    public DeclarativeSurfaces Surfaces { get; init; } = new();
    /// <summary>
    /// Gets or updates permissions, the bindable or domain state represented by this property.
    /// </summary>
    public DeclarativePermissions Permissions { get; init; } = new();
    /// <summary>
    /// Gets or updates workflow, the bindable or domain state represented by this property.
    /// </summary>
    public DeclarativeWorkflow Workflow { get; init; } = new();
    /// <summary>
    /// Gets or updates ui, the bindable or domain state represented by this property.
    /// </summary>
    public DeclarativeUISchema Ui { get; init; } = new();
    /// <summary>
    /// Gets or updates capabilities, the bindable or domain state represented by this property.
    /// </summary>
    public string[] Capabilities { get; init; } = [];
    /// <summary>
    /// Gets or updates settings, the bindable or domain state represented by this property.
    /// </summary>
    public Dictionary<string, DeclarativeSetting> Settings { get; init; } = new();
    /// <summary>
    /// Gets or updates help, the bindable or domain state represented by this property.
    /// </summary>
    public DeclarativeHelp Help { get; init; } = new();
}

/// <summary>
/// Represents declarative surfaces and keeps its related state and behavior together.
/// </summary>
public sealed class DeclarativeSurfaces
{
    /// <summary>
    /// Gets or updates chat, the bindable or domain state represented by this property.
    /// </summary>
    public bool Chat { get; init; } = true;
    /// <summary>
    /// Gets or updates do, the bindable or domain state represented by this property.
    /// </summary>
    public bool Do { get; init; }
    /// <summary>
    /// Gets or updates studio, the bindable or domain state represented by this property.
    /// </summary>
    public bool Studio { get; init; }
    /// <summary>
    /// Gets or updates browse, the bindable or domain state represented by this property.
    /// </summary>
    public bool Browse { get; init; }
    /// <summary>
    /// Gets or updates plan, the bindable or domain state represented by this property.
    /// </summary>
    public bool Plan { get; init; }
    /// <summary>
    /// Gets or updates training, the bindable or domain state represented by this property.
    /// </summary>
    public bool Training { get; init; }
    /// <summary>
    /// Gets or updates additional, the bindable or domain state represented by this property.
    /// </summary>
    public string[] Additional { get; init; } = [];
}

/// <summary>
/// Represents declarative permissions and keeps its related state and behavior together.
/// </summary>
public sealed class DeclarativePermissions
{
    /// <summary>
    /// Gets or updates file permission, the bindable or domain state represented by this property.
    /// </summary>
    public string FilePermission { get; init; } = "Ask";
    /// <summary>
    /// Gets or updates command permission, the bindable or domain state represented by this property.
    /// </summary>
    public string CommandPermission { get; init; } = "Ask";
    /// <summary>
    /// Gets or updates browser permission, the bindable or domain state represented by this property.
    /// </summary>
    public string BrowserPermission { get; init; } = "Ask";
    /// <summary>
    /// Gets or updates allow desktop tools, the bindable or domain state represented by this property.
    /// </summary>
    public bool AllowDesktopTools { get; init; }
    /// <summary>
    /// Gets or updates allow file system writes, the bindable or domain state represented by this property.
    /// </summary>
    public bool AllowFileSystemWrites { get; init; }
    /// <summary>
    /// Gets or updates custom capabilities, the bindable or domain state represented by this property.
    /// </summary>
    public string[] CustomCapabilities { get; init; } = [];
}

/// <summary>
/// Represents declarative workflow and keeps its related state and behavior together.
/// </summary>
public sealed class DeclarativeWorkflow
{
    /// <summary>
    /// Gets or updates type, the bindable or domain state represented by this property.
    /// </summary>
    public string Type { get; init; } = "linear";
    /// <summary>
    /// Gets or updates steps, the bindable or domain state represented by this property.
    /// </summary>
    public DeclarativeWorkflowStep[] Steps { get; init; } = [];
    /// <summary>
    /// Gets or updates edges, the bindable or domain state represented by this property.
    /// </summary>
    public Dictionary<string, DeclarativeWorkflowEdge> Edges { get; init; } = new();
}

/// <summary>
/// Represents declarative workflow step and keeps its related state and behavior together.
/// </summary>
public sealed class DeclarativeWorkflowStep
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public required string Id { get; init; }
    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public required string Name { get; init; }
    /// <summary>
    /// Gets or updates kind, the bindable or domain state represented by this property.
    /// </summary>
    public string Kind { get; init; } = "chat";
    /// <summary>
    /// Gets or updates system prompt, the bindable or domain state represented by this property.
    /// </summary>
    public string? SystemPrompt { get; init; }
    /// <summary>
    /// Gets or updates required capabilities, the bindable or domain state represented by this property.
    /// </summary>
    public string[]? RequiredCapabilities { get; init; }
    /// <summary>
    /// Gets or updates tools, the bindable or domain state represented by this property.
    /// </summary>
    public string[]? Tools { get; init; }
    /// <summary>
    /// Gets or updates config, the bindable or domain state represented by this property.
    /// </summary>
    public Dictionary<string, object>? Config { get; init; }
}

/// <summary>
/// Represents declarative workflow edge and keeps its related state and behavior together.
/// </summary>
public sealed class DeclarativeWorkflowEdge
{
    /// <summary>
    /// Gets or updates condition, the bindable or domain state represented by this property.
    /// </summary>
    public string? Condition { get; init; }
    /// <summary>
    /// Gets or updates target, the bindable or domain state represented by this property.
    /// </summary>
    public string? Target { get; init; }
}

/// <summary>
/// Represents declarative ui schema and keeps its related state and behavior together.
/// </summary>
public sealed class DeclarativeUISchema
{
    /// <summary>
    /// Gets or updates layout, the bindable or domain state represented by this property.
    /// </summary>
    public string Layout { get; init; } = "chat";
    /// <summary>
    /// Gets or updates cards, the bindable or domain state represented by this property.
    /// </summary>
    public DeclarativeCard[] Cards { get; init; } = [];
    /// <summary>
    /// Gets or updates forms, the bindable or domain state represented by this property.
    /// </summary>
    public DeclarativeForm[] Forms { get; init; } = [];
    /// <summary>
    /// Gets or updates command bar, the bindable or domain state represented by this property.
    /// </summary>
    public DeclarativeCommandBar CommandBar { get; init; } = new();
}

/// <summary>
/// Represents declarative card and keeps its related state and behavior together.
/// </summary>
public sealed class DeclarativeCard
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public required string Id { get; init; }
    /// <summary>
    /// Gets or updates title, the bindable or domain state represented by this property.
    /// </summary>
    public required string Title { get; init; }
    /// <summary>
    /// Gets or updates kind, the bindable or domain state represented by this property.
    /// </summary>
    public string Kind { get; init; } = "info";
    /// <summary>
    /// Gets or updates description, the bindable or domain state represented by this property.
    /// </summary>
    public string? Description { get; init; }
    /// <summary>
    /// Gets or updates data bindings, the bindable or domain state represented by this property.
    /// </summary>
    public string[]? DataBindings { get; init; }
    /// <summary>
    /// Gets or updates action, the bindable or domain state represented by this property.
    /// </summary>
    public DeclarativeCardAction? Action { get; init; }
}

/// <summary>
/// Represents declarative card action and keeps its related state and behavior together.
/// </summary>
public sealed class DeclarativeCardAction
{
    /// <summary>
    /// Gets or updates type, the bindable or domain state represented by this property.
    /// </summary>
    public string Type { get; init; } = "navigate";
    /// <summary>
    /// Gets or updates target, the bindable or domain state represented by this property.
    /// </summary>
    public string? Target { get; init; }
    /// <summary>
    /// Gets or updates label, the bindable or domain state represented by this property.
    /// </summary>
    public string? Label { get; init; }
}

/// <summary>
/// Represents declarative form and keeps its related state and behavior together.
/// </summary>
public sealed class DeclarativeForm
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public required string Id { get; init; }
    /// <summary>
    /// Gets or updates label, the bindable or domain state represented by this property.
    /// </summary>
    public required string Label { get; init; }
    /// <summary>
    /// Gets or updates fields, the bindable or domain state represented by this property.
    /// </summary>
    public DeclarativeFormField[] Fields { get; init; } = [];
}

/// <summary>
/// Represents declarative form field and keeps its related state and behavior together.
/// </summary>
public sealed class DeclarativeFormField
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public required string Id { get; init; }
    /// <summary>
    /// Gets or updates label, the bindable or domain state represented by this property.
    /// </summary>
    public required string Label { get; init; }
    /// <summary>
    /// Gets or updates type, the bindable or domain state represented by this property.
    /// </summary>
    public string Type { get; init; } = "text";
    /// <summary>
    /// Gets or updates placeholder, the bindable or domain state represented by this property.
    /// </summary>
    public string? Placeholder { get; init; }
    /// <summary>
    /// Gets or updates required, the bindable or domain state represented by this property.
    /// </summary>
    public bool Required { get; init; }
    /// <summary>
    /// Gets or updates options, the bindable or domain state represented by this property.
    /// </summary>
    public string[]? Options { get; init; }
}

/// <summary>
/// Represents declarative command bar and keeps its related state and behavior together.
/// </summary>
public sealed class DeclarativeCommandBar
{
    /// <summary>
    /// Gets or updates enabled, the bindable or domain state represented by this property.
    /// </summary>
    public bool Enabled { get; init; } = true;
    /// <summary>
    /// Gets or updates items, the bindable or domain state represented by this property.
    /// </summary>
    public DeclarativeCommandItem[] Items { get; init; } = [];
}

/// <summary>
/// Represents declarative command item and keeps its related state and behavior together.
/// </summary>
public sealed class DeclarativeCommandItem
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public required string Id { get; init; }
    /// <summary>
    /// Gets or updates label, the bindable or domain state represented by this property.
    /// </summary>
    public required string Label { get; init; }
    /// <summary>
    /// Gets or updates icon, the bindable or domain state represented by this property.
    /// </summary>
    public string? Icon { get; init; }
    /// <summary>
    /// Gets or updates action, the bindable or domain state represented by this property.
    /// </summary>
    public string? Action { get; init; }
}

/// <summary>
/// Represents declarative setting and keeps its related state and behavior together.
/// </summary>
public sealed class DeclarativeSetting
{
    /// <summary>
    /// Gets or updates label, the bindable or domain state represented by this property.
    /// </summary>
    public required string Label { get; init; }
    /// <summary>
    /// Gets or updates type, the bindable or domain state represented by this property.
    /// </summary>
    public string Type { get; init; } = "string";
    /// <summary>
    /// Gets or updates default, the bindable or domain state represented by this property.
    /// </summary>
    public string? Default { get; init; }
    /// <summary>
    /// Gets or updates description, the bindable or domain state represented by this property.
    /// </summary>
    public string? Description { get; init; }
    /// <summary>
    /// Gets or updates options, the bindable or domain state represented by this property.
    /// </summary>
    public string[]? Options { get; init; }
}

/// <summary>
/// Represents declarative help and keeps its related state and behavior together.
/// </summary>
public sealed class DeclarativeHelp
{
    /// <summary>
    /// Gets or updates description, the bindable or domain state represented by this property.
    /// </summary>
    public string? Description { get; init; }
    /// <summary>
    /// Gets or updates examples, the bindable or domain state represented by this property.
    /// </summary>
    public string[]? Examples { get; init; }
    /// <summary>
    /// Gets or updates capabilities, the bindable or domain state represented by this property.
    /// </summary>
    public string[]? Capabilities { get; init; }
    /// <summary>
    /// Gets or updates limitations, the bindable or domain state represented by this property.
    /// </summary>
    public string[]? Limitations { get; init; }
}

/// <summary>
/// Represents mode package manifest and keeps its related state and behavior together.
/// </summary>
public sealed class ModePackageManifest
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public required string Id { get; init; }
    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public required string Name { get; init; }
    /// <summary>
    /// Gets or updates version, the bindable or domain state represented by this property.
    /// </summary>
    public required string Version { get; init; }
    /// <summary>
    /// Gets or updates definition, the bindable or domain state represented by this property.
    /// </summary>
    public required DeclarativeModeDefinition Definition { get; init; }
    /// <summary>
    /// Gets or updates source, the bindable or domain state represented by this property.
    /// </summary>
    public ModeSource Source { get; init; } = ModeSource.Created;
    /// <summary>
    /// Gets or updates test cases, the bindable or domain state represented by this property.
    /// </summary>
    public string[] TestCases { get; init; } = [];
    /// <summary>
    /// Creates d at with the invariants required by its callers.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Represents mode package validation result and keeps its related state and behavior together.
/// </summary>
public sealed class ModePackageValidationResult
{
    /// <summary>
    /// Reports whether is valid is true for the current state.
    /// </summary>
    public bool IsValid { get; init; }
    /// <summary>
    /// Gets or updates errors, the bindable or domain state represented by this property.
    /// </summary>
    public string[] Errors { get; init; } = [];
    /// <summary>
    /// Gets or updates warnings, the bindable or domain state represented by this property.
    /// </summary>
    public string[] Warnings { get; init; } = [];
    /// <summary>
    /// Gets or updates permission requirements, the bindable or domain state represented by this property.
    /// </summary>
    public string[] PermissionRequirements { get; init; } = [];
    /// <summary>
    /// Gets or updates capability gaps, the bindable or domain state represented by this property.
    /// </summary>
    public string[] CapabilityGaps { get; init; } = [];
}

/// <summary>
/// Represents mode package install result and keeps its related state and behavior together.
/// </summary>
public sealed class ModePackageInstallResult
{
    /// <summary>
    /// Gets or updates succeeded, the bindable or domain state represented by this property.
    /// </summary>
    public bool Succeeded { get; init; }
    /// <summary>
    /// Gets or updates mode id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid? ModeId { get; init; }
    /// <summary>
    /// Gets or updates message, the bindable or domain state represented by this property.
    /// </summary>
    public string Message { get; init; } = string.Empty;
    /// <summary>
    /// Gets or updates warnings, the bindable or domain state represented by this property.
    /// </summary>
    public string[] Warnings { get; init; } = [];
}
