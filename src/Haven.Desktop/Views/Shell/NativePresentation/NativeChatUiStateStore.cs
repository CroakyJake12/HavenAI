using System.Text.Json;

namespace Haven.Desktop.Views.Shell.NativePresentation;

/// <summary>
/// Persists sidebar-only state without adding presentation flags to conversation content.
/// The file contains identifiers, read timestamps, group expansion, and group pinning only.
/// </summary>
internal sealed class NativeChatUiStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;
    private Dictionary<Guid, NativeChatItemState>? _states;

    public NativeChatUiStateStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Haven",
            "native-chat-sidebar.json");
    }

    public async Task<IReadOnlyDictionary<Guid, NativeChatItemState>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        return new Dictionary<Guid, NativeChatItemState>(_states!);
    }

    public Task SetExpandedAsync(Guid id, bool isExpanded, CancellationToken cancellationToken = default) =>
        UpdateAsync(id, state => state with { IsExpanded = isExpanded }, cancellationToken);

    public Task SetPinnedAsync(Guid id, bool isPinned, CancellationToken cancellationToken = default) =>
        UpdateAsync(id, state => state with { IsPinned = isPinned }, cancellationToken);

    public Task MarkReadAsync(
        Guid id,
        DateTimeOffset readAt,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(id, state => state with { LastReadAt = readAt, ForceUnread = false }, cancellationToken);

    public Task MarkUnreadAsync(Guid id, CancellationToken cancellationToken = default) =>
        UpdateAsync(id, state => state with { ForceUnread = true }, cancellationToken);

    private async Task UpdateAsync(
        Guid id,
        Func<NativeChatItemState, NativeChatItemState> update,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            var current = _states!.TryGetValue(id, out var state)
                ? state
                : NativeChatItemState.Empty;
            _states[id] = update(current);
            await SaveCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_states is not null)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (_states is not null)
        {
            return;
        }

        if (!File.Exists(_path))
        {
            _states = [];
            return;
        }

        try
        {
            await using var stream = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            _states = await JsonSerializer.DeserializeAsync<Dictionary<Guid, NativeChatItemState>>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false) ?? [];
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            _states = [];
        }
    }

    private async Task SaveCoreAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("The Chat sidebar state path has no directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = _path + ".tmp";
        await using (var stream = new FileStream(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                _states,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporaryPath, _path, true);
    }
}

internal sealed record NativeChatItemState(
    bool ForceUnread,
    DateTimeOffset? LastReadAt,
    bool IsPinned,
    bool IsExpanded)
{
    public static NativeChatItemState Empty { get; } = new(false, null, false, false);

    public bool IsUnread(DateTimeOffset updatedAt) =>
        ForceUnread || LastReadAt is not null && updatedAt > LastReadAt.Value;
}
