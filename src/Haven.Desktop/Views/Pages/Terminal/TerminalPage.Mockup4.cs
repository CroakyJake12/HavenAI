using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Haven.Application;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenContainer = Haven.UI.Components.Container;
using HavenInput = Haven.UI.Components.Input;
using HavenPage = Haven.UI.Components.Page;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Terminal;

public sealed partial class TerminalPage
{
    private HavenInput? _huiCommandInput;
    private HavenSceneControl? _huiScene;
    private HavenText? _huiStatusText;
    private HavenText? _huiCwdText;

    private Control BuildHuiWorkspace()
    {
        _scroll.Content = _lines;
        Avalonia.Automation.AutomationProperties.SetAutomationId(_scroll, "Terminal.Transcript");

        var runOnce = Btn("Run once");
        runOnce.Click += async (_, _) => await ApproveAsync();
        var deny = Btn("Deny");
        deny.Click += (_, _) => Deny();
        _approvalText.Foreground = Avalonia.Media.Brushes.White;
        _approval.Child = new Grid
        {
            ColumnDefinitions = new("*,Auto,Auto"),
            ColumnSpacing = 8,
            Children = { _approvalText, Col(runOnce, 1), Col(deny, 2) }
        };

        var root = new HavenPage { Name = "Terminal.Root", Layout = HavenLayout.Vertical };
        root.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        root.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        root.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        root.SetValue(HavenProperties.Padding, HavenThickness.Parse("14px 20px 14px 20px"));
        root.Accessibility.AccessibleName = "Haven Terminal workspace";

        var titleRow = new HavenContainer { Name = "Terminal.TitleRow", Layout = HavenLayout.Horizontal };
        titleRow.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        titleRow.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        var title = new HavenText { Name = "Terminal.Title", Content = "Terminal", Level = TextLevel.H1 };
        title.SetValue(HavenProperties.FontSize, 19d);
        titleRow.Add(title);
        var subtitle = new HavenText { Name = "Terminal.Subtitle", Content = "PowerShell workspace" };
        subtitle.SetValue(HavenProperties.Foreground, "TextSecondary");
        subtitle.SetValue(HavenProperties.FontSize, 12d);
        titleRow.Add(subtitle);
        root.Add(titleRow);

        var commandBar = new HavenContainer { Name = "Terminal.CommandBar", Layout = HavenLayout.Horizontal };
        commandBar.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        commandBar.SetValue(HavenProperties.MinHeight, HavenLength.Px(66));
        commandBar.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        commandBar.SetValue(HavenProperties.Padding, HavenThickness.Parse("8px 10px"));
        commandBar.SetValue(HavenProperties.Background, "SurfaceRaised");
        commandBar.SetValue(HavenProperties.BorderColor, "AccentSecondary");
        commandBar.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        commandBar.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(14)));

        var promptMark = new HavenText { Name = "Terminal.CommandMark", Content = ">_" };
        promptMark.SetValue(HavenProperties.Foreground, "AccentSecondary");
        promptMark.SetValue(HavenProperties.FontSize, 18d);
        commandBar.Add(promptMark);

        _huiCommandInput = new HavenInput
        {
            Name = "Terminal.CommandInput",
            Placeholder = "Type a command...",
            SubmitOnEnter = true
        };
        _huiCommandInput.SetValue(HavenProperties.Width, HavenLength.Fr(1));
        _huiCommandInput.SetValue(HavenProperties.MinHeight, HavenLength.Px(48));
        _huiCommandInput.SetValue(HavenProperties.Background, "Transparent");
        _huiCommandInput.SetValue(HavenProperties.BorderWidth, HavenLength.Px(0));
        _huiCommandInput.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(10)));
        commandBar.Add(_huiCommandInput);

        var enterHint = new HavenText { Name = "Terminal.EnterHint", Content = "Enter to run" };
        enterHint.SetValue(HavenProperties.Foreground, "TextSecondary");
        enterHint.SetValue(HavenProperties.FontSize, 11d);
        commandBar.Add(enterHint);

        var run = Action("Terminal.Run", "Run", "play");
        run.Variant = ButtonVariant.Primary;
        run.Invoked += (_, _) => _ = SubmitFromHuiAsync();
        commandBar.Add(run);
        root.Add(commandBar);

        var actions = new HavenContainer { Name = "Terminal.Actions", Layout = HavenLayout.Wrap };
        actions.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        actions.SetValue(HavenProperties.Gap, HavenLength.Px(8));

        var shellChip = new HavenContainer { Name = "Terminal.ShellChip", Layout = HavenLayout.Horizontal };
        shellChip.SetValue(HavenProperties.Padding, HavenThickness.Parse("7px 12px"));
        shellChip.SetValue(HavenProperties.Background, "SurfaceRaised");
        shellChip.SetValue(HavenProperties.BorderColor, "Border");
        shellChip.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        shellChip.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(10)));
        shellChip.Add(new HavenText { Content = "PowerShell" });
        actions.Add(shellChip);

        var fresh = Action("Terminal.NewSession", "New session", "plus");
        fresh.Invoked += (_, _) => NewSession();
        actions.Add(fresh);

        var clear = Action("Terminal.Clear", "Clear", "delete");
        clear.Invoked += (_, _) => ClearTranscript();
        actions.Add(clear);

        var stop = Action("Terminal.Stop", "Stop", "stop");
        stop.Invoked += (_, _) => CancelRunningCommand();
        actions.Add(stop);

        var folder = Action("Terminal.WorkingFolder", "Working folder", "folder");
        folder.Invoked += async (_, _) => await PickFolderAsync();
        actions.Add(folder);
        root.Add(actions);

        var approvalHost = Host("Terminal.ApprovalHost", _approval, "Terminal command approval");
        approvalHost.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        root.Add(approvalHost);

        var transcriptFrame = new HavenContainer { Name = "Terminal.TranscriptFrame", Layout = HavenLayout.Vertical };
        transcriptFrame.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        transcriptFrame.SetValue(HavenProperties.Height, HavenLength.Fr(1));
        transcriptFrame.SetValue(HavenProperties.MinHeight, HavenLength.Px(430));
        transcriptFrame.SetValue(HavenProperties.Padding, HavenThickness.Parse("14px"));
        transcriptFrame.SetValue(HavenProperties.Background, "SurfaceRaised");
        transcriptFrame.SetValue(HavenProperties.BorderColor, "Border");
        transcriptFrame.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        transcriptFrame.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12)));
        var transcriptHost = Host("Terminal.TranscriptHost", _scroll, "Selectable Terminal output transcript");
        transcriptHost.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        transcriptHost.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        transcriptFrame.Add(transcriptHost);
        root.Add(transcriptFrame);

        var statusBar = new HavenContainer { Name = "Terminal.StatusBar", Layout = HavenLayout.Wrap };
        statusBar.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        statusBar.SetValue(HavenProperties.Gap, HavenLength.Px(14));
        statusBar.SetValue(HavenProperties.Padding, HavenThickness.Parse("7px 10px"));
        statusBar.SetValue(HavenProperties.BorderColor, "Border");
        statusBar.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        statusBar.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(9)));

        var shellStatus = new HavenText { Name = "Terminal.ShellStatus", Content = "PowerShell" };
        shellStatus.SetValue(HavenProperties.FontSize, 11d);
        statusBar.Add(shellStatus);

        _huiStatusText = new HavenText { Name = "Terminal.Status", Content = "Ready" };
        _huiStatusText.SetValue(HavenProperties.Foreground, "TextSecondary");
        _huiStatusText.SetValue(HavenProperties.FontSize, 11d);
        statusBar.Add(_huiStatusText);

        _huiCwdText = new HavenText { Name = "Terminal.Cwd", Content = SensitiveTextRedactor.Redact(WorkingDirectory) };
        _huiCwdText.SetValue(HavenProperties.Foreground, "AccentSecondary");
        _huiCwdText.SetValue(HavenProperties.FontSize, 11d);
        statusBar.Add(_huiCwdText);

        var shortcuts = new HavenText { Name = "Terminal.Shortcuts", Content = "Up/Down history  |  Ctrl+C interrupt" };
        shortcuts.SetValue(HavenProperties.Foreground, "TextSecondary");
        shortcuts.SetValue(HavenProperties.FontSize, 10d);
        statusBar.Add(shortcuts);
        root.Add(statusBar);

        var scene = new HavenSceneControl(new HavenAvaloniaImageResolver(), _terminalNativeResolver) { Root = root };
        _huiScene = scene;
        scene.InputSubmitted += input =>
        {
            if (ReferenceEquals(input, _huiCommandInput)) _ = SubmitFromHuiAsync();
        };
        scene.AddHandler(InputElement.KeyDownEvent, OnHuiSceneKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
        SyncHuiInputState();
        return scene;
    }

    private async Task SubmitFromHuiAsync()
    {
        if (_huiCommandInput is null || _running || _pending is not null) return;
        var command = _huiCommandInput.Text;
        if (string.IsNullOrWhiteSpace(command)) return;
        _input.Text = command;
        _huiCommandInput.Text = string.Empty;
        SyncHuiInputState();
        await SubmitAsync();
        SyncHuiInputState();
        if (!_running && _pending is null) FocusCommandLine();
    }

    private void OnHuiSceneKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control) && _running)
        {
            CancelRunningCommand();
            e.Handled = true;
            return;
        }
        if (_huiCommandInput is null || !_huiCommandInput.State.HasFlag(HavenElementState.Focused) || _running) return;
        if (e.Key == Key.Up)
        {
            History(-1);
            SyncHuiCommandTextFromNative();
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            History(1);
            SyncHuiCommandTextFromNative();
            e.Handled = true;
        }
    }

    private void SyncHuiCommandTextFromNative()
    {
        if (_huiCommandInput is null) return;
        _huiCommandInput.Text = _input.Text ?? string.Empty;
        _huiCommandInput.PlaceCaretAtEnd();
    }

    private void SyncHuiInputState()
    {
        if (_huiCommandInput is null) return;
        var enabled = !_running && _pending is null;
        _huiCommandInput.SetValue(HavenProperties.Enabled, enabled);
        _huiCommandInput.Accessibility.Enabled = enabled;
    }

    private void SyncHuiChrome(string? status = null)
    {
        if (_huiCwdText is not null) _huiCwdText.Content = SensitiveTextRedactor.Redact(WorkingDirectory);
        if (_huiStatusText is not null && status is not null) _huiStatusText.Content = status;
    }
}
