using Avalonia.Controls;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Events;
using Haven.Desktop.Views.Pages.LessonSettings;

namespace Haven.Desktop.Views.Shell.Overlays;

public sealed partial class LessonSettingsOverlay : UserControl
{
    public LessonSettingsOverlay(
        Lesson lesson,
        IContainerRepository containers,
        HavenEventBus bus)
    {
        InitializeComponent();
        PageContentHost.Content = new LessonSettingsPage(bus, lesson, containers, () => Task.CompletedTask);
        BackButton.Click += (_, _) =>
        {
            if (this.FindAncestorOfType<MainView>() is { } mainView)
                mainView.HideOverlay();
        };
    }
}
