using System.Collections.ObjectModel;
using Haven.Application;

namespace Haven.Desktop.ViewModels;

public sealed class ReliabilityStatusViewModel : ObservableObject
{
    private readonly IDatabaseMaintenance _database;
    private readonly IStartupRecoveryCoordinator _startup;
    private readonly IProductionDiagnostics _diagnostics;
    private readonly IDiagnosticsBundleService _bundles;
    private readonly IAppPaths _paths;
    private string _databaseStatus = "Not checked yet.";
    private string _startupStatus = "Startup recovery has not reported yet.";
    private string _backupStatus = "No verified backup inventory loaded.";
    private string _status = string.Empty;
    private bool _isBusy;

    public ReliabilityStatusViewModel(
        IDatabaseMaintenance database,
        IStartupRecoveryCoordinator startup,
        IProductionDiagnostics diagnostics,
        IDiagnosticsBundleService bundles,
        IAppPaths paths)
    {
        _database = database;
        _startup = startup;
        _diagnostics = diagnostics;
        _bundles = bundles;
        _paths = paths;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
    }

    public ObservableCollection<ReliabilityEventViewModel> Events { get; } = [];
    public ObservableCollection<DatabaseBackupViewModel> Backups { get; } = [];
    public AsyncRelayCommand RefreshCommand { get; }
    public string DatabaseStatus { get => _databaseStatus; private set => SetProperty(ref _databaseStatus, value); }
    public string StartupStatus { get => _startupStatus; private set => SetProperty(ref _startupStatus, value); }
    public string BackupStatus { get => _backupStatus; private set => SetProperty(ref _backupStatus, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            RefreshCommand.RaiseCanExecuteChanged();
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

            LoadBackups();
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

    private async Task LoadEventsAsync(CancellationToken cancellationToken)
    {
        var events = await _diagnostics.ReadRecentAsync(50, cancellationToken);
        Events.Clear();
        foreach (var item in events) Events.Add(new ReliabilityEventViewModel(item));
    }

    private void LoadBackups()
    {
        Backups.Clear();
        var directory = Path.Combine(_paths.DataDirectory, "Backups");
        if (!Directory.Exists(directory))
        {
            BackupStatus = "No pre-migration backups have been required yet.";
            return;
        }

        foreach (var path in Directory.EnumerateFiles(directory, "haven-v*-to-v*.db", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(File.GetCreationTimeUtc)
                     .Take(10))
        {
            var info = new FileInfo(path);
            Backups.Add(new DatabaseBackupViewModel(
                info.Name,
                info.Length,
                new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero),
                File.Exists(Path.ChangeExtension(path, ".json"))));
        }
        BackupStatus = Backups.Count == 0
            ? "No verified pre-migration backups have been created yet."
            : $"{Backups.Count} retained verified backup{(Backups.Count == 1 ? string.Empty : "s")} · newest {Backups[0].CreatedAt.LocalDateTime:g}.";
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

public sealed record DatabaseBackupViewModel(string Name, long SizeBytes, DateTimeOffset CreatedAt, bool HasManifest)
{
    public string Size => SizeBytes switch
    {
        >= 1024L * 1024 * 1024 => $"{SizeBytes / (1024d * 1024 * 1024):0.00} GB",
        >= 1024L * 1024 => $"{SizeBytes / (1024d * 1024):0.00} MB",
        >= 1024 => $"{SizeBytes / 1024d:0.0} KB",
        _ => SizeBytes + " B"
    };
    public string Verification => HasManifest ? "Verified manifest" : "Manifest missing";
}
