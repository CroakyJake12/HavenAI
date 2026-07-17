using System.Collections.ObjectModel;
using Haven.Application;
using Haven.Browser;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

public sealed class BrowserSafetyViewModel : ObservableObject, IDisposable
{
    private readonly IBrowserAutomationService _automation;
    private readonly BrowserSessionService _browser;
    private readonly BrowserSitePermissionStore _permissions;
    private readonly CancellationTokenSource _lifetime = new();
    private string _status = "Loading browser safety activity…";
    private string _permissionStatus = "Navigate to an HTTP or HTTPS page to manage its permissions.";
    private string _currentOrigin = "No active web origin";
    private BrowserSitePermissionKind _selectedPermissionKind = BrowserSitePermissionKind.Notifications;
    private BrowserSitePermissionDecision _selectedPermissionDecision = BrowserSitePermissionDecision.Ask;
    private bool _isBusy;
    private bool _disposed;

    public BrowserSafetyViewModel(IBrowserAutomationService automation, BrowserSessionService browser, IAppPaths paths)
    {
        _automation = automation;
        _browser = browser;
        _permissions = BrowserSitePermissionStoreProvider.Get(paths);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ApproveCommand = new AsyncRelayCommand<BrowserPendingActionViewModel>(ApproveAsync);
        RejectCommand = new AsyncRelayCommand<BrowserPendingActionViewModel>(RejectAsync);
        SavePermissionCommand = new AsyncRelayCommand(SavePermissionAsync, () => CanManageOrigin);
        RevokeOriginCommand = new AsyncRelayCommand(RevokeOriginAsync, () => CanManageOrigin);
        _browser.StateChanged += OnBrowserStateChanged;
        UpdateCurrentOrigin(_browser.State.Address);
        _ = RefreshAsync();
    }

    public ObservableCollection<BrowserPendingActionViewModel> Pending { get; } = [];
    public ObservableCollection<BrowserAuditEntryViewModel> Audit { get; } = [];
    public ObservableCollection<BrowserDownloadViewModel> Downloads { get; } = [];
    public ObservableCollection<BrowserSitePermissionViewModel> Permissions { get; } = [];
    public IReadOnlyList<BrowserSitePermissionKind> PermissionKinds { get; } = Enum.GetValues<BrowserSitePermissionKind>();
    public IReadOnlyList<BrowserSitePermissionDecision> PermissionDecisions { get; } = Enum.GetValues<BrowserSitePermissionDecision>();
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand<BrowserPendingActionViewModel> ApproveCommand { get; }
    public AsyncRelayCommand<BrowserPendingActionViewModel> RejectCommand { get; }
    public AsyncRelayCommand SavePermissionCommand { get; }
    public AsyncRelayCommand RevokeOriginCommand { get; }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string PermissionStatus { get => _permissionStatus; private set => SetProperty(ref _permissionStatus, value); }
    public string CurrentOrigin { get => _currentOrigin; private set => SetProperty(ref _currentOrigin, value); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public bool HasPending => Pending.Count > 0;
    public bool HasNoPending => !HasPending;
    public bool HasPermissions => Permissions.Count > 0;
    public bool HasNoPermissions => !HasPermissions;
    public bool CanManageOrigin => TryGetCurrentOrigin(out _);

    public BrowserSitePermissionKind SelectedPermissionKind
    {
        get => _selectedPermissionKind;
        set
        {
            if (!SetProperty(ref _selectedPermissionKind, value)) return;
            LoadSelectedDecision();
        }
    }

    public BrowserSitePermissionDecision SelectedPermissionDecision
    {
        get => _selectedPermissionDecision;
        set => SetProperty(ref _selectedPermissionDecision, value);
    }

    private async Task RefreshAsync()
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        try
        {
            IsBusy = true;
            var pendingTask = _automation.GetPendingAsync(operation.Token);
            var auditTask = _automation.GetAuditAsync(100, operation.Token);
            var downloadsTask = _automation.GetDownloadsAsync(50, operation.Token);
            await Task.WhenAll(pendingTask, auditTask, downloadsTask);
            Replace(Pending, await pendingTask);
            Replace(Audit, (await auditTask).Select(item => new BrowserAuditEntryViewModel(item)));
            Replace(Downloads, (await downloadsTask).Select(item => new BrowserDownloadViewModel(item)));
            RefreshPermissions();
            RaisePropertyChanged(nameof(HasPending));
            RaisePropertyChanged(nameof(HasNoPending));
            Status = Pending.Count == 0
                ? "No browser actions are waiting for approval."
                : $"{Pending.Count} browser action{(Pending.Count == 1 ? string.Empty : "s")} waiting for approval.";
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Status = "Browser safety data is unavailable: " + exception.Message;
        }
        finally { IsBusy = false; }
    }

    private async Task ApproveAsync(BrowserPendingActionViewModel? item)
    {
        if (item is null) return;
        try
        {
            IsBusy = true;
            var result = await _automation.ApproveAsync(item.Action.Id, _lifetime.Token);
            Status = result.Message;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Status = "Approval failed: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
            await RefreshAsync();
        }
    }

