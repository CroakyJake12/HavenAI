using Haven.Core;
using Haven.Desktop.Prefabs;
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
    private readonly List<(HavenElement Element, EventHandler Handler)> _visualSubscriptions = [];
    private readonly HavenPrefabCatalog _prefabs;
    private readonly ChatAddMenuSurface _addMenu;
    private bool _disposed;

    public GoHavenScene()
    {
        _prefabs = HavenPrefabCatalog.FromAssembly(typeof(GoHavenScene).Assembly);
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
        LoadMoreButton.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
        LoadMoreButton.Accessibility.AccessibleName = "Load more Go suggestions";
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

        Chatbox = _prefabs.Create("Chatbox", "Go-Chatbox");
        Chatbox.SetValue(HavenProperties.Row, 3);
        Chatbox.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Chatbox.SetValue(HavenProperties.MaxWidth, HavenLength.Px(900));
        Chatbox.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        Composer = Chatbox.GetComponent<Container>("ChatboxRoot");
        Instruction = Chatbox.GetComponent<Input>("Instruction");
        AddButton = Chatbox.GetComponent<HavenButton>("AddMenu");
        SendButton = Chatbox.GetComponent<HavenButton>("Send");
        Root.Add(Chatbox);

        AddMenuPrefab = _prefabs.Create("ChatAddMenu", "Go-AddMenu");
        AddMenuPrefab.SetValue(HavenProperties.Row, 0);
        AddMenuPrefab.SetValue(HavenProperties.RowSpan, 4);
        AddMenuPrefab.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        AddMenuPrefab.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        AddMenuPrefab.SetValue(HavenProperties.ZIndex, 100);
        AddMenuPrefab.SetValue(HavenProperties.PointerEvents, HavenPointerEvents.ChildrenOnly);
        Root.Add(AddMenuPrefab);
        _addMenu = new ChatAddMenuSurface(AddMenuPrefab);

        AddButton.Invoked += OnAddInvoked;
        _addMenu.AddActionSelected += OnSharedAddActionSelected;
        _addMenu.CatalogItemSelected += OnSharedCatalogItemSelected;
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
    public Prefab Chatbox { get; }
    public Prefab AddMenuPrefab { get; }
    public Container Composer { get; }
    public Input Instruction { get; }
    public HavenButton AddButton { get; }
    public HavenButton SendButton { get; }
    public Container AddOverlay => _addMenu.Overlay;
    public HavenButton DismissAddButton => _addMenu.DismissButton;
    public Container MainMenu => _addMenu.MainMenu;
    public Container CatalogPanel => _addMenu.CatalogPanel;
    public Input CatalogSearch => _addMenu.CatalogSearch;
    public Container CatalogResults => _addMenu.CatalogResults;
    public HavenButton AttachFilesButton => _addMenu.AttachFilesButton;
    public HavenButton AgentsButton => _addMenu.AgentsButton;
    public HavenButton InstructionsButton => _addMenu.InstructionsButton;
    public HavenButton CapabilitiesButton => _addMenu.CapabilitiesButton;
    public HavenButton AppsButton => _addMenu.AppsButton;
    public HavenButton AllowActionsButton => _addMenu.AllowActionsButton;
    public HavenButton VisualResponsesButton => _addMenu.VisualResponsesButton;

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
            foreach (var pill in Root.DescendantsAndSelf().OfType<Container>().Where(element =>
                         element.Name == $"Go.Suggestions.Item{index}.IconPill.Wide"
                         || element.Name == $"Go.Suggestions.Item{index}.IconPill.Compact"))
                pill.SetValue(HavenProperties.Background, AccentTokenForColour(suggestion.Colour));
        }
    }

    internal static string AccentTokenForColour(string colour)
    {
        if (colour is not { Length: 7 } || colour[0] != '#') return "Accent";
        if (!int.TryParse(colour.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var red)
            || !int.TryParse(colour.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var green)
            || !int.TryParse(colour.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var blue))
            return "Accent";

        if (blue >= red && blue >= green) return "AccentSecondary";
        if (green >= red && green >= blue) return "AccentMuted";
        return "Accent";
    }

    public void SetCatalogue(
        IReadOnlyList<AgentDefinition> agents,
        IReadOnlyList<CapabilityDefinition> capabilities,
        IReadOnlyList<PromptDefinition> instructions,
        IReadOnlyList<ModeDefinition> apps) =>
        _addMenu.SetCatalogue(agents, capabilities, instructions, apps);

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

    public void ShowAddMenu() => _addMenu.Show();

    public void HideAddMenu() => _addMenu.Hide();

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

    private void OnAddInvoked(object? sender, EventArgs e) => ShowAddMenu();

    private void OnSharedAddActionSelected(object? sender, AddMenu.AddMenuAction action) =>
        AddActionSelected?.Invoke(this, action);

    private void OnSharedCatalogItemSelected(object? sender, AddMenuSelection selection) =>
        CatalogItemSelected?.Invoke(this, selection);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        AddButton.Invoked -= OnAddInvoked;
        _addMenu.AddActionSelected -= OnSharedAddActionSelected;
        _addMenu.CatalogItemSelected -= OnSharedCatalogItemSelected;
        _addMenu.Dispose();
        foreach (var (element, handler) in _visualSubscriptions) element.Invalidated -= handler;
        _visualSubscriptions.Clear();
    }
}
