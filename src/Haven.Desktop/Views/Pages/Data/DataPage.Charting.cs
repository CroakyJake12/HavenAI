using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Data;

public sealed partial class DataPage
{
    private Container? _chartCard;
    private Container? _chartHost;
    private Input? _chartTitleInput;
    private Input? _chartRangeInput;
    private Input? _chartXAxisInput;
    private Input? _chartYAxisInput;
    private HavenButton? _chartTypeButton;
    private HavenButton? _chartLegendButton;
    private DataChartSurface? _chartSurface;
    private int _chartIndex;
    private bool _syncingChartUi;

    private void InitializeCharting()
    {
        var toolbar = _route.Root.DescendantsAndSelf().OfType<Container>().FirstOrDefault(element => element.Name == "Data.Grid.Toolbar");
        if (toolbar is not null && !toolbar.Children.Any(child => child.Name == "Data.Chart.Create")) toolbar.Add(RecoveredButton("Data.Chart.Create", "Create chart", CreateChartFromSelection));
        if (_chartCard is not null) return;

        _chartCard = RecoveredCard("Data.Chart.Editor");
        _chartCard.Add(new HavenText("Chart") { Name = "Data.Chart.Heading", Level = TextLevel.H2 });
        var nav = RecoveredToolbar("Data.Chart.Actions");
        nav.Add(RecoveredButton("Data.Chart.Previous", "Previous", () => MoveChart(-1)));
        nav.Add(RecoveredButton("Data.Chart.Next", "Next", () => MoveChart(1)));
        _chartTypeButton = RecoveredButton("Data.Chart.Type", "Column", CycleChartType); nav.Add(_chartTypeButton);
        _chartLegendButton = RecoveredButton("Data.Chart.Legend", "Legend on", ToggleChartLegend); nav.Add(_chartLegendButton);
        nav.Add(RecoveredButton("Data.Chart.Delete", "Delete chart", DeleteCurrentChart)); _chartCard.Add(nav);

        _chartTitleInput = RecoveredInput("Data.Chart.Title", "Chart title", "Chart");
        _chartRangeInput = RecoveredInput("Data.Chart.Range", "Source range", "A1:B10");
        _chartXAxisInput = RecoveredInput("Data.Chart.XAxis", "X axis title", "");
        _chartYAxisInput = RecoveredInput("Data.Chart.YAxis", "Y axis title", "");
        foreach (var input in new[] { _chartTitleInput, _chartRangeInput, _chartXAxisInput, _chartYAxisInput }) { input.Invalidated += (_, _) => UpdateChartFromInputs(); _chartCard.Add(input); }
        _chartHost = new Container { Name = "Data.Chart.SurfaceHost", Layout = HavenLayout.Grid }; _chartHost.SetValue(HavenProperties.MinHeight, HavenLength.Px(280)); _chartCard.Add(_chartHost);
        _route.Editor.Add(_chartCard); SyncChartUi();
    }

    private void CreateChartFromSelection()
    {
        if (Workbook is null || CurrentSheet is null || SpreadsheetSurface() is not { } surface) return;
        var selected = surface.Selection; if (!ValidateCommandRange(selected, "create a chart")) return;
        var activity = BeginDataActivity("Create spreadsheet chart", $"Creating a live chart from {Address(selected.StartRow, selected.StartColumn)}:{Address(selected.EndRow, selected.EndColumn)}.");
        CaptureSpreadsheetUndo();
        var range = new DataCellRange { StartRow = selected.StartRow, StartColumn = selected.StartColumn, EndRow = selected.EndRow, EndColumn = selected.EndColumn };
        var chart = new DataChartDefinition
        {
            SheetId = CurrentSheet.Id, SourceRange = range, Title = $"Chart {Workbook.Charts.Count(value => value.SheetId == CurrentSheet.Id) + 1}",
            FirstRowIsHeaders = selected.RowCount > 1, CategoryColumn = selected.StartColumn,
            SeriesColumns = Enumerable.Range(selected.StartColumn, selected.ColumnCount).Where(column => column != selected.StartColumn).ToList()
        };
        if (chart.SeriesColumns.Count == 0) chart.SeriesColumns.Add(selected.StartColumn);
        Workbook.Charts.Add(chart); _chartIndex = Workbook.Charts.Where(value => value.SheetId == CurrentSheet.Id).ToList().Count - 1; MarkDirty(); SyncChartUi(); var status = $"Created live chart from {range}."; _route.SetStatus(status); CompleteDataActivity(activity, status);
    }

