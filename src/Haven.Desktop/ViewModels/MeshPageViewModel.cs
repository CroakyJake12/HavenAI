using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

/// <summary>Presentation model for trusted Mesh devices and the multi-device Work Mode team room.</summary>
public sealed class MeshPageViewModel : ObservableObject, IDisposable
{
    private readonly MeshCoordinator _mesh;
    private string _status = "Mesh is ready.";
    private string _pairingOffer = string.Empty;
    private string _incomingPairingOffer = string.Empty;
    private string _workerName = string.Empty;
    private string _workerRole = string.Empty;
    private string _workerSpecialties = string.Empty;
    private int _selectedRuntimeIndex = -1;
    private bool _newWorkerIsCoordinator;
    private Guid? _directWorkerId;
    private string _directMessage = string.Empty;
    private string _teamMessage = string.Empty;
    private string _coordinatorCommand = string.Empty;
    private string _output = string.Empty;
    private bool _disposed;

    public MeshPageViewModel(MeshCoordinator mesh)
    {
        _mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
        _mesh.StateChanged += OnMeshStateChanged;
    }

    public ObservableCollection<MeshPeerSnapshot> Devices { get; } = [];
    public ObservableCollection<MeshDiscoveryCandidate> NearbyDevices { get; } = [];
    public ObservableCollection<MeshWorkMemberStatus> Workers { get; } = [];
    public ObservableCollection<MeshWorkMessage> TeamMessages { get; } = [];
    public ObservableCollection<MeshWorkItem> WorkItems { get; } = [];
    public ObservableCollection<MeshRuntimeChoiceViewModel> RuntimeChoices { get; } = [];
    public ObservableCollection<MeshIncomingClipboard> ReceivedClipboards { get; } = [];
    public ObservableCollection<MeshReceivedFile> ReceivedFiles { get; } = [];

    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string PairingOffer { get => _pairingOffer; private set => SetProperty(ref _pairingOffer, value); }
    public string IncomingPairingOffer { get => _incomingPairingOffer; set => SetProperty(ref _incomingPairingOffer, value); }
    public string WorkerName { get => _workerName; set => SetProperty(ref _workerName, value); }
    public string WorkerRole { get => _workerRole; set => SetProperty(ref _workerRole, value); }
    public string WorkerSpecialties { get => _workerSpecialties; set => SetProperty(ref _workerSpecialties, value); }
    public int SelectedRuntimeIndex { get => _selectedRuntimeIndex; set => SetProperty(ref _selectedRuntimeIndex, value); }
    public bool NewWorkerIsCoordinator { get => _newWorkerIsCoordinator; set => SetProperty(ref _newWorkerIsCoordinator, value); }
    public Guid? DirectWorkerId { get => _directWorkerId; private set { if (SetProperty(ref _directWorkerId, value)) RaisePropertyChanged(nameof(DirectWorkerName)); } }
    public string DirectWorkerName => DirectWorkerId is { } id ? Workers.FirstOrDefault(worker => worker.Member.WorkerId == id)?.Member.Name ?? "worker" : "Select a worker";
    public string DirectMessage { get => _directMessage; set => SetProperty(ref _directMessage, value); }
    public string TeamMessage { get => _teamMessage; set => SetProperty(ref _teamMessage, value); }
    public string CoordinatorCommand { get => _coordinatorCommand; set => SetProperty(ref _coordinatorCommand, value); }
    public string Output { get => _output; private set => SetProperty(ref _output, value); }

    public async Task InitialiseAsync(CancellationToken cancellationToken)
    {
        await _mesh.InitialiseAsync(cancellationToken);
        await RefreshAsync(cancellationToken);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var dashboard = await _mesh.GetDashboardAsync(cancellationToken);
        var work = await _mesh.GetWorkModeAsync(cancellationToken);
        var transfers = await _mesh.GetTransferSnapshotAsync(cancellationToken);
        Replace(Devices, dashboard.TrustedPeers);
        Replace(NearbyDevices, dashboard.NearbyDevices ?? []);
        Replace(Workers, work.Members);
        Replace(TeamMessages, work.RecentMessages);
        Replace(WorkItems, work.RecentWork);
        Replace(ReceivedClipboards, transfers.RecentClipboards);
        Replace(ReceivedFiles, transfers.RecentFiles);
        if (DirectWorkerId is { } direct && !Workers.Any(worker => worker.Member.WorkerId == direct)) DirectWorkerId = null;
        Status = $"{Devices.Count} trusted device{(Devices.Count == 1 ? string.Empty : "s")} · {Workers.Count} Work Mode member{(Workers.Count == 1 ? string.Empty : "s")} · {ReceivedFiles.Count} recent file{(ReceivedFiles.Count == 1 ? string.Empty : "s")}";
    }

