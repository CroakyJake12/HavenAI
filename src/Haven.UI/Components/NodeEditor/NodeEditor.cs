using System.Text.Json;

namespace Haven.UI.Components;

/// <summary>Canonical retained-mode graph surface shared by Haven features.</summary>
public sealed class NodeEditor : HavenElement, IHavenDrawCommandSource, IHavenPointerInputTarget, IHavenScrollInputTarget, IHavenKeyboardInputTarget, IHavenClipboardInputTarget
{
    private const double MinZoom = 0.2;
    private const double MaxZoom = 3.0;
    private const double GridSpacing = 32;
    private const double PortRadius = 5.5;
    private const double MinimapWidth = 172;
    private const double MinimapHeight = 104;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HashSet<Guid> _selected = [];
    private readonly HashSet<Guid> _selectedEdges = [];
    private readonly Stack<NodeEditorDocument> _undo = [];
    private readonly Stack<NodeEditorDocument> _redo = [];
    private NodeEditorDocument _document = NodeEditorDocument.Empty;
    private NodeEditorGesture _gesture;
    private HavenPoint _lastPointer;
    private HavenPoint _marqueeStart;
    private HavenPoint _marqueeEnd;
    private bool _marqueeAdditive;
    private Guid? _connectingNodeId;
    private string? _connectingPortId;
    private NodeEditorDocument? _gestureStartDocument;
    private IReadOnlyList<NodeEditorDiagnostic> _diagnostics = [];

    public NodeEditor()
    {
        Accessibility.Role = HavenAccessibleRole.Group;
        Accessibility.AccessibleName = "Node editor";
        Accessibility.Description = "Pan, zoom, select, connect and arrange workflow nodes.";
        Accessibility.Focusable = true;
        SetValue(HavenProperties.Background, "Transparent");
        SetValue(HavenProperties.Clip, true);
    }

    public NodeEditorDocument Document
    {
        get => _document;
        set
        {
            _document = value ?? NodeEditorDocument.Empty;
            TrimSelection();
            _undo.Clear();
            _redo.Clear();
            Invalidate();
        }
    }

    public IReadOnlyCollection<Guid> SelectedNodeIds => _selected;
    public IReadOnlyCollection<Guid> SelectedEdgeIds => _selectedEdges;
    public IReadOnlyList<NodeEditorDiagnostic> Diagnostics
    {
        get => _diagnostics;
        set { _diagnostics = value?.ToArray() ?? []; Invalidate(); }
    }
    public double PanX { get; private set; }
    public double PanY { get; private set; }
    public double Zoom { get; private set; } = 1;
    public int RealizedNodeCount { get; private set; }
    public int RealizedEdgeCount { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public event Action<NodeEditorDocument>? DocumentChanged;
    public event Action<IReadOnlyCollection<Guid>>? SelectionChanged;
    public event Action<HavenPoint>? EmptySpaceContextRequested;

    public void PanBy(double deltaX, double deltaY) { PanX += deltaX; PanY += deltaY; Invalidate(); }

    public void ZoomAt(double factor, HavenPoint localPoint)
    {
        if (!double.IsFinite(factor) || factor <= 0) return;
        var before = ScreenToWorld(localPoint);
        Zoom = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);
        PanX = localPoint.X - before.X * Zoom;
        PanY = localPoint.Y - before.Y * Zoom;
        Invalidate();
    }

    public void ResetViewport() { PanX = 0; PanY = 0; Zoom = 1; Invalidate(); }

    public HavenPoint ViewportCenterWorld => ScreenToWorld(new HavenPoint(Bounds.Width / 2, Bounds.Height / 2));

    public bool FitToDocument(double padding = 48)
    {
        if (_document.Nodes.Count == 0 || Bounds.Width <= 1 || Bounds.Height <= 1) return false;
        var minX = _document.Nodes.Min(node => node.X);
        var minY = _document.Nodes.Min(node => node.Y);
        var maxX = _document.Nodes.Max(node => node.X + Math.Max(40, node.Width));
        var maxY = _document.Nodes.Max(node => node.Y + Math.Max(32, node.Height));
        var worldWidth = Math.Max(1, maxX - minX);
        var worldHeight = Math.Max(1, maxY - minY);
        var availableWidth = Math.Max(1, Bounds.Width - Math.Max(0, padding) * 2);
        var availableHeight = Math.Max(1, Bounds.Height - Math.Max(0, padding) * 2);
        Zoom = Math.Clamp(Math.Min(availableWidth / worldWidth, availableHeight / worldHeight), MinZoom, MaxZoom);
        PanX = (Bounds.Width - worldWidth * Zoom) / 2 - minX * Zoom;
        PanY = (Bounds.Height - worldHeight * Zoom) / 2 - minY * Zoom;
        Invalidate();
        return true;
    }

