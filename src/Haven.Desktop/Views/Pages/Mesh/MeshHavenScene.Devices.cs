using Haven.Application;
using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using Container = Haven.UI.Components.Container;
using HavenButton = Haven.UI.Components.Button;

namespace Haven.Desktop.Views.Pages.Mesh;

internal sealed partial class MeshHavenScene
{
    private void BuildDevicesContent(Container parent)
    {
        var pairing = Card("Mesh.Devices.Pairing");
        pairing.Add(Heading("Pair a device", TextLevel.H2));
        pairing.Add(Muted("Pairing is explicit and fingerprint-bound. Share the generated offer with the other Haven device, or paste an offer received from it. Trust does not automatically grant remote execution permissions."));

        var generate = Button("Mesh.Pairing.Generate", "Create pairing offer", ButtonVariant.Primary);
        generate.Invoked += async (_, _) => await RunAndRenderAsync(token => _viewModel.CreatePairingOfferAsync(token));
        pairing.Add(generate);
        if (!string.IsNullOrWhiteSpace(_viewModel.PairingOffer))
        {
            var offer = InputField("Mesh.Pairing.LocalOffer", "Pairing offer", multiline: true);
            offer.Text = _viewModel.PairingOffer;
            offer.Accessibility.AccessibleName = "Generated pairing offer";
            pairing.Add(offer);
        }

        var incoming = InputField("Mesh.Pairing.IncomingOffer", "Paste another device's pairing offer JSON", multiline: true);
        incoming.Text = _viewModel.IncomingPairingOffer;
        incoming.Invalidated += (_, _) => _viewModel.IncomingPairingOffer = incoming.Text;
        pairing.Add(incoming);
        var accept = Button("Mesh.Pairing.Accept", "Pair this device", ButtonVariant.Secondary);
        accept.Invoked += async (_, _) =>
        {
            _viewModel.IncomingPairingOffer = incoming.Text;
            await RunAndRenderAsync(token => _viewModel.AcceptPairingOfferAsync(token));
        };
        pairing.Add(accept);
        parent.Add(pairing);

        var trustHeader = Row();
        trustHeader.Add(Heading("Trusted devices", TextLevel.H2));
        var count = Muted(_viewModel.Devices.Count == 0 ? "No trusted peers yet" : $"{_viewModel.Devices.Count} paired");
        Set(count, HavenProperties.Column, 1);
        trustHeader.Add(count);
        parent.Add(trustHeader);

        if (_viewModel.Devices.Count == 0)
        {
            var empty = Card("Mesh.Devices.Empty");
            empty.Add(Muted("Pair another Haven device to expose presence, handoff, remote models/agents and permissioned device capabilities here."));
            parent.Add(empty);
        }
        else
        {
            foreach (var device in _viewModel.Devices) parent.Add(BuildDeviceCard(device));
        }

        BuildTransferHistory(parent);

        if (_viewModel.NearbyDevices.Count > 0)
        {
            parent.Add(Heading("Nearby untrusted devices", TextLevel.H2));
            foreach (var candidate in _viewModel.NearbyDevices)
            {
                var card = Card("Mesh.Nearby." + candidate.DeviceId.ToString("N"));
                card.Add(Heading(candidate.DisplayName));
                card.Add(Muted($"{candidate.Platform} · {candidate.DeviceClass} · seen {candidate.ObservedAt.LocalDateTime:t}"));
                card.Add(Muted($"Endpoint {candidate.Endpoint} · fingerprint {ShortFingerprint(candidate.PublicKeyFingerprint)}"));
                card.Add(Muted("Nearby discovery is informational only. Explicit pairing and code verification are still required before this device is trusted."));
                parent.Add(card);
            }
        }
    }

