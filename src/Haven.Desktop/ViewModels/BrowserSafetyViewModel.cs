/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/ViewModels/BrowserSafetyViewModel.cs, in the Desktop presentation-model layer, exposing bindable state and commands to Avalonia views.
 * What: This file owns BrowserSafetyViewModel, BrowserPendingActionViewModel, BrowserAuditEntryViewModel, BrowserDownloadViewModel, BrowserSitePermissionViewModel. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Keeping UI state here makes the XAML declarative and keeps behavior testable without recreating the full window.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.ObjectModel;
using Haven.Application;
using Haven.Browser;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Represents browser safety view model and keeps its related state and behavior together.
/// </summary>
public sealed class BrowserSafetyViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Stores automation locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IBrowserAutomationService _automation;
    /// <summary>
    /// Stores browser locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly BrowserSessionService _browser;
    /// <summary>
    /// Stores permissions locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly BrowserSitePermissionStore _permissions;
    /// <summary>
    /// Stores lifetime locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly CancellationTokenSource _lifetime = new();
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _status = "Loading browser safety activity…";
    /// <summary>
    /// Stores permission status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _permissionStatus = "Navigate to an HTTP or HTTPS page to manage its permissions.";
    /// <summary>
    /// Stores current origin locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _currentOrigin = "No active web origin";
    /// <summary>
    /// Stores selected permission kind locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private BrowserSitePermissionKind _selectedPermissionKind = BrowserSitePermissionKind.Notifications;
    /// <summary>
    /// Stores selected permission decision locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private BrowserSitePermissionDecision _selectedPermissionDecision = BrowserSitePermissionDecision.Ask;
    /// <summary>
    /// Stores is busy locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isBusy;
    /// <summary>
    /// Stores disposed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
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

    /// <summary>
    /// Gets or updates pending, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<BrowserPendingActionViewModel> Pending { get; } = [];
    /// <summary>
    /// Gets or updates audit, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<BrowserAuditEntryViewModel> Audit { get; } = [];
    /// <summary>
    /// Gets or updates downloads, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<BrowserDownloadViewModel> Downloads { get; } = [];
    /// <summary>
    /// Gets or updates permissions, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<BrowserSitePermissionViewModel> Permissions { get; } = [];
    /// <summary>
    /// Gets or updates permission kinds, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<BrowserSitePermissionKind> PermissionKinds { get; } = Enum.GetValues<BrowserSitePermissionKind>();
    /// <summary>
    /// Gets or updates permission decisions, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<BrowserSitePermissionDecision> PermissionDecisions { get; } = Enum.GetValues<BrowserSitePermissionDecision>();
    /// <summary>
    /// Gets or updates refresh command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand RefreshCommand { get; }
    /// <summary>
    /// Gets or updates approve command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<BrowserPendingActionViewModel> ApproveCommand { get; }
    /// <summary>
    /// Gets or updates reject command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<BrowserPendingActionViewModel> RejectCommand { get; }
    /// <summary>
    /// Gets or updates save permission command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand SavePermissionCommand { get; }
    /// <summary>
    /// Gets or updates revoke origin command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand RevokeOriginCommand { get; }
    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>
    /// Gets or updates permission status, the bindable or domain state represented by this property.
    /// </summary>
    public string PermissionStatus { get => _permissionStatus; private set => SetProperty(ref _permissionStatus, value); }
    /// <summary>
    /// Gets or updates current origin, the bindable or domain state represented by this property.
    /// </summary>
    public string CurrentOrigin { get => _currentOrigin; private set => SetProperty(ref _currentOrigin, value); }
    /// <summary>
    /// Reports whether is busy is true for the current state.
    /// </summary>
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    /// <summary>
    /// Reports whether has pending is true for the current state.
    /// </summary>
    public bool HasPending => Pending.Count > 0;
    /// <summary>
    /// Reports whether has no pending is true for the current state.
    /// </summary>
    public bool HasNoPending => !HasPending;
    /// <summary>
    /// Reports whether has permissions is true for the current state.
    /// </summary>
    public bool HasPermissions => Permissions.Count > 0;
    /// <summary>
    /// Reports whether has no permissions is true for the current state.
    /// </summary>
    public bool HasNoPermissions => !HasPermissions;
    /// <summary>
    /// Reports whether can manage origin is true for the current state.
    /// </summary>
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

    /// <summary>
    /// Performs refresh async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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
            Replace(Pending, (await pendingTask).Select(item => new BrowserPendingActionViewModel(item)));
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

    /// <summary>
    /// Performs approve async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs reject async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs save permission async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs revoke origin async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Handles the browser state changed event raised by the UI or runtime.
    /// </summary>
    private void OnBrowserStateChanged(object? sender, BrowserSnapshot state)
    {
        UpdateCurrentOrigin(state.Address);
        RefreshPermissions();
    }

    /// <summary>
    /// Performs the update current origin step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the load selected decision step owned by this component.
    /// </summary>
    private void LoadSelectedDecision()
    {
        SelectedPermissionDecision = TryGetCurrentOrigin(out var origin)
            ? _permissions.GetDecision(origin, SelectedPermissionKind)
            : BrowserSitePermissionDecision.Ask;
    }

    /// <summary>
    /// Attempts to get current origin and reports the result without using failure for normal control flow.
    /// </summary>
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

    /// <summary>
    /// Performs the refresh permissions step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
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

