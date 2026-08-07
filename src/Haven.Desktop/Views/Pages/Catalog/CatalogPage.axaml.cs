using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Events;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views.Pages.Catalog;

public sealed partial class CatalogPage : UserControl
{
    private readonly HavenEventBus _bus;
    private readonly ICatalogRepository _catalog;
    private readonly IOllamaClient _ollama;
    private readonly CatalogPageKind _kind;

    private bool _isCreating;

    public CatalogPage(HavenEventBus bus, ICatalogRepository catalog, IOllamaClient ollama, CatalogPageKind kind)
    {
        _bus = bus;
        _catalog = catalog;
        _ollama = ollama;
        _kind = kind;

        InitializeComponent();
        ApplyKindDefaults();
        WireEvents();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e) => _ = RefreshAsync();

    private static IBrush? Brush(string key) =>
        Avalonia.Application.Current?.TryFindResource(key, out var value) == true ? value as IBrush : null;

    private void ApplyKindDefaults()
    {
        TitleText.Text = _kind switch
        {
            CatalogPageKind.Agents => "Agents",
            CatalogPageKind.Plugins => "Plugins",
            _ => "Instruction Library"
        };
        SubtitleText.Text = _kind switch
        {
            CatalogPageKind.Agents => "Choose specialised local assistants and model preferences.",
            CatalogPageKind.Plugins => "Functional, capability-backed tools invoked with @.",
            _ => "Reusable built-in and custom instructions invoked with >."
        };
        var createLabel = _kind switch
        {
            CatalogPageKind.Agents => "Create agent",
            CatalogPageKind.Plugins => "Create plugin",
            _ => "Create instruction"
        };
        CreateToggleButton.Content = createLabel;
        CreateItemButton.Content = createLabel;
        BuilderTitleText.Text = _kind switch
        {
            CatalogPageKind.Agents => "AGENT CREATOR",
            CatalogPageKind.Plugins => "PLUGIN CREATOR",
            _ => "INSTRUCTION CREATOR"
        };
        BuilderPromptBox.PlaceholderText = _kind switch
        {
            CatalogPageKind.Agents => "Describe the assistant you want Haven to create",
            CatalogPageKind.Plugins => "Describe the functional capability and constraints",
            _ => "Describe the reusable instruction behaviour"
        };
        AgentFields.IsVisible = _kind == CatalogPageKind.Agents;
        PluginPersistsCheck.IsVisible = _kind == CatalogPageKind.Plugins;
        UploadPluginButton.IsVisible = _kind == CatalogPageKind.Plugins;
        CreateToggleButton.IsVisible = _kind == CatalogPageKind.Prompts;
    }

    private void WireEvents()
    {
        _bus.RegisterElement("Catalog.Actions.Refresh", RefreshButton);
        _bus.WirePointerEvents("Catalog.Actions.Refresh", RefreshButton);
        RefreshButton.Click += async (_, _) =>
        {
            _bus.Fire("Catalog.Actions.Refresh");
            await RefreshAsync();
        };

        _bus.RegisterElement("Catalog.Actions.Import", UploadPluginButton);
        _bus.WirePointerEvents("Catalog.Actions.Import", UploadPluginButton);
        UploadPluginButton.Click += async (_, _) =>
        {
            _bus.Fire("Catalog.Actions.Import");
            await UploadPluginAsync();
        };

        _bus.RegisterElement("Catalog.Actions.Create", CreateToggleButton);
        _bus.WirePointerEvents("Catalog.Actions.Create", CreateToggleButton);
        CreateToggleButton.Click += (_, _) =>
        {
            _isCreating = !_isCreating;
            CreatePanel.IsVisible = _isCreating;
            _bus.Fire("Catalog.Actions.Create");
        };

        _bus.RegisterElement("Catalog.Actions.CloseCreate", CloseCreateButton);
        _bus.WirePointerEvents("Catalog.Actions.CloseCreate", CloseCreateButton);
        CloseCreateButton.Click += (_, _) =>
        {
            _isCreating = false;
            CreatePanel.IsVisible = false;
            _bus.Fire("Catalog.Actions.CloseCreate");
        };

        _bus.RegisterElement("Catalog.Actions.BuildWithAi", BuildWithAiButton);
        _bus.WirePointerEvents("Catalog.Actions.BuildWithAi", BuildWithAiButton);
        BuildWithAiButton.Click += async (_, _) =>
        {
            _bus.Fire("Catalog.Actions.BuildWithAi");
            await BuildWithAiAsync();
        };

        _bus.RegisterElement("Catalog.Actions.CreateItem", CreateItemButton);
        _bus.WirePointerEvents("Catalog.Actions.CreateItem", CreateItemButton);
        CreateItemButton.Click += async (_, _) =>
        {
            _bus.Fire("Catalog.Actions.CreateItem");
            await CreateItemAsync();
        };
    }

