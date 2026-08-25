using Avalonia.Automation;
using Avalonia.Controls;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenContainer = Haven.UI.Components.Container;
using HavenNativeHost = Haven.UI.Components.NativeHost;
using HavenPage = Haven.UI.Components.Page;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Terminal;

public sealed partial class TerminalPage
{
    private readonly TerminalNativeControlResolver _terminalNativeResolver = new();

    private Control BuildLegacyHuiWorkspace()
    {
        _scroll.Content = _lines;
        AutomationProperties.SetAutomationId(_scroll, "Terminal.Transcript");
        AutomationProperties.SetAutomationId(_input, "Terminal.CommandInput.Native");
        AutomationProperties.SetName(_input, "Terminal command input");

        var run = Btn("Run once");
        run.Click += async (_, _) => await ApproveAsync();
        var deny = Btn("Deny");
        deny.Click += (_, _) => Deny();
        _approvalText.Foreground = Avalonia.Media.Brushes.White;
        _approval.Child = new Grid
        {
            ColumnDefinitions = new("*,Auto,Auto"),
            ColumnSpacing = 8,
            Children = { _approvalText, Col(run, 1), Col(deny, 2) }
        };

        var root = new HavenPage { Name = "Terminal.Root", Layout = HavenLayout.Vertical };
        root.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        root.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        root.SetValue(HavenProperties.Gap, HavenLength.Px(12));
        root.SetValue(HavenProperties.Padding, HavenThickness.Parse("18px 22px 16px 22px"));
        root.Accessibility.AccessibleName = "Haven Terminal workspace";

        var header = new HavenContainer { Name = "Terminal.Header", Layout = HavenLayout.Wrap };
        header.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        header.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        header.Add(new HavenText { Name = "Terminal.Title", Content = "Terminal", Level = TextLevel.H1 });
        var statusHost = Host("Terminal.StatusHost", _status, "Terminal session status");
        statusHost.SetValue(HavenProperties.MinWidth, HavenLength.Px(220));
        header.Add(statusHost);

        var folder = Action("Terminal.WorkingFolder", "Working folder", "folder");
        folder.Invoked += async (_, _) => await PickFolderAsync();
        var fresh = Action("Terminal.NewSession", "New session", "plus");
        fresh.Invoked += (_, _) => NewSession();
        var clear = Action("Terminal.Clear", "Clear", "delete");
        clear.Invoked += (_, _) => ClearTranscript();
        var stop = Action("Terminal.Stop", "Stop", "stop");
        stop.Invoked += (_, _) => CancelRunningCommand();
        header.Add(folder);
        header.Add(fresh);
        header.Add(clear);
        header.Add(stop);
        root.Add(header);

        var transcriptFrame = new HavenContainer { Name = "Terminal.TranscriptFrame", Layout = HavenLayout.Vertical };
        transcriptFrame.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        transcriptFrame.SetValue(HavenProperties.Height, HavenLength.Percent(68));
        transcriptFrame.SetValue(HavenProperties.MinHeight, HavenLength.Px(420));
        transcriptFrame.SetValue(HavenProperties.Padding, HavenThickness.Parse("12px"));
        transcriptFrame.SetValue(HavenProperties.Background, "SurfaceRaised");
        transcriptFrame.SetValue(HavenProperties.BorderColor, "Border");
        transcriptFrame.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        transcriptFrame.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(14)));
        var transcriptHost = Host("Terminal.TranscriptHost", _scroll, "Selectable Terminal output transcript");
        transcriptHost.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        transcriptHost.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        transcriptFrame.Add(transcriptHost);
        root.Add(transcriptFrame);

        var approvalHost = Host("Terminal.ApprovalHost", _approval, "Terminal command approval");
        approvalHost.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        root.Add(approvalHost);

        var composer = new HavenContainer { Name = "Terminal.Composer", Layout = HavenLayout.Horizontal };
        composer.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        composer.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        composer.SetValue(HavenProperties.Padding, HavenThickness.Parse("8px 10px"));
        composer.SetValue(HavenProperties.Background, "SurfaceRaised");
        composer.SetValue(HavenProperties.BorderColor, "Border");
        composer.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        composer.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12)));
        var promptHost = Host("Terminal.PromptHost", _prompt, "PowerShell prompt");
        promptHost.SetValue(HavenProperties.MinWidth, HavenLength.Px(180));
        composer.Add(promptHost);
        var inputHost = Host("Terminal.CommandInputHost", _input, "Terminal command input");
        inputHost.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        inputHost.SetValue(HavenProperties.MinHeight, HavenLength.Px(40));
        composer.Add(inputHost);
        root.Add(composer);

        var hint = new HavenText
        {
            Name = "Terminal.Hint",
            Content = "Enter run · Up/Down history · Ctrl+C interrupt · cd/Get-Location use the live shell · Clear only clears this transcript"
        };
        hint.SetValue(HavenProperties.Foreground, "TextSecondary");
        hint.SetValue(HavenProperties.FontSize, 11d);
        root.Add(hint);

        return new HavenSceneControl(new HavenAvaloniaImageResolver(), _terminalNativeResolver) { Root = root };
    }

    private HavenNativeHost Host(string name, Control control, string accessibleName)
    {
        var host = new HavenNativeHost { Name = name };
        host.Accessibility.AccessibleName = accessibleName;
        _terminalNativeResolver.Register(host, control);
        return host;
    }

    private static HavenButton Action(string name, string content, string icon) => new()
    {
        Name = name,
        Content = content,
        IconKey = icon,
        Variant = ButtonVariant.Ghost
    };
}

internal sealed class TerminalNativeControlResolver : IHavenAvaloniaNativeControlResolver
{
    private readonly Dictionary<HavenElement, Control> _controls = [];

    public void Register(HavenElement element, Control control)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(control);
        if (!_controls.TryAdd(element, control))
            throw new InvalidOperationException("Terminal native host is already registered.");
    }

    public bool TryCreate(HavenElement element, out Control? control)
    {
        if (_controls.TryGetValue(element, out var found))
        {
            control = found;
            return true;
        }
        control = null;
        return false;
    }
}
