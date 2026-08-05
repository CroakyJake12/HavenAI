using Avalonia;
using Avalonia.Controls;
using Haven.Core;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private async Task OpenMobileContextDrawerAsync()
    {
        if (_mobileDrawerContent is null)
            return;

        await RefreshRecentsAsync(CancellationToken.None);
        _mobileDrawerContent.Children.Clear();
        AddDrawerHeading(_mobileDrawerContent, RecentHeading);

        var conversations = PinnedConversations.Concat(RecentConversations).ToArray();
        foreach (var item in conversations)
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

        if (conversations.Length == 0)
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

        OpenMobileDrawer();
    }

    private async Task ShowMobileLauncherAsync()
    {
        if (_mobileDrawerContent is null)
            return;

        _mobileDrawerContent.Children.Clear();
        AddDrawerHeading(_mobileDrawerContent, "Haven apps");

        var modes = await _modeRegistry.GetModesAsync(CancellationToken.None);
        foreach (var mode in modes.OrderBy(item => DisplayObject(item), StringComparer.CurrentCultureIgnoreCase))
        {
            var selected = mode;
            var button = MobileListButton(
                DisplayObject(mode),
                "Haven app",
                string.IsNullOrWhiteSpace(mode.Key) ? "apps" : mode.Key);
            button.Click += async (_, _) =>
            {
                CloseMobileDrawer();
                await LaunchAppAsync(selected, openInNewTab: false);
            };
            _mobileDrawerContent.Children.Add(button);
        }

        OpenMobileDrawer();
    }

    private void ShowMobileActions()
    {
        if (_mobileDrawerContent is null)
            return;

        _mobileDrawerContent.Children.Clear();
        AddDrawerHeading(_mobileDrawerContent, "Actions");

        AddMobileAction(
            "New chat",
            "Start a separate conversation.",
            "plus",
            () => _ = OpenNewChatAsync(forceNewTab: true));

        AddMobileAction(
            "New chat group",
            "Create a grouped chat workspace.",
            "folder",
            () =>
            {
                if (NewContainerCommand.CanExecute(null))
                    NewContainerCommand.Execute(null);
                CloseMobileDrawer();
            });

        AddMobileAction(
            "Connect Android app",
            "Attach an installed app to the current chat without launching it.",
            "apps",
            () => _ = ShowMobileInstalledAppsAsync());

        AddMobileAction(
            "ChatGPT / OpenAI connection",
            "Open plugin and provider setup.",
            "plugins",
            () =>
            {
                CloseMobileDrawer();
                OpenCatalog(CatalogPageKind.Plugins);
                OpenApplicationSettings();
            });

        AddMobileAction(
            "Device Use",
            "Ask Haven to work with this Android device through supported intents and providers.",
            "device",
            () =>
            {
                CloseMobileDrawer();
                _ = OpenNewChatAsync(
                    "Device Use is active on Android. Help with the requested device task using supported Android intents, " +
                    "content providers, accessibility-safe flows, and explicit user confirmation for consequential actions.");
            });

        AddMobileAction(
            "Add or import model",
            "Choose existing GGUF model files, including PocketPal downloads.",
            "download",
            () =>
            {
                CloseMobileDrawer();
                LaunchAndroidModelImporter();
            });

        AddMobileAction(
            "Model settings",
            "Select and refresh Haven model providers.",
            "model",
            () =>
            {
                CloseMobileDrawer();
                OpenApplicationSettings();
            });

        AddMobileAction(
            "Plugins",
            "Browse Haven plugins and integrations.",
            "plugins",
            () =>
            {
                CloseMobileDrawer();
                OpenCatalog(CatalogPageKind.Plugins);
            });

        AddMobileAction(
            "Automations",
            "Open scheduled actions.",
            "automation",
            () =>
            {
                CloseMobileDrawer();
                OpenAutomations();
            });

        AddMobileAction(
            "Settings",
            "Open Haven settings.",
            "settings",
            () =>
            {
                CloseMobileDrawer();
                OpenApplicationSettings();
            });

        OpenMobileDrawer();
    }

    private void AddMobileAction(string title, string detail, string icon, Action action)
    {
        if (_mobileDrawerContent is null)
            return;

        var button = MobileListButton(title, detail, icon);
        button.Click += (_, _) => action();
        _mobileDrawerContent.Children.Add(button);
    }

    private async Task ShowMobileInstalledAppsAsync()
    {
        if (_mobileDrawerContent is null)
            return;

        _mobileDrawerContent.Children.Clear();
        AddDrawerHeading(_mobileDrawerContent, "Connect an app");
        _mobileDrawerContent.Children.Add(new TextBlock
        {
            Text = "Selecting an app adds it to chat context. Haven will use supported Android APIs or documented web APIs and will not simply launch the app.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Foreground = ResourceBrush("HavenTextSoftBrush"),
            Margin = new Thickness(4, 0, 4, 8)
        });

        var apps = await GetInstalledAndroidAppsAsync();
        foreach (var app in apps)
        {
            var selected = app;
            var button = MobileListButton(selected.Label, selected.PackageName, "apps");
            button.Click += async (_, _) =>
            {
                CloseMobileDrawer();
                await ConnectAndroidAppToChatAsync(selected);
            };
            _mobileDrawerContent.Children.Add(button);
        }

        OpenMobileDrawer();
    }

    private void ShowMobileNotifications()
    {
        if (_mobileDrawerContent is null)
            return;

        _mobileDrawerContent.Children.Clear();
        AddDrawerHeading(_mobileDrawerContent, "Notifications");

        if (Notifications.Count == 0)
        {
            _mobileDrawerContent.Children.Add(new TextBlock
            {
                Text = "You’re all caught up.",
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

        OpenMobileDrawer();
    }
}
