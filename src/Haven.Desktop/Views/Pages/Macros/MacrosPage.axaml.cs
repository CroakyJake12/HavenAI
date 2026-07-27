using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Events;

namespace Haven.Desktop.Views.Pages.Macros;

public sealed partial class MacrosPage : UserControl
{
    private readonly HavenEventBus _bus;
    private readonly IWorkspaceStateRepository _workspaceState;
    private readonly Guid? _containerId;
    private readonly Func<string, Task> _invoke;

    public MacrosPage(HavenEventBus bus, IWorkspaceStateRepository workspaceState, Guid? containerId, Func<string, Task> invoke)
    {
        _bus = bus;
        _workspaceState = workspaceState;
        _containerId = containerId;
        _invoke = invoke;

        InitializeComponent();
        WireEvents();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e) => _ = RefreshAsync();

    private static IBrush? Brush(string key) =>
        Avalonia.Application.Current?.TryFindResource(key, out var value) == true ? value as IBrush : null;

    private void WireEvents()
    {
        _bus.RegisterElement("Macros.Actions.Refresh", RefreshButton);
        _bus.WirePointerEvents("Macros.Actions.Refresh", RefreshButton);
        RefreshButton.Click += async (_, _) =>
        {
            _bus.Fire("Macros.Actions.Refresh");
            await RefreshAsync();
        };

        _bus.RegisterElement("Macros.Actions.Create", CreateButton);
        _bus.WirePointerEvents("Macros.Actions.Create", CreateButton);
        CreateButton.Click += async (_, _) =>
        {
            _bus.Fire("Macros.Actions.Create");
            await CreateAsync();
        };
    }

    private async Task RefreshAsync()
    {
        ItemsPanel.Children.Clear();
        StatusText.Text = "Loading…";

        try
        {
            var items = await _workspaceState.GetMacrosAsync(_containerId, CancellationToken.None);
            foreach (var item in items)
                ItemsPanel.Children.Add(CreateItemCard(item));
            StatusText.Text = items.Count == 0 ? "No macros yet." : $"{items.Count} macro{(items.Count == 1 ? "" : "s")}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to load: {ex.Message}";
        }
    }

    private Border CreateItemCard(MacroDefinition macro)
    {
        var qName = $"Macros.List.Item{ItemsPanel.Children.Count}";

        var nameBlock = new TextBlock { Text = macro.Name, FontWeight = FontWeight.SemiBold, FontSize = 16 };
        var descBlock = new TextBlock { Text = macro.Description, Classes = { "muted" } };
        var instrBlock = new TextBlock { Text = macro.Instruction, Classes = { "muted2" }, FontSize = 10, Margin = new Avalonia.Thickness(0, 5, 0, 0) };
        var stack = new StackPanel { Children = { nameBlock, descBlock, instrBlock } };

        var runButton = new Button { Content = "Run", Classes = { "accent" }, VerticalAlignment = VerticalAlignment.Center };
        var deleteButton = new Button { Content = "Delete", Classes = { "danger" }, VerticalAlignment = VerticalAlignment.Center };

        runButton.RegisterWithEvents($"{qName}.Run", _bus);
        runButton.Click += async (_, _) =>
        {
            _bus.Fire($"{qName}.Run");
            await InvokeMacroAsync(macro);
        };

        deleteButton.RegisterWithEvents($"{qName}.Delete", _bus);
        deleteButton.Click += async (_, _) =>
        {
            _bus.Fire($"{qName}.Delete");
            await DeleteAsync(macro);
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 7 };
        grid.Children.Add(stack);
        Grid.SetColumn(runButton, 1);
        grid.Children.Add(runButton);
        Grid.SetColumn(deleteButton, 2);
        grid.Children.Add(deleteButton);

        var border = new Border { Classes = { "card" }, Margin = new Avalonia.Thickness(0, 0, 0, 9), Child = grid };
        border.PointerEntered += (_, _) => _bus.Fire($"{qName}.Hover");
        border.PointerExited += (_, _) => _bus.Fire($"{qName}.Leave");
        return border;
    }

    private async Task CreateAsync()
    {
        var name = NewNameBox.Text?.Trim() ?? "";
        var desc = NewDescriptionBox.Text?.Trim() ?? "";
        var instr = NewInstructionBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(instr))
        {
            StatusText.Text = "Name and instruction are required.";
            return;
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            var macro = new MacroDefinition(Guid.NewGuid(), name, desc, instr, _containerId, true, now, now);
            await _workspaceState.UpsertMacroAsync(macro, CancellationToken.None);
            NewNameBox.Text = string.Empty;
            NewDescriptionBox.Text = string.Empty;
            NewInstructionBox.Text = string.Empty;
            await RefreshAsync();
            StatusText.Text = $"Created macro \"{name}\".";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not create macro: {ex.Message}";
        }
    }

    private async Task InvokeMacroAsync(MacroDefinition macro)
    {
        StatusText.Text = $"Running \"{macro.Name}\"…";
        try
        {
            await _invoke(macro.Instruction);
            StatusText.Text = $"Invoked macro \"{macro.Name}\".";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Invoke failed: {ex.Message}";
        }
    }

    private async Task DeleteAsync(MacroDefinition macro)
    {
        await _workspaceState.DeleteMacroAsync(macro.Id, CancellationToken.None);
        await RefreshAsync();
        StatusText.Text = $"Deleted macro \"{macro.Name}\".";
    }
}
