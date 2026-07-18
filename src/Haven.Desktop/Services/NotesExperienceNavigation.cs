using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

public static class NotesExperienceNavigation
{
    public static Task OpenAsync(MainWindowViewModel shell, NotesExperienceKind kind)
    {
        ArgumentNullException.ThrowIfNull(shell);
        var key = "notes-experience-" + kind.ToString().ToLowerInvariant();
        var existing = shell.OpenTabs.FirstOrDefault(tab => tab.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            shell.SelectedTab = existing;
            return Task.CompletedTask;
        }

        var title = DisplayName(kind);
        Control page;
        if (kind == NotesExperienceKind.Notes)
        {
            var services = App.Services ?? throw new InvalidOperationException("Haven services are unavailable.");
            var viewModel = ActivatorUtilities.CreateInstance<NotesWorkspaceViewModel>(
                services,
                services.GetRequiredService<IProviderModelClient>());
            page = new NotesWorkspaceView(viewModel);
        }
        else
        {
            page = new BlankNotesExperienceView(kind);
        }

        var tab = new WorkspaceTabViewModel(key, title, page, true, HavenSurface.Home);
        shell.OpenTabs.Add(tab);
        shell.SelectedTab = tab;
        return Task.CompletedTask;
    }

    public static string DisplayName(NotesExperienceKind kind) => kind switch
    {
        NotesExperienceKind.Notes => "Haven Notes",
        NotesExperienceKind.Present => "Haven Present",
        NotesExperienceKind.Data => "Haven Data",
        NotesExperienceKind.Tasks => "Haven Tasks",
        NotesExperienceKind.Imagine => "Haven Imagine",
        _ => "Haven"
    };

    public static string Description(NotesExperienceKind kind) => kind switch
    {
        NotesExperienceKind.Notes => "Documents, ink, equations, interactive widgets, flashcards and reviewed AI.",
        NotesExperienceKind.Present => "Slides",
        NotesExperienceKind.Data => "Spreadsheets",
        NotesExperienceKind.Tasks => "Tasks",
        NotesExperienceKind.Imagine => "Image, video and audio generation",
        _ => string.Empty
    };
}

public sealed class BlankNotesExperienceView : UserControl
{
    public BlankNotesExperienceView(NotesExperienceKind kind)
    {
        Focusable = true;
        var title = NotesExperienceNavigation.DisplayName(kind);
        AutomationProperties.SetName(this, title);
        Content = new Grid
        {
            Children =
            {
                new StackPanel
                {
                    Width = 520,
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = title,
                            FontSize = 32,
                            FontWeight = FontWeight.SemiBold,
                            HorizontalAlignment = HorizontalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = NotesExperienceNavigation.Description(kind),
                            FontSize = 15,
                            Foreground = Avalonia.Application.Current?.Resources["HavenMutedBrush"] as IBrush ?? Brushes.Gray,
                            HorizontalAlignment = HorizontalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = "This is a real routed Haven workspace and is intentionally blank in this Notes build. It contains no invented editor controls.",
                            TextWrapping = TextWrapping.Wrap,
                            TextAlignment = TextAlignment.Center,
                            Foreground = Avalonia.Application.Current?.Resources["HavenMuted2Brush"] as IBrush ?? Brushes.Gray
                        }
                    }
                }
            }
        };
    }
}
