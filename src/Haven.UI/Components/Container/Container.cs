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
    private string _columns = "1fr";
    private string _rows = "Auto";

    public Container()
    {
        Accessibility.Role = HavenAccessibleRole.Group;
        SetValue(HavenProperties.Hover, false, HavenValueSource.Default);
        SetValue(HavenProperties.Background, "Transparent", HavenValueSource.Default);
    }

    public HavenLayout Layout { get; set; } = HavenLayout.Vertical;
    public string Columns { get => _columns; set => _columns = string.IsNullOrWhiteSpace(value) ? "1fr" : value; }
    public string Rows { get => _rows; set => _rows = string.IsNullOrWhiteSpace(value) ? "Auto" : value; }
    public double ScrollX { get; set; }
    public double ScrollY { get; set; }

    public IReadOnlyList<HavenLength> ColumnTracks => ParseTracks(Columns);
    public IReadOnlyList<HavenLength> RowTracks => ParseTracks(Rows);

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
}
