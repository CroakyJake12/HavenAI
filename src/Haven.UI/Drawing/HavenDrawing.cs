namespace Haven.UI;

public abstract record HavenBrush;
public sealed record HavenTokenBrush(string Token) : HavenBrush;
public sealed record HavenSolidBrush(byte A, byte R, byte G, byte B) : HavenBrush;
public sealed record HavenPen(HavenBrush Brush, double Thickness);
public sealed record HavenPath(IReadOnlyList<HavenPoint> Points, bool Closed = false);
public sealed record HavenGeometry(HavenPath Path);
public sealed record HavenTransform(double ScaleX = 1, double ScaleY = 1, double RotationDegrees = 0, double TranslateX = 0, double TranslateY = 0);
public sealed record HavenImage(string Source);
public sealed record HavenTextLayout(string Text, string FontFamily, double FontSize, int FontWeight, double MaxWidth = double.PositiveInfinity);
public sealed record HavenShadow(HavenBrush Brush, double Blur, double OffsetX, double OffsetY);
public sealed record HavenGlow(HavenBrush Brush, double Blur, double Opacity);

public abstract record HavenDrawCommand(HavenRect Bounds, double Opacity = 1d);
public sealed record HavenPushTransformCommand(HavenRect Rect, HavenTransform Transform, HavenPoint Origin) : HavenDrawCommand(Rect);
public sealed record HavenPopTransformCommand(HavenRect Rect) : HavenDrawCommand(Rect);
public sealed record HavenFillRoundedRectCommand(HavenRect Rect, HavenBrush Brush, double Radius, double Alpha = 1d) : HavenDrawCommand(Rect, Alpha);
public sealed record HavenStrokeRoundedRectCommand(HavenRect Rect, HavenPen Pen, double Radius, double Alpha = 1d) : HavenDrawCommand(Rect, Alpha);
public sealed record HavenTextCommand(HavenRect Rect, HavenTextLayout Layout, HavenBrush Brush, double Alpha = 1d) : HavenDrawCommand(Rect, Alpha);
public sealed record HavenLineCommand(HavenPoint Start, HavenPoint End, HavenPen Pen, double Alpha = 1d) : HavenDrawCommand(new HavenRect(Math.Min(Start.X, End.X), Math.Min(Start.Y, End.Y), Math.Abs(End.X - Start.X), Math.Abs(End.Y - Start.Y)), Alpha);
public sealed record HavenEllipseCommand(HavenRect Rect, HavenBrush Brush, HavenPen? Pen = null, double Alpha = 1d) : HavenDrawCommand(Rect, Alpha);
public sealed record HavenImageCommand(HavenRect Rect, HavenImage Image, double Alpha = 1d) : HavenDrawCommand(Rect, Alpha);
public sealed record HavenGlowCommand(HavenRect Rect, HavenGlow Glow, double Radius) : HavenDrawCommand(Rect, Glow.Opacity);

public sealed class HavenDrawingContext
{
    private readonly List<HavenDrawCommand> _commands = [];
    public IReadOnlyList<HavenDrawCommand> Commands => _commands;
    public void Add(HavenDrawCommand command) => _commands.Add(command ?? throw new ArgumentNullException(nameof(command)));
}
