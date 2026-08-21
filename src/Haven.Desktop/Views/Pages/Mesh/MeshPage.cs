using Avalonia.Automation;
using Avalonia.Controls;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views.Pages.Mesh;

/// <summary>Thin Avalonia host for the Haven-native Mesh devices and Work Mode scene.</summary>
public sealed class MeshPage : UserControl, IDisposable
{
    private readonly MeshHavenScene _scene;
    private bool _disposed;

    public MeshPage(MeshPageViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _scene = new MeshHavenScene(viewModel);
        Scene = new HavenSceneControl { Root = _scene.Root };
        AutomationProperties.SetAutomationId(this, "HavenNativeMeshPage");
        AutomationProperties.SetName(this, "Haven Mesh and Work Mode");
        AutomationProperties.SetAutomationId(Scene, "HavenNativeMeshScene");
        AutomationProperties.SetName(Scene, "Mesh device and AI team management");
        Content = Scene;
        _ = _scene.InitialiseAsync();
    }

    public HavenSceneControl Scene { get; }
    internal MeshHavenScene HavenScene => _scene;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Scene.Root = null;
        _scene.Dispose();
    }
}
