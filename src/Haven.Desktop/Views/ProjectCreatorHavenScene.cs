using Haven.Desktop.ViewModels;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views;

internal sealed record ProjectCreatorSceneState(
    string ProjectName,
    string Destination,
    string Prompt,
    string PackageDescription,
    string SelectedTemplate,
    bool IsDotNetProject,
    bool IsNuGetPackage,
    string Status,
    bool IsBusy,
    bool CanReview,
    bool CanApprove,
    ProjectCreationProposal? Proposal);

internal sealed class ProjectCreatorHavenScene
{
    private readonly Dictionary<string, HavenButton> _templateButtons = new(StringComparer.Ordinal);
    private bool _syncing;
    private string? _proposalFingerprint;

    public ProjectCreatorHavenScene()
    {
        Root = new Page { Name = "ProjectCreator.Root", Layout = HavenLayout.Grid, Columns = "1fr", Rows = "Auto 1fr Auto" };
        Root.Accessibility.AccessibleName = "Create or connect Project";
        Root.SetValue(HavenProperties.Padding, HavenThickness.Parse("22px 28px 18px 28px"));
        Root.SetValue(HavenProperties.Gap, HavenLength.Px(16));

        var header = Vertical("ProjectCreator.Header", 4);
        header.SetValue(HavenProperties.Row, 0);
        header.Add(Label("ProjectCreator.Title", "Create Project", TextLevel.H1));
        header.Add(Muted("ProjectCreator.Subtitle", "Start something new, or connect work that already exists."));
        Root.Add(header);

        Body = Grid("ProjectCreator.Body", "1fr 1fr", "Auto");
        Body.SetValue(HavenProperties.Row, 1);
        Body.SetValue(HavenProperties.Gap, HavenLength.Px(16));
        Body.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        Root.Add(Body);

        CreatePanel = Card("ProjectCreator.Create");
        CreatePanel.SetValue(HavenProperties.Column, 0);
        Body.Add(CreatePanel);
        CreatePanel.Add(Label("ProjectCreator.Create.Title", "Create new", TextLevel.H3));
        CreatePanel.Add(Muted("ProjectCreator.Create.Help", "Choose the essentials first. Haven shows exactly what it will create before anything runs."));
        CreatePanel.Add(Caption("Project name"));
        ProjectNameInput = InputField("ProjectCreator.Name", "Project name");
        CreatePanel.Add(ProjectNameInput);

        CreatePanel.Add(Caption("Destination"));
        var destinationRow = Grid("ProjectCreator.Destination.Row", "1fr Auto", "Auto");
        DestinationInput = InputField("ProjectCreator.Destination", "Destination folder");
        destinationRow.Add(DestinationInput);
        ChooseDestinationButton = Ghost("ProjectCreator.ChooseDestination", "Choose folder", "folder");
        ChooseDestinationButton.SetValue(HavenProperties.Column, 1);
        destinationRow.Add(ChooseDestinationButton);
        CreatePanel.Add(destinationRow);

        CreatePanel.Add(Caption("Project type"));
        var kindRow = Wrap("ProjectCreator.Kind", 8);
        DotNetButton = new HavenButton { Name = "ProjectCreator.DotNet", Content = ".NET project", Variant = ButtonVariant.Primary };
        PackageButton = new HavenButton { Name = "ProjectCreator.Package", Content = "NuGet package", Variant = ButtonVariant.Ghost };
        kindRow.Add(DotNetButton);
        kindRow.Add(PackageButton);
        CreatePanel.Add(kindRow);

        TemplateSection = Vertical("ProjectCreator.TemplateSection", 8);
        TemplateSection.Add(Caption("Template"));
        Templates = Wrap("ProjectCreator.Templates", 8);
        foreach (var template in new[] { "Console app", "Class library", "Web API", "Worker service" })
        {
            var captured = template;
            var button = Ghost("ProjectCreator.Template." + SafeName(template), DisplayTemplateName(template), string.Empty);
            button.Invoked += (_, _) => { if (!_syncing) TemplateRequested?.Invoke(captured); };
            _templateButtons.Add(template, button);
            Templates.Add(button);
        }
        TemplateSection.Add(Templates);
        CreatePanel.Add(TemplateSection);

        CreatePanel.Add(Caption("What are you building?"));
        PromptInput = InputField("ProjectCreator.Prompt", "Optional description for Haven to infer the right project type");
        PromptInput.Multiline = true;
        PromptInput.SubmitOnEnter = false;
        PromptInput.SetValue(HavenProperties.MinHeight, HavenLength.Px(92));
        CreatePanel.Add(PromptInput);

        PackageSection = Vertical("ProjectCreator.PackageSection", 8);
        PackageSection.Add(Caption("Package description"));
        PackageDescriptionInput = InputField("ProjectCreator.PackageDescription", "Short package description");
        PackageDescriptionInput.Multiline = true;
        PackageDescriptionInput.SubmitOnEnter = false;
        PackageDescriptionInput.SetValue(HavenProperties.MinHeight, HavenLength.Px(72));
        PackageSection.Add(PackageDescriptionInput);
        CreatePanel.Add(PackageSection);

        ReviewButton = new HavenButton { Name = "ProjectCreator.Review", Content = "Review what Haven will create", Variant = ButtonVariant.Primary };
        CreatePanel.Add(ReviewButton);

        ProposalCard = Card("ProjectCreator.Proposal");
        ProposalCard.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        ProposalCard.Add(Label("ProjectCreator.Proposal.Title", "Review proposal", TextLevel.H3));
        ProposalSummary = Label("ProjectCreator.Proposal.Summary", string.Empty);
        ProposalCard.Add(ProposalSummary);
        ProposalTarget = Muted("ProjectCreator.Proposal.Target", string.Empty);
        ProposalCard.Add(ProposalTarget);
        ProposalCard.Add(Muted("ProjectCreator.Proposal.Warning", "Nothing runs until you approve this reviewed proposal. Editing any input invalidates it."));
        ProposalCard.Add(Caption("Files"));
        ProposalFiles = Vertical("ProjectCreator.Proposal.Files", 6);
        ProposalCard.Add(ProposalFiles);
        ProposalCard.Add(Caption("Commands"));
        ProposalCommands = Vertical("ProjectCreator.Proposal.Commands", 6);
        ProposalCard.Add(ProposalCommands);
        ApproveButton = new HavenButton { Name = "ProjectCreator.Approve", Content = "Approve and create", Variant = ButtonVariant.Primary };
        ProposalCard.Add(ApproveButton);
        CreatePanel.Add(ProposalCard);

        ExistingPanel = Card("ProjectCreator.OpenExisting");
        ExistingPanel.SetValue(HavenProperties.Column, 1);
        Body.Add(ExistingPanel);
        ExistingPanel.Add(Label("ProjectCreator.OpenExisting.Title", "Open existing", TextLevel.H3));
        ExistingPanel.Add(Muted("ProjectCreator.OpenExisting.Help", "Connect an existing local folder, solution, or project file without running creation commands."));
        OpenFolderButton = new HavenButton { Name = "ProjectCreator.OpenFolder", Content = "Open folder", IconKey = "folder", Variant = ButtonVariant.Primary };
        ExistingPanel.Add(OpenFolderButton);
        OpenProjectFileButton = Ghost("ProjectCreator.OpenProjectFile", "Open project or solution file", "file");
        ExistingPanel.Add(OpenProjectFileButton);
        ExistingPanel.Add(Muted("ProjectCreator.OpenExisting.Note", "Haven inspects the selected work and connects it as a Project. Source files remain in their existing location."));

        Status = Muted("ProjectCreator.Status", "Describe the project, then review the proposed files and commands.");
        Status.SetValue(HavenProperties.Row, 2);
        Root.Add(Status);

        ProjectNameInput.TextChanged += (_, _) => { if (!_syncing) ProjectNameChanged?.Invoke(ProjectNameInput.Text); };
        DestinationInput.TextChanged += (_, _) => { if (!_syncing) DestinationChanged?.Invoke(DestinationInput.Text); };
        PromptInput.TextChanged += (_, _) => { if (!_syncing) PromptChanged?.Invoke(PromptInput.Text); };
        PackageDescriptionInput.TextChanged += (_, _) => { if (!_syncing) PackageDescriptionChanged?.Invoke(PackageDescriptionInput.Text); };
        DotNetButton.Invoked += (_, _) => DotNetRequested?.Invoke(this, EventArgs.Empty);
        PackageButton.Invoked += (_, _) => PackageRequested?.Invoke(this, EventArgs.Empty);
        ReviewButton.Invoked += (_, _) => ReviewRequested?.Invoke(this, EventArgs.Empty);
        ApproveButton.Invoked += (_, _) => ApproveRequested?.Invoke(this, EventArgs.Empty);
        ChooseDestinationButton.Invoked += (_, _) => ChooseDestinationRequested?.Invoke(this, EventArgs.Empty);
        OpenFolderButton.Invoked += (_, _) => OpenFolderRequested?.Invoke(this, EventArgs.Empty);
        OpenProjectFileButton.Invoked += (_, _) => OpenProjectFileRequested?.Invoke(this, EventArgs.Empty);
        SetViewportWidth(1200);
    }

