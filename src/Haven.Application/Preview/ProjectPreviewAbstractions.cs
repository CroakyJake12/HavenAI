namespace Haven.Application;

public enum ProjectPreviewKind { Website = 0, Document = 1, Image = 2, Presentation = 3, Application = 4 }

public sealed record ProjectPreviewDescriptor(
    string ProviderId,
    ProjectPreviewKind Kind,
    string DisplayName,
    string ProjectRoot,
    string EntryDescription);

public interface IProjectPreviewSession : IAsyncDisposable
{
    ProjectPreviewDescriptor Descriptor { get; }
    Uri PreviewUri { get; }
    string SafeLogTail { get; }
    event EventHandler? SourceChanged;
}

public interface IProjectPreviewProvider
{
    string Id { get; }
    bool CanPreview(string projectRoot);
    ProjectPreviewDescriptor Describe(string projectRoot);
    Task<IProjectPreviewSession> StartAsync(string projectRoot, CancellationToken cancellationToken);
}
