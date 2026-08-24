using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Events;
using Haven.Desktop.HavenUI.Components.Buttons;

namespace Haven.Desktop.Views.Pages.Catalog;

/// <summary>
/// Code-behind-only editor for the authoritative Capability Registry. The form
/// exposes runtime ownership and safety metadata instead of writing obsolete
/// Plugin records or accepting executable manifest code.
/// </summary>
public sealed partial class CapabilityCatalogPage : UserControl
{
    private readonly HavenEventBus _bus;
    private readonly ICapabilityRepository _capabilities;
    private readonly IModeRegistry _modes;
    private readonly IOllamaClient _ollama;
    private readonly Action _openTemplateLab;
    private IReadOnlyList<ModeDefinition> _apps = [];

    public CapabilityCatalogPage(
        HavenEventBus bus,
        ICapabilityRepository capabilities,
        IModeRegistry modes,
        IOllamaClient ollama,
        Action openTemplateLab)
    {
        _bus = bus;
        _capabilities = capabilities;
        _modes = modes;
        _ollama = ollama;
        _openTemplateLab = openTemplateLab;
        InitializeComponent();
        ConfigureSelectors();
        WireEvents();
        _ = RefreshAsync();
    }

    private void ConfigureSelectors()
    {
        PlatformBox.ItemsSource = Enum.GetValues<CapabilityPlatform>()
            .Where(value => value is CapabilityPlatform.Windows or CapabilityPlatform.Android or CapabilityPlatform.All)
            .ToArray();
        PlatformBox.SelectedItem = CapabilityPlatform.All;
        RiskBox.ItemsSource = Enum.GetValues<CapabilityRiskClass>();
        RiskBox.SelectedItem = CapabilityRiskClass.Low;
        AvailabilityBox.ItemsSource = Enum.GetValues<CapabilityAvailability>();
        AvailabilityBox.SelectedItem = CapabilityAvailability.DependencyRequired;
    }

