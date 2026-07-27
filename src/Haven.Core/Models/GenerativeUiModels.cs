// Theme packs, layout manifests, widget definitions, and UI catalog items.

namespace Haven.Core;

/// <summary>
/// Lists the supported generative theme appearance values used to make state explicit and type-safe.
/// </summary>
public enum GenerativeThemeAppearance
{
    Light = 1,
    Dark = 2
}

/// <summary>
/// Lists the supported generative theme origin values used to make state explicit and type-safe.
/// </summary>
public enum GenerativeThemeOrigin
{
    BuiltIn = 0,
    Manual = 1,
    AiGenerated = 2,
    Imported = 3
}

/// <summary>
/// Lists the supported generated widget kind values used to make state explicit and type-safe.
/// </summary>
public enum GeneratedWidgetKind
{
    Text = 0,
    ShortcutGrid = 1,
    Timer = 2,
    CommandButton = 3,
    Divider = 4
}

/// <summary>
/// Represents generative theme palette and keeps its related state and behavior together.
/// </summary>
public sealed record GenerativeThemePalette(
    string Background,
    string Elevated,
    string Panel,
    string Panel2,
    string Panel3,
    string PanelHover,
    string Text,
    string TextSoft,
    string Muted,
    string Muted2,
    string Accent,
    string AccentInk,
    string AccentSoft,
    string Blue,
    string BlueSoft,
    string Danger,
    string Warning,
    string Line,
    string LineStrong,
    string Nub,
    string AcrylicTint,
    string AcrylicFallback,
    string Button,
    string ButtonHover,
    string ButtonPressed,
    string Focus);

/// <summary>
/// Represents generative theme typography and keeps its related state and behavior together.
/// </summary>
public sealed record GenerativeThemeTypography(
    string FontFamily,
    double BaseFontSize,
    double HeadingScale,
    double LetterSpacing);

/// <summary>
/// Represents generative theme shape and keeps its related state and behavior together.
/// </summary>
public sealed record GenerativeThemeShape(
    double ControlRadius,
    double CardRadius,
    double SurfaceRadius,
    double SpacingScale,
    bool ShowCardBorders,
    bool UseAcrylic);

/// <summary>
/// Represents generative ui placement and keeps its related state and behavior together.
/// </summary>
public sealed record GenerativeUiPlacement(
    string ItemId,
    string Region,
    int Order,
    bool IsVisible = true,
    string Presentation = "default");

/// <summary>
/// Represents generative layout manifest and keeps its related state and behavior together.
/// </summary>
public sealed record GenerativeLayoutManifest(
    IReadOnlyList<GenerativeUiPlacement> Placements,
    IReadOnlyList<string> HiddenPageIds);

/// <summary>
/// Represents generated widget definition and keeps its related state and behavior together.
/// </summary>
public sealed record GeneratedWidgetDefinition(
    string Id,
    GeneratedWidgetKind Kind,
    string Title,
    string? Text,
    string? CommandId,
    int DurationSeconds,
    IReadOnlyList<string> ShortcutCommandIds);

/// <summary>
/// Represents generated page definition and keeps its related state and behavior together.
/// </summary>
public sealed record GeneratedPageDefinition(
    string Id,
    string Title,
    string Description,
    string IconKey,
    int Order,
    IReadOnlyList<GeneratedWidgetDefinition> Widgets);

/// <summary>
/// Represents generative theme pack and keeps its related state and behavior together.
/// </summary>
public sealed record GenerativeThemePack(
    int SchemaVersion,
    Guid Id,
    string Name,
    string Description,
    string Author,
    GenerativeThemeOrigin Origin,
    bool IsBuiltIn,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    GenerativeThemePalette Light,
    GenerativeThemePalette Dark,
    GenerativeThemeTypography Typography,
    GenerativeThemeShape Shape,
    GenerativeLayoutManifest Layout,
    IReadOnlyList<GeneratedPageDefinition> Pages);

/// <summary>
/// Represents generative theme selection and keeps its related state and behavior together.
/// </summary>
public sealed record GenerativeThemeSelection(
    int SchemaVersion,
    Guid ActiveThemeId,
    GenerativeThemeAppearance Appearance,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Represents generative theme validation issue and keeps its related state and behavior together.
/// </summary>
public sealed record GenerativeThemeValidationIssue(
    string Path,
    string Message,
    bool IsError);

/// <summary>
/// Represents generative theme validation result and keeps its related state and behavior together.
/// </summary>
public sealed record GenerativeThemeValidationResult(
    bool IsValid,
    GenerativeThemePack? NormalizedTheme,
    IReadOnlyList<GenerativeThemeValidationIssue> Issues);

/// <summary>
/// Represents generative theme proposal and keeps its related state and behavior together.
/// </summary>
public sealed record GenerativeThemeProposal(
    GenerativeThemePack Theme,
    string Summary,
    IReadOnlyList<string> Changes,
    IReadOnlyList<string> SafetyNotes);

/// <summary>
/// Represents generative ui catalog item and keeps its related state and behavior together.
/// </summary>
public sealed record GenerativeUiCatalogItem(
    string Id,
    string DisplayName,
    string Description,
    IReadOnlyList<string> AllowedRegions,
    bool CanHide,
    bool CanMove,
    string DefaultRegion,
    int DefaultOrder);

/// <summary>
/// Represents generated command descriptor and keeps its related state and behavior together.
/// </summary>
public sealed record GeneratedCommandDescriptor(
    string Id,
    string DisplayName,
    string Description,
    string IconKey);
