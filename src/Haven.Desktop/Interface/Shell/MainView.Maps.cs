using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Maps;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    public void OpenMaps()
    {
        const string key = "haven-maps";
        var existing = OpenTabs.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SelectedTab = existing;
            return;
        }

        var services = App.Services
            ?? throw new InvalidOperationException("Haven services are unavailable while opening Maps.");
        var page = new MapsPage(
            services.GetRequiredService<IMapService>(),
            services.GetRequiredService<ITileSource>(),
            services.GetRequiredService<IMapsSavedPlaceStore>());
        AddOrSelectTab(key, "Maps", page, closeable: true, surface: HavenSurface.Maps);
    }
}
