namespace Haven.UI.Components;

public enum HavenImageFit { Contain, Cover, Fill, None }

public sealed class Image : HavenElement
{
    public string Source { get; set; } = string.Empty;
    public HavenImageFit Fit { get; set; } = HavenImageFit.Contain;
    public Image() { Accessibility.Role = HavenAccessibleRole.Image; SetValue(HavenProperties.Hover, false, HavenValueSource.Default); }
    public override HavenComponentMetadata Metadata => new("Image", "Components/Media/Media.cs", ["Image"], [], "Bitmap/vector source is decoded by the rendering backend; semantic layout remains Haven-owned.");
}

public sealed class Icon : HavenElement
{
    public string Key { get; set; } = string.Empty;
    public Icon() { Accessibility.Role = HavenAccessibleRole.Image; SetValue(HavenProperties.Hover, false, HavenValueSource.Default); }
    public override HavenComponentMetadata Metadata => new("Icon", "Components/Media/Media.cs", ["Icon"], [], "Semantic icon key is resolved by the backend icon provider.");
}

public sealed class Video : HavenElement
{
    public string Source { get; set; } = string.Empty;
    public bool AutoPlay { get; set; }
    public override HavenComponentMetadata Metadata => new("Video", "Components/Media/Media.cs", ["Video"], [], "Media playback is a native/backend adapter behind this Haven primitive.");
}

public sealed class Web : HavenElement
{
    public string Url { get; set; } = string.Empty;
    public override HavenComponentMetadata Metadata => new("Web", "Components/Media/Media.cs", ["Web"], [], "WebView remains a platform capability behind this Haven primitive.");
}
