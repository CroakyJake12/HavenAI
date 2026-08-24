#if !ANDROID
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Chat;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Overlay;

/// <summary>Creates real production Chat surfaces for independent Overlay sessions.</summary>
internal sealed class OverlayChatSessionFactory(
    IServiceProvider services,
    IConversationRepository conversations,
    CapabilityRegistryService capabilities,
    ICatalogRepository catalog,
    IModeRegistry modes)
{
    public async Task<NewChatPage> CreateNewAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var page = ActivatorUtilities.CreateInstance<NewChatPage>(services);
        await page.StartFreshConversationAsync();
        await PopulateCatalogueAsync(page, cancellationToken);
        return page;
    }

    public async Task<NewChatPage?> RestoreAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = await conversations.GetAsync(conversationId, cancellationToken);
        if (conversation is null) return null;
        var page = ActivatorUtilities.CreateInstance<NewChatPage>(services);
        await page.LoadConversationAsync(conversation);
        await PopulateCatalogueAsync(page, cancellationToken);
        return page;
    }

    public async Task<CapabilityDefinition?> FindCapabilityAsync(OverlayContextActionDescriptor action, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(action.ImplementationKey)) return null;
        var available = await capabilities.DiscoverAsync(CapabilityPlatform.Windows, cancellationToken);
        return available.FirstOrDefault(capability =>
            capability.ImplementationKey.Equals(action.ImplementationKey, StringComparison.OrdinalIgnoreCase));
    }

    private async Task PopulateCatalogueAsync(NewChatPage page, CancellationToken cancellationToken)
    {
        var agents = await catalog.GetAgentsAsync(cancellationToken);
        var prompts = await catalog.GetPromptsAsync(cancellationToken);
        var availableCapabilities = await capabilities.DiscoverAsync(CapabilityPlatform.Windows, cancellationToken);
        var apps = await modes.GetModesAsync(cancellationToken);
        page.SetAddCatalogue(agents, availableCapabilities, prompts, apps);
    }
}
#endif
