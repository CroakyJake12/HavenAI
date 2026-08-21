using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Events;
using Haven.Desktop.HavenUI.Components;
using Haven.Desktop.HavenUI.Tokens;
using Haven.Desktop.Views.Pages.Go;
using Haven.Desktop.Views.Pages.Automations;
using Haven.Desktop.Views.Shell.TopRail;

namespace Haven.Desktop.Tests;

/// <summary>
/// Opt-in rendered-frame harness for mockup comparison. Normal test runs make
/// no files; set HAVEN_VISUAL_CAPTURE_DIR to capture the canonical surfaces.
/// </summary>
public sealed class HavenUiVisualCaptureTests
{
    [AvaloniaFact]
    public void Capture_primary_surfaces_when_requested()
    {
        var destination = Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(destination))
            return;

        Directory.CreateDirectory(destination);
        HavenUiResourceApplier.Apply(SurfacePaletteCatalog.For(HavenSurface.Go, HavenUiAppearance.SuperDark));

        using (var page = new GoPage(new HavenEventBus()))
        {
            Capture(Path.Combine(destination, "go-desktop.png"), page, 1440, 900);
        }

        using (var compactPage = new GoPage(new HavenEventBus()))
        {
            Capture(Path.Combine(destination, "go-compact.png"), compactPage, 430, 860);
        }

        HavenUiResourceApplier.Apply(SurfacePaletteCatalog.For(HavenSurface.Tasks, HavenUiAppearance.SuperDark));
        Capture(
            Path.Combine(destination, "component-gallery.png"),
            BuildComponentGallery(),
            1440,
            900,
            HavenSurface.Tasks);

        var automations = new AutomationsPage(
            new EmptyWorkspaceStateRepository(),
            new EmptyAutomationRepository(),
            null,
            () => Task.CompletedTask,
            _ => Task.CompletedTask);
        Capture(Path.Combine(destination, "automations-desktop.png"), automations, 1440, 900, HavenSurface.Automations);
        Capture(Path.Combine(destination, "automations-compact.png"), automations, 620, 860, HavenSurface.Automations);

        HavenUiResourceApplier.Apply(SurfacePaletteCatalog.For(HavenSurface.Go, HavenUiAppearance.SuperDark));