    public void SelectNode(Guid nodeId, bool additive = false)
    {
        if (!_document.Nodes.Any(node => node.Id == nodeId)) return;
        if (!additive) { _selected.Clear(); _selectedEdges.Clear(); }
        if (additive && _selected.Contains(nodeId)) _selected.Remove(nodeId); else _selected.Add(nodeId);
        RaiseSelectionChanged();
    }

    public void SelectEdge(Guid edgeId, bool additive = false)
    {
        if (!_document.Edges.Any(edge => edge.Id == edgeId)) return;
        if (!additive) { _selected.Clear(); _selectedEdges.Clear(); }
        if (additive && _selectedEdges.Contains(edgeId)) _selectedEdges.Remove(edgeId); else _selectedEdges.Add(edgeId);
        RaiseSelectionChanged();
    }

    public void SelectAll()
    {
        _selected.Clear();
        _selectedEdges.Clear();
        foreach (var node in _document.Nodes) _selected.Add(node.Id);
        RaiseSelectionChanged();
    }

    public void ClearSelection()
    {
        if (_selected.Count == 0 && _selectedEdges.Count == 0) return;
        _selected.Clear();
        _selectedEdges.Clear();
        RaiseSelectionChanged();
    }

    public void SelectNodesInWorldRect(HavenRect worldRect, bool additive = false)
    {
        if (!additive) { _selected.Clear(); _selectedEdges.Clear(); }
        foreach (var node in _document.Nodes) if (Intersects(NodeWorldRect(node), worldRect)) _selected.Add(node.Id);
        RaiseSelectionChanged();
    }

