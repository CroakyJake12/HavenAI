using Haven.Core;

namespace Haven.Application;

public sealed class BuiltInModeSeed
{
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
            "teach", "Teach", "Structured lessons and knowledge checks", "book",
            HavenMode.Teach, "[\"Teach\"]", "[]", "[]", "[]", "",
            ModeSource.BuiltIn, ModeInstallState.BuiltIn, "Haven", "1.0.0", "[]",
            DateTimeOffset.MinValue, DateTimeOffset.MinValue),
        new ModeDefinition(
            Guid.Parse("a0000000-0000-0000-0000-000000000003"),
            "do", "Do", "Task completion with approvals and audit trail", "rocket",
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
            DateTimeOffset.MinValue, DateTimeOffset.MinValue),
        new ModeDefinition(
            Guid.Parse("a0000000-0000-0000-0000-000000000008"),
            "call", "Call", "Voice conversations with local models", "phone",
            HavenMode.Chat, "[\"Chat\"]", "[]", "[]", "[]", "",
            ModeSource.BuiltIn, ModeInstallState.BuiltIn, "Haven", "1.0.0", "[]",
            DateTimeOffset.MinValue, DateTimeOffset.MinValue)
    ];
}
