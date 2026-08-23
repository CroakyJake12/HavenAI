using Haven.Core;

namespace Haven.Application;

public enum ImagineGenerationFailureKind
{
    None = 0,
    ConnectionRequired = 1,
    ProviderError = 2
}

public sealed record ImagineGenerationRequest(
    string Prompt,
    string? ReferenceImagePath = null,
    string Size = "1024x1024",
    string Quality = "medium");

public sealed record ImagineGenerationResult(
    bool Succeeded,
    string Status,
    string? Provider,
    string? Model,
    byte[]? ImageBytes,
    ImagineGenerationFailureKind FailureKind = ImagineGenerationFailureKind.None);

public interface IImagineGenerationService
{
    Task<ImagineGenerationResult> GenerateAsync(ImagineGenerationRequest request, CancellationToken cancellationToken);
}

public sealed record ImagineGenerationCommandResult(
    ImagineGenerationResult Generation,
    ImagineProject? Project,
    Guid? ObjectId)
{
    public bool Succeeded => Generation.Succeeded && Project is not null && ObjectId is not null;
}

/// <summary>Commits a provider result through the same durable Imagine asset/session path used by imported images.</summary>
public sealed class ImagineGenerationCommand(
    IImagineProjectRepository projects,
    IImagineGenerationService generation)
{
    public async Task<ImagineGenerationCommandResult> ExecuteAsync(
        ImagineGenerationRequest request,
        string projectName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new ArgumentException("An image generation prompt is required.", nameof(request));

        var generated = await generation.GenerateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!generated.Succeeded)
            return new ImagineGenerationCommandResult(generated, null, null);
        if (generated.ImageBytes is not { Length: > 0 })
            return new ImagineGenerationCommandResult(generated with
            {
                Succeeded = false,
                Status = "The provider reported success without returning usable image bytes.",
                FailureKind = ImagineGenerationFailureKind.ProviderError
            }, null, null);

        cancellationToken.ThrowIfCancellationRequested();
        var stagingPath = Path.Combine(Path.GetTempPath(), "haven-imagine-generation-" + Guid.NewGuid().ToString("N") + ".png");
        Guid? projectId = null;
        try
        {
            await File.WriteAllBytesAsync(stagingPath, generated.ImageBytes, cancellationToken).ConfigureAwait(false);
            var project = await projects.CreateAsync(projectName, 1600, 1000, cancellationToken).ConfigureAwait(false);
            projectId = project.Id;
            var asset = await projects.ImportAssetAsync(project.Id, stagingPath, ImagineMediaKind.Image, cancellationToken).ConfigureAwait(false);
            var session = new ImagineProjectSession(project);
            session.AddImportedAsset(asset);
            await projects.SaveAsync(session.Project, cancellationToken).ConfigureAwait(false);
            var objectId = session.Project.Selection.Kind == ImagineSelectionKind.Object
                ? session.Project.Selection.TargetId
                : null;
            if (objectId is null)
                throw new InvalidOperationException("The generated image was stored, but Imagine did not create an editable image object.");
            return new ImagineGenerationCommandResult(generated, session.Project, objectId);
        }
        catch
        {
            if (projectId is Guid createdProjectId)
            {
                try { await projects.DeleteAsync(createdProjectId, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            }
            throw;
        }
        finally
        {
            try { if (File.Exists(stagingPath)) File.Delete(stagingPath); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
        }
    }
}
