using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Views.Pages.Go;
using Haven.Desktop.Views.Shell.TopRail;

namespace Haven.Desktop.Tests;

public sealed class GoPendingTaskTests
{
    [Fact]
    public void Pending_task_can_be_restored_after_router_snapshot_is_taken()
    {
        using var page = new GoPage(new HavenEventBus());
        var file = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "go-router-context.txt"));
        page.AttachFiles([file]);

        var snapshot = page.TakeAttachments();
        Assert.Single(snapshot.Files);

        page.RestorePendingTask("open this", snapshot);

        Assert.Equal("open this", page.Route.Instruction.Text);
        var restored = page.TakeAttachments();
        Assert.Single(restored.Files);
        Assert.Equal(file, restored.Files[0]);
    }

    [Fact]
    public void Pending_task_snapshot_preserves_agent_instructions_and_response_preferences()
    {
        using var page = new GoPage(new HavenEventBus());
        var now = DateTimeOffset.UtcNow;
        var agent = new AgentDefinition(
            Guid.NewGuid(),
            "Research Agent",
            "Research context",
            "Use research agent",
            "agents",
            string.Empty,
            null,
            "[]",
            "{}",
            true,
            true,
            now);
        var instruction = new PromptDefinition(
            Guid.NewGuid(),
            "Be concise",
            "Prefer concise answers",
            "prompt",
            "Keep the response concise.",
            false,
            true,
            true,
            now);

        page.ApplyTaskSelection(new AddMenuSelection(AddMenu.AddMenuAction.Agent, agent));
        page.ApplyTaskSelection(new AddMenuSelection(AddMenu.AddMenuAction.Instruction, instruction));
        page.ApplyTaskSelection(new AddMenuSelection(AddMenu.AddMenuAction.AllowActions, ChatActionMode.JustChat));
        page.ApplyTaskSelection(new AddMenuSelection(AddMenu.AddMenuAction.VisualResponses, GenerativeUiResponseMode.AlwaysText));

        Assert.Contains("Instructions: Be concise", page.Route.AttachmentText.Content);
        var snapshot = page.TakeTaskSnapshot();

        Assert.Equal(agent.Id, snapshot.Agent?.Id);
        Assert.Equal(instruction.Id, Assert.Single(snapshot.Instructions).Id);
        Assert.Equal(ChatActionMode.JustChat, snapshot.ActionMode);
        Assert.Equal(GenerativeUiResponseMode.AlwaysText, snapshot.VisualResponseMode);

        var cleared = page.TakeTaskSnapshot();
        Assert.Null(cleared.Agent);
        Assert.Empty(cleared.Instructions);
        Assert.Null(cleared.ActionMode);
        Assert.Null(cleared.VisualResponseMode);

        page.RestorePendingTask("continue this", snapshot);
        Assert.Equal("continue this", page.Route.Instruction.Text);
        Assert.Contains("Instructions: Be concise", page.Route.AttachmentText.Content);

        var restored = page.TakeTaskSnapshot();
        Assert.Equal(agent.Id, restored.Agent?.Id);
        Assert.Equal(instruction.Id, Assert.Single(restored.Instructions).Id);
        Assert.Equal(ChatActionMode.JustChat, restored.ActionMode);
        Assert.Equal(GenerativeUiResponseMode.AlwaysText, restored.VisualResponseMode);
    }
}
