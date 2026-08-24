namespace Haven.Application;

public enum SpaceLayoutPortDirection
{
    Input = 0,
    Output = 1
}

public sealed record SpaceLayoutPort(
    string Id,
    string Label,
    SpaceLayoutPortDirection Direction,
    string DataType = "flow",
    bool AllowsMultipleConnections = true);

public sealed record SpaceLayoutNode(Guid Id, string Category, string Title)
{
    public string Subtitle { get; init; } = string.Empty;
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; } = 220;
    public double Height { get; init; } = 118;
    public IReadOnlyList<SpaceLayoutPort> Ports { get; init; } = [];
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

public sealed record SpaceLayoutEdge(Guid Id, Guid FromNodeId, string FromPortId, Guid ToNodeId, string ToPortId)
{
    public string Label { get; init; } = string.Empty;
    public string Branch { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

public sealed record SpaceLayoutDocument(IReadOnlyList<SpaceLayoutNode> Nodes, IReadOnlyList<SpaceLayoutEdge> Edges)
{
    public static SpaceLayoutDocument Empty { get; } = new([], []);
}
