namespace Haven.Core;

/// <summary>
/// The four colour-only appearances supported by the canonical HavenUI design system.
/// Numeric order matches the discrete Settings brightness slider.
/// </summary>
public enum HavenUiAppearance
{
    SuperBright = 0,
    Bright = 1,
    Dark = 2,
    SuperDark = 3
}

/// <summary>Lists the supported generated widget kinds.</summary>
public enum GeneratedWidgetKind
{
    Text = 0,
    ShortcutGrid = 1,
    Timer = 2,
    CommandButton = 3,
    Divider = 4
}

/// <summary>Represents a generated UI placement.</summary>
public sealed record GenerativeUiPlacement(
    string ItemId,
    string Region,
    int Order,
    bool IsVisible = true,
    string Presentation = "default");

/// <summary>Represents a generated layout manifest.</summary>
public sealed record GenerativeLayoutManifest(
    IReadOnlyList<GenerativeUiPlacement> Placements,
    IReadOnlyList<string> HiddenPageIds);

/// <summary>Represents a generated widget definition.</summary>
public sealed record GeneratedWidgetDefinition(
    string Id,
    GeneratedWidgetKind Kind,
    string Title,
    string? Text,
    string? CommandId,
    int DurationSeconds,
    IReadOnlyList<string> ShortcutCommandIds);

/// <summary>Represents a generated page definition.</summary>
public sealed record GeneratedPageDefinition(
    string Id,
    string Title,
    string Description,
    string IconKey,
    int Order,
    IReadOnlyList<GeneratedWidgetDefinition> Widgets);

/// <summary>Represents a generated UI catalog item.</summary>
public sealed record GenerativeUiCatalogItem(
    string Id,
    string DisplayName,
    string Description,
    IReadOnlyList<string> AllowedRegions,
    bool CanHide,
    bool CanMove,
    string DefaultRegion,
    int DefaultOrder);

/// <summary>Represents a generated command descriptor.</summary>
public sealed record GeneratedCommandDescriptor(
    string Id,
    string DisplayName,
    string Description,
    string IconKey);
