using Haven.Core;

namespace Haven.Application;

public interface IImagineProjectRepository
{
    Task<ImagineProject> CreateAsync(string name, double canvasWidth, double canvasHeight, CancellationToken cancellationToken);
    Task<ImagineProject?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ImagineProject>> GetRecentAsync(int limit, CancellationToken cancellationToken);
    Task SaveAsync(ImagineProject project, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<ImagineMediaAsset> ImportAssetAsync(Guid projectId, string sourcePath, ImagineMediaKind kind, CancellationToken cancellationToken);
    Task<string> ExportBundleAsync(ImagineProject project, string destinationPath, CancellationToken cancellationToken);
}

public interface IImagineSemanticService
{
    Task<ImagineSemanticDecompositionResult> DecomposeImageAsync(ImagineProject project, Guid assetId, CancellationToken cancellationToken);
}

public interface IImagineAssistantService
{
    Task<ImagineAssistantEditResult> ProposeEditAsync(ImagineProject project, ImagineAiEditRequest request, CancellationToken cancellationToken);
}

public sealed record ImagineSemanticDecompositionResult(bool Succeeded, string Status, string? Model, ImagineSemanticComponent[] Components);

public sealed record ImagineAssistantEditResult(
    bool Succeeded, string Status, string? Model, Guid? ObjectId, ImagineTransform? Transform, string? Text, string? Fill);
