namespace Haven.UI.Components;

public sealed class Page : Container
{
    public Page()
    {
        Accessibility.Role = HavenAccessibleRole.Window;
        Layout = HavenLayout.Vertical;
        SetValue(HavenProperties.Width, HavenLength.Percent(100), HavenValueSource.Default);
        SetValue(HavenProperties.Height, HavenLength.Percent(100), HavenValueSource.Default);
    }

    public string PageAccent
    {
        get => GetValue(HavenProperties.Accent);
        set => SetValue(HavenProperties.Accent, value ?? "Accent");
    }

    public override HavenComponentMetadata Metadata => new(
        "Page",
        "Components/Page/Page.cs",
        ["Page"],
        [],
        "Screen root; page-owned accent selection remains semantic and is resolved by the platform host.");
}
