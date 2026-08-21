#if !ANDROID
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Views.Pages.Go;
using Haven.UI;
using HavenButton = Haven.UI.Components.Button;
using HavenInput = Haven.UI.Components.Input;

namespace Haven.Desktop.Overlay;

/// <summary>
/// Thin native host for one compact Overlay session. Chat and Go are execution/session backends only;
/// the only visible product surface is the Haven.UI Overlay scene.
/// </summary>
internal sealed class OverlayWorkspaceWindow : Window
{
    private const double ExpandedMinWidth = 380;
    private const double ExpandedMaxWidth = 620;
    private const double ExpandedMinHeight = 300;
    private const double ExpandedMaxHeight = 560;
    private const double CollapsedMinWidth = 340;
    private const double CollapsedHeight = 72;

    private readonly NewChatPage? _chatPage;
    private readonly List<(HavenButton Button, EventHandler Handler)> _goSuggestionSubscriptions = [];
    private bool _closingFromController;
    private bool _isCollapsed;

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
            throw new ArgumentException("An Overlay window requires a real Haven execution backend.", nameof(session));

        _chatPage = chatPage;
        GoPage = goPage;
        SessionId = session.Id;
        ShellScene = new OverlayShellHavenScene();
        ShellControl = new HavenSceneControl { Root = ShellScene.Root };

        Title = "Haven Overlay — " + session.Title;
        Width = Math.Clamp(session.Geometry.Width, ExpandedMinWidth, ExpandedMaxWidth);
        Height = Math.Clamp(session.Geometry.Height, ExpandedMinHeight, ExpandedMaxHeight);
        MinWidth = ExpandedMinWidth;
        MaxWidth = ExpandedMaxWidth;
        MinHeight = ExpandedMinHeight;
        MaxHeight = ExpandedMaxHeight;
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
        AutomationProperties.SetName(ShellControl, "Haven Overlay compact surface");

        Content = CreateVisualRoot(ShellControl);

        ShellScene.DragDelta += OnDragDelta;
        ShellScene.CaptureRequested += (_, _) => CaptureRequested?.Invoke(this, EventArgs.Empty);
        ShellScene.NewChatRequested += (_, _) => NewChatRequested?.Invoke(this, EventArgs.Empty);
        ShellScene.PinToggleRequested += (_, _) => PinToggleRequested?.Invoke(this, EventArgs.Empty);
        ShellScene.CollapseToggleRequested += (_, _) => CollapseToggleRequested?.Invoke(this, EventArgs.Empty);
        ShellScene.CloseRequested += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        ShellScene.AddRequested += (_, _) => _ = AttachFilesAsync();
        ShellScene.SubmitRequested += (_, instruction) => SubmitCompact(instruction);
        ShellScene.SuggestionRequested += (_, index) => InvokeGoSuggestion(index);
        ShellScene.SessionActivated += (_, id) => SessionActivated?.Invoke(this, id);
        ShellScene.ActionRequested += (_, action) => ActionRequested?.Invoke(this, action);
        ShellControl.InputSubmitted += OnShellInputSubmitted;
        PositionChanged += (_, _) => PublishGeometry();
        SizeChanged += (_, _) => PublishGeometry();

        if (goPage is not null) WireGoSuggestions(goPage);

        Closed += (_, _) =>
        {
            if (!_closingFromController) NativeCloseRequested?.Invoke(this, EventArgs.Empty);
            ShellControl.InputSubmitted -= OnShellInputSubmitted;
            foreach (var (button, handler) in _goSuggestionSubscriptions) button.Invalidated -= handler;
            _goSuggestionSubscriptions.Clear();
            ShellScene.Dispose();
            _chatPage?.Dispose();
            GoPage?.Dispose();
        };

