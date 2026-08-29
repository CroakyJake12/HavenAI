using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views;

internal sealed partial class ProjectHavenScene
{
    private HavenDevJourneyPresentationState? _havenDevState;

    public Container HavenDevPanel { get; private set; } = null!;
    public HavenText HavenDevEvidence { get; private set; } = null!;
    public HavenText HavenDevActivePath { get; private set; } = null!;
    public HavenText HavenDevExplorerItems { get; private set; } = null!;
    public HavenText HavenDevOutput { get; private set; } = null!;
    public HavenText HavenDevDeployStatus { get; private set; } = null!;
    public HavenButton HavenDevLogicalButton { get; private set; } = null!;
    public HavenButton HavenDevFilesystemButton { get; private set; } = null!;
    public HavenButton HavenDevDiagnosticButton { get; private set; } = null!;
    public HavenButton HavenDevDeployButton { get; private set; } = null!;

    public event Action<HavenDevExplorerMode>? HavenDevExplorerModeRequested;
    public event Action<HavenDevTool>? HavenDevToolRequested;
    public event Action<HavenDevDiagnosticPresentation>? HavenDevDiagnosticRequested;
    public event EventHandler? HavenDevDeployRequested;

    private void InitializeHavenDevJourney()
    {
        HavenDevPanel = Vertical("Project.Dev.Panel", 6);
        HavenDevPanel.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        HavenDevPanel.SetValue(HavenProperties.BorderColor, "Border");
        HavenDevPanel.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        HavenDevPanel.SetValue(HavenProperties.Padding, HavenThickness.Parse("8px 10px"));

        HavenDevEvidence = Muted("Project.Dev.Evidence", HavenDevJourneyPresentationState.SimulatedEvidenceLabel);
        HavenDevEvidence.Accessibility.AccessibleName = "Simulated evidence; not real AOSP validation";
        HavenDevPanel.Add(HavenDevEvidence);

        var views = Wrap("Project.Dev.Views", 4);
        HavenDevLogicalButton = Ghost("Project.Dev.View.Logical", "Logical", "");
        HavenDevLogicalButton.Accessibility.AccessibleName = "Show logical simulated workspace view";
        HavenDevFilesystemButton = Ghost("Project.Dev.View.Filesystem", "Filesystem", "");
        HavenDevFilesystemButton.Accessibility.AccessibleName = "Show filesystem simulated workspace view";
        views.Add(HavenDevLogicalButton);
        views.Add(HavenDevFilesystemButton);
        HavenDevPanel.Add(views);

        HavenDevActivePath = Muted("Project.Dev.ActivePath", "No simulated file selected");
        HavenDevPanel.Add(HavenDevActivePath);
        HavenDevExplorerItems = Muted("Project.Dev.Explorer.Items", string.Empty);
        HavenDevPanel.Add(HavenDevExplorerItems);

        var tools = Wrap("Project.Dev.Tools", 4);
        AddToolButton(tools, HavenDevTool.Build, "Build");
        AddToolButton(tools, HavenDevTool.Problems, "Problems");
        AddToolButton(tools, HavenDevTool.Device, "Device");
        AddToolButton(tools, HavenDevTool.Logcat, "Logcat");
        AddToolButton(tools, HavenDevTool.Tests, "Tests");
        AddToolButton(tools, HavenDevTool.Changes, "Changes");
        HavenDevPanel.Add(tools);

        var actions = Wrap("Project.Dev.Actions", 4);
        HavenDevDiagnosticButton = Ghost("Project.Dev.Diagnostic", "Open error", "edit");
        HavenDevDiagnosticButton.Accessibility.AccessibleName = "Navigate to simulated build diagnostic";
        HavenDevDeployButton = Ghost("Project.Dev.Deploy", "Deploy", "play");
        HavenDevDeployButton.Accessibility.AccessibleName = "Deploy to simulated device";
        actions.Add(HavenDevDiagnosticButton);
        actions.Add(HavenDevDeployButton);
        HavenDevPanel.Add(actions);

        HavenDevDeployStatus = Muted("Project.Dev.DeployStatus", "SIMULATED deploy not run");
        HavenDevPanel.Add(HavenDevDeployStatus);
        HavenDevOutput = Muted("Project.Dev.Output", string.Empty);
        HavenDevOutput.SetValue(HavenProperties.MinHeight, HavenLength.Px(48));
        HavenDevPanel.Add(HavenDevOutput);
        ToolDock.Add(HavenDevPanel);

        Wire(HavenDevLogicalButton, () => HavenDevExplorerModeRequested?.Invoke(HavenDevExplorerMode.Logical));
        Wire(HavenDevFilesystemButton, () => HavenDevExplorerModeRequested?.Invoke(HavenDevExplorerMode.Filesystem));
        Wire(HavenDevDiagnosticButton, RequestHavenDevDiagnosticNavigation);
        Wire(HavenDevDeployButton, () => HavenDevDeployRequested?.Invoke(this, EventArgs.Empty));
    }

    private void AddToolButton(Container host, HavenDevTool tool, string label)
    {
        var button = Ghost($"Project.Dev.Tool.{tool}", label, "");
        button.Accessibility.AccessibleName = $"Show simulated Haven Dev {label.ToLowerInvariant()}";
        Wire(button, () => HavenDevToolRequested?.Invoke(tool));
        host.Add(button);
    }

    public void SyncHavenDev(HavenDevJourneyPresentationState? state)
    {
        _havenDevState = state;
        HavenDevPanel.SetValue(HavenProperties.Visibility, state is null ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        if (state is null) return;

        HavenDevEvidence.Content = state.EvidenceLabel;
        HavenDevActivePath.Content = "Active file · " + state.ActivePath;
        HavenDevExplorerItems.Content = string.Join("\n", state.ExplorerRows);
        HavenDevOutput.Content = state.ToolOutput;
        HavenDevDeployStatus.Content = state.DeployStatus;
        HavenDevLogicalButton.SetState(HavenElementState.Selected, state.ExplorerMode == HavenDevExplorerMode.Logical);
        HavenDevFilesystemButton.SetState(HavenElementState.Selected, state.ExplorerMode == HavenDevExplorerMode.Filesystem);
        HavenDevDiagnosticButton.SetValue(HavenProperties.Enabled, state.Diagnostic is not null);
        HavenDevDeployButton.SetValue(HavenProperties.Enabled, state.CanDeploy);

        foreach (var tool in Enum.GetValues<HavenDevTool>())
            Find<HavenButton>($"Project.Dev.Tool.{tool}").SetState(HavenElementState.Selected, state.ActiveTool == tool);
    }

    public void RequestHavenDevDiagnosticNavigation()
    {
        if (_havenDevState?.Diagnostic is { } diagnostic)
            HavenDevDiagnosticRequested?.Invoke(diagnostic);
    }
}
