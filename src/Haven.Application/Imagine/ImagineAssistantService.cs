using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>Uses Haven's shared provider runtime for real structural canvas edits without fabricating pixel edits.</summary>
public sealed class ImagineAssistantService(IProviderModelClient models) : IImagineAssistantService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    public async Task<ImagineAssistantEditResult> ProposeEditAsync(ImagineProject project, ImagineAiEditRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project); ArgumentNullException.ThrowIfNull(request);
        if (request.ProjectId != project.Id) return Fail("The AI edit request belongs to a different Imagine project.");
        if (request.Scope.Kind != ImagineSelectionKind.Object || request.Scope.TargetId is not Guid objectId)
            return Fail("Pixel-level generative editing for semantic regions is not installed. Select a canvas object for a real structural edit, or use Vision to inspect the region.");
        var target = project.Objects.FirstOrDefault(item => item.Id == objectId);
        if (target is null) return Fail("The selected canvas object no longer exists.");
        ImagineMediaAsset? asset = target.AssetId is Guid assetId ? project.Assets.FirstOrDefault(item => item.Id == assetId) : null;
        var available = await models.GetModelsAsync(cancellationToken).ConfigureAwait(false);
        var model = asset?.Kind == ImagineMediaKind.Image
            ? available.FirstOrDefault(item => item.Supports(ToolCapability.Vision)) ?? available.FirstOrDefault(item => item.Supports(ToolCapability.Text))
            : available.FirstOrDefault(item => item.Supports(ToolCapability.Text));
        if (model is null) return Fail("No compatible model is available for this structural edit.");
        var prompt = $"""
            Apply this instruction to the selected Imagine canvas object: {request.Instruction}
            Canvas: {project.CanvasWidth:0.##} x {project.CanvasHeight:0.##}
            Object kind: {target.Kind}; name: {target.Name}
            Current transform: x={target.Transform.X:0.##}, y={target.Transform.Y:0.##}, width={target.Transform.Width:0.##}, height={target.Transform.Height:0.##}, rotation={target.Transform.RotationDegrees:0.##}
            Current text: {target.Text}
            Current fill: {target.Fill}
            Return one JSON object only with optional x, y, width, height, rotationDegrees, text, fill.
            Use canvas coordinates. Omit unchanged fields. fill must be #RRGGBB. Never claim to alter image pixels.
            """;
        var images = asset is { Kind: ImagineMediaKind.Image } && File.Exists(asset.ManagedPath) ? new[] { asset.ManagedPath } : null;
        try
        {
            var response = await models.CompleteAsync(new OllamaChatRequest(model.Name, [new OllamaMessage("user", prompt, images)], EffortLevel.Medium, "You are Haven Imagine's structural editor. Return strict JSON only and never claim unsupported pixel edits."), cancellationToken).ConfigureAwait(false);
            var dto = Parse(response);
            if (dto is null) return new(false, "The model did not return a usable structural edit. The project was not changed.", model.Name, objectId, null, null, null);
            var transform = target.Transform with
            {
                X = Finite(dto.X, target.Transform.X), Y = Finite(dto.Y, target.Transform.Y),
                Width = Math.Max(12, Finite(dto.Width, target.Transform.Width)), Height = Math.Max(12, Finite(dto.Height, target.Transform.Height)),
                RotationDegrees = NormaliseDegrees(Finite(dto.RotationDegrees, target.Transform.RotationDegrees))
            };
            var text = target.Kind == ImagineObjectKind.Text && !string.IsNullOrWhiteSpace(dto.Text) ? dto.Text.Trim() : target.Text;
            var fill = ValidFill(dto.Fill) ? dto.Fill!.ToUpperInvariant() : target.Fill;
            return new(true, "Haven proposed a real structural edit for the selected object. Image pixels were not fabricated or replaced.", model.Name, objectId, transform, text, fill);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException or JsonException)
        {
            return new(false, "The structural edit failed without changing the project: " + exception.Message, model.Name, objectId, null, null, null);
        }
    }

    private static ImagineAssistantEditResult Fail(string status) => new(false, status, null, null, null, null, null);
    private static EditDto? Parse(string value) { if (string.IsNullOrWhiteSpace(value)) return null; var start = value.IndexOf('{'); var end = value.LastIndexOf('}'); return start < 0 || end <= start ? null : JsonSerializer.Deserialize<EditDto>(value[start..(end + 1)], JsonOptions); }
    private static double Finite(double? value, double fallback) => value is { } number && double.IsFinite(number) ? number : fallback;
    private static double NormaliseDegrees(double value) { var result = value % 360; return result < -180 ? result + 360 : result > 180 ? result - 360 : result; }
    private static bool ValidFill(string? value) => value is { Length: 7 } && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);
    private sealed record EditDto(double? X, double? Y, double? Width, double? Height, double? RotationDegrees, string? Text, string? Fill);
}