    public async Task RefreshRuntimeChoicesAsync(CancellationToken cancellationToken)
    {
        Status = "Checking remote models and agents…";
        var choices = new List<MeshRuntimeChoiceViewModel>();
        foreach (var model in await _mesh.GetRemoteModelsAsync(cancellationToken))
        {
            MeshModelRoute route;
            try { route = MeshRemoteModelProvider.DecodeRoute(model.Name); } catch (ArgumentException) { continue; }
            var deviceName = Devices.FirstOrDefault(device => device.Peer.DeviceId == route.DeviceId)?.Peer.DisplayName ?? route.DeviceId.ToString("N")[..8];
            choices.Add(new(MeshWorkRuntimeKind.Model, route.DeviceId, deviceName, route.ProviderId, route.ModelName, null, model.DisplayName ?? model.Name));
        }
        foreach (var agent in await _mesh.GetRemoteAgentsAsync(cancellationToken))
            choices.Add(new(MeshWorkRuntimeKind.Agent, agent.DeviceId, agent.DeviceName, null, null, agent.AgentId, agent.Name));
        Replace(RuntimeChoices, choices.OrderBy(choice => choice.DeviceName).ThenBy(choice => choice.DisplayName));
        SelectedRuntimeIndex = RuntimeChoices.Count > 0 ? Math.Clamp(SelectedRuntimeIndex, 0, RuntimeChoices.Count - 1) : -1;
        Status = RuntimeChoices.Count == 0 ? "No permitted remote models or agents are currently available." : $"Found {RuntimeChoices.Count} remote runtime choice{(RuntimeChoices.Count == 1 ? string.Empty : "s")}.";
    }

