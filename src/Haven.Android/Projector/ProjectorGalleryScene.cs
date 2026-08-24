using Haven.Application;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Android;

internal sealed class ProjectorGalleryScene
{
    private readonly Container _wideGrid;
    private readonly Container _compactList;
    private readonly HavenText _selectionTitle;
    private readonly HavenText _selectionDescription;

    public ProjectorGalleryScene(string displayName)
    {
        Root = new Page
        {
            Name = "Projector.Gallery.Root",
            Layout = HavenLayout.Grid,
            Columns = "1fr",
            Rows = "Auto 1fr Auto"
        };
        Root.SetValue(HavenProperties.Padding, HavenThickness.Parse("32px 42px"));
        Root.SetValue(HavenProperties.Background, "Surface");
        Root.SetValue(HavenProperties.Overflow, HavenOverflow.Clip);

        var header = new Container
        {
            Name = "Projector.Gallery.Header",
            Layout = HavenLayout.Vertical
        };
        header.SetValue(HavenProperties.Row, 0);
        header.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        header.SetValue(HavenProperties.Margin, HavenThickness.Parse("0px 0px 26px 0px"));

        var eyebrow = new HavenText("PROJECTOR")
        {
            Name = "Projector.Gallery.Eyebrow",
            Level = TextLevel.Caption
        };
        eyebrow.SetValue(HavenProperties.Foreground, "AccentSecondary");
        header.Add(eyebrow);

        var title = new HavenText("What should this screen become?")
        {
            Name = "Projector.Gallery.Title",
            Level = TextLevel.H1
        };
        title.SetValue(HavenProperties.FontSize, 42d);
        header.Add(title);

        var subtitle = new HavenText($"Choose an experience for {displayName}.")
        {
            Name = "Projector.Gallery.Subtitle",
            Level = TextLevel.Paragraph
        };
        subtitle.SetValue(HavenProperties.Foreground, "TextSecondary");
        header.Add(subtitle);

        var route = new Container
        {
            Name = "Projector.Gallery.Route",
            Layout = HavenLayout.Grid,
            Columns = "1fr Auto",
            Rows = "Auto"
        };
        route.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        route.SetValue(HavenProperties.Margin, HavenThickness.Parse("10px 0px 0px 0px"));

        RouteInput = new Input
        {
            Name = "Projector.Gallery.Route.Input",
            Placeholder = "What should this screen become?",
            Multiline = false,
            SubmitOnEnter = true
        };
        RouteInput.SetValue(HavenProperties.Column, 0);
        route.Add(RouteInput);

        var routeButton = new HavenButton
        {
            Name = "Projector.Gallery.Route.Go",
            Content = "Go",
            IconKey = "arrow-up",
            Variant = ButtonVariant.Primary
        };
        routeButton.SetValue(HavenProperties.Column, 1);
        routeButton.Invoked += (_, _) => SubmitRoute();
        route.Add(routeButton);
        header.Add(route);
        Root.Add(header);

        _wideGrid = new Container
        {
            Name = "Projector.Gallery.Experiences.Wide",
            Layout = HavenLayout.Grid,
            Columns = "1fr 1fr 1fr",
            Rows = "170px"
        };
        _wideGrid.SetValue(HavenProperties.Row, 1);
        _wideGrid.SetValue(HavenProperties.Gap, HavenLength.Px(18));
        _wideGrid.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        _wideGrid.Conditions.Add(new HavenScreenRangeCondition(HavenScreenAxis.Width, HavenLength.Px(960)));
        Root.Add(_wideGrid);

        _compactList = new Container
        {
            Name = "Projector.Gallery.Experiences.Compact",
            Layout = HavenLayout.Vertical
        };
        _compactList.SetValue(HavenProperties.Row, 1);
        _compactList.SetValue(HavenProperties.Gap, HavenLength.Px(12));
        _compactList.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        _compactList.Conditions.Add(new HavenScreenRangeCondition(
            HavenScreenAxis.Width,
            maximum: HavenLength.Px(959.999)));
        Root.Add(_compactList);

        var selection = new Container
        {
            Name = "Projector.Gallery.Selection",
            Layout = HavenLayout.Vertical
        };
        selection.SetValue(HavenProperties.Row, 2);
        selection.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        selection.SetValue(HavenProperties.Padding, HavenThickness.Parse("14px 18px"));
        selection.SetValue(HavenProperties.Margin, HavenThickness.Parse("24px 0px 0px 0px"));
        selection.SetValue(HavenProperties.Background, "SurfaceRaised");
        selection.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(18)));

        _selectionTitle = new HavenText("Choose an experience")
        {
            Name = "Projector.Gallery.Selection.Title",
            Level = TextLevel.H4
        };
        selection.Add(_selectionTitle);

        _selectionDescription = new HavenText("Select a tile to preview what it will turn this screen into.")
        {
            Name = "Projector.Gallery.Selection.Description",
            Level = TextLevel.Caption
        };
        _selectionDescription.SetValue(HavenProperties.Foreground, "TextSecondary");
        selection.Add(_selectionDescription);
        Root.Add(selection);
    }

    public Page Root { get; }
    public Input RouteInput { get; }
    public event Action<ProjectorExperience>? ExperienceInvoked;
    public event Action<string>? RouteRequested;

    public void SubmitRoute()
    {
        var request = RouteInput.Text.Trim();
        if (request.Length == 0)
        {
            SetRouteStatus("Describe this screen", "Type an experience or app, then press Enter or Go.");
            return;
        }

        SetRouteStatus("Planning route", request);
        RouteRequested?.Invoke(request);
    }

    public void SetRouteStatus(string title, string description)
    {
        _selectionTitle.Content = title;
        _selectionDescription.Content = description;
    }

    public void SetExperiences(IReadOnlyList<ProjectorExperience> experiences)
    {
        ArgumentNullException.ThrowIfNull(experiences);
        Clear(_wideGrid);
        Clear(_compactList);

        if (experiences.Count == 0)
        {
            AddEmptyState(_wideGrid);
            AddEmptyState(_compactList);
            return;
        }

        foreach (var experience in experiences)
        {
            _wideGrid.Add(CreateTile(experience, compact: false));
            _compactList.Add(CreateTile(experience, compact: true));
        }
    }

    private HavenButton CreateTile(ProjectorExperience experience, bool compact)
    {
        var tile = new HavenButton
        {
            Name = $"Projector.Gallery.{(compact ? "Compact" : "Wide")}.{SafeName(experience.Id)}",
            Content = experience.Name,
            IconKey = experience.IconKey,
            Variant = ButtonVariant.Navigation
        };
        tile.SetValue(HavenProperties.MinHeight, HavenLength.Px(compact ? 64 : 150));
        tile.SetValue(HavenProperties.Padding, HavenThickness.Parse(compact ? "10px 16px" : "18px 20px"));
        tile.Invoked += (_, _) =>
        {
            SetExperienceStatus(experience, experience.Description);
            ExperienceInvoked?.Invoke(experience);
        };
        return tile;
    }

    public void SetExperienceStatus(ProjectorExperience experience, string description)
    {
        ArgumentNullException.ThrowIfNull(experience);
        _selectionTitle.Content = experience.Name;
        _selectionDescription.Content = description;
    }

    private static void AddEmptyState(Container target)
    {
        var empty = new HavenText("No Projector experiences are available for this display yet.")
        {
            Level = TextLevel.Paragraph
        };
        empty.SetValue(HavenProperties.Foreground, "TextSecondary");
        target.Add(empty);
    }

    private static void Clear(Container target)
    {
        foreach (var child in target.Children.ToArray())
            target.Remove(child);
    }

    private static string SafeName(string value)
    {
        var safe = new string(value.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "Experience" : safe;
    }
}
