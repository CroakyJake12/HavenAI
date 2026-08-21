namespace Haven.Core;

public enum ImagineMediaKind { Image = 0, Audio = 1, Video = 2 }
public enum ImagineObjectKind { Image = 0, Rectangle = 1, Ellipse = 2, Text = 3, Stroke = 4 }
public enum ImagineTrackKind { Visual = 0, Audio = 1, Video = 2 }
public enum ImagineSelectionKind
{
    None = 0,
    WholeProject = 1,
    Media = 2,
    Object = 3,
    SemanticComponent = 4,
    Region = 5,
    Frame = 6,
    Clip = 7,
    TimelineRange = 8,
    AudioRegion = 9,
    Track = 10
}

public sealed record ImagineRegion(double X, double Y, double Width, double Height);
public sealed record ImagineTransform(double X, double Y, double Width, double Height, double RotationDegrees = 0);

public sealed record ImagineMediaAsset(
    Guid Id,
    ImagineMediaKind Kind,
    string Name,
    string OriginalSourcePath,
    string ManagedPath,
    long SizeBytes,
    string Sha256,
    DateTimeOffset CreatedAt,
    string MetadataJson = "{}");

public sealed record ImagineEditableObject(
    Guid Id,
    ImagineObjectKind Kind,
    string Name,
    Guid? AssetId,
    ImagineTransform Transform,
    int ZIndex,
    string Text,
    string Fill,
    bool IsVisible,
    bool IsLocked,
    Guid? SemanticComponentId = null,
    string MetadataJson = "{}");

public sealed record ImagineSemanticComponent(
    Guid Id,
    Guid AssetId,
    Guid? ParentId,
    string Key,
    string Label,
    string Type,
    ImagineRegion Bounds,
    int Order,
    string? MaskPath,
    double? Confidence,
    string Provenance,
    string? Model,
    string MetadataJson = "{}");

public sealed record ImagineClip(
    Guid Id,
    Guid AssetId,
    string Name,
    double TimelineStartSeconds,
    double SourceStartSeconds,
    double DurationSeconds,
    double Gain = 1,
    bool IsMuted = false);

public sealed record ImagineTrack(
    Guid Id,
    ImagineTrackKind Kind,
    string Name,
    int Order,
    bool IsMuted,
    double Gain,
    ImagineClip[] Clips);

public sealed record ImagineSelectionScope(
    ImagineSelectionKind Kind,
    Guid? TargetId = null,
    ImagineRegion? Region = null,
    double? StartSeconds = null,
    double? EndSeconds = null);

public sealed record ImagineEditRecord(
    Guid Id,
    string Operation,
    ImagineSelectionScope Scope,
    string Provenance,
    string? Model,
    string BeforeJson,
    string AfterJson,
    DateTimeOffset CreatedAt);

public sealed record ImagineProject(
    Guid Id,
    string Name,
    double CanvasWidth,
    double CanvasHeight,
    ImagineMediaAsset[] Assets,
    ImagineTrack[] Tracks,
    ImagineEditableObject[] Objects,
    ImagineSemanticComponent[] SemanticComponents,
    ImagineSelectionScope Selection,
    ImagineEditRecord[] History,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ImagineAiEditRequest(
    Guid ProjectId,
    string Instruction,
    ImagineSelectionScope Scope,
    Guid[] AssetIds,
    DateTimeOffset CreatedAt);
