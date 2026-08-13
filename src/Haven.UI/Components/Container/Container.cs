namespace Haven.UI.Components;

public enum HavenLayout
{
    Vertical,
    Horizontal,
    Grid,
    Wrap,
    Canvas,
    Overlay
}

/// <summary>
/// Canonical Haven layout/framing primitive. Grid, stack and border concepts
/// are properties of Container rather than separate normal UI primitives.
/// </summary>
public class Container : HavenElement
{
    private HavenLayout _layout = HavenLayout.Vertical;
    private string _columns = "1fr";
    private string _rows = "Auto";
    private double _scrollX;
    private double _scrollY;

    public Container()
    {
        Accessibility.Role = HavenAccessibleRole.Group;
        SetValue(HavenProperties.Hover, false, HavenValueSource.Default);
        SetValue(HavenProperties.Background, "Transparent", HavenValueSource.Default);
    }

    public HavenLayout Layout
    {
        get => _layout;
        set { if (_layout == value) return; _layout = value; Invalidate(); }
    }

    public string Columns
    {
        get => _columns;
        set
        {
            var next = string.IsNullOrWhiteSpace(value) ? "1fr" : value;
            if (_columns == next) return;
            _columns = next;
            Invalidate();
        }
    }

    public string Rows
    {
        get => _rows;
        set
        {
            var next = string.IsNullOrWhiteSpace(value) ? "Auto" : value;
            if (_rows == next) return;
            _rows = next;
            Invalidate();
        }
    }

    public double ScrollX
    {
        get => _scrollX;
        set => SetScrollOffset(value, _scrollY);
    }

    public double ScrollY
    {
        get => _scrollY;
        set => SetScrollOffset(_scrollX, value);
    }

    /// <summary>Viewport available to this container's content after padding.</summary>
    public HavenSize ViewportSize { get; private set; }

    /// <summary>Measured content extent before scroll translation.</summary>
    public HavenSize ExtentSize { get; private set; }

    public double MaxScrollX => Math.Max(0, ExtentSize.Width - ViewportSize.Width);
    public double MaxScrollY => Math.Max(0, ExtentSize.Height - ViewportSize.Height);

    internal HavenSize MeasuredContentSize { get; set; }

    public IReadOnlyList<HavenLength> ColumnTracks => ParseTracks(Columns);
    public IReadOnlyList<HavenLength> RowTracks => ParseTracks(Rows);

    public bool ScrollBy(double deltaX, double deltaY) =>
        SetScrollOffset(_scrollX + deltaX, _scrollY + deltaY);

    internal void UpdateScrollMetrics(HavenSize viewport, HavenSize extent)
    {
        ViewportSize = new HavenSize(Math.Max(0, viewport.Width), Math.Max(0, viewport.Height));
        ExtentSize = new HavenSize(
            Math.Max(ViewportSize.Width, extent.Width),
            Math.Max(ViewportSize.Height, extent.Height));
        _scrollX = Math.Clamp(_scrollX, 0, MaxScrollX);
        _scrollY = Math.Clamp(_scrollY, 0, MaxScrollY);
    }

    public override HavenComponentMetadata Metadata => new(
        "Container",
        "Components/Container/Container.cs",
        ["Container"],
        [],
        "Layout modes and grouping defaults live here; measure/arrange lives in Layout/HavenLayoutEngine.cs.");

    private static IReadOnlyList<HavenLength> ParseTracks(string value) => value
        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(HavenLength.Parse)
        .ToArray();

    private bool SetScrollOffset(double x, double y)
    {
        var nextX = Math.Clamp(double.IsFinite(x) ? x : 0, 0, MaxScrollX);
        var nextY = Math.Clamp(double.IsFinite(y) ? y : 0, 0, MaxScrollY);
        if (Math.Abs(nextX - _scrollX) < .0001d && Math.Abs(nextY - _scrollY) < .0001d) return false;
        _scrollX = nextX;
        _scrollY = nextY;
        Invalidate();
        return true;
    }
}
