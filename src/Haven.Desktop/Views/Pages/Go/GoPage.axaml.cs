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
    private IReadOnlyList<GoSuggestion> _suggestions = GoSuggestionService.ImmediateDefaults;
    private bool _disposed;

    public GoPage(HavenEventBus bus)
    {
        _bus = bus;
        InitializeComponent();
        _suggestionButtons = [RecentChatsButton, TeachingButton, StudioButton, RecapButton];
        _suggestionIcons = [SuggestionIcon0, SuggestionIcon1, SuggestionIcon2, SuggestionIcon3];
        _suggestionTexts = [SuggestionText0, SuggestionText1, SuggestionText2, SuggestionText3];
        WireEvents();
        SetSuggestions(_suggestions);
    }

    public event EventHandler<string>? SubmitRequested;
    public event EventHandler? RefreshSuggestionsRequested;
    public event EventHandler<AddMenu.AddMenuAction>? AddRequested;
    public event EventHandler<AddMenuSelection>? AddCatalogItemSelected;
    public event EventHandler? Disposed;

    public void FocusComposer() => InstructionBox.Focus();

    public void SetSuggestions(IReadOnlyList<GoSuggestion> suggestions)
    {
        if (_disposed || suggestions.Count != _suggestionButtons.Length) return;
        _suggestions = suggestions.ToArray();
        for (var index = 0; index < _suggestions.Count; index++)
        {
            var suggestion = _suggestions[index];
            var brush = new SolidColorBrush(Color.Parse(suggestion.Colour));
            _suggestionIcons[index].IconKey = suggestion.IconKey;
            _suggestionIcons[index].Foreground = brush;
            _suggestionTexts[index].Text = suggestion.Label;
            _suggestionTexts[index].Foreground = brush;
            ToolTip.SetTip(_suggestionButtons[index], suggestion.Instruction);
        }
    }

    public void SetAddCatalogue(
        IReadOnlyList<AgentDefinition> agents,
        IReadOnlyList<PluginDefinition> plugins,
        IReadOnlyList<PromptDefinition> instructions,
        IReadOnlyList<ModeDefinition> apps) =>
        AddButton.SetCatalogue(agents, plugins, instructions, apps);

    public void SetRefreshInProgress(bool inProgress)
    {
        if (_disposed) return;
        LoadMoreButton.IsEnabled = !inProgress;
        LoadMoreButton.Content = new TextBlock
        {
            Text = inProgress ? "Finding More…" : "Load More",
            Foreground = new SolidColorBrush(Color.Parse("#111111")),
            FontSize = 14,
            FontWeight = FontWeight.ExtraBold,
            FontStyle = FontStyle.Italic
        };
    }

    private void WireEvents()
    {
        Register("Go.Suggestions.RecentChats", RecentChatsButton);
        Register("Go.Suggestions.Teaching", TeachingButton);
        Register("Go.Suggestions.Studio", StudioButton);
        Register("Go.Suggestions.Recap", RecapButton);
        Register("Go.Suggestions.LoadMore", LoadMoreButton);
        Register("Go.Composer.Instruction", InstructionBox);
        Register("Go.Composer.Send", SendButton);
        Register("Go.Composer.Add", AddButton);

        RecentChatsButton.Click += (_, _) => SubmitSuggestion(0);
        TeachingButton.Click += (_, _) => SubmitSuggestion(1);
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
        AddButton.CatalogItemSelected += (_, selection) => AddCatalogItemSelected?.Invoke(this, selection);
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        AddButton.Dispose();
        Disposed?.Invoke(this, EventArgs.Empty);
    }
}
