using System.Globalization;
using System.Text.Json;
using Haven.Core;
using Haven.UI;

namespace Haven.Desktop.HavenUI.GenerativeUi;

/// <summary>
/// Backend-neutral generated plot. It emits only Haven drawing commands, so desktop/mobile backends
/// render the same visual without embedding an Avalonia chart control.
/// </summary>
internal sealed class HavenGenUiPlot : HavenElement, IHavenDrawCommandSource
{
    private IReadOnlyList<double[]> _series;
    private IReadOnlyList<string> _expressions;
    private bool _barChart;
    private double _xMin;
    private double _xMax;
    private double _yMin;
    private double _yMax;

    private HavenGenUiPlot(
        IReadOnlyList<double[]> series,
        IReadOnlyList<string> expressions,
        bool barChart,
        double xMin,
        double xMax,
        double yMin,
        double yMax)
    {
        _series = series;
        _expressions = expressions;
        _barChart = barChart;
        _xMin = xMin;
        _xMax = xMax;
        _yMin = yMin;
        _yMax = yMax;
        Accessibility.Role = HavenAccessibleRole.Image;
        Accessibility.AccessibleName = barChart ? "Generated chart" : "Generated graph";
        SetValue(HavenProperties.MinHeight, HavenLength.Px(220));
        SetValue(HavenProperties.Width, HavenLength.Percent(100));
        SetValue(HavenProperties.Background, "SurfaceRaised");
        SetValue(HavenProperties.BorderColor, "Border");
        SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(16)));
        SetValue(HavenProperties.Clip, true);
    }

    public static HavenGenUiPlot FromComponent(GenUiComponent component)
    {
        var expressions = ReadStrings(component, "expressions");
        var series = ReadSeries(component);
        var xMin = ReadDouble(component, "xMin", -10);
        var xMax = ReadDouble(component, "xMax", 10);
        var yMin = ReadDouble(component, "yMin", -2);
        var yMax = ReadDouble(component, "yMax", 2);
        if (component.Properties.TryGetValue("viewport", out var viewport) && viewport.ValueKind == JsonValueKind.Object)
        {
            xMin = ReadDouble(viewport, "xMin", xMin);
            xMax = ReadDouble(viewport, "xMax", xMax);
            yMin = ReadDouble(viewport, "yMin", yMin);
            yMax = ReadDouble(viewport, "yMax", yMax);
        }
        if (xMax <= xMin) (xMin, xMax) = (-10, 10);
        if (yMax <= yMin)
        {
            var values = series.SelectMany(value => value).Where(double.IsFinite).ToArray();
            if (values.Length > 0)
            {
                var min = values.Min();
                var max = values.Max();
                var pad = Math.Max(.5, (max - min) * .12);
                yMin = min - pad;
                yMax = max + pad;
            }
            else (yMin, yMax) = (-2, 2);
        }
        var bar = component.ComponentType.Equals("HavenChart", StringComparison.OrdinalIgnoreCase)
                  && !ReadString(component, "kind").Equals("line", StringComparison.OrdinalIgnoreCase);
        return new HavenGenUiPlot(series, expressions, bar, xMin, xMax, yMin, yMax);
    }

    public void Update(GenUiComponent component)
    {
        var next = FromComponent(component);
        _series = next._series;
        _expressions = next._expressions;
        _barChart = next._barChart;
        _xMin = next._xMin;
        _xMax = next._xMax;
        _yMin = next._yMin;
        _yMax = next._yMax;
        Accessibility.AccessibleName = next.Accessibility.AccessibleName;
    }

    public void Draw(HavenDrawingContext context, double opacity)
    {
        var rect = Bounds;
        if (rect.Width <= 20 || rect.Height <= 20) return;

        var left = rect.X + 34;
        var top = rect.Y + 18;
        var right = rect.Right - 18;
        var bottom = rect.Bottom - 28;
        if (right <= left || bottom <= top) return;

        var gridPen = new HavenPen(new HavenTokenBrush("Border"), 1);
        for (var i = 0; i <= 4; i++)
        {
            var x = left + (right - left) * i / 4d;
            var y = top + (bottom - top) * i / 4d;
            context.Add(new HavenLineCommand(new HavenPoint(x, top), new HavenPoint(x, bottom), gridPen, opacity * .42));
            context.Add(new HavenLineCommand(new HavenPoint(left, y), new HavenPoint(right, y), gridPen, opacity * .42));
        }

        var axisPen = new HavenPen(new HavenTokenBrush("TextSecondary"), 1.35);
        if (_xMin <= 0 && _xMax >= 0)
        {
            var x = MapX(0, left, right);
            context.Add(new HavenLineCommand(new HavenPoint(x, top), new HavenPoint(x, bottom), axisPen, opacity * .85));
        }
        if (_yMin <= 0 && _yMax >= 0)
        {
            var y = MapY(0, top, bottom);
            context.Add(new HavenLineCommand(new HavenPoint(left, y), new HavenPoint(right, y), axisPen, opacity * .85));
        }

        if (_barChart && _series.Count > 0)
            DrawBars(context, left, top, right, bottom, opacity);
        else
            DrawLines(context, left, top, right, bottom, opacity);
    }

    private void DrawBars(HavenDrawingContext context, double left, double top, double right, double bottom, double opacity)
    {
        var values = _series[0];
        if (values.Length == 0) return;
        var slot = (right - left) / values.Length;
        var width = Math.Max(2, slot * .66);
        var zero = MapY(Math.Clamp(0, _yMin, _yMax), top, bottom);
        for (var i = 0; i < values.Length; i++)
        {
            if (!double.IsFinite(values[i])) continue;
            var y = MapY(Math.Clamp(values[i], _yMin, _yMax), top, bottom);
            var rect = new HavenRect(left + i * slot + (slot - width) / 2, Math.Min(y, zero), width, Math.Max(1, Math.Abs(zero - y)));
            context.Add(new HavenFillRoundedRectCommand(rect, new HavenTokenBrush("Accent"), 3, opacity * .9));
        }
    }

    private void DrawLines(HavenDrawingContext context, double left, double top, double right, double bottom, double opacity)
    {
        var datasets = new List<double[]>(_series);
        foreach (var expression in _expressions)
        {
            var samples = new double[121];
            for (var i = 0; i < samples.Length; i++)
            {
                var x = _xMin + (_xMax - _xMin) * i / (samples.Length - 1d);
                samples[i] = TryEvaluate(expression, x, out var y) ? y : double.NaN;
            }
            datasets.Add(samples);
        }
        if (datasets.Count == 0) return;

        var tokens = new[] { "Accent", "AccentSecondary", "TextPrimary", "TextSecondary" };
        for (var seriesIndex = 0; seriesIndex < datasets.Count; seriesIndex++)
        {
            var values = datasets[seriesIndex];
            if (values.Length < 2) continue;
            var pen = new HavenPen(new HavenTokenBrush(tokens[seriesIndex % tokens.Length]), 2.25);
            HavenPoint? previous = null;
            for (var i = 0; i < values.Length; i++)
            {
                var value = values[i];
                if (!double.IsFinite(value) || value < _yMin - (_yMax - _yMin) || value > _yMax + (_yMax - _yMin))
                {
                    previous = null;
                    continue;
                }
                var xValue = _xMin + (_xMax - _xMin) * i / Math.Max(1, values.Length - 1d);
                var point = new HavenPoint(MapX(xValue, left, right), MapY(Math.Clamp(value, _yMin, _yMax), top, bottom));
                if (previous is HavenPoint from)
                    context.Add(new HavenLineCommand(from, point, pen, opacity));
                previous = point;
            }
        }
    }

    private double MapX(double value, double left, double right) =>
        left + (value - _xMin) / (_xMax - _xMin) * (right - left);

    private double MapY(double value, double top, double bottom) =>
        bottom - (value - _yMin) / (_yMax - _yMin) * (bottom - top);

    private static IReadOnlyList<string> ReadStrings(GenUiComponent component, string key)
    {
        if (!component.Properties.TryGetValue(key, out var value)) return [];
        if (value.ValueKind == JsonValueKind.Array)
            return value.EnumerateArray().Select(item => item.GetString() ?? string.Empty).Where(item => item.Length > 0).ToArray();
        if (value.ValueKind == JsonValueKind.String)
            return value.GetString()!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return [];
    }

    private static IReadOnlyList<double[]> ReadSeries(GenUiComponent component)
    {
        foreach (var key in new[] { "series", "data", "values", "points" })
        {
            if (!component.Properties.TryGetValue(key, out var value)) continue;
            var parsed = ParseSeries(value);
            if (parsed.Count > 0) return parsed;
        }
        return [];
    }

    private static IReadOnlyList<double[]> ParseSeries(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array) return [];
        var items = value.EnumerateArray().ToArray();
        if (items.Length == 0) return [];
        if (items.All(item => item.ValueKind == JsonValueKind.Number))
            return [items.Select(item => item.GetDouble()).ToArray()];
        var result = new List<double[]>();
        foreach (var item in items)
        {
            if (item.ValueKind == JsonValueKind.Array)
            {
                var values = item.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.Number).Select(v => v.GetDouble()).ToArray();
                if (values.Length > 0) result.Add(values);
                continue;
            }
            if (item.ValueKind == JsonValueKind.Object)
            {
                foreach (var key in new[] { "values", "data", "points" })
                {
                    if (!item.TryGetProperty(key, out var nested) || nested.ValueKind != JsonValueKind.Array) continue;
                    var values = nested.EnumerateArray().Select(ReadPointValue).Where(double.IsFinite).ToArray();
                    if (values.Length > 0) result.Add(values);
                    break;
                }
            }
        }
        return result;
    }

    private static double ReadPointValue(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number) return value.GetDouble();
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "y", "value" })
                if (value.TryGetProperty(key, out var y) && y.ValueKind == JsonValueKind.Number) return y.GetDouble();
        }
        return double.NaN;
    }

    private static string ReadString(GenUiComponent component, string key) =>
        component.Properties.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty : string.Empty;

    private static double ReadDouble(GenUiComponent component, string key, double fallback) =>
        component.Properties.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : fallback;

    private static double ReadDouble(JsonElement element, string key, double fallback) =>
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : fallback;

    private static bool TryEvaluate(string expression, double x, out double value)
    {
        try
        {
            value = new ExpressionParser(expression, x).Parse();
            return double.IsFinite(value);
        }
        catch
        {
            value = double.NaN;
            return false;
        }
    }

    private sealed class ExpressionParser(string source, double x)
    {
        private int _index;

        public double Parse()
        {
            var value = AddSubtract();
            Skip();
            if (_index != source.Length) throw new FormatException();
            return value;
        }

        private double AddSubtract()
        {
            var value = MultiplyDivide();
            while (true)
            {
                Skip();
                if (Take('+')) value += MultiplyDivide();
                else if (Take('-')) value -= MultiplyDivide();
                else return value;
            }
        }

        private double MultiplyDivide()
        {
            var value = Power();
            while (true)
            {
                Skip();
                if (Take('*')) value *= Power();
                else if (Take('/')) value /= Power();
                else return value;
            }
        }

        private double Power()
        {
            var value = Unary();
            Skip();
            if (Take('^')) value = Math.Pow(value, Power());
            return value;
        }

        private double Unary()
        {
            Skip();
            if (Take('+')) return Unary();
            if (Take('-')) return -Unary();
            return Primary();
        }

        private double Primary()
        {
            Skip();
            if (Take('('))
            {
                var value = AddSubtract();
                if (!Take(')')) throw new FormatException();
                return value;
            }
            if (_index < source.Length && (char.IsDigit(source[_index]) || source[_index] == '.'))
            {
                var start = _index;
                while (_index < source.Length && (char.IsDigit(source[_index]) || source[_index] is '.' or 'e' or 'E' or '+' or '-'))
                {
                    if ((_index > start) && source[_index] is '+' or '-' && source[_index - 1] is not ('e' or 'E')) break;
                    _index++;
                }
                return double.Parse(source[start.._index], NumberStyles.Float, CultureInfo.InvariantCulture);
            }
            var nameStart = _index;
            while (_index < source.Length && char.IsLetter(source[_index])) _index++;
            var name = source[nameStart.._index].ToLowerInvariant();
            if (name == "x") return x;
            if (name == "pi") return Math.PI;
            if (name == "e") return Math.E;
            if (name.Length == 0) throw new FormatException();
            if (!Take('(')) throw new FormatException();
            var arg = AddSubtract();
            if (!Take(')')) throw new FormatException();
            return name switch
            {
                "sin" => Math.Sin(arg),
                "cos" => Math.Cos(arg),
                "tan" => Math.Tan(arg),
                "sqrt" => Math.Sqrt(arg),
                "abs" => Math.Abs(arg),
                "exp" => Math.Exp(arg),
                "ln" or "log" => Math.Log(arg),
                _ => throw new FormatException()
            };
        }

        private bool Take(char expected)
        {
            Skip();
            if (_index >= source.Length || source[_index] != expected) return false;
            _index++;
            return true;
        }

        private void Skip()
        {
            while (_index < source.Length && char.IsWhiteSpace(source[_index])) _index++;
        }
    }
}
