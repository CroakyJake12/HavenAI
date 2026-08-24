using Haven.Application.Automations;
using Haven.Core;
using Haven.Desktop.Views.Pages.Automations;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class AutomationGraphEditorAdapterTests
{
    [Fact]
    public void Round_trip_preserves_layout_ports_branches_parameters_and_device_configuration()
    {
        var conditionId = Guid.NewGuid(); var deviceId = Guid.NewGuid(); var edgeId = Guid.NewGuid();
        var target = new DeviceTargetDescriptor("current", "This PC", CapabilityPlatform.Windows, DeviceTargetKind.CurrentDevice, "test.device");
        var condition = new AutomationGraphNodeDefinition(conditionId, "Condition", null, null, new Dictionary<string, string> { ["expression"] = "true" }) { X = 40, Y = 80, Width = 240, Height = 130, Title = "Check condition", Subtitle = "Routes true or false", Ports = [new("in", "In", AutomationGraphPortDirection.Input, "flow", false), new("true", "True", AutomationGraphPortDirection.Output, "flow", true), new("false", "False", AutomationGraphPortDirection.Output, "flow", true)] };
        var device = AutomationGraphNodeDefinition.FromDevice(new DeviceAutomationNodeDefinition(deviceId, target, "ui.snapshot", new Dictionary<string, string> { ["quality"] = "high" })) with { X = 380, Y = 80 };
        var edge = new AutomationGraphEdgeDefinition(conditionId, deviceId) { Id = edgeId, FromPortId = "true", ToPortId = "in", Branch = "true", Label = "matched" };
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [condition, device], [edge]); var roundTrip = AutomationGraphEditorAdapter.FromEditor(AutomationGraphEditorAdapter.ToEditor(graph), graph);
        var restoredCondition = roundTrip.Nodes.Single(node => node.Id == conditionId); Assert.Equal(40, restoredCondition.X); Assert.Equal(240, restoredCondition.Width); Assert.Equal("true", restoredCondition.Parameters["expression"]); Assert.Contains(restoredCondition.Ports, port => port.Id == "true" && port.Direction == AutomationGraphPortDirection.Output);
        var restoredDevice = roundTrip.Nodes.Single(node => node.Id == deviceId); Assert.Equal(target, restoredDevice.DeviceTarget); Assert.Equal("ui.snapshot", restoredDevice.ActionKey); Assert.Equal("high", restoredDevice.Parameters["quality"]);
        var restoredEdge = Assert.Single(roundTrip.Edges); Assert.Equal(edgeId, restoredEdge.Id); Assert.Equal("true", restoredEdge.FromPortId); Assert.Equal("true", restoredEdge.Branch); Assert.Equal("matched", restoredEdge.Label);
    }

    [Fact]
    public void Palette_exposes_trigger_schedule_recurrence_watch_condition_branch_and_device_families()
    {
        var templates = AutomationGraphEditorAdapter.Templates; Assert.Contains(templates, template => template.Category == "Trigger"); Assert.Contains(templates, template => template.Category == "Schedule" && template.Title == "Schedule"); Assert.Contains(templates, template => template.Category == "Schedule" && template.Title == "Recurrence"); var watch = Assert.Single(templates, template => template.Category == "ConditionWatch"); Assert.Equal("60", watch.Metadata!["parameter.intervalMinutes"]); Assert.Contains(templates, template => template.Category == "Condition"); Assert.Contains(templates, template => template.Category == "Branch"); Assert.Contains(templates, template => template.Category == BuiltInAutomationNodeCategory.App && template.Title == "Launch app"); Assert.Contains(templates, template => template.Category == BuiltInAutomationNodeCategory.File && template.Title == "Read file"); Assert.Contains(templates, template => template.Category == BuiltInAutomationNodeCategory.File && template.Title == "Search files"); Assert.Contains(templates, template => template.Category == BuiltInAutomationNodeCategory.Action && template.Title == "Emit value"); Assert.Contains(templates, template => template.Category == BuiltInAutomationNodeCategory.Action && template.Title == "Delay"); Assert.Contains(templates, template => template.Category == DeviceAutomationNodeCategory.Key);
    }

    [Fact]
    public void Condition_edges_infer_branch_from_true_false_ports()
    {
        var condition = AutomationGraphEditorAdapter.Templates.Single(template => template.Category == "Condition"); var action = AutomationGraphEditorAdapter.Templates.Single(template => template.Category == DeviceAutomationNodeCategory.Key);
        var conditionNode = new NodeEditorNode(Guid.NewGuid(), condition.Category, condition.Title) { Ports = condition.Ports, Metadata = condition.Metadata ?? new Dictionary<string, string>() }; var actionNode = new NodeEditorNode(Guid.NewGuid(), action.Category, action.Title) { Ports = action.Ports, Metadata = action.Metadata ?? new Dictionary<string, string>() };
        var graph = AutomationGraphEditorAdapter.FromEditor(new NodeEditorDocument([conditionNode, actionNode], [new NodeEditorEdge(Guid.NewGuid(), conditionNode.Id, "false", actionNode.Id, "in")])); Assert.Equal("false", Assert.Single(graph.Edges).Branch);
    }
}
