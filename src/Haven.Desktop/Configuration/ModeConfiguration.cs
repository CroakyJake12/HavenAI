using Haven.Core;

namespace Haven.Desktop.Configuration;

/// <summary>
/// Defines the available modes and their properties.
/// Edit this file to add, remove, or modify modes.
/// </summary>
public static class ModeConfiguration
{
    public static readonly IReadOnlyList<ModeDefinition> Modes =
    [
        new()
        {
            Key = "chat",
            Name = "Chat",
            Description = "General conversation with Haven",
            IconKey = "chat",
            BaseMode = HavenMode.Chat,
            IsEnabled = true,
            SortOrder = 0
        },
        new()
        {
            Key = "study",
            Name = "Study",
            Description = "Structured learning and knowledge checks",
            IconKey = "teach",
            BaseMode = HavenMode.Teach,
            IsEnabled = true,
            SortOrder = 1
        },
        new()
        {
            Key = "go",
            Name = "Go",
            Description = "Quick actions and navigation",
            IconKey = "search",
            BaseMode = HavenMode.Go,
            IsEnabled = true,
            SortOrder = 2
        },
        new()
        {
            Key = "studio",
            Name = "Studio",
            Description = "Project workspace and development",
            IconKey = "studio",
            BaseMode = HavenMode.Studio,
            IsEnabled = true,
            SortOrder = 3
        }
    ];

    public sealed class ModeDefinition
    {
        public required string Key { get; init; }
        public required string Name { get; init; }
        public required string Description { get; init; }
        public required string IconKey { get; init; }
        public required HavenMode BaseMode { get; init; }
        public required bool IsEnabled { get; init; }
        public required int SortOrder { get; init; }
    }
}