    public Page Root { get; }
    public Container Body { get; }
    public Container CreatePanel { get; }
    public Container ExistingPanel { get; }
    public Container TemplateSection { get; }
    public Container Templates { get; }
    public Container PackageSection { get; }
    public Container ProposalCard { get; }
    public Container ProposalFiles { get; }
    public Container ProposalCommands { get; }
    public Input ProjectNameInput { get; }
    public Input DestinationInput { get; }
    public Input PromptInput { get; }
    public Input PackageDescriptionInput { get; }
    public HavenButton DotNetButton { get; }
    public HavenButton PackageButton { get; }
    public HavenButton ChooseDestinationButton { get; }
    public HavenButton ReviewButton { get; }
    public HavenButton ApproveButton { get; }
    public HavenButton OpenFolderButton { get; }
    public HavenButton OpenProjectFileButton { get; }
    public HavenText ProposalSummary { get; }
    public HavenText ProposalTarget { get; }
    public HavenText Status { get; }

    public event Action<string>? ProjectNameChanged;
    public event Action<string>? DestinationChanged;
    public event Action<string>? PromptChanged;
    public event Action<string>? PackageDescriptionChanged;
    public event Action<string>? TemplateRequested;
    public event EventHandler? DotNetRequested;
    public event EventHandler? PackageRequested;
    public event EventHandler? ReviewRequested;
    public event EventHandler? ApproveRequested;
    public event EventHandler? ChooseDestinationRequested;
    public event EventHandler? OpenFolderRequested;
    public event EventHandler? OpenProjectFileRequested;

