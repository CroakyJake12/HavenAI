/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/ViewModels/BrowserPageViewModel.cs, in the Desktop presentation-model layer, exposing bindable state and commands to Avalonia views.
 * What: This file owns BrowserPageViewModel, BrowserTabViewModel. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Keeping UI state here makes the XAML declarative and keeps behavior testable without recreating the full window.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text;
using Avalonia.Threading;
using Haven.Application;
using Haven.Browser;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Represents browser page view model and keeps its related state and behavior together.
/// </summary>
public sealed class BrowserPageViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Stores browser locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly BrowserSessionService _browser;
    /// <summary>
    /// Stores data locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly BrowserDataService _data;
    /// <summary>
    /// Stores ollama locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IOllamaClient _ollama;
    /// <summary>
    /// Stores preferences locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly UserPreferencesService _preferences;
    /// <summary>
    /// Stores browser tools locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly BrowserToolRuntime _browserTools;
    /// <summary>
    /// Stores selected tab locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private BrowserTabViewModel? _selectedTab;
    /// <summary>
    /// Stores address locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _address;
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _status;
    /// <summary>Tracks whether the native page is currently navigating.</summary>
    private bool _isLoading;
    /// <summary>Mirrors the native adapter's backward-history availability.</summary>
    private bool _canGoBack;
    /// <summary>Mirrors the native adapter's forward-history availability.</summary>
    private bool _canGoForward;
    /// <summary>
    /// Stores bookmark group locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _bookmarkGroup = "Bookmarks";
    /// <summary>
    /// Stores new group name locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _newGroupName = "Research";
    /// <summary>
    /// Stores assistant input locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _assistantInput = string.Empty;
    /// <summary>
    /// Stores assistant output locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _assistantOutput = "Ask Haven to summarise, explain, or extract information from this page.";
    /// <summary>
    /// Stores is bookmarks open locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isBookmarksOpen;
    /// <summary>
    /// Stores is history open locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isHistoryOpen;
    /// <summary>
    /// Stores is settings open locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isSettingsOpen;
    /// <summary>
    /// Stores is extensions open locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isExtensionsOpen;
    /// <summary>
    /// Stores is logins open locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isLoginsOpen;
    /// <summary>
    /// Stores is assistant open locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isAssistantOpen;
    /// <summary>
    /// Stores home page locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _homePage;
    /// <summary>
    /// Stores search template locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _searchTemplate;
    /// <summary>
    /// Stores save history locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _saveHistory;
    /// <summary>
    /// Stores offer to save logins locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _offerToSaveLogins;
    /// <summary>
    /// Stores restore tabs locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _restoreTabs;
    /// <summary>
    /// Stores enable extensions locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _enableExtensions;
    /// <summary>
    /// Stores vertical tabs locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _verticalTabs;
    /// <summary>
    /// Stores login origin locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _loginOrigin = string.Empty;
    /// <summary>
    /// Stores login username locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _loginUsername = string.Empty;
    /// <summary>
    /// Stores login password locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _loginPassword = string.Empty;

    public BrowserPageViewModel(BrowserSessionService browser, BrowserDataService data, IOllamaClient ollama, UserPreferencesService preferences)
    {
        _browser = browser;
        _data = data;
        _ollama = ollama;
        _preferences = preferences;
        _browserTools = new BrowserToolRuntime(browser);
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
    }

    /// <summary>
    /// Stores import extension requested locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event EventHandler<bool>? ImportExtensionRequested;
    /// <summary>
    /// Gets or updates browser, the bindable or domain state represented by this property.
    /// </summary>
    public BrowserSessionService Browser => _browser;
    /// <summary>
    /// Gets or updates tabs, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<BrowserTabViewModel> Tabs { get; } = [];
    /// <summary>
    /// Gets or updates bookmarks, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<BrowserBookmark> Bookmarks { get; } = [];
    /// <summary>
    /// Gets or updates history, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<BrowserHistoryEntry> History { get; } = [];
    /// <summary>
    /// Gets or updates extensions, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<BrowserExtensionDefinition> Extensions { get; } = [];
    /// <summary>
    /// Gets or updates logins, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<SavedLogin> Logins { get; } = [];
    /// <summary>
    /// Gets or updates tab groups, the bindable or domain state represented by this property.
    /// </summary>
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
            _ = NavigateSafelyAsync();
        }
    }

    /// <summary>
    /// Gets or updates address, the bindable or domain state represented by this property.
    /// </summary>
    public string Address { get => _address; set => SetProperty(ref _address, value); }
    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>Reports whether a navigation is in progress so the view can cover stale pixels.</summary>
    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }
    /// <summary>Reports whether the native history contains an earlier entry.</summary>
    public bool CanGoBack { get => _canGoBack; private set => SetProperty(ref _canGoBack, value); }
    /// <summary>Reports whether the native history contains a later entry.</summary>
    public bool CanGoForward { get => _canGoForward; private set => SetProperty(ref _canGoForward, value); }
    /// <summary>
    /// Gets or updates bookmark group, the bindable or domain state represented by this property.
    /// </summary>
    public string BookmarkGroup { get => _bookmarkGroup; set => SetProperty(ref _bookmarkGroup, value); }
    /// <summary>
    /// Reports whether bookmarks applies to the current state.
    /// </summary>
    public bool HasBookmarks => Bookmarks.Count > 0;
    /// <summary>
    /// Reports whether no bookmarks applies to the current state.
    /// </summary>
    public bool HasNoBookmarks => !HasBookmarks;
    /// <summary>
    /// Gets or updates bookmark summary, the bindable or domain state represented by this property.
    /// </summary>
    public string BookmarkSummary => Bookmarks.Count == 1 ? "1 saved bookmark" : $"{Bookmarks.Count} saved bookmarks";
    /// <summary>
    /// Gets or updates new group name, the bindable or domain state represented by this property.
    /// </summary>
    public string NewGroupName { get => _newGroupName; set => SetProperty(ref _newGroupName, value); }
    /// <summary>
    /// Gets or updates assistant input, the bindable or domain state represented by this property.
    /// </summary>
    public string AssistantInput { get => _assistantInput; set { if (SetProperty(ref _assistantInput, value)) AskAssistantCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates assistant output, the bindable or domain state represented by this property.
    /// </summary>
    public string AssistantOutput { get => _assistantOutput; private set => SetProperty(ref _assistantOutput, value); }
    /// <summary>
    /// Reports whether bookmarks open applies to the current state.
    /// </summary>
    public bool IsBookmarksOpen { get => _isBookmarksOpen; private set => SetProperty(ref _isBookmarksOpen, value); }
    /// <summary>
    /// Reports whether history open applies to the current state.
    /// </summary>
    public bool IsHistoryOpen { get => _isHistoryOpen; private set => SetProperty(ref _isHistoryOpen, value); }
    /// <summary>
    /// Reports whether settings open applies to the current state.
    /// </summary>
    public bool IsSettingsOpen { get => _isSettingsOpen; private set => SetProperty(ref _isSettingsOpen, value); }
    /// <summary>
    /// Reports whether extensions open applies to the current state.
    /// </summary>
    public bool IsExtensionsOpen { get => _isExtensionsOpen; private set => SetProperty(ref _isExtensionsOpen, value); }
    /// <summary>
    /// Reports whether logins open applies to the current state.
    /// </summary>
    public bool IsLoginsOpen { get => _isLoginsOpen; private set => SetProperty(ref _isLoginsOpen, value); }
    /// <summary>
    /// Reports whether assistant open applies to the current state.
    /// </summary>
    public bool IsAssistantOpen { get => _isAssistantOpen; private set => SetProperty(ref _isAssistantOpen, value); }
    /// <summary>
    /// Reports whether any panel open applies to the current state.
    /// </summary>
    public bool IsAnyPanelOpen => IsBookmarksOpen || IsHistoryOpen || IsSettingsOpen || IsExtensionsOpen || IsLoginsOpen || IsAssistantOpen;
    /// <summary>
    /// Reports whether private applies to the current state.
    /// </summary>
    public bool IsPrivate => SelectedTab?.IsPrivate == true;
    /// <summary>
    /// Gets or updates privacy label, the bindable or domain state represented by this property.
    /// </summary>
    public string PrivacyLabel => IsPrivate ? "Private tab - history and tab state are not saved" : "Standard tab";
    /// <summary>
    /// Gets or updates home page, the bindable or domain state represented by this property.
    /// </summary>
    public string HomePage { get => _homePage; set => SetProperty(ref _homePage, value); }
    /// <summary>
    /// Gets or updates search template, the bindable or domain state represented by this property.
    /// </summary>
    public string SearchTemplate { get => _searchTemplate; set => SetProperty(ref _searchTemplate, value); }
    /// <summary>
    /// Gets or updates save history, the bindable or domain state represented by this property.
    /// </summary>
    public bool SaveHistory { get => _saveHistory; set => SetProperty(ref _saveHistory, value); }
    /// <summary>
    /// Gets or updates offer to save logins, the bindable or domain state represented by this property.
    /// </summary>
    public bool OfferToSaveLogins { get => _offerToSaveLogins; set => SetProperty(ref _offerToSaveLogins, value); }
    /// <summary>
    /// Gets or updates restore tabs, the bindable or domain state represented by this property.
    /// </summary>
    public bool RestoreTabs { get => _restoreTabs; set => SetProperty(ref _restoreTabs, value); }
    /// <summary>
    /// Gets or updates enable extensions, the bindable or domain state represented by this property.
    /// </summary>
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
    /// <summary>
    /// Gets or updates horizontal tabs, the bindable or domain state represented by this property.
    /// </summary>
    public bool HorizontalTabs => !VerticalTabs;
    /// <summary>
    /// Gets or updates login origin, the bindable or domain state represented by this property.
    /// </summary>
    public string LoginOrigin { get => _loginOrigin; set => SetProperty(ref _loginOrigin, value); }
    /// <summary>
    /// Gets or updates login username, the bindable or domain state represented by this property.
    /// </summary>
    public string LoginUsername { get => _loginUsername; set => SetProperty(ref _loginUsername, value); }
    /// <summary>
    /// Gets or updates login password, the bindable or domain state represented by this property.
    /// </summary>
    public string LoginPassword { get => _loginPassword; set => SetProperty(ref _loginPassword, value); }

    /// <summary>
    /// Gets or updates navigate command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand NavigateCommand { get; }
    /// <summary>
    /// Gets or updates back command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand BackCommand { get; }
    /// <summary>
    /// Gets or updates forward command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand ForwardCommand { get; }
    /// <summary>
    /// Gets or updates reload command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand ReloadCommand { get; }
    /// <summary>
    /// Gets or updates hard reload command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand HardReloadCommand { get; }
    /// <summary>
    /// Gets or updates stop command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand StopCommand { get; }
    /// <summary>
    /// Gets or updates home command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand HomeCommand { get; }
    /// <summary>
    /// Gets or updates new tab command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand NewTabCommand { get; }
    /// <summary>
    /// Gets or updates new private tab command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand NewPrivateTabCommand { get; }
    /// <summary>
    /// Gets or updates close tab command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<BrowserTabViewModel> CloseTabCommand { get; }
    /// <summary>
    /// Gets or updates select tab command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<BrowserTabViewModel> SelectTabCommand { get; }
    /// <summary>
    /// Gets or updates add bookmark command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand AddBookmarkCommand { get; }
    /// <summary>
    /// Gets or updates remove bookmark command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<BrowserBookmark> RemoveBookmarkCommand { get; }
    /// <summary>
    /// Gets or updates open bookmark command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<BrowserBookmark> OpenBookmarkCommand { get; }
    /// <summary>
    /// Gets or updates open history command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<BrowserHistoryEntry> OpenHistoryCommand { get; }
    /// <summary>
    /// Gets or updates clear history command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand ClearHistoryCommand { get; }
    /// <summary>
    /// Creates tab group command with the invariants required by its callers.
    /// </summary>
    public AsyncRelayCommand CreateTabGroupCommand { get; }
    /// <summary>
    /// Gets or updates print command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand PrintCommand { get; }
    /// <summary>
    /// Gets or updates inspect command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand InspectCommand { get; }
    /// <summary>
    /// Gets or updates ask assistant command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand AskAssistantCommand { get; }
    /// <summary>
    /// Gets or updates summarise command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand SummariseCommand { get; }
    /// <summary>
    /// Gets or updates save browser settings command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand SaveBrowserSettingsCommand { get; }
    /// <summary>
    /// Gets or updates toggle extension command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<BrowserExtensionDefinition> ToggleExtensionCommand { get; }
    /// <summary>
    /// Gets or updates delete extension command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<BrowserExtensionDefinition> DeleteExtensionCommand { get; }
    /// <summary>
    /// Gets or updates save login command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand SaveLoginCommand { get; }
    /// <summary>
    /// Gets or updates delete login command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<SavedLogin> DeleteLoginCommand { get; }
    /// <summary>
    /// Gets or updates autofill login command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<SavedLogin> AutofillLoginCommand { get; }
    /// <summary>
    /// Gets or updates toggle bookmarks command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand ToggleBookmarksCommand { get; }
    /// <summary>
    /// Gets or updates toggle history command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand ToggleHistoryCommand { get; }
    /// <summary>
    /// Gets or updates toggle settings command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand ToggleSettingsCommand { get; }
    /// <summary>
    /// Gets or updates toggle extensions command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand ToggleExtensionsCommand { get; }
    /// <summary>
    /// Gets or updates toggle logins command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand ToggleLoginsCommand { get; }
    /// <summary>
    /// Gets or updates toggle assistant command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand ToggleAssistantCommand { get; }
    /// <summary>
    /// Gets or updates import extension requested command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand ImportExtensionRequestedCommand { get; }
    /// <summary>
    /// Gets or updates convert chrome extension requested command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand ConvertChromeExtensionRequestedCommand { get; }

    /// <summary>
    /// Performs navigate safely asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task NavigateSafelyAsync() => RunSafelyAsync(async () =>
    {
        Status = await _browser.NavigateAsync(Address, CancellationToken.None);
    });

    /// <summary>
    /// Performs import extension asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task ImportExtensionAsync(string path, bool convertChrome) => RunSafelyAsync(async () =>
    {
        var extension = convertChrome
            ? await _data.ConvertChromeExtensionAsync(path, CancellationToken.None)
            : await _data.ImportHavenExtensionAsync(path, CancellationToken.None);
        RefreshCollections();
        Status = $"Imported {extension.Name}. Review it, then enable it explicitly.";
    });

    /// <summary>
    /// Performs the report browser error step owned by this component.
    /// </summary>
    public void ReportBrowserError(Exception exception) => Status = $"Browser unavailable: {exception.Message}";

    /// <summary>
    /// Performs the restore saved tabs step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs add tab asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task AddTabAsync(bool isPrivate)
    {
        var tab = new BrowserTabViewModel(Guid.NewGuid(), isPrivate ? "Private tab" : "New tab", HomePage, isPrivate, string.Empty);
        Tabs.Add(tab);
        SelectedTab = tab;
        await SaveTabsAsync();
    }

    /// <summary>
    /// Performs close tab asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task CloseTabAsync(BrowserTabViewModel? tab)
    {
        if (tab is null) return;
        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        if (Tabs.Count == 0) Tabs.Add(new BrowserTabViewModel(Guid.NewGuid(), "New tab", HomePage, false, string.Empty));
        SelectedTab = Tabs[Math.Clamp(index, 0, Tabs.Count - 1)];
        await SaveTabsAsync();
    }

    /// <summary>
    /// Performs select tab asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private Task SelectTabAsync(BrowserTabViewModel? tab)
    {
        if (tab is not null) SelectedTab = tab;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Performs add bookmark asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private Task AddBookmarkAsync() => RunSafelyAsync(async () =>
    {
        await _data.AddBookmarkAsync(SelectedTab?.Title ?? Address, Address, BookmarkGroup, CancellationToken.None);
        RefreshCollections();
        Status = $"Bookmark saved locally in {NormalizedBookmarkGroup}.";
    });

    /// <summary>
    /// Performs remove bookmark asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs open bookmark asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task OpenBookmarkAsync(BrowserBookmark? bookmark)
    {
        if (bookmark is null) return;
        Address = bookmark.Address;
        await NavigateSafelyAsync();
    }

    /// <summary>
    /// Performs open history asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task OpenHistoryAsync(BrowserHistoryEntry? entry)
    {
        if (entry is null) return;
        Address = entry.Address;
        await NavigateSafelyAsync();
    }

    /// <summary>
    /// Performs clear history asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ClearHistoryAsync()
    {
        await _data.ClearHistoryAsync(CancellationToken.None);
        RefreshCollections();
        Status = "Browser history cleared.";
    }

    /// <summary>
    /// Creates tab group async with the invariants required by its callers.
    /// </summary>
    private async Task CreateTabGroupAsync()
    {
        if (SelectedTab is null || string.IsNullOrWhiteSpace(NewGroupName)) return;
        SelectedTab.Group = NewGroupName.Trim();
        RefreshGroups();
        await SaveTabsAsync();
        Status = $"Added this tab to {SelectedTab.Group}.";
    }

    /// <summary>
    /// Performs ask assistant asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private Task AskAssistantAsync() => AskAssistantAsync(AssistantInput);

    /// <summary>
    /// Performs ask assistant asynchronously so I/O does not block the caller's thread.
    /// </summary>
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
                    activity.Append(result.Activity.Succeeded ? "✓ " : "! ").Append(result.Activity.Title).Append(": ").AppendLine(result.Activity.Detail);
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

    /// <summary>
    /// Performs save browser settings asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private Task SaveBrowserSettingsAsync() => RunSafelyAsync(async () =>
    {
        await _data.SaveSettingsAsync(new BrowserSettings(HomePage.Trim(), SearchTemplate.Trim(), SaveHistory, OfferToSaveLogins,
            RestoreTabs, EnableExtensions, VerticalTabs), CancellationToken.None);
        Status = "Browser settings saved locally.";
    });

    /// <summary>
    /// Performs toggle extension asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ToggleExtensionAsync(BrowserExtensionDefinition? extension)
    {
        if (extension is null) return;
        await _data.SetExtensionEnabledAsync(extension.Id, !extension.IsEnabled, CancellationToken.None);
        RefreshCollections();
    }

    /// <summary>
    /// Performs delete extension asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task DeleteExtensionAsync(BrowserExtensionDefinition? extension)
    {
        if (extension is null) return;
        await _data.DeleteExtensionAsync(extension.Id, CancellationToken.None);
        RefreshCollections();
    }

    /// <summary>
    /// Performs save login asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private Task SaveLoginAsync() => RunSafelyAsync(async () =>
    {
        await _data.SaveLoginAsync(LoginOrigin, LoginUsername, LoginPassword, CancellationToken.None);
        LoginPassword = string.Empty;
        RefreshCollections();
        Status = "Login stored in Windows Credential Manager. Haven saved only its origin and username metadata.";
    });

    /// <summary>
    /// Performs delete login asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task DeleteLoginAsync(SavedLogin? login)
    {
        if (login is null) return;
        await _data.DeleteLoginAsync(login, CancellationToken.None);
        RefreshCollections();
    }

    /// <summary>
    /// Performs autofill login asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs apply extensions asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ApplyExtensionsAsync(Uri address)
    {
        foreach (var extension in _data.GetScriptsFor(address))
        {
            try { await _browser.ExecuteUiScriptAsync("(() => { 'use strict'; " + extension.Script + "\n})()", CancellationToken.None); }
            catch (Exception ex) { Status = $"{extension.Name} could not run: {ex.Message}"; }
        }
    }

    /// <summary>
    /// Performs save tabs asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private Task SaveTabsAsync() => _data.SaveTabsAsync(Tabs.Select(tab => new BrowserTabState(tab.Id, tab.Title, tab.Address,
        tab.IsPrivate ? BrowserTabPrivacy.Private : BrowserTabPrivacy.Standard, tab.Group, DateTimeOffset.UtcNow)), CancellationToken.None);

    /// <summary>
    /// Runs run safely async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    private async Task RunSafelyAsync(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { Status = "Browser action stopped."; }
        catch (Exception ex) { ReportBrowserError(ex); }
    }

    /// <summary>
    /// Performs the refresh collections step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the refresh groups step owned by this component.
    /// </summary>
    private void RefreshGroups() => Replace(TabGroups, Tabs.Select(tab => tab.Group).Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value));

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
    }

    /// <summary>
    /// Performs the toggle panel step owned by this component.
    /// </summary>
    private void TogglePanel(string panel)
    {
        var opening = panel switch
        {
            nameof(IsBookmarksOpen) => !IsBookmarksOpen,
            nameof(IsHistoryOpen) => !IsHistoryOpen,
            nameof(IsSettingsOpen) => !IsSettingsOpen,
            nameof(IsExtensionsOpen) => !IsExtensionsOpen,
            nameof(IsLoginsOpen) => !IsLoginsOpen,
            _ => !IsAssistantOpen
        };
        IsBookmarksOpen = opening && panel == nameof(IsBookmarksOpen);
        IsHistoryOpen = opening && panel == nameof(IsHistoryOpen);
        IsSettingsOpen = opening && panel == nameof(IsSettingsOpen);
        IsExtensionsOpen = opening && panel == nameof(IsExtensionsOpen);
        IsLoginsOpen = opening && panel == nameof(IsLoginsOpen);
        IsAssistantOpen = opening && panel == nameof(IsAssistantOpen);
        RaisePropertyChanged(nameof(IsAnyPanelOpen));
    }

    /// <summary>
    /// Gets or updates normalized bookmark group, the bindable or domain state represented by this property.
    /// </summary>
    private string NormalizedBookmarkGroup => string.IsNullOrWhiteSpace(BookmarkGroup) ? "Bookmarks" : BookmarkGroup.Trim();

    /// <summary>
    /// Handles the state changed event raised by the UI or runtime.
    /// </summary>
    private void OnStateChanged(object? sender, BrowserSnapshot state) =>
        Dispatcher.UIThread.Post(() => _ = HandleStateChangedAsync(state));

    /// <summary>
    /// Performs handle state changed asynchronously so I/O does not block the caller's thread.
    /// </summary>
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
        if (state.IsLoading || state.Address is null) return;
        await _data.RecordVisitAsync(SelectedTab.Title, Address, SelectedTab.IsPrivate, CancellationToken.None);
        await SaveTabsAsync();
        RefreshCollections();
        await ApplyExtensionsAsync(state.Address);
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose()
    {
        _browser.StateChanged -= OnStateChanged;
        _ = SaveTabsAsync();
    }
}