        ApplySnapshot(new OverlayWorkspaceSnapshot(session.Id, [session]));
    }

    public Guid SessionId { get; }
    public NewChatPage ChatPage => _chatPage ?? throw new InvalidOperationException("This Overlay session uses Go as its execution backend.");
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
    public event EventHandler<OverlaySurfaceGeometry>? GeometryChanged;

    internal static Control CreateVisualRoot(HavenSceneControl shellControl)
    {
        ArgumentNullException.ThrowIfNull(shellControl);
        return shellControl;
    }

    public void ApplySnapshot(OverlayWorkspaceSnapshot snapshot)
    {
        ShellScene.ApplySnapshot(snapshot, SessionId);
        var current = snapshot.Sessions.FirstOrDefault(session => session.Id == SessionId);
        if (current is null) return;

        _isCollapsed = current.IsCollapsed;
        CanResize = !_isCollapsed;

        if (_isCollapsed)
        {
            MinWidth = CollapsedMinWidth;
            MaxWidth = ExpandedMaxWidth;
            MinHeight = CollapsedHeight;
            MaxHeight = CollapsedHeight;
            Width = Math.Clamp(current.Geometry.Width, CollapsedMinWidth, ExpandedMaxWidth);
            Height = CollapsedHeight;
        }
        else
        {
            MinWidth = ExpandedMinWidth;
            MaxWidth = ExpandedMaxWidth;
            MinHeight = ExpandedMinHeight;
            MaxHeight = ExpandedMaxHeight;
            Width = Math.Clamp(current.Geometry.Width, ExpandedMinWidth, ExpandedMaxWidth);
            Height = Math.Clamp(current.Geometry.Height, ExpandedMinHeight, ExpandedMaxHeight);
        }

        Position = new PixelPoint((int)Math.Round(current.Geometry.X), (int)Math.Round(current.Geometry.Y));
        if (IsVisible) EnsureVisibleOnAvailableScreen();
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
        EnsureVisibleOnAvailableScreen();
        Activate();
        SyncDraftFromBackend();
        SyncGoSuggestions();
        if (!_isCollapsed) ShellControl.FocusElement(ShellScene.ComposerInput);
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

    private void OnShellInputSubmitted(HavenInput input)
    {
        if (ReferenceEquals(input, ShellScene.ComposerInput)) ShellScene.SubmitComposer();
    }

    private void SubmitCompact(string instruction)
    {
        var pending = instruction?.Trim();
        if (string.IsNullOrWhiteSpace(pending)) return;

        if (_chatPage is not null)
        {
            _chatPage.Submit(pending);
            ShellScene.ClearDraft();
            ShellScene.SetStatus("Sent to Chat");
            return;
        }

        if (GoPage is not { } goPage) return;
        goPage.Route.Instruction.Text = pending;
        goPage.Route.Instruction.PlaceCaretAtEnd();
        if (!goPage.SceneHost.ActivateElementForAutomation(goPage.Route.Instruction))
        {
            ShellScene.SetStatus("Haven could not submit that instruction.");
            return;
        }

        ShellScene.ClearDraft();
        ShellScene.SetStatus("Sent to Go");
    }

    private async Task AttachFilesAsync()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Add files to Haven Overlay",
                AllowMultiple = true
            });
            var paths = files.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray();
            if (paths.Length == 0) return;

            if (_chatPage is not null) await _chatPage.AddFilesAsync(paths);
            else GoPage?.AttachFiles(paths);

            ShellScene.SetStatus(paths.Length == 1
                ? "Attached " + Path.GetFileName(paths[0])
                : $"Attached {paths.Length} files");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShellScene.SetStatus("Could not attach files: " + exception.Message);
        }
    }

    private void WireGoSuggestions(GoPage goPage)
    {
        for (var index = 0; index < 4; index++)
        {
            var button = goPage.Route.SuggestionButtons(index).FirstOrDefault();
            if (button is null) continue;
            EventHandler handler = (_, _) => SyncGoSuggestions();
            button.Invalidated += handler;
            _goSuggestionSubscriptions.Add((button, handler));
        }
        SyncGoSuggestions();
    }

    private void SyncGoSuggestions()
    {
        if (GoPage is not { } goPage)
        {
            ShellScene.SetSuggestions([]);
            return;
        }

        var suggestions = new List<OverlayCompactSuggestion>(4);
        for (var index = 0; index < 4; index++)
        {
            var button = goPage.Route.SuggestionButtons(index).FirstOrDefault();
            if (button is null || string.IsNullOrWhiteSpace(button.Content)) continue;
            suggestions.Add(new OverlayCompactSuggestion(
                button.Content,
                string.IsNullOrWhiteSpace(button.Accessibility.Description) ? button.Content : button.Accessibility.Description!));
        }
        ShellScene.SetSuggestions(suggestions);
    }

    private void InvokeGoSuggestion(int index)
    {
        if (GoPage is not { } goPage || index is < 0 or > 3) return;
        var button = goPage.Route.SuggestionButtons(index).FirstOrDefault();
        if (button is null || !goPage.SceneHost.ActivateElementForAutomation(button))
            ShellScene.SetStatus("That suggestion is unavailable right now.");
    }

    private void SyncDraftFromBackend()
    {
        string? backendDraft = null;
        if (_chatPage?.Scene.Root is { } chatRoot)
        {
            backendDraft = chatRoot.DescendantsAndSelf()
                .OfType<HavenInput>()
                .FirstOrDefault(input => input.Name.EndsWith("Instruction", StringComparison.OrdinalIgnoreCase))
                ?.Text;
        }
        else if (GoPage is { } goPage)
        {
            backendDraft = goPage.Route.Instruction.Text;
        }

        if (!string.IsNullOrWhiteSpace(backendDraft)) ShellScene.SetDraft(backendDraft);
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
