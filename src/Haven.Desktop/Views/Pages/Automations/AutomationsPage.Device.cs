using Haven.Application.Automations;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Automations;

public sealed partial class AutomationsPage
{
    private sealed record DeviceTargetChoice(DeviceTargetDescriptor Target)
    {
        public override string ToString() => Target.DisplayName;
    }

    private sealed record DeviceActionChoice(DeviceActionDescriptor Action)
    {
        public override string ToString() => $"{Action.Name} — {Action.Availability}";
    }

    private void ConfigureDeviceEditor()
    {
        if (_deviceEditor.Children.Count > 0) return;

        _deviceEditor.Children.Add(Label("Target device"));
        _deviceEditor.Children.Add(_deviceTarget);
        _deviceEditor.Children.Add(Label("Device action"));
        _deviceEditor.Children.Add(_deviceAction);
        _deviceEditor.Children.Add(_deviceAvailability);
        _deviceEditor.Children.Add(_deviceParameters);

        var targets = new List<DeviceTargetChoice>();
        if (_deviceActions is not null && OperatingSystem.IsWindows())
        {
            targets.Add(new DeviceTargetChoice(new DeviceTargetDescriptor(
                "current",
                "This PC",
                CapabilityPlatform.Windows,
                DeviceTargetKind.CurrentDevice)));
        }

        _deviceTarget.ItemsSource = targets;
        if (targets.Count == 0)
            _deviceAvailability.Text = "No real device-action target is available in this host.";

        _workflowType.SelectionChanged += (_, _) =>
        {
            var isDevice = string.Equals(_workflowType.SelectedItem as string, DeviceAutomationNodeCategory.Key, StringComparison.OrdinalIgnoreCase);
            _deviceEditor.IsVisible = isDevice;
            if (isDevice && _deviceTarget.SelectedItem is null && targets.Count > 0)
                _deviceTarget.SelectedIndex = 0;
        };

        _deviceTarget.SelectionChanged += async (_, _) =>
        {
            if (_deviceTarget.SelectedItem is DeviceTargetChoice choice)
                await LoadDeviceSnapshotAsync(choice.Target);
        };

        _deviceAction.SelectionChanged += (_, _) => ConfigureDeviceParameters();
    }

    private async Task LoadDeviceSnapshotAsync(DeviceTargetDescriptor target)
    {
        _deviceSnapshot = null;
        _deviceAction.ItemsSource = Array.Empty<DeviceActionChoice>();
        _deviceAction.SelectedItem = null;
        _deviceParameters.Children.Clear();
        _deviceParameterInputs.Clear();

        if (_deviceActions is null)
        {
            _deviceAvailability.Text = "Device actions are unavailable in this host.";
            return;
        }

        _deviceAvailability.Text = $"Checking capabilities on {target.DisplayName}…";
        try
        {
            var snapshot = await _deviceActions.GetSnapshotAsync(target, CancellationToken.None);
            if (_deviceTarget.SelectedItem is not DeviceTargetChoice current ||
                !string.Equals(current.Target.Id, target.Id, StringComparison.OrdinalIgnoreCase))
                return;

            _deviceSnapshot = snapshot;
            var choices = snapshot.Actions.Select(action => new DeviceActionChoice(action)).ToArray();
            _deviceAction.ItemsSource = choices;

            var preferredKey = _editingDeviceNode is { } editing &&
                               string.Equals(editing.Target.Id, target.Id, StringComparison.OrdinalIgnoreCase)
                ? editing.ActionKey
                : null;
            var preferred = choices.FirstOrDefault(choice =>
                string.Equals(choice.Action.Key, preferredKey, StringComparison.OrdinalIgnoreCase));

            if (preferred is not null)
                _deviceAction.SelectedItem = preferred;
            else if (choices.Length > 0)
                _deviceAction.SelectedIndex = 0;
            else
                _deviceAvailability.Text = snapshot.IsReachable
                    ? "This target exposes no DEVICE actions."
                    : $"{target.DisplayName} is currently unavailable.";
        }
        catch (Exception ex)
        {
            _deviceAvailability.Text = $"Capability discovery failed: {ex.Message}";
        }
    }

    private void ConfigureDeviceParameters()
    {
        _deviceParameters.Children.Clear();
        _deviceParameterInputs.Clear();

        if (_deviceAction.SelectedItem is not DeviceActionChoice choice)
        {
            if (_deviceSnapshot is not null)
                _deviceAvailability.Text = _deviceSnapshot.IsReachable
                    ? "Choose an action."
                    : $"{_deviceSnapshot.Target.DisplayName} is currently unavailable.";
            return;
        }

        var action = choice.Action;
        var targetName = _deviceSnapshot?.Target.DisplayName ?? "the selected device";
        _deviceAvailability.Text = action.Availability switch
        {
            DeviceActionAvailability.Supported => $"Supported on {targetName}.",
            DeviceActionAvailability.PermissionRequired => $"Available on {targetName} with permission. Haven will request permission before execution.",
            DeviceActionAvailability.AvailableThroughPlugin => $"Available on {targetName} through {action.ProviderId}.",
            DeviceActionAvailability.Unsupported => $"Unsupported on {targetName}. Haven will not execute or save this DEVICE action.",
            _ => $"Availability for this action on {targetName} could not be resolved."
        };

        foreach (var parameter in action.RequiredParameters)
        {
            var input = Field(parameter);
            if (_editingDeviceNode is { } editing &&
                string.Equals(editing.ActionKey, action.Key, StringComparison.OrdinalIgnoreCase) &&
                editing.Parameters.TryGetValue(parameter, out var value))
                input.Text = value;

            _deviceParameterInputs[parameter] = input;
            _deviceParameters.Children.Add(Label(parameter));
            _deviceParameters.Children.Add(input);
        }

        if (action.RequiredParameters.Count == 0)
            _deviceParameters.Children.Add(Muted("No parameters required."));
    }

