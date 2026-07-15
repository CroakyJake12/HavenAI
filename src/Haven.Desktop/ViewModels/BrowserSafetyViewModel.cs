using System.Collections.ObjectModel;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

public sealed class BrowserSafetyViewModel : ObservableObject
{
    private readonly IBrowserAutomationService _automation;
    private string _status = "Loading browser safety activity…";
    private bool _isBusy;

    public BrowserSafetyViewModel(IBrowserAutomationService automation)
    {
        _automation = automation;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ApproveCommand = new AsyncRelayCommand<BrowserPendingActionViewModel>(ApproveAsync);
        RejectCommand = new AsyncRelayCommand<BrowserPendingActionViewModel>(RejectAsync);
        _ = RefreshAsync();
    }

    public ObservableCollection<BrowserPendingActionViewModel> Pending { get; } = [];
    public ObservableCollection<BrowserAuditEntryViewModel> Audit { get; } = [];
    public ObservableCollection<BrowserDownloadViewModel> Downloads { get; } = [];
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand<BrowserPendingActionViewModel> ApproveCommand { get; }
    public AsyncRelayCommand<BrowserPendingActionViewModel> RejectCommand { get; }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public bool HasPending => Pending.Count > 0;
    public bool HasNoPending => !HasPending;

    private async Task RefreshAsync()
    {
        try
        {
            IsBusy = true;
            var pending = await _automation.GetPendingAsync(CancellationToken.None);
            var audit = await _automation.GetAuditAsync(100, CancellationToken.None);
            var downloads = await _automation.GetDownloadsAsync(50, CancellationToken.None);
            Replace(Pending, pending.Select(item => new BrowserPendingActionViewModel(item)));
            Replace(Audit, audit.Select(item => new BrowserAuditEntryViewModel(item)));
            Replace(Downloads, downloads.Select(item => new BrowserDownloadViewModel(item)));
            RaisePropertyChanged(nameof(HasPending));
            RaisePropertyChanged(nameof(HasNoPending));
            Status = Pending.Count == 0
                ? "No browser actions are waiting for approval."
                : $"{Pending.Count} browser action{(Pending.Count == 1 ? string.Empty : "s")} waiting for approval.";
        }
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
            var result = await _automation.ApproveAsync(item.Action.Id, CancellationToken.None);
            Status = result.Message;
        }
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
            var result = await _automation.RejectAsync(item.Action.Id, CancellationToken.None);
            Status = result.Message;
        }
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

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
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
