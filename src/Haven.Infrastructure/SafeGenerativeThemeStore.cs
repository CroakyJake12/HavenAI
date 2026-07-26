using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Provides validated access to the generative theme store and repairs invalid persisted selections.
/// </summary>
public sealed class SafeGenerativeThemeStore(
    GenerativeThemeStore inner,
    IProductionDiagnostics diagnostics) : IGenerativeThemeStore
{
    public Task<IReadOnlyList<GenerativeThemePack>> GetThemesAsync(
        CancellationToken cancellationToken) =>
        inner.GetThemesAsync(cancellationToken);

    public async Task<GenerativeThemeSelection> GetSelectionAsync(
        CancellationToken cancellationToken)
    {
        var selection = await inner.GetSelectionAsync(cancellationToken).ConfigureAwait(false);
        var themes = await inner.GetThemesAsync(cancellationToken).ConfigureAwait(false);
        var validAppearance = Enum.IsDefined(selection.Appearance);
        var activeExists = themes.Any(theme => theme.Id == selection.ActiveThemeId);
        if (validAppearance && activeExists)
        {
            return selection;
        }

        var repairedTheme = activeExists
            ? selection.ActiveThemeId
            : themes.FirstOrDefault(theme => theme.IsBuiltIn)?.Id
              ?? throw new InvalidDataException(
                  "Haven has no built-in Generative UI theme available for selection recovery.");

        var repairedAppearance = validAppearance
            ? selection.Appearance
            : GenerativeThemeAppearance.Dark;

        await inner.SelectAsync(
            repairedTheme,
            repairedAppearance,
            cancellationToken).ConfigureAwait(false);

        await diagnostics.WriteAsync(
            ReliabilitySeverity.Warning,
            "generative-ui",
            "selection-repaired",
            "An unsupported Generative UI selection was repaired before it reached the runtime.",
            new Dictionary<string, string>
            {
                ["legacyThemeId"] = selection.ActiveThemeId.ToString("D"),
                ["legacyAppearanceValue"] = Convert.ToInt32(
                        selection.Appearance,
                        System.Globalization.CultureInfo.InvariantCulture)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["themeWasMissing"] = (!activeExists).ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                ["appearanceWasInvalid"] = (!validAppearance).ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                ["repairedThemeId"] = repairedTheme.ToString("D"),
                ["repairedAppearance"] = repairedAppearance.ToString()
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return selection with
        {
            ActiveThemeId = repairedTheme,
            Appearance = repairedAppearance,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public async Task<GenerativeThemePack> GetActiveThemeAsync(
        CancellationToken cancellationToken)
    {
        // Do not bypass the safe selection boundary: startup commonly asks for the active
        // theme directly, including after files have been restored or manually removed.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var selection = await GetSelectionAsync(cancellationToken).ConfigureAwait(false);
            var themes = await inner.GetThemesAsync(cancellationToken).ConfigureAwait(false);
            var active = themes.FirstOrDefault(theme => theme.Id == selection.ActiveThemeId);
            if (active is not null)
            {
                return active;
            }
        }

        throw new InvalidDataException(
            "The active Generative UI theme could not be resolved after selection recovery.");
    }

    public Task SaveAsync(
        GenerativeThemePack theme,
        CancellationToken cancellationToken) =>
        inner.SaveAsync(theme, cancellationToken);

    public Task RenameAsync(
        Guid themeId,
        string name,
        CancellationToken cancellationToken) =>
        inner.RenameAsync(themeId, name, cancellationToken);

    public Task DeleteAsync(
        Guid themeId,
        CancellationToken cancellationToken) =>
        inner.DeleteAsync(themeId, cancellationToken);

    public Task SelectAsync(
        Guid themeId,
        GenerativeThemeAppearance appearance,
        CancellationToken cancellationToken)
    {
        EnsureAppearance(appearance);
        return inner.SelectAsync(themeId, appearance, cancellationToken);
    }

    public Task SetAppearanceAsync(
        GenerativeThemeAppearance appearance,
        CancellationToken cancellationToken)
    {
        EnsureAppearance(appearance);
        return inner.SetAppearanceAsync(appearance, cancellationToken);
    }

    public Task<string> ExportAsync(
        Guid themeId,
        string destinationDirectory,
        CancellationToken cancellationToken) =>
        inner.ExportAsync(themeId, destinationDirectory, cancellationToken);

    public Task<GenerativeThemePack> ImportAsync(
        string sourcePath,
        CancellationToken cancellationToken) =>
        inner.ImportAsync(sourcePath, cancellationToken);

    private static void EnsureAppearance(GenerativeThemeAppearance appearance)
    {
        if (!Enum.IsDefined(appearance))
        {
            throw new ArgumentOutOfRangeException(
                nameof(appearance),
                "Only explicit Light and Dark theme variants are supported.");
        }
    }
}
