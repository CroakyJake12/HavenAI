namespace Haven.Application;

public sealed class SpaceRegistry
{
    private const string SettingsKey = "spaces.registry";
    private const string CurrentSpaceSettingsKey = "spaces.current";
    private const int CurrentVersion = 1;
    private readonly IVersionedSettingsStore _settings;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public static Guid StudySpaceId { get; } = Guid.Parse("b1000000-0000-0000-0000-000000000001");
    public static Guid ShoppingSpaceId { get; } = Guid.Parse("b1000000-0000-0000-0000-000000000002");
    public static Guid ResearchSpaceId { get; } = Guid.Parse("b1000000-0000-0000-0000-000000000003");
    public static Guid AgentSpaceId { get; } = Guid.Parse("b1000000-0000-0000-0000-000000000004");

    public SpaceRegistry(IVersionedSettingsStore settings) : this(settings, () => DateTimeOffset.UtcNow)
    {
    }

    internal SpaceRegistry(IVersionedSettingsStore settings, Func<DateTimeOffset> clock)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<IReadOnlyList<SpaceDefinition>> GetAllAsync(bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadAndReconcileAsync(cancellationToken).ConfigureAwait(false);
            return state.Spaces
                .Where(space => includeArchived || !space.IsArchived)
                .OrderByDescending(space => space.IsBuiltIn)
                .ThenBy(space => space.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SpaceDefinition?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var spaces = await GetAllAsync(includeArchived: true, cancellationToken).ConfigureAwait(false);
        return spaces.FirstOrDefault(space => space.Id == id);
    }

    public async Task<SpaceDefinition> CreateAsync(string name, string? description = null, CancellationToken cancellationToken = default)
    {
        var normalizedName = NormalizeName(name);
        return await MutateAsync(state =>
        {
            EnsureUniqueName(state.Spaces, normalizedName);
            var now = _clock();
            var created = new SpaceDefinition(
                Guid.NewGuid(), normalizedName, description?.Trim() ?? string.Empty, "sparkles", SpaceKind.General,
                false, false, null, string.Empty, SpaceThinkingMode.Default, [], [], null, now, now);
            return (state with { Spaces = [.. state.Spaces, created] }, created);
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<SpaceDefinition> RenameAsync(Guid id, string name, CancellationToken cancellationToken = default)
    {
        var normalizedName = NormalizeName(name);
        return MutateSpaceAsync(id, (space, spaces) =>
        {
            EnsureUniqueName(spaces, normalizedName, id);
            return space with { Name = normalizedName, UpdatedAt = _clock() };
        }, cancellationToken);
    }

    public Task<SpaceDefinition> SetArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default) =>
        MutateSpaceAsync(id, (space, _) => space with { IsArchived = archived, UpdatedAt = _clock() }, cancellationToken);

    public Task<SpaceDefinition> UpdateAsync(SpaceDefinition updated, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updated);
        return MutateSpaceAsync(updated.Id, (existing, spaces) =>
        {
            var name = NormalizeName(updated.Name);
            EnsureUniqueName(spaces, name, updated.Id);
            return updated with
            {
                Name = name,
                IsBuiltIn = existing.IsBuiltIn,
                Kind = existing.IsBuiltIn ? existing.Kind : updated.Kind,
                CreatedAt = existing.CreatedAt,
                UpdatedAt = _clock(),
                ExamplePairs = updated.ExamplePairs ?? [],
                Files = updated.Files ?? []
            };
        }, cancellationToken);
    }

    /// <summary>Returns the Space the shell is currently scoped to, or null for unscoped Chat.</summary>
    public async Task<Guid?> GetCurrentSpaceIdAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var value = await _settings.GetAsync<string>(CurrentSpaceSettingsKey, cancellationToken).ConfigureAwait(false);
            return Guid.TryParse(value, out var id) ? id : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Persists the Space the shell is scoped to; null returns to unscoped Chat.</summary>
    public async Task SetCurrentSpaceIdAsync(Guid? spaceId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (spaceId is { } id) await _settings.SetAsync(CurrentSpaceSettingsKey, id.ToString(), cancellationToken).ConfigureAwait(false);
            else await _settings.RemoveAsync(CurrentSpaceSettingsKey, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SpaceDefinition> ForkAsync(Guid id, string? name = null, CancellationToken cancellationToken = default)
    {
        return await MutateAsync(state =>
        {
            var source = FindRequired(state.Spaces, id);
            var requested = string.IsNullOrWhiteSpace(name) ? $"{source.Name} copy" : name!;
            var forkName = MakeUniqueName(state.Spaces, NormalizeName(requested));
            var now = _clock();
            var fork = source with
            {
                Id = Guid.NewGuid(),
                Name = forkName,
                IsBuiltIn = false,
                IsArchived = false,
                ForkedFromSpaceId = source.Id,
                Files = source.Files.ToArray(),
                ExamplePairs = source.ExamplePairs.ToArray(),
                LayoutDocument = CloneLayout(source.LayoutDocument),
                CreatedAt = now,
                UpdatedAt = now
            };
            return (state with { Spaces = [.. state.Spaces, fork] }, fork);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await MutateAsync(state =>
        {
            var existing = FindRequired(state.Spaces, id);
            if (existing.IsBuiltIn)
                throw new InvalidOperationException("Built-in Spaces cannot be deleted. Fork one if you need an independent version.");
            return (state with { Spaces = state.Spaces.Where(space => space.Id != id).ToArray() }, true);
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<SpaceDefinition> SetLayoutAsync(Guid id, SpaceLayoutDocument? layout, CancellationToken cancellationToken = default)
    {
        return MutateSpaceAsync(id, (space, _) => space with
        {
            LayoutDocument = CloneLayout(layout),
            UpdatedAt = _clock()
        }, cancellationToken);
    }

    public Task<SpaceDefinition> AddFileAsync(
        Guid id,
        string path,
        SpaceFilePermission permission = SpaceFilePermission.ReadOnly,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A file path is required.", nameof(path));
        var fullPath = Path.GetFullPath(path);
        return MutateSpaceAsync(id, (space, _) =>
        {
            var files = space.Files
                .Where(file => !file.Path.Equals(fullPath, StringComparison.OrdinalIgnoreCase))
                .Append(new SpaceFileReference(fullPath, Path.GetFileName(fullPath), permission, _clock()))
                .ToArray();
            return space with { Files = files, UpdatedAt = _clock() };
        }, cancellationToken);
    }

    public Task<SpaceDefinition> RemoveFileAsync(Guid id, string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A file path is required.", nameof(path));
        var fullPath = Path.GetFullPath(path);
        return MutateSpaceAsync(id, (space, _) => space with
        {
            Files = space.Files.Where(file => !file.Path.Equals(fullPath, StringComparison.OrdinalIgnoreCase)).ToArray(),
            UpdatedAt = _clock()
        }, cancellationToken);
    }

    private async Task<SpaceDefinition> MutateSpaceAsync(
        Guid id,
        Func<SpaceDefinition, IReadOnlyList<SpaceDefinition>, SpaceDefinition> update,
        CancellationToken cancellationToken)
    {
        return await MutateAsync(state =>
        {
            var existing = FindRequired(state.Spaces, id);
            var changed = update(existing, state.Spaces);
            var spaces = state.Spaces.Select(space => space.Id == id ? changed : space).ToArray();
            return (state with { Spaces = spaces }, changed);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TResult> MutateAsync<TResult>(
        Func<SpaceRegistryState, (SpaceRegistryState State, TResult Result)> mutation,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadAndReconcileAsync(cancellationToken).ConfigureAwait(false);
            var (next, result) = mutation(state);
            await _settings.SetAsync(SettingsKey, next, cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SpaceRegistryState> LoadAndReconcileAsync(CancellationToken cancellationToken)
    {
        var state = await _settings.GetAsync<SpaceRegistryState>(SettingsKey, cancellationToken).ConfigureAwait(false)
            ?? new SpaceRegistryState(CurrentVersion, []);
        var spaces = state.Spaces?.ToList() ?? [];
        var changed = state.Version != CurrentVersion;
        foreach (var builtIn in BuiltIns())
        {
            if (spaces.Any(space => space.Id == builtIn.Id)) continue;
            spaces.Add(builtIn);
            changed = true;
        }
        if (!changed) return state with { Spaces = spaces };
        var reconciled = new SpaceRegistryState(CurrentVersion, spaces);
        await _settings.SetAsync(SettingsKey, reconciled, cancellationToken).ConfigureAwait(false);
        return reconciled;
    }

    private static SpaceLayoutDocument? CloneLayout(SpaceLayoutDocument? layout)
    {
        if (layout is null) return null;
        var nodes = layout.Nodes.Select(node => node with
        {
            Ports = node.Ports.ToArray(),
            Metadata = new Dictionary<string, string>(node.Metadata, StringComparer.Ordinal)
        }).ToArray();
        var edges = layout.Edges.Select(edge => edge with
        {
            Metadata = new Dictionary<string, string>(edge.Metadata, StringComparer.Ordinal)
        }).ToArray();
        return new SpaceLayoutDocument(nodes, edges);
    }

    private IReadOnlyList<SpaceDefinition> BuiltIns()
    {
        var epoch = DateTimeOffset.UnixEpoch;
        return
        [
            new(StudySpaceId, "Study", "Organise subjects, revision material and study workflows.", "book", SpaceKind.Study, true, false, null, "Use the Study product for subject, topic, progress and assessment work.", SpaceThinkingMode.Balanced, [], [], null, epoch, epoch),
            new(ShoppingSpaceId, "Shopping", "Compare products, research options and keep buying context together.", "cart", SpaceKind.Shopping, true, false, null, "Help compare options and preserve the user's requirements and trade-offs.", SpaceThinkingMode.Balanced, [], [], null, epoch, epoch),
            new(ResearchSpaceId, "Research", "Collect sources, files and deeper investigation in one reusable workspace.", "search", SpaceKind.Research, true, false, null, "Separate sourced facts, inference and unresolved questions.", SpaceThinkingMode.Deep, [], [], null, epoch, epoch),
            new(AgentSpaceId, "Agent", "Run long-horizon agentic work that plans, executes tools, verifies results and hands off cleanly.", "agents", SpaceKind.Agent, true, false, null, "Plan before acting, then execute one step at a time. Use Tasks or Studio workspaces for file and command work. Keep the Action Graph as the audit trail of every tool action. Checkpoint state before any mutation and confirm risky or destructive changes. Prefer steering or queueing follow-up work over abandoning long runs. Finish with a handoff summary of what was done, what remains and where results live.", SpaceThinkingMode.Deep, [], [], null, epoch, epoch)
        ];
    }

    private static SpaceDefinition FindRequired(IReadOnlyList<SpaceDefinition> spaces, Guid id) =>
        spaces.FirstOrDefault(space => space.Id == id)
        ?? throw new KeyNotFoundException($"Space '{id}' was not found.");

    private static string NormalizeName(string name)
    {
        var value = name?.Trim() ?? string.Empty;
        if (value.Length == 0) throw new ArgumentException("A Space name is required.", nameof(name));
        if (value.Length > 80) throw new ArgumentException("Space names can be at most 80 characters.", nameof(name));
        return value;
    }

    private static void EnsureUniqueName(IReadOnlyList<SpaceDefinition> spaces, string name, Guid? exceptId = null)
    {
        if (spaces.Any(space => space.Id != exceptId && space.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A Space named '{name}' already exists.");
    }

    private static string MakeUniqueName(IReadOnlyList<SpaceDefinition> spaces, string desired)
    {
        if (!spaces.Any(space => space.Name.Equals(desired, StringComparison.OrdinalIgnoreCase))) return desired;
        for (var suffix = 2; suffix < 1000; suffix++)
        {
            var candidate = $"{desired} {suffix}";
            if (!spaces.Any(space => space.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase))) return candidate;
        }
        throw new InvalidOperationException("Could not create a unique Space name.");
    }
}
