using Haven.Application;
using Haven.Desktop.Views.Pages.Spaces;

namespace Haven.Desktop.Tests;

public sealed class SpaceGeneratedSurfaceRendererTests
{
    [Fact]
    public void Checklist_space_renders_through_trusted_genui_and_dispose_removes_instance()
    {
        var instances = new GenUiInstanceStore();
        var localActions = new GenUiLocalActionRegistry();
        var renderer = CreateRenderer(instances, localActions);
        var spaceId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var space = new SpaceDefinition(
            spaceId, "Launch checklist", string.Empty, "check", SpaceKind.General, false, false, null, string.Empty,
            SpaceThinkingMode.Default, [], [], new SpaceGeneratedSurface("checklist", "{\"items\":[\"Read brief\",\"Review evidence\"]}"), now, now);

        var mount = renderer.Render(space);
        var instanceId = mount.InstanceId;
        var document = instances.TryGet(instanceId);

        Assert.NotNull(document);
        Assert.Equal(spaceId, document!.Origin.ThreadId);
        Assert.Equal($"space:{spaceId:N}", document.AccentKey);
        Assert.Equal("Checklist", document.Title);
        Assert.Contains(document.Root.Children, component => component.ComponentId == "checklist.item.0");
        Assert.NotEmpty(mount.Root.Children);

        mount.Dispose();
        Assert.Null(instances.TryGet(instanceId));
    }

    [Fact]
    public void Inputs_are_bounded_and_must_be_a_json_object()
    {
        Assert.Throws<InvalidOperationException>(() => SpaceGeneratedSurfaceRenderer.ParseInputs("[]"));
        Assert.Throws<InvalidOperationException>(() => SpaceGeneratedSurfaceRenderer.ParseInputs("{\"payload\":\"" + new string('x', 8_300) + "\"}"));

        var parsed = SpaceGeneratedSurfaceRenderer.ParseInputs("{\"items\":[\"One\"]}");
        Assert.True(parsed.ContainsKey("items"));
    }

    private static SpaceGeneratedSurfaceRenderer CreateRenderer(GenUiInstanceStore instances, GenUiLocalActionRegistry localActions)
    {
        var router = new GenerativeUiEventRouter([localActions], new BoundedGenUiEventAuditSink(), instances);
        return new SpaceGeneratedSurfaceRenderer(
            router,
            instances,
            new ChecklistTemplateRuntime(localActions, instances),
            new DataGridTemplateRuntime(localActions),
            new CardDeckTemplateRuntime(localActions, instances),
            new DashboardTemplateRuntime(localActions),
            new AssessmentTemplateRuntime(localActions),
            new WorkflowTemplateRuntime(localActions),
            new CustomTemplateRuntime(localActions, instances));
    }
}
