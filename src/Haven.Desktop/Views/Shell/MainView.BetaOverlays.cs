using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Shell.NativePresentation;
using Haven.Desktop.Views.Shell.Overlays;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private bool _betaOverlayLifecycleWired;
    private bool _betaOverlaysAttached;
    private bool _redirectingLegacyPresentation;
    private object? _lastNonCallContent;
    private object? _legacyProjectsContent;
    private Popup? _globalCallPopup;
    private Popup? _globalExecutionPopup;
    private GlobalCallWidget? _globalCallWidget;
    private ChatExecutionStatusControl? _globalExecutionStatus;
    private InChatCallWidgetViewModel? _globalCallViewModel;
    private NativeProjectsPage? _nativeProjectsPage;
    private readonly NativeProjectUiStateStore _nativeProjectUiStateStore = new();

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
            RedirectLegacyPresentation(PageContent.Content);
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

        RedirectLegacyPresentation(PageContent.Content);
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
        if (e.Property != ContentControl.ContentProperty || _redirectingLegacyPresentation)
        {
            return;
        }

        RedirectLegacyPresentation(PageContent.Content);
    }

    private void RedirectLegacyPresentation(object? content)
    {
        if (content is null || _redirectingLegacyPresentation)
        {
            return;
        }

        var destination = ClassifyLegacyPresentation(content);
        switch (destination)
        {
            case NativePresentationDestination.ChatCallWidget:
                RedirectLegacyCallPage();
                return;

            case NativePresentationDestination.Projects:
                ReplaceLegacyProjectsPage(content);
                return;

            default:
                if (!ReferenceEquals(content, _nativeProjectsPage))
                {
                    DisposeNativeProjectsPage();
                }

                _lastNonCallContent = content;
                return;
        }
    }

    private void RedirectLegacyCallPage()
    {
        _redirectingLegacyPresentation = true;
        try
        {
            PageContent.Content = _lastNonCallContent;
            OpenVoiceSession();
        }
        finally
        {
            _redirectingLegacyPresentation = false;
        }
    }

    private void ReplaceLegacyProjectsPage(object legacyContent)
    {
        if (legacyContent is NativeProjectsPage)
        {
            _lastNonCallContent = legacyContent;
            return;
        }

        _legacyProjectsContent = legacyContent;
        DisposeNativeProjectsPage();

        var nativePage = new NativeProjectsPage(
            legacyContent,
            ReadFallbackProjects,
            OpenProjectCreatorFallbackAsync,
            OpenProjectFallbackAsync,
            ArchiveProjectFallbackAsync,
            _nativeProjectUiStateStore);

        _nativeProjectsPage = nativePage;

        _redirectingLegacyPresentation = true;
        try
        {
            PageContent.Content = nativePage;
            _lastNonCallContent = nativePage;
        }
        finally
        {
            _redirectingLegacyPresentation = false;
        }
    }

    private static NativePresentationDestination ClassifyLegacyPresentation(object content)
    {
        var surfaceName = content.GetType().Name;
        var dataContextName = content is Control control
            ? control.DataContext?.GetType().Name
            : surfaceName;

        return NativePresentationRoutePolicy.Classify(surfaceName, dataContextName);
    }

    private IEnumerable<object> ReadFallbackProjects()
    {
        return NativePresentationReflection.ReadCollection(
            this,
            "Projects",
            "ProjectItems",
            "ProjectCards",
            "Workspaces",
            "Containers");
    }

    private async Task OpenProjectCreatorFallbackAsync()
    {
        var handled = await NativePresentationReflection.ExecuteCommandAsync(
            this,
            null,
            "NewProjectCommand",
            "CreateProjectCommand",
            "OpenProjectCreatorCommand",
            "NewContainerCommand");

        if (handled)
        {
            return;
        }

        var invocation = await NativePresentationReflection.InvokeAsync(
            this,
            ["OpenProjectCreatorAsync", "OpenProjectCreator", "OpenNewContainer", "CreateProjectAsync"],
            Array.Empty<object?>());

        if (!invocation.Invoked)
        {
            throw new InvalidOperationException("The project creator route is unavailable.");
        }
    }

    private async Task OpenProjectFallbackAsync(object project)
    {
        var handled = await NativePresentationReflection.ExecuteCommandAsync(
            this,
            project,
            "OpenProjectCommand",
            "SelectProjectCommand",
            "OpenContainerCommand",
            "SelectContainerCommand");

        if (handled)
        {
            return;
        }

        var invocation = await NativePresentationReflection.InvokeAsync(
            this,
            ["OpenProjectAsync", "OpenProject", "OpenContainerAsync", "OpenContainer"],
            project);

        if (!invocation.Invoked)
        {
            throw new InvalidOperationException("The selected project could not be opened.");
        }
    }

    private async Task ArchiveProjectFallbackAsync(object project)
    {
        var handled = await NativePresentationReflection.ExecuteCommandAsync(
            this,
            project,
            "ArchiveProjectCommand",
            "ArchiveContainerCommand");

        if (handled)
        {
            return;
        }

        var invocation = await NativePresentationReflection.InvokeAsync(
            this,
            ["ArchiveProjectAsync", "ArchiveProject", "ArchiveContainerAsync", "ArchiveContainer"],
            project);

        if (!invocation.Invoked)
        {
            throw new InvalidOperationException("The selected project could not be archived.");
        }
    }

    private void DisposeNativeProjectsPage()
    {
        _nativeProjectsPage?.Dispose();
        _nativeProjectsPage = null;
    }

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

        DisposeNativeProjectsPage();
        _legacyProjectsContent = null;

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
