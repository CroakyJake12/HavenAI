#if !ANDROID
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Views.Pages.Go;
using Haven.UI;

namespace Haven.Desktop.Overlay;

/// <summary>Thin native host for one Overlay session; visible product chrome remains Haven.UI-owned.</summary>
internal sealed class OverlayWorkspaceWindow : Window
{
    private readonly NewChatPage? _chatPage;
    private bool _closingFromController;

    public OverlayWorkspaceWindow(OverlaySessionState session, NewChatPage chatPage)
        : this(session, chatPage, null)
    {
    }

    public OverlayWorkspaceWindow(OverlaySessionState session, GoPage goPage)
        : this(session, null, goPage)
    {
    }

    private OverlayWorkspaceWindow(OverlaySessionState session, NewChatPage? chatPage, GoPage? goPage)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (chatPage is null && goPage is null)
            throw new ArgumentException("An Overlay window requires a real Haven app surface.", nameof(session));

        _chatPage = chatPage;
        GoPage = goPage;
        BodyControl = (Control?)chatPage ?? goPage!;
        SessionId = session.Id;
        ShellScene = new OverlayShellHavenScene();
        ShellControl = new HavenSceneControl { Root = ShellScene.Root };

        Title = "Haven Overlay — " + session.Title;
        Width = session.Geometry.Width;
        Height = session.Geometry.Height;
        MinWidth = 420;
        MinHeight = 360;
        CanResize = true;
        ShowInTaskbar = false;
        Topmost = true;
        WindowDecorations = WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyBackgroundFallback = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint((int)Math.Round(session.Geometry.X), (int)Math.Round(session.Geometry.Y));

        AutomationProperties.SetAutomationId(this, "HavenOverlayWindow-" + session.Id.ToString("N"));
        AutomationProperties.SetName(this, "Haven Overlay " + session.Title);
        AutomationProperties.SetAutomationId(ShellControl, "HavenOverlayShell");
        AutomationProperties.SetName(ShellControl, "Haven Overlay controls");
        AutomationProperties.SetAutomationId(BodyControl, goPage is not null ? "HavenOverlayGo" : "HavenOverlayChat");
        AutomationProperties.SetName(BodyControl, goPage is not null ? "Haven Overlay Go workspace" : "Haven Overlay Chat workspace");

        if (goPage is not null)
        {
            ShellScene.SourceText.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
            ShellScene.SessionTabs.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
            ShellScene.ContextPanel.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
            ShellScene.Actions.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        }

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 7,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        root.Children.Add(ShellControl);
        Grid.SetRow(BodyControl, 1);
        root.Children.Add(BodyControl);
        Content = root;

        ShellScene.DragDelta += OnDragDelta;
        ShellScene.NewChatRequested += (_, _) => NewChatRequested?.Invoke(this, EventArgs.Empty);
        ShellScene.PinToggleRequested += (_, _) => PinToggleRequested?.Invoke(this, EventArgs.Empty);
        ShellScene.CollapseToggleRequested += (_, _) => CollapseToggleRequested?.Invoke(this, EventArgs.Empty);
        ShellScene.CloseRequested += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        ShellScene.SessionActivated += (_, id) => SessionActivated?.Invoke(this, id);
        ShellScene.ActionRequested += (_, action) => ActionRequested?.Invoke(this, action);
        PositionChanged += (_, _) => PublishGeometry();
        SizeChanged += (_, _) => PublishGeometry();
        Closed += (_, _) =>
        {
            if (!_closingFromController) NativeCloseRequested?.Invoke(this, EventArgs.Empty);
            ShellScene.Dispose();
            _chatPage?.Dispose();
            GoPage?.Dispose();
        };
    }

    public Guid SessionId { get; }
    public Control BodyControl { get; }
    public NewChatPage ChatPage => _chatPage ?? throw new InvalidOperationException("This Overlay session hosts Go rather than Chat.");
    public GoPage? GoPage { get; }
    public OverlayShellHavenScene ShellScene { get; }
    public HavenSceneControl ShellControl { get; }
    public bool WorkspaceVisible => IsVisible;

    public event EventHandler? NewChatRequested;
    public event EventHandler? PinToggleRequested;
    public event EventHandler? CollapseToggleRequested;
    public event EventHandler? CloseRequested;
    public event EventHandler? NativeCloseRequested;
    public event EventHandler<Guid>? SessionActivated;
    public event EventHandler<OverlayContextActionDescriptor>? ActionRequested;
    public event EventHandler<OverlaySurfaceGeometry>? GeometryChanged;

    public void ApplySnapshot(OverlayWorkspaceSnapshot snapshot)
    {
        ShellScene.ApplySnapshot(snapshot, SessionId);
        var current = snapshot.Sessions.FirstOrDefault(session => session.Id == SessionId);
        if (current is null) return;

        var showExpandedChrome = !current.IsCollapsed && GoPage is null;
        ShellScene.SourceText.SetValue(HavenProperties.Visibility, showExpandedChrome ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        ShellScene.SessionTabs.SetValue(HavenProperties.Visibility, showExpandedChrome ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        ShellScene.ContextPanel.SetValue(HavenProperties.Visibility, showExpandedChrome ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        ShellScene.Actions.SetValue(HavenProperties.Visibility, showExpandedChrome ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        BodyControl.IsVisible = !current.IsCollapsed;
        CanResize = !current.IsCollapsed;

        if (current.IsCollapsed)
        {
            MinHeight = 72;
            if (Height > 120) Height = 96;
            return;
        }

        MinHeight = 360;
        if (Height > 120) return;
        Width = current.Geometry.Width;
        Height = current.Geometry.Height;
        Position = new PixelPoint((int)Math.Round(current.Geometry.X), (int)Math.Round(current.Geometry.Y));
    }

    public void SetActions(IReadOnlyList<OverlayContextActionDescriptor> actions) => ShellScene.SetActions(actions);

    public OverlaySurfaceGeometry CaptureGeometry() => new(
        Math.Max(MinWidth, Width),
        Math.Max(MinHeight, Height),
        Position.X,
        Position.Y);

    public void ShowAndActivate()
    {
        if (!IsVisible) Show();
        Activate();
        if (_chatPage is not null) _chatPage.FocusComposer();
        else GoPage?.FocusComposer();
    }

    public void HideWorkspace()
    {
        if (IsVisible) Hide();
    }

    public void CloseFromController()
    {
        if (_closingFromController) return;
        _closingFromController = true;
        Close();
    }

    private void OnDragDelta(HavenPoint delta)
    {
        Position = new PixelPoint(
            Position.X + (int)Math.Round(delta.X),
            Position.Y + (int)Math.Round(delta.Y));
    }

    private void PublishGeometry() => GeometryChanged?.Invoke(this, CaptureGeometry());
}
#endif
