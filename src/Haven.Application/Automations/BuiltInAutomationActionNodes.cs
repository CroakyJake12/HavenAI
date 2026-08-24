using System.Globalization;
using Haven.Core;

namespace Haven.Application.Automations;

public static class BuiltInAutomationNodeCategory
{
    public const string App = "App";
    public const string File = "File";
    public const string Action = "Action";
}

/// <summary>Executes the built-in non-DEVICE automation node families through existing Haven capability services.</summary>
public sealed class BuiltInAutomationActionNodeExecutor : IAutomationGraphNodeExecutor
{
    private const int MaximumTraceOutputCharacters = 4_000;
    private readonly DeviceActionRouter? _deviceActions;
    private readonly FilesystemActionService? _filesystem;
    private readonly bool _permissionGranted;

    public BuiltInAutomationActionNodeExecutor() : this(null, null, false) { }

    public BuiltInAutomationActionNodeExecutor(DeviceActionRouter deviceActions, FilesystemActionService filesystem)
        : this(deviceActions, filesystem, false)
    {
    }

    private BuiltInAutomationActionNodeExecutor(DeviceActionRouter? deviceActions, FilesystemActionService? filesystem, bool permissionGranted)
    {
        _deviceActions = deviceActions;
        _filesystem = filesystem;
        _permissionGranted = permissionGranted;
    }

    public BuiltInAutomationActionNodeExecutor WithPermission(bool permissionGranted) =>
        new(_deviceActions, _filesystem, permissionGranted);

    public bool CanExecute(AutomationGraphNodeDefinition node) =>
        IsCategory(node, BuiltInAutomationNodeCategory.App)
        || IsCategory(node, BuiltInAutomationNodeCategory.File)
        || IsCategory(node, BuiltInAutomationNodeCategory.Action);

    public async Task<AutomationGraphNodeExecutionResult> ExecuteAsync(AutomationGraphNodeExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var issue = ValidateConfiguration(context.Node).FirstOrDefault();
        if (issue is not null) return new(false, issue.Message);

        if (context.Mode == AutomationGraphRunMode.Test)
            return Preview(context.Node);

        if (IsCategory(context.Node, BuiltInAutomationNodeCategory.App))
            return await ExecuteAppAsync(context.Node, cancellationToken).ConfigureAwait(false);
        if (IsCategory(context.Node, BuiltInAutomationNodeCategory.File))
            return await ExecuteFileAsync(context.Node, cancellationToken).ConfigureAwait(false);
        return await ExecuteActionAsync(context.Node, cancellationToken).ConfigureAwait(false);
    }

