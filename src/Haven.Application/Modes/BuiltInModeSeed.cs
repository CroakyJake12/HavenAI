/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/BuiltInModeSeed.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns BuiltInModeSeed. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Represents built in mode seed and keeps its related state and behavior together.
/// </summary>
public sealed class BuiltInModeSeed
{
    /// <summary>
    /// Gets or updates modes, the bindable or domain state represented by this property.
    /// </summary>
    public static IReadOnlyList<ModeDefinition> Modes { get; } =
    [
        new ModeDefinition(
            Guid.Parse("a0000000-0000-0000-0000-000000000001"),
            "chat", "Chat", "Private conversation with local models", "chat",
            HavenMode.Chat, "[\"Chat\"]", "[]", "[]", "[]", "",
            ModeSource.BuiltIn, ModeInstallState.BuiltIn, "Haven", "1.0.0", "[]",
            DateTimeOffset.MinValue, DateTimeOffset.MinValue),
        new ModeDefinition(
            Guid.Parse("a0000000-0000-0000-0000-000000000002"),
            "teach", "Study", "Structured lessons and knowledge checks", "book",
            HavenMode.Teach, "[\"Teach\"]", "[]", "[]", "[]", "",
            ModeSource.BuiltIn, ModeInstallState.BuiltIn, "Haven", "1.0.0", "[]",
            DateTimeOffset.MinValue, DateTimeOffset.MinValue),
        new ModeDefinition(
            Guid.Parse("a0000000-0000-0000-0000-000000000003"),
            "research", "Research", "Source-driven investigation, comparison and reporting", "search",
            HavenMode.Do, "[\"Do\"]", "[\"write_file\",\"replace_in_file\",\"run_tests\",\"run_command\"]", "[]", "[\"Automate\",\"Macro\"]", "",
            ModeSource.BuiltIn, ModeInstallState.BuiltIn, "Haven", "1.0.0", "[]",
            DateTimeOffset.MinValue, DateTimeOffset.MinValue),
        new ModeDefinition(
            Guid.Parse("a0000000-0000-0000-0000-000000000004"),
            "studio", "Studio", "Inspect, edit, test and repair local projects", "code",
            HavenMode.Studio, "[\"Studio\"]", "[\"write_file\",\"replace_in_file\",\"run_tests\",\"run_command\"]", "[]", "[\"Automate\",\"Macro\",\"Test\"]", "",
            ModeSource.BuiltIn, ModeInstallState.BuiltIn, "Haven", "1.0.0", "[]",
            DateTimeOffset.MinValue, DateTimeOffset.MinValue),
        new ModeDefinition(
            Guid.Parse("a0000000-0000-0000-0000-000000000005"),
            "browse", "Browse", "Isolated tabbed browser with side assistant", "globe",
            HavenMode.Chat, "[\"Browse\"]", "[]", "[]", "[\"BrowserUse\",\"WebSearch\"]", "",
            ModeSource.BuiltIn, ModeInstallState.BuiltIn, "Haven", "1.0.0", "[]",
            DateTimeOffset.MinValue, DateTimeOffset.MinValue),
        new ModeDefinition(
            Guid.Parse("a0000000-0000-0000-0000-000000000006"),
            "plan", "Plan", "Task planning and calendar management", "calendar",
            HavenMode.Chat, "[\"Plan\"]", "[]", "[]", "[]", "",
            ModeSource.BuiltIn, ModeInstallState.BuiltIn, "Haven", "1.0.0", "[]",
            DateTimeOffset.MinValue, DateTimeOffset.MinValue),
        new ModeDefinition(
            Guid.Parse("a0000000-0000-0000-0000-000000000007"),
            "training", "Training", "Autonomous agent sessions with scoring", "target",
            HavenMode.Do, "[\"Training\"]", "[\"write_file\",\"replace_in_file\",\"run_tests\",\"run_command\"]", "[]", "[]", "",
            ModeSource.BuiltIn, ModeInstallState.BuiltIn, "Haven", "1.0.0", "[]",
            DateTimeOffset.MinValue, DateTimeOffset.MinValue)
    ];
}