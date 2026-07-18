/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/ViewModels/ConversationProductionToolbarViewModel.cs, in the Desktop presentation-model layer, exposing bindable state and commands to Avalonia views.
 * What: This file owns ConversationExportFormat, ConversationExportRequest, ConversationProductionToolbarViewModel, ConversationBranchItemViewModel, ProviderModelChoiceViewModel, ConversationSearchResultViewModel. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Keeping UI state here makes the XAML declarative and keeps behavior testable without recreating the full window.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.ObjectModel;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Lists the supported conversation export format values used to make state explicit and type-safe.
/// </summary>
public enum ConversationExportFormat
{
    Markdown,
    Json,
    PlainText
}

/// <summary>
/// Represents conversation export request and keeps its related state and behavior together.
/// </summary>
public sealed record ConversationExportRequest(ConversationExportFormat Format, string Content, string SuggestedFileName);

/// <summary>
/// Represents conversation production toolbar view model and keeps its related state and behavior together.
/// </summary>
public sealed class ConversationProductionToolbarViewModel : ObservableObject
{
    /// <summary>
    /// Stores production locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IConversationProductionRepository _production;
    /// <summary>
    /// Stores exports locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IConversationExportService _exports;
    /// <summary>
    /// Stores sharing locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ILocalConversationShareService _sharing;
    /// <summary>
    /// Stores providers locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IModelProviderRegistry _providers;
    /// <summary>
    /// Stores routing locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ProviderRoutingModelClient _routing;
    /// <summary>
    /// Stores conversation id locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private Guid _conversationId;
    /// <summary>
    /// Stores selected branch locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private ConversationBranchItemViewModel? _selectedBranch;
    /// <summary>
    /// Stores search query locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _searchQuery = string.Empty;
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _status = "Conversation details ready.";
    /// <summary>
    /// Stores is busy locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isBusy;
    /// <summary>
    /// Stores is expanded locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isExpanded;
    /// <summary>
    /// Stores has active share locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _hasActiveShare;
    /// <summary>
    /// Stores share address locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _shareAddress = string.Empty;
    /// <summary>
    /// Stores share expires at locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private DateTimeOffset? _shareExpiresAt;
    /// <summary>
    /// Stores suppress branch selection locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
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

    /// <summary>
    /// Stores branch changed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event EventHandler? BranchChanged;
    /// <summary>
    /// Stores model selected locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event Action<ModelDescriptor>? ModelSelected;
    /// <summary>
    /// Stores export requested locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event Action<ConversationExportRequest>? ExportRequested;

