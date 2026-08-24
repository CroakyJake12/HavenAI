using Haven.Core;
using Haven.Desktop.Dashboard;

namespace Haven.Desktop.Services;

internal sealed record DashboardAppliedEdit(
    string Title,
    IReadOnlyList<DashboardWidgetPlacement> Layout);

internal static class DashboardEditPlanApplier
{
    public static DashboardAppliedEdit Apply(
        DashboardEditPlan plan,
        string currentTitle,
        IReadOnlyList<DashboardTileDefinition> definitions,
        IReadOnlyList<DashboardWidgetPlacement> currentLayout)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var title = currentTitle;
        IReadOnlyList<DashboardWidgetPlacement> layout = DashboardWidgetLayoutEngine.EnsurePlacements(definitions, currentLayout);

        foreach (var operation in plan.Operations)
        {
            layout = operation.Action switch
            {
                "show" => DashboardWidgetLayoutEngine.SetVisibility(layout, operation.Key!, true),
                "hide" => DashboardWidgetLayoutEngine.SetVisibility(layout, operation.Key!, false),
                "move" => DashboardWidgetLayoutEngine.Move(layout, operation.Key!, operation.Column!.Value, operation.Row!.Value),
                "resize" => DashboardWidgetLayoutEngine.Resize(layout, operation.Key!, operation.Width!.Value, operation.Height!.Value),
                "reset-layout" => DashboardWidgetLayoutEngine.EnsurePlacements(definitions),
                "rename-page" => layout,
                _ => throw new InvalidOperationException($"Unsupported validated dashboard operation '{operation.Action}'.")
            };

            if (operation.Action == "rename-page")
                title = operation.Title!;
        }

        return new DashboardAppliedEdit(title, layout);
    }
}
