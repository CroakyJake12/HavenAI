using System.Text.Json;
using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>Persists semantic Mail drafts only. OAuth/access credentials never cross this boundary.</summary>
public sealed class FileMailDraftStore(IAppPaths paths) : IMailDraftStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    private readonly string _directory = Path.Combine(paths.DataDirectory, "MailDrafts");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyList<MailDraft>> GetAllAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(_directory)) return [];
            var drafts = new List<MailDraft>();
            foreach (var file in Directory.EnumerateFiles(_directory, "*.json", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var draft = await ReadCoreAsync(file, cancellationToken).ConfigureAwait(false);
                if (draft is not null) drafts.Add(draft);
            }
            return drafts.OrderByDescending(item => item.UpdatedAt ?? DateTimeOffset.MinValue).ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task<MailDraft?> GetAsync(Guid localDraftId, CancellationToken cancellationToken)
    {
        if (localDraftId == Guid.Empty) return null;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await ReadCoreAsync(PathFor(localDraftId), cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async Task UpsertAsync(MailDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.LocalId == Guid.Empty) throw new ArgumentException("A durable Mail draft requires a local identity.", nameof(draft));
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_directory);
        var path = PathFor(draft.LocalId);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 32 * 1024, useAsync: true))
                {
                    await JsonSerializer.SerializeAsync(stream, draft, JsonOptions, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                File.Move(temporary, path, overwrite: true);
            }
            finally { _gate.Release(); }
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public async Task DeleteAsync(Guid localDraftId, CancellationToken cancellationToken)
    {
        if (localDraftId == Guid.Empty) return;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = PathFor(localDraftId);
            if (File.Exists(path)) File.Delete(path);
        }
        finally { _gate.Release(); }
    }

    private async Task<MailDraft?> ReadCoreAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 32 * 1024, useAsync: true);
            return await JsonSerializer.DeserializeAsync<MailDraft>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("A persisted Mail draft is unreadable.", ex);
        }
    }

    private string PathFor(Guid localDraftId) => Path.Combine(_directory, localDraftId.ToString("N") + ".json");
}
