using Avalonia.Controls;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Events;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Pages.ContainerSettings;

namespace Haven.Desktop.Views.Shell.Overlays;

public sealed partial class ProjectSettingsOverlay : UserControl
{
    public ProjectSettingsOverlay(
        ContainerDefinition definition,
        IContainerRepository repository,
        HavenEventBus bus)
    {
        InitializeComponent();
        var item = new ContainerItemViewModel(definition);
        PageContentHost.Content = new ContainerSettingsPage(bus, item, repository, () => Task.CompletedTask);
        BackButton.Click += (_, _) =>
        {
            if (this.FindAncestorOfType<MainView>() is { } mainView)
                mainView.HideOverlay();
        };
    }
}
