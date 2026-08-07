using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Pages.Chat;
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
    private TranslateTransform? _globalCallTranslation;
    private GlobalCallWidget? _globalCallWidget;
    private ChatExecutionStatusControl? _globalExecutionStatus;
    private InChatCallWidgetViewModel? _globalCallViewModel;
    private NativeProjectsPage? _nativeProjectsPage;
    private readonly NativeProjectUiStateStore _nativeProjectUiStateStore = new();

    public void OpenVoiceSession()
    {
        AttachBetaOverlays();

        if (_globalCallViewModel is null || _globalCallWidget is null)
        {
            return;
        }

        _globalCallViewModel.Open();
        _globalCallWidget.IsVisible = true;
    }

    private async Task OpenVoiceSessionFromActionAsync()
    {
        AttachBetaOverlays();
        if (_globalCallViewModel is null)
        {
            return;
        }

        Guid? conversationId = null;
        ModelDescriptor? selectedModel = null;
        if (CurrentPage is NewChatPage chat)
        {
            conversationId = chat.ConversationId;
            selectedModel = chat.SelectedModel;
        }

        selectedModel ??= CurrentChat.SelectedModel;
        if (selectedModel is null)
        {
            try
            {
                var models = await _ollama.GetModelsAsync(CancellationToken.None);
                selectedModel = models.FirstOrDefault(model => model.Supports(ToolCapability.Text))
                    ?? models.FirstOrDefault();
            }
            catch
            {
                // The model card and disabled Start button expose the unavailable
                // state without navigating away or fabricating a default model.
            }
        }

        // Voice is an application action, not a navigation destination. It can
        // float over Home, Projects, Study, Research, or any other current page.
        _globalCallViewModel.AttachConversation(conversationId, selectedModel);

        if (_globalCallViewModel.IsOpen && !_globalCallViewModel.IsActive)
        {
            _globalCallViewModel.Close();
            return;
        }

        OpenVoiceSession();
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
        _globalCallWidget.DragDelta += OnGlobalCallDragDelta;
        _globalCallTranslation = new TranslateTransform();
        _globalCallWidget.HorizontalAlignment = HorizontalAlignment.Right;
        _globalCallWidget.VerticalAlignment = VerticalAlignment.Bottom;
        _globalCallWidget.Margin = new Thickness(20);
        _globalCallWidget.RenderTransform = _globalCallTranslation;
        _globalCallWidget.IsVisible = false;
        _globalCallWidget.SetValue(Panel.ZIndexProperty, 30);
        NativeOverlayLayer.Children.Add(_globalCallWidget);

        _globalExecutionStatus = new ChatExecutionStatusControl
        {
            MaxWidth = 720,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(20)
        };
        _globalExecutionStatus.Snapshot = _sessions.CurrentExecution;
        _globalExecutionStatus.SetValue(Panel.ZIndexProperty, 20);
        NativeOverlayLayer.Children.Add(_globalExecutionStatus);

        _sessions.ExecutionChanged += OnExecutionChanged;
        _globalCallViewModel.PropertyChanged += OnGlobalCallPropertyChanged;
        PageContent.PropertyChanged += OnPageContentPropertyChanged;

        RedirectLegacyPresentation(PageContent.Content);
    }

    private void OnGlobalCallDragDelta(object? sender, Vector delta)
    {
        if (_globalCallTranslation is null || _globalCallWidget is null)
        {
            return;
        }

        var availableWidth = Math.Max(0, NativeOverlayLayer.Bounds.Width - _globalCallWidget.Bounds.Width - 40);
        var availableHeight = Math.Max(0, NativeOverlayLayer.Bounds.Height - _globalCallWidget.Bounds.Height - 40);
        _globalCallTranslation.X = Math.Clamp(_globalCallTranslation.X + delta.X, -availableWidth, 0);
        _globalCallTranslation.Y = Math.Clamp(_globalCallTranslation.Y + delta.Y, -availableHeight, 0);
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

        if (_globalCallWidget is not null && _globalCallViewModel is not null)
        {
            _globalCallWidget.IsVisible = _globalCallViewModel.IsVisible;
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
            // A retired/restored Call tab must never activate Voice. Voice is opened
            // exclusively from Actions; the legacy route is discarded silently.
            if (_lastNonCallContent is null)
            {
                _ = OpenGoAsync();
            }
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

        var nativePage = CreateNativeProjectsPage(legacyContent);

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

    private NativeProjectsPage CreateNativeProjectsPage(object source)
    {
        _legacyProjectsContent = source;
        DisposeNativeProjectsPage();

        var nativePage = new NativeProjectsPage(
            source,
            ReadFallbackProjects,
            OpenProjectCreatorFallbackAsync,
            OpenProjectFallbackAsync,
            ArchiveProjectFallbackAsync,
            _nativeProjectUiStateStore);

        _nativeProjectsPage = nativePage;
        return nativePage;
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

        if (_globalCallWidget is not null) NativeOverlayLayer.Children.Remove(_globalCallWidget);
        if (_globalExecutionStatus is not null) NativeOverlayLayer.Children.Remove(_globalExecutionStatus);

        DisposeNativeProjectsPage();
        _legacyProjectsContent = null;

        if (_globalCallWidget is not null)
        {
            _globalCallWidget.DragDelta -= OnGlobalCallDragDelta;
        }

        _globalCallWidget?.Dispose();
        _globalCallViewModel?.Dispose();
        _globalExecutionStatus?.Dispose();

        _globalCallTranslation = null;
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
