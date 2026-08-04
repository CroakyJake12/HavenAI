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

    private async Task OpenMobileContextDrawerAsync()
    {
        if (_mobileDrawer is null || _mobileDrawerContent is null)
            return;

        await RefreshRecentsAsync(CancellationToken.None);
        _mobileDrawerContent.Children.Clear();
        AddDrawerHeading(_mobileDrawerContent, RecentHeading);

        foreach (var item in PinnedConversations.Concat(RecentConversations))
        {
            var conversation = item;
            var button = MobileListButton(
                conversation.Definition.Title,
                conversation.Definition.UpdatedAt.LocalDateTime.ToString("g"),
                "chat");
            button.Click += async (_, _) =>
            {
                CloseMobileDrawer();
                await OpenConversationAsync(conversation);
            };
            _mobileDrawerContent.Children.Add(button);
        }

        if (!PinnedConversations.Any() && !RecentConversations.Any())
        {
            _mobileDrawerContent.Children.Add(new TextBlock
            {
                Text = "No conversations here yet.",
                Foreground = ResourceBrush("HavenTextSoftBrush"),
                Margin = new Thickness(4)
            });
        }

        if (IsProjectOpen)
        {
            AddDrawerHeading(_mobileDrawerContent, ActiveProjectName);
            _mobileDrawerContent.Children.Add(new TextBlock
            {
                Text = ActiveProjectRoot,
                FontSize = 11,
                Foreground = ResourceBrush("HavenTextSoftBrush"),
                Margin = new Thickness(4, 0, 4, 4)
            });

            foreach (var file in ActiveProjectFiles.Take(100))
            {
                _mobileDrawerContent.Children.Add(MobileListButton(
                    DisplayObject(file),
                    "Project content",
                    "file"));
            }
        }
        else
        {
            AddDrawerHeading(_mobileDrawerContent, ProductName);
            _mobileDrawerContent.Children.Add(new TextBlock
            {
                Text = "Current mode: " + CurrentMode,
                Foreground = ResourceBrush("HavenTextSoftBrush"),
                Margin = new Thickness(4)
            });
        }

        _mobileDrawer.IsVisible = true;
    }

    private async Task ShowMobileLauncherAsync()
    {
        if (_mobileDrawer is null || _mobileDrawerContent is null)
            return;

        _mobileDrawerContent.Children.Clear();
        AddDrawerHeading(_mobileDrawerContent, "Haven apps");

        var modes = await _modeRegistry.GetModesAsync(CancellationToken.None);
        foreach (var mode in modes)
        {
            var selected = mode;
            var button = MobileListButton(
                DisplayObject(mode),
                "Haven mode",
                mode.Key);
            button.Click += async (_, _) =>
            {
                CloseMobileDrawer();
                await LaunchAppAsync(selected, openInNewTab: false);
            };
            _mobileDrawerContent.Children.Add(button);
        }

#if ANDROID
        AddDrawerHeading(_mobileDrawerContent, "Installed apps");
        foreach (var app in GetInstalledAndroidApps())
        {
            var selected = app;
            var button = MobileListButton(selected.Label, selected.PackageName, "apps");
            button.Click += (_, _) =>
            {
                CloseMobileDrawer();
                LaunchAndroidApp(selected);
            };
            _mobileDrawerContent.Children.Add(button);
        }
#endif

        _mobileDrawer.IsVisible = true;
    }

    private void ShowMobileNotifications()
    {
        if (_mobileDrawer is null || _mobileDrawerContent is null)
            return;

        _mobileDrawerContent.Children.Clear();
        AddDrawerHeading(_mobileDrawerContent, "Notifications");

        if (Notifications.Count == 0)
        {
            _mobileDrawerContent.Children.Add(new TextBlock
            {
                Text = "You're all caught up.",
                Foreground = ResourceBrush("HavenTextSoftBrush"),
                Margin = new Thickness(4)
            });
        }

        foreach (var notification in Notifications)
        {
            var row = MobileListButton(notification.Title, notification.Message, "notification");
            row.Click += (_, _) => _notifications.Dismiss(notification.Id);
            _mobileDrawerContent.Children.Add(row);
        }

        _mobileDrawer.IsVisible = true;
    }

}
