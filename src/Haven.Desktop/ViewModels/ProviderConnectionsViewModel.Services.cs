using System.Collections.ObjectModel;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

public sealed partial class ProviderConnectionsViewModel
{
    private ICalendarSyncProviderRegistry _calendarProviders = null!;
    private IPlannerRepository _planner = null!;
    private string _servicesStatus = "Loading connected services.";
    public ObservableCollection<ServiceConnectionItemViewModel> Services { get; } = [];
    public AsyncRelayCommand<ServiceConnectionItemViewModel> ConnectServiceCommand { get; private set; } = null!;
    public AsyncRelayCommand<ServiceConnectionItemViewModel> DisconnectServiceCommand { get; private set; } = null!;
    public string ServicesStatus { get => _servicesStatus; private set => SetProperty(ref _servicesStatus, value); }

    private void InitializeServiceConnections(ICalendarSyncProviderRegistry calendarProviders, IPlannerRepository planner)
    {
        _calendarProviders = calendarProviders ?? throw new ArgumentNullException(nameof(calendarProviders));
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        ConnectServiceCommand = new AsyncRelayCommand<ServiceConnectionItemViewModel>(ConnectServiceAsync);
        DisconnectServiceCommand = new AsyncRelayCommand<ServiceConnectionItemViewModel>(DisconnectServiceAsync);
    }

    private async Task RefreshServicesAsync()
    {
        try
        {
            var accounts = await _planner.GetCalendarAccountsAsync(CancellationToken.None);
            var existing = Services.ToDictionary(item => item.Kind);
            var refreshed = new List<ServiceConnectionItemViewModel>();
            foreach (var provider in _calendarProviders.Providers.OrderBy(provider => provider.Kind))
            {
                var account = accounts.Where(account => account.Provider == provider.Kind)
                    .OrderByDescending(account => account.UpdatedAt).FirstOrDefault();
                if (!existing.TryGetValue(provider.Kind, out var item))
                    item = new ServiceConnectionItemViewModel(provider.Kind);
                item.Apply(provider, account);
                refreshed.Add(item);
            }

            Services.Clear();
            foreach (var item in refreshed) Services.Add(item);
            ServicesStatus = Services.Count == 1
                ? "1 connected-service provider available."
                : $"{Services.Count} connected-service providers available.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ServicesStatus = $"Connected services could not be loaded: {ex.Message}";
        }
    }

