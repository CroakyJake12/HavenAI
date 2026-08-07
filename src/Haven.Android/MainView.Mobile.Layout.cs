using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private bool _mobileLayoutApplied;

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

        // Mobile adapts the desktop shell instead of creating a second shell.
        // Tabs, Actions and Model therefore keep the exact desktop controls and event flow.
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

        // Replace only the model click bridge so the desktop selector opens first,
        // then Android-specific Hugging Face/download/import actions are appended.
        TopRail.ModelRequested -= OnTopRailModelRequested;
        TopRail.ModelRequested -= OnMobileTopRailModelRequested;
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
        PageContent.Margin = new Thickness(0);
    }
}