    private async Task RefreshAsync()
    {
        ItemsPanel.Children.Clear();
        StatusText.Text = "Loading…";

        try
        {
            if (_kind == CatalogPageKind.Agents)
            {
                var items = await _catalog.GetAgentsAsync(CancellationToken.None);
                foreach (var item in items)
                    ItemsPanel.Children.Add(CreateCard(item.Name, item.IconKey, item.Description,
                        item.PreferredModel, item.IsEnabled, item.IsBuiltIn, item.Id));
            }
            else if (_kind == CatalogPageKind.Plugins)
            {
                var items = await _catalog.GetPluginsAsync(CancellationToken.None);
                foreach (var item in items)
                    ItemsPanel.Children.Add(CreateCard(item.Name, item.IconKey, item.Description,
                        item.Persists ? "Persistent" : "One-shot", item.IsEnabled, item.IsBuiltIn, item.Id));
            }
            else
            {
                var items = await _catalog.GetPromptsAsync(CancellationToken.None);
                foreach (var item in items)
                    ItemsPanel.Children.Add(CreateCard(item.Name, item.IconKey, item.Description,
                        item.Persists ? "Persistent" : "One-shot", item.IsEnabled, item.IsBuiltIn, item.Id));
            }

            StatusText.Text = $"{ItemsPanel.Children.Count} {(TitleText.Text ?? "").ToLowerInvariant()} available locally.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to load: {ex.Message}";
        }
    }

    private Border CreateCard(string name, string iconKey, string description, string meta, bool isEnabled, bool isBuiltIn, Guid id)
    {
        var icon = new HavenIcon { IconKey = iconKey, Width = 18, Height = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var iconBorder = new Border
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(10),
            Background = Brush("HavenAccentSoftBrush"),
            Child = icon
        };
        var nameBlock = new TextBlock { Text = name, FontSize = 17, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var badge = new Border
        {
            Background = Brush("HavenAccentSoftBrush"),
            CornerRadius = new CornerRadius(999), Padding = new Avalonia.Thickness(8, 3),
            Child = new TextBlock { Text = "LOCAL", Foreground = Brush("HavenAccentBrush"), FontSize = 9, FontWeight = FontWeight.Bold }
        };
        var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
        headerGrid.Children.Add(iconBorder);
        Grid.SetColumn(nameBlock, 1);
        headerGrid.Children.Add(nameBlock);
        Grid.SetColumn(badge, 2);
        headerGrid.Children.Add(badge);

        var descBlock = new TextBlock { Text = description, Classes = { "muted" }, Margin = new Avalonia.Thickness(0, 10, 0, 0) };
        var metaBlock = new TextBlock { Text = meta, Foreground = Brush("HavenAccentSecondaryBrush"), FontSize = 11 };

        var deleteButton = new Button { Content = "Delete", Classes = { "danger" }, IsVisible = !isBuiltIn };
        var qName = $"Catalog.List.Item{ItemsPanel.Children.Count}";
        deleteButton.RegisterWithEvents(qName + ".Delete", _bus);
        deleteButton.Click += async (_, _) =>
        {
            _bus.Fire($"{qName}.Delete");
            await DeleteItemAsync(id, isBuiltIn);
        };

        var buttonGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Avalonia.Thickness(0, 9, 0, 0) };
        Grid.SetColumn(deleteButton, 1);
        buttonGrid.Children.Add(deleteButton);

        var contentGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        contentGrid.Children.Add(headerGrid);
        Grid.SetRow(descBlock, 1);
        contentGrid.Children.Add(descBlock);
        Grid.SetRow(metaBlock, 2);
        contentGrid.Children.Add(metaBlock);
        Grid.SetRow(buttonGrid, 3);
        contentGrid.Children.Add(buttonGrid);

        var border = new Border
        {
            Classes = { "card" }, Width = 324, MinHeight = 150, Margin = new Avalonia.Thickness(0, 0, 14, 14),
            Child = contentGrid
        };

        border.PointerEntered += (_, _) => _bus.Fire($"{qName}.Hover");
        border.PointerExited += (_, _) => _bus.Fire($"{qName}.Leave");
        return border;
    }

