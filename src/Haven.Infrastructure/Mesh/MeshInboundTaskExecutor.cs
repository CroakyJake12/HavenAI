using System.Text;
using Haven.Application;
using Haven.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure;

/// <summary>Runs reasoning-only delegated tasks on a target device without implicitly granting remote tool access.</summary>
public sealed class MeshInboundTaskExecutor(IServiceProvider services) : IMeshInboundTaskExecutor
{
    public async Task<string> ExecuteAsync(MeshTaskEnvelope task, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (task.RequiredCapabilities.Count > 0)
            throw new NotSupportedException("This Mesh build executes delegated reasoning tasks without tools. Capability-bearing work must use an explicitly permissioned DEVICE or remote-agent route.");
        var registry = services.GetRequiredService<IModelProviderRegistry>();
        var candidate = await FindModelAsync(registry, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("No non-Mesh model is available on the target device.");
        var references = task.References.Count == 0
            ? string.Empty
            : "\nReferences supplied by the source device:\n" + string.Join("\n", task.References.Select(reference => $"- {reference.Kind}: {reference.DisplayName ?? reference.Id} ({reference.Id})"));
        var request = new OllamaChatRequest(
            candidate.ModelName,
            [new OllamaMessage("user", task.Instruction + references)],
            EffortLevel.Medium,
            $"You are executing a delegated Haven Mesh task from another explicitly trusted device. Complete only the supplied reasoning task. Do not claim to use files, browser, desktop, commands, or other tools because this execution path grants none. Source surface: {task.SourceSurface}.");
        return await candidate.Provider.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(IModelProvider Provider, string ModelName)?> FindModelAsync(IModelProviderRegistry registry, CancellationToken cancellationToken)
    {
        foreach (var provider in registry.Providers
                     .Where(provider => !string.Equals(provider.Id, MeshRemoteModelProvider.MeshProviderId, StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(provider => provider.IsLocal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var model = (await provider.GetModelsAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault();
                if (model is not null) return (provider, model.Name);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException) { }
        }
        return null;
    }
}
