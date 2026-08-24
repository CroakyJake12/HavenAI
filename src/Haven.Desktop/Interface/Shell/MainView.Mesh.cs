using Haven.Application;
using Haven.Core;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Pages.Mesh;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    public void OpenMeshDashboard()
    {
        const string key = "haven-mesh";
        var existing = OpenTabs.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SelectedTab = existing;
            return;
        }

        var services = global::Haven.Desktop.App.Services
            ?? throw new InvalidOperationException("Haven services are unavailable while opening Mesh.");
        var page = new MeshPage(new MeshPageViewModel(services.GetRequiredService<MeshCoordinator>()));
        AddOrSelectTab(key, "Mesh", page, closeable: true, surface: HavenSurface.Mesh);
    }
}