    /// <summary>
    /// Gets or updates branches, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<ConversationBranchItemViewModel> Branches { get; } = [];
    /// <summary>
    /// Gets or updates cloud models, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<ProviderModelChoiceViewModel> CloudModels { get; } = [];
    /// <summary>
    /// Gets or updates search results, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<ConversationSearchResultViewModel> SearchResults { get; } = [];
    /// <summary>
    /// Gets or updates refresh command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand RefreshCommand { get; }
    /// <summary>
    /// Creates branch command with the invariants required by its callers.
    /// </summary>
    public AsyncRelayCommand CreateBranchCommand { get; }
    /// <summary>
    /// Gets or updates search command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand SearchCommand { get; }
    /// <summary>
    /// Gets or updates clear search command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand ClearSearchCommand { get; }
    /// <summary>
    /// Gets or updates export markdown command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand ExportMarkdownCommand { get; }
    /// <summary>
    /// Gets or updates export json command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand ExportJsonCommand { get; }
    /// <summary>
    /// Gets or updates export text command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand ExportTextCommand { get; }
    /// <summary>
    /// Gets or updates start share command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand StartShareCommand { get; }
    /// <summary>
    /// Gets or updates stop share command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand StopShareCommand { get; }
    /// <summary>
    /// Gets or updates select cloud model command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<ProviderModelChoiceViewModel> SelectCloudModelCommand { get; }
    /// <summary>
    /// Gets or updates toggle expanded command, the bindable or domain state represented by this property.
    /// </summary>
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
            if (!SetProperty(ref _selectedBranch, value)) return;
            CreateBranchCommand.RaiseCanExecuteChanged();
            if (_suppressBranchSelection || value is null) return;
            _ = SwitchBranchAsync(value);
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

    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            RaiseCommandStates();
        }
    }
    /// <summary>
    /// Reports whether expanded applies to the current state.
    /// </summary>
    public bool IsExpanded { get => _isExpanded; set { if (SetProperty(ref _isExpanded, value)) RaisePropertyChanged(nameof(ExpandLabel)); } }
    /// <summary>
    /// Gets or updates expand label, the bindable or domain state represented by this property.
    /// </summary>
    public string ExpandLabel => IsExpanded ? "Hide conversation details" : "Conversation details";
    /// <summary>
    /// Gets or updates share address, the bindable or domain state represented by this property.
    /// </summary>
    public string ShareAddress { get => _shareAddress; private set => SetProperty(ref _shareAddress, value); }
    /// <summary>
    /// Gets or updates share expires at, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset? ShareExpiresAt { get => _shareExpiresAt; private set { if (SetProperty(ref _shareExpiresAt, value)) RaisePropertyChanged(nameof(ShareExpiryLabel)); } }
    /// <summary>
    /// Reports whether active share applies to the current state.
    /// </summary>
    public bool HasActiveShare => _hasActiveShare;
    /// <summary>
    /// Gets or updates share expiry label, the bindable or domain state represented by this property.
    /// </summary>
    public string ShareExpiryLabel => ShareExpiresAt is null ? string.Empty : $"Expires {ShareExpiresAt.Value.LocalDateTime:g}";
    /// <summary>
    /// Reports whether search results applies to the current state.
    /// </summary>
    public bool HasSearchResults => SearchResults.Count > 0;
    /// <summary>
    /// Reports whether cloud models applies to the current state.
    /// </summary>
    public bool HasCloudModels => CloudModels.Count > 0;

    /// <summary>
    /// Performs load asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task LoadAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        ConversationId = conversationId;
        ClearSearch();
        await RefreshAsync(cancellationToken);
    }

    /// <summary>
    /// Performs refresh asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    /// <summary>
    /// Performs refresh asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (ConversationId == Guid.Empty) return;
        try
        {
            IsBusy = true;
            var branches = await _production.GetBranchesAsync(ConversationId, cancellationToken);
            var current = branches.FirstOrDefault(item => item.IsCurrent);
            _suppressBranchSelection = true;
            try
            {
                Branches.Clear();
                foreach (var branch in branches) Branches.Add(new ConversationBranchItemViewModel(branch));
                SelectedBranch = current is null ? null : Branches.FirstOrDefault(item => item.Id == current.Id);
            }
            finally
            {
                _suppressBranchSelection = false;
            }

            CloudModels.Clear();
            foreach (var model in (await _providers.GetModelsAsync(cancellationToken)).Where(item => !item.IsLocal))
                CloudModels.Add(new ProviderModelChoiceViewModel(model, _routing.ToCompatibilityDescriptor(model)));
            RaisePropertyChanged(nameof(HasCloudModels));

            var share = await _sharing.GetActiveAsync(ConversationId, cancellationToken);
            SetShareState(share is not null, string.Empty, share?.ExpiresAt);
            Status = share is not null
                ? "A LAN share is active from this app profile. Stop it here, or start a fresh share after stopping it."
                : Branches.Count == 0
                    ? "This conversation will gain a branch when its first saved message is created."
                    : $"{Branches.Count} branch{(Branches.Count == 1 ? string.Empty : "es")} available.";
        }
        catch (Exception ex)
        {
            Status = "Conversation tools could not refresh: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Creates branch async with the invariants required by its callers.
    /// </summary>
    private async Task CreateBranchAsync()
    {
        if (SelectedBranch is null) return;
        try
        {
            IsBusy = true;
            var branch = await _production.CreateBranchAsync(
                ConversationId,
                SelectedBranch.Id,
                null,
                $"Branch {Branches.Count + 1}",
                ConversationBranchReason.Manual,
                CancellationToken.None);
            await RefreshAsync(CancellationToken.None);
            _suppressBranchSelection = true;
            SelectedBranch = Branches.FirstOrDefault(item => item.Id == branch.Id);
            _suppressBranchSelection = false;
            Status = $"Created {branch.Name}.";
            BranchChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Status = "Could not create branch: " + ex.Message;
        }
        finally
        {
            _suppressBranchSelection = false;
            IsBusy = false;
        }
    }

    /// <summary>
    /// Performs switch branch asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task SwitchBranchAsync(ConversationBranchItemViewModel branch)
    {
        try
        {
            IsBusy = true;
            await _production.SetCurrentBranchAsync(ConversationId, branch.Id, CancellationToken.None);
            foreach (var item in Branches) item.IsCurrent = item.Id == branch.Id;
            Status = $"Switched to {branch.Name}.";
            BranchChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Status = "Could not switch branch: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Performs search asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task SearchAsync()
    {
        try
        {
            IsBusy = true;
            SearchResults.Clear();
            foreach (var result in await _production.SearchAsync(SearchQuery, ConversationId, 50, CancellationToken.None))
                SearchResults.Add(new ConversationSearchResultViewModel(result));
            RaisePropertyChanged(nameof(HasSearchResults));
            Status = SearchResults.Count == 0
                ? "No matches in this conversation."
                : $"{SearchResults.Count} match{(SearchResults.Count == 1 ? string.Empty : "es")}.";
        }
        catch (Exception ex)
        {
            Status = "Search failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Performs the clear search step owned by this component.
    /// </summary>
    private void ClearSearch()
    {
        SearchQuery = string.Empty;
        SearchResults.Clear();
        RaisePropertyChanged(nameof(HasSearchResults));
    }

    /// <summary>
    /// Performs export asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ExportAsync(ConversationExportFormat format)
    {
        try
        {
            IsBusy = true;
            var content = format switch
            {
                ConversationExportFormat.Markdown => await _exports.ExportMarkdownAsync(ConversationId, CancellationToken.None),
                ConversationExportFormat.Json => await _exports.ExportJsonAsync(ConversationId, CancellationToken.None),
                _ => await _exports.ExportPlainTextAsync(ConversationId, CancellationToken.None)
            };
            var extension = format switch
            {
                ConversationExportFormat.Markdown => ".md",
                ConversationExportFormat.Json => ".json",
                _ => ".txt"
            };
            ExportRequested?.Invoke(new ConversationExportRequest(
                format,
                content,
                "haven-conversation-" + ConversationId.ToString("N")[..8] + extension));
            Status = $"{format} export prepared.";
        }
        catch (Exception ex)
        {
            Status = "Export failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Performs start share asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task StartShareAsync()
    {
        try
        {
            IsBusy = true;
            var handle = await _sharing.StartAsync(ConversationId, TimeSpan.FromHours(1), CancellationToken.None);
            SetShareState(true, handle.Address.ToString(), handle.ExpiresAt);
            Status = handle.Notice;
        }
        catch (Exception ex)
        {
            Status = "LAN share could not start: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Performs stop share asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task StopShareAsync()
    {
        try
        {
            IsBusy = true;
            await _sharing.StopAsync(ConversationId, CancellationToken.None);
            SetShareState(false, string.Empty, null);
            Status = "LAN share stopped.";
        }
        catch (Exception ex)
        {
            Status = "LAN share could not stop: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Performs the set share state step owned by this component.
    /// </summary>
    private void SetShareState(bool isActive, string address, DateTimeOffset? expiresAt)
    {
        _hasActiveShare = isActive;
        ShareAddress = address;
        ShareExpiresAt = expiresAt;
        RaisePropertyChanged(nameof(HasActiveShare));
        RaiseCommandStates();
    }

    /// <summary>
    /// Performs the select cloud model step owned by this component.
    /// </summary>
    private void SelectCloudModel(ProviderModelChoiceViewModel? item)
    {
        if (item is null) return;
        ModelSelected?.Invoke(item.CompatibilityDescriptor);
        Status = $"Selected {item.DisplayName} from {item.ProviderName}.";
    }

    /// <summary>
    /// Reports whether use conversation applies to the current state.
    /// </summary>
    private bool CanUseConversation() => ConversationId != Guid.Empty && !IsBusy;

    /// <summary>
    /// Performs the raise command states step owned by this component.
    /// </summary>
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

/// <summary>
/// Represents conversation branch item view model and keeps its related state and behavior together.
/// </summary>
public sealed class ConversationBranchItemViewModel(ConversationBranch branch) : ObservableObject
{
    /// <summary>
    /// Stores is current locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isCurrent = branch.IsCurrent;
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id => branch.Id;
    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public string Name => branch.Name;
    /// <summary>
    /// Gets or updates reason, the bindable or domain state represented by this property.
    /// </summary>
    public string Reason => branch.Reason.ToString();
    /// <summary>
    /// Creates d label with the invariants required by its callers.
    /// </summary>
    public string CreatedLabel => branch.CreatedAt.LocalDateTime.ToString("g");
    /// <summary>
    /// Reports whether current applies to the current state.
    /// </summary>
    public bool IsCurrent { get => _isCurrent; set { if (SetProperty(ref _isCurrent, value)) RaisePropertyChanged(nameof(DisplayName)); } }
    /// <summary>
    /// Gets or updates display name, the bindable or domain state represented by this property.
    /// </summary>
    public string DisplayName => IsCurrent ? Name + " · current" : Name;
}

/// <summary>
/// Represents provider model choice view model and keeps its related state and behavior together.
/// </summary>
public sealed record ProviderModelChoiceViewModel(ProviderModelDescriptor Descriptor, ModelDescriptor CompatibilityDescriptor)
{
    /// <summary>
    /// Gets or updates display name, the bindable or domain state represented by this property.
    /// </summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Descriptor.DisplayName) ? Descriptor.Name : Descriptor.DisplayName;
    /// <summary>
    /// Gets or updates provider name, the bindable or domain state represented by this property.
    /// </summary>
    public string ProviderName => Descriptor.ProviderId;
    /// <summary>
    /// Gets or updates context label, the bindable or domain state represented by this property.
    /// </summary>
    public string ContextLabel => Descriptor.ContextWindow is null ? "Context unknown" : $"{Descriptor.ContextWindow:N0} context";
    /// <summary>
    /// Gets or updates capabilities, the bindable or domain state represented by this property.
    /// </summary>
    public string Capabilities => string.Join(", ", Descriptor.Capabilities.OrderBy(item => item));
}

/// <summary>
/// Represents conversation search result view model and keeps its related state and behavior together.
/// </summary>
public sealed record ConversationSearchResultViewModel(ConversationSearchResult Result)
{
    /// <summary>
    /// Gets or updates message id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid? MessageId => Result.MessageId;
    /// <summary>
    /// Gets or updates snippet, the bindable or domain state represented by this property.
    /// </summary>
    public string Snippet => Result.Snippet;
    /// <summary>
    /// Gets or updates time label, the bindable or domain state represented by this property.
    /// </summary>
    public string TimeLabel => Result.Timestamp.LocalDateTime.ToString("g");
}