    public void Sync(ProjectCreatorSceneState state)
    {
        _syncing = true;
        try
        {
            if (ProjectNameInput.Text != state.ProjectName) ProjectNameInput.Text = state.ProjectName;
            if (DestinationInput.Text != state.Destination) DestinationInput.Text = state.Destination;
            if (PromptInput.Text != state.Prompt) PromptInput.Text = state.Prompt;
            if (PackageDescriptionInput.Text != state.PackageDescription) PackageDescriptionInput.Text = state.PackageDescription;
        }
        finally
        {
            _syncing = false;
        }

        DotNetButton.Variant = state.IsDotNetProject ? ButtonVariant.Primary : ButtonVariant.Ghost;
        PackageButton.Variant = state.IsNuGetPackage ? ButtonVariant.Primary : ButtonVariant.Ghost;
        TemplateSection.SetValue(HavenProperties.Visibility, state.IsDotNetProject ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        PackageSection.SetValue(HavenProperties.Visibility, state.IsNuGetPackage ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        foreach (var pair in _templateButtons) pair.Value.Variant = state.IsDotNetProject && string.Equals(pair.Key, state.SelectedTemplate, StringComparison.Ordinal) ? ButtonVariant.Primary : ButtonVariant.Ghost;

        foreach (var element in new HavenElement[] { ProjectNameInput, DestinationInput, PromptInput, PackageDescriptionInput, DotNetButton, PackageButton, ChooseDestinationButton, OpenFolderButton, OpenProjectFileButton })
            element.SetValue(HavenProperties.Enabled, !state.IsBusy);
        ReviewButton.SetValue(HavenProperties.Enabled, state.CanReview);
        ApproveButton.SetValue(HavenProperties.Enabled, state.CanApprove);
        Status.Content = state.Status;
        RenderProposal(state.Proposal);
    }

    public void SetUnavailable()
    {
        Status.Content = "Project creator is unavailable.";
        foreach (var element in Root.DescendantsAndSelf()) element.SetValue(HavenProperties.Enabled, false);
    }

    public void SetViewportWidth(double width)
    {
        if (!double.IsFinite(width) || width <= 0) return;
        var compact = width < 820;
        Body.Columns = compact ? "1fr" : "1fr 1fr";
        Body.Rows = compact ? "Auto Auto" : "Auto";
        ExistingPanel.SetValue(HavenProperties.Column, compact ? 0 : 1);
        ExistingPanel.SetValue(HavenProperties.Row, compact ? 1 : 0);
        Root.SetValue(HavenProperties.Padding, HavenThickness.Parse(width < 560 ? "12px" : "22px 28px 18px 28px"));
    }

    private void RenderProposal(ProjectCreationProposal? proposal)
    {
        ProposalCard.SetValue(HavenProperties.Visibility, proposal is null ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        if (proposal is null)
        {
            _proposalFingerprint = null;
            Clear(ProposalFiles);
            Clear(ProposalCommands);
            ProposalSummary.Content = string.Empty;
            ProposalTarget.Content = string.Empty;
            return;
        }
        if (string.Equals(_proposalFingerprint, proposal.Fingerprint, StringComparison.Ordinal)) return;
        _proposalFingerprint = proposal.Fingerprint;
        ProposalSummary.Content = proposal.Summary;
        ProposalTarget.Content = $"Target: {proposal.TargetFolder}\nTemplate: {proposal.TemplateName}";
        Clear(ProposalFiles);
        foreach (var file in proposal.Files)
        {
            var row = Card("ProjectCreator.Proposal.File." + SafeName(file.RelativePath));
            row.Add(Label(string.Empty, file.RelativePath));
            row.Add(Muted(string.Empty, file.Purpose));
            ProposalFiles.Add(row);
        }
        Clear(ProposalCommands);
        foreach (var command in proposal.Commands)
        {
            var row = Card("ProjectCreator.Proposal.Command." + SafeName(command.DisplayText));
            row.Add(Label(string.Empty, command.DisplayText));
            row.Add(Muted(string.Empty, "Working directory: " + command.WorkingDirectory));
            ProposalCommands.Add(row);
        }
    }

    private static void Clear(Container container)
    {
        foreach (var child in container.Children.ToArray()) container.Remove(child);
    }

    private static string DisplayTemplateName(string template) => template switch
    {
        "Console app" => "Blank project",
        "Class library" => "Library",
        "Web API" => "Web API",
        "Worker service" => "Worker service",
        _ => template
    };

    private static string SafeName(string value)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var character in value) { hash ^= character; hash *= 16777619u; }
            return hash.ToString("x8");
        }
    }

