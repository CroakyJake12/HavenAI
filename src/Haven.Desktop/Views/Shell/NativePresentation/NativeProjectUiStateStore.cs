using System.Text.Json;

namespace Haven.Desktop.Views.Shell.NativePresentation;

/// <summary>
/// Persists presentation-only project state without mutating project content.
/// The store is local, atomic, and contains only project identifiers and UI
/// timestamps; it never stores prompts, file contents, or model output.
/// </summary>
internal sealed class NativeProjectUiStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;
    private Dictionary<Guid, ProjectUiState>? _states;

    public NativeProjectUiStateStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Haven",
            "native-project-ui.json");
    }

    public async Task<ProjectUiState> GetAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty)
        {
            return ProjectUiState.Empty;
        }

        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        return _states!.TryGetValue(projectId, out var state) ? state : ProjectUiState.Empty;
    }

    public async Task<IReadOnlyDictionary<Guid, ProjectUiState>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        return new Dictionary<Guid, ProjectUiState>(_states!);
    }

    public async Task SetPinnedAsync(
        Guid projectId,
        bool isPinned,
        CancellationToken cancellationToken = default)
    {
        await UpdateAsync(
            projectId,
            state => state with { IsPinned = isPinned },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkReadAsync(
        Guid projectId,
        DateTimeOffset readAt,
        CancellationToken cancellationToken = default)
    {
        await UpdateAsync(
            projectId,
            state => state with { LastReadAt = readAt, ForceUnread = false },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkUnreadAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await UpdateAsync(
            projectId,
            state => state with { ForceUnread = true },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task UpdateAsync(
        Guid projectId,
        Func<ProjectUiState, ProjectUiState> update,
        CancellationToken cancellationToken)
    {
        if (projectId == Guid.Empty)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            var current = _states!.TryGetValue(projectId, out var state)
                ? state
                : ProjectUiState.Empty;
            _states[projectId] = update(current);
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
            _states = new Dictionary<Guid, ProjectUiState>();
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

            _states = await JsonSerializer.DeserializeAsync<Dictionary<Guid, ProjectUiState>>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false)
                ?? new Dictionary<Guid, ProjectUiState>();
        }
        catch (JsonException)
        {
            // A corrupt UI preference file must never block access to projects.
            _states = new Dictionary<Guid, ProjectUiState>();
        }
        catch (IOException)
        {
            _states = new Dictionary<Guid, ProjectUiState>();
        }
        catch (UnauthorizedAccessException)
        {
            _states = new Dictionary<Guid, ProjectUiState>();
        }
    }

    private async Task SaveCoreAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("The project UI state path has no directory.");

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

internal sealed record ProjectUiState(
    bool IsPinned,
    bool ForceUnread,
    DateTimeOffset? LastReadAt)
{
    public static ProjectUiState Empty { get; } = new(false, false, null);

    public bool IsUnread(DateTimeOffset updatedAt) =>
        ForceUnread || (LastReadAt is not null && updatedAt > LastReadAt.Value);
}
