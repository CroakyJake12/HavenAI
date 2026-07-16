using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class SafeGenerativeThemeStore(
    GenerativeThemeStore inner,
    IProductionDiagnostics diagnostics) : IGenerativeThemeStore
{
    public Task<IReadOnlyList<GenerativeThemePack>> GetThemesAsync(CancellationToken cancellationToken) =>
        inner.GetThemesAsync(cancellationToken);

    public async Task<GenerativeThemeSelection> GetSelectionAsync(CancellationToken cancellationToken)
    {
        var selection = await inner.GetSelectionAsync(cancellationToken).ConfigureAwait(false);
        if (Enum.IsDefined(selection.Appearance)) return selection;

        await inner.SelectAsync(
            selection.ActiveThemeId,
            GenerativeThemeAppearance.Dark,
            cancellationToken).ConfigureAwait(false);
        await diagnostics.WriteAsync(
            ReliabilitySeverity.Warning,
            "generative-ui",
            "legacy-appearance-repaired",
            "An unsupported legacy Generative UI appearance value was repaired to Dark.",
            new Dictionary<string, string>
            {
                ["legacyValue"] = Convert.ToInt32(selection.Appearance, System.Globalization.CultureInfo.InvariantCulture)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture)
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return selection with
        {
            Appearance = GenerativeThemeAppearance.Dark,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public Task<GenerativeThemePack> GetActiveThemeAsync(CancellationToken cancellationToken) =>
        inner.GetActiveThemeAsync(cancellationToken);

    public Task SaveAsync(GenerativeThemePack theme, CancellationToken cancellationToken) =>
        inner.SaveAsync(theme, cancellationToken);

    public Task RenameAsync(Guid themeId, string name, CancellationToken cancellationToken) =>
        inner.RenameAsync(themeId, name, cancellationToken);

    public Task DeleteAsync(Guid themeId, CancellationToken cancellationToken) =>
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
            throw new ArgumentOutOfRangeException(nameof(appearance), "Only explicit Light and Dark theme variants are supported.");
    }
}