    public async Task CreatePairingOfferAsync(CancellationToken cancellationToken)
    {
        var offer = await _mesh.CreatePairingOfferAsync(cancellationToken);
        PairingOffer = JsonSerializer.Serialize(offer, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        Status = $"Pairing code {offer.VerificationCode} is valid until {offer.ExpiresAt.LocalDateTime:t}.";
    }

    public async Task AcceptPairingOfferAsync(CancellationToken cancellationToken)
    {
        MeshPairingOffer? offer;
        try { offer = JsonSerializer.Deserialize<MeshPairingOffer>(IncomingPairingOffer, new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
        catch (JsonException ex) { Status = "That pairing offer is not valid JSON: " + ex.Message; return; }
        if (offer is null) { Status = "Paste a complete Mesh pairing offer first."; return; }
        var result = await _mesh.PairAsync(offer, cancellationToken);
        Status = result.Message;
        if (result.Succeeded) { IncomingPairingOffer = string.Empty; await RefreshAsync(cancellationToken); }
    }

    public async Task ConnectAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        try { await _mesh.ConnectAsync(deviceId, cancellationToken); Status = "Reconnect request completed."; }
        catch (Exception ex) { Status = "Could not connect: " + ex.Message; }
        await RefreshAsync(cancellationToken);
    }

    public async Task RevokeAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        await _mesh.RevokeAsync(deviceId, cancellationToken);
        await RefreshAsync(cancellationToken);
        Status = "Device trust revoked. It cannot reconnect without being paired again explicitly.";
    }

    public async Task SendClipboardAsync(Guid deviceId, string text, CancellationToken cancellationToken)
    {
        var receipt = await _mesh.SendClipboardTextAsync(deviceId, text, cancellationToken);
        await RefreshAsync(cancellationToken);
        Status = receipt.Message;
    }

    public async Task SendFileAsync(Guid deviceId, string fileName, Stream content, CancellationToken cancellationToken)
    {
        var receipt = await _mesh.SendFileAsync(deviceId, fileName, content, cancellationToken);
        await RefreshAsync(cancellationToken);
        Status = receipt.Message;
    }

    public void SetStatus(string status) => Status = status;

    public async Task SetRemoteGrantAsync(Guid deviceId, string capability, bool allowed, CancellationToken cancellationToken)
    {
        await _mesh.SetPeerCapabilityPermissionAsync(deviceId, capability, allowed, cancellationToken);
        await RefreshAsync(cancellationToken);
        Status = $"Remote {capability} is now {(allowed ? "allowed" : "blocked")} for this device.";
    }

    public async Task CreateWorkerAsync(CancellationToken cancellationToken)
    {
        if (SelectedRuntimeIndex < 0 || SelectedRuntimeIndex >= RuntimeChoices.Count) { Status = "Choose a permitted remote model or agent first."; return; }
        if (string.IsNullOrWhiteSpace(WorkerName)) { Status = "Give this team member a friendly name first."; return; }
        var choice = RuntimeChoices[SelectedRuntimeIndex];
        var specialties = WorkerSpecialties.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (choice.Kind == MeshWorkRuntimeKind.Model)
            await _mesh.ConfigureModelWorkerAsync(WorkerName, choice.DeviceId, choice.ProviderId!, choice.ModelName!, choice.DisplayName, WorkerRole, specialties, NewWorkerIsCoordinator, cancellationToken);
        else
            await _mesh.ConfigureAgentWorkerAsync(WorkerName, choice.DeviceId, choice.AgentId!.Value, choice.DisplayName, WorkerRole, specialties, NewWorkerIsCoordinator, cancellationToken);
        Status = $"{WorkerName.Trim()} joined the Mesh Work Mode team.";
        WorkerName = string.Empty; WorkerRole = string.Empty; WorkerSpecialties = string.Empty; NewWorkerIsCoordinator = false;
        await RefreshAsync(cancellationToken);
    }

    public async Task RemoveWorkerAsync(Guid workerId, CancellationToken cancellationToken)
    {
        await _mesh.RemoveWorkMemberAsync(workerId, cancellationToken);
        await RefreshAsync(cancellationToken);
    }

    public async Task MakeCoordinatorAsync(Guid workerId, CancellationToken cancellationToken)
    {
        await _mesh.SetWorkCoordinatorAsync(workerId, cancellationToken);
        Status = "Coordinator updated.";
        await RefreshAsync(cancellationToken);
    }

    public void SelectDirectWorker(Guid workerId) { DirectWorkerId = workerId; Status = $"Direct chat with {DirectWorkerName}."; }

    public async Task SendDirectAsync(CancellationToken cancellationToken)
    {
        if (DirectWorkerId is not { } workerId || string.IsNullOrWhiteSpace(DirectMessage)) { Status = "Choose a worker and write a message first."; return; }
        var reply = await _mesh.SendWorkMessageAsync(workerId, DirectMessage, cancellationToken);
        Output = reply.Content; DirectMessage = string.Empty; Status = reply.Status == MeshWorkMessageStatus.Succeeded ? $"{DirectWorkerName} replied." : reply.Content;
        await RefreshAsync(cancellationToken);
    }

    public async Task SendTeamMessageAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(TeamMessage)) { Status = "Write a Team Room message first."; return; }
        var replies = await _mesh.PostSharedPoolAsync(TeamMessage, null, cancellationToken);
        Output = string.Join(Environment.NewLine + Environment.NewLine, replies.Select(reply => reply.Content));
        TeamMessage = string.Empty; Status = $"Team Room received {replies.Count} repl{(replies.Count == 1 ? "y" : "ies")}.";
        await RefreshAsync(cancellationToken);
    }

    public async Task RunCoordinatorCommandAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(CoordinatorCommand)) { Status = "Give the coordinator a command first."; return; }
        var result = await _mesh.ExecuteWorkCommandAsync(CoordinatorCommand, cancellationToken);
        Output = result.Message; CoordinatorCommand = string.Empty; Status = "Work Mode command completed.";
        await RefreshAsync(cancellationToken);
    }

private void OnMeshStateChanged()
    {
        if (_disposed) return;
        Dispatcher.UIThread.Post(async () => await RefreshSafelyAsync());
    }

    private async Task RefreshSafelyAsync()
    {
        try { await RefreshAsync(CancellationToken.None); } catch { }
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source) target.Add(item);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _mesh.StateChanged -= OnMeshStateChanged;
    }
}

public sealed record MeshRuntimeChoiceViewModel(
    MeshWorkRuntimeKind Kind, Guid DeviceId, string DeviceName, string? ProviderId, string? ModelName, Guid? AgentId, string DisplayName)
{
    public string Label => $"{DeviceName} · {DisplayName} · {Kind}";
}
