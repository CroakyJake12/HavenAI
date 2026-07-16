using System.Text.RegularExpressions;
using Haven.Core;

namespace Haven.Application;

public sealed partial class GenerativeThemeValidator : IGenerativeThemeValidator
{
    private const int CurrentSchemaVersion = 1;
    private const int MaximumPages = 12;
    private const int MaximumWidgetsPerPage = 30;
    private static readonly IReadOnlyDictionary<string, GenerativeUiCatalogItem> ItemCatalog =
        GenerativeUiCatalog.Items.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> CommandCatalog =
        GenerativeUiCatalog.PageCommands.Select(command => command.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public GenerativeThemeValidationResult Validate(GenerativeThemePack theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var issues = new List<GenerativeThemeValidationIssue>();

        if (theme.SchemaVersion != CurrentSchemaVersion)
            Error("schemaVersion", $"Theme schema version {theme.SchemaVersion} is not supported.");
        if (theme.Id == Guid.Empty) Error("id", "Theme ID cannot be empty.");
        var normalizedName = NormalizeText(theme.Name, 80);
        if (string.IsNullOrWhiteSpace(normalizedName)) Error("name", "Theme name is required.");
        var normalizedDescription = NormalizeText(theme.Description, 400);
        var normalizedAuthor = NormalizeText(theme.Author, 80);
        if (theme.IsBuiltIn && theme.Origin != GenerativeThemeOrigin.BuiltIn)
            Error("origin", "Only built-in themes may set IsBuiltIn.");

        ValidatePalette(theme.Light, "light");
        ValidatePalette(theme.Dark, "dark");
        ValidateTypography(theme.Typography);
        ValidateShape(theme.Shape);

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
        hiddenPageIds = hiddenPageIds.Where(id => normalizedPages.Any(page => page.Id.Equals(id, StringComparison.OrdinalIgnoreCase))).ToArray();

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

        void Error(string path, string message) => issues.Add(new GenerativeThemeValidationIssue(path, message, true));
        void Warning(string path, string message) => issues.Add(new GenerativeThemeValidationIssue(path, message, false));

        void ValidatePalette(GenerativeThemePalette palette, string path)
        {
            if (palette is null)
            {
                Error(path, "Both light and dark palettes are required.");
                return;
            }
            foreach (var property in typeof(GenerativeThemePalette).GetProperties())
            {
                var value = property.GetValue(palette) as string;
                if (!ColorPattern().IsMatch(value ?? string.Empty))
                    Error(path + "." + ToCamelCase(property.Name), "Use #RRGGBB or #AARRGGBB colour notation.");
            }
        }

        void ValidateTypography(GenerativeThemeTypography typography)
        {
            if (typography is null)
            {
                Error("typography", "Typography settings are required.");
                return;
            }
            if (string.IsNullOrWhiteSpace(typography.FontFamily)) Error("typography.fontFamily", "A font family is required.");
            if (typography.BaseFontSize is < 10 or > 24) Error("typography.baseFontSize", "Base font size must be between 10 and 24.");
            if (typography.HeadingScale is < 1 or > 2.5) Error("typography.headingScale", "Heading scale must be between 1 and 2.5.");
            if (typography.LetterSpacing is < -1 or > 5) Error("typography.letterSpacing", "Letter spacing must be between -1 and 5.");
        }

        void ValidateShape(GenerativeThemeShape shape)
        {
            if (shape is null)
            {
                Error("shape", "Shape settings are required.");
                return;
            }
            if (shape.ControlRadius is < 0 or > 40) Error("shape.controlRadius", "Control radius must be between 0 and 40.");
            if (shape.CardRadius is < 0 or > 48) Error("shape.cardRadius", "Card radius must be between 0 and 48.");
            if (shape.SurfaceRadius is < 0 or > 56) Error("shape.surfaceRadius", "Surface radius must be between 0 and 56.");
            if (shape.SpacingScale is < 0.7 or > 1.8) Error("shape.spacingScale", "Spacing scale must be between 0.7 and 1.8.");
        }
    }

    private static IReadOnlyList<GenerativeUiPlacement> ValidatePlacements(
        IReadOnlyList<GenerativeUiPlacement> placements,
        List<GenerativeThemeValidationIssue> issues)
    {
        var normalized = new List<GenerativeUiPlacement>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var placement in placements.Take(64))
        {
            var itemId = NormalizeId(placement.ItemId);
            var region = NormalizeId(placement.Region);
            if (!ItemCatalog.TryGetValue(itemId, out var item))
            {
                issues.Add(new GenerativeThemeValidationIssue($"layout.placements[{itemId}]", "Unknown UI item. Themes cannot invent controls or binding paths.", true));
                continue;
            }
            if (!seen.Add(itemId))
            {
                issues.Add(new GenerativeThemeValidationIssue($"layout.placements[{itemId}]", "Each UI item may appear at most once.", true));
                continue;
            }
            if (!item.AllowedRegions.Contains(region, StringComparer.OrdinalIgnoreCase))
            {
                issues.Add(new GenerativeThemeValidationIssue($"layout.placements[{itemId}].region", "That item cannot be moved to the requested region.", true));
                continue;
            }
            if (!placement.IsVisible && !item.CanHide)
            {
                issues.Add(new GenerativeThemeValidationIssue($"layout.placements[{itemId}].isVisible", "This functional control is required and cannot be hidden.", true));
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
        return normalized.OrderBy(item => item.Region, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Order).ToArray();
    }

    private static IReadOnlyList<GeneratedPageDefinition> ValidatePages(
        IReadOnlyList<GeneratedPageDefinition> pages,
        List<GenerativeThemeValidationIssue> issues)
    {
        var result = new List<GeneratedPageDefinition>();
        var pageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in pages.Take(MaximumPages))
        {
            var id = NormalizeId(page.Id);
            if (!IdentifierPattern().IsMatch(id))
            {
                issues.Add(new GenerativeThemeValidationIssue("pages.id", "Page IDs must use lowercase letters, numbers and hyphens.", true));
                continue;
            }
            if (!pageIds.Add(id))
            {
                issues.Add(new GenerativeThemeValidationIssue($"pages[{id}]", "Page IDs must be unique.", true));
                continue;
            }
            var widgets = ValidateWidgets(id, page.Widgets ?? [], issues);
            result.Add(new GeneratedPageDefinition(
                id,
                NormalizeText(page.Title, 80),
                NormalizeText(page.Description, 240),
                NormalizeId(page.IconKey),
                Math.Clamp(page.Order, 0, 10_000),
                widgets));
        }
        if (pages.Count > MaximumPages)
            issues.Add(new GenerativeThemeValidationIssue("pages", $"A theme can add at most {MaximumPages} safe pages.", true));
        return result.OrderBy(page => page.Order).ThenBy(page => page.Title, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<GeneratedWidgetDefinition> ValidateWidgets(
        string pageId,
        IReadOnlyList<GeneratedWidgetDefinition> widgets,
        List<GenerativeThemeValidationIssue> issues)
    {
        var result = new List<GeneratedWidgetDefinition>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var widget in widgets.Take(MaximumWidgetsPerPage))
        {
            var id = NormalizeId(widget.Id);
            if (!IdentifierPattern().IsMatch(id) || !ids.Add(id))
            {
                issues.Add(new GenerativeThemeValidationIssue($"pages[{pageId}].widgets", "Widget IDs must be unique lowercase identifiers.", true));
                continue;
            }
            var commandId = NormalizeId(widget.CommandId ?? string.Empty);
            var shortcuts = (widget.ShortcutCommandIds ?? [])
                .Select(NormalizeId)
                .Where(command => CommandCatalog.Contains(command))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToArray();
            switch (widget.Kind)
            {
                case GeneratedWidgetKind.CommandButton when !CommandCatalog.Contains(commandId):
                    issues.Add(new GenerativeThemeValidationIssue($"pages[{pageId}].widgets[{id}].commandId", "Command buttons must use an approved Haven command ID.", true));
                    continue;
                case GeneratedWidgetKind.ShortcutGrid when shortcuts.Length == 0:
                    issues.Add(new GenerativeThemeValidationIssue($"pages[{pageId}].widgets[{id}].shortcutCommandIds", "Shortcut grids require at least one approved command.", true));
                    continue;
                case GeneratedWidgetKind.Timer when widget.DurationSeconds is < 5 or > 86_400:
                    issues.Add(new GenerativeThemeValidationIssue($"pages[{pageId}].widgets[{id}].durationSeconds", "Timers must be between 5 seconds and 24 hours.", true));
                    continue;
            }
            result.Add(new GeneratedWidgetDefinition(
                id,
                widget.Kind,
                NormalizeText(widget.Title, 100),
                NormalizeText(widget.Text, 2_000),
                string.IsNullOrWhiteSpace(commandId) ? null : commandId,
                Math.Clamp(widget.DurationSeconds, 0, 86_400),
                shortcuts));
        }
        if (widgets.Count > MaximumWidgetsPerPage)
            issues.Add(new GenerativeThemeValidationIssue($"pages[{pageId}].widgets", $"A page can contain at most {MaximumWidgetsPerPage} widgets.", true));
        return result;
    }

    private static string NormalizeText(string? value, int maximumLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        normalized = new string(normalized.Where(character => !char.IsControl(character) || character is '\n' or '\t').ToArray());
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private static string NormalizeId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    private static string NormalizePresentation(string? value) => NormalizeId(value) switch
    {
        "compact" => "compact",
        "labelled" => "labelled",
        "icon" => "icon",
        _ => "default"
    };

    private static string ToCamelCase(string value) => char.ToLowerInvariant(value[0]) + value[1..];

    [GeneratedRegex("^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$")]
    private static partial Regex ColorPattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$")]
    private static partial Regex IdentifierPattern();
}