    public static IReadOnlyList<AutomationGraphValidationIssue> ValidateConfiguration(AutomationGraphNodeDefinition node)
    {
        var issues = new List<AutomationGraphValidationIssue>();
        void Require(string key, string message)
        {
            if (string.IsNullOrWhiteSpace(Parameter(node, key)))
                issues.Add(new AutomationGraphValidationIssue($"{node.Category.ToLowerInvariant()}.{key}.required", message, node.Id));
        }

        if (IsCategory(node, BuiltInAutomationNodeCategory.App))
        {
            var action = Parameter(node, "action", "launch");
            if (!action.Equals("launch", StringComparison.OrdinalIgnoreCase))
                issues.Add(new AutomationGraphValidationIssue("app.action.unsupported", "App nodes currently support only the launch action.", node.Id));
            Require("name", "App launch needs an application name.");
        }
        else if (IsCategory(node, BuiltInAutomationNodeCategory.File))
        {
            var operation = Parameter(node, "operation", "read");
            if (operation.Equals("read", StringComparison.OrdinalIgnoreCase))
            {
                Require("workspaceRoot", "File read needs a workspace root.");
                Require("path", "File read needs a workspace-relative path.");
            }
            else if (operation.Equals("search", StringComparison.OrdinalIgnoreCase))
            {
                Require("workspaceRoot", "File search needs a workspace root.");
                Require("pattern", "File search needs a filename pattern.");
            }
            else
            {
                issues.Add(new AutomationGraphValidationIssue("file.operation.unsupported", "File nodes support read and search. Writes stay on Haven's explicit permissioned file-action path.", node.Id));
            }
        }
        else if (IsCategory(node, BuiltInAutomationNodeCategory.Action))
        {
            var action = Parameter(node, "action", "emit");
            if (action.Equals("delay", StringComparison.OrdinalIgnoreCase))
            {
                var text = Parameter(node, "milliseconds", "1000");
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds) || milliseconds is < 0 or > 60_000)
                    issues.Add(new AutomationGraphValidationIssue("action.delay.invalid", "Delay must be between 0 and 60000 milliseconds.", node.Id));
            }
            else if (!action.Equals("emit", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new AutomationGraphValidationIssue("action.kind.unsupported", "Action nodes support emit and delay.", node.Id));
            }
        }
        return issues;
    }

    private async Task<AutomationGraphNodeExecutionResult> ExecuteAppAsync(AutomationGraphNodeDefinition node, CancellationToken cancellationToken)
    {
        if (_deviceActions is null) return new(false, "The app-launch capability is unavailable. Haven did not perform a substitute action.");
        var platform = OperatingSystem.IsAndroid() ? CapabilityPlatform.Android : CapabilityPlatform.Windows;
        var target = new DeviceTargetDescriptor("current", "Current device", platform, DeviceTargetKind.CurrentDevice,
            platform == CapabilityPlatform.Windows ? WindowsComputerDeviceActionProvider.NativeProviderId : null);
        var request = new DeviceActionRequest(target, "applications.launch", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = Parameter(node, "name")
        }, _permissionGranted);
        var result = await _deviceActions.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        return new(result.Succeeded, result.Message, LimitOutput(result.Output));
    }

    private async Task<AutomationGraphNodeExecutionResult> ExecuteFileAsync(AutomationGraphNodeDefinition node, CancellationToken cancellationToken)
    {
        if (_filesystem is null) return new(false, "The workspace file capability is unavailable. Haven did not perform a substitute action.");
        var root = Parameter(node, "workspaceRoot");
        var operation = Parameter(node, "operation", "read");
        FilesystemActionResult result = operation.Equals("search", StringComparison.OrdinalIgnoreCase)
            ? await _filesystem.SearchFilesAsync(root, Parameter(node, "pattern"), cancellationToken).ConfigureAwait(false)
            : await _filesystem.ReadFileAsync(root, Parameter(node, "path"), cancellationToken).ConfigureAwait(false);
        return new(result.Succeeded, result.Message, LimitOutput(result.Content));
    }

    private static async Task<AutomationGraphNodeExecutionResult> ExecuteActionAsync(AutomationGraphNodeDefinition node, CancellationToken cancellationToken)
    {
        var action = Parameter(node, "action", "emit");
        if (action.Equals("delay", StringComparison.OrdinalIgnoreCase))
        {
            var milliseconds = int.Parse(Parameter(node, "milliseconds", "1000"), CultureInfo.InvariantCulture);
            if (milliseconds > 0) await Task.Delay(milliseconds, cancellationToken).ConfigureAwait(false);
            return new(true, $"Delayed for {milliseconds} ms.", milliseconds.ToString(CultureInfo.InvariantCulture));
        }
        var value = Parameter(node, "value");
        return new(true, "Emitted the configured value.", LimitOutput(value));
    }

    private static AutomationGraphNodeExecutionResult Preview(AutomationGraphNodeDefinition node)
    {
        if (IsCategory(node, BuiltInAutomationNodeCategory.App))
            return new(true, $"Test mode would launch {Parameter(node, "name")} without opening the application.");
        if (IsCategory(node, BuiltInAutomationNodeCategory.File))
        {
            var operation = Parameter(node, "operation", "read");
            var target = operation.Equals("search", StringComparison.OrdinalIgnoreCase) ? Parameter(node, "pattern") : Parameter(node, "path");
            return new(true, $"Test mode would {operation} '{target}' inside the configured workspace without reading external data.");
        }
        var action = Parameter(node, "action", "emit");
        return action.Equals("delay", StringComparison.OrdinalIgnoreCase)
            ? new(true, $"Test mode would delay for {Parameter(node, "milliseconds", "1000")} ms without waiting.")
            : new(true, "Test mode would emit the configured value.", LimitOutput(Parameter(node, "value")));
    }

    private static bool IsCategory(AutomationGraphNodeDefinition node, string category) =>
        node.Category.Equals(category, StringComparison.OrdinalIgnoreCase);

    private static string Parameter(AutomationGraphNodeDefinition node, string key, string fallback = "") =>
        node.Parameters.TryGetValue(key, out var value) ? value?.Trim() ?? fallback : fallback;

    private static string? LimitOutput(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= MaximumTraceOutputCharacters ? value : value[..MaximumTraceOutputCharacters] + "…";
    }
}
