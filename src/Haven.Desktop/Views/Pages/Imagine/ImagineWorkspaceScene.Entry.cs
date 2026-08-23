using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenContainer = Haven.UI.Components.Container;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Imagine;

internal sealed partial class ImagineWorkspaceScene
{
    public HavenContainer Home { get; private set; } = null!;
    public Input GenerationInput { get; private set; } = null!;
    public HavenButton GenerateButton { get; private set; } = null!;
    public HavenButton ReferenceButton { get; private set; } = null!;
    public HavenButton ConfigureProviderButton { get; private set; } = null!;
    public HavenButton HomeCancelButton { get; private set; } = null!;
    public HavenButton EditorCancelButton { get; private set; } = null!;
    public HavenText ReferenceLabel { get; private set; } = null!;
    public HavenText HomeStatus { get; private set; } = null!;
    public DynamicUIRuntime HomeRecent { get; private set; } = null!;
    public string GenerationSize { get; private set; } = "1024x1024";
    public string GenerationQuality { get; private set; } = "medium";

    public event Action<string>? GenerateRequested;
    public event EventHandler? ReferenceRequested;
    public event EventHandler? NewBlankRequested;
    public event EventHandler? HomeRequested;
    public event EventHandler? CancelRequested;
    public event EventHandler? ProviderSettingsRequested;
    public event Action<ImagineProject>? HomeProjectRequested;

    private void BuildEntrySurface()
    {
        Home = Vertical("Imagine.Home", 14);
        Home.SetValue(HavenProperties.MinHeight, HavenLength.Px(520));
        Home.Add(new HavenText { Content = "Imagine", Level = TextLevel.H1 });
        Home.Add(Muted("Imagine.Home.Subtitle", "Create an image, start from a reference, or reopen an Imagine project. Generated images become normal editable Imagine assets."));

        GenerationInput = new Input { Name = "Imagine.GenerationPrompt", Placeholder = "Describe the image you want to create…", Multiline = true, SubmitOnEnter = false };
        GenerationInput.SetValue(HavenProperties.MinHeight, HavenLength.Px(110));
        Home.Add(GenerationInput);

        var actions = Wrap("Imagine.Home.Actions", 8);
        GenerateButton = new HavenButton { Name = "Imagine.Generate", Content = "Generate", IconKey = "sparkles", Variant = ButtonVariant.Primary };
        GenerateButton.Invoked += (_, _) => GenerateRequested?.Invoke(GenerationInput.Text);
        ReferenceButton = new HavenButton { Name = "Imagine.Reference", Content = "Add reference image", IconKey = "image", Variant = ButtonVariant.Ghost };
        ReferenceButton.Invoked += (_, _) => ReferenceRequested?.Invoke(this, EventArgs.Empty);
        var blank = new HavenButton { Name = "Imagine.Blank", Content = "Blank canvas", IconKey = "plus", Variant = ButtonVariant.Ghost };
        blank.Invoked += (_, _) => NewBlankRequested?.Invoke(this, EventArgs.Empty);
        HomeCancelButton = new HavenButton { Name = "Imagine.Home.Cancel", Content = "Cancel", IconKey = "close", Variant = ButtonVariant.Ghost };
        HomeCancelButton.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        HomeCancelButton.Invoked += (_, _) => CancelRequested?.Invoke(this, EventArgs.Empty);
        actions.Add(GenerateButton); actions.Add(ReferenceButton); actions.Add(blank); actions.Add(HomeCancelButton);
        Home.Add(actions);

        ReferenceLabel = Muted("Imagine.ReferenceLabel", "No reference image selected.");
        Home.Add(ReferenceLabel);

        var options = Wrap("Imagine.Home.Options", 6);
        options.Add(Muted("Imagine.Home.SizeLabel", "Size"));
        AddOption(options, "Square", "1024x1024", true, value => GenerationSize = value);
        AddOption(options, "Portrait", "1024x1536", false, value => GenerationSize = value);
        AddOption(options, "Landscape", "1536x1024", false, value => GenerationSize = value);
        options.Add(Muted("Imagine.Home.QualityLabel", "Quality"));
        AddOption(options, "Low", "low", false, value => GenerationQuality = value);
        AddOption(options, "Medium", "medium", true, value => GenerationQuality = value);
        AddOption(options, "High", "high", false, value => GenerationQuality = value);
        Home.Add(options);

        ConfigureProviderButton = new HavenButton { Name = "Imagine.ConfigureProvider", Content = "Open provider connections", IconKey = "settings", Variant = ButtonVariant.Tertiary };
        ConfigureProviderButton.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        ConfigureProviderButton.Invoked += (_, _) => ProviderSettingsRequested?.Invoke(this, EventArgs.Empty);
        Home.Add(ConfigureProviderButton);
        HomeStatus = Muted("Imagine.Home.Status", "Ready to create."); Home.Add(HomeStatus);
        Home.Add(Heading("Recent Imagine projects"));
        HomeRecent = Runtime("Imagine.Home.Recent"); Home.Add(HomeRecent);
        Root.Add(Home);

        EditorCancelButton = new HavenButton { Name = "Imagine.Editor.Cancel", Content = "Cancel operation", IconKey = "close", Variant = ButtonVariant.Ghost };
        EditorCancelButton.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        if (Root.Children[0] is HavenContainer globalToolbar) globalToolbar.Add(EditorCancelButton);
    }

    private void AddOption(HavenContainer parent, string label, string value, bool selected, Action<string> set)
    {
        var button = new HavenButton { Content = label, Variant = selected ? ButtonVariant.Tertiary : ButtonVariant.Ghost };
        button.Invoked += (_, _) =>
        {
            foreach (var sibling in parent.Children.OfType<HavenButton>()) sibling.Variant = ButtonVariant.Ghost;
            button.Variant = ButtonVariant.Tertiary;
            set(value);
        };
        parent.Add(button);
    }

    public void ShowHome(IReadOnlyList<ImagineProject> recent)
    {
        foreach (var child in Root.Children) child.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        Home.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
        HomeRecent.ClearItems();
        foreach (var project in recent.Take(20))
        {
            var row = _dynamic.CreateItem("ImagineProjectRow", HomeRecent.Name!, "home-project-" + project.Id.ToString("N"), new Dictionary<string, object?>
            {
                ["TITLE"] = project.Name, ["DETAIL"] = project.UpdatedAt.LocalDateTime.ToString("d MMM HH:mm")
            });
            row.GetComponent<HavenButton>("Open").Invoked += (_, _) => HomeProjectRequested?.Invoke(project);
        }
    }

    public void ShowEditor()
    {
        foreach (var child in Root.Children) child.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
        Home.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        SetMode(Mode);
        SetViewportWidth(_viewportWidth);
    }

    public void SetReference(string path) => ReferenceLabel.Content = "Reference: " + Path.GetFileName(path);

    public void SetConnectionRequired(bool required) => ConfigureProviderButton.SetValue(HavenProperties.Visibility, required ? HavenVisibility.Visible : HavenVisibility.Collapsed);

    public void SetBusy(bool busy)
    {
        GenerateButton.SetState(HavenElementState.Disabled, busy);
        ReferenceButton.SetState(HavenElementState.Disabled, busy);
        HomeCancelButton.SetValue(HavenProperties.Visibility, busy ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        EditorCancelButton.SetValue(HavenProperties.Visibility, busy ? HavenVisibility.Visible : HavenVisibility.Collapsed);
    }
}