    public IReadOnlyList<NodeEditorTemplate> SearchTemplates(IEnumerable<NodeEditorTemplate> templates, string? query)
    {
        var value = query?.Trim();
        if (string.IsNullOrEmpty(value)) return templates.ToArray();
        return templates.Where(template => template.Title.Contains(value, StringComparison.OrdinalIgnoreCase) || template.Category.Contains(value, StringComparison.OrdinalIgnoreCase) || template.Subtitle.Contains(value, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    public Guid AddNode(NodeEditorTemplate template, double x, double y)
    {
        var node = new NodeEditorNode(Guid.NewGuid(), template.Category, template.Title)
        {
            Subtitle = template.Subtitle, X = x, Y = y, Ports = template.Ports.ToArray(),
            Metadata = template.Metadata is null ? new Dictionary<string, string>(StringComparer.Ordinal) : new Dictionary<string, string>(template.Metadata, StringComparer.Ordinal)
        };
        SetDocument(new NodeEditorDocument([.. _document.Nodes, node], _document.Edges), true);
        _selected.Clear(); _selectedEdges.Clear(); _selected.Add(node.Id); RaiseSelectionChanged();
        return node.Id;
    }

    public bool UpdateNode(Guid nodeId, Func<NodeEditorNode, NodeEditorNode> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var index = -1;
        for (var candidate = 0; candidate < _document.Nodes.Count; candidate++)
        {
            if (_document.Nodes[candidate].Id != nodeId) continue;
            index = candidate;
            break;
        }
        if (index < 0) return false;
        var current = _document.Nodes[index];
        var updated = update(current) ?? current;
        if (updated.Id != nodeId) updated = updated with { Id = nodeId };
        if (Equals(current, updated)) return false;
        var nodes = _document.Nodes.ToArray();
        nodes[index] = updated;
        SetDocument(new NodeEditorDocument(nodes, _document.Edges), true);
        return true;
    }

    public void MoveSelectionBy(double deltaX, double deltaY)
    {
        if (_selected.Count == 0 || (Math.Abs(deltaX) < .001 && Math.Abs(deltaY) < .001)) return;
        var nodes = _document.Nodes.Select(node => _selected.Contains(node.Id) ? node with { X = node.X + deltaX, Y = node.Y + deltaY } : node).ToArray();
        SetDocument(new NodeEditorDocument(nodes, _document.Edges), true);
    }

    public bool Connect(Guid fromNodeId, string fromPortId, Guid toNodeId, string toPortId)
    {
        if (!CanConnect(fromNodeId, fromPortId, toNodeId, toPortId)) return false;
        SetDocument(new NodeEditorDocument(_document.Nodes, [.. _document.Edges, new NodeEditorEdge(Guid.NewGuid(), fromNodeId, fromPortId, toNodeId, toPortId)]), true);
        return true;
    }

    public bool Disconnect(Guid edgeId)
    {
        if (!_document.Edges.Any(edge => edge.Id == edgeId)) return false;
        SetDocument(new NodeEditorDocument(_document.Nodes, _document.Edges.Where(edge => edge.Id != edgeId).ToArray()), true);
        return true;
    }

    public bool CanConnect(Guid fromNodeId, string fromPortId, Guid toNodeId, string toPortId)
    {
        if (fromNodeId == toNodeId) return false;
        var from = FindNode(fromNodeId); var to = FindNode(toNodeId);
        if (from is null || to is null) return false;
        var fromPort = FindPort(from, fromPortId); var toPort = FindPort(to, toPortId);
        if (fromPort is null || toPort is null || fromPort.Direction != NodeEditorPortDirection.Output || toPort.Direction != NodeEditorPortDirection.Input) return false;
        if (!string.Equals(fromPort.DataType, toPort.DataType, StringComparison.OrdinalIgnoreCase)) return false;
        if (_document.Edges.Any(edge => edge.FromNodeId == fromNodeId && edge.FromPortId == fromPortId && edge.ToNodeId == toNodeId && edge.ToPortId == toPortId)) return false;
        if (!fromPort.AllowsMultipleConnections && _document.Edges.Any(edge => edge.FromNodeId == fromNodeId && edge.FromPortId == fromPortId)) return false;
        if (!toPort.AllowsMultipleConnections && _document.Edges.Any(edge => edge.ToNodeId == toNodeId && edge.ToPortId == toPortId)) return false;
        return !WouldCreateCycle(fromNodeId, toNodeId);
    }

    public string? CopySelection()
    {
        if (_selected.Count == 0) return null;
        var nodes = _document.Nodes.Where(node => _selected.Contains(node.Id)).ToArray();
        var ids = nodes.Select(node => node.Id).ToHashSet();
        var edges = _document.Edges.Where(edge => ids.Contains(edge.FromNodeId) && ids.Contains(edge.ToNodeId)).ToArray();
        return JsonSerializer.Serialize(new NodeEditorClipboardPayload(nodes, edges), JsonOptions);
    }

    public IReadOnlyList<Guid> PasteSelection(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        NodeEditorClipboardPayload? payload;
        try { payload = JsonSerializer.Deserialize<NodeEditorClipboardPayload>(text, JsonOptions); } catch (JsonException) { return []; }
        if (payload is null || payload.Nodes.Count == 0) return [];
        var idMap = payload.Nodes.ToDictionary(node => node.Id, _ => Guid.NewGuid());
        var nodes = payload.Nodes.Select(node => node with { Id = idMap[node.Id], X = node.X + 28, Y = node.Y + 28 }).ToArray();
        var edges = payload.Edges.Where(edge => idMap.ContainsKey(edge.FromNodeId) && idMap.ContainsKey(edge.ToNodeId)).Select(edge => edge with { Id = Guid.NewGuid(), FromNodeId = idMap[edge.FromNodeId], ToNodeId = idMap[edge.ToNodeId] }).ToArray();
        SetDocument(new NodeEditorDocument([.. _document.Nodes, .. nodes], [.. _document.Edges, .. edges]), true);
        _selected.Clear(); foreach (var node in nodes) _selected.Add(node.Id); RaiseSelectionChanged();
        return nodes.Select(node => node.Id).ToArray();
    }

    public IReadOnlyList<Guid> DuplicateSelection() => PasteSelection(CopySelection());

    public void DeleteSelection()
    {
        if (_selected.Count == 0 && _selectedEdges.Count == 0) return;
        var removedNodes = _selected.ToHashSet();
        var removedEdges = _selectedEdges.ToHashSet();
        SetDocument(new NodeEditorDocument(
            _document.Nodes.Where(node => !removedNodes.Contains(node.Id)).ToArray(),
            _document.Edges.Where(edge => !removedEdges.Contains(edge.Id) && !removedNodes.Contains(edge.FromNodeId) && !removedNodes.Contains(edge.ToNodeId)).ToArray()), true);
        _selected.Clear(); _selectedEdges.Clear(); RaiseSelectionChanged();
    }

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        _redo.Push(_document); _document = _undo.Pop(); TrimSelection(); DocumentChanged?.Invoke(_document); RaiseSelectionChanged(); Invalidate(); return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        _undo.Push(_document); _document = _redo.Pop(); TrimSelection(); DocumentChanged?.Invoke(_document); RaiseSelectionChanged(); Invalidate(); return true;
    }

    public IReadOnlyList<NodeEditorDiagnostic> ValidateDocument()
    {
        var diagnostics = new List<NodeEditorDiagnostic>();
        foreach (var group in _document.Nodes.GroupBy(node => node.Id).Where(group => group.Count() > 1)) diagnostics.Add(new NodeEditorDiagnostic("duplicate-node-id", "Node IDs must be unique.", group.Key));
        foreach (var node in _document.Nodes) foreach (var group in node.Ports.GroupBy(port => port.Id, StringComparer.Ordinal).Where(group => group.Count() > 1)) diagnostics.Add(new NodeEditorDiagnostic("duplicate-port-id", $"Port '{group.Key}' is duplicated.", node.Id));
        foreach (var group in _document.Edges.GroupBy(edge => edge.Id).Where(group => group.Count() > 1)) diagnostics.Add(new NodeEditorDiagnostic("duplicate-edge-id", "Edge IDs must be unique.", EdgeId: group.Key));
        foreach (var edge in _document.Edges)
        {
            var from = FindNode(edge.FromNodeId); var to = FindNode(edge.ToNodeId);
            if (from is null || to is null) { diagnostics.Add(new NodeEditorDiagnostic("missing-node", "Edge references a missing node.", EdgeId: edge.Id)); continue; }
            var fromPort = FindPort(from, edge.FromPortId); var toPort = FindPort(to, edge.ToPortId);
            if (fromPort is null || toPort is null) { diagnostics.Add(new NodeEditorDiagnostic("missing-port", "Edge references a missing port.", EdgeId: edge.Id)); continue; }
            if (fromPort.Direction != NodeEditorPortDirection.Output || toPort.Direction != NodeEditorPortDirection.Input) diagnostics.Add(new NodeEditorDiagnostic("port-direction", "Edges must connect output ports to input ports.", EdgeId: edge.Id));
            if (!string.Equals(fromPort.DataType, toPort.DataType, StringComparison.OrdinalIgnoreCase)) diagnostics.Add(new NodeEditorDiagnostic("port-type", "Connected ports must have matching data types.", EdgeId: edge.Id));
        }
        if (HasCycle()) diagnostics.Add(new NodeEditorDiagnostic("cycle", "Graph contains a cycle."));
        return diagnostics;
    }

    public bool PointerPressed(HavenPointerInput input)
    {
        _lastPointer = input.LocalPosition; _gestureStartDocument = null;
        if (input.Button == HavenPointerButton.Middle) { _gesture = NodeEditorGesture.Pan; return true; }
        if (input.Button == HavenPointerButton.Secondary)
        {
            if (HitNode(input.LocalPosition) is null && HitEdge(input.LocalPosition) is null && !TryHitPort(input.LocalPosition, out _, out _))
            {
                _gesture = NodeEditorGesture.None;
                EmptySpaceContextRequested?.Invoke(ScreenToWorld(input.LocalPosition));
                return true;
            }
            return false;
        }
        if (TryHitPort(input.LocalPosition, out var portNode, out var port) && port.Direction == NodeEditorPortDirection.Output) { _connectingNodeId = portNode.Id; _connectingPortId = port.Id; _gesture = NodeEditorGesture.Connect; Invalidate(); return true; }
        var additive = input.Modifiers.HasFlag(HavenKeyModifiers.Shift) || input.Modifiers.HasFlag(HavenKeyModifiers.Control) || input.Modifiers.HasFlag(HavenKeyModifiers.Meta);
        if (HitNode(input.LocalPosition) is { } node)
        {
            if (!_selected.Contains(node.Id) || additive) SelectNode(node.Id, additive);
            _gesture = NodeEditorGesture.Move; _gestureStartDocument = _document; return true;
        }
        if (HitEdge(input.LocalPosition) is { } edge)
        {
            SelectEdge(edge.Id, additive); _gesture = NodeEditorGesture.None; return true;
        }
        _marqueeStart = ScreenToWorld(input.LocalPosition); _marqueeEnd = _marqueeStart; _marqueeAdditive = input.Modifiers.HasFlag(HavenKeyModifiers.Shift); _gesture = NodeEditorGesture.Marquee;
        if (!_marqueeAdditive) ClearSelection(); Invalidate(); return true;
    }

    public bool PointerMoved(HavenPointerInput input)
    {
        var dx = input.LocalPosition.X - _lastPointer.X; var dy = input.LocalPosition.Y - _lastPointer.Y;
        switch (_gesture)
        {
            case NodeEditorGesture.Pan: PanX += dx; PanY += dy; break;
            case NodeEditorGesture.Move when _selected.Count > 0:
                _document = new NodeEditorDocument(_document.Nodes.Select(node => _selected.Contains(node.Id) ? node with { X = node.X + dx / Zoom, Y = node.Y + dy / Zoom } : node).ToArray(), _document.Edges); DocumentChanged?.Invoke(_document); break;
            case NodeEditorGesture.Marquee: _marqueeEnd = ScreenToWorld(input.LocalPosition); break;
            case NodeEditorGesture.Connect: break;
            default: return false;
        }
        _lastPointer = input.LocalPosition; Invalidate(); return true;
    }

    public bool PointerReleased(HavenPointerInput input)
    {
        switch (_gesture)
        {
            case NodeEditorGesture.Move when _gestureStartDocument is not null:
                if (!SameNodePositions(_gestureStartDocument, _document)) { _undo.Push(_gestureStartDocument); _redo.Clear(); } break;
            case NodeEditorGesture.Marquee:
                _marqueeEnd = ScreenToWorld(input.LocalPosition); SelectNodesInWorldRect(NormalizeRect(_marqueeStart, _marqueeEnd), _marqueeAdditive); break;
            case NodeEditorGesture.Connect when _connectingNodeId is { } fromId && _connectingPortId is { } fromPort:
                if (TryHitPort(input.LocalPosition, out var targetNode, out var targetPort) && targetPort.Direction == NodeEditorPortDirection.Input) Connect(fromId, fromPort, targetNode.Id, targetPort.Id); break;
        }
        _gesture = NodeEditorGesture.None; _gestureStartDocument = null; _connectingNodeId = null; _connectingPortId = null; Invalidate(); return true;
    }

    public bool PointerWheel(HavenPoint localPosition, double deltaX, double deltaY)
    {
        if (Math.Abs(deltaX) > Math.Abs(deltaY) && Math.Abs(deltaX) > .001) { PanBy(-deltaX * 36, 0); return true; }
        if (Math.Abs(deltaY) < .001) return false;
        ZoomAt(deltaY < 0 ? 1.1 : 0.9, localPosition); return true;
    }

    public bool KeyDown(HavenKeyInput input)
    {
        if (input.PrimaryModifier && input.Key == HavenKey.A) { SelectAll(); return true; }
        if (input.PrimaryModifier && input.Key == HavenKey.Z) return input.Shift ? Redo() : Undo();
        if (input.PrimaryModifier && input.Key == HavenKey.Y) return Redo();
        if (input.PrimaryModifier && input.Key == HavenKey.D) { DuplicateSelection(); return true; }
        if (input.Key is HavenKey.Delete or HavenKey.Backspace) { DeleteSelection(); return true; }
        if (input.Key == HavenKey.Escape) { _gesture = NodeEditorGesture.None; _connectingNodeId = null; _connectingPortId = null; ClearSelection(); Invalidate(); return true; }
        if (input.Key == HavenKey.Home) { ResetViewport(); return true; }
        var amount = input.Shift ? 10 : 1;
        return input.Key switch
        {
            HavenKey.Left => MoveAndReturn(-amount, 0), HavenKey.Right => MoveAndReturn(amount, 0),
            HavenKey.Up => MoveAndReturn(0, -amount), HavenKey.Down => MoveAndReturn(0, amount), _ => false
        };
    }

    public string? Copy() => CopySelection();
    public string? Cut() { var text = CopySelection(); if (text is not null) DeleteSelection(); return text; }
    public bool Paste(string? text) => PasteSelection(text).Count > 0;

    public void Draw(HavenDrawingContext context, double opacity)
    {
        if (Bounds.Width <= 1 || Bounds.Height <= 1) return;
        context.Add(new HavenFillRoundedRectCommand(Bounds, new HavenTokenBrush("Surface"), 10, opacity)); DrawGrid(context, opacity);
        var viewport = VisibleWorldRect(90 / Zoom);
        var visible = _document.Nodes.Where(node => Intersects(NodeWorldRect(node), viewport)).Select(node => node.Id).ToHashSet();
        RealizedNodeCount = visible.Count; RealizedEdgeCount = 0;
        foreach (var edge in _document.Edges) if ((visible.Contains(edge.FromNodeId) || visible.Contains(edge.ToNodeId)) && DrawEdge(context, edge, opacity)) RealizedEdgeCount++;
        foreach (var node in _document.Nodes) if (visible.Contains(node.Id)) DrawNode(context, node, opacity);
        if (_gesture == NodeEditorGesture.Connect && _connectingNodeId is { } fromId && _connectingPortId is { } fromPort && FindNode(fromId) is { } fromNode)
        {
            var start = PortScreenPoint(fromNode, FindPort(fromNode, fromPort)!); var end = new HavenPoint(Bounds.X + _lastPointer.X, Bounds.Y + _lastPointer.Y);
            context.Add(new HavenLineCommand(start, end, new HavenPen(new HavenTokenBrush("Accent"), 2), opacity));
        }
        if (_gesture == NodeEditorGesture.Marquee)
        {
            var rect = WorldToScreen(NormalizeRect(_marqueeStart, _marqueeEnd));
            context.Add(new HavenFillRoundedRectCommand(rect, new HavenSolidBrush(24, 100, 120, 255), 4, opacity));
            context.Add(new HavenStrokeRoundedRectCommand(rect, new HavenPen(new HavenTokenBrush("Accent"), 1.5), 4, opacity));
        }
        if (_document.Nodes.Count >= 12) DrawMinimap(context, opacity);
    }

    private bool MoveAndReturn(double dx, double dy) { MoveSelectionBy(dx, dy); return true; }

    private void DrawGrid(HavenDrawingContext context, double opacity)
    {
        var step = GridSpacing * Zoom; if (step < 10) return; var pen = new HavenPen(new HavenSolidBrush(24, 115, 120, 130), 1);
        for (var x = Bounds.X + Mod(PanX, step); x <= Bounds.Right; x += step) context.Add(new HavenLineCommand(new HavenPoint(x, Bounds.Y), new HavenPoint(x, Bounds.Bottom), pen, opacity));
        for (var y = Bounds.Y + Mod(PanY, step); y <= Bounds.Bottom; y += step) context.Add(new HavenLineCommand(new HavenPoint(Bounds.X, y), new HavenPoint(Bounds.Right, y), pen, opacity));
    }

    private bool DrawEdge(HavenDrawingContext context, NodeEditorEdge edge, double opacity)
    {
        var from = FindNode(edge.FromNodeId); var to = FindNode(edge.ToNodeId); if (from is null || to is null) return false;
        var fromPort = FindPort(from, edge.FromPortId); var toPort = FindPort(to, edge.ToPortId); if (fromPort is null || toPort is null) return false;
        var start = PortScreenPoint(from, fromPort); var end = PortScreenPoint(to, toPort); var midX = (start.X + end.X) / 2;
        var invalid = _diagnostics.Any(diagnostic => diagnostic.EdgeId == edge.Id);
        var selected = _selectedEdges.Contains(edge.Id);
        var pen = invalid
            ? new HavenPen(new HavenSolidBrush(255, 220, 64, 72), selected ? 4 : 3)
            : new HavenPen(new HavenTokenBrush("Accent"), selected ? 4 : 2);
        context.Add(new HavenLineCommand(start, new HavenPoint(midX, start.Y), pen, opacity));
        context.Add(new HavenLineCommand(new HavenPoint(midX, start.Y), new HavenPoint(midX, end.Y), pen, opacity));
        context.Add(new HavenLineCommand(new HavenPoint(midX, end.Y), end, pen, opacity)); return true;
    }

    private void DrawNode(HavenDrawingContext context, NodeEditorNode node, double opacity)
    {
        var rect = WorldToScreen(NodeWorldRect(node));
        var invalid = _diagnostics.Any(diagnostic => diagnostic.NodeId == node.Id);
        var selected = _selected.Contains(node.Id);
        var borderPen = invalid
            ? new HavenPen(new HavenSolidBrush(255, 220, 64, 72), selected ? 4 : 3)
            : new HavenPen(new HavenTokenBrush(selected ? "Accent" : "Border"), selected ? 2.5 : 1);
        context.Add(new HavenFillRoundedRectCommand(rect, new HavenTokenBrush("SurfaceRaised"), 10, opacity));
        context.Add(new HavenStrokeRoundedRectCommand(rect, borderPen, 10, opacity));
        context.Add(new HavenTextCommand(new HavenRect(rect.X + 14, rect.Y + 10, Math.Max(20, rect.Width - 28), 18), new HavenTextLayout(node.Category.ToUpperInvariant(), "Segoe UI", Math.Max(9, 10 * Zoom), 600, Math.Max(20, rect.Width - 28)), new HavenTokenBrush("TextSecondary"), opacity));
        context.Add(new HavenTextCommand(new HavenRect(rect.X + 14, rect.Y + 30, Math.Max(20, rect.Width - 28), 24), new HavenTextLayout(node.Title, "Segoe UI", Math.Max(11, 15 * Zoom), 650, Math.Max(20, rect.Width - 28)), new HavenTokenBrush("TextPrimary"), opacity));
        if (!string.IsNullOrWhiteSpace(node.Subtitle)) context.Add(new HavenTextCommand(new HavenRect(rect.X + 14, rect.Y + 54, Math.Max(20, rect.Width - 28), 20), new HavenTextLayout(node.Subtitle, "Segoe UI", Math.Max(9, 11 * Zoom), 400, Math.Max(20, rect.Width - 28)), new HavenTokenBrush("TextSecondary"), opacity));
        foreach (var port in node.Ports)
        {
            var point = PortScreenPoint(node, port); var radius = Math.Max(3, PortRadius * Math.Sqrt(Zoom));
            context.Add(new HavenEllipseCommand(new HavenRect(point.X - radius, point.Y - radius, radius * 2, radius * 2), new HavenTokenBrush("Accent"), null, opacity));
        }
    }

    private void DrawMinimap(HavenDrawingContext context, double opacity)
    {
        if (_document.Nodes.Count == 0) return;
        var minX = _document.Nodes.Min(node => node.X); var minY = _document.Nodes.Min(node => node.Y); var maxX = _document.Nodes.Max(node => node.X + node.Width); var maxY = _document.Nodes.Max(node => node.Y + node.Height);
        var rect = new HavenRect(Bounds.Right - MinimapWidth - 14, Bounds.Bottom - MinimapHeight - 14, MinimapWidth, MinimapHeight);
        context.Add(new HavenFillRoundedRectCommand(rect, new HavenSolidBrush(225, 20, 24, 32), 8, opacity)); context.Add(new HavenStrokeRoundedRectCommand(rect, new HavenPen(new HavenTokenBrush("Border"), 1), 8, opacity));
        var scale = Math.Min((rect.Width - 12) / Math.Max(1, maxX - minX), (rect.Height - 12) / Math.Max(1, maxY - minY));
        foreach (var node in _document.Nodes) context.Add(new HavenFillRoundedRectCommand(new HavenRect(rect.X + 6 + (node.X - minX) * scale, rect.Y + 6 + (node.Y - minY) * scale, Math.Max(2, node.Width * scale), Math.Max(2, node.Height * scale)), new HavenSolidBrush(150, 120, 130, 170), 1, opacity));
        var view = VisibleWorldRect();
        context.Add(new HavenStrokeRoundedRectCommand(new HavenRect(rect.X + 6 + (view.X - minX) * scale, rect.Y + 6 + (view.Y - minY) * scale, Math.Max(2, view.Width * scale), Math.Max(2, view.Height * scale)), new HavenPen(new HavenTokenBrush("Accent"), 1.2), 2, opacity));
    }

    private void SetDocument(NodeEditorDocument next, bool recordUndo)
    {
        if (recordUndo) { _undo.Push(_document); _redo.Clear(); } _document = next; TrimSelection(); DocumentChanged?.Invoke(_document); Invalidate();
    }

    private void TrimSelection()
    {
        _selected.RemoveWhere(id => !_document.Nodes.Any(node => node.Id == id));
        _selectedEdges.RemoveWhere(id => !_document.Edges.Any(edge => edge.Id == id));
    }
    private void RaiseSelectionChanged() { SelectionChanged?.Invoke(_selected.ToArray()); Invalidate(); }
    private NodeEditorNode? FindNode(Guid id) => _document.Nodes.FirstOrDefault(node => node.Id == id);
    private static NodeEditorPort? FindPort(NodeEditorNode node, string portId) => node.Ports.FirstOrDefault(port => string.Equals(port.Id, portId, StringComparison.Ordinal));

    private NodeEditorNode? HitNode(HavenPoint localPoint)
    {
        for (var index = _document.Nodes.Count - 1; index >= 0; index--) if (Contains(NodeLocalRect(_document.Nodes[index]), localPoint)) return _document.Nodes[index];
        return null;
    }

    private NodeEditorEdge? HitEdge(HavenPoint localPoint)
    {
        var tolerance = Math.Max(6, 8 * Math.Sqrt(Zoom));
        foreach (var edge in _document.Edges.Reverse())
        {
            var from = FindNode(edge.FromNodeId); var to = FindNode(edge.ToNodeId);
            if (from is null || to is null) continue;
            var fromPort = FindPort(from, edge.FromPortId); var toPort = FindPort(to, edge.ToPortId);
            if (fromPort is null || toPort is null) continue;
            var start = PortLocalPoint(from, fromPort); var end = PortLocalPoint(to, toPort); var midX = (start.X + end.X) / 2;
            if (DistanceToSegment(localPoint, start, new HavenPoint(midX, start.Y)) <= tolerance
                || DistanceToSegment(localPoint, new HavenPoint(midX, start.Y), new HavenPoint(midX, end.Y)) <= tolerance
                || DistanceToSegment(localPoint, new HavenPoint(midX, end.Y), end) <= tolerance)
                return edge;
        }
        return null;
    }

    private bool TryHitPort(HavenPoint localPoint, out NodeEditorNode node, out NodeEditorPort port)
    {
        foreach (var candidate in _document.Nodes.Reverse()) foreach (var candidatePort in candidate.Ports)
        {
            var point = PortLocalPoint(candidate, candidatePort); var dx = point.X - localPoint.X; var dy = point.Y - localPoint.Y; var hitRadius = Math.Max(9, 12 * Math.Sqrt(Zoom));
            if (dx * dx + dy * dy <= hitRadius * hitRadius) { node = candidate; port = candidatePort; return true; }
        }
        node = null!; port = null!; return false;
    }

    private HavenPoint PortLocalPoint(NodeEditorNode node, NodeEditorPort port)
    {
        var same = node.Ports.Where(candidate => candidate.Direction == port.Direction).ToArray(); var index = Math.Max(0, Array.FindIndex(same, candidate => candidate.Id == port.Id));
        var y = node.Y + Math.Min(node.Height - 18, 68 + index * 22); var x = port.Direction == NodeEditorPortDirection.Input ? node.X : node.X + node.Width;
        return new HavenPoint(PanX + x * Zoom, PanY + y * Zoom);
    }

    private HavenPoint PortScreenPoint(NodeEditorNode node, NodeEditorPort port) { var local = PortLocalPoint(node, port); return new HavenPoint(Bounds.X + local.X, Bounds.Y + local.Y); }
    private HavenRect NodeLocalRect(NodeEditorNode node) => new(PanX + node.X * Zoom, PanY + node.Y * Zoom, node.Width * Zoom, node.Height * Zoom);
    private static HavenRect NodeWorldRect(NodeEditorNode node) => new(node.X, node.Y, Math.Max(40, node.Width), Math.Max(32, node.Height));
    private HavenPoint ScreenToWorld(HavenPoint localPoint) => new((localPoint.X - PanX) / Zoom, (localPoint.Y - PanY) / Zoom);
    private HavenRect WorldToScreen(HavenRect world) => new(Bounds.X + PanX + world.X * Zoom, Bounds.Y + PanY + world.Y * Zoom, world.Width * Zoom, world.Height * Zoom);
    private HavenRect VisibleWorldRect(double margin = 0) => new((-PanX / Zoom) - margin, (-PanY / Zoom) - margin, Bounds.Width / Zoom + margin * 2, Bounds.Height / Zoom + margin * 2);

    private bool WouldCreateCycle(Guid from, Guid to)
    {
        var stack = new Stack<Guid>(); var seen = new HashSet<Guid>(); stack.Push(to);
        while (stack.Count > 0) { var current = stack.Pop(); if (current == from) return true; if (!seen.Add(current)) continue; foreach (var next in _document.Edges.Where(edge => edge.FromNodeId == current).Select(edge => edge.ToNodeId)) stack.Push(next); }
        return false;
    }

    private bool HasCycle()
    {
        var state = new Dictionary<Guid, byte>(); foreach (var node in _document.Nodes) if (Visit(node.Id, state)) return true; return false;
    }

    private bool Visit(Guid id, Dictionary<Guid, byte> state)
    {
        if (state.TryGetValue(id, out var value)) return value == 1; state[id] = 1;
        foreach (var next in _document.Edges.Where(edge => edge.FromNodeId == id).Select(edge => edge.ToNodeId)) if (Visit(next, state)) return true; state[id] = 2; return false;
    }

    private static bool SameNodePositions(NodeEditorDocument first, NodeEditorDocument second)
    {
        if (first.Nodes.Count != second.Nodes.Count) return false; var byId = second.Nodes.ToDictionary(node => node.Id);
        return first.Nodes.All(node => byId.TryGetValue(node.Id, out var other) && Math.Abs(node.X - other.X) <= .001 && Math.Abs(node.Y - other.Y) <= .001);
    }

    private static HavenRect NormalizeRect(HavenPoint a, HavenPoint b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static double DistanceToSegment(HavenPoint point, HavenPoint start, HavenPoint end)
    {
        var dx = end.X - start.X; var dy = end.Y - start.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= .0001) return Math.Sqrt(Math.Pow(point.X - start.X, 2) + Math.Pow(point.Y - start.Y, 2));
        var t = Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared, 0, 1);
        var nearestX = start.X + t * dx; var nearestY = start.Y + t * dy;
        return Math.Sqrt(Math.Pow(point.X - nearestX, 2) + Math.Pow(point.Y - nearestY, 2));
    }

    private static bool Contains(HavenRect rect, HavenPoint point) => point.X >= rect.X && point.X <= rect.Right && point.Y >= rect.Y && point.Y <= rect.Bottom;
    private static bool Intersects(HavenRect a, HavenRect b) => a.X <= b.Right && a.Right >= b.X && a.Y <= b.Bottom && a.Bottom >= b.Y;
    private static double Mod(double value, double modulus) { var result = value % modulus; return result < 0 ? result + modulus : result; }

    private enum NodeEditorGesture { None, Pan, Move, Marquee, Connect }
}
