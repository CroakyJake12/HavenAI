using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.LessonSettings;

internal sealed class LessonSettingsHavenScene : IDisposable
{
    private readonly List<(HavenElement Element, EventHandler Handler)> _stateSubscriptions = [];

    public LessonSettingsHavenScene()
    {
        Root = new Page { Name = "LessonSettings.Root", Layout = HavenLayout.Vertical };
        Root.SetValue(HavenProperties.Padding, HavenThickness.Parse("28px 32px 32px 32px"));
        Root.SetValue(HavenProperties.Gap, HavenLength.Px(15));
        Root.SetValue(HavenProperties.MaxWidth, HavenLength.Px(880));
        Root.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Stretch);
        Root.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);

        Eyebrow = new HavenText { Name = "LessonSettings.Eyebrow", Level = TextLevel.Caption, Content = "LESSON SETTINGS" };
        Eyebrow.SetValue(HavenProperties.FontWeight, 700);
        Eyebrow.SetValue(HavenProperties.Foreground, "TextSecondary");
        Root.Add(Eyebrow);

        NameHeading = new HavenText { Name = "LessonSettings.NameHeading", Level = TextLevel.H2 };
        Root.Add(NameHeading);

        Description = new HavenText("Organise the lesson and preserve its structured Study outline.")
        {
            Name = "LessonSettings.Description",
            Level = TextLevel.Paragraph
        };
        Description.SetValue(HavenProperties.Foreground, "TextSecondary");
        Root.Add(Description);

        Basics = BuildBasicsCard();
        Root.Add(Basics);

        Structure = BuildStructureCard();
        Root.Add(Structure);

        Footer = BuildFooter();
        Root.Add(Footer);
    }

    public Page Root { get; }
    public HavenText Eyebrow { get; }
    public HavenText NameHeading { get; }
    public HavenText Description { get; }
    public Container Basics { get; }
    public Container Structure { get; }
    public Container Footer { get; }
    public Input NameInput { get; private set; } = null!;
    public Input TopicGroupInput { get; private set; } = null!;
    public Input StructureJsonInput { get; private set; } = null!;
    public HavenText StatusText { get; private set; } = null!;
    public Button SaveButton { get; private set; } = null!;

    public void LoadLesson(Lesson lesson)
    {
        NameHeading.Content = lesson.Name;
        NameInput.Text = lesson.Name;
        NameInput.Accessibility.AccessibleName = "Lesson name";
        NameInput.Placeholder = "Lesson name";
        TopicGroupInput.Text = lesson.TopicGroup;
        TopicGroupInput.Accessibility.AccessibleName = "Topic group";
        TopicGroupInput.Placeholder = "General";
        StructureJsonInput.Text = lesson.StructureJson;
        StructureJsonInput.Accessibility.AccessibleName = "Lesson structure JSON";
        StructureJsonInput.Placeholder = "{}";
    }

    public void SetStatus(string text)
    {
        StatusText.Content = text;
    }

    public void EnableSave(bool enabled)
    {
        SaveButton.SetValue(HavenProperties.Enabled, enabled);
    }

    private Container BuildBasicsCard()
    {
        var card = new Container { Name = "LessonSettings.Basics", Layout = HavenLayout.Vertical };
        card.SetValue(HavenProperties.Background, "SurfaceRaised");
        card.SetValue(HavenProperties.BorderColor, "Border");
        card.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(18)));
        card.SetValue(HavenProperties.Shadow, "Card");
        card.SetValue(HavenProperties.Padding, HavenThickness.Parse("18px"));
        card.SetValue(HavenProperties.Gap, HavenLength.Px(9));

        var nameLabel = new HavenText("Lesson name") { Name = "LessonSettings.NameLabel", Level = TextLevel.Caption };
        nameLabel.SetValue(HavenProperties.FontSize, 11d);
        nameLabel.SetValue(HavenProperties.FontWeight, 600);
        nameLabel.SetValue(HavenProperties.Foreground, "TextSecondary");
        card.Add(nameLabel);

        NameInput = new Input { Name = "LessonSettings.Name" };
        NameInput.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        card.Add(NameInput);

        var topicLabel = new HavenText("Topic group") { Name = "LessonSettings.TopicLabel", Level = TextLevel.Caption };
        topicLabel.SetValue(HavenProperties.FontSize, 11d);
        topicLabel.SetValue(HavenProperties.FontWeight, 600);
        topicLabel.SetValue(HavenProperties.Foreground, "TextSecondary");
        topicLabel.SetValue(HavenProperties.Margin, HavenThickness.Parse("5px 0px 0px 0px"));
        card.Add(topicLabel);

        TopicGroupInput = new Input { Name = "LessonSettings.TopicGroup", Placeholder = "General" };
        TopicGroupInput.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        card.Add(TopicGroupInput);

        return card;
    }

    private Container BuildStructureCard()
    {
        var card = new Container { Name = "LessonSettings.Structure", Layout = HavenLayout.Vertical };
        card.SetValue(HavenProperties.Background, "SurfaceRaised");
        card.SetValue(HavenProperties.BorderColor, "Border");
        card.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(18)));
        card.SetValue(HavenProperties.Shadow, "Card");
        card.SetValue(HavenProperties.Padding, HavenThickness.Parse("18px"));
        card.SetValue(HavenProperties.Gap, HavenLength.Px(9));

        var eyebrow = new HavenText("STRUCTURE") { Name = "LessonSettings.Structure.Eyebrow", Level = TextLevel.Caption };
        eyebrow.SetValue(HavenProperties.FontWeight, 700);
        eyebrow.SetValue(HavenProperties.Foreground, "TextSecondary");
        card.Add(eyebrow);

        var description = new HavenText("JSON outline used to preserve sections, objectives, and progress metadata.") { Name = "LessonSettings.Structure.Description", Level = TextLevel.Caption };
        description.SetValue(HavenProperties.FontSize, 11d);
        description.SetValue(HavenProperties.FontWeight, 500);
        description.SetValue(HavenProperties.Foreground, "TextSecondary");
        card.Add(description);

        StructureJsonInput = new Input { Name = "LessonSettings.StructureJson", Multiline = true };
        StructureJsonInput.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        StructureJsonInput.SetValue(HavenProperties.MinHeight, HavenLength.Px(190));
        StructureJsonInput.SetValue(HavenProperties.FontFamily, "Code");
        card.Add(StructureJsonInput);

        return card;
    }

    private Container BuildFooter()
    {
        var footer = new Container { Name = "LessonSettings.Footer", Layout = HavenLayout.Horizontal };
        footer.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Stretch);
        footer.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        footer.SetValue(HavenProperties.Gap, HavenLength.Px(12));
        footer.SetValue(HavenProperties.Margin, HavenThickness.Parse("4px 0px 0px 0px"));

        StatusText = new HavenText { Name = "LessonSettings.Status", Level = TextLevel.Caption };
        StatusText.SetValue(HavenProperties.Foreground, "TextSecondary");
        StatusText.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        StatusText.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Start);
        StatusText.SetValue(HavenProperties.MinHeight, HavenLength.Px(48));
        StatusText.SetValue(HavenProperties.Hover, false);
        footer.Add(StatusText);

        SaveButton = new Button { Name = "LessonSettings.Save", Content = "Save lesson", Variant = ButtonVariant.Primary };
        SaveButton.Accessibility.AccessibleName = "Save lesson";
        SaveButton.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.End);
        SaveButton.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        SaveButton.Invoked += OnSaveInvoked;
        footer.Add(SaveButton);

        return footer;
    }

    private void OnSaveInvoked(object? sender, EventArgs e) => SaveRequested?.Invoke(this, EventArgs.Empty);

    public event EventHandler? SaveRequested;

    public void Dispose()
    {
        foreach (var (element, handler) in _stateSubscriptions) element.Invalidated -= handler;
        _stateSubscriptions.Clear();
        if (SaveButton is not null) SaveButton.Invoked -= OnSaveInvoked;
    }
}
