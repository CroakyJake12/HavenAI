using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using Haven.Application;
using Haven.Android;
using Haven.Desktop.HavenUI.Components;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private IProjectorSessionCoordinator? _projectorSessions;
    private AndroidProjectorControllerActionDispatcher? _projectorControllerDispatcher;
    private HavenMobileSheet? _projectorControllerSheet;
    private TextBlock? _projectorControllerTitle;
    private TextBlock? _projectorControllerSubtitle;
    private TextBlock? _projectorControllerStatus;
    private StackPanel? _projectorControllerActions;
    private bool _projectorControllerDetachHooked;

    public void AttachProjectorControllerSession(
        IProjectorSessionCoordinator sessions,
        AndroidProjectorControllerActionDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(dispatcher);
        if (!_mobileLayoutApplied)
            throw new InvalidOperationException("Apply Haven's mobile layout before attaching the Projector phone controller.");
        if (Content is not Grid root)
            throw new InvalidOperationException("Haven's mobile shell requires the MainView root grid.");

        if (_projectorSessions is not null)
            _projectorSessions.StateChanged -= OnProjectorSessionChanged;

        _projectorSessions = sessions;
        _projectorControllerDispatcher = dispatcher;

        if (_projectorControllerSheet is null)
        {
            _projectorControllerSheet = BuildProjectorControllerSheet();
            Grid.SetRowSpan(_projectorControllerSheet, 2);
            _projectorControllerSheet.ZIndex = 80;
            root.Children.Add(_projectorControllerSheet);
        }

        if (!_projectorControllerDetachHooked)
        {
            _projectorControllerDetachHooked = true;
            DetachedFromVisualTree += (_, _) => DetachProjectorControllerSession();
        }

        _projectorSessions.StateChanged += OnProjectorSessionChanged;
        RefreshProjectorController(_projectorSessions.Current);
    }

    private HavenMobileSheet BuildProjectorControllerSheet()
    {
        _projectorControllerTitle = new TextBlock
        {
            FontSize = 14,
            FontWeight = Avalonia.Media.FontWeight.Bold
        };
        _projectorControllerSubtitle = new TextBlock
        {
            FontSize = 11,
            Foreground = ResourceBrush("HavenTextSoftBrush")
        };
        _projectorControllerStatus = new TextBlock
        {
            FontSize = 11,
            Foreground = ResourceBrush("HavenTextSoftBrush")
        };
        _projectorControllerActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6
        };

        var actionsScroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _projectorControllerActions
        };

        return new HavenMobileSheet
        {
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(8, 0, 8, 98),
            Padding = new Thickness(14, 10),
            CornerRadius = new CornerRadius(22),
            Background = ResourceBrush("HavenElevatedBrush"),
            BorderBrush = ResourceBrush("HavenAccentBorderBrush"),
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Spacing = 7,
                Children =
                {
                    _projectorControllerTitle,
                    _projectorControllerSubtitle,
                    actionsScroller,
                    _projectorControllerStatus
                }
            }
        };
    }

    private void OnProjectorSessionChanged(ProjectorSessionSnapshot? session)
        => Dispatcher.UIThread.Post(() => RefreshProjectorController(session));

    private void RefreshProjectorController(ProjectorSessionSnapshot? session)
    {
        if (_projectorControllerSheet is null
            || _projectorControllerTitle is null
            || _projectorControllerSubtitle is null
            || _projectorControllerStatus is null
            || _projectorControllerActions is null)
        {
            return;
        }

        _projectorControllerActions.Children.Clear();
        if (session is null || session.State is ProjectorSessionState.Disconnected or ProjectorSessionState.Stopping or ProjectorSessionState.Failed)
        {
            _projectorControllerSheet.IsVisible = false;
            return;
        }

        _projectorControllerTitle.Text = "Projector · " + session.TargetDisplay.Name;
        var experienceLabel = string.IsNullOrWhiteSpace(session.CurrentExperienceId)
            ? session.State.ToString()
            : session.CurrentExperienceId;
        _projectorControllerSubtitle.Text = $"{experienceLabel} · {session.TargetDisplay.Trust} display";

        foreach (var trust in new[]
        {
            ProjectorDisplayTrust.Private,
            ProjectorDisplayTrust.Trusted,
            ProjectorDisplayTrust.Shared,
            ProjectorDisplayTrust.Public
        })
        {
            var capturedTrust = trust;
            var label = trust == session.TargetDisplay.Trust ? $"{trust} ✓" : trust.ToString();
            _projectorControllerActions.Children.Add(MobileButton(
                label,
                "bolt",
                () => SetProjectorTrust(session.Id, capturedTrust),
                10));
        }

        if (session.State != ProjectorSessionState.Active)
        {
            _projectorControllerStatus.Text = "Display trust controls which experiences Projector may reveal.";
            _projectorControllerSheet.IsVisible = true;
            return;
        }

        var controller = session.Controller;
        if (controller is null || controller.Actions.Count == 0)
        {
            _projectorControllerStatus.Text = "This experience hasn't exposed phone controls yet.";
        }
        else
        {
            _projectorControllerStatus.Text = controller.Id;
            foreach (var action in controller.Actions)
            {
                var captured = action;
                _projectorControllerActions.Children.Add(MobileButton(
                    captured.Label,
                    string.IsNullOrWhiteSpace(captured.IconKey) ? "bolt" : captured.IconKey,
                    () => _ = InvokeProjectorControllerActionAsync(session.Id, captured),
                    10));
            }
        }

        _projectorControllerSheet.IsVisible = true;
    }

    private void SetProjectorTrust(Guid sessionId, ProjectorDisplayTrust trust)
    {
        var sessions = _projectorSessions;
        var current = sessions?.Current;
        if (sessions is null || current is null || current.Id != sessionId)
            return;

        try
        {
            var updated = sessions.SetTargetTrust(trust);
            if (_projectorControllerStatus is not null)
                _projectorControllerStatus.Text = $"{updated.TargetDisplay.Name} is now classified as {trust}.";
        }
        catch (Exception exception)
        {
            if (_projectorControllerStatus is not null)
                _projectorControllerStatus.Text = "Display trust could not be changed: " + exception.Message;
        }
    }

    private async Task InvokeProjectorControllerActionAsync(Guid sessionId, ProjectorControllerAction action)
    {
        var sessions = _projectorSessions;
        var dispatcher = _projectorControllerDispatcher;
        var current = sessions?.Current;
        if (sessions is null
            || dispatcher is null
            || current is null
            || current.Id != sessionId
            || current.State != ProjectorSessionState.Active)
        {
            return;
        }

        var result = await dispatcher.InvokeAsync(current, action);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var latest = _projectorSessions?.Current;
            if (_projectorControllerStatus is null
                || latest is null
                || latest.Id != sessionId
                || latest.State != ProjectorSessionState.Active)
            {
                return;
            }

            _projectorControllerStatus.Text = result.Message;
        });
    }

    private void DetachProjectorControllerSession()
    {
        if (_projectorSessions is not null)
            _projectorSessions.StateChanged -= OnProjectorSessionChanged;
        _projectorSessions = null;
        _projectorControllerDispatcher = null;
        if (_projectorControllerSheet is not null)
            _projectorControllerSheet.IsVisible = false;
    }
}