    private IReadOnlyList<DataChartDefinition> CurrentSheetCharts() => Workbook is null || CurrentSheet is null ? [] : Workbook.Charts.Where(chart => chart.SheetId == CurrentSheet.Id).ToArray();
    private DataChartDefinition? CurrentChart() { var charts = CurrentSheetCharts(); if (charts.Count == 0) return null; _chartIndex = Math.Clamp(_chartIndex, 0, charts.Count - 1); return charts[_chartIndex]; }

    private void MoveChart(int delta) { var charts = CurrentSheetCharts(); if (charts.Count == 0) return; _chartIndex = (_chartIndex + delta + charts.Count) % charts.Count; SyncChartUi(); }
    private void CycleChartType() { var chart = CurrentChart(); if (chart is null) return; CaptureSpreadsheetUndo(); chart.Type = (DataChartType)(((int)chart.Type + 1) % Enum.GetValues<DataChartType>().Length); MarkDirty(); SyncChartUi(); }
    private void ToggleChartLegend() { var chart = CurrentChart(); if (chart is null) return; CaptureSpreadsheetUndo(); chart.ShowLegend = !chart.ShowLegend; MarkDirty(); SyncChartUi(); }
    private void DeleteCurrentChart() { if (Workbook is null || CurrentChart() is not { } chart) return; CaptureSpreadsheetUndo(); Workbook.Charts.Remove(chart); _chartIndex = Math.Max(0, _chartIndex - 1); MarkDirty(); SyncChartUi(); _route.SetStatus("Deleted chart."); }

    private void UpdateChartFromInputs()
    {
        if (_syncingChartUi || CurrentChart() is not { } chart) return;
        var title = (_chartTitleInput?.Text ?? string.Empty).Trim(); if (title.Length == 0) title = chart.Title;
        var xAxis = (_chartXAxisInput?.Text ?? string.Empty).Trim();
        var yAxis = (_chartYAxisInput?.Text ?? string.Empty).Trim();
        var hasRange = DataCellRange.TryParse(_chartRangeInput?.Text, out var range);
        var rangeChanged = hasRange && range.ToString() != chart.SourceRange.ToString();
        if (string.Equals(title, chart.Title, StringComparison.Ordinal) && string.Equals(xAxis, chart.XAxisTitle, StringComparison.Ordinal) && string.Equals(yAxis, chart.YAxisTitle, StringComparison.Ordinal) && !rangeChanged) return;
        CaptureSpreadsheetUndo();
        chart.Title = title; chart.XAxisTitle = xAxis; chart.YAxisTitle = yAxis;
        if (rangeChanged)
        {
            chart.SourceRange = range; chart.CategoryColumn = range.StartColumn; chart.SeriesColumns = Enumerable.Range(range.StartColumn, range.ColumnCount).Where(column => column != range.StartColumn).ToList(); if (chart.SeriesColumns.Count == 0) chart.SeriesColumns.Add(range.StartColumn);
        }
        chart.Normalize(); MarkDirty(); UpdateChartSurface(chart);
    }

    private void SyncChartUi()
    {
        if (_chartCard is null || _chartHost is null) return; var chart = CurrentChart(); _chartCard.SetValue(HavenProperties.Visibility, chart is null ? HavenVisibility.Collapsed : HavenVisibility.Visible); if (chart is null) { _chartHost.Children.ToList().ForEach(child => child.Parent?.Remove(child)); _chartSurface = null; return; }
        _syncingChartUi = true;
        try
        {
            if (_chartTitleInput is not null) _chartTitleInput.Text = chart.Title; if (_chartRangeInput is not null) _chartRangeInput.Text = chart.SourceRange.ToString(); if (_chartXAxisInput is not null) _chartXAxisInput.Text = chart.XAxisTitle; if (_chartYAxisInput is not null) _chartYAxisInput.Text = chart.YAxisTitle;
            if (_chartTypeButton is not null) _chartTypeButton.Content = chart.Type.ToString(); if (_chartLegendButton is not null) _chartLegendButton.Content = chart.ShowLegend ? "Legend on" : "Legend off"; UpdateChartSurface(chart);
        }
        finally { _syncingChartUi = false; }
    }

    private void UpdateChartSurface(DataChartDefinition chart)
    {
        if (_chartHost is null || CurrentSheet is null) return;
        if (_chartSurface is null) { _chartSurface = new DataChartSurface(CurrentSheet, chart); _chartHost.Children.ToList().ForEach(child => child.Parent?.Remove(child)); _chartHost.Add(_chartSurface); } else _chartSurface.Update(CurrentSheet, chart);
    }
}
