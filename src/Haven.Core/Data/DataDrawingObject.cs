namespace Haven.Core;

public sealed class DataDrawingObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DataDrawingKind Kind { get; set; } = DataDrawingKind.CustomShape;
    public string Name { get; set; } = "Custom shape";
    public double X { get; set; } = 40;
    public double Y { get; set; } = 40;
    public double Width { get; set; } = 240;
    public double Height { get; set; } = 160;
    public double Rotation { get; set; }
    public int ZIndex { get; set; }
    public bool Locked { get; set; }
    public DocumentVectorShape? VectorShape { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);

    public void Normalize()
    {
        if (Id == Guid.Empty) Id = Guid.NewGuid();
        Name = string.IsNullOrWhiteSpace(Name) ? "Custom shape" : Name.Trim();
        X = double.IsFinite(X) ? X : 40;
        Y = double.IsFinite(Y) ? Y : 40;
        Width = double.IsFinite(Width) && Width > 0 ? Math.Clamp(Width, 1, 100000) : 240;
        Height = double.IsFinite(Height) && Height > 0 ? Math.Clamp(Height, 1, 100000) : 160;
        Rotation = double.IsFinite(Rotation) ? ((Rotation % 360) + 360) % 360 : 0;
        Metadata ??= new Dictionary<string, string>(StringComparer.Ordinal);
        VectorShape?.Normalize();
    }
}
