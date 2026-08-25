using Haven.Application;
using Haven.Desktop.Services;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Spaces;

internal sealed record SpaceEditorDraft(
    string Name,
    string Description,
    string? ModelName,
    string Instructions,
    SpaceThinkingMode ThinkingMode,
    IReadOnlyList<SpaceExamplePair> ExamplePairs,
    SpaceGeneratedSurface? GeneratedSurface);

internal sealed class SpacesHavenScene : IDisposable
{
    private static readonly IReadOnlyList<string> ThinkingChoices = ["Default", "Fast", "Balanced", "Deep"];
    private static readonly IReadOnlyList<string> SurfaceChoices = ["Standard", "Checklist", "Data grid", "Card deck", "Dashboard", "Assessment", "Workflow", "Custom"];
    private readonly List<SpaceExamplePair> _examples = [];
    private SpaceDefinition? _selected;
    private bool _editWithHavenAvailable;
    private bool _disposed;

    public SpacesHavenScene()
    {
        Root = BuildRoot();
        Header = Get<Container>("Header");
        HeaderTitle = Get<Container>("HeaderTitle");
        Body = Get<Container>("Body");
        PickerPanel = Get<Container>("PickerPanel");
        EditorPanel = Get<Container>("EditorPanel");
        EditorActions = Get<Container>("EditorActions");
        SelectedHeading = Get<Container>("SelectedHeading");
        SpaceRows = Get<Container>("SpaceRows");
        EmptyState = Get<HavenText>("EmptyState");
        Editor = Get<Container>("Editor");
        SelectedName = Get<HavenText>("SelectedName");
        SelectedMeta = Get<HavenText>("SelectedMeta");
        EditInstruction = Get<Input>("EditInstruction");
        ApplySuggestedEdit = Get<HavenButton>("ApplySuggestedEdit");
        Name = Get<Input>("Name");
        Description = Get<Input>("Description");
        Model = Get<Input>("Model");
        Instructions = Get<Input>("Instructions");
        Thinking = Get<Select>("Thinking");
        Examples = Get<Container>("Examples");
        ExampleUser = Get<Input>("ExampleUser");
        ExampleAssistant = Get<Input>("ExampleAssistant");
        SurfaceTemplate = Get<Select>("SurfaceTemplate");
        SurfaceInputs = Get<Input>("SurfaceInputs");
        GeneratedPreviewState = Get<HavenText>("GeneratedPreviewState");
        GeneratedPreview = Get<Container>("GeneratedPreview");
        FilePermission = Get<Select>("FilePermission");
        Files = Get<Container>("Files");
        CreateSpace = Get<HavenButton>("CreateSpace");
        ShowArchived = Get<HavenButton>("ShowArchived");
        Save = Get<HavenButton>("Save");
        Launch = Get<HavenButton>("Launch");
        Fork = Get<HavenButton>("Fork");
        Archive = Get<HavenButton>("Archive");
        Delete = Get<HavenButton>("Delete");
        AddExample = Get<HavenButton>("AddExample");
        AddFile = Get<HavenButton>("AddFile");
        ManageLayout = Get<HavenButton>("ManageLayout");
        LayoutState = Get<HavenText>("LayoutState");
        Status = Get<HavenText>("Status");

        Thinking.Items = ThinkingChoices;
        SurfaceTemplate.Items = SurfaceChoices;
        FilePermission.Items = ["Read-only", "Read & write"];
        Thinking.SelectedIndex = 0;
        SurfaceTemplate.SelectedIndex = 0;
        FilePermission.SelectedIndex = 0;

        CreateSpace.Invoked += OnCreate;
        ShowArchived.Invoked += OnShowArchived;
        Save.Invoked += OnSave;
        Launch.Invoked += OnLaunch;
        Fork.Invoked += OnFork;
        Archive.Invoked += OnArchive;
        Delete.Invoked += OnDelete;
        AddExample.Invoked += OnAddExample;
        AddFile.Invoked += OnAddFile;
        ManageLayout.Invoked += OnManageLayout;
        ApplySuggestedEdit.Invoked += OnApplySuggestedEdit;
        SetEditWithHavenAvailable(false);
        SetLayoutEditorAvailable(false);
    }