    private void WireEvents()
    {
        Register("Capabilities.Refresh", RefreshButton);
        Register("Capabilities.TemplateLab", TemplateLabButton);
        Register("Capabilities.Create.Toggle", CreateToggleButton);
        Register("Capabilities.Create.Close", CloseCreateButton);
        Register("Capabilities.Create.BuildWithAi", BuildWithAiButton);
        Register("Capabilities.Create.Save", CreateButton);
        RefreshButton.Click += async (_, _) => await RefreshAsync();
        TemplateLabButton.Click += (_, _) => _openTemplateLab();
        CreateToggleButton.Click += (_, _) => CreatePanel.IsVisible = !CreatePanel.IsVisible;
        CloseCreateButton.Click += (_, _) => CreatePanel.IsVisible = false;
        BuildWithAiButton.Click += async (_, _) => await BuildWithAiAsync();
        CreateButton.Click += async (_, _) => await CreateAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            var appsTask = _modes.GetModesAsync(CancellationToken.None);
            var capabilityTask = _capabilities.GetCapabilitiesAsync(CancellationToken.None);
            await Task.WhenAll(appsTask, capabilityTask);
            _apps = (await appsTask).Where(app => app.IsEnabled).OrderBy(app => app.Name).ToArray();
            OwnerAppBox.ItemsSource = new[] { new OwnerOption(CapabilityRegistryCatalog.GeneralOwner, "General") }
                .Concat(_apps.Select(app => new OwnerOption(app.Key, app.Name)))
                .ToArray();
            OwnerAppBox.SelectedIndex = Math.Max(0, OwnerAppBox.SelectedIndex);
            RebuildGroups(await capabilityTask);
            StatusText.Text = $"{(await capabilityTask).Count} capabilities available locally.";
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            StatusText.Text = "Capabilities could not be loaded: " + exception.Message;
        }
    }

    private void RebuildGroups(IReadOnlyList<CapabilityDefinition> capabilities)
    {
        GroupsPanel.Children.Clear();
        foreach (var group in capabilities
                     .OrderBy(item => item.OwnerAppKey.Equals(CapabilityRegistryCatalog.GeneralOwner, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                     .ThenBy(item => item.OwnerAppKey, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                     .GroupBy(item => item.OwnerAppKey, StringComparer.OrdinalIgnoreCase))
        {
            GroupsPanel.Children.Add(new TextBlock
            {
                Text = OwnerDisplayName(group.Key),
                FontSize = 17,
                FontWeight = FontWeight.ExtraBold,
                Margin = new Thickness(2, 4, 0, 0)
            });
            var cards = new WrapPanel { ItemWidth = 360 };
            foreach (var capability in group) cards.Children.Add(BuildCard(capability));
            GroupsPanel.Children.Add(cards);
        }
    }

    private Control BuildCard(CapabilityDefinition capability)
    {
        var icon = new HavenIcon { IconKey = capability.IconKey, Width = 20, Height = 20 };
        var title = new TextBlock { Text = capability.Name, FontSize = 16, FontWeight = FontWeight.ExtraBold };
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
        heading.Children.Add(icon);
        Grid.SetColumn(title, 1);
        heading.Children.Add(title);

        var delete = new HoldToConfirmButton
        {
            Content = "Delete",
            IsVisible = !capability.IsBuiltIn,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        delete.Click += async (_, _) =>
        {
            await _capabilities.DeleteCustomCapabilityAsync(capability.Id, CancellationToken.None);
            await RefreshAsync();
            StatusText.Text = $"Deleted {capability.Name}.";
        };
        AutomationProperties.SetName(delete, $"Hold for five seconds to delete {capability.Name}");

        return new HavenAdaptiveSurface
        {
            Classes = { "card" },
            Width = 344,
            MinHeight = 190,
            Margin = new Thickness(0, 0, 14, 14),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    heading,
                    new TextBlock { Text = capability.Description, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap },
                    new TextBlock
                    {
                        Text = $"{capability.Key} · {capability.RiskClass} · {capability.Availability}",
                        FontSize = 11,
                        FontWeight = FontWeight.Bold,
                        Foreground = ResourceBrush("HavenAccentSecondaryBrush", Colors.Teal)
                    },
                    new TextBlock
                    {
                        Text = $"{capability.ProviderId} → {capability.ImplementationKey} · {capability.Platforms}",
                        Classes = { "muted" },
                        FontSize = 10,
                        TextWrapping = TextWrapping.Wrap
                    },
                    delete
                }
            }
        };
    }

    private async Task BuildWithAiAsync()
    {
        var prompt = BuilderPromptBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            StatusText.Text = "Describe the capability before asking Haven to draft it.";
            return;
        }

        try
        {
            StatusText.Text = "Drafting provider and safety instructions with the selected local model…";
            var models = await _ollama.GetModelsAsync(CancellationToken.None);
            var model = models.FirstOrDefault(item => item.Supports(ToolCapability.Text)) ?? models.FirstOrDefault()
                        ?? throw new InvalidOperationException("No local Ollama model is installed.");
            var result = await _ollama.CompleteAsync(new OllamaChatRequest(
                model.Name,
                [new OllamaMessage("user", $"Write concise provider-routing and safety instructions for a Haven capability with this purpose: {prompt}\nState that observed outcomes must be verified. Return instructions only.")],
                EffortLevel.Medium), CancellationToken.None);
            InstructionsBox.Text = result.Trim();
            if (string.IsNullOrWhiteSpace(DescriptionBox.Text)) DescriptionBox.Text = prompt;
            StatusText.Text = "Draft ready. Review every runtime and risk field before creating it.";
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException)
        {
            StatusText.Text = "AI draft failed: " + exception.Message;
        }
    }

    private async Task CreateAsync()
    {
        try
        {
            var name = Required(NameBox.Text, "Name");
            var key = NormaliseKey(Required(KeyBox.Text, "Stable key"));
            var description = Required(DescriptionBox.Text, "Description");
            var implementation = Required(ImplementationKeyBox.Text, "Implementation key");
            var provider = Required(ProviderBox.Text, "Provider ID");
            var instructions = Required(InstructionsBox.Text, "Instructions");
            var owner = OwnerAppBox.SelectedItem as OwnerOption
                        ?? throw new InvalidOperationException("Choose an owning App or General.");
            var actions = ValidateJsonArray(SemanticActionsBox.Text, "Semantic actions");
            var dependencies = ValidateJsonArray(DependenciesBox.Text, "Dependencies");

            var existing = (await _capabilities.GetCapabilitiesAsync(CancellationToken.None))
                .FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
                throw new InvalidOperationException($"Capability key '{key}' is already registered.");

            await _capabilities.UpsertCapabilityAsync(new CapabilityDefinition(
                GuidUtility.FromStableName("haven.user.capability." + key),
                key,
                name,
                description,
                owner.Key,
                string.IsNullOrWhiteSpace(IconKeyBox.Text) ? "bolt" : IconKeyBox.Text.Trim(),
                instructions,
                implementation,
                actions,
                PlatformBox.SelectedItem is CapabilityPlatform platforms ? platforms : CapabilityPlatform.All,
                RiskBox.SelectedItem is CapabilityRiskClass risk ? risk : CapabilityRiskClass.Low,
                AvailabilityBox.SelectedItem is CapabilityAvailability availability ? availability : CapabilityAvailability.DependencyRequired,
                dependencies,
                provider,
                AttachableCheck.IsChecked == true,
                AgentUsableCheck.IsChecked == true,
                IsBuiltIn: false,
                IsEnabled: true,
                UpdatedAt: DateTimeOffset.UtcNow), CancellationToken.None);

            ClearCreator();
            await RefreshAsync();
            StatusText.Text = $"Created {name}. It is registered locally; availability and permissions still govern execution.";
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or IOException)
        {
            StatusText.Text = "Could not create capability: " + exception.Message;
        }
    }

    private void ClearCreator()
    {
        foreach (var box in new[] { BuilderPromptBox, NameBox, KeyBox, DescriptionBox, ImplementationKeyBox, InstructionsBox })
            box.Text = string.Empty;
        ProviderBox.Text = "user.declarative";
        IconKeyBox.Text = "bolt";
        SemanticActionsBox.Text = "[]";
        DependenciesBox.Text = "[]";
        AvailabilityBox.SelectedItem = CapabilityAvailability.DependencyRequired;
        AgentUsableCheck.IsChecked = false;
        CreatePanel.IsVisible = false;
    }

    private string OwnerDisplayName(string key) =>
        key.Equals(CapabilityRegistryCatalog.GeneralOwner, StringComparison.OrdinalIgnoreCase)
            ? "General"
            : _apps.FirstOrDefault(app => app.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Name ?? key;

    private static string Required(string? value, string label) =>
        !string.IsNullOrWhiteSpace(value) ? value.Trim() : throw new InvalidOperationException(label + " is required.");

    private static string NormaliseKey(string value)
    {
        var key = string.Join('-', value.Trim().ToLowerInvariant().Split(
            [' ', '_', '-'], StringSplitOptions.RemoveEmptyEntries));
        if (key.Length == 0 || key.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
            throw new InvalidOperationException("Stable key may contain letters, numbers, and hyphens only.");
        return key;
    }

    private static string ValidateJsonArray(string? value, string label)
    {
        var json = string.IsNullOrWhiteSpace(value) ? "[]" : value.Trim();
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException(label + " must be a JSON array.");
        return json;
    }

    private void Register(string name, Control control)
    {
        _bus.RegisterElement(name, control);
        _bus.WirePointerEvents(name, control);
    }

    private static IBrush ResourceBrush(string key, Color fallback) =>
        Avalonia.Application.Current?.TryFindResource(key, out var value) == true && value is IBrush brush
            ? brush
            : new SolidColorBrush(fallback);

    private sealed record OwnerOption(string Key, string Name)
    {
        public override string ToString() => Name;
    }
}
