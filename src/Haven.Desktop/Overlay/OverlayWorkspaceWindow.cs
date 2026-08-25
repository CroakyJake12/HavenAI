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
        Width = Math.Clamp(session.Geometry.Width, 400, 560);
        Height = Math.Clamp(session.Geometry.Height, 240, 480);
        MinWidth = 400;
        MinHeight = 240;
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

        Content = CreateVisualRoot(ShellControl, chatPage, goPage);
        ShellControl.InputSubmitted += input =>
        {
            if (ReferenceEquals(input, ShellScene.ComposerInput)) ShellScene.SubmitComposer();
        };
        ShellScene.DragDelta += OnDragDelta;
        ShellScene.CaptureRequested += (_, _) => CaptureRequested?.Invoke(this, EventArgs.Empty);
        ShellScene.NewChatRequested += (_, _) => NewChatRequested?.Invoke(this, EventArgs.Empty);
        ShellScene.PinToggleRequested += (_, _) => PinToggleRequested?.Invoke(this, EventArgs.Empty);
        ShellScene.CollapseToggleRequested += (_, _) => CollapseToggleRequested?.Invoke(this, EventArgs.Empty);
        ShellScene.CloseRequested += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        ShellScene.SessionActivated += (_, id) => SessionActivated?.Invoke(this, id);
        ShellScene.ActionRequested += (_, action) => ActionRequested?.Invoke(this, action);
        ShellScene.AddRequested += (_, _) => AddRequested?.Invoke(this, EventArgs.Empty);
        ShellScene.SubmitRequested += (_, text) => SubmitRequested?.Invoke(this, text);
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

    public event EventHandler? CaptureRequested;
    public event EventHandler? NewChatRequested;
    public event EventHandler? PinToggleRequested;
    public event EventHandler? CollapseToggleRequested;
    public event EventHandler? CloseRequested;
    public event EventHandler? NativeCloseRequested;
    public event EventHandler<Guid>? SessionActivated;
    public event EventHandler<OverlayContextActionDescriptor>? ActionRequested;
    public event EventHandler? AddRequested;
    public event EventHandler<string>? SubmitRequested;
    public event EventHandler<OverlaySurfaceGeometry>? GeometryChanged;

    internal static Control CreateVisualRoot(HavenSceneControl shellControl, params Control?[] executionBackends)
    {
        ArgumentNullException.ThrowIfNull(shellControl);
        _ = executionBackends;
        return shellControl;
    }

    public void ApplySnapshot(OverlayWorkspaceSnapshot snapshot)
    {
        ShellScene.ApplySnapshot(snapshot, SessionId);
        var current = snapshot.Sessions.FirstOrDefault(session => session.Id == SessionId);
        if (current is null) return;

        Title = "Haven Overlay - " + current.Title;
        if (current.IsCollapsed)
        {
            CanResize = false;
            MinWidth = 300;
            MinHeight = 56;
            Width = 360;
            Height = 64;
            Position = new PixelPoint((int)Math.Round(current.Geometry.X), (int)Math.Round(current.Geometry.Y));
            if (IsVisible) EnsureVisibleOnAvailableScreen();
            return;
        }

        CanResize = true;
        MinWidth = 400;
        MinHeight = 240;
        Width = Math.Clamp(current.Geometry.Width, 400, 560);
        Height = Math.Clamp(current.Geometry.Height, 240, 480);
        Position = new PixelPoint((int)Math.Round(current.Geometry.X), (int)Math.Round(current.Geometry.Y));
        if (IsVisible) EnsureVisibleOnAvailableScreen();
    }    public void SetActions(IReadOnlyList<OverlayContextActionDescriptor> actions) => ShellScene.SetActions(actions);
    public void SetSuggestions(IReadOnlyList<string> labels) => ShellScene.SetSuggestions(labels);
    public void SetActionProgress(string label, int current = 1, int total = 1) => ShellScene.SetActionProgress(label, current, total);
    public void SetActionResult(string message, bool success = true) => ShellScene.SetActionResult(message, success);
    public void ClearActionFeedback() => ShellScene.ClearActionFeedback();

    public OverlaySurfaceGeometry CaptureGeometry() => new(
        Math.Max(MinWidth, Width),
        Math.Max(MinHeight, Height),
        Position.X,
        Position.Y);

    public void ShowWithoutActivation()
    {
        if (!IsVisible)
        {
            ShowActivated = false;
            Show();
        }
        EnsureVisibleOnAvailableScreen();
    }

    public void ShowAndActivate()
    {
        if (!IsVisible) Show();
        ShowActivated = true;
        EnsureVisibleOnAvailableScreen();
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

    private void EnsureVisibleOnAvailableScreen()
    {
        var screens = Screens.All;
        if (screens.Count == 0) return;
        var screen = screens.FirstOrDefault(candidate => Contains(candidate.WorkingArea, Position))
                     ?? Screens.Primary
                     ?? screens[0];
        Position = ClampPositionToWorkingArea(Position, screen.WorkingArea, screen.Scaling, Width, Height);
    }

    internal static PixelPoint ClampPositionToWorkingArea(
        PixelPoint desired,
        PixelRect workingArea,
        double scaling,
        double width,
        double height)
    {
        scaling = scaling <= 0 ? 1 : scaling;
        var widthPixels = Math.Max(1, (int)Math.Ceiling(width * scaling));
        var heightPixels = Math.Max(1, (int)Math.Ceiling(height * scaling));
        var maxX = Math.Max(workingArea.X, workingArea.Right - widthPixels);
        var maxY = Math.Max(workingArea.Y, workingArea.Bottom - heightPixels);
        return new PixelPoint(
            Math.Clamp(desired.X, workingArea.X, maxX),
            Math.Clamp(desired.Y, workingArea.Y, maxY));
    }

    private static bool Contains(PixelRect area, PixelPoint point) =>
        point.X >= area.X && point.X < area.Right && point.Y >= area.Y && point.Y < area.Bottom;

    private void PublishGeometry() => GeometryChanged?.Invoke(this, CaptureGeometry());
}
#endif
