using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Dashboard;
using Haven.Desktop.HavenUI.Components;

namespace Haven.Desktop.Views.Pages.Home;

internal sealed record DashboardCustomWidgetDefinition(
    string Id,
    string Title,
    string Value,
    string Detail)
{
    public string Key => $"custom:{Id}";
}

internal sealed record DashboardCustomWidgetState(
    int Version,
    Dictionary<string, List<DashboardCustomWidgetDefinition>> Pages);

public sealed partial class NewDashboardPage
{
    private const string CustomWidgetStateKey = "dashboard.custom-widgets.v1";
    private const string CustomWidgetActionPrefix = "dashboard-custom-edit:";
    private DashboardCustomWidgetState? _customWidgetState;

    internal static DashboardWidgetViewState ToCustomWidgetView(DashboardCustomWidgetDefinition widget)
    {
        var definition = new DashboardTileDefinition(
            widget.Key,
            widget.Title,
            "Local custom widget",
            "edit",
            "custom-local",
            CustomWidgetActionPrefix + widget.Id,
            DashboardTileSize.Standard,
            1000,
            IsBuiltIn: false);
        return new DashboardWidgetViewState(
            definition,
            new DashboardTileData(widget.Value, widget.Detail),
            DashboardWidgetDataState.Ready);
    }

    internal static bool TryGetCustomWidgetId(string? actionKey, out string id)
    {
        id = string.Empty;
        if (string.IsNullOrWhiteSpace(actionKey) || !actionKey.StartsWith(CustomWidgetActionPrefix, StringComparison.OrdinalIgnoreCase))
            return false;
        var candidate = actionKey[CustomWidgetActionPrefix.Length..].Trim();
        if (!Guid.TryParseExact(candidate, "N", out var parsed)) return false;
        id = parsed.ToString("N");
        return true;
    }

    private async Task EnsureCustomWidgetStateLoadedAsync(CancellationToken token)
    {
        if (_customWidgetState is not null) return;
        var stored = await _settings.GetAsync<DashboardCustomWidgetState>(CustomWidgetStateKey, token);
        var pages = new Dictionary<string, List<DashboardCustomWidgetDefinition>>(StringComparer.OrdinalIgnoreCase);
        if (stored is { Version: 1 })
        {
            foreach (var (pageId, widgets) in stored.Pages)
            {
                if (string.IsNullOrWhiteSpace(pageId)) continue;
                pages[pageId] = widgets
                    .Select(NormalizeCustomWidget)
                    .Where(widget => widget is not null)
                    .Cast<DashboardCustomWidgetDefinition>()
                    .GroupBy(widget => widget.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.Last())
                    .Take(32)
                    .ToList();
            }
        }
        _customWidgetState = new DashboardCustomWidgetState(1, pages);
    }

    private static DashboardCustomWidgetDefinition? NormalizeCustomWidget(DashboardCustomWidgetDefinition? widget)
    {
        if (widget is null || !Guid.TryParseExact(widget.Id, "N", out var parsed)) return null;
        var title = (widget.Title ?? string.Empty).Trim();
        if (title.Length is < 1 or > 60) return null;
        var value = (widget.Value ?? string.Empty).Trim();
        var detail = (widget.Detail ?? string.Empty).Trim();
        if (value.Length > 120 || detail.Length > 240) return null;
        return new DashboardCustomWidgetDefinition(parsed.ToString("N"), title, value, detail);
    }

    private IReadOnlyList<DashboardCustomWidgetDefinition> CurrentCustomWidgets()
    {
        if (_customWidgetState is null) return [];
        return _customWidgetState.Pages.TryGetValue(_selectedPageId, out var widgets) ? widgets : [];
    }

    private IReadOnlyList<DashboardWidgetViewState> BuildCustomWidgetViews() =>
        CurrentCustomWidgets().Select(ToCustomWidgetView).ToArray();

    private IReadOnlyList<DashboardTileDefinition> GetAllWidgetDefinitions() =>
        _widgetProviders.Select(provider => provider.Definition)
            .Concat(BuildCustomWidgetViews().Select(view => view.Definition))
            .ToArray();

    private IReadOnlyList<DashboardWidgetViewState> AppendCustomWidgetViews(IEnumerable<DashboardWidgetViewState> providerViews) =>
        providerViews.Concat(BuildCustomWidgetViews()).ToArray();

    private bool TryOpenCustomWidget(string actionKey)
    {
        if (!TryGetCustomWidgetId(actionKey, out var id)) return false;
        var widget = CurrentCustomWidgets().FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (widget is not null) ShowCustomWidgetEditor(widget, (Control?)_widgetArrangeButton ?? ConfigurePageButton);
        return true;
    }

