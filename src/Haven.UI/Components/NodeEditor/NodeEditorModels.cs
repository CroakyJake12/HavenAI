namespace Haven.UI.Components;

public enum NodeEditorPortDirection
{
    Input = 0,
    Output = 1
}

public sealed record NodeEditorPort(
    string Id,
    string Label,
    NodeEditorPortDirection Direction,
    string DataType = "flow",
    bool AllowsMultipleConnections = true);

public sealed record NodeEditorNode(Guid Id, string Category, string Title)
{
    public string Subtitle { get; init; } = string.Empty;
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; } = 220;
    public double Height { get; init; } = 118;
    public IReadOnlyList<NodeEditorPort> Ports { get; init; } = [];
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

public sealed record NodeEditorEdge(Guid Id, Guid FromNodeId, string FromPortId, Guid ToNodeId, string ToPortId)
{
    public string Label { get; init; } = string.Empty;
    public string Branch { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

public sealed record NodeEditorDocument(IReadOnlyList<NodeEditorNode> Nodes, IReadOnlyList<NodeEditorEdge> Edges)
{
    public static NodeEditorDocument Empty { get; } = new([], []);
}

public sealed record NodeEditorTemplate(
    string Category,
    string Title,
    string Subtitle,
    IReadOnlyList<NodeEditorPort> Ports,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record NodeEditorDiagnostic(string Code, string Message, Guid? NodeId = null, Guid? EdgeId = null);

internal sealed record NodeEditorClipboardPayload(IReadOnlyList<NodeEditorNode> Nodes, IReadOnlyList<NodeEditorEdge> Edges);
