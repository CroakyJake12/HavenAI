using System.Globalization;

namespace Haven.UI;

/// <summary>Haven-owned named keyframe executor. Platform hosts only schedule frames.</summary>
public sealed class HavenAnimationEngine
{
    private readonly List<ActiveAnimation> _active = [];
    public bool HasActiveAnimations => _active.Count > 0;

    public void Start(HavenElement element, HavenAnimationDefinition definition, DateTimeOffset startedAt)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(definition);
        Stop(element);
        _active.Add(new ActiveAnimation(element, definition, startedAt));
    }

    public bool Tick(DateTimeOffset now)
    {
        for (var index = _active.Count - 1; index >= 0; index--)
        {
            var active = _active[index];
            var duration = Math.Max(1d, active.Definition.Duration.TotalMilliseconds);
            var raw = Math.Clamp((now - active.StartedAt).TotalMilliseconds / duration, 0d, 1d);
            Apply(active, Ease(raw, active.Definition.Easing));
            if (raw < 1d) continue;
            ClearAnimationValues(active);
            _active.RemoveAt(index);
        }
        return _active.Count > 0;
    }

    public void Stop(HavenElement element)
    {
        for (var index = _active.Count - 1; index >= 0; index--)
        {
            if (!ReferenceEquals(_active[index].Element, element)) continue;
            ClearAnimationValues(_active[index]);
            _active.RemoveAt(index);
        }
    }

    private static void Apply(ActiveAnimation active, double progress)
    {
        var frames = active.Definition.Keyframes;
        if (frames.Count == 0) return;
        var percentage = progress * 100d;
        var before = frames.LastOrDefault(frame => frame.Percent <= percentage) ?? frames[0];
        var after = frames.FirstOrDefault(frame => frame.Percent >= percentage) ?? frames[^1];
        var span = Math.Max(.0001d, after.Percent - before.Percent);
        var local = before.Percent == after.Percent ? 1d : Math.Clamp((percentage - before.Percent) / span, 0d, 1d);
        foreach (var name in before.Properties.Keys.Concat(after.Properties.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var from = before.Properties.GetValueOrDefault(name) ?? after.Properties[name];
            var to = after.Properties.GetValueOrDefault(name) ?? before.Properties[name];
            HavenPropertyCodec.Set(active.Element, name, Interpolate(from, to, local), HavenValueSource.Animation);
            active.Properties.Add(name);
        }
    }

    private static string Interpolate(string from, string to, double amount)
    {
        if (double.TryParse(from, NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
            && double.TryParse(to, NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
            return (a + (b - a) * amount).ToString("0.####", CultureInfo.InvariantCulture);
        try
        {
            var fromLength = HavenLength.Parse(from);
            var toLength = HavenLength.Parse(to);
            if (fromLength.Unit == toLength.Unit)
                return new HavenLength(fromLength.Value + (toLength.Value - fromLength.Value) * amount, fromLength.Unit).ToString();
        }
        catch (FormatException) { }
        return amount >= 1d ? to : from;
    }

    private static void ClearAnimationValues(ActiveAnimation active)
    {
        foreach (var name in active.Properties)
        {
            HavenProperty? property = name.Trim().ToLowerInvariant() switch
            {
                "opacity" => HavenProperties.Opacity,
                "scale" => HavenProperties.Scale,
                "rotation" => HavenProperties.Rotation,
                "translationx" => HavenProperties.TranslationX,
                "translationy" => HavenProperties.TranslationY,
                _ => null
            };
            if (property is not null) active.Element.ClearValue(property, HavenValueSource.Animation);
        }
    }

    private static double Ease(double t, string easing) => easing.Trim().ToLowerInvariant() switch
    {
        "easein" => t * t,
        "easeout" => 1d - Math.Pow(1d - t, 2d),
        "easeinout" => t < .5d ? 2d * t * t : 1d - Math.Pow(-2d * t + 2d, 2d) / 2d,
        "spring" => Math.Clamp(1d - Math.Exp(-7d * t) * Math.Cos(11d * t), 0d, 1.08d),
        _ => t
    };

    private sealed record ActiveAnimation(HavenElement Element, HavenAnimationDefinition Definition, DateTimeOffset StartedAt)
    {
        public HashSet<string> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