/// <summary>
/// Represents browser pending action view model and keeps its related state and behavior together.
/// </summary>
public sealed record BrowserPendingActionViewModel(BrowserPendingAction Action)
{
    /// <summary>
    /// Gets or updates kind, the bindable or domain state represented by this property.
    /// </summary>
    public string Kind => Action.Kind == BrowserActionKind.Download ? "Download" : "Form submission";
    /// <summary>
    /// Gets or updates summary, the bindable or domain state represented by this property.
    /// </summary>
    public string Summary => Action.Summary;
    /// <summary>
    /// Gets or updates origin, the bindable or domain state represented by this property.
    /// </summary>
    public string Origin => Action.Origin;
    /// <summary>
    /// Gets or updates expires, the bindable or domain state represented by this property.
    /// </summary>
    public string Expires => $"Expires {Action.ExpiresAt.LocalDateTime:g}";
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public string Id => Action.Id.ToString();
}

/// <summary>
/// Represents browser audit entry view model and keeps its related state and behavior together.
/// </summary>
public sealed record BrowserAuditEntryViewModel(BrowserAuditEntry Entry)
{
    /// <summary>
    /// Gets or updates result, the bindable or domain state represented by this property.
    /// </summary>
    public string Result => Entry.Succeeded ? "Completed" : "Blocked / failed";
    /// <summary>
    /// Gets or updates operation, the bindable or domain state represented by this property.
    /// </summary>
    public string Operation => Entry.Operation.Replace('-', ' ');
    /// <summary>
    /// Gets or updates origin, the bindable or domain state represented by this property.
    /// </summary>
    public string Origin => Entry.Origin;
    /// <summary>
    /// Gets or updates detail, the bindable or domain state represented by this property.
    /// </summary>
    public string Detail => Entry.Detail;
    /// <summary>
    /// Gets or updates recorded, the bindable or domain state represented by this property.
    /// </summary>
    public string Recorded => Entry.RecordedAt.LocalDateTime.ToString("g");
}

/// <summary>
/// Represents browser download view model and keeps its related state and behavior together.
/// </summary>
public sealed record BrowserDownloadViewModel(BrowserDownloadRecord Download)
{
    /// <summary>
    /// Gets or updates file name, the bindable or domain state represented by this property.
    /// </summary>
    public string FileName => Download.FileName;
    /// <summary>
    /// Gets or updates size, the bindable or domain state represented by this property.
    /// </summary>
    public string Size => Download.SizeBytes < 1024 * 1024
        ? $"{Download.SizeBytes / 1024d:0.0} KB"
        : $"{Download.SizeBytes / 1024d / 1024d:0.0} MB";
    /// <summary>
    /// Reports whether hash is true for the current state.
    /// </summary>
    public string Hash => Download.Sha256;
    /// <summary>
    /// Gets or updates path, the bindable or domain state represented by this property.
    /// </summary>
    public string Path => Download.StoredPath;
    /// <summary>
    /// Gets or updates completed, the bindable or domain state represented by this property.
    /// </summary>
    public string Completed => Download.CompletedAt.LocalDateTime.ToString("g");
}

/// <summary>
/// Represents browser site permission view model and keeps its related state and behavior together.
/// </summary>
public sealed record BrowserSitePermissionViewModel(BrowserSitePermission Permission)
{
    /// <summary>
    /// Gets or updates origin, the bindable or domain state represented by this property.
    /// </summary>
    public string Origin => Permission.Origin;
    /// <summary>
    /// Gets or updates kind, the bindable or domain state represented by this property.
    /// </summary>
    public string Kind => Permission.Kind.ToString();
    /// <summary>
    /// Gets or updates decision, the bindable or domain state represented by this property.
    /// </summary>
    public string Decision => Permission.Decision.ToString();
    /// <summary>
    /// Gets or updates updated, the bindable or domain state represented by this property.
    /// </summary>
    public string Updated => Permission.UpdatedAt.LocalDateTime.ToString("g");
}
