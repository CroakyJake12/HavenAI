
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

public sealed partial class TerminalPage : UserControl, IDisposable
{
    private readonly ITerminalSessionFactory _sessionFactory;
    private ITerminalSession _session;
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
    private string? _pending;
    private int _historyIndex;
    private bool _running;
    private bool _attached;
    private bool _disposed;
    private CancellationTokenSource? _commandCancellation;

    public TerminalPage(ITerminalSessionFactory sessionFactory, UserPreferencesService prefs, TerminalCommandActivityHub hub, string? initialDirectory = null, Guid? restoredFromSessionId = null)
    {
        _sessionFactory = sessionFactory; _prefs = prefs; _hub = hub;
        _home = StartDirectory(initialDirectory);
        _session = CreateSession(_home);
        InitializeComponent();
        Content = BuildHuiWorkspace();
        _input.KeyDown += InputKeyDown; _stop.Click += (_, _) => CancelRunningCommand();
        AttachedToVisualTree += Attached; DetachedFromVisualTree += Detached;
        UpdatePrompt();
        SystemLine($"Haven Terminal · session {_session.Metadata.SessionId:N} · persistent {_session.Metadata.ShellRuntime} · PID {_session.ProcessId?.ToString() ?? "starting"}");
        if (restoredFromSessionId is { } previousSessionId)
            SystemLine($"Restored Terminal tab from Haven session {previousSessionId:N}; the prior OS shell did not survive. This is a new shell session/process.");
        SystemLine("cwd, environment variables, functions and shell variables persist inside this tab's live shell process.");
        SystemLine("Shell processes are runtime-only: reopening Haven creates a fresh shell even when the Terminal tab is restored.");
        SystemLine("Visible history is memory-only and secret-redacted. Every submitted command still uses Haven's command permission policy.");
    }

