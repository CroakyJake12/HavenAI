using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Events;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views.Pages.Browser;

/// <summary>
/// Browser page shell. Owns all browser state, commands, and the BrowserView,
/// and wires pointer events through the HavenEventBus.
/// </summary>
public sealed partial class BrowserPage : UserControl, IDisposable
{
    public new event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<bool>? ImportExtensionRequested;

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void RaisePropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private readonly HavenEventBus _bus;
    private readonly BrowserSessionService _browser;
    private readonly BrowserDataService _data;
    private readonly IOllamaClient _ollama;
    private readonly UserPreferencesService _preferences;
    private readonly BrowserToolRuntime _browserTools;
    private readonly BrowserHavenScene _havenScene;
    private readonly BrowserNativeWebResolver _nativeWebResolver;
    private readonly HavenSceneControl _sceneControl;
    private BrowserTabViewModel? _selectedTab;
    private string _address;
    private string _status;
    private bool _isLoading;
    private bool _canGoBack;
    private bool _canGoForward;
    private string _bookmarkGroup = "Bookmarks";
    private string _newGroupName = "Research";
    private string _assistantInput = string.Empty;
    private string _assistantOutput = "Ask Haven to summarise, explain, or extract information from this page.";
    private bool _isBookmarksOpen;
    private bool _isHistoryOpen;
    private bool _isSettingsOpen;
    private bool _isExtensionsOpen;
    private bool _isLoginsOpen;
    private bool _isAssistantOpen;
    private string _homePage;
    private string _searchTemplate;
    private bool _saveHistory;
    private bool _offerToSaveLogins;
    private bool _restoreTabs;
    private bool _enableExtensions;
    private bool _verticalTabs;
    private string _loginOrigin = string.Empty;
    private string _loginUsername = string.Empty;
    private string _loginPassword = string.Empty;
    private bool _disposed;

