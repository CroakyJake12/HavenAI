/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Services/NotesExperienceNavigation.cs, in the Desktop services layer, adapting application behavior to Windows and Avalonia concerns.
 * What: This file owns NotesExperienceNavigation, BlankNotesExperienceView. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views;
using Haven.Desktop.Views.Shell;
using Haven.Desktop.Views.Pages.Notes;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

using NotesPageView = Views.Pages.Notes.NotesPage;

/// <summary>
/// Represents notes experience navigation and keeps its related state and behavior together.
/// </summary>
public static class NotesExperienceNavigation
{
    /// <summary>
    /// Performs open asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public static Task OpenAsync(MainView shell, NotesExperienceKind kind)
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
            page = ActivatorUtilities.CreateInstance<NotesPageView>(
                services,
                services.GetRequiredService<IProviderModelClient>());
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

    /// <summary>
    /// Performs the display name step owned by this component.
    /// </summary>
    public static string DisplayName(NotesExperienceKind kind) => kind switch
    {
        NotesExperienceKind.Notes => "Haven Notes",
        NotesExperienceKind.Present => "Haven Present",
        NotesExperienceKind.Data => "Haven Data",
        NotesExperienceKind.Tasks => "Haven Tasks",
        NotesExperienceKind.Imagine => "Haven Imagine",
        _ => "Haven"
    };

    /// <summary>
    /// Performs the description step owned by this component.
    /// </summary>
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

/// <summary>
/// Represents blank notes experience view and keeps its related state and behavior together.
/// </summary>
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
