using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Haven.Desktop.Controls;
using Haven.Desktop.Events;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Shell.TopRail;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Go;

/// <summary>
/// New Haven's mockup-defined Go workspace. It owns no legacy chat, plugin,
/// instruction, agent, or voice controls.
/// </summary>
public sealed partial class GoPage : UserControl, IDisposable
{
    private readonly HavenEventBus _bus;
    private readonly Button[] _suggestionButtons;
    private readonly HavenIcon[] _suggestionIcons;
    private readonly TextBlock[] _suggestionTexts;
    private readonly TaskAttachmentContext _attachments = new();
    private IReadOnlyList<ModeDefinition> _availableApps = [];
    private IReadOnlyList<GoSuggestion> _suggestions = GoSuggestionService.ImmediateDefaults;
    private bool _disposed;

    public GoPage(HavenEventBus bus)
    {
        _bus = bus;
        InitializeComponent();
        _suggestionButtons = [RecentChatsButton, StudyButton, StudioButton, RecapButton];
        _suggestionIcons = [SuggestionIcon0, SuggestionIcon1, SuggestionIcon2, SuggestionIcon3];
        _suggestionTexts = [SuggestionText0, SuggestionText1, SuggestionText2, SuggestionText3];
        WireEvents();
        SetSuggestions(_suggestions);
        SizeChanged += (_, args) => ApplyResponsiveLayout(args.NewSize.Width);
    }

    public event EventHandler<string>? SubmitRequested;
    public event EventHandler? RefreshSuggestionsRequested;
    public event EventHandler<AddMenu.AddMenuAction>? AddRequested;
    public event EventHandler<AddMenuSelection>? AddCatalogItemSelected;
    public event EventHandler? Disposed;

    public void AttachFiles(IEnumerable<string> paths)
    {
        _attachments.AttachFiles(paths);
        RefreshAttachmentStatus();
    }

    public void AttachApp(ModeDefinition app)
    {
        _attachments.AttachApp(app);
        RefreshAttachmentStatus();
    }

    public bool IsCapabilityAttached(Guid capabilityId) => _attachments.IsCapabilityAttached(capabilityId);

    public void ToggleCapability(CapabilityDefinition capability)
    {
        if (_attachments.IsCapabilityAttached(capability.Id))
            _attachments.RemoveCapability(capability.Id);
        else
            AttachCapability(capability);
        RefreshAttachmentStatus();
    }

    private void AttachCapability(CapabilityDefinition capability)
    {
        var owner = _availableApps.FirstOrDefault(app =>
            app.Key.Equals(capability.OwnerAppKey, StringComparison.OrdinalIgnoreCase));
        _attachments.AttachCapability(capability, owner);
    }

    public TaskAttachmentSnapshot TakeAttachments()
    {
        var snapshot = _attachments.TakeSnapshot();
        RefreshAttachmentStatus();
        return snapshot;
    }

    public void FocusComposer() => InstructionBox.Focus();

    public void SetSuggestions(IReadOnlyList<GoSuggestion> suggestions)
    {
        if (_disposed || suggestions.Count != _suggestionButtons.Length) return;
        _suggestions = suggestions.ToArray();
        for (var index = 0; index < _suggestions.Count; index++)
        {
            var suggestion = _suggestions[index];
            _suggestionIcons[index].IconKey = suggestion.IconKey;
            _suggestionTexts[index].Text = suggestion.Label;
            ToolTip.SetTip(_suggestionButtons[index], suggestion.Instruction);
        }
    }

    private void ApplyResponsiveLayout(double width)
    {
        if (width <= 0) return;
        var compact = width < 680;

        RootGrid.Margin = compact ? new Avalonia.Thickness(18, 10, 18, 18) : new Avalonia.Thickness(42, 12, 42, 22);
        HeroStack.Spacing = compact ? 22 : 34;
        HeroStack.Margin = compact ? new Avalonia.Thickness(0, 0, 0, 14) : new Avalonia.Thickness(0, 0, 0, 32);
        HeroTitle.FontSize = compact ? 34 : 40;
        SuggestionsGrid.MaxWidth = compact ? double.PositiveInfinity : 800;
        SuggestionsGrid.Width = compact ? double.NaN : 780;
        SuggestionsGrid.ColumnDefinitions = new ColumnDefinitions(compact ? "*" : "*,*");
        SuggestionsGrid.RowDefinitions = new RowDefinitions(compact ? "Auto,Auto,Auto,Auto" : "Auto,Auto");
        SuggestionsGrid.ColumnSpacing = compact ? 0 : 18;
        SuggestionsGrid.RowSpacing = compact ? 10 : 14;

        PlaceSuggestion(RecentChatsButton, 0, 0);
        PlaceSuggestion(StudySuggestionScope, compact ? 1 : 0, compact ? 0 : 1);
        PlaceSuggestion(StudioSuggestionScope, compact ? 2 : 1, 0);
        PlaceSuggestion(RecapButton, compact ? 3 : 1, compact ? 0 : 1);
    }

