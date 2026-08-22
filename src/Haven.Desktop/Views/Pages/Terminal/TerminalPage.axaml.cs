using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Haven.Application;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Terminal;

public sealed partial class TerminalPage : UserControl
{
    private readonly IWorkspaceToolService _tools;
    private readonly UserPreferencesService _prefs;
    private readonly TerminalCommandActivityHub _hub;
    private readonly StackPanel _lines = new() { Spacing = 2 };
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBox _input = new() { Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontFamily = new("Cascadia Mono, Consolas"), FontSize = 14, MinHeight = 36 };
    private readonly TextBlock _prompt = Mono("");
    private readonly TextBlock _status = Mono("Ready");
    private readonly TextBlock _approvalText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Border _approval = new() { IsVisible = false, Padding = new Thickness(12), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), BorderBrush = B("#8B6F2A"), Background = B("#30260F") };
    private readonly Button _stop = Btn("Stop");
    private readonly List<string> _history = [];
    private readonly string _home;
    private string _cwd;
    private string? _pending;
    private int _historyIndex;
    private bool _running;
    private bool _attached;
    private CancellationTokenSource? _cancel;

    public TerminalPage(IWorkspaceToolService tools, UserPreferencesService prefs, TerminalCommandActivityHub hub, string? initialDirectory = null)
    {
        _tools = tools; _prefs = prefs; _hub = hub;
        _home = StartDirectory(initialDirectory); _cwd = _home;
        InitializeComponent();
        (this.FindControl<Grid>("CodeBehindHost") ?? throw new InvalidOperationException("Terminal host missing.")).Children.Add(Build());
        _input.KeyDown += InputKeyDown; _stop.Click += (_, _) => CancelRunningCommand();
        AttachedToVisualTree += Attached; DetachedFromVisualTree += Detached;
        UpdatePrompt();
        SystemLine("Haven Terminal · genuine PowerShell execution");
        SystemLine("cwd persists in this tab; each command is a fresh process, so process environment changes do not persist.");
        SystemLine("Visible history is memory-only and secret-redacted. Full Access commands retain your Windows-account permissions.");
    }

    public bool IsRunning => _running;
    public string WorkingDirectory => _cwd;
    public void FocusCommandLine() => _input.Focus();
    public void CancelRunningCommand() { if (_running) { _cancel?.Cancel(); Status("Cancelling…"); } }
    public void ClearTranscript() { _lines.Children.Clear(); SystemLine("Transcript cleared."); }
    public void NewSession() { CancelRunningCommand(); _pending = null; _approval.IsVisible = false; _cwd = _home; _history.Clear(); _historyIndex = 0; _lines.Children.Clear(); SystemLine("New session · cwd and history reset."); UpdatePrompt(); }

    private Control Build()
    {
        var folder = Btn("Working folder"); folder.Click += async (_, _) => await PickFolderAsync();
        var fresh = Btn("New session"); fresh.Click += (_, _) => NewSession();
        var clear = Btn("Clear"); clear.Click += (_, _) => ClearTranscript(); _stop.IsEnabled = false;
        var header = new Grid { ColumnDefinitions = new("*,Auto,Auto,Auto,Auto"), ColumnSpacing = 8, Margin = new Thickness(18, 12),
            Children = { new StackPanel { Children = { new TextBlock { Text = "Terminal", FontSize = 22, FontWeight = FontWeight.Bold }, _status } }, Col(folder,1), Col(fresh,2), Col(clear,3), Col(_stop,4) } };
        _scroll.Content = _lines;
        var body = new Border { Background = B("#0B0E14"), BorderBrush = B("#343A46"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(14), Margin = new Thickness(18,0,18,10), Child = _scroll };
        var run = Btn("Run once"); run.Click += async (_, _) => await ApproveAsync();
        var deny = Btn("Deny"); deny.Click += (_, _) => Deny();
        _approvalText.Foreground = Brushes.White;
        _approval.Child = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 8, Children = { _approvalText, Col(run,1), Col(deny,2) } };
        _prompt.Foreground = B("#7EE787"); _input.Foreground = Brushes.White; _input.CaretBrush = Brushes.White;
        var composer = new Border { Background = B("#10151E"), BorderBrush = B("#343A46"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(10,4),
            Child = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 8, Children = { _prompt, Col(_input,1) } } };
        var footer = new StackPanel { Spacing = 8, Margin = new Thickness(18,0,18,16), Children = { _approval, composer,
            new TextBlock { Text = "Enter run · ↑/↓ history · Ctrl+C cancel · cd/pwd/clear are session commands", FontSize = 11, Foreground = B("#7D8590") } } };
        return new Grid { RowDefinitions = new("Auto,*,Auto"), Children = { header, Row(body,1), Row(footer,2) } };
    }

    private async void InputKeyDown(object? _, KeyEventArgs e)
    {
        if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { e.Handled = true; CancelRunningCommand(); return; }
        if (e.Key == Key.Up) { e.Handled = true; History(-1); return; }
        if (e.Key == Key.Down) { e.Handled = true; History(1); return; }
        if (e.Key == Key.Enter && !_running) { e.Handled = true; await SubmitAsync(); }
    }

    private async Task SubmitAsync()
    {
        var command = (_input.Text ?? "").Trim(); if (command.Length == 0) return; _input.Text = "";
        if (SessionCommand(command)) return;
        var safe = SensitiveTextRedactor.Redact(command, 8_000); _history.Add(safe); _historyIndex = _history.Count;
        var permission = TerminalCommandPolicy.Evaluate(_prefs.CommandPermission);
        if (permission.Decision == TerminalPermissionDecision.Denied) { CommandLine(safe); ErrorLine(permission.Reason); Status("Denied"); return; }
        if (permission.Decision == TerminalPermissionDecision.RequiresApproval)
        { _pending = command; _approvalText.Text = permission.Reason + "\n" + safe; _approval.IsVisible = true; _input.IsEnabled = false; Status("Permission required"); return; }
        await RunAsync(command);
    }

    private async Task ApproveAsync()
    {
        if (_pending is null) return; var command = _pending; _pending = null; _approval.IsVisible = false; _input.IsEnabled = true;
        var permission = TerminalCommandPolicy.Evaluate(_prefs.CommandPermission, true);
        if (permission.Decision != TerminalPermissionDecision.Allowed) { CommandLine(SensitiveTextRedactor.Redact(command)); ErrorLine(permission.Reason); return; }
        await RunAsync(command);
    }

    private void Deny()
    {
        if (_pending is not null) { CommandLine(SensitiveTextRedactor.Redact(_pending)); ErrorLine("Command denied by user."); }
        _pending = null; _approval.IsVisible = false; _input.IsEnabled = true; Status("Denied"); _input.Focus();
    }

    private async Task RunAsync(string command)
    {
        if (_running) return;
        CommandLine(SensitiveTextRedactor.Redact(command, 8_000));
        Running(true); Status("Running…"); _cancel = new CancellationTokenSource();
        try
        {
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
            var shell = OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh";
            var result = await _tools.RunProcessAsync(new ProcessRequest(shell, $"-NoProfile -NonInteractive -EncodedCommand {encoded}", _cwd, TimeSpan.FromMinutes(15)), _cancel.Token);
            if (!string.IsNullOrWhiteSpace(result.StandardOutput)) OutputLine(SensitiveTextRedactor.Redact(result.StandardOutput, 120_000));
            if (!string.IsNullOrWhiteSpace(result.StandardError)) ErrorLine(SensitiveTextRedactor.Redact(result.StandardError, 120_000));
            if (result.TimedOut) { ErrorLine("Timed out · process tree stopped."); Status("Timed out"); }
            else { SystemLine($"Exit {result.ExitCode} · {result.Duration.TotalSeconds:0.0}s"); Status($"{(result.ExitCode == 0 ? "Succeeded" : "Failed")} · exit {result.ExitCode}"); }
        }
        catch (OperationCanceledException) { SystemLine("Cancelled · process tree stopped."); Status("Cancelled"); }
        catch (Exception ex) { ErrorLine(SensitiveTextRedactor.Redact(ex.Message)); Status("Failed to start"); }
        finally { _cancel?.Dispose(); _cancel = null; Running(false); _input.Focus(); }
    }

    private bool SessionCommand(string command)
    {
        var value = command.Trim();
        if (value.Equals("clear", StringComparison.OrdinalIgnoreCase) || value.Equals("cls", StringComparison.OrdinalIgnoreCase)) { Remember("clear"); ClearTranscript(); return true; }
        if (value.Equals("pwd", StringComparison.OrdinalIgnoreCase) || value.Equals("Get-Location", StringComparison.OrdinalIgnoreCase)) { Remember(value); CommandLine(value); OutputLine(SensitiveTextRedactor.Redact(_cwd)); return true; }
        if (!value.Equals("cd", StringComparison.OrdinalIgnoreCase) && !value.StartsWith("cd ", StringComparison.OrdinalIgnoreCase) && !value.StartsWith("Set-Location ", StringComparison.OrdinalIgnoreCase)) return false;
        Remember(SensitiveTextRedactor.Redact(value)); CommandLine(SensitiveTextRedactor.Redact(value));
        var target = value.Equals("cd", StringComparison.OrdinalIgnoreCase) ? _home : value.StartsWith("cd ", StringComparison.OrdinalIgnoreCase) ? value[3..].Trim() : value[13..].Trim();
        try
        {
            target = Environment.ExpandEnvironmentVariables(target.Trim().Trim('"'));
            var resolved = Path.IsPathRooted(target) ? Path.GetFullPath(target) : Path.GetFullPath(Path.Combine(_cwd, target));
            if (!Directory.Exists(resolved)) ErrorLine("Directory not found: " + SensitiveTextRedactor.Redact(resolved)); else { _cwd = resolved; UpdatePrompt(); Status("Ready"); }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException) { ErrorLine(SensitiveTextRedactor.Redact(ex.Message)); }
        return true;
    }

    private async Task PickFolderAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider; if (storage is null) return;
        var selected = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose Terminal working folder", AllowMultiple = false });
        var path = selected.FirstOrDefault()?.TryGetLocalPath(); if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        _cwd = Path.GetFullPath(path); UpdatePrompt(); SystemLine("Working folder: " + SensitiveTextRedactor.Redact(_cwd)); Status("Ready");
    }

    private void Attached(object? _, Avalonia.VisualTreeAttachmentEventArgs e) { if (_attached) return; _attached = true; _hub.ActivityPublished += AgentActivity; }
    private void Detached(object? _, Avalonia.VisualTreeAttachmentEventArgs e) { if (!_attached) return; _attached = false; _hub.ActivityPublished -= AgentActivity; }
    private void AgentActivity(object? _, TerminalCommandActivity activity)
    {
        if (activity.Origin != TerminalCommandOrigin.Agent) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (activity.State == TerminalExecutionState.Requested) Line($"[agent] PS {activity.WorkingDirectory}> {activity.Command}", "#D2A8FF");
            else if (activity.State == TerminalExecutionState.Running) Status("Agent command running…");
            else if (activity.State is TerminalExecutionState.Succeeded or TerminalExecutionState.Failed)
            {
                if (!string.IsNullOrWhiteSpace(activity.Result?.StandardOutput)) OutputLine(activity.Result.StandardOutput);
                if (!string.IsNullOrWhiteSpace(activity.Result?.StandardError)) ErrorLine(activity.Result.StandardError);
                if (activity.Result is { } r) SystemLine($"[agent] Exit {r.ExitCode} · {r.Duration.TotalSeconds:0.0}s");
                Status(activity.State == TerminalExecutionState.Succeeded ? "Agent command succeeded" : "Agent command failed");
            }
            else if (activity.State == TerminalExecutionState.Cancelled) { SystemLine("[agent] Command cancelled."); Status("Agent command cancelled"); }
            else if (activity.State == TerminalExecutionState.Denied) { ErrorLine(activity.Error ?? "[agent] Command denied."); Status("Agent command denied"); }
        });
    }

    private void Remember(string value) { _history.Add(value); _historyIndex = _history.Count; }
    private void History(int delta) { if (_history.Count == 0) return; _historyIndex = Math.Clamp(_historyIndex + delta, 0, _history.Count); _input.Text = _historyIndex == _history.Count ? "" : _history[_historyIndex]; _input.CaretIndex = _input.Text?.Length ?? 0; }
    private void Running(bool value) { _running = value; _stop.IsEnabled = value; _input.IsEnabled = !value && _pending is null; }
    private void UpdatePrompt() => _prompt.Text = $"PS {SensitiveTextRedactor.Redact(_cwd)}> ";
    private void Status(string value) => _status.Text = $"{value} · command permission: {_prefs.CommandPermission}";
    private void CommandLine(string value) => Line($"PS {SensitiveTextRedactor.Redact(_cwd)}> {value}", "#7EE787");
    private void OutputLine(string value) => Multiline(value, "#E6EDF3");
    private void ErrorLine(string value) => Multiline(value, "#FF7B72");
    private void SystemLine(string value) => Multiline(value, "#8B949E");
    private void Multiline(string value, string color) { foreach (var line in value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')) Line(line, color); }
    private void Line(string value, string color) { _lines.Children.Add(new TextBlock { Text = value, FontFamily = new("Cascadia Mono, Consolas"), FontSize = 13, Foreground = B(color), TextWrapping = TextWrapping.NoWrap }); Dispatcher.UIThread.Post(() => _scroll.ScrollToEnd()); }
    private static string StartDirectory(string? path) { if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) return Path.GetFullPath(path); var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); return Directory.Exists(home) ? home : Environment.CurrentDirectory; }
    private static TextBlock Mono(string text) => new() { Text = text, FontFamily = new("Cascadia Mono, Consolas"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private static Button Btn(string text) => new() { Content = text, MinHeight = 34, Padding = new Thickness(12, 5) };
    private static SolidColorBrush B(string color) => new(Color.Parse(color));
    private static T Row<T>(T c, int row) where T : Control { Grid.SetRow(c,row); return c; }
    private static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c,col); return c; }
}
