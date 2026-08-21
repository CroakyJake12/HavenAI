/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Views/Pages/Browser/BrowserHavenScene.cs, Browser route composition.
 * What: Builds Browser chrome entirely from Haven.UI and projects BrowserPage state into it.
 * How: Static controls are Haven.UI and runtime tabs are DynamicUI instances backed by existing BrowserPage commands.
 * Why: Product chrome must remain Haven.UI-owned while a native WebView is used only as the Web renderer.
 */

using System.Collections.Specialized;
using PropertyChangedEventArgs = System.ComponentModel.PropertyChangedEventArgs;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenWeb = Haven.UI.Components.Web;
using Container = Haven.UI.Components.Container;

namespace Haven.Desktop.Views.Pages.Browser;

internal sealed partial class BrowserHavenScene : IDisposable
{
    private const string TabsLocation = "Browser.Tabs.Runtime";
    private readonly BrowserPage _page;
    private readonly DynamicUI _dynamic;
    private readonly HashSet<BrowserTabViewModel> _observedTabs = [];
    private bool _syncing;
    private bool _disposed;

    public BrowserHavenScene(BrowserPage page)
    {
        _page = page ?? throw new ArgumentNullException(nameof(page));
        var templates = new HavenDynamicUITemplateCatalog();
        templates.Register("""
<DynamicUI Name="BrowserTab">
  <Container Layout="Horizontal">
    <Button Name="Select" Type="{{TYPE}}">{{TITLE}}</Button>
    <Button Name="Close" Type="Text">x</Button>
  </Container>
</DynamicUI>
""", "BrowserTab.dynamicUI.hui");

        Root = new Page
        {
            Name = "Browser.Root",
            Layout = HavenLayout.Grid,
            Columns = "1fr Auto",
            Rows = "Auto Auto Auto 1fr Auto"
        };
        Root.SetValue(HavenProperties.Background, "Transparent");
        Root.SetValue(HavenProperties.Overflow, HavenOverflow.Clip);

        Tabs = new DynamicUIRuntime { Name = TabsLocation, Layout = HavenLayout.Horizontal };
        Tabs.SetValue(HavenProperties.Row, 0);
        Tabs.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        Tabs.SetValue(HavenProperties.Padding, HavenThickness.Parse("6px 8px"));
        Tabs.SetValue(HavenProperties.Background, "Transparent");
        Tabs.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        Root.Add(Tabs);

        var nav = new Container
        {
            Name = "Browser.Navigation",
            Layout = HavenLayout.Grid,
            Columns = "Auto Auto Auto Auto Auto 1fr Auto Auto",
            Rows = "44px"
        };
        nav.SetValue(HavenProperties.Row, 1);
        nav.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        nav.SetValue(HavenProperties.Padding, HavenThickness.Parse("4px 8px"));

        BackButton = Nav("Browser.Back", "Back");
        ForwardButton = Nav("Browser.Forward", "Forward");
        ReloadButton = Nav("Browser.Reload", "Reload");
        StopButton = Nav("Browser.Stop", "Stop");
        HomeButton = Nav("Browser.Home", "Home");
        AddressInput = new Input
        {
            Name = "Browser.Address",
            Placeholder = "Search or enter address",
            Text = page.Address,
            SubmitOnEnter = true
        };
        AddressInput.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        BookmarkButton = Nav("Browser.Bookmark", "Bookmark");
        NewTabButton = Nav("Browser.NewTab", "+");

        AddAt(nav, BackButton, 0);
        AddAt(nav, ForwardButton, 1);
        AddAt(nav, ReloadButton, 2);
        AddAt(nav, StopButton, 3);
        AddAt(nav, HomeButton, 4);
        AddAt(nav, AddressInput, 5);
        AddAt(nav, BookmarkButton, 6);
        AddAt(nav, NewTabButton, 7);
        Root.Add(nav);

        var tools = new Container { Name = "Browser.Utilities", Layout = HavenLayout.Horizontal };
        tools.SetValue(HavenProperties.Row, 2);
        tools.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        tools.SetValue(HavenProperties.Padding, HavenThickness.Parse("2px 8px 6px 8px"));

        BookmarksButton = Tool("Browser.Tools.Bookmarks", "Bookmarks");
        HistoryButton = Tool("Browser.Tools.History", "History");
        AssistantButton = Tool("Browser.Tools.Assistant", "Ask Haven");
        SettingsButton = Tool("Browser.Tools.Settings", "Settings");
        PrivateTabButton = Tool("Browser.Tools.Private", "Private tab");
        ExtensionsButton = Tool("Browser.Tools.Extensions", "Extensions");
        LoginsButton = Tool("Browser.Tools.Logins", "Logins");
        tools.Add(BookmarksButton);
        tools.Add(HistoryButton);
        tools.Add(AssistantButton);
        BuildResearchTool(tools);
        tools.Add(SettingsButton);
        tools.Add(PrivateTabButton);
        tools.Add(ExtensionsButton);
        tools.Add(LoginsButton);
        Root.Add(tools);

        WebSurface = new HavenWeb { Name = "Browser.Web", Url = page.Address };
        WebSurface.SetValue(HavenProperties.Row, 3);
        WebSurface.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        WebSurface.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        Root.Add(WebSurface);
        WebSurface.SetValue(HavenProperties.Column, 0);
        BuildUtilityPanel(templates);

        StatusText = new Text { Name = "Browser.Status", Level = TextLevel.Caption };
        StatusText.SetValue(HavenProperties.Row, 4);
        StatusText.SetValue(HavenProperties.Padding, HavenThickness.Parse("4px 10px"));
        StatusText.SetValue(HavenProperties.Foreground, "TextSecondary");
        Root.Add(StatusText);

        _dynamic = new DynamicUI(Root, templates);
        WireCommands();
        Subscribe();
        Refresh();
    }