    private static void PlaceSuggestion(Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
    }

    public void SetAddCatalogue(
        IReadOnlyList<AgentDefinition> agents,
        IReadOnlyList<CapabilityDefinition> capabilities,
        IReadOnlyList<PromptDefinition> instructions,
        IReadOnlyList<ModeDefinition> apps)
    {
        _availableApps = apps;
        AddButton.SetCatalogue(agents, capabilities, instructions, apps);
    }

    public void SetRefreshInProgress(bool inProgress)
    {
        if (_disposed) return;
        LoadMoreButton.IsEnabled = !inProgress;
        LoadMoreButton.Content = new TextBlock
        {
            Text = inProgress ? "Finding more…" : "Load more",
            Foreground = Avalonia.Application.Current?.Resources["HavenTextSoftBrush"] as IBrush
                         ?? new SolidColorBrush(Color.Parse("#FFD5D7E4")),
            FontSize = 12,
            FontWeight = FontWeight.SemiBold
        };
    }

    private void WireEvents()
    {
        Register("Go.Suggestions.RecentChats", RecentChatsButton);
        Register("Go.Suggestions.Study", StudyButton);
        Register("Go.Suggestions.Studio", StudioButton);
        Register("Go.Suggestions.Recap", RecapButton);
        Register("Go.Suggestions.LoadMore", LoadMoreButton);
        Register("Go.Composer.Instruction", InstructionBox);
        Register("Go.Composer.Send", SendButton);
        Register("Go.Composer.Add", AddButton);

        RecentChatsButton.Click += (_, _) => SubmitSuggestion(0);
        StudyButton.Click += (_, _) => SubmitSuggestion(1);
        StudioButton.Click += (_, _) => SubmitSuggestion(2);
        RecapButton.Click += (_, _) => SubmitSuggestion(3);
        LoadMoreButton.Click += (_, _) =>
        {
            _bus.Fire("Go.Suggestions.LoadMore.Click");
            RefreshSuggestionsRequested?.Invoke(this, EventArgs.Empty);
        };
        SendButton.Click += (_, _) => Submit();
        InstructionBox.KeyDown += OnInstructionKeyDown;
        AddButton.ActionSelected += (_, action) => AddRequested?.Invoke(this, action);
        AddButton.CatalogItemSelected += (_, selection) =>
        {
            if (selection.Item is ModeDefinition app) AttachApp(app);
            if (selection.Item is CapabilityDefinition capability) ToggleCapability(capability);
            AddCatalogItemSelected?.Invoke(this, selection);
        };
    }

    private void SubmitSuggestion(int index)
    {
        if (_disposed || index < 0 || index >= _suggestions.Count) return;
        _bus.Fire($"Go.Suggestions.Item{index}.Click");
        SubmitRequested?.Invoke(this, _suggestions[index].Instruction);
    }

    private void OnInstructionKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
        e.Handled = true;
        Submit();
    }

    private void Submit()
    {
        var instruction = InstructionBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(instruction)) return;
        InstructionBox.Text = string.Empty;
        _bus.Fire("Go.Composer.Send.Click");
        SubmitRequested?.Invoke(this, instruction);
    }

    private void Register(string name, Control control)
    {
        _bus.RegisterElement(name, control);
        _bus.WirePointerEvents(name, control);
    }

    private void RefreshAttachmentStatus()
    {
        if (_attachments.IsEmpty)
        {
            AttachmentStatusHost.IsVisible = false;
            AttachmentStatusText.Text = string.Empty;
            return;
        }

        var parts = new List<string>();
        if (_attachments.Apps.Count > 0)
            parts.Add("Apps: " + string.Join(", ", _attachments.Apps.Select(item => item.Name)));
        if (_attachments.Capabilities.Count > 0)
            parts.Add("Capabilities: " + string.Join(", ", _attachments.Capabilities.Select(item => item.Name)));
        if (_attachments.Files.Count > 0)
            parts.Add("Files: " + string.Join(", ", _attachments.Files.Select(Path.GetFileName)));
        AttachmentStatusText.Text = string.Join("  •  ", parts);
        AttachmentStatusHost.IsVisible = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        AddButton.Dispose();
        Disposed?.Invoke(this, EventArgs.Empty);
    }
}
