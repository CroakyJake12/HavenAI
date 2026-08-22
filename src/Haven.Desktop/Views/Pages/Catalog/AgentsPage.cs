using Avalonia.Automation;
using Avalonia.Controls;
using Haven.Application;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views.Pages.Catalog;

/// <summary>Thin Avalonia backend host for the Haven.UI Agents management scene.</summary>
public sealed class AgentsPage : UserControl, IDisposable
{
    private readonly AgentsHavenScene _scene;
    private bool _disposed;

    public AgentsPage(CatalogPageViewModel viewModel, AgentTaskRuntimeService? runtime = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        if (viewModel.Kind != CatalogPageKind.Agents)
            throw new ArgumentException("AgentsPage requires an Agents catalogue view-model.", nameof(viewModel));

        _scene = new AgentsHavenScene(viewModel, runtime);
        Scene = new HavenSceneControl { Root = _scene.Root };
        AutomationProperties.SetAutomationId(this, "HavenNativeAgentsPage");
        AutomationProperties.SetName(this, "Haven-native Agents management");
        AutomationProperties.SetAutomationId(Scene, "HavenNativeAgentsScene");
        AutomationProperties.SetName(Scene, "Agents management");
        Content = Scene;
    }

    public HavenSceneControl Scene { get; }
    internal AgentsHavenScene HavenScene => _scene;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Scene.Root = null;
        _scene.Dispose();
    }
}