    private static Container Grid(string name, string columns, string rows) { var c = new Container { Name = name, Layout = HavenLayout.Grid, Columns = columns, Rows = rows }; c.SetValue(HavenProperties.Width, HavenLength.Percent(100)); return c; }
    private static Container Vertical(string name, double gap) { var c = new Container { Name = name, Layout = HavenLayout.Vertical }; c.SetValue(HavenProperties.Width, HavenLength.Percent(100)); c.SetValue(HavenProperties.Gap, HavenLength.Px(gap)); return c; }
    private static Container Wrap(string name, double gap) { var c = new Container { Name = name, Layout = HavenLayout.Wrap }; c.SetValue(HavenProperties.Width, HavenLength.Percent(100)); c.SetValue(HavenProperties.Gap, HavenLength.Px(gap)); return c; }
    private static Container Card(string name) { var c = Vertical(name, 8); c.SetValue(HavenProperties.Background, "Surface"); c.SetValue(HavenProperties.BorderColor, "Border"); c.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1)); c.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(16))); c.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(16))); return c; }
    private static Input InputField(string name, string placeholder) { var input = new Input { Name = name, Placeholder = placeholder }; input.SetValue(HavenProperties.Width, HavenLength.Percent(100)); return input; }
    private static HavenButton Ghost(string name, string content, string icon) => new() { Name = name, Content = content, IconKey = icon, Variant = ButtonVariant.Ghost };
    private static HavenText Label(string name, string content, TextLevel level = TextLevel.Paragraph) => new() { Name = name, Content = content, Level = level };
    private static HavenText Caption(string content) { var text = Label(string.Empty, content, TextLevel.Caption); text.SetValue(HavenProperties.Foreground, "TextSecondary"); return text; }
    private static HavenText Muted(string name, string content) { var text = Label(name, content, TextLevel.Caption); text.SetValue(HavenProperties.Foreground, "TextSecondary"); return text; }
}