    private async Task ConnectServiceAsync(ServiceConnectionItemViewModel? item)
    {
        if (item is null) return;
        var provider = _calendarProviders.Get(item.Kind);
        if (!provider.IsConfigured)
        {
            item.Status = provider.ConfigurationStatus;
            return;
        }

        try
        {
            item.IsBusy = true;
            item.Status = "Continue sign-in, MFA/CAPTCHA and consent with the provider in your browser.";
            var result = await provider.ConnectAsync(CancellationToken.None);
            if (result.Succeeded)
                await RefreshServicesAsync();
            else
                item.Status = result.Message;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            item.Status = $"Connection failed: {ex.Message}";
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    private async Task DisconnectServiceAsync(ServiceConnectionItemViewModel? item)
    {
        if (item?.AccountId is not Guid accountId) return;
        try
        {
            item.IsBusy = true;
            await _calendarProviders.Get(item.Kind).DisconnectAsync(accountId, CancellationToken.None);
            await RefreshServicesAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            item.Status = $"Disconnect failed: {ex.Message}";
        }
        finally
        {
            item.IsBusy = false;
        }
    }
}

public sealed class ServiceConnectionItemViewModel : ObservableObject
{
    private bool _isConfigured;
    private bool _isConnected;
    private bool _isBusy;
    private Guid? _accountId;
    private string _accountLabel = "No account connected";
    private string _connectionLabel = "Disconnected";
    private string _lastSyncLabel = "Never synced";
    private string _status = string.Empty;
    private bool _requiresReconnect;

    public ServiceConnectionItemViewModel(CalendarProviderKind kind)
    {
        Kind = kind;
        DisplayName = kind == CalendarProviderKind.Google ? "Google Calendar" : "Microsoft Calendar / Outlook";
        SetupMethodLabel = "Browser OAuth";
        CapabilityLabel = "Calendar events - read and write";
        PermissionLabel = kind == CalendarProviderKind.Google
            ? "Haven requests: account email + Google Calendar read/write"
            : "Haven requests: basic profile + calendar read/write + offline access";
    }

    public CalendarProviderKind Kind { get; }
    public string DisplayName { get; }
    public string SetupMethodLabel { get; }
    public string CapabilityLabel { get; }
    public string PermissionLabel { get; }
    public bool IsConfigured { get => _isConfigured; private set { if (SetProperty(ref _isConfigured, value)) OnAvailabilityChanged(); } }
    public bool IsConnected { get => _isConnected; private set { if (SetProperty(ref _isConnected, value)) OnAvailabilityChanged(); } }
    public Guid? AccountId { get => _accountId; private set { if (SetProperty(ref _accountId, value)) OnAvailabilityChanged(); } }
    public string AccountLabel { get => _accountLabel; private set => SetProperty(ref _accountLabel, value); }
    public string ConnectionLabel { get => _connectionLabel; private set => SetProperty(ref _connectionLabel, value); }
    public string LastSyncLabel { get => _lastSyncLabel; private set => SetProperty(ref _lastSyncLabel, value); }
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public bool IsBusy { get => _isBusy; set { if (SetProperty(ref _isBusy, value)) OnAvailabilityChanged(); } }
    public bool RequiresReconnect { get => _requiresReconnect; private set { if (SetProperty(ref _requiresReconnect, value)) OnAvailabilityChanged(); } }
    public string ConnectActionLabel => RequiresReconnect ? "Reconnect" : "Connect";
    public bool CanConnect => !IsBusy && IsConfigured && (!IsConnected || RequiresReconnect);
    public bool CanDisconnect => !IsBusy && IsConnected && AccountId.HasValue;

    public void Apply(ICalendarSyncProvider provider, CalendarAccount? account)
    {
        IsConfigured = provider.IsConfigured;
        AccountId = account?.Id;
        IsConnected = account is not null && account.Status is not (CalendarSyncStatus.NotConfigured or CalendarSyncStatus.Disconnected);
        RequiresReconnect = account is not null && NeedsAuthorizationRefresh(account.Status, account.StatusMessage);
        ConnectionLabel = !IsConfigured ? "Not configured" : account is null ? "Disconnected" : account.Status switch
        {
            CalendarSyncStatus.Ready => "Connected",
            CalendarSyncStatus.Syncing => "Syncing",
            CalendarSyncStatus.Offline => "Offline",
            CalendarSyncStatus.Error when RequiresReconnect => "Reconnect required",
            CalendarSyncStatus.Error => "Needs attention",
            _ => "Disconnected"
        };

        AccountLabel = account is null
            ? "No account connected"
            : string.Equals(account.DisplayName, account.AccountIdentifier, StringComparison.OrdinalIgnoreCase)
                ? account.DisplayName
                : $"{account.DisplayName} - {account.AccountIdentifier}";
        LastSyncLabel = account?.LastSyncedAt is { } synced
            ? $"Last synced {synced.LocalDateTime:g}"
            : "Never synced";
        Status = account?.StatusMessage ?? provider.ConfigurationStatus;
    }

    private static bool NeedsAuthorizationRefresh(CalendarSyncStatus status, string? message)
    {
        if (status != CalendarSyncStatus.Error || string.IsNullOrWhiteSpace(message))
            return false;

        return message.Contains("connect again", StringComparison.OrdinalIgnoreCase)
               || message.Contains("connected again", StringComparison.OrdinalIgnoreCase)
               || message.Contains("session expired", StringComparison.OrdinalIgnoreCase)
               || message.Contains("refresh token", StringComparison.OrdinalIgnoreCase)
               || message.Contains("token refresh", StringComparison.OrdinalIgnoreCase)
               || message.Contains("authorization expired", StringComparison.OrdinalIgnoreCase);
    }

    private void OnAvailabilityChanged()
    {
        RaisePropertyChanged(nameof(ConnectActionLabel));
        RaisePropertyChanged(nameof(CanConnect));
        RaisePropertyChanged(nameof(CanDisconnect));
    }
}
