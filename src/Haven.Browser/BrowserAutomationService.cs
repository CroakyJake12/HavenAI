using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Haven.Application;
using Haven.Core;

namespace Haven.Browser;

public sealed class BrowserAutomationService : IBrowserAutomationService, IDisposable
{
    private const long MaximumDownloadBytes = 250L * 1024 * 1024;
    private readonly BrowserSessionService _browser;
    private readonly IBrowserNavigationPolicy _policy;
    private readonly IBrowserAutomationStore _store;
    private readonly string _downloadDirectory;
    private readonly HttpClient _http;

    public BrowserAutomationService(
        BrowserSessionService browser,
        IBrowserNavigationPolicy policy,
        IBrowserAutomationStore store,
        IAppPaths paths)
    {
        _browser = browser;
        _policy = policy;
        _store = store;
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _downloadDirectory = string.IsNullOrWhiteSpace(profile)
            ? Path.Combine(paths.DataDirectory, "Downloads")
            : Path.Combine(profile, "Downloads", "Haven");
        _http = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false
        }) { Timeout = TimeSpan.FromMinutes(10) };
    }

    public async Task<BrowserPageSnapshot> CapturePageAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _browser.CaptureStructuredPageAsync(cancellationToken).ConfigureAwait(false);
        await AuditAsync(null, "capture", Origin(snapshot.Address), $"Captured {snapshot.Elements.Count} bounded page elements.", true, cancellationToken).ConfigureAwait(false);
        return snapshot;
    }

    public async Task<string> NavigateAsync(string address, CancellationToken cancellationToken)
    {
        var uri = NormalizeAddress(address);
        var assessment = await _policy.AssessAsync(uri, cancellationToken).ConfigureAwait(false);
        if (!assessment.IsAllowed)
        {
            await AuditAsync(null, "navigate", Origin(uri), assessment.Reason, false, cancellationToken).ConfigureAwait(false);
            throw new UnauthorizedAccessException("Navigation blocked: " + assessment.Reason);
        }
        var result = await _browser.NavigateAsync(uri.ToString(), cancellationToken).ConfigureAwait(false);
        await AuditAsync(null, "navigate", Origin(uri), $"Navigated to {uri}.", true, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<string> ClickReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var snapshot = await _browser.CaptureStructuredPageAsync(cancellationToken).ConfigureAwait(false);
        var element = FindElement(snapshot, reference);
        if (element.IsSensitive) throw new UnauthorizedAccessException("Sensitive page controls cannot be clicked by browser automation.");
        if (element.Address is { Length: > 0 } target && Uri.TryCreate(snapshot.Address, target, out var targetUri))
        {
            var assessment = await _policy.AssessAsync(targetUri, cancellationToken).ConfigureAwait(false);
            if (!assessment.IsAllowed) throw new UnauthorizedAccessException("The element points to a blocked destination: " + assessment.Reason);
        }
        if (element.SubmitsForm)
        {
            var action = NewAction(
                BrowserActionKind.SubmitElement,
                Origin(snapshot.Address),
                $"Submit the form control '{Bounded(element.Text, 120)}'",
                element.Reference,
                null);
            await _store.AddPendingAsync(action, cancellationToken).ConfigureAwait(false);
            await AuditAsync(action.Kind, "approval-requested", action.Origin, action.Summary, true, cancellationToken).ConfigureAwait(false);
            return $"Approval required before form submission. Pending browser action: {action.Id}. Open Browser safety to approve or reject it.";
        }
        var result = await _browser.ClickReferenceAsync(element.Reference, cancellationToken).ConfigureAwait(false);
        await AuditAsync(null, "click", Origin(snapshot.Address), $"Clicked {element.Kind} reference {element.Reference}.", true, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<string> FillReferenceAsync(string reference, string value, CancellationToken cancellationToken)
    {
        var snapshot = await _browser.CaptureStructuredPageAsync(cancellationToken).ConfigureAwait(false);
        var element = FindElement(snapshot, reference);
        if (element.Kind != "input" && element.Kind != "textarea" && element.Kind != "select")
            throw new InvalidOperationException("The reference is not an editable field.");
        if (element.IsSensitive || element.InputType is "password" or "file" or "hidden")
            throw new UnauthorizedAccessException("Password, file, hidden, and other sensitive fields cannot be filled by the model-facing browser tools.");
        var result = await _browser.FillReferenceAsync(element.Reference, value, cancellationToken).ConfigureAwait(false);
        await AuditAsync(null, "fill", Origin(snapshot.Address), $"Filled non-sensitive field {element.Reference}; the value was not logged.", true, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<BrowserPendingAction> RequestDownloadAsync(string address, string? suggestedFileName, CancellationToken cancellationToken)
    {
        var uri = NormalizeAddress(address);
        var assessment = await _policy.AssessAsync(uri, cancellationToken).ConfigureAwait(false);
        if (!assessment.IsAllowed) throw new UnauthorizedAccessException("Download blocked: " + assessment.Reason);
        var action = NewAction(
            BrowserActionKind.Download,
            Origin(uri),
            $"Download {uri}",
            uri.ToString(),
            string.IsNullOrWhiteSpace(suggestedFileName) ? null : suggestedFileName.Trim());
        await _store.AddPendingAsync(action, cancellationToken).ConfigureAwait(false);
        await AuditAsync(action.Kind, "approval-requested", action.Origin, action.Summary, true, cancellationToken).ConfigureAwait(false);
        return action;
    }

    public async Task<BrowserActionExecutionResult> ApproveAsync(Guid actionId, CancellationToken cancellationToken)
    {
        var action = await _store.GetActionAsync(actionId, cancellationToken).ConfigureAwait(false)
                     ?? throw new KeyNotFoundException("The browser action no longer exists.");
        if (action.State != BrowserActionState.Pending)
            return new BrowserActionExecutionResult(action.Id, action.State, $"The action is already {action.State.ToString().ToLowerInvariant()}.");
        if (action.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            var expired = action with { State = BrowserActionState.Expired, UpdatedAt = DateTimeOffset.UtcNow, Failure = "The approval expired." };
            await _store.UpdateActionAsync(expired, cancellationToken).ConfigureAwait(false);
            return new BrowserActionExecutionResult(expired.Id, expired.State, "The approval expired; request the action again.");
        }

        action = await _store.UpdateActionAsync(action with { State = BrowserActionState.Approved, UpdatedAt = DateTimeOffset.UtcNow }, cancellationToken).ConfigureAwait(false);
        try
        {
            BrowserDownloadRecord? download = null;
            string message;
            switch (action.Kind)
            {
                case BrowserActionKind.SubmitElement:
                    if (!Origin(_browser.State.Address).Equals(action.Origin, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("The active page origin changed after approval was requested.");
                    message = await _browser.ClickReferenceAsync(action.Target, cancellationToken).ConfigureAwait(false);
                    break;
                case BrowserActionKind.Download:
                    download = await DownloadAsync(action, cancellationToken).ConfigureAwait(false);
                    message = $"Downloaded {download.FileName} ({download.SizeBytes:N0} bytes) to Haven's Downloads folder.";
                    break;
                default:
                    throw new InvalidOperationException("Unsupported browser action kind.");
            }
            var executed = action with { State = BrowserActionState.Executed, UpdatedAt = DateTimeOffset.UtcNow, Failure = null };
            await _store.UpdateActionAsync(executed, cancellationToken).ConfigureAwait(false);
            await AuditAsync(action.Kind, "executed", action.Origin, message, true, cancellationToken).ConfigureAwait(false);
            return new BrowserActionExecutionResult(action.Id, executed.State, message, download);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var failed = action with { State = BrowserActionState.Failed, UpdatedAt = DateTimeOffset.UtcNow, Failure = Bounded(exception.Message, 1_000) };
            await _store.UpdateActionAsync(failed, cancellationToken).ConfigureAwait(false);
            await AuditAsync(action.Kind, "execution-failed", action.Origin, failed.Failure, false, cancellationToken).ConfigureAwait(false);
            return new BrowserActionExecutionResult(action.Id, failed.State, "Browser action failed: " + exception.Message);
        }
    }

    public async Task<BrowserActionExecutionResult> RejectAsync(Guid actionId, CancellationToken cancellationToken)
    {
        var action = await _store.GetActionAsync(actionId, cancellationToken).ConfigureAwait(false)
                     ?? throw new KeyNotFoundException("The browser action no longer exists.");
        if (action.State != BrowserActionState.Pending)
            return new BrowserActionExecutionResult(action.Id, action.State, $"The action is already {action.State.ToString().ToLowerInvariant()}.");
        var rejected = action with { State = BrowserActionState.Rejected, UpdatedAt = DateTimeOffset.UtcNow, Failure = "Rejected by the user." };
        await _store.UpdateActionAsync(rejected, cancellationToken).ConfigureAwait(false);
        await AuditAsync(action.Kind, "rejected", action.Origin, action.Summary, true, cancellationToken).ConfigureAwait(false);
        return new BrowserActionExecutionResult(action.Id, rejected.State, "Browser action rejected.");
    }

    public Task<IReadOnlyList<BrowserPendingAction>> GetPendingAsync(CancellationToken cancellationToken) => _store.GetPendingAsync(cancellationToken);
    public Task<IReadOnlyList<BrowserAuditEntry>> GetAuditAsync(int limit, CancellationToken cancellationToken) => _store.GetAuditAsync(limit, cancellationToken);
    public Task<IReadOnlyList<BrowserDownloadRecord>> GetDownloadsAsync(int limit, CancellationToken cancellationToken) => _store.GetDownloadsAsync(limit, cancellationToken);

    private async Task<BrowserDownloadRecord> DownloadAsync(BrowserPendingAction action, CancellationToken cancellationToken)
    {
        var current = new Uri(action.Target, UriKind.Absolute);
        HttpResponseMessage? response = null;
        try
        {
            for (var redirect = 0; redirect <= 8; redirect++)
            {
                var assessment = await _policy.AssessAsync(current, cancellationToken).ConfigureAwait(false);
                if (!assessment.IsAllowed) throw new UnauthorizedAccessException("Download redirect blocked: " + assessment.Reason);
                response?.Dispose();
                response = await _http.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if ((int)response.StatusCode is >= 300 and <= 399 && response.Headers.Location is { } location)
                {
                    current = location.IsAbsoluteUri ? location : new Uri(current, location);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                break;
            }
            if (response is null || !response.IsSuccessStatusCode) throw new HttpRequestException("The download exceeded the redirect limit.");
            if (response.Content.Headers.ContentLength is > MaximumDownloadBytes)
                throw new InvalidOperationException("The download exceeds Haven's 250 MB limit.");

            Directory.CreateDirectory(_downloadDirectory);
            var fileName = SafeFileName(action.SuggestedFileName)
                           ?? SafeFileName(FileNameFromHeaders(response.Content.Headers.ContentDisposition))
                           ?? SafeFileName(Path.GetFileName(current.LocalPath))
                           ?? "download.bin";
            var destination = UniquePath(_downloadDirectory, fileName);
            var temporary = destination + ".haven-download-" + Guid.NewGuid().ToString("N") + ".tmp";
            long size = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            try
            {
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var buffer = new byte[64 * 1024];
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0) break;
                    size += read;
                    if (size > MaximumDownloadBytes) throw new InvalidOperationException("The download exceeded Haven's 250 MB limit while streaming.");
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                File.Move(temporary, destination, false);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { }
            }

            var record = new BrowserDownloadRecord(
                Guid.NewGuid(), action.Id, current.ToString(), Path.GetFileName(destination), destination, size,
                Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), response.Content.Headers.ContentType?.MediaType,
                DateTimeOffset.UtcNow);
            await _store.AddDownloadAsync(record, cancellationToken).ConfigureAwait(false);
            return record;
        }
        finally { response?.Dispose(); }
    }

    private static BrowserPageElement FindElement(BrowserPageSnapshot snapshot, string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) throw new ArgumentException("An element reference is required.", nameof(reference));
        return snapshot.Elements.FirstOrDefault(item => item.Reference.Equals(reference.Trim(), StringComparison.Ordinal))
               ?? throw new KeyNotFoundException("The element reference is stale or was not present in the latest page snapshot.");
    }

    private static BrowserPendingAction NewAction(BrowserActionKind kind, string origin, string summary, string target, string? fileName)
    {
        var now = DateTimeOffset.UtcNow;
        return new BrowserPendingAction(Guid.NewGuid(), kind, origin, summary, target, fileName, BrowserActionState.Pending, now, now.AddMinutes(10), now, null);
    }

    private Task AuditAsync(BrowserActionKind? kind, string operation, string origin, string? detail, bool succeeded, CancellationToken cancellationToken) =>
        _store.AddAuditAsync(new BrowserAuditEntry(Guid.NewGuid(), kind, operation, origin, Bounded(detail ?? string.Empty, 2_000), succeeded, DateTimeOffset.UtcNow), cancellationToken);

    private static Uri NormalizeAddress(string value)
    {
        var candidate = value.Trim();
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var direct)) return direct;
        if (!candidate.Contains(' ') && candidate.Contains('.')) return new Uri("https://" + candidate, UriKind.Absolute);
        return new Uri("https://www.google.com/search?q=" + Uri.EscapeDataString(candidate), UriKind.Absolute);
    }

    private static string Origin(Uri? address) => address is null ? string.Empty : address.GetLeftPart(UriPartial.Authority);
    private static string Bounded(string value, int maximum) => value.Length <= maximum ? value : value[..maximum] + "…";

    private static string? FileNameFromHeaders(ContentDispositionHeaderValue? disposition)
    {
        var value = disposition?.FileNameStar ?? disposition?.FileName;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().Trim('"');
    }

    private static string? SafeFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var name = Path.GetFileName(value.Trim());
        foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
        name = name.Trim().TrimEnd('.');
        if (name is "" or "." or "..") return null;
        return name.Length <= 180 ? name : name[..180];
    }

    private static string UniquePath(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path)) return path;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var index = 2; index < 10_000; index++)
        {
            path = Path.Combine(directory, $"{stem} ({index}){extension}");
            if (!File.Exists(path)) return path;
        }
        throw new IOException("Could not allocate a unique download file name.");
    }

    public void Dispose() => _http.Dispose();
}
