using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Desktop.Views.Pages.Chat;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private bool _mobileLayoutApplied;

    public void ApplyMobileLayout()
    {
        if (!_mobileLayoutApplied)
        {
            _mobileLayoutApplied = true;

            if (Content is not Grid root)
                throw new InvalidOperationException("Haven's mobile shell requires the MainView root grid.");

            var body = root.Children
                .OfType<Grid>()
                .FirstOrDefault(candidate => Grid.GetRow(candidate) == 1)
                ?? throw new InvalidOperationException("Haven's main content grid was not found.");

            var contentHost = body.Children
                .OfType<Grid>()
                .FirstOrDefault(candidate => Grid.GetColumn(candidate) == 1)
                ?? throw new InvalidOperationException("Haven's content host was not found.");

            TopRail.IsVisible = true;
            TopRail.ApplyMobileCompactLayout();

            // Android never uses either desktop sidebar presentation. Keeping IsSidebarOpen
            // false also prevents the shared ApplyShellVisualState() path from re-enabling
            // NativeSidebarHost after navigation.
            IsSidebarOpen = false;
            SidebarControl.IsVisible = false;
            NativeSidebarHost.IsVisible = false;
            ShellContextBar.IsVisible = false;
            InstallMobileChatSidebarPolicy();

            foreach (var child in body.Children)
            {
                if (!ReferenceEquals(child, contentHost))
                    child.IsVisible = false;
            }

            body.ColumnDefinitions = new ColumnDefinitions("*");
            body.Margin = new Thickness(6, 2, 6, 8);
            Grid.SetColumn(contentHost, 0);
            Grid.SetColumnSpan(contentHost, 1);

            ContentArea.CornerRadius = new CornerRadius(18);
            PageContent.Margin = new Thickness(0);

            TopRail.ModelRequested -= OnTopRailModelRequested;
            TopRail.ModelRequested -= OnMobileTopRailModelRequested;
            TopRail.ModelRequested += OnMobileTopRailModelRequested;

            PropertyChanged += OnMobileSharedShellPropertyChanged;
        }

        // Apply again after startup/page creation as well as on the initial shell. The
        // New Haven page graph is created after MainView itself, so a one-shot pre-init
        // adaptation is not enough.
        ApplyMobileSharedShellState();
        Dispatcher.UIThread.Post(ApplyMobileSharedShellState);
    }

    private void OnMobileSharedShellPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
        => Dispatcher.UIThread.Post(ApplyMobileSharedShellState);

    private void ApplyMobileSharedShellState()
    {
        if (!_mobileLayoutApplied)
            return;

        if (IsSidebarOpen)
            IsSidebarOpen = false;

        TopRail.IsVisible = true;
        TopRail.ApplyMobileCompactLayout();
        SidebarControl.IsVisible = false;
        NativeSidebarHost.IsVisible = false;
        ShellContextBar.IsVisible = false;
        StoredChatDropdown.IsVisible = false;
        PageContent.Margin = new Thickness(0);
        ApplyMobileChatSidebarState();

        switch (CurrentPage)
        {
            case NewChatPage newChatPage:
                newChatPage.ApplyAndroidMobileComposition();
                break;
            case ChatPage chatPage:
                chatPage.ApplyAndroidMobileComposition();
                break;
        }
    }
}
