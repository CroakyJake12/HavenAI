using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Shell.Overlays;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private bool _betaOverlayLifecycleWired;
    private bool _betaOverlaysAttached;
    private ChatExecutionStatusControl? _globalExecutionStatus;
    private InChatCallWidgetViewModel? _globalCallViewModel;

    private void AttachBetaOverlays()
    {
        if (!_betaOverlayLifecycleWired)
        {
            _betaOverlayLifecycleWired = true;
            AttachedToVisualTree += OnBetaOverlayAttached;
            DetachedFromVisualTree += OnBetaOverlayDetached;
        }

        HideLegacyShellContextBar();

        if (_betaOverlaysAttached)
        {
            return;
        }

        ChatExecutionStatusControl? executionStatus = null;
        InChatCallWidgetViewModel? callViewModel = null;

        try
        {
            executionStatus = new ChatExecutionStatusControl
            {
                Margin = new Thickness(24),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                MaxWidth = 720
            };

            callViewModel = new InChatCallWidgetViewModel(
                _callCoordinator,
                _conversations);
            callViewModel.Open();

            var callWidget = new GlobalCallWidget(callViewModel)
            {
                Margin = new Thickness(24),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom
            };

            var overlayGrid = new Grid
            {
                Background = null
            };
            overlayGrid.Children.Add(executionStatus);
            overlayGrid.Children.Add(callWidget);

            OverlayHost.Background = null;
            OverlayHost.Child = overlayGrid;

            _globalExecutionStatus = executionStatus;
            _globalCallViewModel = callViewModel;
            _sessions.ExecutionChanged += OnExecutionChanged;
            executionStatus.Snapshot = _sessions.CurrentExecution;

            _betaOverlaysAttached = true;
            OverlayHost.IsVisible = true;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Unable to attach Haven beta overlays: {exception}");

            executionStatus?.Dispose();
            callViewModel?.Dispose();
            OverlayHost.Child = null;
            OverlayHost.Background = null;
            OverlayHost.IsVisible = false;
            _globalExecutionStatus = null;
            _globalCallViewModel = null;
            _betaOverlaysAttached = false;
        }
    }

    private void HideLegacyShellContextBar()
    {
        ShellContextBar.IsVisible = false;
        ShellContextBar.IsHitTestVisible = false;
        ShellContextBar.Opacity = 0;
        ShellContextBar.Width = 0;
        ShellContextBar.Height = 0;
        ShellContextBar.Margin = new Thickness(0);
    }

    private void DetachBetaOverlays()
    {
        if (!_betaOverlaysAttached)
        {
            OverlayHost.Child = null;
            OverlayHost.Background = null;
            OverlayHost.IsVisible = false;
            return;
        }

        _betaOverlaysAttached = false;
        _sessions.ExecutionChanged -= OnExecutionChanged;
        _globalExecutionStatus?.Dispose();
        _globalCallViewModel?.Dispose();
        _globalExecutionStatus = null;
        _globalCallViewModel = null;
        OverlayHost.Child = null;
        OverlayHost.Background = null;
        OverlayHost.IsVisible = false;
    }

    private void OnExecutionChanged(ChatExecutionSnapshot snapshot)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_globalExecutionStatus is not null)
            {
                _globalExecutionStatus.Snapshot = snapshot;
            }
        });
    }

    private void OnBetaOverlayAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        AttachBetaOverlays();
    }

    private void OnBetaOverlayDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        DetachBetaOverlays();
    }
}
