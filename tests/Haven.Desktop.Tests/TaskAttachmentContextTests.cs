using Haven.Core;
using Haven.Desktop.Views.Shell.TopRail;

namespace Haven.Desktop.Tests;

public sealed class TaskAttachmentContextTests
{
    [Fact]
    public void AppsAndFilesAttachWithoutNavigationStateAndDeduplicateByIdentity()
    {
        var now = DateTimeOffset.UtcNow;
        var app = new ModeDefinition(
            Guid.NewGuid(), "research", "Research", "Compare sources", "browse",
            HavenMode.Chat, "[]", "[]", "[]", "[]", "Use cited sources.",
            ModeSource.Created, ModeInstallState.InstalledByUser, "User", "1.0.0", "[]", now, now);
        var file = Path.Combine(Path.GetTempPath(), "brief.txt");
        var context = new TaskAttachmentContext();

        context.AttachApp(app);
        context.AttachApp(app);
        context.AttachFiles([file, file.ToUpperInvariant()]);

        Assert.Single(context.Apps);
        Assert.Single(context.Files);
        Assert.Contains("Attached Haven app: Research (research)", context.BuildAppContext());
        Assert.Contains("Use cited sources.", context.BuildAppContext());
    }

    [Fact]
    public void TakingThePendingTaskContextTransfersAndClearsIt()
    {
        var now = DateTimeOffset.UtcNow;
        var app = new ModeDefinition(
            Guid.NewGuid(), "data-helper", "Data Helper", "Inspect data", "data",
            HavenMode.Tasks, "[]", "[]", "[]", "[]", "",
            ModeSource.Created, ModeInstallState.InstalledByUser, "User", "1.0.0", "[]", now, now);
        var context = new TaskAttachmentContext();
        context.AttachApp(app);
        context.AttachFiles([Path.Combine(Path.GetTempPath(), "data.csv")]);

        var snapshot = context.TakeSnapshot();

        Assert.Single(snapshot.Apps);
        Assert.Single(snapshot.Files);
        Assert.True(context.IsEmpty);
    }

    [Fact]
    public void CapabilityAttachmentIsARelevanceSignalAndImplicitlyAttachesItsOwningApp()
    {
        var app = CreateApp("tasks", "Tasks");
        var capability = CapabilityRegistryCatalog.BuiltIns.Single(item => item.Key == "run-task");
        var context = new TaskAttachmentContext();

        context.AttachCapability(capability, app);
        context.AttachCapability(capability, app);

        Assert.Single(context.Capabilities);
        Assert.Single(context.Apps);
        Assert.Contains("relevance signals only", context.BuildCapabilityContext());
        Assert.Contains("Run Task (run-task)", context.BuildCapabilityContext());
    }

    [Fact]
    public void RemovingLastCapabilityRemovesOnlyAnImplicitOwnerApp()
    {
        var app = CreateApp("tasks", "Tasks");
        var capability = CapabilityRegistryCatalog.BuiltIns.Single(item => item.Key == "run-task");
        var implicitContext = new TaskAttachmentContext();
        implicitContext.AttachCapability(capability, app);

        Assert.True(implicitContext.RemoveCapability(capability.Id));
        Assert.Empty(implicitContext.Apps);

        var explicitContext = new TaskAttachmentContext();
        explicitContext.AttachApp(app);
        explicitContext.AttachCapability(capability, app);

        Assert.True(explicitContext.RemoveCapability(capability.Id));
        Assert.Single(explicitContext.Apps);
    }

    [Fact]
    public void SnapshotTransferPreservesImplicitAndExplicitAppOwnership()
    {
        var tasks = CreateApp("tasks", "Tasks");
        var studio = CreateApp("studio", "Studio");
        var capability = CapabilityRegistryCatalog.BuiltIns.Single(item => item.Key == "run-task");
        var source = new TaskAttachmentContext();
        source.AttachApp(studio);
        source.AttachCapability(capability, tasks);

        var target = new TaskAttachmentContext();
        target.AttachSnapshot(source.TakeSnapshot());
        target.RemoveCapability(capability.Id);

        Assert.Single(target.Apps);
        Assert.Equal("studio", target.Apps[0].Key);
    }

    private static ModeDefinition CreateApp(string key, string name)
    {
        var now = DateTimeOffset.UtcNow;
        return new ModeDefinition(
            Guid.NewGuid(), key, name, $"{name} app", key,
            HavenMode.Tasks, "[]", "[]", "[]", "[]", string.Empty,
            ModeSource.BuiltIn, ModeInstallState.BuiltIn, "Haven", "1.0.0", "[]", now, now);
    }
}
