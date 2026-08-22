using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Components;

namespace Haven.Desktop.Views.Pages.ActionGraph;

/// <summary>Graph and chronological projections over the same persisted execution trace.</summary>
public sealed partial class ActionGraphPage : UserControl, IDisposable
{
    private const double NodeWidth = 228;
    private const double NodeHeight = 112;
    private readonly ExecutionTraceService _traces;
    private readonly IActionFeedbackRepository _feedback;
    private readonly IRemediationRepository _remediations;
    private readonly RemediationCoordinator _remediationCoordinator;
    private readonly ExecutionEventHub? _hub;
    private readonly Action<string> _fixWithAi;
    private readonly Action _openSettings;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _liveRefreshTimer;
    private IReadOnlyList<ExecutionSummary> _summaries = [];
    private IReadOnlyList<ExecutionEvent> _events = [];
    private ExecutionSummary? _selectedExecution;
    private ExecutionEvent? _selectedAction;
    private ActionFeedback? _selectedFeedback;
    private bool _graphView = true;
    private double _zoom = 1;
    private bool _disposed;
    private int _liveRefreshQueued;

    public ActionGraphPage(
        ExecutionTraceService traces,
        IActionFeedbackRepository feedback,
        IRemediationRepository remediations,
        RemediationCoordinator remediationCoordinator,
        ExecutionEventHub? hub,
        Action<string> fixWithAi,
        Action openSettings)
    {
        _traces = traces;
        _feedback = feedback;
        _remediations = remediations;
        _remediationCoordinator = remediationCoordinator;
        _hub = hub;
        _fixWithAi = fixWithAi;
        _openSettings = openSettings;
        _liveRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _liveRefreshTimer.Tick += RefreshLiveTrace;
        InitializeComponent();
        GraphViewButton.Click += (_, _) => SetView(true);
        ListViewButton.Click += (_, _) => SetView(false);
        HistorySearch.TextChanged += (_, _) => RenderHistory();
        ZoomInButton.Click += (_, _) => SetZoom(_zoom + .15);
        ZoomOutButton.Click += (_, _) => SetZoom(_zoom - .15);
        FitButton.Click += (_, _) => FitGraph();
        ThumbUpButton.Click += async (_, _) => await SaveRatingAsync(ActionFeedbackRating.Positive);
        ThumbDownButton.Click += async (_, _) => await SaveRatingAsync(ActionFeedbackRating.Negative);
        SaveCommentButton.Click += async (_, _) => await SaveCommentAsync();
        DeleteFeedbackButton.Click += async (_, _) => await DeleteFeedbackAsync();
        SizeChanged += (_, _) => ApplyResponsiveLayout(Bounds.Width);
        if (_hub is not null) _hub.Published += OnEventPublished;
        _ = LoadHistoryAsync();
    }

    private async Task LoadHistoryAsync()
    {
        _summaries = await _traces.SearchAsync(HistorySearch.Text, 250, _lifetime.Token);
        await Dispatcher.UIThread.InvokeAsync(RenderHistory);
        if (_selectedExecution is null && _summaries.Count > 0)
            await SelectExecutionAsync(_summaries[0]);
    }

    public async Task OpenAsync(Guid executionId, Guid? actionId = null)
    {
        var summary = _summaries.FirstOrDefault(item => item.ExecutionId == executionId);
        if (summary is null)
        {
            _summaries = await _traces.SearchAsync(null, 500, _lifetime.Token);
            summary = _summaries.FirstOrDefault(item => item.ExecutionId == executionId);
        }
        if (summary is null) return;
        await SelectExecutionAsync(summary);
        if (actionId is { } id && _events.FirstOrDefault(item => item.ActionId == id) is { } action)
            await SelectActionAsync(action);
    }

