using Haven.Desktop.ViewModels;
using Haven.UI;
using Haven.UI.Components;
using Container = Haven.UI.Components.Container;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Mesh;

/// <summary>Native Mesh surface: trusted devices plus the distributed AI team room built on them.</summary>
internal sealed partial class MeshHavenScene : IDisposable
{
    private readonly MeshPageViewModel _viewModel;
    private bool _showWorkMode;
    private bool _disposed;

    public MeshHavenScene(MeshPageViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        Root = new Page { Name = "Mesh.Root", Layout = HavenLayout.Grid, Columns = "1fr", Rows = "auto auto auto 1fr" };
        Set(Root, HavenProperties.Padding, HavenThickness.Parse("24px 28px"));
        Set(Root, HavenProperties.Gap, HavenLength.Px(14));
        Set(Root, HavenProperties.Background, "Transparent");

        var header = new Container { Name = "Mesh.Header", Layout = HavenLayout.Grid, Columns = "1fr Auto", Rows = "auto" };
        Set(header, HavenProperties.Gap, HavenLength.Px(10));
        var titleStack = new Container { Layout = HavenLayout.Vertical };
        Set(titleStack, HavenProperties.Gap, HavenLength.Px(4));
        titleStack.Add(new HavenText("Mesh") { Name = "Mesh.Title", Level = TextLevel.H1 });
        titleStack.Add(Muted("Connect trusted devices, hand off work, and turn remote models and agents into one AI team."));
        header.Add(titleStack);
        RefreshButton = Button("Mesh.Refresh", "Refresh", ButtonVariant.Secondary);
        Set(RefreshButton, HavenProperties.Column, 1);
        RefreshButton.Invoked += async (_, _) => await RunAndRenderAsync(token => _viewModel.RefreshAsync(token));
        header.Add(RefreshButton);
        Root.Add(header);

        var tabs = new Container { Name = "Mesh.Tabs", Layout = HavenLayout.Horizontal };
        Set(tabs, HavenProperties.Row, 1);
        Set(tabs, HavenProperties.Gap, HavenLength.Px(8));
        DevicesTab = Button("Mesh.Tab.Devices", "Devices", ButtonVariant.Primary);
        WorkModeTab = Button("Mesh.Tab.WorkMode", "Work Mode", ButtonVariant.Ghost);
        DevicesTab.Invoked += (_, _) => { _showWorkMode = false; Render(); };
        WorkModeTab.Invoked += (_, _) => { _showWorkMode = true; Render(); };
        tabs.Add(DevicesTab); tabs.Add(WorkModeTab);
        Root.Add(tabs);

        StatusText = Muted(_viewModel.Status);
        StatusText.Name = "Mesh.Status";
        StatusText.Accessibility.AccessibleName = "Mesh status";
        Set(StatusText, HavenProperties.Row, 2);
        Root.Add(StatusText);

        ContentPanel = new Container { Name = "Mesh.Content", Layout = HavenLayout.Vertical };
        Set(ContentPanel, HavenProperties.Row, 3);
        Set(ContentPanel, HavenProperties.Width, HavenLength.Percent(100));
        Set(ContentPanel, HavenProperties.Height, HavenLength.Percent(100));
        Set(ContentPanel, HavenProperties.Gap, HavenLength.Px(12));
        Set(ContentPanel, HavenProperties.Overflow, HavenOverflow.Scroll);
        Root.Add(ContentPanel);
        Render();
    }

    public Page Root { get; }
    internal Container ContentPanel { get; }
    internal HavenText StatusText { get; }
    internal HavenButton DevicesTab { get; }
    internal HavenButton WorkModeTab { get; }
    internal HavenButton RefreshButton { get; }

    internal async Task InitialiseAsync()
    {
        await RunAndRenderAsync(token => _viewModel.InitialiseAsync(token));
    }

    private void Render()
    {
        DevicesTab.Variant = _showWorkMode ? ButtonVariant.Ghost : ButtonVariant.Primary;
        WorkModeTab.Variant = _showWorkMode ? ButtonVariant.Primary : ButtonVariant.Ghost;
        StatusText.Content = _viewModel.Status;
        foreach (var child in ContentPanel.Children.ToArray()) ContentPanel.Remove(child);
        if (_showWorkMode) BuildWorkModeContent(ContentPanel);
        else BuildDevicesContent(ContentPanel);
    }

    private async Task RunAndRenderAsync(Func<CancellationToken, Task> operation)
    {
        try { await operation(CancellationToken.None); }
        catch (Exception ex) { StatusText.Content = "Mesh action failed: " + ex.Message; return; }
        Render();
    }

    internal static Container Card(string name)
    {
        var card = new Container { Name = name, Layout = HavenLayout.Vertical };
        Set(card, HavenProperties.Width, HavenLength.Percent(100));
        Set(card, HavenProperties.Background, "SurfaceRaised");
        Set(card, HavenProperties.BorderColor, "Border");
        Set(card, HavenProperties.BorderWidth, HavenLength.Px(1));
        Set(card, HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(18)));
        Set(card, HavenProperties.Padding, HavenThickness.Parse("16px"));
        Set(card, HavenProperties.Gap, HavenLength.Px(9));
        Set(card, HavenProperties.Shadow, "Card");
        return card;
    }

    internal static Container Row(string columns = "1fr Auto")
    {
        var row = new Container { Layout = HavenLayout.Grid, Columns = columns, Rows = "auto" };
        Set(row, HavenProperties.Gap, HavenLength.Px(8));
        return row;
    }

    internal static HavenText Heading(string content, TextLevel level = TextLevel.H3) => new(content) { Level = level };
    internal static HavenText Muted(string content)
    {
        var text = new HavenText(content) { Level = TextLevel.Paragraph };
        Set(text, HavenProperties.Foreground, "TextSecondary");
        return text;
    }
    internal static Input InputField(string name, string placeholder, bool multiline = false)
    {
        var input = new Input { Name = name, Placeholder = placeholder, SubmitOnEnter = !multiline, Multiline = multiline };
        input.Accessibility.AccessibleName = placeholder;
        Set(input, HavenProperties.Width, HavenLength.Percent(100));
        if (multiline) Set(input, HavenProperties.MinHeight, HavenLength.Px(96));
        return input;
    }
    internal static HavenButton Button(string name, string content, ButtonVariant variant)
    {
        var button = new HavenButton { Name = name, Content = content, Variant = variant };
        button.Accessibility.AccessibleName = content;
        return button;
    }
    internal static void Set<T>(HavenElement element, HavenProperty<T> property, T value) => element.SetValue(property, value);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _viewModel.Dispose();
        foreach (var child in Root.Children.ToArray()) Root.Remove(child);
    }
}