    public BrowserPage(
        HavenEventBus bus,
        BrowserSessionService browser,
        BrowserDataService data,
        IOllamaClient ollama,
        UserPreferencesService preferences)
    {
        _bus = bus;
        _browser = browser;
        _data = data;
        _ollama = ollama;
        _preferences = preferences;
        _browserTools = new BrowserToolRuntime(browser);
        InitializeResearch();
        var settings = data.Settings;
        _homePage = settings.HomePage;
        _searchTemplate = settings.SearchTemplate;
        _saveHistory = settings.SaveHistory;
        _offerToSaveLogins = settings.OfferToSaveLogins;
        _restoreTabs = settings.RestoreTabs;
        _enableExtensions = settings.EnableExtensions;
        _verticalTabs = settings.VerticalTabs;
        _address = settings.HomePage;
        _status = browser.State.Status;

        NavigateCommand = new AsyncRelayCommand(NavigateSafelyAsync);
        BackCommand = new AsyncRelayCommand(
            () => RunSafelyAsync(async () => _ = await _browser.BackAsync(CancellationToken.None)),
            () => CanGoBack);
        ForwardCommand = new AsyncRelayCommand(
            () => RunSafelyAsync(async () => _ = await _browser.ForwardAsync(CancellationToken.None)),
            () => CanGoForward);
        ReloadCommand = new AsyncRelayCommand(() => RunSafelyAsync(() => _browser.ReloadAsync(CancellationToken.None)));
        HardReloadCommand = new AsyncRelayCommand(() => RunSafelyAsync(async () => _ = await _browser.ReloadAsync(true, CancellationToken.None)));
        StopCommand = new AsyncRelayCommand(() => RunSafelyAsync(() => _browser.StopAsync(CancellationToken.None)));
        HomeCommand = new AsyncRelayCommand(async () => { Address = HomePage; await NavigateSafelyAsync(); });
        NewTabCommand = new AsyncRelayCommand(() => AddTabAsync(false));
        NewPrivateTabCommand = new AsyncRelayCommand(() => AddTabAsync(true));
        CloseTabCommand = new AsyncRelayCommand<BrowserTabViewModel>(CloseTabAsync);
        SelectTabCommand = new AsyncRelayCommand<BrowserTabViewModel>(SelectTabAsync);
        AddBookmarkCommand = new AsyncRelayCommand(AddBookmarkAsync);
        ToggleBookmarkCommand = new AsyncRelayCommand(ToggleBookmarkAsync);
        RemoveBookmarkCommand = new AsyncRelayCommand<BrowserBookmark>(RemoveBookmarkAsync);
        OpenBookmarkCommand = new AsyncRelayCommand<BrowserBookmark>(OpenBookmarkAsync);
        OpenHistoryCommand = new AsyncRelayCommand<BrowserHistoryEntry>(OpenHistoryAsync);
        ClearHistoryCommand = new AsyncRelayCommand(ClearHistoryAsync);
        CreateTabGroupCommand = new AsyncRelayCommand(CreateTabGroupAsync);
        PrintCommand = new AsyncRelayCommand(() => RunSafelyAsync(() => _browser.PrintAsync(CancellationToken.None)));
        InspectCommand = new AsyncRelayCommand(() => RunSafelyAsync(() => _browser.OpenDeveloperToolsAsync(CancellationToken.None)));
        AskAssistantCommand = new AsyncRelayCommand(AskAssistantAsync, () => !string.IsNullOrWhiteSpace(AssistantInput));
        SummariseCommand = new AsyncRelayCommand(() => AskAssistantAsync("Summarise this page. Include the key claims and any action items."));
        SaveBrowserSettingsCommand = new AsyncRelayCommand(SaveBrowserSettingsAsync);
        ToggleExtensionCommand = new AsyncRelayCommand<BrowserExtensionDefinition>(ToggleExtensionAsync);
        DeleteExtensionCommand = new AsyncRelayCommand<BrowserExtensionDefinition>(DeleteExtensionAsync);
        SaveLoginCommand = new AsyncRelayCommand(SaveLoginAsync);
        DeleteLoginCommand = new AsyncRelayCommand<SavedLogin>(DeleteLoginAsync);
        AutofillLoginCommand = new AsyncRelayCommand<SavedLogin>(AutofillLoginAsync);
        ToggleBookmarksCommand = new RelayCommand(() => TogglePanel(nameof(IsBookmarksOpen)));
        ToggleHistoryCommand = new RelayCommand(() => TogglePanel(nameof(IsHistoryOpen)));
        ToggleSettingsCommand = new RelayCommand(() => TogglePanel(nameof(IsSettingsOpen)));
        ToggleExtensionsCommand = new RelayCommand(() => TogglePanel(nameof(IsExtensionsOpen)));
        ToggleLoginsCommand = new RelayCommand(() => TogglePanel(nameof(IsLoginsOpen)));
        ToggleAssistantCommand = new RelayCommand(() => TogglePanel(nameof(IsAssistantOpen)));
        ImportExtensionRequestedCommand = new RelayCommand(() => ImportExtensionRequested?.Invoke(this, false));
        ConvertChromeExtensionRequestedCommand = new RelayCommand(() => ImportExtensionRequested?.Invoke(this, true));

        RefreshCollections();
        RestoreSavedTabs();
        _browser.StateChanged += OnStateChanged;

        _havenScene = new BrowserHavenScene(this);
        _nativeWebResolver = new BrowserNativeWebResolver(this, _havenScene.WebSurface);
        _sceneControl = new HavenSceneControl(new HavenAvaloniaImageResolver(), _nativeWebResolver)
        {
            Root = _havenScene.Root
        };
        DataContext = this;
        Content = _sceneControl;
        InitializeComponent();
        WireEvents();
    }

    private void WireEvents()
    {
        _bus.RegisterElement("Browser.View", _sceneControl);
        _bus.WirePointerEvents("Browser.View", _sceneControl);
        _sceneControl.InputSubmitted += _havenScene.HandleInputSubmitted;
        WireBrowserShortcuts();
        ImportExtensionRequested += OnImportExtensionRequested;
    }

