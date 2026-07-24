using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
    private bool _redirectingLegacyCallPage;
    private object? _lastNonCallContent;
    private Popup? _globalCallPopup;
    private Popup? _globalExecutionPopup;
    private GlobalCallWidget? _globalCallWidget;
    private ChatExecutionStatusControl? _globalExecutionStatus;
    private InChatCallWidgetViewModel? _globalCallViewModel;

    public void OpenVoiceSession()
    {
        AttachBetaOverlays();

        if (_globalCallViewModel is null || _globalCallPopup is null)
        {
            return;
        }

        _globalCallViewModel.Open();
        _globalCallPopup.IsOpen = true;
    }

    private void AttachBetaOverlays()
    {
        if (!_betaOverlayLifecycleWired)
        {
            _betaOverlayLifecycleWired = true;
            AttachedToVisualTree += OnBetaOverlayAttached;
            DetachedFromVisualTree += OnBetaOverlayDetached;
        }

        DisableLegacyOverlayHost();
        HideLegacyShellContextBar();

        if (_betaOverlaysAttached)
        {
            return;
        }

        _betaOverlaysAttached = true;

        _globalCallViewModel = new InChatCallWidgetViewModel(
            _callCoordinator,
            _conversations);

        _globalCallWidget = new GlobalCallWidget(_globalCallViewModel);

        _globalCallPopup = new Popup
        {
            PlacementTarget = ContentArea,
            Placement = PlacementMode.BottomEdgeAlignedRight,
            HorizontalOffset = -20,
            VerticalOffset = -20,
            IsLightDismissEnabled = false,
            Child = _globalCallWidget,
            IsOpen = false
        };

        _globalExecutionStatus = new ChatExecutionStatusControl
        {
            MaxWidth = 720
        };
        _globalExecutionStatus.Snapshot = _sessions.CurrentExecution;

        _globalExecutionPopup = new Popup
        {
            PlacementTarget = ContentArea,
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            HorizontalOffset = 20,
            VerticalOffset = -20,
            IsLightDismissEnabled = false,
            Child = _globalExecutionStatus,
            IsOpen = true
        };

        _sessions.ExecutionChanged += OnExecutionChanged;
        _globalCallViewModel.PropertyChanged += OnGlobalCallPropertyChanged;
        PageContent.PropertyChanged += OnPageContentPropertyChanged;

        var currentContent = PageContent.Content;
        if (currentContent is not null && !IsLegacyCallPage(currentContent))
        {
            _lastNonCallContent = currentContent;
        }
    }

    private void DisableLegacyOverlayHost()
    {
        OverlayHost.IsVisible = false;
        OverlayHost.IsHitTestVisible = false;
        OverlayHost.Child = null;
        OverlayHost.Background = null;
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

    private void OnGlobalCallPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(InChatCallWidgetViewModel.IsVisible) &&
            e.PropertyName is not nameof(InChatCallWidgetViewModel.IsOpen) &&
            e.PropertyName is not nameof(InChatCallWidgetViewModel.IsActive))
        {
            return;
        }

        if (_globalCallPopup is not null && _globalCallViewModel is not null)
        {
            _globalCallPopup.IsOpen = _globalCallViewModel.IsVisible;
        }
    }

    private void OnPageContentPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != ContentControl.ContentProperty || _redirectingLegacyCallPage)
        {
            return;
        }

        var content = PageContent.Content;
        if (content is null)
        {
            return;
        }

        if (!IsLegacyCallPage(content))
        {
            _lastNonCallContent = content;
            return;
        }

        _redirectingLegacyCallPage = true;
        try
        {
            PageContent.Content = _lastNonCallContent;
            OpenVoiceSession();
        }
        finally
        {
            _redirectingLegacyCallPage = false;
        }
    }

    private static bool IsLegacyCallPage(object content) =>
        string.Equals(
            content.GetType().Name,
            "CallPage",
            StringComparison.Ordinal);

    private void DetachBetaOverlays()
    {
        if (!_betaOverlaysAttached)
        {
            DisableLegacyOverlayHost();
            return;
        }

        _betaOverlaysAttached = false;

        _sessions.ExecutionChanged -= OnExecutionChanged;
        PageContent.PropertyChanged -= OnPageContentPropertyChanged;

        if (_globalCallViewModel is not null)
        {
            _globalCallViewModel.PropertyChanged -= OnGlobalCallPropertyChanged;
        }

        if (_globalCallPopup is not null)
        {
            _globalCallPopup.IsOpen = false;
            _globalCallPopup.Child = null;
        }

        if (_globalExecutionPopup is not null)
        {
            _globalExecutionPopup.IsOpen = false;
            _globalExecutionPopup.Child = null;
        }

        _globalCallWidget?.Dispose();
        _globalCallViewModel?.Dispose();
        _globalExecutionStatus?.Dispose();

        _globalCallPopup = null;
        _globalExecutionPopup = null;
        _globalCallWidget = null;
        _globalCallViewModel = null;
        _globalExecutionStatus = null;

        DisableLegacyOverlayHost();
    }

    private void OnExecutionChanged(ChatExecutionSnapshot snapshot)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
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
