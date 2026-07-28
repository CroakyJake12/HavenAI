namespace Haven.Desktop.Configuration;

/// <summary>
/// Defines the available actions for the Actions toolbar.
/// Edit this file to add, remove, or modify actions.
/// </summary>
public static class ActionConfiguration
{
    public static readonly IReadOnlyList<ActionDefinition> FeaturedActions =
    [
        new()
        {
            Name = "Voice session",
            IconKey = "call",
            Description = "Start a live voice session in Chat.",
            Category = "Featured",
            IsFeatured = true,
            Action = "OpenVoiceSession"
        },
        new()
        {
            Name = "Open Notifications",
            IconKey = "bell",
            Description = "Review priority and unread notifications.",
            Category = "Featured",
            IsFeatured = true,
            Action = "ShowNotifications"
        },
        new()
        {
            Name = "Open App (In New Tab)",
            IconKey = "rocket",
            Description = "Choose an app without replacing this tab.",
            Category = "Featured",
            IsFeatured = true,
            Action = "ShowAppLauncherNewTab"
        },
        new()
        {
            Name = "Open App (Current Tab)",
            IconKey = "rocket",
            Description = "Choose an app for the current tab.",
            Category = "Featured",
            IsFeatured = true,
            Action = "ShowAppLauncherCurrentTab"
        },
        new()
        {
            Name = "Settings",
            IconKey = "settings",
            Description = "Open Haven settings.",
            Category = "Featured",
            IsFeatured = true,
            Action = "OpenSettings"
        }
    ];

    public static readonly IReadOnlyList<ActionDefinition> CatalogueActions =
    [
        new()
        {
            Name = "New chat",
            IconKey = "plus",
            Description = "Start a clean conversation in the current product.",
            Category = "File",
            Shortcut = "Ctrl+N",
            Action = "NewChat"
        },
        new()
        {
            Name = "Branch chat",
            IconKey = "branch",
            Description = "Copy the current conversation and context into an independent branch.",
            Category = "File",
            Action = "BranchChat"
        },
        new()
        {
            Name = "Temporary chat",
            IconKey = "chat",
            Description = "Toggle local history for this conversation.",
            Category = "File",
            Action = "ToggleTemporary"
        },
        new()
        {
            Name = "Compact context",
            IconKey = "refresh",
            Description = "Summarise older turns while preserving decisions and requirements.",
            Category = "File",
            Action = "CompactContext"
        },
        new()
        {
            Name = "Archive current chat",
            IconKey = "archive",
            Description = "Remove the chat from recents without destroying it.",
            Category = "File",
            Action = "ArchiveChat"
        },
        new()
        {
            Name = "Rename chat",
            IconKey = "edit",
            Description = "Change the current chat title.",
            Category = "Edit",
            Action = "RenameChat"
        },
        new()
        {
            Name = "Delete current chat",
            IconKey = "delete",
            Description = "Permanently remove the current conversation after confirmation.",
            Category = "Edit",
            Action = "DeleteChat"
        },
        new()
        {
            Name = "Pin or unpin chat",
            IconKey = "pin",
            Description = "Toggle the chat in the Pinned section.",
            Category = "Edit",
            Action = "TogglePin"
        },
        new()
        {
            Name = "Copy last response",
            IconKey = "file",
            Description = "Copy the most recent Haven response.",
            Category = "Edit",
            Action = "CopyLastResponse"
        },
        new()
        {
            Name = "Undo",
            IconKey = "chevron-left",
            Description = "Undo the latest editable workspace change.",
            Category = "Edit",
            Shortcut = "Ctrl+Z",
            Action = "Undo"
        },
        new()
        {
            Name = "Redo",
            IconKey = "chevron-right",
            Description = "Redo the latest editable workspace change.",
            Category = "Edit",
            Shortcut = "Ctrl+Y",
            Action = "Redo"
        },
        new()
        {
            Name = "Save",
            IconKey = "file",
            Description = "Save the current editable workspace.",
            Category = "Edit",
            Shortcut = "Ctrl+S",
            Action = "Save"
        },
        new()
        {
            Name = "Configure model",
            IconKey = "refresh",
            Description = "Search models and open advanced generation and safety options.",
            Category = "Chat",
            Action = "ConfigureModel"
        },
        new()
        {
            Name = "Instruction Library",
            IconKey = "prompt",
            Description = "Browse built-in and custom reusable instructions invoked with >.",
            Category = "Chat",
            Action = "NavigatePrompts"
        },
        new()
        {
            Name = "Plugins",
            IconKey = "plugin",
            Description = "Browse functional Haven capabilities invoked with @.",
            Category = "Chat",
            Action = "NavigatePlugins"
        },
        new()
        {
            Name = "Scheduled Actions",
            IconKey = "plan",
            Description = "Create and manage scheduled local jobs.",
            Category = "Tools",
            Action = "NavigateAutomations"
        },
        new()
        {
            Name = "Macros",
            IconKey = "macro",
            Description = "Create or run explicit click-to-run actions.",
            Category = "Tools",
            Action = "NavigateMacros"
        },
        new()
        {
            Name = "Archive",
            IconKey = "archive",
            Description = "Restore archived chats, groups, and projects.",
            Category = "Tools",
            Action = "NavigateArchive"
        },
        new()
        {
            Name = "Activity Log",
            IconKey = "activity",
            Description = "View recent conversations and tool activity across sessions.",
            Category = "Tools",
            Action = "NavigateActivityLog"
        },
        new()
        {
            Name = "Haven Browse",
            IconKey = "browse",
            Description = "Open the isolated tabbed browser and side assistant.",
            Category = "Tools",
            Action = "NavigateBrowser"
        },
        new()
        {
            Name = "Haven Training",
            IconKey = "training",
            Description = "Run an autonomous agent session and score the result.",
            Category = "Tools",
            Action = "NavigateTraining"
        },
        new()
        {
            Name = "App Library",
            IconKey = "rocket",
            Description = "Discover, pin, and create Haven apps.",
            Category = "Tools",
            Action = "NavigateModeLibrary"
        },
        new()
        {
            Name = "Build Browse extension",
            IconKey = "studio",
            Description = "Create a scoped Haven extension manifest and content script in Do or Studio.",
            Category = "Project",
            Action = "BuildBrowserExtension"
        },
        new()
        {
            Name = "Toggle sidebar",
            IconKey = "sidebar",
            Description = "Show or hide the current product sidebar.",
            Category = "View",
            Action = "ToggleSidebar"
        },
        new()
        {
            Name = "Refresh models",
            IconKey = "refresh",
            Description = "Reload the installed Ollama model list.",
            Category = "Tools",
            Action = "RefreshModels"
        },
        new()
        {
            Name = "Settings",
            IconKey = "settings",
            Description = "Appearance, models, permissions, context, and browser options.",
            Category = "Tools",
            Action = "OpenSettings"
        }
    ];

    public sealed class ActionDefinition
    {
        public required string Name { get; init; }
        public required string IconKey { get; init; }
        public required string Description { get; init; }
        public required string Category { get; init; }
        public required string Action { get; init; }
        public string? Shortcut { get; init; }
        public bool IsFeatured { get; init; }
    }
}
