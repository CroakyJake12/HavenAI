using Avalonia.Automation;
using Avalonia.Controls;
using AvaloniaButton = Avalonia.Controls.Button;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
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
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class AutomationsPageEndToEndTests
{
    [AvaloniaFact]
    public async Task Create_test_save_and_reopen_graph_workflow_through_real_page()
    {
        HavenUiResourceApplier.Apply(SurfacePaletteCatalog.For(HavenSurface.Automations, HavenUiAppearance.SuperDark));
        var tasks = new MemoryWorkspaceStateRepository();
        var automations = new EmptyAutomationRepository();
        var page = new AutomationsPage(
            tasks,
            automations,
            null,
            () => Task.CompletedTask,
            _ => Task.CompletedTask);
        var window = new Window { Width = 1440, Height = 900, Content = page };

        try
        {
            window.Show();
            await PumpAsync();

            Click(ButtonWithText(page, "+ New Workflow"));
            await PumpAsync();

            TextInput(page, "Workflow Name").Text = "Worker 05 acceptance";
            TextInput(page, "Your Desired Outcome").Text = "Prove the graph workflow persists.";
            TextInput(page, "Define rules that must be followed.").Text = "Keep stable graph IDs.";

            var graphHost = Assert.Single(
                page.GetVisualDescendants().OfType<HavenSceneControl>(),
                control => control.Root is NodeEditor);
            var editor = Assert.IsType<NodeEditor>(graphHost.Root);

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

            var triggerId = editor.AddNode(trigger, 80, 100);
            var emitId = editor.AddNode(emit, 360, 100);
            Assert.True(editor.Connect(triggerId, "out", emitId, "in"));

            Click(ButtonWithText(page, "Test Graph"));
            await WaitUntilAsync(() => VisibleText(page, "Test passed: 2 nodes traced without external side effects."));
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(),
                text => text.IsVisible && text.Text?.Contains("Succeeded: Manual trigger", StringComparison.Ordinal) == true);
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(),
                text => text.IsVisible && text.Text?.Contains("Succeeded: Emit value", StringComparison.Ordinal) == true);

            Click(ButtonWithText(page, "Save Changes"));
            var saved = await tasks.Saved.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal("Worker 05 acceptance", saved.Name);
            Assert.True(AutomationGraphCodec.TryDeserialize(saved.GraphJson, out var persisted));
            Assert.Equal(2, persisted.Nodes.Count);
            Assert.Single(persisted.Edges);
            Assert.Equal(triggerId, persisted.Nodes.Single(node => node.Category == "Trigger").Id);
            Assert.Equal(emitId, persisted.Nodes.Single(node => node.Category == BuiltInAutomationNodeCategory.Action).Id);
            Assert.Equal("ready", persisted.Nodes.Single(node => node.Id == emitId).Parameters["value"]);

            await WaitUntilAsync(() => page.GetVisualDescendants().OfType<HavenNavigationButton>()
                .Any(button => Equals(button.Tag, saved.Name)));

            Click(ButtonWithText(page, "Reusable Workflows"));
            await PumpAsync();

            var savedRow = Assert.Single(page.GetVisualDescendants().OfType<HavenNavigationButton>(),
                button => Equals(button.Tag, saved.Name));
            Click(savedRow);
            await WaitUntilAsync(() => page.GetVisualDescendants().OfType<TextBlock>()
                .Any(text => text.IsVisible && text.Text == "Edit Workflow"));

            Assert.Equal("Worker 05 acceptance", TextInput(page, "Workflow Name").Text);
            Assert.Equal(2, editor.Document.Nodes.Count);
            Assert.Single(editor.Document.Edges);
            Assert.Contains(editor.Document.Nodes, node => node.Id == triggerId);
            Assert.Contains(editor.Document.Nodes, node => node.Id == emitId);
        }
        finally
        {
            window.Close();
        }
    }

    private static HavenTextInput TextInput(Control root, string placeholder) =>
        Assert.Single(root.GetVisualDescendants().OfType<HavenTextInput>(),
            input => input.PlaceholderText == placeholder);

    private static AvaloniaButton ButtonWithText(Control root, string text) =>
        Assert.Single(root.GetVisualDescendants().OfType<AvaloniaButton>(),
            button => Equals(button.Content, text));

    private static void Click(AvaloniaButton button) =>
        button.RaiseEvent(new RoutedEventArgs(AvaloniaButton.ClickEvent));

    private static bool VisibleText(Control root, string expected) =>
        root.GetVisualDescendants().OfType<TextBlock>()
            .Any(text => text.IsVisible && string.Equals(text.Text, expected, StringComparison.Ordinal));

    private static async Task PumpAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { });
        await Task.Delay(10);
        await Dispatcher.UIThread.InvokeAsync(() => { });
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!condition() && DateTime.UtcNow < deadline)
            await PumpAsync();
        Assert.True(condition());
    }

    private sealed class MemoryWorkspaceStateRepository : IWorkspaceStateRepository
    {
        private readonly List<ReusableTaskDefinition> _items = [];
        public TaskCompletionSource<ReusableTaskDefinition> Saved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<ReusableTaskDefinition>> GetReusableTasksAsync(Guid? containerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReusableTaskDefinition>>(_items.ToArray());

        public Task UpsertReusableTaskAsync(ReusableTaskDefinition task, CancellationToken cancellationToken)
        {
            _items.RemoveAll(item => item.Id == task.Id);
            _items.Add(task);
            Saved.TrySetResult(task);
            return Task.CompletedTask;
        }

        public Task DeleteReusableTaskAsync(Guid id, CancellationToken cancellationToken)
        {
            _items.RemoveAll(item => item.Id == id);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<WorkspaceVersion>> GetVersionsAsync(Guid? containerId, string? relativePath, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WorkspaceVersion>>([]);
        public Task AddVersionAsync(WorkspaceVersion version, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<DecisionRecord>> GetDecisionsAsync(Guid containerId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DecisionRecord>>([]);
        public Task UpsertDecisionAsync(DecisionRecord decision, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteDecisionAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class EmptyAutomationRepository : IAutomationRepository
    {
        public Task<IReadOnlyList<AutomationDefinition>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AutomationDefinition>>([]);
        public Task<IReadOnlyList<AutomationDefinition>> GetDueAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AutomationDefinition>>([]);
        public Task UpsertAsync(AutomationDefinition automation, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> TryAcquireLeaseAsync(Guid automationId, string leaseToken, DateTimeOffset leaseUntil, CancellationToken cancellationToken) =>
            Task.FromResult(false);
        public Task CompleteRunAsync(AutomationRun run, DateTimeOffset? nextRunAt, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<AutomationRun>> GetRunsAsync(Guid automationId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AutomationRun>>([]);
    }
}
