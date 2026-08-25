namespace Haven.Core;

public enum BoardsLiveComponentKind
{
    TaskList = 0,
    Poll = 1,
    Status = 2,
    Table = 3,
    List = 4
}

public enum BoardsLiveSourceKind
{
    Manual = 0,
    SharedRuntime = 1,
    External = 2
}

public enum BoardsLiveAvailability
{
    Available = 0,
    Stale = 1,
    Unavailable = 2
}

public sealed class BoardsLiveComponentSource
{
    public BoardsLiveSourceKind Kind { get; set; } = BoardsLiveSourceKind.Manual;
    public string Provider { get; set; } = "Haven";
    public string ResourceId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = "Manual board content";
    public BoardsLiveAvailability Availability { get; set; } = BoardsLiveAvailability.Available;
    public DateTimeOffset? LastSyncedAt { get; set; }
    public string UnavailableReason { get; set; } = string.Empty;
}

public sealed class BoardsLiveComponent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public BoardsLiveComponentKind Kind { get; set; }
    public string Title { get; set; } = string.Empty;
    public List<BoardsLiveComponentItem> Items { get; set; } = [];
    public BoardsLiveComponentSource Source { get; set; } = new();
    public long Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class BoardsLiveComponentItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = string.Empty;
    public bool Checked { get; set; }
    public int Votes { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<string> Cells { get; set; } = [];
}

public sealed class BoardsLiveComponentPlacement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ComponentId { get; set; }
    public Guid PageId { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 360;
    public double Height { get; set; } = 180;
    public int ZIndex { get; set; }
}

public enum BoardsAttachmentStatus
{
    Available = 0,
    Missing = 1,
    Unavailable = 2
}

public sealed record BoardsAttachmentResolution(
    Guid AttachmentId,
    BoardsAttachmentStatus Status,
    string? ResolvedPath,
    string Message);