    private void HydrateDeviceEditor(ReusableTaskDefinition? item)
    {
        _editingGraph = AutomationGraphDefinition.Empty;
        _editingDeviceNode = null;
        _deviceSnapshot = null;
        _deviceAction.ItemsSource = Array.Empty<DeviceActionChoice>();
        _deviceAction.SelectedItem = null;
        _deviceTarget.SelectedItem = null;
        _deviceParameters.Children.Clear();
        _deviceParameterInputs.Clear();

        if (!AutomationGraphCodec.TryDeserialize(item?.GraphJson, out var graph))
        {
            _workflowType.SelectedItem = "Instruction";
            _deviceEditor.IsVisible = false;
            _deviceAvailability.Text = "Stored graph data could not be read. It will be preserved unless you choose DEVICE.";
            return;
        }

        _editingGraph = graph;
        _editingDeviceNode = graph.Nodes.Select(node => node.ToDevice()).FirstOrDefault(node => node is not null);
        if (_editingDeviceNode is null)
        {
            _workflowType.SelectedItem = "Instruction";
            _deviceEditor.IsVisible = false;
            return;
        }

        _workflowType.SelectedItem = DeviceAutomationNodeCategory.Key;
        _deviceEditor.IsVisible = true;

        var choices = (_deviceTarget.ItemsSource as IEnumerable<DeviceTargetChoice>)?.ToArray() ?? [];
        var targetChoice = choices.FirstOrDefault(choice =>
            string.Equals(choice.Target.Id, _editingDeviceNode.Target.Id, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(_editingDeviceNode.Target.ProviderId) ||
             string.Equals(choice.Target.ProviderId, _editingDeviceNode.Target.ProviderId, StringComparison.OrdinalIgnoreCase)));

        if (targetChoice is null)
        {
            _deviceAvailability.Text = $"Saved target {_editingDeviceNode.Target.DisplayName} is not currently available. Haven will not substitute another device.";
            return;
        }

        _deviceTarget.SelectedItem = targetChoice;
    }

    private bool TryBuildGraphJson(out string? graphJson, out string? error)
    {
        graphJson = null;
        error = null;

        var isDevice = string.Equals(
            _workflowType.SelectedItem as string,
            DeviceAutomationNodeCategory.Key,
            StringComparison.OrdinalIgnoreCase);

        if (!isDevice)
        {
            if (_editingDeviceNode is null)
            {
                graphJson = _editing?.GraphJson;
                return true;
            }

            var nodes = _editingGraph.Nodes
                .Where(node => node.Id != _editingDeviceNode.Id)
                .ToArray();
            var edges = _editingGraph.Edges
                .Where(edge => edge.FromNodeId != _editingDeviceNode.Id && edge.ToNodeId != _editingDeviceNode.Id)
                .ToArray();

            graphJson = nodes.Length == 0 && edges.Length == 0
                ? null
                : AutomationGraphCodec.Serialize(new AutomationGraphDefinition(
                    AutomationGraphDefinition.CurrentVersion,
                    nodes,
                    edges));
            return true;
        }

        if (_deviceTarget.SelectedItem is not DeviceTargetChoice targetChoice)
        {
            error = "Choose an available target device.";
            return false;
        }

        if (_deviceAction.SelectedItem is not DeviceActionChoice actionChoice)
        {
            error = "Choose a device action.";
            return false;
        }

        var action = actionChoice.Action;
        if (action.Availability is DeviceActionAvailability.Unsupported or DeviceActionAvailability.Unknown)
        {
            error = action.Availability == DeviceActionAvailability.Unsupported
                ? $"{action.Name} is not supported on {targetChoice.Target.DisplayName}."
                : $"Availability for {action.Name} on {targetChoice.Target.DisplayName} could not be resolved.";
            return false;
        }

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var required in action.RequiredParameters)
        {
            if (!_deviceParameterInputs.TryGetValue(required, out var input) ||
                string.IsNullOrWhiteSpace(input.Text))
            {
                error = $"Enter a value for {required}.";
                return false;
            }

            parameters[required] = input.Text.Trim();
        }

        var deviceNode = new DeviceAutomationNodeDefinition(
            _editingDeviceNode?.Id ?? Guid.NewGuid(),
            targetChoice.Target,
            action.Key,
            parameters);

        var nodesToSave = _editingGraph.Nodes
            .Where(node => _editingDeviceNode is null || node.Id != _editingDeviceNode.Id)
            .ToList();
        nodesToSave.Add(AutomationGraphNodeDefinition.FromDevice(deviceNode));

        graphJson = AutomationGraphCodec.Serialize(new AutomationGraphDefinition(
            AutomationGraphDefinition.CurrentVersion,
            nodesToSave,
            _editingGraph.Edges));
        return true;
    }
}
