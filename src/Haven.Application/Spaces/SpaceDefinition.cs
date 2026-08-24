namespace Haven.Application;

public enum SpaceKind
{
    General = 0,
    Study = 1,
    Shopping = 2,
    Research = 3
}

public enum SpaceThinkingMode
{
    Default = 0,
    Fast = 1,
    Balanced = 2,
    Deep = 3
}

public enum SpaceFilePermission
{
    ReadOnly = 0,
    ReadWrite = 1
}

public sealed record SpaceExamplePair(string User, string Assistant);

public sealed record SpaceFileReference(
    string Path,
    string DisplayName,
    SpaceFilePermission Permission,
    DateTimeOffset AddedAt);

public sealed record SpaceGeneratedSurface(
    string TemplateKey,
    string InputsJson);

public sealed record SpaceDefinition(
    Guid Id,
    string Name,
    string Description,
    string IconKey,
    SpaceKind Kind,
    bool IsBuiltIn,
    bool IsArchived,
    string? ModelName,
    string Instructions,
    SpaceThinkingMode ThinkingMode,
    IReadOnlyList<SpaceExamplePair> ExamplePairs,
    IReadOnlyList<SpaceFileReference> Files,
    SpaceGeneratedSurface? GeneratedSurface,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? ForkedFromSpaceId = null,
    SpaceLayoutDocument? LayoutDocument = null);

internal sealed record SpaceRegistryState(int Version, IReadOnlyList<SpaceDefinition> Spaces);
