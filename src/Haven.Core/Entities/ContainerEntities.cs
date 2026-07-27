namespace Haven.Core;

/// <summary>
/// Represents a container definition.
/// </summary>
public sealed record ContainerDefinition(
    Guid Id,
    HavenMode Mode,
    string Name,
    string? RootPath,
    string Context,
    string Instructions,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsArchived = false);

/// <summary>
/// Represents a lesson.
/// </summary>
public sealed record Lesson(
    Guid Id,
    Guid SubjectId,
    string TopicGroup,
    string Name,
    string StructureJson,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Represents a container resource.
/// </summary>
public sealed record ContainerResource(
    Guid Id,
    Guid ContainerId,
    string Name,
    string StoredName,
    string MediaType,
    ContainerResourceKind Kind,
    long SizeBytes,
    string Sha256,
    DateTimeOffset CreatedAt);