    public Page Root { get; }
    public DynamicUIRuntime Tabs { get; }
    public Input AddressInput { get; }
    public HavenWeb WebSurface { get; }
    public Text StatusText { get; }
    public HavenButton BackButton { get; }
    public HavenButton ForwardButton { get; }
    public HavenButton ReloadButton { get; }
    public HavenButton StopButton { get; }
    public HavenButton HomeButton { get; }
    public HavenButton BookmarkButton { get; }
    public HavenButton NewTabButton { get; }
    public HavenButton BookmarksButton { get; }
    public HavenButton HistoryButton { get; }
    public HavenButton AssistantButton { get; }
    public HavenButton SettingsButton { get; }
    public HavenButton PrivateTabButton { get; }
    public HavenButton ExtensionsButton { get; }
    public HavenButton LoginsButton { get; }

    private static HavenButton Nav(string name, string content) =>
        new() { Name = name, Variant = ButtonVariant.Secondary, Content = content };

    private static HavenButton Tool(string name, string content) =>
        new() { Name = name, Variant = ButtonVariant.Text, Content = content };

    private static void AddAt(Container parent, HavenElement child, int column)
    {
        child.SetValue(HavenProperties.Column, column);
        parent.Add(child);
    }

    private void WireCommands()
    {
        BackButton.Invoked += (_, _) => _page.BackCommand.Execute(null);
        ForwardButton.Invoked += (_, _) => _page.ForwardCommand.Execute(null);
        ReloadButton.Invoked += (_, _) => _page.ReloadCommand.Execute(null);
        StopButton.Invoked += (_, _) => _page.StopCommand.Execute(null);
        HomeButton.Invoked += (_, _) => _page.HomeCommand.Execute(null);
        BookmarkButton.Invoked += (_, _) => _page.ToggleBookmarkCommand.Execute(null);
        NewTabButton.Invoked += (_, _) => _page.NewTabCommand.Execute(null);
        PrivateTabButton.Invoked += (_, _) => _page.NewPrivateTabCommand.Execute(null);
        BookmarksButton.Invoked += (_, _) => _page.ToggleBookmarksCommand.Execute(null);
        HistoryButton.Invoked += (_, _) => _page.ToggleHistoryCommand.Execute(null);
        AssistantButton.Invoked += (_, _) => _page.ToggleAssistantCommand.Execute(null);
        SettingsButton.Invoked += (_, _) => _page.ToggleSettingsCommand.Execute(null);
        ExtensionsButton.Invoked += (_, _) => _page.ToggleExtensionsCommand.Execute(null);
        LoginsButton.Invoked += (_, _) => _page.ToggleLoginsCommand.Execute(null);
        AddressInput.TextChanged += (_, _) =>
        {
            if (!_syncing) _page.Address = AddressInput.Text;
        };
    }