    private async Task RejectAsync(BrowserPendingActionViewModel? item)
    {
        if (item is null) return;
        try
        {
            IsBusy = true;
            var result = await _automation.RejectAsync(item.Action.Id, _lifetime.Token);
            Status = result.Message;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Status = "Rejection failed: " + exception.Message;
        }
        finally
        {
            IsBusy = false;
            await RefreshAsync();
        }
    }

    private async Task SavePermissionAsync()
    {
        if (!TryGetCurrentOrigin(out var origin))
        {
            PermissionStatus = "Navigate to an HTTP or HTTPS page before changing permissions.";
            return;
        }

        try
        {
            IsBusy = true;
            await _permissions.SetDecisionAsync(origin, SelectedPermissionKind, SelectedPermissionDecision, _lifetime.Token);
            PermissionStatus = SelectedPermissionDecision == BrowserSitePermissionDecision.Ask
                ? $"{SelectedPermissionKind} reset to Ask for {CurrentOrigin}."
                : $"{SelectedPermissionKind} set to {SelectedPermissionDecision} for {CurrentOrigin}.";
            RefreshPermissions();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            PermissionStatus = "Permission update failed and was rolled back: " + exception.Message;
        }
        finally { IsBusy = false; }
    }

    private async Task RevokeOriginAsync()
    {
        if (!TryGetCurrentOrigin(out var origin))
        {
            PermissionStatus = "No active HTTP or HTTPS origin is available to revoke.";
            return;
        }

        try
        {
            IsBusy = true;
            await _permissions.RevokeOriginAsync(origin, _lifetime.Token);
            PermissionStatus = $"All saved permission decisions for {CurrentOrigin} were reset to Ask.";
            RefreshPermissions();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            PermissionStatus = "Origin revocation failed and was rolled back: " + exception.Message;
        }
        finally { IsBusy = false; }
    }

    private void OnBrowserStateChanged(object? sender, BrowserSnapshot state)
    {
        UpdateCurrentOrigin(state.Address);
        RefreshPermissions();
    }

    private void UpdateCurrentOrigin(Uri? address)
    {
        if (address is not null && address.IsAbsoluteUri && address.Scheme is "http" or "https" && string.IsNullOrEmpty(address.UserInfo))
        {
            CurrentOrigin = BrowserSitePermissionStore.CanonicalOrigin(address);
            PermissionStatus = "Saved decisions apply only to this exact origin. Ask delegates the current request to Haven's prompt flow.";
        }
        else
        {
            CurrentOrigin = "No active web origin";
            PermissionStatus = "Navigate to an HTTP or HTTPS page to manage its permissions.";
        }
        RaisePropertyChanged(nameof(CanManageOrigin));
        SavePermissionCommand.RaiseCanExecuteChanged();
        RevokeOriginCommand.RaiseCanExecuteChanged();
        LoadSelectedDecision();
    }

    private void LoadSelectedDecision()
    {
        SelectedPermissionDecision = TryGetCurrentOrigin(out var origin)
            ? _permissions.GetDecision(origin, SelectedPermissionKind)
            : BrowserSitePermissionDecision.Ask;
    }

    private bool TryGetCurrentOrigin(out Uri origin)
    {
        var address = _browser.State.Address;
        if (address is not null && address.IsAbsoluteUri && address.Scheme is "http" or "https" && string.IsNullOrEmpty(address.UserInfo))
        {
            origin = address;
            return true;
        }
        origin = null!;
        return false;
    }

    private void RefreshPermissions()
    {
        Replace(Permissions, _permissions.Permissions.Select(item => new BrowserSitePermissionViewModel(item)));
        RaisePropertyChanged(nameof(HasPermissions));
        RaisePropertyChanged(nameof(HasNoPermissions));
        LoadSelectedDecision();
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _browser.StateChanged -= OnBrowserStateChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }
}

public sealed record BrowserPendingActionViewModel(BrowserPendingAction Action)
{
    public string Kind => Action.Kind == BrowserActionKind.Download ? "Download" : "Form submission";
    public string Summary => Action.Summary;
    public string Origin => Action.Origin;
    public string Expires => $"Expires {Action.ExpiresAt.LocalDateTime:g}";
    public string Id => Action.Id.ToString();
}

public sealed record BrowserAuditEntryViewModel(BrowserAuditEntry Entry)
{
    public string Result => Entry.Succeeded ? "Completed" : "Blocked / failed";
    public string Operation => Entry.Operation.Replace('-', ' ');
    public string Origin => Entry.Origin;
    public string Detail => Entry.Detail;
    public string Recorded => Entry.RecordedAt.LocalDateTime.ToString("g");
}

public sealed record BrowserDownloadViewModel(BrowserDownloadRecord Download)
{
    public string FileName => Download.FileName;
    public string Size => Download.SizeBytes < 1024 * 1024
        ? $"{Download.SizeBytes / 1024d:0.0} KB"
        : $"{Download.SizeBytes / 1024d / 1024d:0.0} MB";
    public string Hash => Download.Sha256;
    public string Path => Download.StoredPath;
    public string Completed => Download.CompletedAt.LocalDateTime.ToString("g");
}

public sealed record BrowserSitePermissionViewModel(BrowserSitePermission Permission)
{
    public string Origin => Permission.Origin;
    public string Kind => Permission.Kind.ToString();
    public string Decision => Permission.Decision.ToString();
    public string Updated => Permission.UpdatedAt.LocalDateTime.ToString("g");
}