    private void RenderHistory()
    {
        HistoryItems.Children.Clear();
        var query = HistorySearch.Text?.Trim();
        foreach (var summary in _summaries.Where(item => string.IsNullOrWhiteSpace(query) || item.PromptSummary.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            var selected = _selectedExecution?.ExecutionId == summary.ExecutionId;
            var button = new HavenButton
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = StatusGlyph(summary.Status) + " " + Truncate(summary.PromptSummary, 72), TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.Bold },
                        new TextBlock { Text = $"{summary.UpdatedAt.LocalDateTime:g} · {summary.ActionCount} actions · {FormatDuration(summary.Duration)}", Classes = { "muted" }, FontSize = 11 }
                    }
                }
            };
            button.Classes.Add(selected ? "accent" : "secondary");
            AutomationProperties.SetName(button, $"Open execution {summary.PromptSummary}");
            button.Click += async (_, _) => await SelectExecutionAsync(summary);
            HistoryItems.Children.Add(button);
        }
    }

    private async Task SelectExecutionAsync(ExecutionSummary summary)
    {
        _selectedExecution = summary;
        _events = Collapse(await _traces.GetTraceAsync(summary.ExecutionId, _lifetime.Token));
        _selectedAction = _events.FirstOrDefault();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            RenderHistory();
            RenderMetrics();
            RenderGraph();
            RenderList();
        });
        await RenderDetailsAsync();
    }

    private static IReadOnlyList<ExecutionEvent> Collapse(IReadOnlyList<ExecutionEvent> events) => events
        .GroupBy(item => item.ActionId)
        .Select(group => group.OrderByDescending(item => item.Timestamp).First())
        .OrderBy(item => item.StartedAt ?? item.Timestamp)
        .ThenBy(item => item.Timestamp)
        .ToArray();

    private void RenderMetrics()
    {
        SummaryMetrics.Children.Clear();
        var metrics = new[]
        {
            $"Steps {_events.Count}",
            $"Tools {_events.Count(item => item.ActionType is ExecutionActionType.ToolCall or ExecutionActionType.PluginCall or ExecutionActionType.McpCall)}",
            $"Apps {_events.Where(item => item.ComponentId is not null).Select(item => item.ComponentId).Distinct(StringComparer.OrdinalIgnoreCase).Count()}",
            $"Retries {_events.Count(item => item.ActionType == ExecutionActionType.Retry || item.RetryOfActionId is not null)}",
            $"Time {FormatDuration(_selectedExecution?.Duration ?? TimeSpan.Zero)}"
        };
        foreach (var metric in metrics)
            SummaryMetrics.Children.Add(new Border { Padding = new Thickness(10, 5), Margin = new Thickness(0, 0, 6, 4), CornerRadius = new CornerRadius(12), Child = new TextBlock { Text = metric, FontWeight = FontWeight.Bold, FontSize = 11 } });
    }

    private void RenderGraph()
    {
        GraphCanvas.Children.Clear();
        EmptyState.IsVisible = _events.Count == 0;
        if (_events.Count == 0) return;
        var positions = LayoutGraph(_events);
        var scale = new ScaleTransform(_zoom, _zoom);
        GraphCanvas.RenderTransform = scale;
        GraphCanvas.RenderTransformOrigin = RelativePoint.TopLeft;
        GraphCanvas.Width = Math.Max(780, positions.Values.Max(point => point.X) + NodeWidth + 50);
        GraphCanvas.Height = Math.Max(520, positions.Values.Max(point => point.Y) + NodeHeight + 50);
        foreach (var action in _events)
        {
            if (action.ParentActionId is not { } parent || !positions.TryGetValue(parent, out var from)) continue;
            var to = positions[action.ActionId];
            GraphCanvas.Children.Add(new Line
            {
                StartPoint = new Point(from.X + NodeWidth, from.Y + NodeHeight / 2),
                EndPoint = new Point(to.X, to.Y + NodeHeight / 2),
                Stroke = Brush("HavenBorderSubtleBrush", Brushes.Gray),
                StrokeThickness = action.RetryOfActionId is null ? 2 : 3
            });
        }
        foreach (var action in _events)
        {
            var selected = _selectedAction?.ActionId == action.ActionId;
            var panel = new StackPanel { Spacing = 4 };
            panel.Children.Add(new TextBlock { Text = StatusGlyph(action.Status) + " " + action.Name, FontWeight = FontWeight.ExtraBold, TextWrapping = TextWrapping.Wrap, MaxHeight = 42 });
            panel.Children.Add(new TextBlock { Text = action.ActionType.ToString(), Classes = { "muted" }, FontSize = 10 });
            panel.Children.Add(new TextBlock { Text = Truncate(action.SafeReasoningSummary ?? action.SafeDetail ?? action.Failure?.Title ?? string.Empty, 90), Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, MaxHeight = 32 });
            if (selected)
            {
                var quick = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                quick.Children.Add(QuickFeedback("👍", ActionFeedbackRating.Positive));
                quick.Children.Add(QuickFeedback("👎", ActionFeedbackRating.Negative));
                panel.Children.Add(quick);
            }
            var node = new Border
            {
                Width = NodeWidth,
                MinHeight = NodeHeight,
                Padding = new Thickness(12),
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(selected ? 2 : 1),
                BorderBrush = Brush(selected ? "HavenAccentBrush" : StatusBrushKey(action.Status), Brushes.Gray),
                Background = Brush("HavenOverlaySurfaceBrush", Brushes.Transparent),
                Child = panel,
                Tag = action
            };
            AutomationProperties.SetName(node, $"{action.Name}, {action.Status}");
            node.PointerPressed += OnNodePressed;
            var point = positions[action.ActionId];
            Avalonia.Controls.Canvas.SetLeft(node, point.X);
            Avalonia.Controls.Canvas.SetTop(node, point.Y);
            GraphCanvas.Children.Add(node);
        }
    }

    private HavenChipButton QuickFeedback(string label, ActionFeedbackRating rating)
    {
        var button = new HavenChipButton { Content = label, Padding = new Thickness(7, 2), MinWidth = 34 };
        button.Click += async (_, _) => await SaveRatingAsync(rating);
        return button;
    }

    private static Dictionary<Guid, Point> LayoutGraph(IReadOnlyList<ExecutionEvent> events)
    {
        var byId = events.ToDictionary(item => item.ActionId);
        var depths = new Dictionary<Guid, int>();
        int Depth(ExecutionEvent item, HashSet<Guid> visiting)
        {
            if (depths.TryGetValue(item.ActionId, out var known)) return known;
            if (!visiting.Add(item.ActionId) || item.ParentActionId is not { } parent || !byId.TryGetValue(parent, out var parentItem)) return depths[item.ActionId] = 0;
            var value = 1 + Depth(parentItem, visiting);
            visiting.Remove(item.ActionId);
            return depths[item.ActionId] = value;
        }
        foreach (var item in events) Depth(item, []);
        var positions = new Dictionary<Guid, Point>();
        foreach (var group in events.GroupBy(item => depths[item.ActionId]).OrderBy(group => group.Key))
        {
            var lane = 0;
            foreach (var item in group) positions[item.ActionId] = new Point(28 + group.Key * 282, 28 + lane++ * 146);
        }
        return positions;
    }

    private void RenderList()
    {
        ListItems.Children.Clear();
        var index = 0;
        foreach (var action in _events)
        {
            index++;
            var button = new HavenButton
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("42,2*,1.2*,3*,Auto,Auto"),
                    Children =
                    {
                        Cell(index.ToString(), 0, true),
                        Cell(StatusGlyph(action.Status) + " " + action.Name, 1, true),
                        Cell(action.ActionType.ToString(), 2),
                        Cell(Truncate(action.SafeReasoningSummary ?? action.SafeDetail ?? action.Failure?.Message ?? string.Empty, 150), 3),
                        Cell(action.Status.ToString(), 4),
                        Cell(FormatDuration(action.Duration ?? TimeSpan.Zero), 5)
                    }
                }
            };
            button.Click += async (_, _) => await SelectActionAsync(action);
            ListItems.Children.Add(button);
        }
    }

    private static TextBlock Cell(string text, int column, bool bold = false)
    {
        var cell = new TextBlock { Text = text, Margin = new Thickness(5), TextWrapping = TextWrapping.Wrap, FontWeight = bold ? FontWeight.Bold : FontWeight.Normal };
        Grid.SetColumn(cell, column);
        return cell;
    }

    private async void OnNodePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { Tag: ExecutionEvent action }) await SelectActionAsync(action);
        e.Handled = true;
    }

    private async Task SelectActionAsync(ExecutionEvent action)
    {
        _selectedAction = action;
        RenderGraph();
        await RenderDetailsAsync();
    }

    private async Task RenderDetailsAsync()
    {
        var action = _selectedAction;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            DetailsItems.Children.Clear();
            FeedbackPanel.IsVisible = action is not null;
            RemediationPanel.Children.Clear();
            if (action is null)
            {
                DetailsItems.Children.Add(Muted("Select a graph node or list row."));
                return;
            }
            AddDetail("Overview", $"{action.ActionType} · {action.Status}\n{action.Timestamp.LocalDateTime:g} · {FormatDuration(action.Duration ?? TimeSpan.Zero)}");
            if (!string.IsNullOrWhiteSpace(action.SafeReasoningSummary)) AddDetail("Reasoning", action.SafeReasoningSummary!);
            if (!string.IsNullOrWhiteSpace(action.SafeDetail)) AddDetail("Details", action.SafeDetail!);
            if (!string.IsNullOrWhiteSpace(action.ComponentId)) AddDetail("Tool / App", action.ComponentId!);
            AddDetail("Graph context", $"Parent: {action.ParentActionId?.ToString() ?? "none"}\nRetry of: {action.RetryOfActionId?.ToString() ?? "none"}\nRecovery of: {action.RecoveryOfActionId?.ToString() ?? "none"}");
            AddDetail("Metadata", $"Execution ID: {action.ExecutionId}\nAction ID: {action.ActionId}\nOrigin: {action.Origin}");
            if (action.Failure is { } failure)
            {
                AddDetail("Error", $"{failure.Code} — {failure.Title}\n{failure.Message}\nAttempt: {failure.Attempt}" + (failure.Recovered ? "\nRecovered automatically" : string.Empty), true);
                if (!failure.Recovered)
                {
                    var fix = new HavenPrimaryButton { Content = "Fix with AI" };
                    fix.Click += (_, _) => _fixWithAi($"Inspect and safely repair Action {action.ActionId} in Execution {action.ExecutionId}. Failure: {failure.Code} — {failure.Title}. Stay within the original permissions and task scope.");
                    DetailsItems.Children.Add(fix);
                }
            }
        });
        if (action is null) return;
        _selectedFeedback = await _feedback.GetAsync(action.ExecutionId, action.ActionId, _lifetime.Token);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            CommentInput.Text = _selectedFeedback?.Comment ?? string.Empty;
            DeleteFeedbackButton.IsVisible = _selectedFeedback is not null;
        });
        if (action.RemediationId is { } remediationId)
        {
            var remediation = await _remediations.GetAsync(remediationId, _lifetime.Token);
            if (remediation is not null) await Dispatcher.UIThread.InvokeAsync(() => RenderRemediation(remediation));
        }
    }

    private void AddDetail(string heading, string body, bool danger = false)
    {
        var card = new Border { Padding = new Thickness(10), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), BorderBrush = Brush(danger ? "HavenDangerBrush" : "HavenBorderSubtleBrush", Brushes.Gray) };
        card.Child = new StackPanel { Spacing = 4, Children = { new TextBlock { Text = heading, FontWeight = FontWeight.ExtraBold }, new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap, Classes = { "muted" } } } };
        DetailsItems.Children.Add(card);
    }

    private void RenderRemediation(RemediationRequest request)
    {
        RemediationPanel.Children.Clear();
        RemediationPanel.Children.Add(new TextBlock { Text = request.State == RemediationState.Suspended ? "Action paused" : "User Action Required", FontWeight = FontWeight.ExtraBold, FontSize = 16 });
        RemediationPanel.Children.Add(new TextBlock { Text = request.RequestingComponentName + (request.ProviderName is null ? string.Empty : " · " + request.ProviderName), FontWeight = FontWeight.Bold });
        RemediationPanel.Children.Add(Muted(request.Explanation + "\nStored securely by Haven where applicable."));
        if (request.Type == RemediationType.SecretInput)
        {
            var input = request.RequiredInputs.FirstOrDefault();
            var secret = new TextBox { PasswordChar = '•', PlaceholderText = input?.Label ?? "Required secret" };
            secret.TextChanged += async (_, _) => await _remediationCoordinator.RecordInteractionAsync(request.Id, _lifetime.Token);
            var save = new HavenPrimaryButton { Content = request.State == RemediationState.Suspended ? "Save Securely & Retry" : "Save Securely & Retry" };
            save.Click += async (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(secret.Text)) return;
                await _remediationCoordinator.SaveSecretAndResolveAsync(request.Id, input?.Key ?? "secret", secret.Text, _lifetime.Token);
                secret.Text = string.Empty;
                await RenderDetailsAsync();
            };
            RemediationPanel.Children.Add(secret);
            RemediationPanel.Children.Add(save);
        }
        else
        {
            var open = new HavenButton { Content = request.Type switch { RemediationType.OAuthReconnect => "Reconnect Account", RemediationType.PermissionRequest => "Review Permission", RemediationType.ResourceSelection => "Select Resource", _ => "Open Settings" } };
            open.Click += (_, _) => _openSettings();
            RemediationPanel.Children.Add(open);
        }
    }

    private async Task SaveRatingAsync(ActionFeedbackRating rating)
    {
        if (_selectedAction is not { } action) return;
        var now = DateTimeOffset.UtcNow;
        var feedback = new ActionFeedback(_selectedFeedback?.Id ?? Guid.NewGuid(), action.ExecutionId, action.ActionId, rating,
            _selectedFeedback?.Comment, action.ActionType.ToString(), action.ComponentId, action.SafeReasoningSummary, _selectedFeedback?.CreatedAt ?? now, now);
        await _traces.UpsertFeedbackAsync(feedback, _lifetime.Token);
        _selectedFeedback = feedback;
    }

    private async Task SaveCommentAsync()
    {
        if (_selectedAction is not { } action || string.IsNullOrWhiteSpace(CommentInput.Text)) return;
        var now = DateTimeOffset.UtcNow;
        var feedback = new ActionFeedback(_selectedFeedback?.Id ?? Guid.NewGuid(), action.ExecutionId, action.ActionId, _selectedFeedback?.Rating,
            CommentInput.Text, action.ActionType.ToString(), action.ComponentId, action.SafeReasoningSummary, _selectedFeedback?.CreatedAt ?? now, now);
        await _traces.UpsertFeedbackAsync(feedback, _lifetime.Token);
        _selectedFeedback = feedback;
        DeleteFeedbackButton.IsVisible = true;
    }

    private async Task DeleteFeedbackAsync()
    {
        if (_selectedFeedback is not { } feedback) return;
        await _traces.DeleteFeedbackAsync(feedback.Id, _lifetime.Token);
        _selectedFeedback = null;
        CommentInput.Text = string.Empty;
        DeleteFeedbackButton.IsVisible = false;
    }

    private void SetView(bool graph)
    {
        _graphView = graph;
        GraphScroll.IsVisible = graph;
        ListScroll.IsVisible = !graph;
        ZoomInButton.IsVisible = graph;
        ZoomOutButton.IsVisible = graph;
        FitButton.IsVisible = graph;
    }

    private void SetZoom(double value)
    {
        _zoom = Math.Clamp(value, .45, 2.25);
        RenderGraph();
    }

    private void FitGraph()
    {
        if (_events.Count == 0) return;
        _zoom = Math.Clamp(Math.Min(Math.Max(300, GraphScroll.Bounds.Width) / Math.Max(1, GraphCanvas.Width), Math.Max(300, GraphScroll.Bounds.Height) / Math.Max(1, GraphCanvas.Height)), .45, 1.25);
        RenderGraph();
        GraphScroll.Offset = default;
    }

    private void ApplyResponsiveLayout(double width)
    {
        if (width < 900)
        {
            WorkspaceGrid.ColumnDefinitions = new ColumnDefinitions("0,0,*,0,0");
            WorkspaceGrid.RowDefinitions = new RowDefinitions("*,Auto");
            HistorySurface.IsVisible = false;
            PlaceDetailsBelowCanvas();
        }
        else if (width < 1250)
        {
            WorkspaceGrid.ColumnDefinitions = new ColumnDefinitions("210,6,*,0,0");
            WorkspaceGrid.RowDefinitions = new RowDefinitions("*,Auto");
            HistorySurface.IsVisible = true;
            PlaceDetailsBelowCanvas();
        }
        else
        {
            WorkspaceGrid.ColumnDefinitions = new ColumnDefinitions("250,8,*,8,330");
            WorkspaceGrid.RowDefinitions = new RowDefinitions("*");
            HistorySurface.IsVisible = true;
            DetailsSurface.IsVisible = true;
            Grid.SetColumn(DetailsSurface, 4);
            Grid.SetRow(DetailsSurface, 0);
            DetailsSurface.Margin = default;
            DetailsSurface.MaxHeight = double.PositiveInfinity;
        }
    }

    private void PlaceDetailsBelowCanvas()
    {
        DetailsSurface.IsVisible = true;
        Grid.SetColumn(DetailsSurface, 2);
        Grid.SetRow(DetailsSurface, 1);
        DetailsSurface.Margin = new Thickness(0, 8, 0, 0);
        DetailsSurface.MaxHeight = 280;
    }

    private void OnEventPublished(object? sender, ExecutionEvent item)
    {
        if (_selectedExecution?.ExecutionId != item.ExecutionId) return;
        if (Interlocked.Exchange(ref _liveRefreshQueued, 1) == 1) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed)
            {
                Interlocked.Exchange(ref _liveRefreshQueued, 0);
                return;
            }
            _liveRefreshTimer.Start();
        });
    }

    private async void RefreshLiveTrace(object? sender, EventArgs args)
    {
        _liveRefreshTimer.Stop();
        Interlocked.Exchange(ref _liveRefreshQueued, 0);
        try
        {
            if (!_disposed && _selectedExecution is { } current) await SelectExecutionAsync(current);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    private static string StatusGlyph(ExecutionActionStatus status) => status switch
    {
        ExecutionActionStatus.Completed => "✓",
        ExecutionActionStatus.Failed => "❗",
        ExecutionActionStatus.Warning => "⚠",
        ExecutionActionStatus.Running => "●",
        ExecutionActionStatus.Waiting or ExecutionActionStatus.Queued => "◷",
        ExecutionActionStatus.Suspended => "⏸",
        ExecutionActionStatus.Cancelled => "⊘",
        _ => "⚠"
    };

    private static string StatusBrushKey(ExecutionActionStatus status) => status switch
    {
        ExecutionActionStatus.Completed => "HavenSuccessBrush",
        ExecutionActionStatus.Failed => "HavenDangerBrush",
        ExecutionActionStatus.Warning or ExecutionActionStatus.UserActionRequired or ExecutionActionStatus.Blocked => "HavenWarningBrush",
        _ => "HavenBorderSubtleBrush"
    };

    private static IBrush Brush(string key, IBrush fallback) => Avalonia.Application.Current?.TryFindResource(key, out var value) == true && value is IBrush brush ? brush : fallback;
    private static TextBlock Muted(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Classes = { "muted" } };
    private static string Truncate(string value, int maximum) => value.Length <= maximum ? value : value[..Math.Max(0, maximum - 1)] + "…";
    private static string FormatDuration(TimeSpan value) => value.TotalMinutes >= 1 ? $"{value.TotalMinutes:0.#}m" : value.TotalSeconds >= 1 ? $"{value.TotalSeconds:0.#}s" : $"{value.TotalMilliseconds:0}ms";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hub is not null) _hub.Published -= OnEventPublished;
        _liveRefreshTimer.Stop();
        _liveRefreshTimer.Tick -= RefreshLiveTrace;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
