using System.Collections.ObjectModel;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Desktop.ViewModels;

public enum ConversationExportFormat
{
    Markdown,
    Json,
    PlainText
}

public sealed record ConversationExportRequest(ConversationExportFormat Format, string Content, string SuggestedFileName);

public sealed class ConversationProductionToolbarViewModel : ObservableObject
{
    private readonly IConversationProductionRepository _production;
    private readonly IConversationExportService _exports;
    private readonly ILocalConversationShareService _sharing;
    private readonly IModelProviderRegistry _providers;
    private readonly ProviderRoutingModelClient _routing;
    private Guid _conversationId;
    private ConversationBranchItemViewModel? _selectedBranch;
    private string _searchQuery = string.Empty;
    private string _status = "Conversation tools ready.";
    private bool _isBusy;
    private bool _isExpanded;
    private string _shareAddress = string.Empty;
    private DateTimeOffset? _shareExpiresAt;
    private bool _suppressBranchSelection;

    public ConversationProductionToolbarViewModel(
        IConversationProductionRepository production,
        IConversationExportService exports,
        ILocalConversationShareService sharing,
        IModelProviderRegistry providers,
        ProviderRoutingModelClient routing)
    {
        _production = production;
        _exports = exports;
        _sharing = sharing;
        _providers = providers;
        _routing = routing;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        CreateBranchCommand = new AsyncRelayCommand(CreateBranchAsync, () => ConversationId != Guid.Empty && SelectedBranch is not null);
        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !string.IsNullOrWhiteSpace(SearchQuery));
        ClearSearchCommand = new RelayCommand(ClearSearch);
        ExportMarkdownCommand = new AsyncRelayCommand(() => ExportAsync(ConversationExportFormat.Markdown), CanUseConversation);
        ExportJsonCommand = new AsyncRelayCommand(() => ExportAsync(ConversationExportFormat.Json), CanUseConversation);
        ExportTextCommand = new AsyncRelayCommand(() => ExportAsync(ConversationExportFormat.PlainText), CanUseConversation);
        StartShareCommand = new AsyncRelayCommand(StartShareAsync, () => CanUseConversation() && !HasActiveShare);
        StopShareCommand = new AsyncRelayCommand(StopShareAsync, () => CanUseConversation() && HasActiveShare);
        SelectCloudModelCommand = new RelayCommand<ProviderModelChoiceViewModel>(SelectCloudModel);
        ToggleExpandedCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
    }

    public event EventHandler? BranchChanged;
    public event EventHandler<ModelDescriptor>? ModelSelected;
    public event EventHandler<ConversationExportRequest>? ExportRequested;

    public ObservableCollection<ConversationBranchItemViewModel> Branches { get; } = [];
    public ObservableCollection<ProviderModelChoiceViewModel> CloudModels { get; } = [];
    public ObservableCollection<ConversationSearchResultViewModel> SearchResults { get; } = [];
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand CreateBranchCommand { get; }
    public AsyncRelayCommand SearchCommand { get; }
    public RelayCommand ClearSearchCommand { get; }
    public AsyncRelayCommand ExportMarkdownCommand { get; }
    public AsyncRelayCommand ExportJsonCommand { get; }
    public AsyncRelayCommand ExportTextCommand { get; }
    public AsyncRelayCommand StartShareCommand { get; }
    public AsyncRelayCommand StopShareCommand { get; }
    public RelayCommand<ProviderModelChoiceViewModel> SelectCloudModelCommand { get; }
    public RelayCommand ToggleExpandedCommand { get; }

    public Guid ConversationId
    {
        get => _conversationId;
        private set
        {
            if (!SetProperty(ref _conversationId, value)) return;
            RaiseCommandStates();
        }
    }

    public ConversationBranchItemViewModel? SelectedBranch
    {
        get => _selectedBranch;
        set
        {
            if (!SetProperty(ref _selectedBranch, value) || _suppressBranchSelection || value is null) return;
            _ = SwitchBranchAsync(value);
            CreateBranchCommand.RaiseCanExecuteChanged();
        }
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (!SetProperty(ref _searchQuery, value)) return;
            SearchCommand.RaiseCanExecuteChanged();
        }
    }

    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public bool IsExpanded { get => _isExpanded; set { if (SetProperty(ref _isExpanded, value)) RaisePropertyChanged(nameof(ExpandLabel)); } }
    public string ExpandLabel => IsExpanded ? "Hide conversation tools" : "Conversation tools";
    public string ShareAddress { get => _shareAddress; private set { if (SetProperty(ref _shareAddress, value)) RaisePropertyChanged(nameof(HasActiveShare)); } }
    public DateTimeOffset? ShareExpiresAt { get => _shareExpiresAt; private set { if (SetProperty(ref _shareExpiresAt, value)) RaisePropertyChanged(nameof(ShareExpiryLabel)); } }
    public bool HasActiveShare => !string.IsNullOrWhiteSpace(ShareAddress);
    public string ShareExpiryLabel => ShareExpiresAt is null ? string.Empty : $"Expires {ShareExpiresAt.Value.LocalDateTime:g}";
    public bool HasSearchResults => SearchResults.Count > 0;
    public bool HasCloudModels => CloudModels.Count > 0;

    public async Task LoadAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        ConversationId = conversationId;
        ClearSearch();
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (ConversationId == Guid.Empty) return;
        try
        {
            IsBusy = true;
            var branches = await _production.GetBranchesAsync(ConversationId, cancellationToken).ConfigureAwait(false);
            var current = branches.FirstOrDefault(item => item.IsCurrent);
            _suppressBranchSelection = true;
            try
            {
                Branches.Clear();
                foreach (var branch in branches) Branches.Add(new ConversationBranchItemViewModel(branch));
                SelectedBranch = current is null ? null : Branches.FirstOrDefault(item => item.Id == current.Id);
            }
            finally { _suppressBranchSelection = false; }

            CloudModels.Clear();
            foreach (var model in (await _providers.GetModelsAsync(cancellationToken).ConfigureAwait(false)).Where(item => !item.IsLocal))
                CloudModels.Add(new ProviderModelChoiceViewModel(model, _routing.ToCompatibilityDescriptor(model)));
            RaisePropertyChanged(nameof(HasCloudModels));

            if (await _sharing.GetActiveAsync(ConversationId, cancellationToken).ConfigureAwait(false) is { } share)
            {
                ShareAddress = string.Empty;
                ShareExpiresAt = share.ExpiresAt;
                Status = "A LAN share record is active. Start a new share to generate a fresh private link for this app session.";
            }
            else
            {
                ShareAddress = string.Empty;
                ShareExpiresAt = null;
                Status = Branches.Count == 0 ? "This conversation will gain a branch when its first saved message is created." : $"{Branches.Count} branch{(Branches.Count == 1 ? string.Empty : "es")} available.";
            }
            RaiseCommandStates();
        }
        catch (Exception ex)
        {
            Status = "Conversation tools could not refresh: " + ex.Message;
        }
        finally { IsBusy = false; }
    }

    private async Task CreateBranchAsync()
    {
        if (SelectedBranch is null) return;
        try
        {
            IsBusy = true;
            var branch = await _production.CreateBranchAsync(
                ConversationId, SelectedBranch.Id, null, $"Branch {Branches.Count + 1}", ConversationBranchReason.Manual, CancellationToken.None).ConfigureAwait(false);
            await RefreshAsync(CancellationToken.None).ConfigureAwait(false);
            SelectedBranch = Branches.FirstOrDefault(item => item.Id == branch.Id);
            Status = $"Created {branch.Name}.";
            BranchChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { Status = "Could not create branch: " + ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task SwitchBranchAsync(ConversationBranchItemViewModel branch)
    {
        try
        {
            IsBusy = true;
            await _production.SetCurrentBranchAsync(ConversationId, branch.Id, CancellationToken.None).ConfigureAwait(false);
            foreach (var item in Branches) item.IsCurrent = item.Id == branch.Id;
            Status = $"Switched to {branch.Name}.";
            BranchChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { Status = "Could not switch branch: " + ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task SearchAsync()
    {
        try
        {
            IsBusy = true;
            SearchResults.Clear();
            foreach (var result in await _production.SearchAsync(SearchQuery, ConversationId, 50, CancellationToken.None).ConfigureAwait(false))
                SearchResults.Add(new ConversationSearchResultViewModel(result));
            RaisePropertyChanged(nameof(HasSearchResults));
            Status = SearchResults.Count == 0 ? "No matches in this conversation." : $"{SearchResults.Count} match{(SearchResults.Count == 1 ? string.Empty : "es")}.";
        }
        catch (Exception ex) { Status = "Search failed: " + ex.Message; }
        finally { IsBusy = false; }
    }

    private void ClearSearch()
    {
        SearchQuery = string.Empty;
        SearchResults.Clear();
        RaisePropertyChanged(nameof(HasSearchResults));
    }

    private async Task ExportAsync(ConversationExportFormat format)
    {
        try
        {
            IsBusy = true;
            var content = format switch
            {
                ConversationExportFormat.Markdown => await _exports.ExportMarkdownAsync(ConversationId, CancellationToken.None).ConfigureAwait(false),
                ConversationExportFormat.Json => await _exports.ExportJsonAsync(ConversationId, CancellationToken.None).ConfigureAwait(false),
                _ => await _exports.ExportPlainTextAsync(ConversationId, CancellationToken.None).ConfigureAwait(false)
            };
            var extension = format switch { ConversationExportFormat.Markdown => ".md", ConversationExportFormat.Json => ".json", _ => ".txt" };
            ExportRequested?.Invoke(this, new ConversationExportRequest(format, content, "haven-conversation-" + ConversationId.ToString("N")[..8] + extension));
            Status = $"{format} export prepared.";
        }
        catch (Exception ex) { Status = "Export failed: " + ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task StartShareAsync()
    {
        try
        {
            IsBusy = true;
            var handle = await _sharing.StartAsync(ConversationId, TimeSpan.FromHours(1), CancellationToken.None).ConfigureAwait(false);
            ShareAddress = handle.Address.ToString();
            ShareExpiresAt = handle.ExpiresAt;
            Status = handle.Notice;
            RaiseCommandStates();
        }
        catch (Exception ex) { Status = "LAN share could not start: " + ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task StopShareAsync()
    {
        try
        {
            IsBusy = true;
            await _sharing.StopAsync(ConversationId, CancellationToken.None).ConfigureAwait(false);
            ShareAddress = string.Empty;
            ShareExpiresAt = null;
            Status = "LAN share stopped.";
            RaiseCommandStates();
        }
        catch (Exception ex) { Status = "LAN share could not stop: " + ex.Message; }
        finally { IsBusy = false; }
    }

    private void SelectCloudModel(ProviderModelChoiceViewModel? item)
    {
        if (item is null) return;
        ModelSelected?.Invoke(this, item.CompatibilityDescriptor);
        Status = $"Selected {item.DisplayName} from {item.ProviderName}.";
    }

    private bool CanUseConversation() => ConversationId != Guid.Empty;

    private void RaiseCommandStates()
    {
        CreateBranchCommand.RaiseCanExecuteChanged();
        ExportMarkdownCommand.RaiseCanExecuteChanged();
        ExportJsonCommand.RaiseCanExecuteChanged();
        ExportTextCommand.RaiseCanExecuteChanged();
        StartShareCommand.RaiseCanExecuteChanged();
        StopShareCommand.RaiseCanExecuteChanged();
    }
}

public sealed class ConversationBranchItemViewModel(ConversationBranch branch) : ObservableObject
{
    private bool _isCurrent = branch.IsCurrent;
    public Guid Id => branch.Id;
    public string Name => branch.Name;
    public string Reason => branch.Reason.ToString();
    public string CreatedLabel => branch.CreatedAt.LocalDateTime.ToString("g");
    public bool IsCurrent { get => _isCurrent; set { if (SetProperty(ref _isCurrent, value)) RaisePropertyChanged(nameof(DisplayName)); } }
    public string DisplayName => IsCurrent ? Name + " · current" : Name;
}

public sealed record ProviderModelChoiceViewModel(ProviderModelDescriptor Descriptor, ModelDescriptor CompatibilityDescriptor)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Descriptor.DisplayName) ? Descriptor.Name : Descriptor.DisplayName;
    public string ProviderName => Descriptor.ProviderId;
    public string ContextLabel => Descriptor.ContextWindow is null ? "Context unknown" : $"{Descriptor.ContextWindow:N0} context";
    public string Capabilities => string.Join(", ", Descriptor.Capabilities.OrderBy(item => item));
}

public sealed record ConversationSearchResultViewModel(ConversationSearchResult Result)
{
    public Guid? MessageId => Result.MessageId;
    public string Snippet => Result.Snippet;
    public string TimeLabel => Result.Timestamp.LocalDateTime.ToString("g");
}