    private void Subscribe()
    {
        _page.PropertyChanged += OnPageChanged;
        _page.Tabs.CollectionChanged += OnTabsChanged;
        SyncTabSubscriptions();
    }

    private void OnPageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BrowserPage.SelectedTab) or nameof(BrowserPage.IsPrivate))
            RefreshTabs();
        RefreshStatic();
    }

    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncTabSubscriptions();
        RefreshTabs();
    }

    private void SyncTabSubscriptions()
    {
        foreach (var tab in _observedTabs.Except(_page.Tabs).ToArray())
        {
            tab.PropertyChanged -= OnTabChanged;
            _observedTabs.Remove(tab);
        }
        foreach (var tab in _page.Tabs.Where(tab => _observedTabs.Add(tab)))
            tab.PropertyChanged += OnTabChanged;
    }

    private void OnTabChanged(object? sender, PropertyChangedEventArgs e) => RefreshTabs();

    public void HandleInputSubmitted(Input input)
    {
        if (!ReferenceEquals(input, AddressInput)) return;
        _page.Address = AddressInput.Text;
        _page.NavigateCommand.Execute(null);
    }

    public void Refresh()
    {
        RefreshStatic();
        RefreshTabs();
    }

    private void RefreshStatic()
    {
        if (_disposed) return;
        _syncing = true;
        try
        {
            if (!string.Equals(AddressInput.Text, _page.Address, StringComparison.Ordinal))
                AddressInput.Text = _page.Address;
            WebSurface.Url = _page.Address;
            StatusText.Content = _page.Status;
            BackButton.SetValue(HavenProperties.Enabled, _page.CanGoBack);
            ForwardButton.SetValue(HavenProperties.Enabled, _page.CanGoForward);
            ReloadButton.SetValue(HavenProperties.Visibility,
                _page.IsLoading ? HavenVisibility.Collapsed : HavenVisibility.Visible);
            StopButton.SetValue(HavenProperties.Visibility,
                _page.IsLoading ? HavenVisibility.Visible : HavenVisibility.Collapsed);
            RefreshUtilityPanel();
        }
        finally
        {
            _syncing = false;
        }
    }

    private void RefreshTabs()
    {
        if (_disposed) return;
        Tabs.ClearItems();
        foreach (var tab in _page.Tabs)
        {
            var item = _dynamic.CreateItem("BrowserTab", TabsLocation, tab.Id.ToString("N"),
                new Dictionary<string, object?>
                {
                    ["TITLE"] = tab.DisplayTitle,
                    ["TYPE"] = tab.IsSelected ? "Secondary" : "Text"
                });
            item.GetComponent<HavenButton>("Select").Invoked += (_, _) => _page.SelectTabCommand.Execute(tab);
            item.GetComponent<HavenButton>("Close").Invoked += (_, _) => _page.CloseTabCommand.Execute(tab);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _page.PropertyChanged -= OnPageChanged;
        _page.Tabs.CollectionChanged -= OnTabsChanged;
        foreach (var tab in _observedTabs)
            tab.PropertyChanged -= OnTabChanged;
        _observedTabs.Clear();
        GC.SuppressFinalize(this);
    }
}