    private void ShowCustomWidgetEditor(DashboardCustomWidgetDefinition? existing, Control anchor)
    {
        var isNew = existing is null;
        var source = existing ?? new DashboardCustomWidgetDefinition(Guid.NewGuid().ToString("N"), "Custom widget", string.Empty, string.Empty);
        var titleBox = new HavenTextInput { Text = source.Title, PlaceholderText = "Widget title", MaxLength = 60 };
        var valueBox = new HavenTextInput { Text = source.Value, PlaceholderText = "Value or note", MaxLength = 120 };
        var detailBox = new HavenTextInput
        {
            Text = source.Detail,
            PlaceholderText = "Detail (optional)",
            MaxLength = 240,
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MinHeight = 80
        };
        var save = new HavenPrimaryButton { Content = isNew ? "Add widget" : "Save widget", HorizontalAlignment = HorizontalAlignment.Stretch };
        var remove = new HavenNegativeButton { Content = "Delete widget", HorizontalAlignment = HorizontalAlignment.Stretch, IsVisible = !isNew };
        var body = new StackPanel
        {
            Width = 420,
            Spacing = 9,
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock { Text = isNew ? "Add custom widget" : "Edit custom widget", FontSize = 21, FontWeight = Avalonia.Media.FontWeight.ExtraBold },
                new TextBlock { Text = "Custom widgets are local text only. They cannot run code, HTML, links or external actions.", Classes = { "muted" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                titleBox,
                valueBox,
                detailBox,
                save,
                remove
            }
        };
        var flyout = new HavenDropdown
        {
            Content = new HavenDropdownCard { CornerRadius = new CornerRadius(22), Child = body }
        };

        save.Click += async (_, _) =>
        {
            var title = titleBox.Text?.Trim() ?? string.Empty;
            var value = valueBox.Text?.Trim() ?? string.Empty;
            var detail = detailBox.Text?.Trim() ?? string.Empty;
            if (title.Length == 0) { titleBox.Focus(); return; }
            if (value.Length == 0 && detail.Length == 0) { valueBox.Focus(); return; }
            if (_customWidgetState is null)
                _customWidgetState = new DashboardCustomWidgetState(1, new Dictionary<string, List<DashboardCustomWidgetDefinition>>(StringComparer.OrdinalIgnoreCase));
            if (!_customWidgetState.Pages.TryGetValue(_selectedPageId, out var widgets))
                _customWidgetState.Pages[_selectedPageId] = widgets = [];
            var updated = new DashboardCustomWidgetDefinition(source.Id, title, value, detail);
            var index = widgets.FindIndex(item => item.Id.Equals(source.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) widgets[index] = updated; else if (widgets.Count < 32) widgets.Add(updated);
            await SaveCustomWidgetStateAsync();
            await ReconcileCustomWidgetPresentationAsync();
            flyout.Hide();
        };

        var deleteArmed = false;
        remove.Click += async (_, _) =>
        {
            if (!deleteArmed) { deleteArmed = true; remove.Content = "Confirm delete"; return; }
            if (_customWidgetState?.Pages.TryGetValue(_selectedPageId, out var widgets) == true)
                widgets.RemoveAll(item => item.Id.Equals(source.Id, StringComparison.OrdinalIgnoreCase));
            if (_widgetLayoutState?.Pages.TryGetValue(_selectedPageId, out var layout) == true)
                layout.RemoveAll(item => item.Key.Equals(source.Key, StringComparison.OrdinalIgnoreCase));
            await SaveCustomWidgetStateAsync();
            await SaveWidgetLayoutStateAsync(_selectedPageId.Equals(HomePageId, StringComparison.OrdinalIgnoreCase));
            await ReconcileCustomWidgetPresentationAsync();
            flyout.Hide();
        };

        flyout.ShowAt(anchor);
        titleBox.SelectAll();
        titleBox.Focus();
    }

    private async Task ReconcileCustomWidgetPresentationAsync()
    {
        var providerViews = _widgetViews.Where(view => !view.Definition.ProviderKey.Equals("custom-local", StringComparison.OrdinalIgnoreCase));
        _widgetViews = AppendCustomWidgetViews(providerViews);
        if (_widgetLayoutState is not null)
        {
            var next = DashboardWidgetLayoutEngine.EnsurePlacements(GetAllWidgetDefinitions(), GetCurrentWidgetPlacements()).ToList();
            _widgetLayoutState.Pages[_selectedPageId] = next;
            await SaveWidgetLayoutStateAsync(_selectedPageId.Equals(HomePageId, StringComparison.OrdinalIgnoreCase));
        }
        RenderWidgetPage();
    }

    private Task SaveCustomWidgetStateAsync()
    {
        _customWidgetState ??= new DashboardCustomWidgetState(1, new Dictionary<string, List<DashboardCustomWidgetDefinition>>(StringComparer.OrdinalIgnoreCase));
        var snapshot = new DashboardCustomWidgetState(1, _customWidgetState.Pages.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Select(NormalizeCustomWidget).Where(widget => widget is not null).Cast<DashboardCustomWidgetDefinition>().Take(32).ToList(),
            StringComparer.OrdinalIgnoreCase));
        return _settings.SetAsync(CustomWidgetStateKey, snapshot, CancellationToken.None);
    }
}
