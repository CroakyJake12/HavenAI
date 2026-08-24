using System.Collections.ObjectModel;
using System.Diagnostics;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views.Pages.Browser;

public sealed partial class BrowserPage
{
    private const int HistoryPageSize = 80;
    private IBrowserAutomationService? _managementAutomation;
    private bool _isDownloadsOpen;
    private bool _isTabsManagerOpen;
    private bool _isPageActionsOpen;
    private string _managementSearch = string.Empty;
    private int _historyVisibleCount = HistoryPageSize;

    public ObservableCollection<BrowserDownloadRecord> Downloads { get; } = [];
    public bool IsDownloadsOpen { get => _isDownloadsOpen; private set => SetProperty(ref _isDownloadsOpen, value); }
    public bool IsTabsManagerOpen { get => _isTabsManagerOpen; private set => SetProperty(ref _isTabsManagerOpen, value); }
    public bool IsPageActionsOpen { get => _isPageActionsOpen; private set => SetProperty(ref _isPageActionsOpen, value); }
    public string ManagementSearch
    {
        get => _managementSearch;
        set
        {
            if (!SetProperty(ref _managementSearch, value)) return;
            _historyVisibleCount = HistoryPageSize;
            RaisePropertyChanged(nameof(HasMoreHistory));
            RaisePropertyChanged(nameof(HistoryResultSummary));
        }
    }

    public bool HasMoreHistory => FilterHistory().Skip(_historyVisibleCount).Any();
    public string HistoryResultSummary
    {
        get
        {
            var total = FilterHistory().Count();
            var visible = Math.Min(_historyVisibleCount, total);
            return visible == total
                ? $"{total} history item{(total == 1 ? string.Empty : "s")}"
                : $"Showing {visible} of {total} history items";
        }
    }

    public RelayCommand ToggleDownloadsCommand { get; private set; } = null!;
    public RelayCommand ToggleTabsManagerCommand { get; private set; } = null!;
    public RelayCommand TogglePageActionsCommand { get; private set; } = null!;
    public RelayCommand LoadMoreHistoryCommand { get; private set; } = null!;
    public AsyncRelayCommand RefreshDownloadsCommand { get; private set; } = null!;
    public RelayCommand<BrowserDownloadRecord> OpenDownloadCommand { get; private set; } = null!;
    public RelayCommand<BrowserDownloadRecord> RevealDownloadCommand { get; private set; } = null!;

    private void InitializeRecoveryManagement()
    {
        _managementAutomation = BrowserAutomationRegistry.Resolve(_browser);
        ToggleDownloadsCommand = new RelayCommand(() =>
        {
            TogglePanel(nameof(IsDownloadsOpen));
            if (IsDownloadsOpen) _ = RefreshDownloadsAsync();
        });
        ToggleTabsManagerCommand = new RelayCommand(() => TogglePanel(nameof(IsTabsManagerOpen)));
        TogglePageActionsCommand = new RelayCommand(() => TogglePanel(nameof(IsPageActionsOpen)));
        LoadMoreHistoryCommand = new RelayCommand(() =>
        {
            _historyVisibleCount = Math.Min(_historyVisibleCount + HistoryPageSize, FilterHistory().Count());
            RaisePropertyChanged(nameof(HasMoreHistory));
            RaisePropertyChanged(nameof(HistoryResultSummary));
            RaisePropertyChanged(nameof(ManagementSearch));
        });
        RefreshDownloadsCommand = new AsyncRelayCommand(RefreshDownloadsAsync);
        OpenDownloadCommand = new RelayCommand<BrowserDownloadRecord>(OpenDownload);
        RevealDownloadCommand = new RelayCommand<BrowserDownloadRecord>(RevealDownload);
        _ = RefreshDownloadsAsync();
    }

    internal IReadOnlyList<BrowserHistoryEntry> VisibleHistory() =>
        FilterHistory().Take(_historyVisibleCount).ToArray();

    internal IReadOnlyList<BrowserBookmark> VisibleBookmarks()
    {
        var query = ManagementSearch.Trim();
        return Bookmarks
            .Where(item => string.IsNullOrWhiteSpace(query)
                || item.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.Address.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.Group.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    internal IReadOnlyList<BrowserDownloadRecord> VisibleDownloads()
    {
        var query = ManagementSearch.Trim();
        return Downloads
            .Where(item => string.IsNullOrWhiteSpace(query)
                || item.FileName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.Address.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.StoredPath.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.CompletedAt)
            .ToArray();
    }

    internal IReadOnlyList<BrowserTabViewModel> VisibleManagedTabs()
    {
        var query = ManagementSearch.Trim();
        return Tabs
            .Where(item => string.IsNullOrWhiteSpace(query)
                || item.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.Address.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.Group.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    internal static string FormatDownloadSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024d:0.#} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024d * 1024):0.#} MB";
        return $"{bytes / (1024d * 1024 * 1024):0.#} GB";
    }

    private IEnumerable<BrowserHistoryEntry> FilterHistory()
    {
        var query = ManagementSearch.Trim();
        return History.Where(item => string.IsNullOrWhiteSpace(query)
            || item.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || item.Address.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private async Task RefreshDownloadsAsync()
    {
        if (_managementAutomation is null) return;
        try
        {
            var rows = await _managementAutomation.GetDownloadsAsync(500, CancellationToken.None);
            Replace(Downloads, rows.OrderByDescending(item => item.CompletedAt));
            Status = Downloads.Count == 0
                ? "No completed downloads yet."
                : $"{Downloads.Count} completed download{(Downloads.Count == 1 ? string.Empty : "s")} available.";
            RaisePropertyChanged(nameof(Downloads));
        }
        catch (Exception ex)
        {
            Status = $"Could not load downloads: {ex.Message}";
        }
    }

    private void OpenDownload(BrowserDownloadRecord? download)
    {
        if (download is null) return;
        try
        {
            if (!File.Exists(download.StoredPath))
            {
                Status = $"Downloaded file is no longer available: {download.FileName}";
                return;
            }
            Process.Start(new ProcessStartInfo(download.StoredPath) { UseShellExecute = true });
            Status = $"Opened {download.FileName}.";
        }
        catch (Exception ex)
        {
            Status = $"Could not open {download.FileName}: {ex.Message}";
        }
    }

    private void RevealDownload(BrowserDownloadRecord? download)
    {
        if (download is null) return;
        try
        {
            if (!File.Exists(download.StoredPath))
            {
                Status = $"Downloaded file is no longer available: {download.FileName}";
                return;
            }
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{download.StoredPath}\"") { UseShellExecute = true });
            Status = $"Located {download.FileName} in File Explorer.";
        }
        catch (Exception ex)
        {
            Status = $"Could not reveal {download.FileName}: {ex.Message}";
        }
    }
}
