using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.HavenUI.Components;
using Haven.Desktop.HavenUI.Tokens;
using Haven.Desktop.Views.Pages.Automations;
using Haven.Desktop.Views.Shell.NativePresentation;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class NativeAutomationsPageTests
{
    [Fact]
    public void Scene_is_pure_haven_ui_and_workflow_actions_have_semantic_events()
    {
        using var scene = new AutomationsHavenScene();
        var id = Guid.NewGuid();
        scene.SetDashboardData([new AutomationsWorkflowCard(id, "Nightly review", "Review the project", true, false, string.Empty)], [], [], []);

        Guid? run = null;
        Guid? test = null;
        Guid? edit = null;
        Guid? delete = null;
        (Guid Id, bool Enabled)? enabled = null;
        scene.RunWorkflowRequested += value => run = value;
        scene.TestWorkflowRequested += value => test = value;
        scene.EditWorkflowRequested += value => edit = value;
        scene.DeleteWorkflowRequested += value => delete = value;
        scene.SetWorkflowEnabledRequested += (value, state) => enabled = (value, state);

        Invoke(scene.Root, "Automations.Tab.Library");
        Invoke(scene.Root, $"Automations.Workflow.{id:N}.Run");
        Invoke(scene.Root, $"Automations.Workflow.{id:N}.Test");
        Invoke(scene.Root, $"Automations.Workflow.{id:N}.Edit");
        Invoke(scene.Root, $"Automations.Workflow.{id:N}.Enabled");
        Invoke(scene.Root, $"Automations.Workflow.{id:N}.Delete");

        Assert.Equal(id, run);
        Assert.Equal(id, test);
        Assert.Equal(id, edit);
        Assert.Equal(id, delete);
        Assert.Equal((id, false), enabled);
        Assert.Contains(scene.Root.DescendantsAndSelf(), element => element is NodeEditor && element.Name == "Automations.Graph");
        Assert.All(scene.Root.DescendantsAndSelf(), element =>
            Assert.False(element.GetType().Namespace?.StartsWith("Avalonia", StringComparison.Ordinal) == true));
    }

    [AvaloniaFact]
    public async Task Native_route_hosts_exactly_one_haven_scene_and_create_test_save_reopen_uses_that_scene()
    {
        HavenUiResourceApplier.Apply(SurfacePaletteCatalog.For(HavenSurface.Automations, HavenUiAppearance.SuperDark));
        var tasks = new MemoryWorkspaceStateRepository();
        var automations = new MemoryAutomationRepository();
        using var page = new NativeAutomationsPage(tasks, automations, null, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new Window { Width = 1440, Height = 900, Content = page };

        try
        {
            window.Show();
            await PumpAsync();

            Assert.Same(page.SceneHost, page.Content);
            Assert.Same(page.Scene.Root, page.SceneHost.Root);
            Assert.Single(page.GetVisualDescendants().OfType<HavenSceneControl>());
            Assert.Single(page.SceneHost.Children);
            Assert.All(page.Scene.Root.DescendantsAndSelf(), element =>
                Assert.False(element.GetType().Namespace?.StartsWith("Avalonia", StringComparison.Ordinal) == true));

            Invoke(page.Scene.Root, "Automations.New");
            Input(page.Scene.Root, "Automations.Editor.Name").Text = "Worker 05 native acceptance";
            Input(page.Scene.Root, "Automations.Editor.Goal").Text = "Prove the native graph workflow persists.";
            Input(page.Scene.Root, "Automations.Editor.Rules").Text = "Keep stable graph IDs.";

            var trigger = new NodeEditorTemplate(
                "Trigger",
                "Manual trigger",
                "Starts the workflow on demand.",
                [new NodeEditorPort("out", "Out", NodeEditorPortDirection.Output, "flow", true)]);
            var emit = new NodeEditorTemplate(
                BuiltInAutomationNodeCategory.Action,
                "Emit value",
                "Emits a deterministic value.",
                [
                    new NodeEditorPort("in", "In", NodeEditorPortDirection.Input, "flow", false),
                    new NodeEditorPort("out", "Out", NodeEditorPortDirection.Output, "flow", true)
                ],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["parameter.action"] = "emit",
                    ["parameter.value"] = "ready"
                });

            var triggerId = page.Scene.GraphEditor.AddNode(trigger, 80, 100);
            var emitId = page.Scene.GraphEditor.AddNode(emit, 360, 100);
            Assert.True(page.Scene.GraphEditor.Connect(triggerId, "out", emitId, "in"));

            Invoke(page.Scene.Root, "Automations.Editor.Test");
            await WaitUntilAsync(() => page.Scene.StatusText.Content.Contains("Test passed: 2 nodes traced without external side effects.", StringComparison.Ordinal));
            Assert.Contains(page.Scene.Root.DescendantsAndSelf().OfType<Haven.UI.Components.Text>(),
                text => text.Content.Contains("Succeeded: Manual trigger", StringComparison.Ordinal));
            Assert.Contains(page.Scene.Root.DescendantsAndSelf().OfType<Haven.UI.Components.Text>(),
                text => text.Content.Contains("Succeeded: Emit value", StringComparison.Ordinal));

            Invoke(page.Scene.Root, "Automations.Editor.Save");
            await WaitUntilAsync(() => tasks.Items.Any(item => item.Name == "Worker 05 native acceptance"));
            var saved = Assert.Single(tasks.Items, item => item.Name == "Worker 05 native acceptance");
            Assert.True(AutomationGraphCodec.TryDeserialize(saved.GraphJson, out var persisted));
            Assert.Equal(2, persisted.Nodes.Count);
            Assert.Single(persisted.Edges);
            Assert.Contains(persisted.Nodes, node => node.Id == triggerId);
            Assert.Contains(persisted.Nodes, node => node.Id == emitId);

            Invoke(page.Scene.Root, "Automations.Tab.Library");
            await WaitUntilAsync(() => page.Scene.Root.DescendantsAndSelf().Any(element => element.Name == $"Automations.Workflow.{saved.Id:N}.Edit"));
            Invoke(page.Scene.Root, $"Automations.Workflow.{saved.Id:N}.Edit");

            Assert.Equal("Worker 05 native acceptance", Input(page.Scene.Root, "Automations.Editor.Name").Text);
            Assert.Equal(2, page.Scene.GraphEditor.Document.Nodes.Count);
            Assert.Single(page.Scene.GraphEditor.Document.Edges);
            Assert.Contains(page.Scene.GraphEditor.Document.Nodes, node => node.Id == triggerId);
            Assert.Contains(page.Scene.GraphEditor.Document.Nodes, node => node.Id == emitId);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Pause_and_resume_persist_workflow_and_linked_schedule_without_substitution()
    {
        HavenUiResourceApplier.Apply(SurfacePaletteCatalog.For(HavenSurface.Automations, HavenUiAppearance.SuperDark));
        var now = DateTimeOffset.UtcNow;
        var workflowId = Guid.NewGuid();
        var workflow = new ReusableTaskDefinition(workflowId, "Scheduled review", "Review", "Review it", null, true, now, now, null);
        var graphJson = AutomationGraphCodec.Serialize(AutomationGraphDefinition.Empty);
        var payload = ScheduledGraphAutomationPayloadCodec.Serialize(workflowId, Guid.NewGuid(), workflow.Name, graphJson, null);
        var scheduled = new AutomationDefinition(
            workflowId, workflow.Name, HavenMode.Tasks, payload, AutomationScheduleKind.Hourly, "{\"intervalHours\":1}",
            now.AddHours(1), null, true, now, now);
        var tasks = new MemoryWorkspaceStateRepository([workflow]);
        var automations = new MemoryAutomationRepository([scheduled]);
        using var page = new NativeAutomationsPage(tasks, automations, null, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new Window { Width = 1200, Height = 800, Content = page };

        try
        {
            window.Show();
            await WaitUntilAsync(() => page.Scene.StatusText.Content.Contains("1 reusable workflow", StringComparison.Ordinal));
            Invoke(page.Scene.Root, "Automations.Tab.Library");
            Invoke(page.Scene.Root, $"Automations.Workflow.{workflowId:N}.Enabled");

            await WaitUntilAsync(() => tasks.Items.Single().IsEnabled == false && automations.Items.Single().IsEnabled == false);
            Assert.Null(automations.Items.Single().NextRunAt);
            Assert.Contains("Paused Scheduled review", page.Scene.StatusText.Content, StringComparison.Ordinal);

            Invoke(page.Scene.Root, $"Automations.Workflow.{workflowId:N}.Enabled");
            await WaitUntilAsync(() => tasks.Items.Single().IsEnabled && automations.Items.Single().IsEnabled);
            Assert.NotNull(automations.Items.Single().NextRunAt);
            Assert.Contains("Resumed Scheduled review", page.Scene.StatusText.Content, StringComparison.Ordinal);
        }
        finally
        {
            window.Close();
        }
    }

    private static Input Input(HavenElement root, string name) =>
        Assert.Single(root.DescendantsAndSelf().OfType<Input>(), input => input.Name == name);

    private static void Invoke(HavenElement root, string name)
    {
        var element = Assert.Single(root.DescendantsAndSelf(), candidate => candidate.Name == name);
        var method = typeof(HavenElement).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(element, null);
    }

    private static async Task PumpAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { });
        await Task.Delay(10);
        await Dispatcher.UIThread.InvokeAsync(() => { });
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(4);
        while (!condition() && DateTime.UtcNow < deadline) await PumpAsync();
        Assert.True(condition());
    }

    private sealed class MemoryWorkspaceStateRepository : IWorkspaceStateRepository
    {
        private readonly List<ReusableTaskDefinition> _items;
        public MemoryWorkspaceStateRepository(IEnumerable<ReusableTaskDefinition>? items = null) => _items = items?.ToList() ?? [];
        public IReadOnlyList<ReusableTaskDefinition> Items => _items.ToArray();

        public Task<IReadOnlyList<ReusableTaskDefinition>> GetReusableTasksAsync(Guid? containerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReusableTaskDefinition>>(_items.Where(item => item.ContainerId == containerId).ToArray());
        public Task UpsertReusableTaskAsync(ReusableTaskDefinition task, CancellationToken cancellationToken)
        {
            _items.RemoveAll(item => item.Id == task.Id);
            _items.Add(task);
            return Task.CompletedTask;
        }
        public Task DeleteReusableTaskAsync(Guid id, CancellationToken cancellationToken)
        {
            _items.RemoveAll(item => item.Id == id);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<WorkspaceVersion>> GetVersionsAsync(Guid? containerId, string? relativePath, int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WorkspaceVersion>>([]);
        public Task AddVersionAsync(WorkspaceVersion version, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<DecisionRecord>> GetDecisionsAsync(Guid containerId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DecisionRecord>>([]);
        public Task UpsertDecisionAsync(DecisionRecord decision, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteDecisionAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class MemoryAutomationRepository : IAutomationRepository
    {
        private readonly List<AutomationDefinition> _items;
        public MemoryAutomationRepository(IEnumerable<AutomationDefinition>? items = null) => _items = items?.ToList() ?? [];
        public IReadOnlyList<AutomationDefinition> Items => _items.ToArray();

        public Task<IReadOnlyList<AutomationDefinition>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AutomationDefinition>>(_items.ToArray());
        public Task<IReadOnlyList<AutomationDefinition>> GetDueAsync(DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AutomationDefinition>>(_items.Where(item => item.IsEnabled && item.NextRunAt <= now).ToArray());
        public Task UpsertAsync(AutomationDefinition automation, CancellationToken cancellationToken)
        {
            _items.RemoveAll(item => item.Id == automation.Id);
            _items.Add(automation);
            return Task.CompletedTask;
        }
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            _items.RemoveAll(item => item.Id == id);
            return Task.CompletedTask;
        }
        public Task<bool> TryAcquireLeaseAsync(Guid automationId, string leaseToken, DateTimeOffset leaseUntil, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task CompleteRunAsync(AutomationRun run, DateTimeOffset? nextRunAt, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<AutomationRun>> GetRunsAsync(Guid automationId, int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AutomationRun>>([]);
    }
}