    private Container BuildDeviceCard(MeshPeerSnapshot snapshot)
    {
        var peer = snapshot.Peer;
        var card = Card("Mesh.Device." + peer.DeviceId.ToString("N"));
        var title = Row();
        title.Add(Heading(peer.DisplayName));
        var state = Muted($"{snapshot.Presence.Presence} · {snapshot.Presence.Connection}");
        Set(state, HavenProperties.Column, 1);
        title.Add(state);
        card.Add(title);
        card.Add(Muted($"{peer.Platform} · {peer.DeviceClass} · {peer.LastKnownEndpoint ?? "endpoint unavailable"}"));
        card.Add(Muted($"Identity {ShortFingerprint(peer.PublicKeyFingerprint)} · trusted {peer.TrustedAt?.LocalDateTime:g}"));

        if (snapshot.Presence.Capabilities.Count > 0)
            card.Add(Muted("Advertised: " + string.Join(", ", snapshot.Presence.Capabilities.Select(capability => capability.Name))));

        var grants = (peer.AllowedRemoteCapabilities ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var permissions = new Container { Layout = HavenLayout.Wrap };
        Set(permissions, HavenProperties.Gap, HavenLength.Px(7));
        permissions.Add(GrantButton(peer.DeviceId, MeshCoordinator.RemoteModelCapability, "Models", grants));
        permissions.Add(GrantButton(peer.DeviceId, MeshCoordinator.RemoteAgentCapability, "Agents", grants));
        permissions.Add(GrantButton(peer.DeviceId, MeshCoordinator.RemoteTaskCapability, "Tasks", grants));
        permissions.Add(GrantButton(peer.DeviceId, "computer-device-use", "Device actions", grants));
        permissions.Add(GrantButton(peer.DeviceId, MeshCoordinator.RemoteClipboardCapability, "Clipboard receive", grants));
        permissions.Add(GrantButton(peer.DeviceId, MeshCoordinator.RemoteFileCapability, "File receive", grants));
        card.Add(permissions);

        var actions = new Container { Layout = HavenLayout.Wrap };
        Set(actions, HavenProperties.Gap, HavenLength.Px(8));
        var sendClipboard = Button("Mesh.Device.SendClipboard." + peer.DeviceId.ToString("N"), "Send clipboard", ButtonVariant.Secondary);
        sendClipboard.Invoked += (_, _) => ClipboardSendRequested?.Invoke(peer.DeviceId);
        actions.Add(sendClipboard);
        var sendFile = Button("Mesh.Device.SendFile." + peer.DeviceId.ToString("N"), "Send file", ButtonVariant.Secondary);
        sendFile.Invoked += (_, _) => FileSendRequested?.Invoke(peer.DeviceId);
        actions.Add(sendFile);
        var connect = Button("Mesh.Device.Connect." + peer.DeviceId.ToString("N"), snapshot.Presence.Connection == MeshConnectionState.Connected ? "Reconnect" : "Connect", ButtonVariant.Secondary);
        connect.Invoked += async (_, _) => await RunAndRenderAsync(token => _viewModel.ConnectAsync(peer.DeviceId, token));
        actions.Add(connect);
        var revoke = Button("Mesh.Device.Revoke." + peer.DeviceId.ToString("N"), "Revoke trust", ButtonVariant.Danger);
        revoke.Invoked += async (_, _) => await RunAndRenderAsync(token => _viewModel.RevokeAsync(peer.DeviceId, token));
        actions.Add(revoke);
        card.Add(actions);
        return card;
    }

    private HavenButton GrantButton(Guid deviceId, string capability, string label, HashSet<string> grants)
    {
        var allowed = grants.Contains(capability);
        var button = Button($"Mesh.Grant.{deviceId:N}.{capability}", $"{label}: {(allowed ? "allowed" : "blocked")}", allowed ? ButtonVariant.Tertiary : ButtonVariant.Ghost);
        button.Invoked += async (_, _) => await RunAndRenderAsync(token => _viewModel.SetRemoteGrantAsync(deviceId, capability, !allowed, token));
        return button;
    }

    private static string ShortFingerprint(string fingerprint) => fingerprint.Length <= 16 ? fingerprint : fingerprint[..8] + "…" + fingerprint[^8..];
}
