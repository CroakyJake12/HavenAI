using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Haven.Core;
using Haven.Desktop.Controls;

namespace Haven.Desktop.Views.Shell.TopRail;

/// <summary>Builds the mockup-native floating App launcher.</summary>
internal static class AppLauncherFlyout
{
    public static Flyout Create(
        IReadOnlyList<ModeDefinition> apps,
        IReadOnlySet<Guid> pinnedIds,
        bool openInNewTab,
        Action<ModeDefinition, bool> launch,
        Action manage)
    {
        var search = new HavenTextInput
        {
            PlaceholderText = "Search",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(38, 10, 12, 10),
            FontSize = 14
        };
        var results = new StackPanel { Spacing = 7, Margin = new Thickness(0, 0, 2, 4) };
        var flyout = new HavenAdaptivePopup
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            FlyoutPresenterTheme = FloatingPresenterTheme()
        };

        void Rebuild()
        {
            results.Children.Clear();
            var query = search.Text?.Trim() ?? string.Empty;
            var filtered = apps
                .Where(item => item.IsEnabled)
                .Where(item => string.IsNullOrWhiteSpace(query)
                               || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                               || item.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.Name)
                .ToArray();
            AddSection("Pinned", filtered.Where(item => pinnedIds.Contains(item.Id)).ToArray());
            AddSection("Productivity", filtered.Where(item => !pinnedIds.Contains(item.Id)).ToArray());
        }

        void AddSection(string title, IReadOnlyList<ModeDefinition> items)
        {
            if (items.Count == 0) return;
            results.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeight.ExtraBold,
                FontSize = 12,
                Margin = new Thickness(4, 7, 4, 2)
            });
            var cards = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                ItemWidth = 194,
                ItemHeight = 64
            };
            foreach (var item in items)
            {
                var button = BuildAppButton(item);
                button.Click += (_, _) =>
                {
                    flyout.Hide();
                    launch(item, openInNewTab);
                };
                cards.Children.Add(button);
            }
            results.Children.Add(cards);
        }

        search.TextChanged += (_, _) => Rebuild();
        var searchHost = new Grid();
        searchHost.Children.Add(search);
        searchHost.Children.Add(new HavenIcon
        {
            IconKey = "search", Width = 18, Height = 18, Margin = new Thickness(13, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false, Opacity = 0.72
        });

        var main = new HavenAdaptiveSurface
        {
            Width = 420,
            Background = ResourceBrush("HavenElevatedBrush", Colors.White),
            BorderBrush = ResourceBrush("HavenLineBrush", Color.FromArgb(30, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(24),
            Child = new StackPanel
            {
                Spacing = 10,
                Margin = new Thickness(16),
                Children =
                {
                    new TextBlock { Text = "Launch App", FontSize = 20, FontWeight = FontWeight.ExtraBold },
                    searchHost,
                    new ScrollViewer
                    {
                        MaxHeight = 350,
                        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        Content = results
                    }
                }
            }
        };
        var manageButton = new HavenButton
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(16, 13),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Children =
                {
                    new HavenIcon { IconKey = "settings", Width = 19, Height = 19 },
                    new TextBlock { Text = "Manage Apps", FontWeight = FontWeight.ExtraBold, VerticalAlignment = VerticalAlignment.Center }
                }
            }
        };
        manageButton.Classes.Add("sidebar");
        manageButton.Click += (_, _) =>
        {
            flyout.Hide();
            manage();
        };
        var footer = new HavenAdaptiveSurface
        {
            Width = 420,
            Background = ResourceBrush("HavenElevatedBrush", Colors.White),
            BorderBrush = ResourceBrush("HavenLineBrush", Color.FromArgb(30, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(4),
            Child = manageButton
        };
        flyout.Content = new StackPanel { Spacing = 9, Children = { main, footer } };
        Rebuild();
        return flyout;
    }

    private static Button BuildAppButton(ModeDefinition item)
    {
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 11 };
        content.Children.Add(new HavenIcon { IconKey = item.IconKey, Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Center });
        var text = new TextBlock
        {
            Text = item.Name,
            FontWeight = FontWeight.ExtraBold,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(text, 1);
        content.Children.Add(text);
        var button = new HavenButton
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Width = 190,
            Height = 56,
            Margin = new Thickness(2, 0, 2, 8),
            Padding = new Thickness(12, 8),
            Background = ResourceBrush("HavenPanel2Brush", Color.Parse("#FFF8F8F8")),
            BorderBrush = ResourceBrush("HavenLineBrush", Color.FromArgb(28, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Content = content
        };
        button.Classes.Add("sidebar");
        var normalBackground = button.Background;
        button.PointerEntered += (_, _) => button.Background = ResourceBrush("HavenAccentSoftBrush", Color.Parse("#FFE0F7FA"));
        button.PointerExited += (_, _) => button.Background = normalBackground;
        return button;
    }

    private static IBrush ResourceBrush(string key, Color fallback) =>
        Avalonia.Application.Current?.TryFindResource(key, out var value) == true && value is IBrush brush
            ? brush
            : new SolidColorBrush(fallback);

    private static ControlTheme? FloatingPresenterTheme() =>
        Avalonia.Application.Current?.TryFindResource("HavenFloatingFlyoutPresenterTheme", out var value) == true
            ? value as ControlTheme
            : null;
}
