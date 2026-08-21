using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Uses the shared Haven provider client to turn an image into persistent semantic
/// bounds. It never invents masks: a model must return usable geometry and mask
/// generation remains a separately capability-gated operation.
/// </summary>
public sealed class ImagineSemanticService(IProviderModelClient models) : IImagineSemanticService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<ImagineSemanticDecompositionResult> DecomposeImageAsync(
        ImagineProject project,
        Guid assetId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        var asset = project.Assets.FirstOrDefault(item => item.Id == assetId && item.Kind == ImagineMediaKind.Image);
        if (asset is null)
            return new(false, "Select an imported image before decomposing it.", null, []);
        var available = await models.GetModelsAsync(cancellationToken).ConfigureAwait(false);
        var model = available.FirstOrDefault(item => item.Supports(ToolCapability.Vision));
        if (model is null)
            return new(false, "No compatible vision model is available. Semantic decomposition was not changed.", null, []);

        const string system =
            "You are Haven Imagine's visual decomposition engine. Return strict JSON only. " +
            "Describe visible editable semantic parts with normalized bounds. Never claim segmentation masks you did not produce.";
        const string prompt =
            """
            Decompose this image into a useful dynamic hierarchy of editable semantic components.
            Return a JSON array only. Each item must contain:
            key: stable short unique string,
            parentKey: parent key or null,
            label: concise human label,
            type: semantic type,
            x, y, width, height: normalized 0..1 bounds,
            confidence: 0..1.
            Include meaningful foreground/background/object/part hierarchy where visually justified.
            Do not hard-code a face schema and do not invent invisible parts.
            """;

        try
        {
            var response = await models.CompleteAsync(
                new OllamaChatRequest(
                    model.Name,
                    [new OllamaMessage("user", prompt, [asset.ManagedPath])],
                    EffortLevel.Medium,
                    system),
                cancellationToken).ConfigureAwait(false);
            var rows = ParseRows(response);
            if (rows.Length == 0)
                return new(false, "The vision model returned no usable semantic bounds. The project was not changed.", model.Name, []);

            var ids = rows.ToDictionary(
                row => row.Key,
                row => StableId(asset.Id, row.Key),
                StringComparer.OrdinalIgnoreCase);
            var components = rows.Select((row, index) =>
            {
                var x = Math.Clamp(row.X, 0, 1);
                var y = Math.Clamp(row.Y, 0, 1);
                var width = Math.Clamp(row.Width, 0, 1 - x);
                var height = Math.Clamp(row.Height, 0, 1 - y);
                Guid? parent = !string.IsNullOrWhiteSpace(row.ParentKey) && ids.TryGetValue(row.ParentKey, out var parentId)
                    ? parentId
                    : null;
                return new ImagineSemanticComponent(
                    ids[row.Key],
                    asset.Id,
                    parent,
                    row.Key,
                    row.Label.Trim(),
                    row.Type.Trim(),
                    new ImagineRegion(x, y, width, height),
                    index,
                    null,
                    double.IsFinite(row.Confidence) ? Math.Clamp(row.Confidence, 0, 1) : null,
                    "vision-bounds",
                    model.Name);
            }).ToArray();

            return new(
                true,
                $"Found {components.Length} semantic component{(components.Length == 1 ? string.Empty : "s")}. Bounds are editable; segmentation masks were not fabricated.",
                model.Name,
                components);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidOperationException or HttpRequestException)
        {
            return new(false, "Semantic decomposition failed without changing the project: " + exception.Message, model.Name, []);
        }
    }

    internal static SemanticRow[] ParseRows(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        var start = value.IndexOf('[');
        var end = value.LastIndexOf(']');
        if (start < 0 || end <= start) return [];
        var parsed = JsonSerializer.Deserialize<SemanticRow[]>(value[start..(end + 1)], JsonOptions) ?? [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return parsed
            .Where(row =>
                !string.IsNullOrWhiteSpace(row.Key) &&
                !string.IsNullOrWhiteSpace(row.Label) &&
                !string.IsNullOrWhiteSpace(row.Type) &&
                row.Width > 0 &&
                row.Height > 0 &&
                seen.Add(row.Key.Trim()))
            .Select(row => row with { Key = row.Key.Trim(), ParentKey = row.ParentKey?.Trim() })
            .ToArray();
    }

    private static Guid StableId(Guid assetId, string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(assetId.ToString("N") + ":" + key.Trim().ToLowerInvariant()));
        return new Guid(bytes.AsSpan(0, 16));
    }

    internal sealed record SemanticRow(
        string Key,
        string? ParentKey,
        string Label,
        string Type,
        double X,
        double Y,
        double Width,
        double Height,
        double Confidence);
}
