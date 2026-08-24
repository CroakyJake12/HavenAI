namespace Haven.UI.Components;

public sealed class Canvas : Container
{
    public Canvas() => Layout = HavenLayout.Canvas;
    public double PanX { get; set; }
    public double PanY { get; set; }
    public double Zoom { get; set; } = 1d;
    public bool PanEnabled { get; set; } = true;
    public bool ZoomEnabled { get; set; } = true;
    public override HavenComponentMetadata Metadata => new("Canvas", "Components/Canvas/Canvas.cs", ["Canvas"], [], "Canvas children are positioned/drawn through Haven scene commands; backend controls are not created per object.");
}
