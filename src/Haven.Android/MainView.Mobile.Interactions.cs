using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;

#if ANDROID
using Android.Content;
using Android.Content.PM;
#endif

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{

    private async Task SubmitMobileGoAsync()
    {
        var text = _mobileGoInput?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return;

        var context = IsProjectOpen
            ? $"{ProductName}, project {ActiveProjectName}"
            : ProductName;

        if (_mobileGoInput is not null)
            _mobileGoInput.Text = string.Empty;

        await OpenNewChatAsync($"Current mobile screen: {context}. {text}");
    }

    private void OnMobileGoKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        _ = SubmitMobileGoAsync();
    }

    private void OnMobileAffordancePointerPressed(object? sender, PointerPressedEventArgs e)
        => _mobileSwipeStartY = e.GetPosition(this).Y;

    private void OnMobileAffordancePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_mobileSwipeStartY is double start
            && start - e.GetPosition(this).Y > 28)
        {
            _ = OpenMobileContextDrawerAsync();
        }

        _mobileSwipeStartY = null;
    }

    private void OnMobileTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Dispatcher.UIThread.Post(RefreshMobileTabs);

    private void OnMobileNotificationsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Dispatcher.UIThread.Post(RefreshMobileChrome);

    private void OnMobileShellPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        => Dispatcher.UIThread.Post(RefreshMobileChrome);

    private void RefreshMobileChrome()
    {
        if (!_mobileLayoutApplied)
            return;

        var isHome = CurrentSurface == HavenSurface.Home;
        if (_mobileHeader is not null)
            _mobileHeader.IsVisible = !isHome;
        if (_mobileBottomAffordance is not null)
            _mobileBottomAffordance.IsVisible = !isHome;
        if (_mobileHomeFooter is not null)
            _mobileHomeFooter.IsVisible = isHome;

        PageContent.Margin = isHome
            ? new Thickness(0, 0, 0, 72)
            : new Thickness(0, 0, 0, 58);

        RefreshMobileTabs();
    }

    private void RefreshMobileTabs()
    {
        if (_mobileTabs is null)
            return;

        _mobileTabs.Children.Clear();
        foreach (var tab in OpenTabs)
        {
            var selected = tab;
            var button = new Button
            {
                Content = tab.Title,
                MinHeight = 38,
                Padding = new Thickness(12, 5),
                CornerRadius = new CornerRadius(14),
                Background = ReferenceEquals(tab, SelectedTab)
                    ? ResourceBrush("HavenAccentSoftBrush")
                    : Brushes.Transparent,
                BorderBrush = ReferenceEquals(tab, SelectedTab)
                    ? ResourceBrush("HavenAccentBorderBrush")
                    : ResourceBrush("HavenLineBrush"),
                BorderThickness = new Thickness(1)
            };
            button.Click += (_, _) => SelectedTab = selected;
            _mobileTabs.Children.Add(button);
        }
    }

    private void CloseMobileDrawer()
    {
        if (_mobileDrawer is not null)
            _mobileDrawer.IsVisible = false;
    }

    private static void AddDrawerHeading(Panel panel, string text)
    {
        panel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(4, 8, 4, 2)
        });
    }

    private static Button MobileListButton(string title, string detail, string iconKey)
    {
        var icon = new HavenIcon
        {
            IconKey = iconKey,
            Width = 20,
            Height = 20,
            Margin = new Thickness(0, 2, 0, 0)
        };

        var text = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    FontWeight = FontWeight.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                new TextBlock
                {
                    Text = detail,
                    FontSize = 11,
                    Foreground = ResourceBrush("HavenTextSoftBrush"),
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            }
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 12
        };
        grid.Children.Add(icon);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        return new Button
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            MinHeight = 52,
            Padding = new Thickness(12, 8),
            CornerRadius = new CornerRadius(16),
            Content = grid
        };
    }

    private static Button MobileButton(
        string text,
        string iconKey,
        Action action,
        double horizontalPadding = 8)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            Children =
            {
                new HavenIcon { IconKey = iconKey, Width = 17, Height = 17 },
                new TextBlock
                {
                    Text = text,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeight.SemiBold,
                    FontSize = 12
                }
            }
        };

        var button = new Button
        {
            Content = content,
            MinHeight = 44,
            Padding = new Thickness(horizontalPadding, 6),
            CornerRadius = new CornerRadius(14)
        };
        button.Click += (_, _) => action();
        return button;
    }

    private static IBrush? ResourceBrush(string key)
        => Avalonia.Application.Current?.Resources[key] as IBrush;

    private static string DisplayObject(object value)
    {
        foreach (var propertyName in new[] { "DisplayName", "Name", "Title", "RelativePath", "Key" })
        {
            var property = value.GetType().GetProperty(propertyName);
            if (property?.GetValue(value) is string text && !string.IsNullOrWhiteSpace(text))
                return text;
        }

        return value.ToString() ?? value.GetType().Name;
    }

}
