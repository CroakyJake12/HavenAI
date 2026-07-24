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

        if (_betaOverlaysAttached)
        {
            return;
        }

        _betaOverlaysAttached = true;
        OverlayHost.Background = null;
        OverlayHost.IsVisible = true;

        _globalExecutionStatus = new ChatExecutionStatusControl
        {
            Margin = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            MaxWidth = 720
        };

        _globalCallViewModel = new InChatCallWidgetViewModel(
            _callCoordinator,
            _conversations);
        _globalCallViewModel.Open();

        var callWidget = new GlobalCallWidget(_globalCallViewModel)
        {
            Margin = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        };

        var overlayGrid = new Grid();
        overlayGrid.Children.Add(_globalExecutionStatus);
        overlayGrid.Children.Add(callWidget);
        OverlayHost.Child = overlayGrid;

        _sessions.ExecutionChanged += OnExecutionChanged;
        _globalExecutionStatus.Snapshot = _sessions.CurrentExecution;
    }

    private void DetachBetaOverlays()
    {
        if (!_betaOverlaysAttached)
        {
            return;
        }

        _betaOverlaysAttached = false;
        _sessions.ExecutionChanged -= OnExecutionChanged;
        _globalExecutionStatus?.Dispose();
        _globalCallViewModel?.Dispose();
        _globalExecutionStatus = null;
        _globalCallViewModel = null;
        OverlayHost.Child = null;
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