        using (var rail = new Haven.Desktop.Views.Shell.TopRail.TopRail())
        {
            var host = new Grid
            {
                Background = new SolidColorBrush(Color.Parse("#FF111424")),
                Children = { rail }
            };
            Capture(Path.Combine(destination, "top-rail.png"), host, 1440, 100);
        }
    }

    private static Control BuildComponentGallery()
    {
        var selectedTab = new HavenTabButton { Content = "Selected tab", IsSelected = true };
        var regularTab = new HavenTabButton { Content = "Tab" };
        var dropdown = new HavenDropdownCard
        {
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Drop-Down Title", FontSize = 23, FontWeight = FontWeight.ExtraBold },
                    new HavenDropdownItemButton { Content = "Important", Role = HavenDropdownItemRole.Important },
                    new HavenDropdownItemButton { Content = "Main" },
                    new HavenDropdownItemButton { Content = "Negative", Role = HavenDropdownItemRole.Negative }
                }
            }
        };
        var popup = new HavenPopupCard
        {
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*,Auto"),
                RowSpacing = 14,
                Children =
                {
                    new TextBlock { Text = "Pop-Up Title", FontSize = 24, FontWeight = FontWeight.ExtraBold },
                    Row(new TextBlock
                    {
                        Text = "Popup content grows here and scrolls vertically when needed.",
                        Foreground = Avalonia.Application.Current?.Resources["HavenTextSecondaryBrush"] as IBrush,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                    }, 1),
                    Row(new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 10,
                        Children =
                        {
                            new HavenTertiaryButton { Content = "Close" },
                            new HavenPrimaryButton { Content = "Primary Action" }
                        }
                    }, 2)
                }
            }
        };

        return new Grid
        {
            Margin = new Thickness(52, 40),
            ColumnDefinitions = new ColumnDefinitions("*,*") ,
            ColumnSpacing = 44,
            Children =
            {
                new StackPanel
                {
                    Spacing = 20,
                    Children =
                    {
                        new TextBlock { Text = "Buttons", FontSize = 40, FontWeight = FontWeight.ExtraBold },
                        new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            Spacing = 12,
                            Children =
                            {
                                new HavenPrimaryButton { Content = "Primary" },
                                new HavenSecondaryButton { Content = "Secondary" },
                                new HavenTertiaryButton { Content = "Tertiary" }
                            }
                        },
                        new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            Spacing = 12,
                            Children =
                            {
                                new HavenNegativeButton { Content = "Negative" },
                                new HavenTextButton { Content = "Text Button" }
                            }
                        },
                        new TextBlock { Text = "Other", FontSize = 40, FontWeight = FontWeight.ExtraBold, Margin = new Thickness(0, 16, 0, 0) },
                        new HavenTextInput { PlaceholderText = "Input Box (Placeholder Text)", Width = 560 },
                        new HavenTextInput { Text = "Input Box (Inputted Text)", Width = 560 },
                        new HavenSlider { Minimum = 0, Maximum = 100, Value = 82, Width = 560 },
                        new HavenProgressBar { Minimum = 0, Maximum = 100, Value = 68, Width = 560 },
                        new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            Spacing = 18,
                            Children =
                            {
                                new HavenSwitch { IsChecked = true, Content = "Switch (on)" },
                                new HavenSwitch { IsChecked = false, Content = "Switch (off)" }
                            }
                        },
                        new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            Spacing = 12,
                            Children = { selectedTab, regularTab }
                        }
                    }
                },
                Column(new StackPanel
                {
                    Spacing = 24,
                    Children =
                    {
                        new TextBlock { Text = "Drop-Downs", FontSize = 40, FontWeight = FontWeight.ExtraBold },
                        dropdown,
                        new TextBlock { Text = "Pop-Ups", FontSize = 40, FontWeight = FontWeight.ExtraBold },
                        popup
                    }
                }, 1)
            }
        };
    }

    private static T Row<T>(T control, int row) where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }

    private static T Column<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private static void Capture(
        string path,
        Control content,
        double width,
        double height,
        HavenSurface surface = HavenSurface.Go)
    {
        var window = new Window
        {
            Width = width,
            Height = height,
            CanResize = false,
            Background = SettledTide(surface),
            Content = content
        };

        try
        {
            window.Show();
            if (content is GoPage go && go.FindControl<TextBox>("InstructionBox") is { } input)
            {
                File.WriteAllLines(
                    Path.ChangeExtension(path, ".visual-tree.txt"),
                    input.GetVisualDescendants().OfType<Control>().Select(control =>
                        $"{control.GetType().Name}\t{control.Name}\tvisible={control.IsVisible}\tclasses={string.Join(',', control.Classes)}"));
            }
            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(path);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static IBrush SettledTide(HavenSurface surface)
    {
        var palette = SurfacePaletteCatalog.For(surface, HavenUiAppearance.SuperDark);
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(palette.TideBase, 0),
                new GradientStop(palette.TideBase, 0.46),
                new GradientStop(palette.TideColour, 0.72),
                new GradientStop(palette.TideColour, 1)
            }
        };
    }

    private sealed class EmptyWorkspaceStateRepository : IWorkspaceStateRepository
    {
        public Task<IReadOnlyList<ReusableTaskDefinition>> GetReusableTasksAsync(Guid? containerId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ReusableTaskDefinition>>([]);
        public Task UpsertReusableTaskAsync(ReusableTaskDefinition task, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteReusableTaskAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<WorkspaceVersion>> GetVersionsAsync(Guid? containerId, string? relativePath, int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WorkspaceVersion>>([]);
        public Task AddVersionAsync(WorkspaceVersion version, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<DecisionRecord>> GetDecisionsAsync(Guid containerId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DecisionRecord>>([]);
        public Task UpsertDecisionAsync(DecisionRecord decision, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteDecisionAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class EmptyAutomationRepository : IAutomationRepository
    {
        public Task<IReadOnlyList<AutomationDefinition>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AutomationDefinition>>([]);
        public Task<IReadOnlyList<AutomationDefinition>> GetDueAsync(DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AutomationDefinition>>([]);
        public Task UpsertAsync(AutomationDefinition automation, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> TryAcquireLeaseAsync(Guid automationId, string leaseToken, DateTimeOffset leaseUntil, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task CompleteRunAsync(AutomationRun run, DateTimeOffset? nextRunAt, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<AutomationRun>> GetRunsAsync(Guid automationId, int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AutomationRun>>([]);
    }

    private sealed class EmptyConversationRepository : IConversationRepository
    {
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Conversation>>([]);
        public Task<Conversation?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Conversation?>(null);
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ChatMessage>>([]);
        public Task UpsertConversationAsync(Conversation conversation, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteConversationAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
