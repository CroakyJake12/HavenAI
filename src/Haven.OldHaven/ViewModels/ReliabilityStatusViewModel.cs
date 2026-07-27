/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/ViewModels/ReliabilityStatusViewModel.cs, in the Desktop presentation-model layer, exposing bindable state and commands to Avalonia views.
 * What: This file owns ReliabilityStatusViewModel, ReliabilityEventViewModel, DatabaseBackupViewModel. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Keeping UI state here makes the XAML declarative and keeps behavior testable without recreating the full window.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.ObjectModel;
using Haven.Application;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Represents reliability status view model and keeps its related state and behavior together.
/// </summary>
public sealed class ReliabilityStatusViewModel : ObservableObject
{
    /// <summary>
    /// Stores database locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IDatabaseMaintenance _database;
    /// <summary>
    /// Stores restore locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IDatabaseRestoreService _restore;
    /// <summary>
    /// Stores startup locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IStartupRecoveryCoordinator _startup;
    /// <summary>
    /// Stores diagnostics locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IProductionDiagnostics _diagnostics;
    /// <summary>
    /// Stores bundles locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IDiagnosticsBundleService _bundles;
    /// <summary>
    /// Stores database status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _databaseStatus = "Not checked yet.";
    /// <summary>
    /// Stores startup status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _startupStatus = "Startup recovery has not reported yet.";
    /// <summary>
    /// Stores backup status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _backupStatus = "No verified backup inventory loaded.";
    /// <summary>
    /// Stores pending restore status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _pendingRestoreStatus = "No database restore is pending.";
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _status = string.Empty;
    /// <summary>
    /// Stores is busy locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isBusy;
    /// <summary>
    /// Stores is restore confirming locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isRestoreConfirming;
    /// <summary>
    /// Stores restore candidate locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private DatabaseBackupViewModel? _restoreCandidate;

    public ReliabilityStatusViewModel(
        IDatabaseMaintenance database,
        IDatabaseRestoreService restore,
        IStartupRecoveryCoordinator startup,
        IProductionDiagnostics diagnostics,
        IDiagnosticsBundleService bundles)
    {
        _database = database;
        _restore = restore;
        _startup = startup;
        _diagnostics = diagnostics;
        _bundles = bundles;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        RequestRestoreCommand = new RelayCommand<DatabaseBackupViewModel>(BeginRestore);
        ConfirmRestoreCommand = new AsyncRelayCommand(ConfirmRestoreAsync, () => RestoreCandidate is not null && !IsBusy);
        CancelRestoreConfirmationCommand = new RelayCommand(CancelRestoreConfirmation);
        CancelPendingRestoreCommand = new AsyncRelayCommand(CancelPendingRestoreAsync, () => HasPendingRestore && !IsBusy);
    }

    /// <summary>
    /// Gets or updates events, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<ReliabilityEventViewModel> Events { get; } = [];
    /// <summary>
    /// Gets or updates backups, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<DatabaseBackupViewModel> Backups { get; } = [];
    /// <summary>
    /// Gets or updates refresh command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand RefreshCommand { get; }
    /// <summary>
    /// Gets or updates request restore command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<DatabaseBackupViewModel> RequestRestoreCommand { get; }
    /// <summary>
    /// Gets or updates confirm restore command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand ConfirmRestoreCommand { get; }
    /// <summary>
    /// Reports whether cancel restore confirmation command is true for the current state.
    /// </summary>
    public RelayCommand CancelRestoreConfirmationCommand { get; }
    /// <summary>
    /// Reports whether cancel pending restore command is true for the current state.
    /// </summary>
    public AsyncRelayCommand CancelPendingRestoreCommand { get; }
    /// <summary>
    /// Gets or updates database status, the bindable or domain state represented by this property.
    /// </summary>
    public string DatabaseStatus { get => _databaseStatus; private set => SetProperty(ref _databaseStatus, value); }
    /// <summary>
    /// Gets or updates startup status, the bindable or domain state represented by this property.
    /// </summary>
    public string StartupStatus { get => _startupStatus; private set => SetProperty(ref _startupStatus, value); }
    /// <summary>
    /// Gets or updates backup status, the bindable or domain state represented by this property.
    /// </summary>
    public string BackupStatus { get => _backupStatus; private set => SetProperty(ref _backupStatus, value); }
    /// <summary>
    /// Gets or updates pending restore status, the bindable or domain state represented by this property.
    /// </summary>
    public string PendingRestoreStatus { get => _pendingRestoreStatus; private set => SetProperty(ref _pendingRestoreStatus, value); }
    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>
    /// Reports whether restore confirming applies to the current state.
    /// </summary>
    public bool IsRestoreConfirming { get => _isRestoreConfirming; private set => SetProperty(ref _isRestoreConfirming, value); }
    public DatabaseBackupViewModel? RestoreCandidate
    {
        get => _restoreCandidate;
        private set
        {
            if (!SetProperty(ref _restoreCandidate, value)) return;
            ConfirmRestoreCommand.RaiseCanExecuteChanged();
        }
    }
    /// <summary>
    /// Reports whether pending restore applies to the current state.
    /// </summary>
    public bool HasPendingRestore { get; private set; }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            RefreshCommand.RaiseCanExecuteChanged();
            ConfirmRestoreCommand.RaiseCanExecuteChanged();
            CancelPendingRestoreCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Performs initialize asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task InitializeAsync(CancellationToken cancellationToken) => RefreshAsync(cancellationToken);

