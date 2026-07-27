using Avalonia.Controls;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Desktop.Events;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Settings;

namespace Haven.Desktop.Views.Shell.Overlays;

public sealed partial class SettingsOverlay : UserControl
{
    public SettingsOverlay(HavenEventBus bus, UserPreferencesService preferences, IOllamaClient ollama)
    {
        InitializeComponent();
        SettingsPageHost.Content = new SettingsPage(bus, preferences, ollama);
        BackButton.Click += (_, _) =>
        {
            if (this.FindAncestorOfType<MainView>() is { } mainView)
                mainView.HideOverlay();
        };
    }
}
