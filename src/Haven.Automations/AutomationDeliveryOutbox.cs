/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Automations/AutomationDeliveryOutbox.cs, in the Automations layer, which parses schedules and runs durable background actions.
 * What: This file owns AutomationDeliveryOutbox. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

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
    /// <summary>
    /// Stores json options locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    /// <summary>
    /// Stores path locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _path;
    /// <summary>
    /// Stores lock path locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _lockPath;

    public AutomationDeliveryOutbox(IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        Directory.CreateDirectory(paths.DataDirectory);
        _path = Path.Combine(paths.DataDirectory, "automation-deliveries.json");
        _lockPath = Path.Combine(paths.DataDirectory, "automation-deliveries.lock");
    }

    /// <summary>
    /// Performs enqueue async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs drain async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<IReadOnlyList<AutomationDelivery>> DrainAsync(
        CancellationToken cancellationToken)
    {
        await using var processLock = await AcquireLockAsync(cancellationToken).ConfigureAwait(false);
        var existing = await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
        if (existing.Count == 0) return [];
        await WriteUnsafeAsync([], cancellationToken).ConfigureAwait(false);
        return existing.OrderBy(item => item.CreatedAt).ToArray();
    }

    /// <summary>
    /// Performs acquire lock async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs read unsafe async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs write unsafe async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs the quarantine corrupt file step owned by this component.
    /// </summary>
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
