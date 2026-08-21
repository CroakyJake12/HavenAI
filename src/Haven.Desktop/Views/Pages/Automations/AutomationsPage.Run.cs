using Haven.Application.Automations;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Automations;

public sealed partial class AutomationsPage
{
    private readonly ReusableDeviceWorkflowRunner? _deviceWorkflowRunner;

    private async Task RunReusableAsync(ReusableTaskDefinition item)
    {
        if (_deviceWorkflowRunner is null)
        {
            if (string.IsNullOrWhiteSpace(item.GraphJson))
            {
                await InvokeAsync(item.Instruction);
                return;
            }

            _status.Text = "The graph runtime is unavailable. Haven did not route this graph to Tasks or perform a substitute instruction.";
            return;
        }

        _status.Text = "Running reusable workflow…";
        try
        {
            var run = await _deviceWorkflowRunner.RunAsync(item, permissionGranted: false, CancellationToken.None);
            if (!run.Handled)
            {
                await InvokeAsync(item.Instruction);
                return;
            }

            if (run.GraphResult is not null && !string.IsNullOrWhiteSpace(item.GraphJson))
                await RecordGraphHistoryAsync(item, item.GraphJson, run.GraphResult);
            _status.Text = FormatDeviceWorkflowRunStatus(run);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _status.Text = "The DEVICE workflow could not run: " + ex.Message;
        }
    }

    private static string FormatDeviceWorkflowRunStatus(ReusableDeviceWorkflowRunResult run)
    {
        if (run.Kind != ReusableDeviceWorkflowRunKind.DeviceAction || run.DeviceResult is null)
            return run.Message;

        var result = run.DeviceResult;
        var prefix = result.Status switch
        {
            DeviceActionResultStatus.Success => "DEVICE completed",
            DeviceActionResultStatus.Unsupported => "Unsupported",
            DeviceActionResultStatus.PermissionRequired => "Permission required",
            DeviceActionResultStatus.DeviceUnavailable => "Device unavailable",
            DeviceActionResultStatus.ConnectionLost => "Connection lost",
            DeviceActionResultStatus.ActionRejected => "Action rejected",
            DeviceActionResultStatus.PlatformError => "Platform error",
            _ => "DEVICE result"
        };

        return string.IsNullOrWhiteSpace(result.Output)
            ? $"{prefix}: {result.Message}"
            : $"{prefix}: {result.Message} {result.Output}";
    }
}
