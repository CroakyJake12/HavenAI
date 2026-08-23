using Haven.UI;

namespace Haven.Desktop.Views.Pages.Canvas;

internal sealed class CanvasHueStrip : HavenElement, IHavenDrawCommandSource, IHavenPointerInputTarget
{
    private double _hue = 210;

    public CanvasHueStrip()
    {
        Name = "Canvas.Pen.Hue";
        Accessibility.Focusable = true;
        Accessibility.AccessibleName = "Pen colour hue";
        SetValue(HavenProperties.Width, HavenLength.Percent(100));
        SetValue(HavenProperties.Height, HavenLength.Px(26));
        SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(9)));
        SetValue(HavenProperties.Clip, true);
    }

    public event Action<double, string>? HueChanged;
    public double Hue => _hue;
    public string Hex => HueToHex(_hue);

    public void SetHue(double hue)
    {
        _hue = Normalize(hue);
        Invalidate();
    }

    public bool PointerPressed(HavenPointerInput input) => Update(input.LocalPosition.X);
    public bool PointerMoved(HavenPointerInput input) => Update(input.LocalPosition.X);
    public bool PointerReleased(HavenPointerInput input) => Update(input.LocalPosition.X);

    public void Draw(HavenDrawingContext context, double opacity)
    {
        if (Bounds.Width <= 1 || Bounds.Height <= 1) return;
        const int segments = 60;
        var step = Bounds.Width / segments;
        for (var i = 0; i < segments; i++)
        {
            var hue = i * 360d / segments;
            var (r, g, b) = HueToRgb(hue);
            context.Add(new HavenFillRoundedRectCommand(
                new HavenRect(Bounds.X + i * step, Bounds.Y, step + 1, Bounds.Height),
                new HavenSolidBrush(255, r, g, b), 0, opacity));
        }
        var x = Bounds.X + Bounds.Width * _hue / 360d;
        context.Add(new HavenStrokeRoundedRectCommand(
            new HavenRect(x - 3, Bounds.Y + 1, 6, Math.Max(1, Bounds.Height - 2)),
            new HavenPen(new HavenSolidBrush(255, 255, 255, 255), 2), 3, opacity));
    }

    private bool Update(double x)
    {
        if (Bounds.Width <= 1) return false;
        var next = Math.Clamp(x / Bounds.Width * 360d, 0, 360);
        if (Math.Abs(next - _hue) < .1) return true;
        _hue = next;
        HueChanged?.Invoke(_hue, HueToHex(_hue));
        Invalidate();
        return true;
    }

    private static double Normalize(double hue)
    {
        hue %= 360;
        return hue < 0 ? hue + 360 : hue;
    }

    private static string HueToHex(double hue)
    {
        var (r, g, b) = HueToRgb(hue);
        return $"#FF{r:X2}{g:X2}{b:X2}";
    }

    private static (byte R, byte G, byte B) HueToRgb(double hue)
    {
        var h = Normalize(hue) / 60d;
        var x = 1d - Math.Abs(h % 2d - 1d);
        var (r, g, b) = h switch
        {
            < 1 => (1d, x, 0d), < 2 => (x, 1d, 0d), < 3 => (0d, 1d, x),
            < 4 => (0d, x, 1d), < 5 => (x, 0d, 1d), _ => (1d, 0d, x)
        };
        return ((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
    }
}
