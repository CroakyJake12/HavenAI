using Haven.Application;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Android;

internal sealed class ProjectorDesktopScene
{
    private readonly Container _applications;
    private readonly HavenText _status;

    public ProjectorDesktopScene(ProjectorDisplay display)
    {
        ArgumentNullException.ThrowIfNull(display);

        Root = new Page
        {
            Name = "Projector.Desktop.Root",
            Layout = HavenLayout.Grid,
            Columns = "1fr",
            Rows = "Auto 1fr Auto"
        };
        Root.SetValue(HavenProperties.Padding, HavenThickness.Parse("28px 36px"));
        Root.SetValue(HavenProperties.Background, "Surface");
        Root.SetValue(HavenProperties.Overflow, HavenOverflow.Clip);

        var header = new Container
        {
            Name = "Projector.Desktop.Header",
            Layout = HavenLayout.Grid,
            Columns = "1fr Auto",
            Rows = "Auto"
        };
        header.SetValue(HavenProperties.Row, 0);
        header.SetValue(HavenProperties.Gap, HavenLength.Px(18));
        header.SetValue(HavenProperties.Margin, HavenThickness.Parse("0px 0px 22px 0px"));

        var heading = new Container { Name = "Projector.Desktop.Heading", Layout = HavenLayout.Vertical };
        heading.SetValue(HavenProperties.Column, 0);
        heading.SetValue(HavenProperties.Gap, HavenLength.Px(5));
        var eyebrow = new HavenText("PROJECTOR DESKTOP") { Name = "Projector.Desktop.Eyebrow", Level = TextLevel.Caption };
        eyebrow.SetValue(HavenProperties.Foreground, "AccentSecondary");
        heading.Add(eyebrow);
        var title = new HavenText("Desktop") { Name = "Projector.Desktop.Title", Level = TextLevel.H1 };
        title.SetValue(HavenProperties.FontSize, 38d);
        heading.Add(title);
        var subtitle = new HavenText(DisplaySummary(display)) { Name = "Projector.Desktop.Display", Level = TextLevel.Paragraph };
        subtitle.SetValue(HavenProperties.Foreground, "TextSecondary");
        heading.Add(subtitle);
        header.Add(heading);

        var gallery = new HavenButton
        {
            Name = "Projector.Desktop.Gallery",
            Content = "Gallery",
            IconKey = "chevron-left",
            Variant = ButtonVariant.Secondary
        };
        gallery.SetValue(HavenProperties.Column, 1);
        gallery.Invoked += (_, _) => GalleryRequested?.Invoke();
        header.Add(gallery);
        Root.Add(header);

        var body = new Container { Name = "Projector.Desktop.Body", Layout = HavenLayout.Vertical };
        body.SetValue(HavenProperties.Row, 1);
        body.SetValue(HavenProperties.Gap, HavenLength.Px(18));
        body.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        var overview = new Container { Name = "Projector.Desktop.Overview", Layout = HavenLayout.Vertical };
        overview.SetValue(HavenProperties.Padding, HavenThickness.Parse("18px 20px"));
        overview.SetValue(HavenProperties.Background, "SurfaceRaised");
        overview.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(20)));
        overview.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        overview.Add(new HavenText("Apps on this device") { Level = TextLevel.H3 });
        var overviewText = new HavenText("Launch Android apps that the device and this display explicitly allow on a secondary screen.") { Level = TextLevel.Paragraph };
        overviewText.SetValue(HavenProperties.Foreground, "TextSecondary");
        overview.Add(overviewText);
        body.Add(overview);

        _applications = new Container { Name = "Projector.Desktop.Applications", Layout = HavenLayout.Wrap };
        _applications.SetValue(HavenProperties.Gap, HavenLength.Px(12));
        body.Add(_applications);
        Root.Add(body);

        var footer = new Container { Name = "Projector.Desktop.Footer", Layout = HavenLayout.Vertical };
        footer.SetValue(HavenProperties.Row, 2);
        footer.SetValue(HavenProperties.Margin, HavenThickness.Parse("20px 0px 0px 0px"));
        footer.SetValue(HavenProperties.Padding, HavenThickness.Parse("12px 16px"));
        footer.SetValue(HavenProperties.Background, "SurfaceRaised");
        footer.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(16)));
        _status = new HavenText($"{display.Trust} display · phone controls and private context stay on the phone.")
        {
            Name = "Projector.Desktop.Status",
            Level = TextLevel.Caption
        };
        _status.SetValue(HavenProperties.Foreground, "TextSecondary");
        footer.Add(_status);
        Root.Add(footer);
    }

    public Page Root { get; }
    public event Action? GalleryRequested;
    public event Action<ProjectorExperience>? ApplicationInvoked;

    public void SetApplications(IReadOnlyList<ProjectorExperience> applications)
    {
        ArgumentNullException.ThrowIfNull(applications);
        Clear(_applications);
        var launchable = applications
            .Where(experience => experience.Source == ProjectorExperienceSource.Application
                && experience.LaunchStrategy == ProjectorLaunchStrategy.AndroidApplication)
            .OrderBy(experience => experience.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(experience => experience.Id, StringComparer.Ordinal)
            .ToArray();

        if (launchable.Length == 0)
        {
            var empty = new HavenText("No installed Android apps are currently eligible for this Projector display.") { Level = TextLevel.Paragraph };
            empty.SetValue(HavenProperties.Foreground, "TextSecondary");
            _applications.Add(empty);
            return;
        }

        foreach (var application in launchable)
        {
            var captured = application;
            var tile = new HavenButton
            {
                Name = "Projector.Desktop.App." + SafeName(captured.Id),
                Content = captured.Name,
                IconKey = string.IsNullOrWhiteSpace(captured.IconKey) ? "apps" : captured.IconKey,
                Variant = ButtonVariant.Navigation
            };
            tile.SetValue(HavenProperties.MinWidth, HavenLength.Px(220));
            tile.SetValue(HavenProperties.MinHeight, HavenLength.Px(78));
            tile.SetValue(HavenProperties.Padding, HavenThickness.Parse("14px 18px"));
            tile.Invoked += (_, _) =>
            {
                SetStatus("Opening " + captured.Name + "…");
                ApplicationInvoked?.Invoke(captured);
            };
            _applications.Add(tile);
        }
    }

    public void SetStatus(string message)
        => _status.Content = string.IsNullOrWhiteSpace(message) ? "Projector Desktop" : message.Trim();

    private static string DisplaySummary(ProjectorDisplay display)
    {
        var resolution = display.WidthPixels is > 0 && display.HeightPixels is > 0
            ? $"{display.WidthPixels}×{display.HeightPixels}"
            : "External display";
        return $"{display.Name} · {resolution} · {display.Trust} · {display.Connection}";
    }

    private static void Clear(Container target)
    {
        foreach (var child in target.Children.ToArray())
            target.Remove(child);
    }

    private static string SafeName(string value)
    {
        var safe = new string(value.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "Application" : safe;
    }
}
