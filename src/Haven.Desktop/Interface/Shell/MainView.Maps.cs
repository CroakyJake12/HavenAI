using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
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

        var services = App.Services;
        var maps = services?.GetService<IMapService>();
        var tiles = services?.GetService<ITileSource>();
        var savedPlaces = services?.GetService<IMapsSavedPlaceStore>();
        if (maps is null || tiles is null || savedPlaces is null)
        {
            _notifications.Show(
                "Maps unavailable",
                "Maps is registered, but its map services are not available in this image.",
                ToastKind.Warning,
                TimeSpan.FromSeconds(5));
            return;
        }

        var page = new MapsPage(maps, tiles, savedPlaces);
        AddOrSelectTab(key, "Maps", page, closeable: true, surface: HavenSurface.Maps);
    }
}
