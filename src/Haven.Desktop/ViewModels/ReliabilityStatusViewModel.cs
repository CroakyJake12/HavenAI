using System.Collections.ObjectModel;
using Haven.Application;

namespace Haven.Desktop.ViewModels;

public sealed class ReliabilityStatusViewModel : ObservableObject
{
    private readonly IDatabaseMaintenance _database;
    private readonly IDatabaseRestoreService _restore;
    private readonly IStartupRecoveryCoordinator _startup;
    private readonly IProductionDiagnostics _diagnostics;
    private readonly IDiagnosticsBundleService _bundles;
    private string _databaseStatus = "Not checked yet.";
    private string _startupStatus = "Startup recovery has not reported yet.";
    private string _backupStatus = "No verified backup inventory loaded.";
    private string _pendingRestoreStatus = "No database restore is pending.";
    private string _status = string.Empty;
    private bool _isBusy;
    private bool _isRestoreConfirming;
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

    public ObservableCollection<ReliabilityEventViewModel> Events { get; } = [];
    public ObservableCollection<DatabaseBackupViewModel> Backups { get; } = [];
    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand<DatabaseBackupViewModel> RequestRestoreCommand { get; }
    public AsyncRelayCommand ConfirmRestoreCommand { get; }
    public RelayCommand CancelRestoreConfirmationCommand { get; }
    public AsyncRelayCommand CancelPendingRestoreCommand { get; }
    public string DatabaseStatus { get => _databaseStatus; private set => SetProperty(ref _databaseStatus, value); }
    public string StartupStatus { get => _startupStatus; private set => SetProperty(ref _startupStatus, value); }
    public string BackupStatus { get => _backupStatus; private set => SetProperty(ref _backupStatus, value); }
    public string PendingRestoreStatus { get => _pendingRestoreStatus; private set => SetProperty(ref _pendingRestoreStatus, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
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

    public Task InitializeAsync(CancellationToken cancellationToken) => RefreshAsync(cancellationToken);

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

    private Task RefreshAsync() => RefreshAsync(CancellationToken.None);

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

    private void CancelRestoreConfirmation()
    {
        RestoreCandidate = null;
        IsRestoreConfirming = false;
        Status = "Restore selection cancelled. No data was changed.";
    }

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

    private async Task LoadEventsAsync(CancellationToken cancellationToken)
    {
        var events = await _diagnostics.ReadRecentAsync(50, cancellationToken);
        Events.Clear();
        foreach (var item in events) Events.Add(new ReliabilityEventViewModel(item));
    }
}

public sealed record ReliabilityEventViewModel(ReliabilityEvent Event)
{
    public string Time => Event.Timestamp.LocalDateTime.ToString("g", System.Globalization.CultureInfo.CurrentCulture);
    public string Severity => Event.Severity.ToString();
    public string Source => Event.Component + " / " + Event.EventName;
    public string Message => Event.Message;
    public string CorrelationId => Event.CorrelationId;
}

public sealed record DatabaseBackupViewModel(ManagedDatabaseBackup Backup)
{
    public string FileName => Backup.FileName;
    public string Name => $"Schema {Backup.FromVersion} backup · {Backup.CreatedAt.LocalDateTime:g}";
    public string Size => Backup.SizeBytes switch
    {
        >= 1024L * 1024 * 1024 => $"{Backup.SizeBytes / (1024d * 1024 * 1024):0.00} GB",
        >= 1024L * 1024 => $"{Backup.SizeBytes / (1024d * 1024):0.00} MB",
        >= 1024 => $"{Backup.SizeBytes / 1024d:0.0} KB",
        _ => Backup.SizeBytes + " B"
    };
    public bool IsVerified => Backup.IsVerified;
    public string Verification => Backup.IsVerified ? "Verified" : "Blocked: " + Backup.VerificationMessage;
    public string Schema => $"v{Backup.FromVersion} → target v{Backup.ToVersion}";
}
