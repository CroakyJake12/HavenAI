using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Shell.TopRail;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Go;

internal sealed class GoHavenScene : IDisposable
{
    private readonly List<HavenButton>[] _suggestionButtons = [[], [], [], []];
    private readonly List<Icon>[] _suggestionIcons = [[], [], [], []];
    private readonly List<(HavenButton Button, AddMenu.AddMenuAction Action, object Item)> _catalogRows = [];
    private readonly List<(HavenElement Element, EventHandler Handler)> _visualSubscriptions = [];
    private IReadOnlyList<AgentDefinition> _agents = [];
    private IReadOnlyList<CapabilityDefinition> _capabilities = [];
    private IReadOnlyList<PromptDefinition> _instructions = [];
    private IReadOnlyList<ModeDefinition> _apps = [];
    private AddMenu.AddMenuAction? _catalogAction;
    private string _lastSearch = string.Empty;
    private bool _disposed;

    public GoHavenScene()
    {
        Root = new Page { Name = "Go.Root", Layout = HavenLayout.Grid, Columns = "1fr", Rows = "1fr Auto Auto Auto" };
        Root.SetValue(HavenProperties.Padding, HavenThickness.Parse("12px 42px 22px 42px"));
        Root.SetValue(HavenProperties.Background, "Transparent");
        Root.SetValue(HavenProperties.Overflow, HavenOverflow.Clip);

        WideHero = BuildHero(compact: false);
        WideHero.Conditions.Add(new HavenScreenRangeCondition(HavenScreenAxis.Width, HavenLength.Px(680)));
        CompactHero = BuildHero(compact: true);
        CompactHero.Conditions.Add(new HavenScreenRangeCondition(HavenScreenAxis.Width, maximum: HavenLength.Px(679.999)));
        Root.Add(WideHero);
        Root.Add(CompactHero);

        LoadMoreButton = new HavenButton { Name = "Go.Suggestions.LoadMore", Variant = ButtonVariant.Text, Content = "More suggestions" };
        LoadMoreButton.SetValue(HavenProperties.Row, 1);
        LoadMoreButton.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        LoadMoreButton.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        Root.Add(LoadMoreButton);

        AttachmentText = new HavenText { Name = "Go.Attachments.StatusText", Level = TextLevel.Caption };
        AttachmentText.SetValue(HavenProperties.Foreground, "TextSecondary");
        AttachmentHost = new Container { Name = "Go.Attachments.Status", Layout = HavenLayout.Overlay };
        AttachmentHost.SetValue(HavenProperties.Row, 2);
        AttachmentHost.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        AttachmentHost.SetValue(HavenProperties.MaxWidth, HavenLength.Px(900));
        AttachmentHost.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        AttachmentHost.SetValue(HavenProperties.Background, "Surface");
        AttachmentHost.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(16)));
        AttachmentHost.SetValue(HavenProperties.Padding, HavenThickness.Parse("8px 14px"));
        AttachmentHost.SetValue(HavenProperties.Margin, HavenThickness.Parse("0px 0px 12px 0px"));
        AttachmentHost.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        AttachmentHost.Add(AttachmentText);
        Root.Add(AttachmentHost);

        Composer = new Container { Name = "Go.Composer", Layout = HavenLayout.Grid, Columns = "44px 1fr 44px", Rows = "44px" };
        Composer.SetValue(HavenProperties.Row, 3);
        Composer.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Composer.SetValue(HavenProperties.MaxWidth, HavenLength.Px(900));
        Composer.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        Composer.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        Composer.SetValue(HavenProperties.Padding, HavenThickness.Parse("7px"));
        Composer.SetValue(HavenProperties.Background, "SurfaceRaised");
        Composer.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(28)));
        Composer.SetValue(HavenProperties.Shadow, "Card");

        AddButton = CreateIconButton("Go.Composer.Add", "plus", 0, "Add", out var addIcon);
        Composer.Add(AddButton);
        Composer.Add(addIcon);

        Instruction = new Input { Name = "Go.Composer.Instruction", Placeholder = "Ask Haven anything" };
        Instruction.Accessibility.AccessibleName = "Ask Haven anything";
        Instruction.SetValue(HavenProperties.Column, 1);
        Instruction.SetValue(HavenProperties.MinHeight, HavenLength.Px(44));
        Instruction.SetValue(HavenProperties.Height, HavenLength.Px(44));
        Instruction.SetValue(HavenProperties.Background, "Transparent");
        Instruction.SetValue(HavenProperties.Padding, HavenThickness.Parse("0px 12px"));
        Instruction.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(22)));
        Composer.Add(Instruction);

        SendButton = CreateIconButton("Go.Composer.Send", "arrow-up", 2, "Send", out var sendIcon);
        Composer.Add(SendButton);
        Composer.Add(sendIcon);
        Root.Add(Composer);

        AddOverlay = BuildAddOverlay();
        Root.Add(AddOverlay);

        AddButton.Invoked += OnAddInvoked;
        DismissAddButton.Invoked += OnDismissInvoked;
        AttachFilesButton.Invoked += OnAttachFilesInvoked;
        AgentsButton.Invoked += (_, _) => OpenCatalogue(AddMenu.AddMenuAction.Agent);
        InstructionsButton.Invoked += (_, _) => OpenCatalogue(AddMenu.AddMenuAction.Instruction);
        CapabilitiesButton.Invoked += (_, _) => OpenCatalogue(AddMenu.AddMenuAction.Capability);
        AppsButton.Invoked += (_, _) => OpenCatalogue(AddMenu.AddMenuAction.App);
        AllowActionsButton.Invoked += (_, _) => OpenCatalogue(AddMenu.AddMenuAction.AllowActions);
        VisualResponsesButton.Invoked += (_, _) => OpenCatalogue(AddMenu.AddMenuAction.VisualResponses);
        CatalogSearch.Invalidated += OnCatalogSearchInvalidated;
    }

    public event EventHandler<AddMenu.AddMenuAction>? AddActionSelected;
    public event EventHandler<AddMenuSelection>? CatalogItemSelected;

    public Page Root { get; }
    public Container WideHero { get; }
    public Container CompactHero { get; }
    public HavenText WideTitle { get; private set; } = null!;
    public HavenText CompactTitle { get; private set; } = null!;
    public Container WideSuggestions { get; private set; } = null!;
    public Container CompactSuggestions { get; private set; } = null!;
    public Container AttachmentHost { get; }
    public HavenButton LoadMoreButton { get; }
    public HavenText AttachmentText { get; }
    public Container Composer { get; }
    public Input Instruction { get; }
    public HavenButton AddButton { get; }
    public HavenButton SendButton { get; }
    public Container AddOverlay { get; }
    public HavenButton DismissAddButton { get; private set; } = null!;
    public Container MainMenu { get; private set; } = null!;
    public Container CatalogPanel { get; private set; } = null!;
    public Input CatalogSearch { get; private set; } = null!;
    public Container CatalogResults { get; private set; } = null!;
    public HavenButton AttachFilesButton { get; private set; } = null!;
    public HavenButton AgentsButton { get; private set; } = null!;
    public HavenButton InstructionsButton { get; private set; } = null!;
    public HavenButton CapabilitiesButton { get; private set; } = null!;
    public HavenButton AppsButton { get; private set; } = null!;
    public HavenButton AllowActionsButton { get; private set; } = null!;
    public HavenButton VisualResponsesButton { get; private set; } = null!;

    public IReadOnlyList<HavenButton> SuggestionButtons(int index) => _suggestionButtons[index];

    public void SetSuggestions(IReadOnlyList<GoSuggestion> suggestions)
    {
        if (suggestions.Count != 4) return;
        for (var index = 0; index < suggestions.Count; index++)
        {
            var suggestion = suggestions[index];
            foreach (var button in _suggestionButtons[index])
            {
                button.Content = suggestion.Label;
                button.Accessibility.AccessibleName = suggestion.Label;
                button.Accessibility.Description = suggestion.Instruction;
            }
            foreach (var icon in _suggestionIcons[index]) icon.Key = suggestion.IconKey;
        }
    }

    public void SetCatalogue(
        IReadOnlyList<AgentDefinition> agents,
        IReadOnlyList<CapabilityDefinition> capabilities,
        IReadOnlyList<PromptDefinition> instructions,
        IReadOnlyList<ModeDefinition> apps)
    {
        _agents = agents.Where(item => item.IsEnabled).OrderBy(item => item.Name).ToArray();
        _capabilities = capabilities.Where(item => item.IsEnabled && item.IsAttachable).OrderBy(item => item.Name).ToArray();
        _instructions = instructions.Where(item => item.IsEnabled).OrderBy(item => item.Name).ToArray();
        _apps = apps.Where(item => item.IsEnabled).OrderBy(item => item.Name).ToArray();
        if (_catalogAction is not null) RebuildCatalogue();
    }

    public void SetRefreshInProgress(bool inProgress)
    {
        LoadMoreButton.SetValue(HavenProperties.Enabled, !inProgress);
        LoadMoreButton.Content = inProgress ? "Finding more…" : "More suggestions";
    }

    public void SetAttachmentStatus(string? text)
    {
        var empty = string.IsNullOrWhiteSpace(text);
        AttachmentText.Content = empty ? string.Empty : text!;
        AttachmentHost.SetValue(HavenProperties.Visibility, empty ? HavenVisibility.Collapsed : HavenVisibility.Visible);
    }

    public void ShowAddMenu()
    {
        _catalogAction = null;
        CatalogPanel.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        AddOverlay.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
    }

    public void HideAddMenu()
    {
        _catalogAction = null;
        CatalogSearch.Text = string.Empty;
        CatalogPanel.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        AddOverlay.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
    }

    private Container BuildHero(bool compact)
    {
        var hero = new Container { Name = compact ? "Go.Hero.Compact" : "Go.Hero.Wide", Layout = HavenLayout.Vertical };
        hero.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        hero.SetValue(HavenProperties.MaxWidth, HavenLength.Px(800));
        hero.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        hero.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        hero.SetValue(HavenProperties.Gap, HavenLength.Px(compact ? 22 : 34));
        hero.SetValue(HavenProperties.Margin, HavenThickness.Parse(compact ? "0px 0px 14px 0px" : "0px 0px 32px 0px"));

        var title = new HavenText("How can I help?") { Name = compact ? "Go.Hero.Title.Compact" : "Go.Hero.Title.Wide", Level = TextLevel.H1 };
        title.SetValue(HavenProperties.FontSize, compact ? 34d : 40d);
        title.SetValue(HavenProperties.FontWeight, 800);
        title.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        hero.Add(title);

        var suggestions = new Container
        {
            Name = compact ? "Go.Suggestions.Compact" : "Go.Suggestions.Wide",
            Layout = HavenLayout.Grid,
            Columns = compact ? "1fr" : "1fr 1fr",
            Rows = compact ? "64px 64px 64px 64px" : "64px 64px"
        };
        suggestions.SetValue(HavenProperties.Width, compact ? HavenLength.Percent(100) : HavenLength.Px(800));
        suggestions.SetValue(HavenProperties.MaxWidth, HavenLength.Px(800));
        suggestions.SetValue(HavenProperties.Gap, HavenLength.Px(compact ? 10 : 14));
        suggestions.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);

        for (var index = 0; index < 4; index++)
        {
            var card = CreateSuggestion(index, compact);
            card.SetValue(HavenProperties.Row, compact ? index : index / 2);
            card.SetValue(HavenProperties.Column, compact ? 0 : index % 2);
            suggestions.Add(card);
        }
        hero.Add(suggestions);

        if (compact) { CompactTitle = title; CompactSuggestions = suggestions; }
        else { WideTitle = title; WideSuggestions = suggestions; }
        return hero;
    }

    private Container CreateSuggestion(int index, bool compact)
    {
        var suffix = compact ? "Compact" : "Wide";
        var host = new Container { Name = $"Go.Suggestions.Item{index}.{suffix}", Layout = HavenLayout.Overlay };
        host.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        host.SetValue(HavenProperties.Height, HavenLength.Px(64));

        var suggestion = GoSuggestionService.ImmediateDefaults[index];
        var button = new HavenButton { Name = $"Go.Suggestions.Item{index}.Button.{suffix}", Variant = ButtonVariant.Tertiary, Content = suggestion.Label };
        button.Accessibility.AccessibleName = suggestion.Label;
        button.Accessibility.Description = suggestion.Instruction;
        button.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        button.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        button.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(32)));
        button.SetValue(HavenProperties.Padding, HavenThickness.Parse("18px 18px 18px 64px"));
        button.SetValue(HavenProperties.FontSize, 13d);
        button.SetValue(HavenProperties.FontWeight, 700);
        host.Add(button);

        var pill = new Container { Name = $"Go.Suggestions.Item{index}.IconPill.{suffix}", Layout = HavenLayout.Overlay };
        pill.SetValue(HavenProperties.Width, HavenLength.Px(44));
        pill.SetValue(HavenProperties.Height, HavenLength.Px(44));
        pill.SetValue(HavenProperties.Margin, HavenThickness.Parse("0px 0px 0px 10px"));
        pill.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Start);
        pill.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        pill.SetValue(HavenProperties.Background, "Accent");
        pill.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(22)));
        pill.SetValue(HavenProperties.PointerEvents, HavenPointerEvents.None);
        pill.SetValue(HavenProperties.ZIndex, 1);

        var icon = new Icon { Name = $"Go.Suggestions.Item{index}.Icon.{suffix}", Key = suggestion.IconKey };
        icon.SetValue(HavenProperties.Width, HavenLength.Px(22));
        icon.SetValue(HavenProperties.Height, HavenLength.Px(22));
        icon.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        icon.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        icon.SetValue(HavenProperties.Foreground, "TextOnAccent");
        icon.SetValue(HavenProperties.PointerEvents, HavenPointerEvents.None);
        pill.Add(icon);
        host.Add(pill);

        EventHandler motionHandler = (_, _) =>
        {
            var targetScale = button.State.HasFlag(HavenElementState.Pressed)
                ? .94d
                : button.State.HasFlag(HavenElementState.Hover) ? 1.018d : 1d;
            button.SetValue(HavenProperties.Scale, 1d, HavenValueSource.State);
            host.SetValue(HavenProperties.Scale, targetScale, HavenValueSource.State);
            host.SetValue(
                HavenProperties.Transition,
                button.State.HasFlag(HavenElementState.Pressed) ? ButtonDefaults.PressedTransition : ButtonDefaults.HoverTransition,
                HavenValueSource.State);
        };
        button.Invalidated += motionHandler;
        _visualSubscriptions.Add((button, motionHandler));

        _suggestionButtons[index].Add(button);
        _suggestionIcons[index].Add(icon);
        return host;
    }

    private Container BuildAddOverlay()
    {
        var overlay = new Container { Name = "Go.Add.Overlay", Layout = HavenLayout.Overlay };
        overlay.SetValue(HavenProperties.Row, 0);
        overlay.SetValue(HavenProperties.RowSpan, 4);
        overlay.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        overlay.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        overlay.SetValue(HavenProperties.ZIndex, 100);
        overlay.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);

        DismissAddButton = new HavenButton { Name = "Go.Add.Dismiss", Variant = ButtonVariant.Text, Content = string.Empty };
        DismissAddButton.Accessibility.AccessibleName = "Close Add menu";
        DismissAddButton.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        DismissAddButton.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        DismissAddButton.SetValue(HavenProperties.Background, "Transparent");
        overlay.Add(DismissAddButton);

        var panels = new Container { Name = "Go.Add.Panels", Layout = HavenLayout.Horizontal };
        panels.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        panels.SetValue(HavenProperties.MaxWidth, HavenLength.Px(900));
        panels.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        panels.SetValue(HavenProperties.PointerEvents, HavenPointerEvents.ChildrenOnly);
        panels.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.End);
        panels.SetValue(HavenProperties.Gap, HavenLength.Px(12));
        panels.SetValue(HavenProperties.Margin, HavenThickness.Parse("0px 0px 70px 0px"));
        panels.SetValue(HavenProperties.ZIndex, 101);

        MainMenu = BuildMainMenu();
        panels.Add(MainMenu);
        CatalogPanel = BuildCatalogPanel();
        panels.Add(CatalogPanel);
        overlay.Add(panels);
        return overlay;
    }

    private Container BuildMainMenu()
    {
        var menu = Card("Go.Add.Main", 324, HavenLayout.Vertical, 8);
        menu.SetValue(HavenProperties.Padding, HavenThickness.Parse("14px"));
        menu.Add(Heading("Manage Responses", 22));
        menu.Add(Heading("Available Tools", 13));

        var tools = new Container { Name = "Go.Add.Tools", Layout = HavenLayout.Grid, Columns = "1fr 1fr", Rows = "44px 44px" };
        tools.SetValue(HavenProperties.Width, HavenLength.Px(296));
        tools.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        AgentsButton = MenuButton("Go.Add.Agents", "Agents", 144, iconKey: "agents");
        InstructionsButton = MenuButton("Go.Add.Instructions", "Instructions", 144, iconKey: "prompt");
        InstructionsButton.SetValue(HavenProperties.Column, 1);
        CapabilitiesButton = MenuButton("Go.Add.Capabilities", "Capabilities", 144, iconKey: "bolt");
        CapabilitiesButton.SetValue(HavenProperties.Row, 1);
        AppsButton = MenuButton("Go.Add.Apps", "Apps", 144, iconKey: "rocket");
        AppsButton.SetValue(HavenProperties.Row, 1);
        AppsButton.SetValue(HavenProperties.Column, 1);
        tools.Add(AgentsButton); tools.Add(InstructionsButton); tools.Add(CapabilitiesButton); tools.Add(AppsButton);
        menu.Add(tools);
        menu.Add(Heading("Options", 13));
        AllowActionsButton = MenuButton("Go.Add.AllowActions", "Allow Actions  ›", 296, iconKey: "bolt");
        VisualResponsesButton = MenuButton("Go.Add.VisualResponses", "Prefer Visual Responses  ›", 296, iconKey: "browse");
        menu.Add(AllowActionsButton);
        menu.Add(VisualResponsesButton);
        AttachFilesButton = MenuButton("Go.Add.AttachFiles", "Attach File(s)", 296, ButtonVariant.Primary, "file");
        menu.Add(AttachFilesButton);
        return menu;
    }

    private Container BuildCatalogPanel()
    {
        var panel = Card("Go.Add.Catalog", 320, HavenLayout.Vertical, 8);
        panel.SetValue(HavenProperties.Padding, HavenThickness.Parse("12px"));
        panel.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        CatalogSearch = new Input { Name = "Go.Add.Search", Placeholder = "Search" };
        CatalogSearch.Accessibility.AccessibleName = "Search Add catalogue";
        CatalogSearch.SetValue(HavenProperties.Height, HavenLength.Px(42));
        panel.Add(CatalogSearch);
        CatalogResults = new Container { Name = "Go.Add.Results", Layout = HavenLayout.Vertical };
        CatalogResults.SetValue(HavenProperties.MaxHeight, HavenLength.Px(300));
        CatalogResults.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        CatalogResults.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        panel.Add(CatalogResults);
        return panel;
    }

    private void OpenCatalogue(AddMenu.AddMenuAction action)
    {
        _catalogAction = action;
        _lastSearch = string.Empty;
        CatalogSearch.Text = string.Empty;
        CatalogSearch.Placeholder = action == AddMenu.AddMenuAction.Agent ? "Search Agents" : "Search";
        CatalogSearch.SetValue(HavenProperties.Visibility,
            action is AddMenu.AddMenuAction.AllowActions or AddMenu.AddMenuAction.VisualResponses ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        CatalogPanel.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
        AddActionSelected?.Invoke(this, action);
        RebuildCatalogue();
    }

    private void RebuildCatalogue()
    {
        foreach (var child in CatalogResults.Children.ToArray()) CatalogResults.Remove(child);
        _catalogRows.Clear();
        if (_catalogAction is null) return;
        var query = CatalogSearch.Text.Trim();
        switch (_catalogAction.Value)
        {
            case AddMenu.AddMenuAction.Agent:
                AddCatalogHeading("Current: No Agent (Default)");
                AddCatalogRows(AddMenu.AddMenuAction.Agent, _agents.Where(item => Matches(item.Name, item.Description, query)), item => item.Name, item => item.Description);
                break;
            case AddMenu.AddMenuAction.Capability:
                AddCatalogRows(AddMenu.AddMenuAction.Capability, _capabilities.Where(item => Matches(item.Name, item.Description, query)), item => item.Name, item => item.Description);
                break;
            case AddMenu.AddMenuAction.Instruction:
                AddCatalogRows(AddMenu.AddMenuAction.Instruction, _instructions.Where(item => Matches(item.Name, item.Description, query)), item => item.Name, item => item.Description);
                break;
            case AddMenu.AddMenuAction.App:
                AddCatalogRows(AddMenu.AddMenuAction.App, _apps.Where(item => Matches(item.Name, item.Description, query)), item => item.Name, item => item.Description);
                break;
            case AddMenu.AddMenuAction.AllowActions:
                AddModeRow("Allow All Actions", AddMenu.AddMenuAction.AllowActions, ChatActionMode.AllowAllActions);
                AddModeRow("Allow Basic Actions (Default)", AddMenu.AddMenuAction.AllowActions, ChatActionMode.AllowBasicActions);
                AddModeRow("Just Chat", AddMenu.AddMenuAction.AllowActions, ChatActionMode.JustChat);
                break;
            case AddMenu.AddMenuAction.VisualResponses:
                AddModeRow("Always Visual", AddMenu.AddMenuAction.VisualResponses, GenerativeUiResponseMode.AlwaysVisual);
                AddModeRow("Prefer Visual", AddMenu.AddMenuAction.VisualResponses, GenerativeUiResponseMode.PreferVisual);
                AddModeRow("Auto (Default)", AddMenu.AddMenuAction.VisualResponses, GenerativeUiResponseMode.Auto);
                AddModeRow("Prefer Text", AddMenu.AddMenuAction.VisualResponses, GenerativeUiResponseMode.PreferText);
                AddModeRow("Always Text", AddMenu.AddMenuAction.VisualResponses, GenerativeUiResponseMode.AlwaysText);
                break;
        }
    }

    private void AddCatalogRows<T>(AddMenu.AddMenuAction action, IEnumerable<T> items, Func<T, string> label, Func<T, string> description)
        where T : notnull
    {
        foreach (var item in items)
        {
            var button = MenuButton("Go.Add.Result", label(item), 284);
            button.Accessibility.Description = description(item);
            var boxed = (object)item;
            button.Invoked += (_, _) => SelectCatalogue(new AddMenuSelection(action, boxed));
            _catalogRows.Add((button, action, boxed));
            CatalogResults.Add(button);
        }
    }

    private void AddModeRow(string label, AddMenu.AddMenuAction action, object item)
    {
        var button = MenuButton("Go.Add.Result", label, 284);
        button.Invoked += (_, _) => SelectCatalogue(new AddMenuSelection(action, item));
        _catalogRows.Add((button, action, item));
        CatalogResults.Add(button);
    }

    private void AddCatalogHeading(string text)
    {
        var heading = new HavenText(text) { Level = TextLevel.Caption };
        heading.SetValue(HavenProperties.FontWeight, 700);
        heading.SetValue(HavenProperties.Foreground, "TextSecondary");
        CatalogResults.Add(heading);
    }

    private void SelectCatalogue(AddMenuSelection selection)
    {
        HideAddMenu();
        CatalogItemSelected?.Invoke(this, selection);
    }

    private void OnCatalogSearchInvalidated(object? sender, EventArgs e)
    {
        var next = CatalogSearch.Text;
        if (next == _lastSearch) return;
        _lastSearch = next;
        if (_catalogAction is not null) RebuildCatalogue();
    }

    private void OnAddInvoked(object? sender, EventArgs e) => ShowAddMenu();
    private void OnDismissInvoked(object? sender, EventArgs e) => HideAddMenu();
    private void OnAttachFilesInvoked(object? sender, EventArgs e)
    {
        HideAddMenu();
        AddActionSelected?.Invoke(this, AddMenu.AddMenuAction.File);
    }

    private static Container Card(string name, double width, HavenLayout layout, double gap)
    {
        var card = new Container { Name = name, Layout = layout };
        card.SetValue(HavenProperties.Width, HavenLength.Px(width));
        card.SetValue(HavenProperties.Background, "SurfaceRaised");
        card.SetValue(HavenProperties.BorderColor, "Border");
        card.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(18)));
        card.SetValue(HavenProperties.Shadow, "Card");
        card.SetValue(HavenProperties.Gap, HavenLength.Px(gap));
        return card;
    }

    private static HavenText Heading(string value, double size)
    {
        var text = new HavenText(value) { Level = TextLevel.Paragraph };
        text.SetValue(HavenProperties.FontSize, size);
        text.SetValue(HavenProperties.FontWeight, 800);
        return text;
    }

    private static HavenButton MenuButton(string name, string label, double width, ButtonVariant variant = ButtonVariant.Tertiary, string? iconKey = null)
    {
        var button = new HavenButton { Name = name, Content = label, Variant = variant, IconKey = iconKey ?? string.Empty };
        button.Accessibility.AccessibleName = label.Replace("  ›", string.Empty, StringComparison.Ordinal);
        button.SetValue(HavenProperties.Width, HavenLength.Px(width));
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(44));
        button.SetValue(HavenProperties.Padding, HavenThickness.Parse("10px 12px"));
        button.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(14)));
        button.SetValue(HavenProperties.FontSize, 12d);
        button.SetValue(HavenProperties.FontWeight, 700);
        return button;
    }

    private static HavenButton CreateIconButton(string name, string iconKey, int column, string accessibleName, out Icon icon)
    {
        var button = new HavenButton { Name = name, Variant = ButtonVariant.Icon };
        button.Accessibility.AccessibleName = accessibleName;
        button.SetValue(HavenProperties.Column, column);
        button.SetValue(HavenProperties.Width, HavenLength.Px(44));
        button.SetValue(HavenProperties.Height, HavenLength.Px(44));
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(44));
        button.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(22)));

        icon = new Icon { Name = name + ".Icon", Key = iconKey };
        icon.SetValue(HavenProperties.Column, column);
        icon.SetValue(HavenProperties.Width, HavenLength.Px(20));
        icon.SetValue(HavenProperties.Height, HavenLength.Px(20));
        icon.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        icon.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        icon.SetValue(HavenProperties.Foreground, "TextPrimary");
        icon.SetValue(HavenProperties.PointerEvents, HavenPointerEvents.None);
        icon.SetValue(HavenProperties.ZIndex, 1);
        return button;
    }

    private static bool Matches(string name, string description, string query) =>
        string.IsNullOrWhiteSpace(query)
        || name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || description.Contains(query, StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        AddButton.Invoked -= OnAddInvoked;
        DismissAddButton.Invoked -= OnDismissInvoked;
        AttachFilesButton.Invoked -= OnAttachFilesInvoked;
        CatalogSearch.Invalidated -= OnCatalogSearchInvalidated;
        foreach (var (element, handler) in _visualSubscriptions) element.Invalidated -= handler;
        _visualSubscriptions.Clear();
    }
}
