using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Haven.Desktop.Dashboard;
using Haven.Desktop.HavenUI.Components;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Home;

public sealed partial class NewDashboardPage
{
    private DashboardEditPlanner? _dashboardEditPlanner;
    private HavenPanel? _dashboardAssistantPanel;
    private HavenTextInput? _dashboardAssistantInput;
    private TextBlock? _dashboardAssistantStatus;
    private HavenPrimaryButton? _dashboardAssistantApply;
    private HavenButton? _dashboardAssistantRevert;
    private CancellationTokenSource? _dashboardAssistantCancellation;
    private DashboardAssistantSnapshot? _lastDashboardAssistantSnapshot;
    private bool _dashboardAssistantBusy;

    internal void EnableDashboardAssistant(DashboardEditPlanner planner)
    {
        ArgumentNullException.ThrowIfNull(planner);
        if (_dashboardEditPlanner is not null) return;
        _dashboardEditPlanner = planner;
        ConfigureDashboardAssistantPanel();
        EditWithHavenButton.Click += (_, _) => ToggleDashboardAssistant();
    }

    private void ConfigureDashboardAssistantPanel()
    {
        if (_dashboardAssistantPanel is not null || Content is not Grid root) return;

        var title = new TextBlock
        {
            Text = "Edit with Haven",
            FontSize = 20,
            FontWeight = Avalonia.Media.FontWeight.ExtraBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        var close = new HavenButton { Content = "Close", Classes = { "subtle" } };
        close.Click += (_, _) => ToggleDashboardAssistant(forceVisible: false);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        header.Children.Add(title);
        Grid.SetColumn(close, 1);
        header.Children.Add(close);

        _dashboardAssistantInput = new HavenTextInput
        {
            PlaceholderText = "e.g. Make Plan wide, hide Browse, and move Automations underneath",
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MinHeight = 112,
            MaxHeight = 190,
            MaxLength = 1200
        };
        _dashboardAssistantStatus = new TextBlock
        {
            Text = "Haven can show, hide, move, resize, reset, or rename this page.",
            Classes = { "muted" },
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 11
        };
        _dashboardAssistantApply = new HavenPrimaryButton { Content = "Apply with Haven" };
        _dashboardAssistantApply.Click += async (_, _) => await ApplyDashboardAssistantAsync();
        _dashboardAssistantRevert = new HavenButton { Content = "Revert last Haven change", Classes = { "subtle" }, IsEnabled = false };
        _dashboardAssistantRevert.Click += async (_, _) => await RevertDashboardAssistantAsync();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(_dashboardAssistantApply);
        buttons.Children.Add(_dashboardAssistantRevert);

        var body = new StackPanel { Spacing = 11 };
        body.Children.Add(header);
        body.Children.Add(new TextBlock
        {
            Text = "Describe the layout you want. The dashboard stays visible while Haven applies one reversible, validated layout change.",
            Classes = { "muted" },
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 12
        });
        body.Children.Add(_dashboardAssistantInput);
        body.Children.Add(buttons);
        body.Children.Add(_dashboardAssistantStatus);

        _dashboardAssistantPanel = new HavenPanel
        {
            Width = 410,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, 118, 24, 82),
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(20),
            IsVisible = false,
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content = body
            }
        };
        Grid.SetRowSpan(_dashboardAssistantPanel, 2);
        _dashboardAssistantPanel.SetValue(Panel.ZIndexProperty, 40);
        root.Children.Add(_dashboardAssistantPanel);
    }

    private void ToggleDashboardAssistant(bool? forceVisible = null)
    {
        if (_dashboardAssistantPanel is null) return;
        _dashboardAssistantPanel.IsVisible = forceVisible ?? !_dashboardAssistantPanel.IsVisible;
        if (_dashboardAssistantPanel.IsVisible) _dashboardAssistantInput?.Focus();
    }

    private async Task ApplyDashboardAssistantAsync()
    {
        if (_dashboardEditPlanner is null || _dashboardAssistantInput is null || _dashboardAssistantStatus is null
            || _dashboardAssistantApply is null || _dashboardAssistantBusy) return;

        var instruction = _dashboardAssistantInput.Text?.Trim() ?? string.Empty;
        if (instruction.Length == 0)
        {
            _dashboardAssistantStatus.Text = "Describe the change you want first.";
            _dashboardAssistantInput.Focus();
            return;
        }

        _dashboardAssistantCancellation?.Cancel();
        _dashboardAssistantCancellation?.Dispose();
        _dashboardAssistantCancellation = new CancellationTokenSource();
        var token = _dashboardAssistantCancellation.Token;
        _dashboardAssistantBusy = true;
        _dashboardAssistantApply.IsEnabled = false;
        _dashboardAssistantStatus.Text = "Planning a safe dashboard edit…";

        try
        {
            var views = _widgetViews.Count > 0
                ? _widgetViews
                : _widgetProviders.Select(provider => new DashboardWidgetViewState(provider.Definition, null, DashboardWidgetDataState.Loading)).ToArray();
            var page = SelectedPage;
            var result = await _dashboardEditPlanner.PlanAsync(instruction, page.Title, views, _widgetCanvas.Placements, token);
            if (!result.Succeeded || result.Plan is null)
            {
                _dashboardAssistantStatus.Text = result.Message;
                return;
            }

            var snapshot = new DashboardAssistantSnapshot(page.Id, page.Title, _widgetCanvas.Placements.ToArray());
            var applied = DashboardEditPlanApplier.Apply(
                result.Plan,
                page.Title,
                GetAllWidgetDefinitions(),
                _widgetCanvas.Placements);
            var titleChanged = !applied.Title.Equals(page.Title, StringComparison.Ordinal);
            var layoutChanged = _widgetCanvas.ApplyLayout(applied.Layout);

            if (titleChanged)
            {
                var index = _pages.FindIndex(item => item.Id.Equals(page.Id, StringComparison.OrdinalIgnoreCase));
                if (index >= 0) _pages[index] = _pages[index] with { Title = applied.Title };
                await SavePageStateAsync(CancellationToken.None);
                RebuildPageTabs();
            }

            if (!layoutChanged && !titleChanged)
            {
                _dashboardAssistantStatus.Text = "That plan would not visibly change this dashboard.";
                return;
            }

            _lastDashboardAssistantSnapshot = snapshot;
            if (_dashboardAssistantRevert is not null) _dashboardAssistantRevert.IsEnabled = true;
            _dashboardAssistantStatus.Text = $"{result.Plan.Summary} · Applied as one reversible change.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _dashboardAssistantStatus.Text = "Dashboard edit cancelled.";
        }
        catch (Exception ex)
        {
            _dashboardAssistantStatus.Text = $"Dashboard edit failed safely: {ex.Message}";
        }
        finally
        {
            _dashboardAssistantBusy = false;
            _dashboardAssistantApply.IsEnabled = true;
        }
    }

    private async Task RevertDashboardAssistantAsync()
    {
        if (_lastDashboardAssistantSnapshot is not { } snapshot || _dashboardAssistantStatus is null) return;
        if (!snapshot.PageId.Equals(_selectedPageId, StringComparison.OrdinalIgnoreCase))
        {
            _dashboardAssistantStatus.Text = "Switch back to the dashboard page Haven edited before reverting it.";
            return;
        }

        var pageIndex = _pages.FindIndex(item => item.Id.Equals(snapshot.PageId, StringComparison.OrdinalIgnoreCase));
        if (pageIndex < 0)
        {
            _dashboardAssistantStatus.Text = "That dashboard page no longer exists.";
            return;
        }

        _pages[pageIndex] = _pages[pageIndex] with { Title = snapshot.Title };
        await SavePageStateAsync(CancellationToken.None);
        _widgetCanvas.ApplyLayout(snapshot.Layout);
        RebuildPageTabs();
        _lastDashboardAssistantSnapshot = null;
        if (_dashboardAssistantRevert is not null) _dashboardAssistantRevert.IsEnabled = false;
        _dashboardAssistantStatus.Text = "Reverted the last Haven dashboard change.";
    }

    private sealed record DashboardAssistantSnapshot(
        string PageId,
        string Title,
        IReadOnlyList<DashboardWidgetPlacement> Layout);
}
