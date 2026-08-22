using Haven.Application;
using Haven.Application.Automations;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure;

public static class MeshServiceCollectionExtensions
{
    public static IServiceCollection AddHavenMesh(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IMeshStateStore, MeshStateStore>();
        services.AddSingleton<IMeshIdentitySecretStore, MeshIdentitySecretStore>();
        services.AddSingleton<IMeshCapabilitySource, MeshCapabilitySource>();
        services.AddSingleton<IMeshResourceMergeService, MeshResourceStore>();
        services.AddSingleton<IMeshInboundDeviceActionExecutor, MeshInboundDeviceActionExecutor>();
        services.AddSingleton<IMeshDiscoveryService, LanMeshDiscoveryService>();
        services.AddSingleton<IMeshInboundRuntimeExecutor, MeshInboundRuntimeExecutor>();
        services.AddSingleton<IMeshInboundTaskExecutor, MeshInboundTaskExecutor>();
        services.AddSingleton<IMeshFileTransferStore, MeshFileTransferStore>();
        services.AddSingleton<IMeshTransport, SecureLanMeshTransport>();
        services.AddSingleton<MeshCoordinator>();
        services.AddSingleton<MeshRemoteModelProvider>();
        services.AddSingleton<IModelProvider>(provider => provider.GetRequiredService<MeshRemoteModelProvider>());
        services.AddSingleton<MeshDeviceActionProvider>();
        services.AddSingleton<IDeviceActionProvider>(provider => provider.GetRequiredService<MeshDeviceActionProvider>());
        return services;
    }
}
