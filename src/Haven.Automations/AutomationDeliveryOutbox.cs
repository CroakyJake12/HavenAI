using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Automations;

/// <summary>
/// Small cross-process outbox used by the scheduled worker and desktop app. A separate
/// lock file serializes read/modify/write operations; payload replacement is atomic.
/// </summary>
public sealed class AutomationDeliveryOutbox : IAutomationDeliveryOutbox
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _path;
    private readonly string _lockPath;

    public AutomationDeliveryOutbox(IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        Directory.CreateDirectory(paths.DataDirectory);
        _path = Path.Combine(paths.DataDirectory, "automation-deliveries.json");
        _lockPath = Path.Combine(paths.DataDirectory, "automation-deliveries.lock");
    }

    public async Task EnqueueAsync(
        AutomationDelivery delivery,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        await using var processLock = await AcquireLockAsync(cancellationToken).ConfigureAwait(false);
        var existing = await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
        existing.RemoveAll(item => item.Id == delivery.Id);
        existing.Add(delivery);
        if (existing.Count > 500)
            existing.RemoveRange(0, existing.Count - 500);
        await WriteUnsafeAsync(existing, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AutomationDelivery>> DrainAsync(
        CancellationToken cancellationToken)
    {
        await using var processLock = await AcquireLockAsync(cancellationToken).ConfigureAwait(false);
        var existing = await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
        if (existing.Count == 0) return [];
        await WriteUnsafeAsync([], cancellationToken).ConfigureAwait(false);
        return existing.OrderBy(item => item.CreatedAt).ToArray();
    }

    private async Task<FileStream> AcquireLockAsync(CancellationToken cancellationToken)
    {
        IOException? lastError = null;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    _lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    1,
                    FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            }
            catch (IOException ex)
            {
                lastError = ex;
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }
        throw new IOException("The automation delivery outbox remained locked by another Haven process.", lastError);
    }

    private async Task<List<AutomationDelivery>> ReadUnsafeAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return [];
        try
        {
            await using var stream = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<List<AutomationDelivery>>(
                       stream,
                       JsonOptions,
                       cancellationToken).ConfigureAwait(false)
                   ?? [];
        }
        catch (JsonException)
        {
            QuarantineCorruptFile();
            return [];
        }
    }

    private async Task WriteUnsafeAsync(
        IReadOnlyList<AutomationDelivery> deliveries,
        CancellationToken cancellationToken)
    {
        var temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    deliveries,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            catch (IOException)
            {
                // A stale temp file is harmless and can be removed by maintenance later.
            }
        }
    }

    private void QuarantineCorruptFile()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var quarantine = _path + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff") + ".json";
            File.Move(_path, quarantine, overwrite: false);
        }
        catch (IOException)
        {
            // Fail closed with an empty outbox if quarantine races another process.
        }
    }
}