    private async Task CreateItemAsync()
    {
        var name = NewNameBox.Text?.Trim() ?? "";
        var desc = NewDescriptionBox.Text?.Trim() ?? "";
        var instr = NewInstructionsBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(desc) || string.IsNullOrWhiteSpace(instr))
        {
            StatusText.Text = "Name, description and instructions are required.";
            return;
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            if (_kind == CatalogPageKind.Agents)
            {
                await _catalog.UpsertAgentAsync(new AgentDefinition(
                    Guid.NewGuid(), name, desc, instr, "agent-custom",
                    string.IsNullOrWhiteSpace(NewModelBox.Text) ? "default" : NewModelBox.Text.Trim(),
                    null, BuilderPromptBox.Text?.Trim() ?? "", "{\"mode\":\"ask\"}", false, true, now), CancellationToken.None);
            }
            else if (_kind == CatalogPageKind.Plugins)
            {
                await _catalog.UpsertPluginAsync(new PluginDefinition(
                    Guid.NewGuid(), name, desc, "plugin-custom", instr,
                    "[]", "[]", PluginPersistsCheck.IsChecked == true, false, true, now), CancellationToken.None);
            }
            else
            {
                await _catalog.UpsertPromptAsync(new PromptDefinition(
                    Guid.NewGuid(), name, desc, "prompt-custom", instr,
                    false, false, true, now), CancellationToken.None);
            }

            NewNameBox.Text = string.Empty;
            NewDescriptionBox.Text = string.Empty;
            NewInstructionsBox.Text = string.Empty;
            BuilderPromptBox.Text = string.Empty;
            _isCreating = false;
            CreatePanel.IsVisible = false;
            await RefreshAsync();
            StatusText.Text = $"Created {name}. It is ready to use in chat.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not create item: {ex.Message}";
        }
    }

    private async Task BuildWithAiAsync()
    {
        var prompt = BuilderPromptBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(prompt)) return;
        try
        {
            StatusText.Text = "Asking a local model to draft the instructions…";
            var models = await _ollama.GetModelsAsync(CancellationToken.None);
            var model = models.FirstOrDefault(m => m.Supports(ToolCapability.Text)) ?? models.FirstOrDefault();
            if (model is null) throw new InvalidOperationException("No local Ollama model is installed.");
            var kind = _kind switch { CatalogPageKind.Agents => "agent", CatalogPageKind.Plugins => "functional plugin", _ => "prompt" };
            var result = await _ollama.CompleteAsync(new OllamaChatRequest(
                model.Name,
                [new OllamaMessage("user", $"Write concise, production-ready system instructions for a Haven {kind} with this purpose: {prompt}\nReturn only the instruction text.")],
                EffortLevel.Medium), CancellationToken.None);
            NewInstructionsBox.Text = result.Trim();
            if (string.IsNullOrWhiteSpace(NewDescriptionBox.Text))
                NewDescriptionBox.Text = prompt;
            StatusText.Text = "Draft ready. Review the fields, add a name, then create it.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"AI draft failed: {ex.Message}";
        }
    }

    private async Task UploadPluginAsync()
    {
        if (_kind != CatalogPageKind.Plugins) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select a declarative Haven plugin manifest",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("JSON manifest") { Patterns = ["*.json"] }]
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
            var manifest = System.Text.Json.JsonSerializer.Deserialize<PluginImportManifest>(
                await File.ReadAllTextAsync(path), options)
                ?? throw new InvalidOperationException("Plugin manifest is empty.");
            if (string.IsNullOrWhiteSpace(manifest.Name) || string.IsNullOrWhiteSpace(manifest.Description) || string.IsNullOrWhiteSpace(manifest.Instructions))
                throw new InvalidOperationException("Plugin manifest requires name, description, and instructions.");
            var validCapabilities = manifest.Capabilities
                .Where(v => Enum.TryParse<ToolCapability>(v, true, out _))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            DashboardTileManifestPolicy.ValidateForImport(manifest.DashboardTiles);
            var existingPlugin = (await _catalog.GetPluginsAsync(CancellationToken.None))
                .FirstOrDefault(p => p.Name.Equals(manifest.Name.Trim(), StringComparison.OrdinalIgnoreCase));
            var pluginId = existingPlugin?.Id ?? GuidUtility.FromStableName("haven.imported.plugin." + manifest.Name.Trim().ToLowerInvariant());
            await _catalog.UpsertPluginAsync(new PluginDefinition(pluginId, manifest.Name.Trim(), manifest.Description.Trim(),
                string.IsNullOrWhiteSpace(manifest.IconKey) ? "plugin-custom" : manifest.IconKey.Trim(), manifest.Instructions.Trim(),
                System.Text.Json.JsonSerializer.Serialize(validCapabilities), System.Text.Json.JsonSerializer.Serialize(manifest.Conflicts),
                manifest.Persists, false, true, DateTimeOffset.UtcNow, manifest.IsAgentic,
                System.Text.Json.JsonSerializer.Serialize(manifest.AllowedModes),
                System.Text.Json.JsonSerializer.Serialize(manifest.DashboardTiles)), CancellationToken.None);
            await RefreshAsync();
            StatusText.Text = $"Imported @{manifest.Name} from a declarative Haven plugin manifest.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        {
            StatusText.Text = $"Could not import plugin: {ex.Message}";
        }
    }

    private async Task DeleteItemAsync(Guid id, bool isBuiltIn)
    {
        if (isBuiltIn) return;
        try
        {
            if (_kind == CatalogPageKind.Agents) await _catalog.DeleteCustomAgentAsync(id, CancellationToken.None);
            else if (_kind == CatalogPageKind.Plugins) await _catalog.DeleteCustomPluginAsync(id, CancellationToken.None);
            else await _catalog.DeleteCustomPromptAsync(id, CancellationToken.None);
            await RefreshAsync();
            StatusText.Text = "Item deleted.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Delete failed: {ex.Message}";
        }
    }

    private sealed class PluginImportManifest
    {
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string Instructions { get; init; } = string.Empty;
        public string IconKey { get; init; } = string.Empty;
        public IReadOnlyList<string> Capabilities { get; init; } = [];
        public IReadOnlyList<string> Conflicts { get; init; } = [];
        public IReadOnlyList<string> AllowedModes { get; init; } = [];
        public IReadOnlyList<DashboardPluginTileManifest> DashboardTiles { get; init; } = [];
        public bool Persists { get; init; }
        public bool IsAgentic { get; init; }
    }
}