    public bool IsRunning => _running;
    public string WorkingDirectory => _session.Metadata.CurrentWorkingDirectory ?? _home;
    public TerminalSessionMetadata SessionMetadata => _session.Metadata;
    public void FocusCommandLine() { if (_huiScene is not null && _huiCommandInput is not null) _huiScene.FocusElement(_huiCommandInput); else _input.Focus(); }
    public void CancelRunningCommand()
    {
        if (!_running) return;
        Status("Interrupting…");
        _commandCancellation?.Cancel();
    }
    public void ClearTranscript() { _lines.Children.Clear(); SystemLine("Transcript cleared."); }
    public void NewSession() => _ = NewSessionAsync();

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
            new TextBlock { Text = "Enter run · ↑/↓ history · Ctrl+C interrupt · cd/pwd run in the live shell · clear clears this transcript", FontSize = 11, Foreground = B("#7D8590") } } };
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
        _pending = null; _approval.IsVisible = false; _input.IsEnabled = true; Status("Denied"); SyncHuiInputState(); FocusCommandLine();
    }

    private async Task RunAsync(string command)
    {
        if (_running) return;
        CommandLine(SensitiveTextRedactor.Redact(command, 8_000));
        Running(true); Status("Running…"); _commandCancellation = new CancellationTokenSource();
        try
        {
            var result = await _session.ExecuteAsync(Guid.NewGuid(), command, _commandCancellation.Token);
            UpdatePrompt();
            if (result.Cancelled)
            {
                SystemLine("Interrupted · this shell process ended and a fresh shell was started. Process-local shell state was reset.");
                Status("Interrupted · shell restarted");
            }
            else
            {
                var exit = result.ExitCode?.ToString() ?? "unknown";
                SystemLine($"Exit {exit} · {result.Duration.TotalSeconds:0.0}s");
                Status(result.ShellAlive ? $"{(result.ExitCode == 0 ? "Succeeded" : "Failed")} · exit {exit}" : "Session ended");
            }
        }
        catch (OperationCanceledException) { Status("Interrupted"); }
        catch (Exception ex) { ErrorLine(SensitiveTextRedactor.Redact(ex.Message)); Status("Session unavailable"); }
        finally { _commandCancellation?.Dispose(); _commandCancellation = null; Running(false); UpdatePrompt(); _input.Focus(); }
    }

    private bool SessionCommand(string command)
    {
        var value = command.Trim();
        if (!value.Equals("clear", StringComparison.OrdinalIgnoreCase) && !value.Equals("cls", StringComparison.OrdinalIgnoreCase)) return false;
        Remember("clear");
        ClearTranscript();
        return true;
    }

    private async Task PickFolderAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider; if (storage is null) return;
        var selected = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose Terminal working folder", AllowMultiple = false });
        var path = selected.FirstOrDefault()?.TryGetLocalPath(); if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        try
        {
            await _session.SetWorkingDirectoryAsync(path, CancellationToken.None);
            UpdatePrompt(); SystemLine("Working folder: " + SensitiveTextRedactor.Redact(WorkingDirectory)); Status("Ready");
        }
        catch (Exception ex) { ErrorLine(SensitiveTextRedactor.Redact(ex.Message)); Status("Working folder unchanged"); }
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

    private ITerminalSession CreateSession(string directory)
    {
        var session = _sessionFactory.Create(directory, "Terminal");
        session.OutputReceived += SessionOutputReceived;
        session.MetadataChanged += SessionMetadataChanged;
        return session;
    }

    private Task NewSessionAsync()
    {
        _pending = null;
        _approval.IsVisible = false;
        var old = _session;
        old.OutputReceived -= SessionOutputReceived;
        old.MetadataChanged -= SessionMetadataChanged;
        old.Dispose();
        _session = CreateSession(_home);
        _history.Clear();
        _historyIndex = 0;
        _lines.Children.Clear();
        Running(false);
        UpdatePrompt();
        SystemLine($"New persistent shell session · PID {_session.ProcessId?.ToString() ?? "starting"} · cwd {SensitiveTextRedactor.Redact(WorkingDirectory)}");
        SystemLine("Previous process-local cwd/environment/functions/variables were discarded with the old shell.");
        Status("Ready");
        return Task.CompletedTask;
    }

    private void SessionOutputReceived(object? sender, TerminalSessionOutput output)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            if (output.Stream == TerminalOutputStream.StandardError) ErrorLine(output.Text);
            else if (output.Stream == TerminalOutputStream.StandardOutput) OutputLine(output.Text);
            else SystemLine(output.Text);
        });
    }

    private void SessionMetadataChanged(object? sender, TerminalSessionMetadata metadata)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            UpdatePrompt();
            if (metadata.State == TerminalSessionLifecycleState.Ended) Status("Session ended");
            else if (metadata.State == TerminalSessionLifecycleState.Faulted) Status("Session faulted");
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _commandCancellation?.Dispose();
        _commandCancellation = null;
        if (_attached) { _hub.ActivityPublished -= AgentActivity; _attached = false; }
        _session.OutputReceived -= SessionOutputReceived;
        _session.MetadataChanged -= SessionMetadataChanged;
        _session.Dispose();
    }

    private void Remember(string value) { _history.Add(value); _historyIndex = _history.Count; }
    private void History(int delta) { if (_history.Count == 0) return; _historyIndex = Math.Clamp(_historyIndex + delta, 0, _history.Count); _input.Text = _historyIndex == _history.Count ? "" : _history[_historyIndex]; _input.CaretIndex = _input.Text?.Length ?? 0; }
    private void Running(bool value) { _running = value; _stop.IsEnabled = value; _input.IsEnabled = !value && _pending is null; SyncHuiInputState(); }
    private void UpdatePrompt() { _prompt.Text = $"PS {SensitiveTextRedactor.Redact(WorkingDirectory)}> "; SyncHuiChrome(); }
    private void Status(string value) { _status.Text = $"{value} · command permission: {_prefs.CommandPermission}"; SyncHuiChrome(value); }
    private void CommandLine(string value) => Line($"PS {SensitiveTextRedactor.Redact(WorkingDirectory)}> {value}", "#7EE787");
    private void OutputLine(string value) => Multiline(value, "#E6EDF3");
    private void ErrorLine(string value) => Multiline(value, "#FF7B72");
    private void SystemLine(string value) => Multiline(value, "#8B949E");
    private void Multiline(string value, string color) { foreach (var line in value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')) Line(line, color); }
    private void Line(string value, string color) { _lines.Children.Add(new SelectableTextBlock { Text = value, FontFamily = new("Cascadia Mono, Consolas"), FontSize = 13, Foreground = B(color), TextWrapping = TextWrapping.NoWrap }); Dispatcher.UIThread.Post(() => _scroll.ScrollToEnd()); }
    private static string StartDirectory(string? path) { if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) return Path.GetFullPath(path); var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); return Directory.Exists(home) ? home : Environment.CurrentDirectory; }
    private static TextBlock Mono(string text) => new() { Text = text, FontFamily = new("Cascadia Mono, Consolas"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private static Button Btn(string text) => new() { Content = text, MinHeight = 34, Padding = new Thickness(12, 5) };
    private static SolidColorBrush B(string color) => new(Color.Parse(color));
    private static T Row<T>(T c, int row) where T : Control { Grid.SetRow(c,row); return c; }
    private static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c,col); return c; }
}
