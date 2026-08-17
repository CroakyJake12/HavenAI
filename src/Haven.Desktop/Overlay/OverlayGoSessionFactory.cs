#if !ANDROID
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Go;
using Haven.UI;

namespace Haven.Desktop.Overlay;

/// <summary>
/// Creates the real production Go surface for Overlay and populates it with the same
/// catalogue/suggestion services used by the main Haven shell.
/// </summary>
internal sealed class OverlayGoSessionFactory
{
    private readonly HavenEventBus _bus;
    private readonly ICatalogRepository _catalog;
    private readonly CapabilityRegistryService _capabilities;
    private readonly IModeRegistry _modes;
    private readonly GoSuggestionService _suggestions;

    public OverlayGoSessionFactory(
        HavenEventBus bus,
        ICatalogRepository catalog,
        CapabilityRegistryService capabilities,
        IModeRegistry modes,
        IConversationRepository conversations,
        IOllamaClient models,
        UserPreferencesService preferences)
    {
        _bus = bus;
        _catalog = catalog;
        _capabilities = capabilities;
        _modes = modes;
        _suggestions = new GoSuggestionService(conversations, models, preferences);
    }

    public async Task<GoPage> CreateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var page = new GoPage(_bus);
        ApplyOverlayPresentation(page);
        page.SetSuggestions(GoSuggestionService.ImmediateDefaults);
        await PopulateCatalogueAsync(page, cancellationToken);
        return page;
    }

    public async Task RefreshSuggestionsAsync(
        GoPage page,
        string activity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        page.SetRefreshInProgress(true);
        try
        {
            var generated = await _suggestions.GenerateAsync(activity, cancellationToken).ConfigureAwait(false);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => page.SetSuggestions(generated));
        }
        finally
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => page.SetRefreshInProgress(false));
        }
    }

    private async Task PopulateCatalogueAsync(GoPage page, CancellationToken cancellationToken)
    {
        var agentsTask = _catalog.GetAgentsAsync(cancellationToken);
        var capabilityTask = _capabilities.DiscoverAsync(CapabilityPlatform.Windows, cancellationToken);
        var promptsTask = _catalog.GetPromptsAsync(cancellationToken);
        var appsTask = _modes.GetModesAsync(cancellationToken);
        await Task.WhenAll(agentsTask, capabilityTask, promptsTask, appsTask);
        page.SetAddCatalogue(
            await agentsTask,
            await capabilityTask,
            await promptsTask,
            await appsTask);
    }

    private static void ApplyOverlayPresentation(GoPage page)
    {
        page.SceneRoot.SetValue(HavenProperties.Background, "SurfaceRaised");
        page.SceneRoot.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(26)));
        page.SceneRoot.SetValue(HavenProperties.Shadow, "Card");
        page.SceneRoot.SetValue(HavenProperties.Clip, true);
    }
}
#endif