    public Page Root { get; }
    public Container Header { get; }
    public Container HeaderTitle { get; }
    public Container Body { get; }
    public Container PickerPanel { get; }
    public Container EditorPanel { get; }
    public Container EditorActions { get; }
    public Container SelectedHeading { get; }
    public Container SpaceRows { get; }
    public HavenText EmptyState { get; }
    public Container Editor { get; }
    public HavenText SelectedName { get; }
    public HavenText SelectedMeta { get; }
    public Input EditInstruction { get; }
    public HavenButton ApplySuggestedEdit { get; }
    public Input Name { get; }
    public Input Description { get; }
    public Input Model { get; }
    public Input Instructions { get; }
    public Select Thinking { get; }
    public Container Examples { get; }
    public Input ExampleUser { get; }
    public Input ExampleAssistant { get; }
    public Select SurfaceTemplate { get; }
    public Input SurfaceInputs { get; }
    public HavenText GeneratedPreviewState { get; }
    public Container GeneratedPreview { get; }
    public Select FilePermission { get; }
    public Container Files { get; }
    public HavenButton CreateSpace { get; }
    public HavenButton ShowArchived { get; }
    public HavenButton Save { get; }
    public HavenButton Launch { get; }
    public HavenButton Fork { get; }
    public HavenButton Archive { get; }
    public HavenButton Delete { get; }
    public HavenButton AddExample { get; }
    public HavenButton AddFile { get; }
    public HavenButton ManageLayout { get; }
    public HavenText LayoutState { get; }
    public HavenText Status { get; }

    public event EventHandler? CreateRequested;
    public event EventHandler<bool>? ArchivedVisibilityChanged;
    public event EventHandler<Guid>? SpaceSelected;
    public event EventHandler<SpaceEditorDraft>? SaveRequested;
    public event EventHandler<Guid>? LaunchRequested;
    public event EventHandler<Guid>? ForkRequested;
    public event EventHandler<Guid>? ArchiveRequested;
    public event EventHandler<Guid>? DeleteRequested;
    public event EventHandler<SpaceFilePermission>? AddFileRequested;
    public event EventHandler<string>? RemoveFileRequested;
    public event EventHandler<Guid>? ManageLayoutRequested;
    public event EventHandler<string>? EditWithHavenRequested;

    public bool IncludeArchived { get; private set; }
    public bool IsCompactLayout { get; private set; }

    public void SetCompactLayout(bool compact)
    {
        IsCompactLayout = compact;

        Header.Columns = compact ? "1fr 1fr" : "1fr Auto Auto";
        Header.Rows = compact ? "Auto Auto" : "Auto";
        HeaderTitle.SetValue(HavenProperties.Column, 0);
        HeaderTitle.SetValue(HavenProperties.Row, 0);
        HeaderTitle.SetValue(HavenProperties.ColumnSpan, compact ? 2 : 1);
        ShowArchived.SetValue(HavenProperties.Column, compact ? 0 : 1);
        ShowArchived.SetValue(HavenProperties.Row, compact ? 1 : 0);
        CreateSpace.SetValue(HavenProperties.Column, compact ? 1 : 2);
        CreateSpace.SetValue(HavenProperties.Row, compact ? 1 : 0);

        Body.Columns = compact ? "1fr" : "280px 1fr";
        Body.Rows = compact ? "220px 1fr" : "1fr";
        PickerPanel.SetValue(HavenProperties.Column, 0);
        PickerPanel.SetValue(HavenProperties.Row, 0);
        EditorPanel.SetValue(HavenProperties.Column, compact ? 0 : 1);
        EditorPanel.SetValue(HavenProperties.Row, compact ? 1 : 0);

        EditorActions.Columns = compact ? "1fr 1fr" : "1fr Auto Auto Auto";
        EditorActions.Rows = compact ? "Auto Auto Auto" : "Auto";
        SelectedHeading.SetValue(HavenProperties.Column, 0);
        SelectedHeading.SetValue(HavenProperties.Row, 0);
        SelectedHeading.SetValue(HavenProperties.ColumnSpan, compact ? 2 : 1);
        Launch.SetValue(HavenProperties.Column, compact ? 0 : 1);
        Launch.SetValue(HavenProperties.Row, compact ? 1 : 0);
        Fork.SetValue(HavenProperties.Column, compact ? 1 : 2);
        Fork.SetValue(HavenProperties.Row, compact ? 1 : 0);
        Archive.SetValue(HavenProperties.Column, compact ? 0 : 3);
        Archive.SetValue(HavenProperties.Row, compact ? 2 : 0);
        Archive.SetValue(HavenProperties.ColumnSpan, compact ? 2 : 1);
    }

