using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.HavenUI.Components;
using Haven.Desktop.HavenUI.Tokens;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    public Task ApplyMobileStartupSurfaceAsync() => OpenHomeAsync();

    public async Task ApplyMobileLaunchRequestAsync(string? surface, string? prompt)
    {
        if (!string.IsNullOrWhiteSpace(prompt))
        {
            await OpenNewChatAsync(prompt);
            return;
        }

        if (string.Equals(surface, "assistant", StringComparison.OrdinalIgnoreCase))
        {
            await OpenVoiceSessionFromActionAsync();
            return;
        }

        if (string.Equals(surface, "notifications", StringComparison.OrdinalIgnoreCase))
        {
            ShowMobileNotifications();
            return;
        }

        if (string.Equals(surface, "dashboard", StringComparison.OrdinalIgnoreCase))
        {
            await OpenDashboardAsync();
            return;
        }

        await ApplyMobileStartupSurfaceAsync();
    }

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
        if (_mobileSwipeStartY is double start && start - e.GetPosition(this).Y > 28)
            _ = OpenMobileContextDrawerAsync();
        _mobileSwipeStartY = null;
    }

    private void OnMobileTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Dispatcher.UIThread.Post(RefreshMobileTabs);

    private void OnMobileNotificationsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Dispatcher.UIThread.Post(RefreshMobileChrome);

    private void OnMobileShellPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (e.PropertyName == nameof(CurrentSurface))
                HavenUiResourceApplier.Apply(SurfacePaletteCatalog.For(CurrentSurface, _preferences.Appearance));
            RefreshMobileChrome();
        });
    }

    private void RefreshMobileChrome()
    {
        if (!_mobileLayoutApplied)
            return;

        var isHome = CurrentSurface == HavenSurface.Home;
        var showChatAffordance = CurrentSurface == HavenSurface.Chat;
        SidebarControl.IsVisible = false;
        NativeSidebarHost.IsVisible = false;
        ShellContextBar.IsVisible = false;
        if (_mobileHeader is not null)
            _mobileHeader.IsVisible = true;
        if (_mobileBottomAffordance is not null)
            _mobileBottomAffordance.IsVisible = showChatAffordance;
        if (_mobileHomeFooter is not null)
            _mobileHomeFooter.IsVisible = isHome;
        PageContent.Margin = isHome
            ? new Thickness(0, 0, 0, 78)
            : showChatAffordance
                ? new Thickness(0, 0, 0, 112)
                : new Thickness(0);
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
            var isSelected = ReferenceEquals(tab, SelectedTab);
            var labelRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new HavenIcon
                    {
                        IconKey = IconForSurface(tab.Surface),
                        Width = 16,
                        Height = 16
                    },
                    new TextBlock
                    {
                        Text = tab.Title,
                        VerticalAlignment = VerticalAlignment.Center,
                        FontWeight = isSelected ? FontWeight.Bold : FontWeight.SemiBold,
                        FontSize = 12,
                        MaxWidth = 150,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }
                }
            };
            var select = new HavenTabButton
            {
                Content = labelRow,
                IsSelected = isSelected,
                MinHeight = 36
            };
            select.Click += (_, _) => SelectedTab = selected;

            var tabRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 0,
                Children = { select }
            };
            if (tab.IsCloseable)
            {
                var close = MobileIconButton("close", () => CloseTab(selected), "Close tab");
                close.MinHeight = 34;
                close.MinWidth = 30;
                close.Padding = new Thickness(6);
                tabRow.Children.Add(close);
            }

            var tabShell = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,2"),
                Margin = new Thickness(0, 0, 3, 0)
            };
            tabShell.Children.Add(tabRow);
            var underline = new HavenSelectionIndicator
            {
                Height = 2,
                IsVisible = isSelected
            };
            Grid.SetRow(underline, 1);
            tabShell.Children.Add(underline);
            _mobileTabs.Children.Add(tabShell);
        }

        var add = MobileIconButton("plus", () =>
        {
            if (AddNewTabCommand.CanExecute(null))
                AddNewTabCommand.Execute(null);
        }, "New tab");
        add.MinHeight = 36;
        add.MinWidth = 36;
        add.CornerRadius = new CornerRadius(12);
        add.BorderBrush = ResourceBrush("HavenAccentBorderBrush");
        add.BorderThickness = new Thickness(1);
        _mobileTabs.Children.Add(add);
    }
    private void OpenMobileDrawer()
    {
        if (_mobileDrawerScrim is not null)
            _mobileDrawerScrim.IsVisible = true;
        if (_mobileDrawer is not null)
            _mobileDrawer.IsVisible = true;
    }

    private void CloseMobileDrawer()
    {
        if (_mobileDrawer is not null)
            _mobileDrawer.IsVisible = false;
        if (_mobileDrawerScrim is not null)
            _mobileDrawerScrim.IsVisible = false;
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
                    Text = HumanizeWords(title),
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
        return new HavenNavigationButton
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            MinHeight = 52,
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
        var button = new HavenMobileActionButton
        {
            Content = content,
            Padding = new Thickness(horizontalPadding, 6)
        };
        button.Click += (_, _) => action();
        return button;
    }

    private static Button MobileIconButton(string iconKey, Action action, string tooltip)
    {
        var button = new HavenIconButton
        {
            Content = new HavenIcon { IconKey = iconKey, Width = 18, Height = 18 },
            MinWidth = 44,
            Padding = new Thickness(10)
        };
        ToolTip.SetTip(button, tooltip);
        button.Click += (_, _) => action();
        return button;
    }

    private static string HumanizeWords(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;
        var builder = new System.Text.StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (index > 0
                && char.IsUpper(current)
                && (char.IsLower(value[index - 1])
                    || (index + 1 < value.Length && char.IsLower(value[index + 1]))))
            {
                builder.Append(' ');
            }
            builder.Append(current);
        }
        return builder.ToString();
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