    public BrowserSessionService Browser => _browser;
    public ObservableCollection<BrowserTabViewModel> Tabs { get; } = [];
    public ObservableCollection<BrowserBookmark> Bookmarks { get; } = [];
    public ObservableCollection<BrowserHistoryEntry> History { get; } = [];
    public ObservableCollection<BrowserExtensionDefinition> Extensions { get; } = [];
    public ObservableCollection<SavedLogin> Logins { get; } = [];
    public ObservableCollection<string> TabGroups { get; } = [];

    public BrowserTabViewModel? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (ReferenceEquals(_selectedTab, value) || value is null) return;
            if (_selectedTab is not null) _selectedTab.IsSelected = false;
            if (!SetProperty(ref _selectedTab, value)) return;
            value.IsSelected = true;
            Address = value.Address;
            RaisePropertyChanged(nameof(IsPrivate));
            RaisePropertyChanged(nameof(PrivacyLabel));
        }
    }

    public string Address { get => _address; set => SetProperty(ref _address, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }
    public bool CanGoBack { get => _canGoBack; private set => SetProperty(ref _canGoBack, value); }
    public bool CanGoForward { get => _canGoForward; private set => SetProperty(ref _canGoForward, value); }
    public string BookmarkGroup { get => _bookmarkGroup; set => SetProperty(ref _bookmarkGroup, value); }
    public bool HasBookmarks => Bookmarks.Count > 0;
    public bool HasNoBookmarks => !HasBookmarks;
    public string BookmarkSummary => Bookmarks.Count == 1 ? "1 saved bookmark" : $"{Bookmarks.Count} saved bookmarks";
    public string NewGroupName { get => _newGroupName; set => SetProperty(ref _newGroupName, value); }
    public string AssistantInput { get => _assistantInput; set { if (SetProperty(ref _assistantInput, value)) AskAssistantCommand.RaiseCanExecuteChanged(); } }
    public string AssistantOutput { get => _assistantOutput; private set => SetProperty(ref _assistantOutput, value); }
    public bool IsBookmarksOpen { get => _isBookmarksOpen; private set => SetProperty(ref _isBookmarksOpen, value); }
    public bool IsHistoryOpen { get => _isHistoryOpen; private set => SetProperty(ref _isHistoryOpen, value); }
    public bool IsSettingsOpen { get => _isSettingsOpen; private set => SetProperty(ref _isSettingsOpen, value); }
    public bool IsExtensionsOpen { get => _isExtensionsOpen; private set => SetProperty(ref _isExtensionsOpen, value); }
    public bool IsLoginsOpen { get => _isLoginsOpen; private set => SetProperty(ref _isLoginsOpen, value); }
    public bool IsAssistantOpen { get => _isAssistantOpen; private set => SetProperty(ref _isAssistantOpen, value); }
    public bool IsAnyPanelOpen => IsBookmarksOpen || IsHistoryOpen || IsSettingsOpen || IsExtensionsOpen || IsLoginsOpen || IsAssistantOpen || IsResearchOpen;
    public bool IsPrivate => SelectedTab?.IsPrivate == true;
    public string PrivacyLabel => IsPrivate ? "Private tab - history and tab state are not saved" : "Standard tab";
    public string HomePage { get => _homePage; set => SetProperty(ref _homePage, value); }
    public string SearchTemplate { get => _searchTemplate; set => SetProperty(ref _searchTemplate, value); }
    public bool SaveHistory { get => _saveHistory; set => SetProperty(ref _saveHistory, value); }
    public bool OfferToSaveLogins { get => _offerToSaveLogins; set => SetProperty(ref _offerToSaveLogins, value); }
    public bool RestoreTabs { get => _restoreTabs; set => SetProperty(ref _restoreTabs, value); }
    public bool EnableExtensions { get => _enableExtensions; set => SetProperty(ref _enableExtensions, value); }
    public bool VerticalTabs
    {
        get => _verticalTabs;
        set
        {
            if (!SetProperty(ref _verticalTabs, value)) return;
            RaisePropertyChanged(nameof(HorizontalTabs));
        }
    }
    public bool HorizontalTabs => !VerticalTabs;
    public string LoginOrigin { get => _loginOrigin; set => SetProperty(ref _loginOrigin, value); }
    public string LoginUsername { get => _loginUsername; set => SetProperty(ref _loginUsername, value); }
    public string LoginPassword { get => _loginPassword; set => SetProperty(ref _loginPassword, value); }

    public AsyncRelayCommand NavigateCommand { get; }
    public AsyncRelayCommand BackCommand { get; }
    public AsyncRelayCommand ForwardCommand { get; }
    public AsyncRelayCommand ReloadCommand { get; }
    public AsyncRelayCommand HardReloadCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public AsyncRelayCommand HomeCommand { get; }
    public AsyncRelayCommand NewTabCommand { get; }
    public AsyncRelayCommand NewPrivateTabCommand { get; }
    public AsyncRelayCommand<BrowserTabViewModel> CloseTabCommand { get; }
    public AsyncRelayCommand<BrowserTabViewModel> SelectTabCommand { get; }
    public AsyncRelayCommand AddBookmarkCommand { get; }
    public AsyncRelayCommand ToggleBookmarkCommand { get; }
    public AsyncRelayCommand<BrowserBookmark> RemoveBookmarkCommand { get; }
    public AsyncRelayCommand<BrowserBookmark> OpenBookmarkCommand { get; }
    public AsyncRelayCommand<BrowserHistoryEntry> OpenHistoryCommand { get; }
    public AsyncRelayCommand ClearHistoryCommand { get; }
    public AsyncRelayCommand CreateTabGroupCommand { get; }
    public AsyncRelayCommand PrintCommand { get; }
    public AsyncRelayCommand InspectCommand { get; }
    public AsyncRelayCommand AskAssistantCommand { get; }
    public AsyncRelayCommand SummariseCommand { get; }
    public AsyncRelayCommand SaveBrowserSettingsCommand { get; }
    public AsyncRelayCommand<BrowserExtensionDefinition> ToggleExtensionCommand { get; }
    public AsyncRelayCommand<BrowserExtensionDefinition> DeleteExtensionCommand { get; }
    public AsyncRelayCommand SaveLoginCommand { get; }
    public AsyncRelayCommand<SavedLogin> DeleteLoginCommand { get; }
    public AsyncRelayCommand<SavedLogin> AutofillLoginCommand { get; }
    public RelayCommand ToggleBookmarksCommand { get; }
    public RelayCommand ToggleHistoryCommand { get; }
    public RelayCommand ToggleSettingsCommand { get; }
    public RelayCommand ToggleExtensionsCommand { get; }
    public RelayCommand ToggleLoginsCommand { get; }
    public RelayCommand ToggleAssistantCommand { get; }
    public RelayCommand ImportExtensionRequestedCommand { get; }
    public RelayCommand ConvertChromeExtensionRequestedCommand { get; }

    public Task NavigateSafelyAsync() => RunSafelyAsync(async () =>
    {
        Status = await _browser.NavigateAsync(Address, CancellationToken.None);
    });

    public Task ImportExtensionAsync(string path, bool convertChrome) => RunSafelyAsync(async () =>
    {
        var extension = convertChrome
            ? await _data.ConvertChromeExtensionAsync(path, CancellationToken.None)
            : await _data.ImportHavenExtensionAsync(path, CancellationToken.None);
        RefreshCollections();
        Status = $"Imported {extension.Name}. Review it, then enable it explicitly.";
    });

    public void ReportBrowserError(Exception exception) => Status = $"Browser unavailable: {exception.Message}";

    private void RestoreSavedTabs()
    {
        var stored = RestoreTabs ? _data.Tabs : [];
        foreach (var tab in stored)
            Tabs.Add(new BrowserTabViewModel(tab.Id, tab.Title, tab.Address, tab.Privacy == BrowserTabPrivacy.Private, tab.Group));
        if (Tabs.Count == 0) Tabs.Add(new BrowserTabViewModel(Guid.NewGuid(), "New tab", HomePage, false, string.Empty));
        _selectedTab = Tabs[0];
        _selectedTab.IsSelected = true;
        Address = _selectedTab.Address;
        RaisePropertyChanged(nameof(SelectedTab));
    }

    private async Task AddTabAsync(bool isPrivate)
    {
        var tab = new BrowserTabViewModel(Guid.NewGuid(), isPrivate ? "Private tab" : "New tab", HomePage, isPrivate, string.Empty);
        Tabs.Add(tab);
        SelectedTab = tab;
        await SaveTabsAsync();
    }

    internal async Task OpenPopupInNewTabAsync(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var assessment = BrowserNativeRequestPolicy.AssessTopLevel(address);
        if (!assessment.IsAllowed || address.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("Popup target was rejected: " + assessment.Reason);

        var current = SelectedTab;
        var tab = new BrowserTabViewModel(
            Guid.NewGuid(),
            address.Host,
            address.ToString(),
            current?.IsPrivate == true,
            current?.Group ?? string.Empty);
        Tabs.Add(tab);
        SelectedTab = tab;
        await SaveTabsAsync();
    }

    private async Task CloseTabAsync(BrowserTabViewModel? tab)
    {
        if (tab is null) return;
        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        if (Tabs.Count == 0) Tabs.Add(new BrowserTabViewModel(Guid.NewGuid(), "New tab", HomePage, false, string.Empty));
        SelectedTab = Tabs[Math.Clamp(index, 0, Tabs.Count - 1)];
        await SaveTabsAsync();
    }

    private Task SelectTabAsync(BrowserTabViewModel? tab)
    {
        if (tab is not null) SelectedTab = tab;
        return Task.CompletedTask;
    }

    private Task AddBookmarkAsync() => RunSafelyAsync(async () =>
    {
        await _data.AddBookmarkAsync(SelectedTab?.Title ?? Address, Address, BookmarkGroup, CancellationToken.None);
        RefreshCollections();
        Status = $"Bookmark saved locally in {NormalizedBookmarkGroup}.";
    });

    private Task ToggleBookmarkAsync() => RunSafelyAsync(async () =>
    {
        var added = await _data.ToggleBookmarkAsync(SelectedTab?.Title ?? Address, Address, BookmarkGroup, CancellationToken.None);
        RefreshCollections();
        Status = added
            ? $"Bookmark saved locally in {NormalizedBookmarkGroup}."
            : "Bookmark removed locally.";
    });

    private Task RemoveBookmarkAsync(BrowserBookmark? bookmark)
    {
        if (bookmark is null) return Task.CompletedTask;
        return RunSafelyAsync(async () =>
        {
            await _data.RemoveBookmarkAsync(bookmark.Id, CancellationToken.None);
            RefreshCollections();
            Status = $"Removed bookmark for {bookmark.Title}.";
        });
    }

    private async Task OpenBookmarkAsync(BrowserBookmark? bookmark)
    {
        if (bookmark is null) return;
        Address = bookmark.Address;
        await NavigateSafelyAsync();
    }

    private async Task OpenHistoryAsync(BrowserHistoryEntry? entry)
    {
        if (entry is null) return;
        Address = entry.Address;
        await NavigateSafelyAsync();
    }

    private async Task ClearHistoryAsync()
    {
        await _data.ClearHistoryAsync(CancellationToken.None);
        RefreshCollections();
        Status = "Browser history cleared.";
    }

    private async Task CreateTabGroupAsync()
    {
        if (SelectedTab is null || string.IsNullOrWhiteSpace(NewGroupName)) return;
        SelectedTab.Group = NewGroupName.Trim();
        RefreshGroups();
        await SaveTabsAsync();
        Status = $"Added this tab to {SelectedTab.Group}.";
    }

    private Task AskAssistantAsync() => AskAssistantAsync(AssistantInput);

    private Task AskAssistantAsync(string instruction) => RunSafelyAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(instruction)) return;
        if (!_preferences.BrowserSideAssistant) throw new InvalidOperationException("The browser side assistant is disabled in Settings.");
        AssistantOutput = "Inspecting the current tab...";
        var models = await _ollama.GetModelsAsync(CancellationToken.None);
        var model = _preferences.DefaultModel;
        var selected = models.FirstOrDefault(item => item.Name.Equals(model, StringComparison.OrdinalIgnoreCase)) ?? models.FirstOrDefault();
        if (selected is null) throw new InvalidOperationException("Install or select a local model before using the side assistant.");
        if (_preferences.AutoSwitchCompatibleModels && !selected.Supports(ToolCapability.Tools) && !selected.Supports(ToolCapability.Browser))
            selected = models.FirstOrDefault(item => item.Supports(ToolCapability.Tools) || item.Supports(ToolCapability.Browser)) ?? selected;

        if (selected.Supports(ToolCapability.Tools) || selected.Supports(ToolCapability.Browser))
        {
            var turns = new List<OllamaToolTurn> { new("user", $"Current tab: {Address}\nTask: {instruction}") };
            var activity = new StringBuilder();
            for (var step = 1; step <= 12; step++)
            {
                var response = await _ollama.ChatWithToolsAsync(new OllamaToolRequest(selected.Name, turns, _browserTools.Definitions,
                    _preferences.DefaultEffort,
                    "You are Haven Browse's side assistant. Inspect the page with browser_read_page and use the bounded browser tools to complete the user's request. Work step by step and verify after navigation or clicks. Never submit a purchase, delete data, expose credentials, or bypass a warning. Do not claim an action succeeded without its tool result. When finished, return a concise result.",
                    _preferences.GenerationOptions), CancellationToken.None);
                if (response.ToolCalls.Count == 0)
                {
                    AssistantOutput = activity + (string.IsNullOrWhiteSpace(response.Content) ? "Finished without a final explanation." : response.Content);
                    return;
                }
                turns.Add(new OllamaToolTurn("assistant", response.Content, response.ToolCalls));
                foreach (var call in response.ToolCalls)
                {
                    var result = await _browserTools.ExecuteAsync(call, CancellationToken.None);
                    activity.Append(result.Activity.Succeeded ? "\u2713 " : "! ").Append(result.Activity.Title).Append(": ").AppendLine(result.Activity.Detail);
                    AssistantOutput = activity.ToString();
                    var toolOutput = result.Output.Length > 36_000 ? result.Output[..36_000] + "\n[truncated for model context]" : result.Output;
                    turns.Add(new OllamaToolTurn("tool", toolOutput, ToolName: call.Name));
                }
            }
            AssistantOutput = activity + "Stopped after the 12-action browser safety limit. Continue with a narrower request if needed.";
            return;
        }

        var page = await _browser.ExtractVisibleTextAsync(CancellationToken.None);
        if (page.Length > 32_000) page = page[..32_000];
        AssistantOutput = await _ollama.CompleteAsync(new OllamaChatRequest(selected.Name,
            [new OllamaMessage("user", $"Task: {instruction}\n\nPage URL: {Address}\n\nVisible page text:\n{page}")],
            _preferences.DefaultEffort,
            "You are Haven's browser side assistant. Use only the supplied page text, state uncertainty, and explain that this model cannot interact with the page."), CancellationToken.None);
    });

    private Task SaveBrowserSettingsAsync() => RunSafelyAsync(async () =>
    {
        await _data.SaveSettingsAsync(new BrowserSettings(HomePage.Trim(), SearchTemplate.Trim(), SaveHistory, OfferToSaveLogins,
            RestoreTabs, EnableExtensions, VerticalTabs), CancellationToken.None);
        Status = "Browser settings saved locally.";
    });

    private async Task ToggleExtensionAsync(BrowserExtensionDefinition? extension)
    {
        if (extension is null) return;
        await _data.SetExtensionEnabledAsync(extension.Id, !extension.IsEnabled, CancellationToken.None);
        RefreshCollections();
    }

    private async Task DeleteExtensionAsync(BrowserExtensionDefinition? extension)
    {
        if (extension is null) return;
        await _data.DeleteExtensionAsync(extension.Id, CancellationToken.None);
        RefreshCollections();
    }

    private Task SaveLoginAsync() => RunSafelyAsync(async () =>
    {
        await _data.SaveLoginAsync(LoginOrigin, LoginUsername, LoginPassword, CancellationToken.None);
        LoginPassword = string.Empty;
        RefreshCollections();
        Status = "Login stored in Windows Credential Manager. Haven saved only its origin and username metadata.";
    });

    private async Task DeleteLoginAsync(SavedLogin? login)
    {
        if (login is null) return;
        await _data.DeleteLoginAsync(login, CancellationToken.None);
        RefreshCollections();
    }

    private Task AutofillLoginAsync(SavedLogin? login) => RunSafelyAsync(async () =>
    {
        if (login is null) return;
        if (!Uri.TryCreate(Address, UriKind.Absolute, out var address) || !address.GetLeftPart(UriPartial.Authority).Equals(login.Origin, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Autofill is allowed only on the login's exact saved origin.");
        var password = _data.ReadPassword(login) ?? throw new InvalidOperationException("The password is no longer present in Windows Credential Manager.");
        var usernameJson = JsonSerializer.Serialize(login.Username);
        var passwordJson = JsonSerializer.Serialize(password);
        var script = "(() => { const user=document.querySelector('input[autocomplete=username],input[type=email],input[name*=user i]');" +
                     "const pass=document.querySelector('input[autocomplete=current-password],input[type=password]');" +
                     $"if(user){{user.focus();user.value={usernameJson};user.dispatchEvent(new Event('input',{{bubbles:true}}));}}" +
                     $"if(pass){{pass.focus();pass.value={passwordJson};pass.dispatchEvent(new Event('input',{{bubbles:true}}));}}" +
                     "return user||pass?'filled':'no compatible fields';})()";
        await _browser.ExecuteUiScriptAsync(script, CancellationToken.None);
        Status = "Filled matching fields. Haven did not submit the form.";
    });

    private async Task ApplyExtensionsAsync(Uri address)
    {
        foreach (var extension in _data.GetScriptsFor(address))
        {
            try { await _browser.ExecuteUiScriptAsync("(() => { 'use strict'; " + extension.Script + "\n})()", CancellationToken.None); }
            catch (Exception ex) { Status = $"{extension.Name} could not run: {ex.Message}"; }
        }
    }

    private Task SaveTabsAsync() => _data.SaveTabsAsync(Tabs.Select(tab => new BrowserTabState(tab.Id, tab.Title, tab.Address,
        tab.IsPrivate ? BrowserTabPrivacy.Private : BrowserTabPrivacy.Standard, tab.Group, DateTimeOffset.UtcNow)), CancellationToken.None);

    private async Task RunSafelyAsync(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { Status = "Browser action stopped."; }
        catch (Exception ex) { ReportBrowserError(ex); }
    }

    private void RefreshCollections()
    {
        Replace(Bookmarks, _data.Bookmarks);
        RaisePropertyChanged(nameof(HasBookmarks));
        RaisePropertyChanged(nameof(HasNoBookmarks));
        RaisePropertyChanged(nameof(BookmarkSummary));
        Replace(History, _data.History);
        Replace(Extensions, _data.Extensions);
        Replace(Logins, _data.Logins);
        RefreshGroups();
    }

    private void RefreshGroups() => Replace(TabGroups, Tabs.Select(tab => tab.Group).Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value));

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
    }

    private void TogglePanel(string panel)
    {
        var opening = panel switch
        {
            nameof(IsBookmarksOpen) => !IsBookmarksOpen,
            nameof(IsHistoryOpen) => !IsHistoryOpen,
            nameof(IsSettingsOpen) => !IsSettingsOpen,
            nameof(IsExtensionsOpen) => !IsExtensionsOpen,
            nameof(IsLoginsOpen) => !IsLoginsOpen,
            nameof(IsResearchOpen) => !IsResearchOpen,
            _ => !IsAssistantOpen
        };
        IsBookmarksOpen = opening && panel == nameof(IsBookmarksOpen);
        IsHistoryOpen = opening && panel == nameof(IsHistoryOpen);
        IsSettingsOpen = opening && panel == nameof(IsSettingsOpen);
        IsExtensionsOpen = opening && panel == nameof(IsExtensionsOpen);
        IsLoginsOpen = opening && panel == nameof(IsLoginsOpen);
        IsResearchOpen = opening && panel == nameof(IsResearchOpen);
        IsAssistantOpen = opening && panel == nameof(IsAssistantOpen);
        RaisePropertyChanged(nameof(IsAnyPanelOpen));
    }

    private string NormalizedBookmarkGroup => string.IsNullOrWhiteSpace(BookmarkGroup) ? "Bookmarks" : BookmarkGroup.Trim();

    private void OnStateChanged(object? sender, BrowserSnapshot state) =>
        Dispatcher.UIThread.Post(() => _ = HandleStateChangedAsync(state));

    private async Task HandleStateChangedAsync(BrowserSnapshot state)
    {
        if (state.Address is not null) Address = state.Address.ToString();
        IsLoading = state.IsLoading;
        CanGoBack = state.CanGoBack;
        CanGoForward = state.CanGoForward;
        BackCommand.RaiseCanExecuteChanged();
        ForwardCommand.RaiseCanExecuteChanged();
        Status = state.IsLoading ? "Loading..." : state.Status;
        if (SelectedTab is null) return;
        SelectedTab.Address = Address;
        SelectedTab.Title = string.IsNullOrWhiteSpace(state.Title) ? state.Address?.Host ?? "New tab" : state.Title;
        SelectedTab.Favicon = state.Favicon;
        if (state.IsLoading || state.Address is null) return;
        await _data.RecordVisitAsync(SelectedTab.Title, Address, SelectedTab.IsPrivate, CancellationToken.None);
        await SaveTabsAsync();
        RefreshCollections();
        await ApplyExtensionsAsync(state.Address);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sceneControl.InputSubmitted -= _havenScene.HandleInputSubmitted;
        ImportExtensionRequested -= OnImportExtensionRequested;
        _nativeWebResolver.Dispose();
        _havenScene.Dispose();
        _browser.StateChanged -= OnStateChanged;
        _bus.UnregisterElement("Browser.View");
        _ = SaveTabsAsync();
        _ = SaveResearchCheckpointAsync();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Represents browser tab view model and keeps its related state and behavior together.
/// </summary>
public sealed class BrowserTabViewModel : ObservableObject
{
    private string _title;
    private string _address;
    private string _group;
    private string? _favicon;
    private bool _isSelected;

    public BrowserTabViewModel(Guid id, string title, string address, bool isPrivate, string group)
    {
        Id = id;
        _title = title;
        _address = address;
        IsPrivate = isPrivate;
        _group = group;
    }

    public Guid Id { get; }
    public string Title { get => _title; set { if (SetProperty(ref _title, value)) RaisePropertyChanged(nameof(DisplayTitle)); } }
    public string Address { get => _address; set => SetProperty(ref _address, value); }
    public string? Favicon { get => _favicon; set => SetProperty(ref _favicon, value); }
    public bool IsPrivate { get; }
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    public string Group { get => _group; set => SetProperty(ref _group, value); }
    public string DisplayTitle => (IsPrivate ? "Private - " : string.Empty) + Title;
}