    public void SetSpaces(IReadOnlyList<SpaceDefinition> spaces, Guid? selectedId)
    {
        foreach (var child in SpaceRows.Children.ToArray()) SpaceRows.Remove(child);
        if (spaces.Count == 0)
        {
            var empty = Muted("No Spaces yet. Create one to get started.");
            SpaceRows.Add(empty);
            return;
        }

        foreach (var space in spaces)
        {
            var selected = space.Id == selectedId;
            var card = new Container { Layout = HavenLayout.Vertical };
            card.SetValue(HavenProperties.Width, HavenLength.Percent(100));
            card.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(8)));
            card.SetValue(HavenProperties.Gap, HavenLength.Px(3));
            card.SetValue(HavenProperties.Background, selected ? "AccentSoft" : "SurfaceRaised");
            card.SetValue(HavenProperties.BorderColor, selected ? "AccentSecondary" : "Border");
            card.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
            card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(14)));

            var open = new HavenButton
            {
                Content = space.Name,
                IconKey = string.IsNullOrWhiteSpace(space.IconKey) ? "sparkles" : space.IconKey,
                Variant = ButtonVariant.Navigation
            };
            open.SetValue(HavenProperties.Width, HavenLength.Percent(100));
            open.SetValue(HavenProperties.MinHeight, HavenLength.Px(38));
            open.Accessibility.AccessibleName = $"Open Space {space.Name}";
            var id = space.Id;
            open.Invoked += (_, _) => SpaceSelected?.Invoke(this, id);
            card.Add(open);

            var flags = new List<string>();
            if (space.IsBuiltIn) flags.Add("Built-in");
            if (space.IsArchived) flags.Add("Archived");
            flags.Add(space.Kind.ToString());
            card.Add(Muted(string.Join(" · ", flags)));
            SpaceRows.Add(card);
        }
    }

    public void SetSpace(SpaceDefinition? space)
    {
        _selected = space;
        if (space is null)
        {
            EmptyState.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
            Editor.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
            return;
        }

        EmptyState.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        Editor.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
        SelectedName.Content = space.Name;
        SelectedMeta.Content = $"{space.Kind} Space{(space.IsBuiltIn ? " · Built-in" : string.Empty)}{(space.IsArchived ? " · Archived" : string.Empty)}";
        Name.Text = space.Name;
        Description.Text = space.Description;
        Model.Text = space.ModelName ?? string.Empty;
        Instructions.Text = space.Instructions;
        Thinking.SelectedIndex = Math.Clamp((int)space.ThinkingMode, 0, ThinkingChoices.Count - 1);
        _examples.Clear();
        _examples.AddRange(space.ExamplePairs);
        RenderExamples();
        SurfaceTemplate.SelectedIndex = SurfaceIndex(space.GeneratedSurface?.TemplateKey);
        SurfaceInputs.Text = space.GeneratedSurface?.InputsJson ?? "{}";
        RenderFiles(space.Files);
        Launch.Content = space.Kind == SpaceKind.Study ? "Open Study" : "Open Space";
        Archive.Content = space.IsArchived ? "Restore" : "Archive";
        Delete.SetValue(HavenProperties.Enabled, !space.IsBuiltIn);
        Delete.Content = space.IsBuiltIn ? "Built-in Space" : "Delete";
        SetEditWithHavenAvailable(_editWithHavenAvailable);
    }

    public void SetGeneratedPreview(HavenElement? preview, string? status)
    {
        foreach (var child in GeneratedPreview.Children.ToArray()) GeneratedPreview.Remove(child);
        if (preview is not null)
        {
            preview.SetValue(HavenProperties.Width, HavenLength.Percent(100));
            GeneratedPreview.Add(preview);
            GeneratedPreview.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
        }
        else
        {
            GeneratedPreview.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        }

        GeneratedPreviewState.Content = status ?? string.Empty;
        GeneratedPreviewState.SetValue(HavenProperties.Visibility, string.IsNullOrWhiteSpace(status) ? HavenVisibility.Collapsed : HavenVisibility.Visible);
    }

    public void SetLaunchAvailable(bool available)
    {
        Launch.SetValue(HavenProperties.Enabled, available);
        if (!available) Launch.Content = "Opening will be available when Spaces is connected to the shell";
    }

    public void SetLayoutEditorAvailable(bool available)
    {
        ManageLayout.SetValue(HavenProperties.Enabled, available);
        ManageLayout.Content = available ? "Manage Layout / Additional Logic" : "Manage Layout / Additional Logic · waiting for shared editor";
        LayoutState.Content = available
            ? "Uses Haven's shared node editor."
            : "The canonical shared NodeEditor is still being landed by the Data/Automations workstream; Spaces will consume it rather than create a second graph editor.";
    }

    public void SetEditWithHavenAvailable(bool available)
    {
        _editWithHavenAvailable = available;
        ApplySuggestedEdit.SetValue(HavenProperties.Enabled, available && _selected is not null);
        ApplySuggestedEdit.Content = available ? "Suggest changes" : "Suggest changes · model unavailable";
    }

    internal void ApplyEditPatch(SpaceEditPatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        if (patch.Name is not null) Name.Text = patch.Name;
        if (patch.Description is not null) Description.Text = patch.Description;
        if (patch.ModelName is not null) Model.Text = patch.ModelName;
        if (patch.Instructions is not null) Instructions.Text = patch.Instructions;
        if (patch.ThinkingMode is { } thinking) Thinking.SelectedIndex = Math.Clamp((int)thinking, 0, ThinkingChoices.Count - 1);
        if (patch.SurfaceTemplate is not null) SurfaceTemplate.SelectedIndex = SurfaceIndex(patch.SurfaceTemplate == "standard" ? null : patch.SurfaceTemplate);
        if (patch.SurfaceInputsJson is not null) SurfaceInputs.Text = patch.SurfaceInputsJson;
        EditInstruction.Text = string.Empty;
        SetStatus("Suggested changes are in the draft. Review them, then choose Save Space to persist them.");
    }

    public void SetBusy(bool busy)
    {
        CreateSpace.SetValue(HavenProperties.Enabled, !busy);
        Save.SetValue(HavenProperties.Enabled, !busy && _selected is not null);
        Fork.SetValue(HavenProperties.Enabled, !busy && _selected is not null);
        Archive.SetValue(HavenProperties.Enabled, !busy && _selected is not null);
        AddFile.SetValue(HavenProperties.Enabled, !busy && _selected is not null);
        AddExample.SetValue(HavenProperties.Enabled, !busy && _selected is not null);
        ApplySuggestedEdit.SetValue(HavenProperties.Enabled, !busy && _editWithHavenAvailable && _selected is not null);
        if (_selected is { IsBuiltIn: false }) Delete.SetValue(HavenProperties.Enabled, !busy);
    }

    public void SetStatus(string? value)
    {
        Status.Content = value ?? string.Empty;
        Status.SetValue(HavenProperties.Visibility, string.IsNullOrWhiteSpace(value) ? HavenVisibility.Collapsed : HavenVisibility.Visible);
    }

    private void RenderExamples()
    {
        foreach (var child in Examples.Children.ToArray()) Examples.Remove(child);
        if (_examples.Count == 0) Examples.Add(Muted("No example pairs yet."));
        for (var index = 0; index < _examples.Count; index++)
        {
            var pair = _examples[index];
            var row = Card();
            row.Add(new HavenText { Content = $"You: {pair.User}" });
            row.Add(Muted($"Haven: {pair.Assistant}"));
            var remove = new HavenButton { Content = "Remove example", Variant = ButtonVariant.Text };
            remove.Accessibility.AccessibleName = $"Remove example {index + 1}";
            var removeIndex = index;
            remove.Invoked += (_, _) =>
            {
                _examples.RemoveAt(removeIndex);
                RenderExamples();
            };
            row.Add(remove);
            Examples.Add(row);
        }
    }

    private void RenderFiles(IReadOnlyList<SpaceFileReference> files)
    {
        foreach (var child in Files.Children.ToArray()) Files.Remove(child);
        if (files.Count == 0)
        {
            Files.Add(Muted("No files connected to this Space."));
            return;
        }
        foreach (var file in files)
        {
            var row = Card();
            row.Add(new HavenText { Content = file.DisplayName });
            row.Add(Muted(file.Permission == SpaceFilePermission.ReadWrite ? "Read & write" : "Read-only"));
            var remove = new HavenButton { Content = "Remove", Variant = ButtonVariant.Text };
            remove.Accessibility.AccessibleName = $"Remove {file.DisplayName} from Space";
            var path = file.Path;
            remove.Invoked += (_, _) => RemoveFileRequested?.Invoke(this, path);
            row.Add(remove);
            Files.Add(row);
        }
    }

    private void OnCreate(object? sender, EventArgs e) => CreateRequested?.Invoke(this, EventArgs.Empty);

    private void OnShowArchived(object? sender, EventArgs e)
    {
        IncludeArchived = !IncludeArchived;
        ShowArchived.Content = IncludeArchived ? "Hide archived" : "Show archived";
        ArchivedVisibilityChanged?.Invoke(this, IncludeArchived);
    }

    private void OnSave(object? sender, EventArgs e) => SaveCurrentDraft();

    internal void SaveCurrentDraft()
    {
        if (_selected is null) return;
        if (string.IsNullOrWhiteSpace(Name.Text))
        {
            SetStatus("Give this Space a name before saving.");
            return;
        }
        var templateKey = SurfaceKey(SurfaceTemplate.SelectedItem);
        SpaceGeneratedSurface? generated = templateKey is null
            ? null
            : new SpaceGeneratedSurface(templateKey, string.IsNullOrWhiteSpace(SurfaceInputs.Text) ? "{}" : SurfaceInputs.Text.Trim());
        SaveRequested?.Invoke(this, new SpaceEditorDraft(
            Name.Text.Trim(),
            Description.Text.Trim(),
            string.IsNullOrWhiteSpace(Model.Text) ? null : Model.Text.Trim(),
            Instructions.Text.Trim(),
            (SpaceThinkingMode)Math.Max(0, Thinking.SelectedIndex),
            _examples.ToArray(),
            generated));
    }

    private void OnLaunch(object? sender, EventArgs e)
    {
        if (_selected is not null) LaunchRequested?.Invoke(this, _selected.Id);
    }

    private void OnFork(object? sender, EventArgs e)
    {
        if (_selected is not null) ForkRequested?.Invoke(this, _selected.Id);
    }

    private void OnArchive(object? sender, EventArgs e)
    {
        if (_selected is not null) ArchiveRequested?.Invoke(this, _selected.Id);
    }

    private void OnDelete(object? sender, EventArgs e) => ShowDeleteConfirmation();

    internal void ShowDeleteConfirmation()
    {
        if (_selected is null || _selected.IsBuiltIn) return;
        foreach (var existingPopup in Root.Children.OfType<PopupMenu>().ToArray()) existingPopup.Dismiss();
        var id = _selected.Id;
        var popup = new PopupMenu(Delete, Root,
        [
            new PopupMenuItem("Delete permanently", () => ConfirmDelete(id), true, "trash"),
            new PopupMenuItem("Cancel", () => { })
        ], 240d, $"Delete {_selected.Name}");
        Root.Add(popup);
    }

    internal void ConfirmDelete(Guid id) => DeleteRequested?.Invoke(this, id);

    private void OnAddExample(object? sender, EventArgs e) => AddExampleFromInputs();

    internal void AddExampleFromInputs()
    {
        var user = ExampleUser.Text.Trim();
        var assistant = ExampleAssistant.Text.Trim();
        if (user.Length == 0 || assistant.Length == 0)
        {
            SetStatus("Example pairs need both a user message and a Haven response.");
            return;
        }
        _examples.Add(new SpaceExamplePair(user, assistant));
        ExampleUser.Text = string.Empty;
        ExampleAssistant.Text = string.Empty;
        RenderExamples();
        SetStatus(null);
    }

    private void OnAddFile(object? sender, EventArgs e)
    {
        if (_selected is null) return;
        AddFileRequested?.Invoke(this, FilePermission.SelectedIndex == 1 ? SpaceFilePermission.ReadWrite : SpaceFilePermission.ReadOnly);
    }

    private void OnApplySuggestedEdit(object? sender, EventArgs e)
    {
        if (_selected is null || !_editWithHavenAvailable) return;
        var instruction = EditInstruction.Text.Trim();
        if (instruction.Length == 0)
        {
            SetStatus("Describe the Space change you want first.");
            return;
        }
        EditWithHavenRequested?.Invoke(this, instruction);
    }

    private void OnManageLayout(object? sender, EventArgs e)
    {
        if (_selected is not null && ManageLayout.GetValue(HavenProperties.Enabled)) ManageLayoutRequested?.Invoke(this, _selected.Id);
    }

    private static int SurfaceIndex(string? key) => key?.ToLowerInvariant() switch
    {
        "checklist" => 1,
        "data-grid" => 2,
        "card-deck" => 3,
        "dashboard" => 4,
        "assessment" => 5,
        "workflow" => 6,
        "custom" => 7,
        _ => 0
    };

    private static string? SurfaceKey(string? choice) => choice switch
    {
        "Checklist" => "checklist",
        "Data grid" => "data-grid",
        "Card deck" => "card-deck",
        "Dashboard" => "dashboard",
        "Assessment" => "assessment",
        "Workflow" => "workflow",
        "Custom" => "custom",
        _ => null
    };

    private T Get<T>(string name) where T : HavenElement =>
        (T)Root.DescendantsAndSelf().Single(element => element.Name == name);

    private static HavenText Muted(string content)
    {
        var text = new HavenText { Content = content };
        text.SetValue(HavenProperties.Foreground, "TextSecondary");
        text.SetValue(HavenProperties.FontSize, 11d);
        return text;
    }

    private static Container Card()
    {
        var card = new Container { Layout = HavenLayout.Vertical };
        card.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        card.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(10)));
        card.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        card.SetValue(HavenProperties.Background, "SurfaceRaised");
        card.SetValue(HavenProperties.BorderColor, "Border");
        card.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12)));
        return card;
    }

    private static Page BuildRoot()
    {
        const string markup = """
            <Page Name="SpacesRoot" Layout="Grid" Width="100%" Height="100%" Rows="Auto 1fr Auto" Gap="14px" Padding="22px" Background="Transparent">
              <Container Name="Header" Row="0" Layout="Grid" Columns="1fr Auto Auto" Width="100%" Gap="8px">
                <Container Name="HeaderTitle" Column="0" Layout="Vertical" Gap="2px">
                  <Text Content="Spaces" Level="H1" />
                  <Text Content="Reusable AI workspaces with their own context, files, model behaviour and interface." Foreground="TextSecondary" FontSize="12" />
                </Container>
                <Button Name="ShowArchived" Column="1" Variant="Tertiary" Content="Show archived" MinHeight="38px" />
                <Button Name="CreateSpace" Column="2" Variant="Primary" IconKey="plus" Content="Create Space" MinHeight="38px" />
              </Container>
              <Container Name="Body" Row="1" Layout="Grid" Columns="280px 1fr" Width="100%" Height="100%" Gap="14px">
                <Container Name="PickerPanel" Column="0" Layout="Vertical" Width="100%" Height="100%" Gap="8px" Padding="10px" Background="SurfaceRaised" BorderColor="Border" BorderWidth="1px" Radius="16px">
                  <Text Content="Your Spaces" Level="H2" />
                  <Container Name="SpaceRows" Layout="Vertical" Width="100%" Height="100%" Overflow="Scroll" Clip="true" Gap="7px" />
                </Container>
                <Container Name="EditorPanel" Column="1" Layout="Vertical" Width="100%" Height="100%" Overflow="Scroll" Clip="true" Gap="10px">
                  <Text Name="EmptyState" Content="Choose a Space to edit its configuration." Foreground="TextSecondary" FontSize="13" />
                  <Container Name="Editor" Layout="Vertical" Width="100%" Gap="12px" Visibility="Collapsed">
                    <Container Name="EditorActions" Layout="Grid" Columns="1fr Auto Auto Auto" Width="100%" Gap="7px">
                      <Container Name="SelectedHeading" Column="0" Layout="Vertical" Gap="2px"><Text Name="SelectedName" Content="Space" Level="H2" /><Text Name="SelectedMeta" Content="" Foreground="TextSecondary" FontSize="11" /></Container>
                      <Button Name="Launch" Column="1" Variant="Primary" Content="Open Space" MinHeight="36px" />
                      <Button Name="Fork" Column="2" Variant="Tertiary" Content="Fork" MinHeight="36px" />
                      <Button Name="Archive" Column="3" Variant="Tertiary" Content="Archive" MinHeight="36px" />
                    </Container>
                    <Container Layout="Vertical" Gap="7px" Padding="14px" Background="SurfaceRaised" BorderColor="Border" BorderWidth="1px" Radius="16px">
                      <Text Content="Describe a Space change" Level="H2" />
                      <Text Content="Tell Haven what you want to change. Suggested edits are applied to this draft for review and are not saved automatically." Foreground="TextSecondary" FontSize="11" />
                      <Container Layout="Grid" Columns="1fr Auto" Width="100%" Gap="8px">
                        <Input Name="EditInstruction" Column="0" Width="100%" Placeholder="e.g. Make this a deep research Space with a checklist" />
                        <Button Name="ApplySuggestedEdit" Column="1" Variant="Tertiary" Content="Suggest changes" MinHeight="38px" />
                      </Container>
                    </Container>
                    <Container Layout="Vertical" Gap="7px" Padding="14px" Background="SurfaceRaised" BorderColor="Border" BorderWidth="1px" Radius="16px">
                      <Text Content="Identity &amp; behaviour" Level="H2" />
                      <Text Content="Name" Foreground="TextSecondary" FontSize="11" /><Input Name="Name" Width="100%" Placeholder="Space name" />
                      <Text Content="Description" Foreground="TextSecondary" FontSize="11" /><Input Name="Description" Width="100%" Multiline="true" MinHeight="64px" Placeholder="What is this Space for?" />
                      <Text Content="Model" Foreground="TextSecondary" FontSize="11" /><Input Name="Model" Width="100%" Placeholder="Default model" />
                      <Text Content="Thinking" Foreground="TextSecondary" FontSize="11" /><Select Name="Thinking" Width="100%" />
                      <Text Content="Instructions" Foreground="TextSecondary" FontSize="11" /><Input Name="Instructions" Width="100%" Multiline="true" MinHeight="100px" Placeholder="How should Haven behave in this Space?" />
                    </Container>
                    <Container Layout="Vertical" Gap="7px" Padding="14px" Background="SurfaceRaised" BorderColor="Border" BorderWidth="1px" Radius="16px">
                      <Text Content="Examples" Level="H2" /><Text Content="Teach the Space with small user/assistant examples." Foreground="TextSecondary" FontSize="11" />
                      <Container Name="Examples" Layout="Vertical" Width="100%" Gap="6px" />
                      <Input Name="ExampleUser" Width="100%" Placeholder="Example user message" /><Input Name="ExampleAssistant" Width="100%" Multiline="true" MinHeight="64px" Placeholder="Example Haven response" />
                      <Button Name="AddExample" Variant="Tertiary" Content="Add example pair" MinHeight="36px" />
                    </Container>
                    <Container Layout="Vertical" Gap="7px" Padding="14px" Background="SurfaceRaised" BorderColor="Border" BorderWidth="1px" Radius="16px">
                      <Text Content="Generated surface" Level="H2" /><Text Content="Choose a trusted Haven GenUI template or keep the standard Space surface." Foreground="TextSecondary" FontSize="11" />
                      <Select Name="SurfaceTemplate" Width="100%" /><Input Name="SurfaceInputs" Width="100%" Multiline="true" MinHeight="80px" Placeholder="Template inputs JSON" />
                      <Text Name="GeneratedPreviewState" Content="" Foreground="TextSecondary" FontSize="11" Visibility="Collapsed" />
                      <Container Name="GeneratedPreview" Layout="Vertical" Width="100%" MinHeight="140px" Gap="8px" Padding="12px" Background="Surface" BorderColor="Border" BorderWidth="1px" Radius="14px" Visibility="Collapsed" />
                    </Container>
                    <Container Layout="Vertical" Gap="7px" Padding="14px" Background="SurfaceRaised" BorderColor="Border" BorderWidth="1px" Radius="16px">
                      <Text Content="Manage Files" Level="H2" /><Text Content="Files are scoped to this Space and carry an explicit permission." Foreground="TextSecondary" FontSize="11" />
                      <Container Layout="Grid" Columns="1fr Auto" Width="100%" Gap="8px"><Select Name="FilePermission" Column="0" Width="100%" /><Button Name="AddFile" Column="1" Variant="Tertiary" IconKey="plus" Content="Add files" MinHeight="38px" /></Container>
                      <Container Name="Files" Layout="Vertical" Width="100%" Gap="6px" />
                    </Container>
                    <Container Layout="Vertical" Gap="7px" Padding="14px" Background="SurfaceRaised" BorderColor="Border" BorderWidth="1px" Radius="16px">
                      <Text Content="Manage Layout / Additional Logic" Level="H2" /><Text Name="LayoutState" Content="" Foreground="TextSecondary" FontSize="11" /><Button Name="ManageLayout" Variant="Tertiary" Content="Manage Layout / Additional Logic" MinHeight="38px" />
                    </Container>
                    <Container Layout="Grid" Columns="1fr Auto" Width="100%" Gap="8px"><Button Name="Save" Column="0" Variant="Primary" Content="Save Space" MinHeight="40px" /><Button Name="Delete" Column="1" Variant="Danger" IconKey="trash" Content="Delete" MinHeight="40px" /></Container>
                  </Container>
                </Container>
              </Container>
              <Text Name="Status" Row="2" Content="" Foreground="TextSecondary" FontSize="11" Visibility="Collapsed" />
            </Page>
            """;
        return (Page)new HavenMarkupParser().Parse(markup, "Spaces.hui");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CreateSpace.Invoked -= OnCreate;
        ShowArchived.Invoked -= OnShowArchived;
        Save.Invoked -= OnSave;
        Launch.Invoked -= OnLaunch;
        Fork.Invoked -= OnFork;
        Archive.Invoked -= OnArchive;
        Delete.Invoked -= OnDelete;
        AddExample.Invoked -= OnAddExample;
        AddFile.Invoked -= OnAddFile;
        ManageLayout.Invoked -= OnManageLayout;
        ApplySuggestedEdit.Invoked -= OnApplySuggestedEdit;
        foreach (var popup in Root.Children.OfType<PopupMenu>().ToArray()) popup.Dismiss();
    }
}
