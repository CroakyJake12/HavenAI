namespace Haven.UI.Components;

public sealed class Progress : HavenElement
{
    private double _minimum;
    private double _maximum = 100;
    private double _value;

    public Progress()
    {
        SetValue(HavenProperties.MinHeight, HavenLength.Px(18), HavenValueSource.Default);
        SetValue(HavenProperties.Background, "SurfaceRaised", HavenValueSource.Default);
        SetValue(HavenProperties.Foreground, "Accent", HavenValueSource.Default);
        SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(9)), HavenValueSource.Default);
    }

    public double Minimum { get => _minimum; set { _minimum = value; Maximum = _maximum; Value = _value; } }
    public double Maximum { get => _maximum; set { _maximum = Math.Max(value, _minimum); Value = _value; } }
    public double Value { get => _value; set => _value = Math.Clamp(value, _minimum, _maximum); }
    public double NormalizedValue => _maximum <= _minimum ? 0 : (_value - _minimum) / (_maximum - _minimum);

    public override HavenComponentMetadata Metadata => new("Progress", "Components/Progress/Progress.cs", ["Progress"], [], "Non-interactive progress shares Haven slider geometry without exposing pointer state.");
}
