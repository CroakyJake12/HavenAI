using System.Text.Json;
using Haven.Application;
using Haven.Desktop.HavenUI.GenerativeUi;
using Haven.UI.Components;

namespace Haven.Desktop.Views.Pages.Spaces;

/// <summary>Renders persisted Space UI configuration through Haven's existing trusted GenUI runtimes.</summary>
internal sealed class SpaceGeneratedSurfaceRenderer
{
    private const int MaximumInputsLength = 8_192;
    private readonly GenerativeUiEventRouter _router;
    private readonly GenUiInstanceStore _instances;
    private readonly ChecklistTemplateRuntime _checklist;
    private readonly DataGridTemplateRuntime _dataGrid;
    private readonly CardDeckTemplateRuntime _cardDeck;
    private readonly DashboardTemplateRuntime _dashboard;
    private readonly AssessmentTemplateRuntime _assessment;
    private readonly WorkflowTemplateRuntime _workflow;
    private readonly CustomTemplateRuntime _custom;

    public SpaceGeneratedSurfaceRenderer(
        GenerativeUiEventRouter router,
        GenUiInstanceStore instances,
        ChecklistTemplateRuntime checklist,
        DataGridTemplateRuntime dataGrid,
        CardDeckTemplateRuntime cardDeck,
        DashboardTemplateRuntime dashboard,
        AssessmentTemplateRuntime assessment,
        WorkflowTemplateRuntime workflow,
        CustomTemplateRuntime custom)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _checklist = checklist ?? throw new ArgumentNullException(nameof(checklist));
        _dataGrid = dataGrid ?? throw new ArgumentNullException(nameof(dataGrid));
        _cardDeck = cardDeck ?? throw new ArgumentNullException(nameof(cardDeck));
        _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
        _assessment = assessment ?? throw new ArgumentNullException(nameof(assessment));
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        _custom = custom ?? throw new ArgumentNullException(nameof(custom));
    }

    public SpaceGeneratedSurfaceMount Render(SpaceDefinition space)
    {
        ArgumentNullException.ThrowIfNull(space);
        var configured = space.GeneratedSurface
            ?? throw new InvalidOperationException("This Space does not have a generated surface configured.");
        var inputs = ParseInputs(configured.InputsJson);
        var appKey = $"space:{space.Id:N}";
        var document = configured.TemplateKey.Trim().ToLowerInvariant() switch
        {
            "checklist" => _checklist.Create(space.Id, appKey, inputs),
            "data-grid" => _dataGrid.Create(space.Id, appKey, inputs),
            "card-deck" => _cardDeck.Create(space.Id, appKey, inputs),
            "dashboard" => _dashboard.Create(space.Id, appKey, inputs),
            "assessment" => _assessment.Create(space.Id, appKey, inputs),
            "workflow" => _workflow.Create(space.Id, appKey, inputs),
            "custom" => _custom.Create(space.Id, appKey, inputs),
            _ => throw new InvalidOperationException($"Generated Space template '{configured.TemplateKey}' is not supported.")
        };

        var surface = new HavenGenUiSceneSurface(_router, _instances);
        try
        {
            surface.Present(document);
            return new SpaceGeneratedSurfaceMount(surface, _instances, document.Origin.InstanceId);
        }
        catch
        {
            surface.Dispose();
            _instances.Remove(document.Origin.InstanceId);
            throw;
        }
    }

    internal static IReadOnlyDictionary<string, JsonElement> ParseInputs(string? json)
    {
        var payload = string.IsNullOrWhiteSpace(json) ? "{}" : json.Trim();
        if (payload.Length > MaximumInputsLength)
            throw new InvalidOperationException("Generated Space inputs exceed the safe size limit.");

        using var document = JsonDocument.Parse(payload, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 24
        });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Generated Space inputs must be a JSON object.");

        var properties = document.RootElement.EnumerateObject().ToArray();
        if (properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
            throw new InvalidOperationException("Generated Space inputs contain a duplicate field.");
        return properties.ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
    }
}

internal sealed class SpaceGeneratedSurfaceMount : IDisposable
{
    private readonly HavenGenUiSceneSurface _surface;
    private readonly GenUiInstanceStore _instances;
    private readonly Guid _instanceId;
    private bool _disposed;

    public SpaceGeneratedSurfaceMount(HavenGenUiSceneSurface surface, GenUiInstanceStore instances, Guid instanceId)
    {
        _surface = surface;
        _instances = instances;
        _instanceId = instanceId;
    }

    public Container Root => _surface.Root;
    public Guid InstanceId => _instanceId;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _surface.Dispose();
        _instances.Remove(_instanceId);
    }
}
