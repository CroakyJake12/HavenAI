namespace Haven.UI.Components;

public enum SeparatorOrientation { Horizontal, Vertical }

public sealed class Separator : HavenElement
{
    public Separator()
    {
        SetValue(HavenProperties.Hover, false, HavenValueSource.Default);
        SetValue(HavenProperties.Background, "Border", HavenValueSource.Default);
        SetValue(HavenProperties.Height, HavenLength.Px(1), HavenValueSource.Default);
    }

    public SeparatorOrientation Orientation { get; set; }
    public override HavenComponentMetadata Metadata => new("Separator", "Components/Separator/Separator.cs", ["Separator"], [], "One non-interactive divider primitive.");
}
