/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/GenerativeThemeStore.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns GenerativeThemeStore, BuiltInThemes. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Represents generative theme store and keeps its related state and behavior together.
/// </summary>
public sealed class GenerativeThemeStore(
    IAppPaths paths,
    IGenerativeThemeValidator validator,
    IProductionDiagnostics diagnostics) : IGenerativeThemeStore
{
    /// <summary>
    /// Stores selection schema version locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int SelectionSchemaVersion = 1;
    /// <summary>
    /// Stores json options locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
    /// <summary>
    /// Stores gate locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);
    /// <summary>
    /// Stores root locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _root = Path.Combine(paths.DataDirectory, "GenerativeUi");
    /// <summary>
    /// Stores themes directory locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _themesDirectory = Path.Combine(paths.DataDirectory, "GenerativeUi", "Themes");
    /// <summary>
    /// Stores selection path locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _selectionPath = Path.Combine(paths.DataDirectory, "GenerativeUi", "selection.json");

    /// <summary>
    /// Retrieves themes async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<GenerativeThemePack>> GetThemesAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await GetThemesCoreAsync(cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Retrieves selection async for the current operation.
    /// </summary>
    public async Task<GenerativeThemeSelection> GetSelectionAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await GetSelectionCoreAsync(cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Retrieves active theme async for the current operation.
    /// </summary>
    public async Task<GenerativeThemePack> GetActiveThemeAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var themes = await GetThemesCoreAsync(cancellationToken).ConfigureAwait(false);
            var selection = await GetSelectionCoreAsync(cancellationToken).ConfigureAwait(false);
            return themes.FirstOrDefault(theme => theme.Id == selection.ActiveThemeId) ?? BuiltInThemes.Default;
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Performs save async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task SaveAsync(GenerativeThemePack theme, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(theme);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var validation = validator.Validate(theme);
            if (!validation.IsValid || validation.NormalizedTheme is null)
                throw new InvalidDataException(FormatIssues(validation.Issues));
            var normalized = validation.NormalizedTheme;
            if (normalized.IsBuiltIn || BuiltInThemes.All.Any(item => item.Id == normalized.Id))
                throw new InvalidOperationException("Built-in Haven themes are immutable. Duplicate one before editing it.");
            Directory.CreateDirectory(_themesDirectory);
            await WriteAtomicJsonAsync(ThemePath(normalized.Id), normalized with
            {
                IsBuiltIn = false,
                Origin = normalized.Origin == GenerativeThemeOrigin.BuiltIn ? GenerativeThemeOrigin.Manual : normalized.Origin,
                UpdatedAt = DateTimeOffset.UtcNow
            }, cancellationToken).ConfigureAwait(false);
            await diagnostics.WriteAsync(
                ReliabilitySeverity.Information,
                "generative-ui",
                "theme-saved",
                "A validated Generative UI theme was saved.",
                new Dictionary<string, string>
                {
                    ["themeId"] = normalized.Id.ToString("D"),
                    ["name"] = normalized.Name,
                    ["pageCount"] = normalized.Pages.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Performs rename async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task RenameAsync(Guid themeId, string name, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureCustom(themeId);
            var theme = await ReadThemeFileAsync(ThemePath(themeId), cancellationToken).ConfigureAwait(false)
                        ?? throw new FileNotFoundException("The theme no longer exists.");
            var normalizedName = NormalizeName(name);
            await WriteAtomicJsonAsync(ThemePath(themeId), theme with
            {
                Name = normalizedName,
                UpdatedAt = DateTimeOffset.UtcNow
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Performs delete async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task DeleteAsync(Guid themeId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureCustom(themeId);
            var path = ThemePath(themeId);
            if (File.Exists(path)) File.Delete(path);
            var selection = await GetSelectionCoreAsync(cancellationToken).ConfigureAwait(false);
            if (selection.ActiveThemeId == themeId)
                await WriteAtomicJsonAsync(_selectionPath, selection with
                {
                    ActiveThemeId = BuiltInThemes.Default.Id,
                    UpdatedAt = DateTimeOffset.UtcNow
                }, cancellationToken).ConfigureAwait(false);
            await diagnostics.WriteAsync(
                ReliabilitySeverity.Information,
                "generative-ui",
                "theme-deleted",
                "A custom Generative UI theme was deleted.",
                new Dictionary<string, string> { ["themeId"] = themeId.ToString("D") },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Performs select async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task SelectAsync(Guid themeId, GenerativeThemeAppearance appearance, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var themes = await GetThemesCoreAsync(cancellationToken).ConfigureAwait(false);
            if (!themes.Any(theme => theme.Id == themeId)) throw new FileNotFoundException("The selected theme does not exist.");
            await WriteAtomicJsonAsync(_selectionPath, new GenerativeThemeSelection(
                SelectionSchemaVersion,
                themeId,
                appearance,
                DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Performs set appearance async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task SetAppearanceAsync(GenerativeThemeAppearance appearance, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var selection = await GetSelectionCoreAsync(cancellationToken).ConfigureAwait(false);
            await WriteAtomicJsonAsync(_selectionPath, selection with
            {
                Appearance = appearance,
                UpdatedAt = DateTimeOffset.UtcNow
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Performs export async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<string> ExportAsync(Guid themeId, string destinationDirectory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(destinationDirectory)) throw new ArgumentException("A destination directory is required.", nameof(destinationDirectory));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var theme = (await GetThemesCoreAsync(cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(item => item.Id == themeId)
                ?? throw new FileNotFoundException("The selected theme does not exist.");
            var destination = Path.GetFullPath(destinationDirectory);
            Directory.CreateDirectory(destination);
            var fileName = SanitizeFileName(theme.Name) + ".haven-theme.json";
            var path = UniquePath(destination, fileName);
            await WriteNewJsonAsync(path, theme with { IsBuiltIn = false }, cancellationToken).ConfigureAwait(false);
            return path;
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Performs import async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<GenerativeThemePack> ImportAsync(string sourcePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) throw new ArgumentException("A source theme file is required.", nameof(sourcePath));
        var fullPath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("The theme file does not exist.", fullPath);
        if (!fullPath.EndsWith(".haven-theme.json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only .haven-theme.json files can be imported.");
        if (new FileInfo(fullPath).Length > 2 * 1024 * 1024)
            throw new InvalidDataException("Theme files are limited to 2 MB.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            GenerativeThemePack imported;
            try
            {
                await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 32 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                imported = await JsonSerializer.DeserializeAsync<GenerativeThemePack>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                           ?? throw new InvalidDataException("The theme file was empty.");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("The theme file is not valid JSON.", ex);
            }
            var candidate = imported with
            {
                Id = Guid.NewGuid(),
                IsBuiltIn = false,
                Origin = GenerativeThemeOrigin.Imported,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                Name = NormalizeName(imported.Name)
            };
            var validation = validator.Validate(candidate);
            if (!validation.IsValid || validation.NormalizedTheme is null)
                throw new InvalidDataException(FormatIssues(validation.Issues));
            await WriteAtomicJsonAsync(ThemePath(validation.NormalizedTheme.Id), validation.NormalizedTheme, cancellationToken).ConfigureAwait(false);
            return validation.NormalizedTheme;
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Retrieves themes core async for the current operation.
    /// </summary>
    private async Task<IReadOnlyList<GenerativeThemePack>> GetThemesCoreAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_themesDirectory);
        var result = new List<GenerativeThemePack>(BuiltInThemes.All);
        foreach (var path in Directory.EnumerateFiles(_themesDirectory, "*.haven-theme.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                     .Take(256))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var theme = await ReadThemeFileAsync(path, cancellationToken).ConfigureAwait(false);
            if (theme is not null) result.Add(theme);
        }
        return result
            .GroupBy(theme => theme.Id)
            .Select(group => group.First())
            .OrderByDescending(theme => theme.IsBuiltIn)
            .ThenBy(theme => theme.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Retrieves selection core async for the current operation.
    /// </summary>
    private async Task<GenerativeThemeSelection> GetSelectionCoreAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_root);
        if (!File.Exists(_selectionPath))
        {
            var initial = new GenerativeThemeSelection(
                SelectionSchemaVersion,
                BuiltInThemes.Default.Id,
                GenerativeThemeAppearance.Dark,
                DateTimeOffset.UtcNow);
            await WriteAtomicJsonAsync(_selectionPath, initial, cancellationToken).ConfigureAwait(false);
            return initial;
        }
        try
        {
            await using var stream = new FileStream(_selectionPath, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var selection = await JsonSerializer.DeserializeAsync<GenerativeThemeSelection>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (selection is null || selection.SchemaVersion != SelectionSchemaVersion || selection.ActiveThemeId == Guid.Empty)
                throw new InvalidDataException("The theme selection is invalid.");
            return selection;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Quarantine(_selectionPath);
            await diagnostics.WriteAsync(
                ReliabilitySeverity.Warning,
                "generative-ui",
                "selection-quarantined",
                "The Generative UI selection was unreadable and was reset to Haven Default.",
                new Dictionary<string, string> { ["exceptionType"] = ex.GetType().Name },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var fallback = new GenerativeThemeSelection(
                SelectionSchemaVersion,
                BuiltInThemes.Default.Id,
                GenerativeThemeAppearance.Dark,
                DateTimeOffset.UtcNow);
            await WriteAtomicJsonAsync(_selectionPath, fallback, cancellationToken).ConfigureAwait(false);
            return fallback;
        }
    }

    /// <summary>
    /// Performs read theme file async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<GenerativeThemePack?> ReadThemeFileAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("Theme file exceeded 2 MB.");
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 32 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var theme = await JsonSerializer.DeserializeAsync<GenerativeThemePack>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                        ?? throw new InvalidDataException("Theme file was empty.");
            var validation = validator.Validate(theme with { IsBuiltIn = false });
            if (!validation.IsValid || validation.NormalizedTheme is null)
                throw new InvalidDataException(FormatIssues(validation.Issues));
            return validation.NormalizedTheme;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Quarantine(path);
            await diagnostics.WriteAsync(
                ReliabilitySeverity.Warning,
                "generative-ui",
                "theme-quarantined",
                "An invalid custom Generative UI theme was quarantined.",
                new Dictionary<string, string>
                {
                    ["fileName"] = Path.GetFileName(path),
                    ["exceptionType"] = ex.GetType().Name
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return null;
        }
    }

    /// <summary>
    /// Performs the theme path step owned by this component.
    /// </summary>
    private string ThemePath(Guid id) => Path.Combine(_themesDirectory, id.ToString("N") + ".haven-theme.json");

    /// <summary>
    /// Performs the ensure custom step owned by this component.
    /// </summary>
    private static void EnsureCustom(Guid id)
    {
        if (BuiltInThemes.All.Any(theme => theme.Id == id))
            throw new InvalidOperationException("Built-in Haven themes cannot be renamed or deleted.");
    }

    /// <summary>
    /// Performs the normalize name step owned by this component.
    /// </summary>
    private static string NormalizeName(string? value)
    {
        var name = string.IsNullOrWhiteSpace(value) ? "Custom theme" : value.Trim();
        name = new string(name.Where(character => !char.IsControl(character)).ToArray());
        return name.Length <= 80 ? name : name[..80];
    }

    /// <summary>
    /// Performs the sanitize file name step owned by this component.
    /// </summary>
    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var normalized = new string(value.Where(character => !invalid.Contains(character) && !char.IsControl(character)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "haven-theme" : normalized[..Math.Min(80, normalized.Length)];
    }

    /// <summary>
    /// Performs the unique path step owned by this component.
    /// </summary>
    private static string UniquePath(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        if (!File.Exists(candidate)) return candidate;
        var stem = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(fileName));
        for (var index = 2; index < 10_000; index++)
        {
            candidate = Path.Combine(directory, $"{stem}-{index}.haven-theme.json");
            if (!File.Exists(candidate)) return candidate;
        }
        throw new IOException("Could not allocate a unique theme export name.");
    }

    /// <summary>
    /// Performs the format issues step owned by this component.
    /// </summary>
    private static string FormatIssues(IReadOnlyList<GenerativeThemeValidationIssue> issues) =>
        "Theme validation failed: " + string.Join("; ", issues.Where(issue => issue.IsError).Take(12).Select(issue => issue.Path + ": " + issue.Message));

    private static async Task WriteAtomicJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        var backup = path + ".bak";
        try
        {
            await WriteNewJsonAsync(temp, value, cancellationToken).ConfigureAwait(false);
            if (File.Exists(path)) File.Replace(temp, path, backup, ignoreMetadataErrors: true);
            else File.Move(temp, path);
        }
        finally { TryDelete(temp); }
    }

    private static async Task WriteNewJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 32 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    /// <summary>
    /// Performs the quarantine step owned by this component.
    /// </summary>
    private static void Quarantine(string path)
    {
        if (!File.Exists(path)) return;
        var quarantine = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N");
        try { File.Move(path, quarantine, overwrite: false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Attempts to delete and reports the result without using failure for normal control flow.
    /// </summary>
    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>
/// Represents built in themes and keeps its related state and behavior together.
/// </summary>
internal static class BuiltInThemes
{
    /// <summary>
    /// Gets or updates default, the bindable or domain state represented by this property.
    /// </summary>
    public static GenerativeThemePack Default { get; } = Create(
        Guid.Parse("a87db32e-5ff7-4a70-96b1-6264e807db10"),
        "Haven Default",
        "The balanced Windows 11 Haven appearance.",
        LightPalette(
            background: "#FFF4F6F9",
            panel: "#FFFDFDFE",
            accent: "#FF0078D4"),
        DarkPalette(
            background: "#FF181818",
            panel: "#FF20242D",
            accent: "#FF0078D4"));

    /// <summary>
    /// Gets or updates midnight, the bindable or domain state represented by this property.
    /// </summary>
    public static GenerativeThemePack Midnight { get; } = Create(
        Guid.Parse("e1337929-c3fd-4da8-8895-b17e8e84701b"),
        "Midnight Studio",
        "A deep blue-green workspace with quieter contrast.",
        LightPalette("#FFF2F7F7", "#FFFAFDFD", "#FF087F8C"),
        DarkPalette("#FF0D161B", "#FF142229", "#FF28A6A6"));

    /// <summary>
    /// Gets or updates all, the bindable or domain state represented by this property.
    /// </summary>
    public static IReadOnlyList<GenerativeThemePack> All { get; } = [Default, Midnight];

    /// <summary>
    /// Creates this member with the invariants required by its callers.
    /// </summary>
    private static GenerativeThemePack Create(
        Guid id,
        string name,
        string description,
        GenerativeThemePalette light,
        GenerativeThemePalette dark)
    {
        var timestamp = new DateTimeOffset(2026, 7, 16, 0, 0, 0, TimeSpan.Zero);
        return new GenerativeThemePack(
            1,
            id,
            name,
            description,
            "Haven",
            GenerativeThemeOrigin.BuiltIn,
            true,
            timestamp,
            timestamp,
            light,
            dark,
            new GenerativeThemeTypography("Segoe UI Variable, Segoe UI, Montserrat, sans-serif", 14, 1.35, 0),
            new GenerativeThemeShape(10, 14, 16, 1, false, true),
            GenerativeUiCatalog.DefaultLayout,
            []);
    }

    /// <summary>
    /// Performs the light palette step owned by this component.
    /// </summary>
    private static GenerativeThemePalette LightPalette(string background, string panel, string accent) => new(
        background,
        "#FFF7F9FC",
        panel,
        "#FFF1F4F8",
        "#FFE8EDF3",
        "#FFE1E7EE",
        "#FF16191E",
        "#FF343A44",
        "#FF5F6875",
        "#FF7D8795",
        accent,
        "#FFFFFFFF",
        "#FFDCEEFF",
        "#FF0067B8",
        "#FFD8ECFA",
        "#FFB42335",
        "#FF8B5C00",
        "#24000000",
        "#3D000000",
        accent,
        "#FFF4F6F9",
        "#FFF4F6F9",
        "#E6FFFFFF",
        "#FFFFFFFF",
        "#FFE7EBF0",
        "#800078D4");

    /// <summary>
    /// Performs the dark palette step owned by this component.
    /// </summary>
    private static GenerativeThemePalette DarkPalette(string background, string panel, string accent) => new(
        background,
        "#FF1B2028",
        panel,
        "#FF242B35",
        "#FF2C3541",
        "#FF343E4B",
        "#FFFFFFFF",
        "#FFD6DCE5",
        "#FFA0A8B4",
        "#FF717B88",
        accent,
        "#FFFFFFFF",
        "#FF263A52",
        "#FF60CDFF",
        "#FF1E3A50",
        "#FFFF99A4",
        "#FFFCE4A6",
        "#24FFFFFF",
        "#3DFFFFFF",
        accent,
        "#FF182234",
        "#F2182234",
        "#2EFFFFFF",
        "#46FFFFFF",
        "#20FFFFFF",
        "#A060CDFF");
}
