namespace Haven.Core;

public enum GenerativeThemeAppearance
{
    System = 0,
    Light = 1,
    Dark = 2
}

public enum GenerativeThemeOrigin
{
    BuiltIn = 0,
    Manual = 1,
    AiGenerated = 2,
    Imported = 3
}

public enum GeneratedWidgetKind
{
    Text = 0,
    ShortcutGrid = 1,
    Timer = 2,
    CommandButton = 3,
    Divider = 4
}

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

public sealed record GenerativeThemeTypography(
    string FontFamily,
    double BaseFontSize,
    double HeadingScale,
    double LetterSpacing);

public sealed record GenerativeThemeShape(
    double ControlRadius,
    double CardRadius,
    double SurfaceRadius,
    double SpacingScale,
    bool ShowCardBorders,
    bool UseAcrylic);

public sealed record GenerativeUiPlacement(
    string ItemId,
    string Region,
    int Order,
    bool IsVisible = true,
    string Presentation = "default");

public sealed record GenerativeLayoutManifest(
    IReadOnlyList<GenerativeUiPlacement> Placements,
    IReadOnlyList<string> HiddenPageIds);

public sealed record GeneratedWidgetDefinition(
    string Id,
    GeneratedWidgetKind Kind,
    string Title,
    string? Text,
    string? CommandId,
    int DurationSeconds,
    IReadOnlyList<string> ShortcutCommandIds);

public sealed record GeneratedPageDefinition(
    string Id,
    string Title,
    string Description,
    string IconKey,
    int Order,
    IReadOnlyList<GeneratedWidgetDefinition> Widgets);

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

public sealed record GenerativeThemeSelection(
    int SchemaVersion,
    Guid ActiveThemeId,
    GenerativeThemeAppearance Appearance,
    DateTimeOffset UpdatedAt);

public sealed record GenerativeThemeValidationIssue(
    string Path,
    string Message,
    bool IsError);

public sealed record GenerativeThemeValidationResult(
    bool IsValid,
    GenerativeThemePack? NormalizedTheme,
    IReadOnlyList<GenerativeThemeValidationIssue> Issues);

public sealed record GenerativeThemeProposal(
    GenerativeThemePack Theme,
    string Summary,
    IReadOnlyList<string> Changes,
    IReadOnlyList<string> SafetyNotes);

public sealed record GenerativeUiCatalogItem(
    string Id,
    string DisplayName,
    string Description,
    IReadOnlyList<string> AllowedRegions,
    bool CanHide,
    bool CanMove,
    string DefaultRegion,
    int DefaultOrder);

public sealed record GeneratedCommandDescriptor(
    string Id,
    string DisplayName,
    string Description,
    string IconKey);