    /// <summary>
    /// Creates bundle async with the invariants required by its callers.
    /// </summary>
    public async Task<string> CreateBundleAsync(string destinationDirectory, CancellationToken cancellationToken)
    {
        try
        {
            IsBusy = true;
            Status = "Creating a redacted support bundle…";
            var path = await _bundles.CreateBundleAsync(destinationDirectory, cancellationToken);
            Status = "Support bundle created: " + path;
            await LoadEventsAsync(cancellationToken);
            return path;
        }
        catch (Exception ex)
        {
            Status = "Support bundle failed: " + ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
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
        try
        {
            IsBusy = true;
            Status = "Checking Haven reliability state…";
            var health = await _database.VerifyIntegrityAsync(cancellationToken);
            DatabaseStatus = health.IsHealthy
                ? $"Healthy · schema {health.SchemaVersion} · SQLite integrity and foreign-key checks passed at {health.CheckedAt.LocalDateTime:g}."
                : $"Attention required · schema {health.SchemaVersion} · {string.Join(" · ", health.IntegrityMessages.Concat(health.ForeignKeyViolations).Take(4))}";

            var startup = _startup.Current;
            StartupStatus = startup.StartedAt == DateTimeOffset.MinValue
                ? "Startup recovery has not run in this process."
                : startup.IsSafeMode
                    ? $"Recovery safe mode · {startup.RecentUncleanStarts} recent unclean starts · {startup.Reason}"
                    : $"Normal mode · {startup.RecentUncleanStarts} recent unclean start{(startup.RecentUncleanStarts == 1 ? string.Empty : "s")}.";

            await LoadBackupsAndRestoreAsync(cancellationToken);
            await LoadEventsAsync(cancellationToken);
            Status = health.IsHealthy ? "Reliability checks completed." : "The database needs attention. Haven has recorded the failed integrity result.";
        }
        catch (Exception ex)
        {
            DatabaseStatus = "Integrity check failed to run: " + ex.Message;
            Status = "Reliability refresh failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Performs the begin restore step owned by this component.
    /// </summary>
    private void BeginRestore(DatabaseBackupViewModel? backup)
    {
        if (backup is null) return;
        if (!backup.IsVerified)
        {
            Status = "That backup is not verified and cannot be restored: " + backup.Verification;
            return;
        }
        RestoreCandidate = backup;
        IsRestoreConfirming = true;
        Status = "Review the restore warning before scheduling it.";
    }

    /// <summary>
    /// Performs confirm restore asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ConfirmRestoreAsync()
    {
        if (RestoreCandidate is null) return;
        try
        {
            IsBusy = true;
            var pending = await _restore.RequestRestoreAsync(RestoreCandidate.FileName, CancellationToken.None);
            HasPendingRestore = true;
            RaisePropertyChanged(nameof(HasPendingRestore));
            PendingRestoreStatus = $"Restore pending: {pending.BackupFileName}. It will be re-verified and applied before SQLite opens on the next Haven launch.";
            Status = "Verified database restore scheduled. Close Haven normally, then launch it again to apply the restore.";
            IsRestoreConfirming = false;
            RestoreCandidate = null;
            CancelPendingRestoreCommand.RaiseCanExecuteChanged();
            await LoadEventsAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Status = "Restore request failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Reports whether cancel restore confirmation is true for the current state.
    /// </summary>
    private void CancelRestoreConfirmation()
    {
        RestoreCandidate = null;
        IsRestoreConfirming = false;
        Status = "Restore selection cancelled. No data was changed.";
    }

    /// <summary>
    /// Reports whether cancel pending restore async is true for the current state.
    /// </summary>
    private async Task CancelPendingRestoreAsync()
    {
        try
        {
            IsBusy = true;
            await _restore.CancelPendingAsync(CancellationToken.None);
            HasPendingRestore = false;
            RaisePropertyChanged(nameof(HasPendingRestore));
            PendingRestoreStatus = "No database restore is pending.";
            Status = "Pending restore cancelled. No database file was changed.";
            CancelPendingRestoreCommand.RaiseCanExecuteChanged();
            await LoadEventsAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Status = "Could not cancel the pending restore: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Performs load backups and restore asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task LoadBackupsAndRestoreAsync(CancellationToken cancellationToken)
    {
        var backups = await _restore.GetBackupsAsync(cancellationToken);
        Backups.Clear();
        foreach (var backup in backups.Take(10)) Backups.Add(new DatabaseBackupViewModel(backup));
        BackupStatus = Backups.Count == 0
            ? "No verified pre-migration backups have been required yet."
            : $"{Backups.Count} retained managed backup{(Backups.Count == 1 ? string.Empty : "s")} · {Backups.Count(item => item.IsVerified)} verified.";

        var pending = await _restore.GetPendingAsync(cancellationToken);
        HasPendingRestore = pending?.IsPending == true;
        RaisePropertyChanged(nameof(HasPendingRestore));
        PendingRestoreStatus = pending is null
            ? "No database restore is pending."
            : $"Restore pending: {pending.BackupFileName}, requested {pending.RequestedAt.LocalDateTime:g}. It applies only on the next launch.";
        CancelPendingRestoreCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Performs load events asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task LoadEventsAsync(CancellationToken cancellationToken)
    {
        var events = await _diagnostics.ReadRecentAsync(50, cancellationToken);
        Events.Clear();
        foreach (var item in events) Events.Add(new ReliabilityEventViewModel(item));
    }
}

/// <summary>
/// Represents reliability event view model and keeps its related state and behavior together.
/// </summary>
public sealed record ReliabilityEventViewModel(ReliabilityEvent Event)
{
    /// <summary>
    /// Gets or updates time, the bindable or domain state represented by this property.
    /// </summary>
    public string Time => Event.Timestamp.LocalDateTime.ToString("g", System.Globalization.CultureInfo.CurrentCulture);
    /// <summary>
    /// Gets or updates severity, the bindable or domain state represented by this property.
    /// </summary>
    public string Severity => Event.Severity.ToString();
    /// <summary>
    /// Gets or updates source, the bindable or domain state represented by this property.
    /// </summary>
    public string Source => Event.Component + " / " + Event.EventName;
    /// <summary>
    /// Gets or updates message, the bindable or domain state represented by this property.
    /// </summary>
    public string Message => Event.Message;
    /// <summary>
    /// Gets or updates correlation id, the bindable or domain state represented by this property.
    /// </summary>
    public string CorrelationId => Event.CorrelationId;
}

/// <summary>
/// Represents database backup view model and keeps its related state and behavior together.
/// </summary>
public sealed record DatabaseBackupViewModel(ManagedDatabaseBackup Backup)
{
    /// <summary>
    /// Gets or updates file name, the bindable or domain state represented by this property.
    /// </summary>
    public string FileName => Backup.FileName;
    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public string Name => $"Schema {Backup.FromVersion} backup · {Backup.CreatedAt.LocalDateTime:g}";
    /// <summary>
    /// Gets or updates size, the bindable or domain state represented by this property.
    /// </summary>
    public string Size => Backup.SizeBytes switch
    {
        >= 1024L * 1024 * 1024 => $"{Backup.SizeBytes / (1024d * 1024 * 1024):0.00} GB",
        >= 1024L * 1024 => $"{Backup.SizeBytes / (1024d * 1024):0.00} MB",
        >= 1024 => $"{Backup.SizeBytes / 1024d:0.0} KB",
        _ => Backup.SizeBytes + " B"
    };
    /// <summary>
    /// Reports whether verified applies to the current state.
    /// </summary>
    public bool IsVerified => Backup.IsVerified;
    /// <summary>
    /// Gets or updates verification, the bindable or domain state represented by this property.
    /// </summary>
    public string Verification => Backup.IsVerified ? "Verified" : "Blocked: " + Backup.VerificationMessage;
    /// <summary>
    /// Gets or updates schema, the bindable or domain state represented by this property.
    /// </summary>
    public string Schema => $"v{Backup.FromVersion} → target v{Backup.ToVersion}";
}
