using System.Collections.Concurrent;
using Haven.Application;

namespace Haven.Infrastructure;

public sealed class AppCommandRegistry : IAppCommandRegistry
{
    private readonly ConcurrentDictionary<string, (string Key, string Label, string Description, Action Execute)> _commands = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string key, string label, string description, Action execute)
    {
        _commands[key] = (key, label, description, execute);
    }

    public IReadOnlyList<(string Key, string Label, string Description)> GetAll()
    {
        return _commands.Values.Select(c => (c.Key, c.Label, c.Description)).ToArray();
    }

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
