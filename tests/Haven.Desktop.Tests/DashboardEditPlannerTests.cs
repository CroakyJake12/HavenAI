using System.Runtime.CompilerServices;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Dashboard;
using Haven.Desktop.Services;

namespace Haven.Desktop.Tests;

public sealed class DashboardEditPlannerTests
{
    [Fact]
    public async Task Planner_accepts_only_bounded_operations_for_known_widgets()
    {
        var model = new FakeModelClient("""
            {"summary":"Make Plan prominent","operations":[
              {"action":"move","key":"plan","column":0,"row":0},
              {"action":"resize","key":"plan","width":6,"height":2},
              {"action":"hide","key":"browse"}
            ]}
            """);
        var planner = new DashboardEditPlanner(model, () => "test-model");
        var result = await planner.PlanAsync("Make plan the main widget", "Home", Views(), Layout(), CancellationToken.None);
        Assert.True(result.Succeeded, result.Message);
        Assert.NotNull(result.Plan);
        Assert.Equal(3, result.Plan.Operations.Count);
        Assert.Equal(6, result.Plan.Operations[1].Width);
        Assert.Equal("test-model", model.LastRequest?.Model);
    }

    [Fact]
    public void Parser_rejects_unknown_widgets_and_unsupported_actions()
    {
        var unknown = DashboardEditPlanner.ParseAndValidate(
            "{\"summary\":\"bad\",\"operations\":[{\"action\":\"move\",\"key\":\"missing\",\"column\":0,\"row\":0}]}", Views());
        Assert.False(unknown.Succeeded);

        var executable = DashboardEditPlanner.ParseAndValidate(
            "{\"summary\":\"bad\",\"operations\":[{\"action\":\"launch\",\"key\":\"plan\"}]}", Views());
        Assert.False(executable.Succeeded);
    }

    [Fact]
    public void Applier_combines_validated_operations_before_the_canvas_commit()
    {
        var definitions = Views().Select(view => view.Definition).ToArray();
        var plan = new DashboardEditPlan("Focus the page",
        [
            new DashboardEditOperation("move", "browse", Column: 0, Row: 3),
            new DashboardEditOperation("resize", "plan", Width: 6, Height: 2),
            new DashboardEditOperation("hide", "browse"),
            new DashboardEditOperation("rename-page", Title: "Today")
        ]);
        var applied = DashboardEditPlanApplier.Apply(plan, "Home", definitions, Layout());
        Assert.Equal("Today", applied.Title);
        Assert.Equal(6, applied.Layout.Single(item => item.Key == "plan").Width);
        Assert.False(applied.Layout.Single(item => item.Key == "browse").IsVisible);
        Assert.Equal(3, applied.Layout.Single(item => item.Key == "browse").Row);
    }

    [Fact]
    public async Task Planner_fails_cleanly_when_no_model_is_available()
    {
        var planner = new DashboardEditPlanner(new FakeModelClient("{}", hasModel: false), () => null);
        var result = await planner.PlanAsync("Move Plan", "Home", Views(), Layout(), CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Contains("No model", result.Message);
    }

    private static IReadOnlyList<DashboardWidgetViewState> Views() =>
    [
        new(new DashboardTileDefinition("plan", "Plan", "Tasks", "plan", "plan", "plan"), new DashboardTileData("2", "Due"), DashboardWidgetDataState.Ready),
        new(new DashboardTileDefinition("browse", "Browse", "Browser", "browse", "action", "browse"), new DashboardTileData("Open", "Private"), DashboardWidgetDataState.Ready)
    ];

    private static IReadOnlyList<DashboardWidgetPlacement> Layout() =>
    [
        new("plan", 0, 0, 3, 2),
        new("browse", 3, 0, 3, 2)
    ];

    private sealed class FakeModelClient(string response, bool hasModel = true) : IOllamaClient
    {
        public OllamaChatRequest? LastRequest { get; private set; }
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(hasModel);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ModelDescriptor>>(hasModel
                ? [new ModelDescriptor("test-model", 1, "test", "1B", "Q4", new HashSet<ToolCapability>(), DateTimeOffset.UtcNow)]
                : []);
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield break;
        }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(response);
        }
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new OllamaToolResponse(string.Empty, []));
    }
}
