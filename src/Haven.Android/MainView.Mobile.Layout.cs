using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private bool _mobileLayoutApplied;

    // Retained only so older mobile helper partials still compile. The shared
    // desktop TopRail/content controls now own the visible mobile shell.
    private Border? _mobileHeader;
    private StackPanel? _mobileTabs;
    private Border? _mobileBottomAffordance;
    private Border? _mobileHomeFooter;
    private Border? _mobileDrawerScrim;
    private Border? _mobileDrawer;
    private StackPanel? _mobileDrawerContent;
    private TextBox? _mobileGoInput;
    private Control? _mobilePageContent;
    private double? _mobileSwipeStartY;

    public void ApplyMobileLayout()
    {
        if (_mobileLayoutApplied)
            return;

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

        // Mobile is an adaptation of the desktop shell, not a second shell.
        // Keep the real TopRail (tabs/actions/model) and only collapse the
        // desktop-only side chrome.
        TopRail.IsVisible = true;
        TopRail.ApplyMobileCompactLayout();

        SidebarControl.IsVisible = false;
        NativeSidebarHost.IsVisible = false;
        ShellContextBar.IsVisible = false;

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

        // The shared desktop selector is the source of truth. Android extends it
        // with the model-library entry points after it opens.
        TopRail.ModelRequested -= OnTopRailModelRequested;
        TopRail.ModelRequested += OnMobileTopRailModelRequested;

        PropertyChanged += OnMobileSharedShellPropertyChanged;

        RefreshTopRailTabs();
        ApplyMobileSharedShellState();
    }

    private void OnMobileSharedShellPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
        => Dispatcher.UIThread.Post(ApplyMobileSharedShellState);

    private void ApplyMobileSharedShellState()
    {
        if (!_mobileLayoutApplied)
            return;

        TopRail.IsVisible = true;
        TopRail.ApplyMobileCompactLayout();

        SidebarControl.IsVisible = false;
        NativeSidebarHost.IsVisible = false;
        ShellContextBar.IsVisible = false;

        // Do not reserve space for the removed mobile footer/history controls.
        // Go/NewChat pages own their own composer and can use the full viewport.
        PageContent.Margin = new Thickness(0);
    }
}
