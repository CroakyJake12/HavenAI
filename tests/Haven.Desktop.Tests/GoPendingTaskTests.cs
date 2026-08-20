using Haven.Desktop.Events;
using Haven.Desktop.Views.Pages.Go;

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
}
