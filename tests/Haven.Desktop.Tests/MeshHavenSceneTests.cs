using System.Reflection;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Pages.Mesh;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class MeshHavenSceneTests
{
    [Fact]
    public void Scene_exposes_pairing_and_switches_to_work_mode_team_surface()
    {
        var coordinator = new MeshCoordinator(new EmptyStateStore(), new EmptySecrets(), new EmptyTransport(), new EmptyCapabilities(), new EmptyMerge());
        using var scene = new MeshHavenScene(new MeshPageViewModel(coordinator));

        AssertNamed(scene.Root, "Mesh.Pairing.Generate");
        AssertNamed(scene.Root, "Mesh.Pairing.IncomingOffer");
        AssertNamed(scene.Root, "Mesh.Pairing.Accept");
        Assert.Equal(ButtonVariant.Primary, scene.DevicesTab.Variant);
        Assert.Equal(ButtonVariant.Ghost, scene.WorkModeTab.Variant);

        Invoke(scene.WorkModeTab);

        Assert.Equal(ButtonVariant.Ghost, scene.DevicesTab.Variant);
        Assert.Equal(ButtonVariant.Primary, scene.WorkModeTab.Variant);
        AssertNamed(scene.Root, "Mesh.Work.Creator");
        AssertNamed(scene.Root, "Mesh.Work.Creator.Runtime");
        AssertNamed(scene.Root, "Mesh.Work.Team.Input");
        AssertNamed(scene.Root, "Mesh.Work.Team.Send");
        AssertNamed(scene.Root, "Mesh.Work.Coordinator.Input");
        AssertNamed(scene.Root, "Mesh.Work.Coordinator.Run");
        AssertNamed(scene.Root, "Mesh.Work.Transcript");
        Assert.DoesNotContain(scene.Root.DescendantsAndSelf(), element => element.Name == "Mesh.Pairing.Generate");
    }

    private static HavenElement AssertNamed(HavenElement root, string name) =>
        Assert.Single(root.DescendantsAndSelf(), element => element.Name == name);

    private static void Invoke(HavenElement element)
    {
        var method = typeof(HavenElement).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(element, null);
    }

    private sealed class EmptyStateStore : IMeshStateStore
    {
        public Task<MeshPersistentState> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(MeshPersistentState.Empty);
        public Task SaveAsync(MeshPersistentState state, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class EmptySecrets : IMeshIdentitySecretStore
    {
        public Task<string?> GetPrivateKeyAsync(Guid deviceId, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task SetPrivateKeyAsync(Guid deviceId, string privateKey, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeletePrivateKeyAsync(Guid deviceId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class EmptyTransport : IMeshTransport
    {
        public event Action<MeshTransportPeer>? PeerObserved { add { } remove { } }
        public event Action<MeshTransportPeer>? PairingCompleted { add { } remove { } }
        public event Action<Guid, MeshConnectionState>? ConnectionChanged { add { } remove { } }
        public event Action<MeshTransportMessage>? MessageReceived { add { } remove { } }
        public bool IsRunning => false;
        public string? LocalEndpoint => null;
        public Task StartAsync(MeshLocalIdentity localIdentity, string privateKey, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetPairingChallengeAsync(MeshPairingChallenge? challenge, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetTrustedPeersAsync(IReadOnlyList<MeshPeerRecord> peers, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<MeshTransportPeer> PairAsync(string endpoint, Guid challengeId, string verificationCode, string expectedRemoteFingerprint, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ConnectAsync(MeshPeerRecord peer, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DisconnectAsync(Guid peerDeviceId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SendAsync(Guid peerDeviceId, string kind, string payload, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class EmptyCapabilities : IMeshCapabilitySource
    {
        public Task<IReadOnlyList<MeshCapabilityDescriptor>> GetLocalCapabilitiesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MeshCapabilityDescriptor>>([]);
    }

    private sealed class EmptyMerge : IMeshResourceMergeService
    {
        public Task<MeshResourceSnapshot?> GetCurrentAsync(string resourceType, Guid resourceId, CancellationToken cancellationToken) => Task.FromResult<MeshResourceSnapshot?>(null);
        public Task<bool> TryApplyAsync(MeshSyncMutation mutation, CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
