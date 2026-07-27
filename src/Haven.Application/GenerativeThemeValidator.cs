/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/GenerativeThemeValidator.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns GenerativeThemeValidator. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Globalization;
using System.Text.RegularExpressions;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Represents generative theme validator and keeps its related state and behavior together.
/// </summary>
public sealed partial class GenerativeThemeValidator : IGenerativeThemeValidator
{
    /// <summary>
    /// Stores current schema version locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int CurrentSchemaVersion = 1;
    /// <summary>
    /// Stores maximum pages locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int MaximumPages = 12;
    /// <summary>
    /// Stores maximum widgets per page locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int MaximumWidgetsPerPage = 30;
    /// <summary>
    /// Stores maximum placements locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int MaximumPlacements = 64;
    /// <summary>
    /// Stores maximum shortcuts locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int MaximumShortcuts = 12;
    /// <summary>
    /// Stores normal text contrast locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const double NormalTextContrast = 4.5d;
    /// <summary>
    /// Stores secondary text contrast locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const double SecondaryTextContrast = 3d;

    /// <summary>
    /// Stores item catalog locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, GenerativeUiCatalogItem> ItemCatalog =
        GenerativeUiCatalog.Items.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Stores command catalog locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly HashSet<string> CommandCatalog =
        GenerativeUiCatalog.PageCommands
            .Select(command => command.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Validates this member before it crosses the next trust or persistence boundary.
    /// </summary>
    public GenerativeThemeValidationResult Validate(GenerativeThemePack theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var issues = new List<GenerativeThemeValidationIssue>();

        if (theme.SchemaVersion != CurrentSchemaVersion)
            Error("schemaVersion", $"Theme schema version {theme.SchemaVersion} is not supported.");
        if (theme.Id == Guid.Empty)
            Error("id", "Theme ID cannot be empty.");
        if (!Enum.IsDefined(theme.Origin))
            Error("origin", "Theme origin is not recognised.");
        if (theme.IsBuiltIn && theme.Origin != GenerativeThemeOrigin.BuiltIn)
            Error("origin", "Only built-in themes may set IsBuiltIn.");
        if (!theme.IsBuiltIn && theme.Origin == GenerativeThemeOrigin.BuiltIn)
            Error("origin", "Custom and imported themes cannot claim the built-in origin.");

        var normalizedName = NormalizeText(theme.Name, 80);
        if (string.IsNullOrWhiteSpace(normalizedName))
            Error("name", "Theme name is required.");
        var normalizedDescription = NormalizeText(theme.Description, 400);
        var normalizedAuthor = NormalizeText(theme.Author, 80);

        ValidatePalette(theme.Light, "light");
        ValidatePalette(theme.Dark, "dark");
        ValidateTypography(theme.Typography);
        ValidateShape(theme.Shape);

        if (theme.Layout is null)
            Error("layout", "A layout manifest is required.");
        if (theme.Pages is null)
            Error("pages", "The pages array is required, even when it is empty.");

        var normalizedPlacements = ValidatePlacements(theme.Layout?.Placements ?? [], issues);
        var normalizedPages = ValidatePages(theme.Pages ?? [], issues);
        var hiddenPageIds = (theme.Layout?.HiddenPageIds ?? [])
            .Select(NormalizeId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var hidden in hiddenPageIds)
        {
            if (!normalizedPages.Any(page => page.Id.Equals(hidden, StringComparison.OrdinalIgnoreCase)))
                Warning($"layout.hiddenPageIds[{hidden}]", "Hidden page ID does not refer to a page in this theme and was ignored.");
        }

        hiddenPageIds = hiddenPageIds
            .Where(id => normalizedPages.Any(page => page.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (issues.Any(issue => issue.IsError))
            return new GenerativeThemeValidationResult(false, null, issues);

        var now = DateTimeOffset.UtcNow;
        var normalized = theme with
        {
            SchemaVersion = CurrentSchemaVersion,
            Name = normalizedName,
            Description = normalizedDescription,
            Author = normalizedAuthor,
            UpdatedAt = theme.UpdatedAt == default ? now : theme.UpdatedAt,
            CreatedAt = theme.CreatedAt == default ? now : theme.CreatedAt,
            Typography = theme.Typography with
            {
                FontFamily = NormalizeText(theme.Typography.FontFamily, 160)
            },
            Layout = new GenerativeLayoutManifest(normalizedPlacements, hiddenPageIds),
            Pages = normalizedPages
        };

        return new GenerativeThemeValidationResult(true, normalized, issues);

        void Error(string path, string message) =>
            issues.Add(new GenerativeThemeValidationIssue(path, message, true));

        void Warning(string path, string message) =>
            issues.Add(new GenerativeThemeValidationIssue(path, message, false));

        void ValidatePalette(GenerativeThemePalette? palette, string path)
        {
            if (palette is null)
            {
                Error(path, "Both light and dark palettes are required.");
                return;
            }

            var valid = true;
            foreach (var property in typeof(GenerativeThemePalette).GetProperties())
            {
                var value = property.GetValue(palette) as string;
                if (ColorPattern().IsMatch(value ?? string.Empty)) continue;
                valid = false;
                Error(path + "." + ToCamelCase(property.Name), "Use #RRGGBB or #AARRGGBB colour notation.");
            }

            if (!valid) return;

            var textContrast = ContrastRatio(palette.Text, palette.Background);
            if (textContrast < NormalTextContrast)
                Error(path + ".text", $"Primary text contrast is {textContrast:0.00}:1; at least {NormalTextContrast:0.0}:1 is required.");

            var softContrast = ContrastRatio(palette.TextSoft, palette.Background);
            if (softContrast < NormalTextContrast)
                Error(path + ".textSoft", $"Soft text contrast is {softContrast:0.00}:1; at least {NormalTextContrast:0.0}:1 is required.");

            var mutedContrast = ContrastRatio(palette.Muted, palette.Background);
            if (mutedContrast < SecondaryTextContrast)
                Error(path + ".muted", $"Muted text contrast is {mutedContrast:0.00}:1; at least {SecondaryTextContrast:0.0}:1 is required.");

            var accentContrast = ContrastRatio(palette.AccentInk, palette.Accent);
            if (accentContrast < NormalTextContrast)
                Error(path + ".accentInk", $"Accent text contrast is {accentContrast:0.00}:1; at least {NormalTextContrast:0.0}:1 is required.");
        }

        void ValidateTypography(GenerativeThemeTypography? typography)
        {
            if (typography is null)
            {
                Error("typography", "Typography settings are required.");
                return;
            }

            var family = NormalizeText(typography.FontFamily, 160);
            if (string.IsNullOrWhiteSpace(family))
                Error("typography.fontFamily", "A font family is required.");
            else if (!FontFamilyPattern().IsMatch(family))
                Error("typography.fontFamily", "Font families may contain names separated by commas, but not URIs, paths or resource references.");

            if (typography.BaseFontSize is < 10 or > 24)
                Error("typography.baseFontSize", "Base font size must be between 10 and 24.");
            if (typography.HeadingScale is < 1 or > 2.5)
                Error("typography.headingScale", "Heading scale must be between 1 and 2.5.");
            if (typography.LetterSpacing is < -1 or > 5)
                Error("typography.letterSpacing", "Letter spacing must be between -1 and 5.");
        }

        void ValidateShape(GenerativeThemeShape? shape)
        {
            if (shape is null)
            {
                Error("shape", "Shape settings are required.");
                return;
            }

            if (shape.ControlRadius is < 0 or > 40)
                Error("shape.controlRadius", "Control radius must be between 0 and 40.");
            if (shape.CardRadius is < 0 or > 48)
                Error("shape.cardRadius", "Card radius must be between 0 and 48.");
            if (shape.SurfaceRadius is < 0 or > 56)
                Error("shape.surfaceRadius", "Surface radius must be between 0 and 56.");
            if (shape.SpacingScale is < 0.7 or > 1.8)
                Error("shape.spacingScale", "Spacing scale must be between 0.7 and 1.8.");
        }
    }

    /// <summary>
    /// Validates placements before it crosses the next trust or persistence boundary.
    /// </summary>
    private static IReadOnlyList<GenerativeUiPlacement> ValidatePlacements(
        IReadOnlyList<GenerativeUiPlacement> placements,
        List<GenerativeThemeValidationIssue> issues)
    {
        if (placements.Count > MaximumPlacements)
            issues.Add(new GenerativeThemeValidationIssue(
                "layout.placements",
                $"A theme can contain at most {MaximumPlacements} placements.",
                true));

        var normalized = new List<GenerativeUiPlacement>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var placement in placements.Take(MaximumPlacements))
        {
            if (placement is null)
            {
                issues.Add(new GenerativeThemeValidationIssue("layout.placements", "Null placement entries are not allowed.", true));
                continue;
            }

            var itemId = NormalizeId(placement.ItemId);
            var region = NormalizeId(placement.Region);
            if (!ItemCatalog.TryGetValue(itemId, out var item))
            {
                issues.Add(new GenerativeThemeValidationIssue(
                    $"layout.placements[{itemId}]",
                    "Unknown UI item. Themes cannot invent controls or binding paths.",
                    true));
                continue;
            }

            if (!seen.Add(itemId))
            {
                issues.Add(new GenerativeThemeValidationIssue(
                    $"layout.placements[{itemId}]",
                    "Each UI item may appear at most once.",
                    true));
                continue;
            }

            if (!item.AllowedRegions.Contains(region, StringComparer.OrdinalIgnoreCase))
            {
                issues.Add(new GenerativeThemeValidationIssue(
                    $"layout.placements[{itemId}].region",
                    "That item cannot be moved to the requested region.",
                    true));
                continue;
            }

            if (!placement.IsVisible && !item.CanHide)
            {
                issues.Add(new GenerativeThemeValidationIssue(
                    $"layout.placements[{itemId}].isVisible",
                    "This functional control is required and cannot be hidden.",
                    true));
                continue;
            }

            normalized.Add(new GenerativeUiPlacement(
                item.Id,
                region,
                Math.Clamp(placement.Order, 0, 10_000),
                placement.IsVisible,
                NormalizePresentation(placement.Presentation)));
        }

        foreach (var item in GenerativeUiCatalog.Items)
        {
            if (seen.Contains(item.Id)) continue;
            normalized.Add(new GenerativeUiPlacement(
                item.Id,
                item.DefaultRegion,
                item.DefaultOrder,
                IsVisible: !item.CanHide,
                Presentation: "default"));
        }

        return normalized
            .OrderBy(item => item.Region, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Order)
            .ToArray();
    }

    /// <summary>
    /// Validates pages before it crosses the next trust or persistence boundary.
    /// </summary>
    private static IReadOnlyList<GeneratedPageDefinition> ValidatePages(
        IReadOnlyList<GeneratedPageDefinition> pages,
        List<GenerativeThemeValidationIssue> issues)
    {
        if (pages.Count > MaximumPages)
            issues.Add(new GenerativeThemeValidationIssue(
                "pages",
                $"A theme can add at most {MaximumPages} safe pages.",
                true));

        var result = new List<GeneratedPageDefinition>();
        var pageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in pages.Take(MaximumPages))
        {
            if (page is null)
            {
                issues.Add(new GenerativeThemeValidationIssue("pages", "Null page entries are not allowed.", true));
                continue;
            }

            var id = NormalizeId(page.Id);
            if (!IdentifierPattern().IsMatch(id))
            {
                issues.Add(new GenerativeThemeValidationIssue(
                    "pages.id",
                    "Page IDs must use lowercase letters, numbers and hyphens.",
                    true));
                continue;
            }

            if (!pageIds.Add(id))
            {
                issues.Add(new GenerativeThemeValidationIssue(
                    $"pages[{id}]",
                    "Page IDs must be unique.",
                    true));
                continue;
            }

            var title = NormalizeText(page.Title, 80);
            if (string.IsNullOrWhiteSpace(title))
            {
                issues.Add(new GenerativeThemeValidationIssue(
                    $"pages[{id}].title",
                    "Generated pages require a visible title.",
                    true));
                continue;
            }

            var iconKey = NormalizeId(page.IconKey);
            if (!IdentifierPattern().IsMatch(iconKey))
            {
                issues.Add(new GenerativeThemeValidationIssue(
                    $"pages[{id}].iconKey",
                    "Page icon keys must use lowercase letters, numbers and hyphens.",
                    true));
                continue;
            }

            var widgets = ValidateWidgets(id, page.Widgets ?? [], issues);
            if (widgets.Count == 0)
            {
                issues.Add(new GenerativeThemeValidationIssue(
                    $"pages[{id}].widgets",
                    "Generated pages must contain at least one functional or informational widget.",
                    true));
                continue;
            }

            result.Add(new GeneratedPageDefinition(
                id,
                title,
                NormalizeText(page.Description, 240),
                iconKey,
                Math.Clamp(page.Order, 0, 10_000),
                widgets));
        }

        return result
            .OrderBy(page => page.Order)
            .ThenBy(page => page.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Validates widgets before it crosses the next trust or persistence boundary.
    /// </summary>
    private static IReadOnlyList<GeneratedWidgetDefinition> ValidateWidgets(
        string pageId,
        IReadOnlyList<GeneratedWidgetDefinition> widgets,
        List<GenerativeThemeValidationIssue> issues)
    {
        if (widgets.Count > MaximumWidgetsPerPage)
            issues.Add(new GenerativeThemeValidationIssue(
                $"pages[{pageId}].widgets",
                $"A page can contain at most {MaximumWidgetsPerPage} widgets.",
                true));

        var result = new List<GeneratedWidgetDefinition>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var widget in widgets.Take(MaximumWidgetsPerPage))
        {
            if (widget is null)
            {
                issues.Add(new GenerativeThemeValidationIssue(
                    $"pages[{pageId}].widgets",
                    "Null widget entries are not allowed.",
                    true));
                continue;
            }

            var id = NormalizeId(widget.Id);
            if (!IdentifierPattern().IsMatch(id) || !ids.Add(id))
            {
                issues.Add(new GenerativeThemeValidationIssue(
                    $"pages[{pageId}].widgets",
                    "Widget IDs must be unique lowercase identifiers.",
                    true));
                continue;
            }

            if (!Enum.IsDefined(widget.Kind))
            {
                issues.Add(new GenerativeThemeValidationIssue(
                    $"pages[{pageId}].widgets[{id}].kind",
                    "Unknown generated widget kind.",
                    true));
                continue;
            }

            var title = NormalizeText(widget.Title, 100);
            if (widget.Kind != GeneratedWidgetKind.Divider && string.IsNullOrWhiteSpace(title))
            {
                issues.Add(new GenerativeThemeValidationIssue(
                    $"pages[{pageId}].widgets[{id}].title",
                    "Widgets require a visible title.",
                    true));
                continue;
            }

            var commandId = NormalizeId(widget.CommandId ?? string.Empty);
            var requestedShortcuts = (widget.ShortcutCommandIds ?? [])
                .Select(NormalizeId)
                .Where(command => !string.IsNullOrWhiteSpace(command))
                .ToArray();

            if (requestedShortcuts.Length > MaximumShortcuts)
            {
                issues.Add(new GenerativeThemeValidationIssue(
                    $"pages[{pageId}].widgets[{id}].shortcutCommandIds",
                    $"Shortcut grids can contain at most {MaximumShortcuts} commands.",
                    true));
                continue;
            }

            var unknownShortcuts = requestedShortcuts
                .Where(command => !CommandCatalog.Contains(command))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (unknownShortcuts.Length > 0)
            {
                issues.Add(new GenerativeThemeValidationIssue(
                    $"pages[{pageId}].widgets[{id}].shortcutCommandIds",
                    "Unknown Haven command IDs are not allowed: " + string.Join(", ", unknownShortcuts),
                    true));
                continue;
            }

            var shortcuts = requestedShortcuts
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            switch (widget.Kind)
            {
                case GeneratedWidgetKind.CommandButton:
                    if (!CommandCatalog.Contains(commandId))
                    {
                        issues.Add(new GenerativeThemeValidationIssue(
                            $"pages[{pageId}].widgets[{id}].commandId",
                            "Command buttons must use an approved Haven command ID.",
                            true));
                        continue;
                    }
                    if (widget.DurationSeconds != 0 || shortcuts.Length != 0)
                    {
                        issues.Add(new GenerativeThemeValidationIssue(
                            $"pages[{pageId}].widgets[{id}]",
                            "Command buttons cannot also declare timer or shortcut-grid data.",
                            true));
                        continue;
                    }
                    break;

                case GeneratedWidgetKind.ShortcutGrid:
                    if (shortcuts.Length == 0)
                    {
                        issues.Add(new GenerativeThemeValidationIssue(
                            $"pages[{pageId}].widgets[{id}].shortcutCommandIds",
                            "Shortcut grids require at least one approved command.",
                            true));
                        continue;
                    }
                    if (!string.IsNullOrWhiteSpace(commandId) || widget.DurationSeconds != 0)
                    {
                        issues.Add(new GenerativeThemeValidationIssue(
                            $"pages[{pageId}].widgets[{id}]",
                            "Shortcut grids cannot also declare a command-button or timer value.",
                            true));
                        continue;
                    }
                    break;

                case GeneratedWidgetKind.Timer:
                    if (widget.DurationSeconds is < 5 or > 86_400)
                    {
                        issues.Add(new GenerativeThemeValidationIssue(
                            $"pages[{pageId}].widgets[{id}].durationSeconds",
                            "Timers must be between 5 seconds and 24 hours.",
                            true));
                        continue;
                    }
                    if (!string.IsNullOrWhiteSpace(commandId) || shortcuts.Length != 0)
                    {
                        issues.Add(new GenerativeThemeValidationIssue(
                            $"pages[{pageId}].widgets[{id}]",
                            "Timers cannot also declare command-button or shortcut-grid data.",
                            true));
                        continue;
                    }
                    break;

                case GeneratedWidgetKind.Text:
                case GeneratedWidgetKind.Divider:
                    if (!string.IsNullOrWhiteSpace(commandId) || widget.DurationSeconds != 0 || shortcuts.Length != 0)
                    {
                        issues.Add(new GenerativeThemeValidationIssue(
                            $"pages[{pageId}].widgets[{id}]",
                            "Text and divider widgets cannot declare executable command or timer data.",
                            true));
                        continue;
                    }
                    break;
            }

            result.Add(new GeneratedWidgetDefinition(
                id,
                widget.Kind,
                title,
                NormalizeText(widget.Text, 2_000),
                widget.Kind == GeneratedWidgetKind.CommandButton ? commandId : null,
                widget.Kind == GeneratedWidgetKind.Timer ? widget.DurationSeconds : 0,
                widget.Kind == GeneratedWidgetKind.ShortcutGrid ? shortcuts : []));
        }

        return result;
    }

    /// <summary>
    /// Performs the normalize text step owned by this component.
    /// </summary>
    private static string NormalizeText(string? value, int maximumLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        normalized = new string(normalized
            .Where(character => !char.IsControl(character) || character is '\n' or '\t')
            .ToArray());
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    /// <summary>
    /// Performs the normalize id step owned by this component.
    /// </summary>
    private static string NormalizeId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    /// <summary>
    /// Performs the normalize presentation step owned by this component.
    /// </summary>
    private static string NormalizePresentation(string? value) => NormalizeId(value) switch
    {
        "compact" => "compact",
        "labelled" => "labelled",
        "icon" => "icon",
        _ => "default"
    };

    /// <summary>
    /// Performs the to camel case step owned by this component.
    /// </summary>
    private static string ToCamelCase(string value) =>
        char.ToLowerInvariant(value[0]) + value[1..];

    /// <summary>
    /// Performs the contrast ratio step owned by this component.
    /// </summary>
    private static double ContrastRatio(string foreground, string background)
    {
        var foregroundLuminance = RelativeLuminance(ParseRgb(foreground));
        var backgroundLuminance = RelativeLuminance(ParseRgb(background));
        var lighter = Math.Max(foregroundLuminance, backgroundLuminance);
        var darker = Math.Min(foregroundLuminance, backgroundLuminance);
        return (lighter + 0.05d) / (darker + 0.05d);
    }

    private static (double Red, double Green, double Blue) ParseRgb(string colour)
    {
        var start = colour.Length == 9 ? 3 : 1;
        return (
            byte.Parse(colour.AsSpan(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d,
            byte.Parse(colour.AsSpan(start + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d,
            byte.Parse(colour.AsSpan(start + 4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d);
    }

    /// <summary>
    /// Performs the relative luminance step owned by this component.
    /// </summary>
    private static double RelativeLuminance((double Red, double Green, double Blue) colour)
    {
        static double Linear(double channel) =>
            channel <= 0.04045d
                ? channel / 12.92d
                : Math.Pow((channel + 0.055d) / 1.055d, 2.4d);

        return 0.2126d * Linear(colour.Red)
               + 0.7152d * Linear(colour.Green)
               + 0.0722d * Linear(colour.Blue);
    }

    /// <summary>
    /// Performs the color pattern step owned by this component.
    /// </summary>
    [GeneratedRegex("^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$")]
    private static partial Regex ColorPattern();

    /// <summary>
    /// Performs the identifier pattern step owned by this component.
    /// </summary>
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$")]
    private static partial Regex IdentifierPattern();

    /// <summary>
    /// Performs the font family pattern step owned by this component.
    /// </summary>
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9 ,.'-]{0,159}$")]
    private static partial Regex FontFamilyPattern();
}
