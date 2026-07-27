/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/AppCommandRegistry.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns AppCommandRegistry. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.Concurrent;
using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>
/// Represents app command registry and keeps its related state and behavior together.
/// </summary>
public sealed class AppCommandRegistry : IAppCommandRegistry
{
    private readonly ConcurrentDictionary<string, (string Key, string Label, string Description, Action Execute)> _commands = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Performs the register step owned by this component.
    /// </summary>
    public void Register(string key, string label, string description, Action execute)
    {
        _commands[key] = (key, label, description, execute);
    }

    public IReadOnlyList<(string Key, string Label, string Description)> GetAll()
    {
        return _commands.Values.Select(c => (c.Key, c.Label, c.Description)).ToArray();
    }

    /// <summary>
    /// Runs execute while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    public void Execute(string key)
    {
        if (_commands.TryGetValue(key, out var cmd))
            cmd.Execute();
    }

    public IReadOnlyList<(string Key, string Label, string Description)> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return GetAll();
        return _commands.Values
            .Where(c => c.Key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        c.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        c.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(c => (c.Key, c.Label, c.Description))
            .ToArray();
    }
}