/// <summary>
/// Represents browser tab view model and keeps its related state and behavior together.
/// </summary>
public sealed class BrowserTabViewModel : ObservableObject
{
    /// <summary>
    /// Stores title locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _title;
    /// <summary>
    /// Stores address locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _address;
    /// <summary>
    /// Stores group locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _group;
    /// <summary>
    /// Stores is selected locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isSelected;

    public BrowserTabViewModel(Guid id, string title, string address, bool isPrivate, string group)
    {
        Id = id;
        _title = title;
        _address = address;
        IsPrivate = isPrivate;
        _group = group;
    }

    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id { get; }
    /// <summary>
    /// Gets or updates title, the bindable or domain state represented by this property.
    /// </summary>
    public string Title { get => _title; set { if (SetProperty(ref _title, value)) RaisePropertyChanged(nameof(DisplayTitle)); } }
    /// <summary>
    /// Gets or updates address, the bindable or domain state represented by this property.
    /// </summary>
    public string Address { get => _address; set => SetProperty(ref _address, value); }
    /// <summary>
    /// Reports whether private applies to the current state.
    /// </summary>
    public bool IsPrivate { get; }
    /// <summary>
    /// Reports whether selected applies to the current state.
    /// </summary>
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    /// <summary>
    /// Gets or updates group, the bindable or domain state represented by this property.
    /// </summary>
    public string Group { get => _group; set => SetProperty(ref _group, value); }
    /// <summary>
    /// Gets or updates display title, the bindable or domain state represented by this property.
    /// </summary>
    public string DisplayTitle => (IsPrivate ? "Private - " : string.Empty) + Title;
}
