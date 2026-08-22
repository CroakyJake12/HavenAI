namespace Haven.Core;

public enum WorkspaceWindowKind { Main = 0, Normal = 1, PopUp = 2 }
public enum WorkspaceLayoutKind { Single = 0, Split = 1 }
public enum SplitOrientation { Horizontal = 0, Vertical = 1 }

public sealed record TabSessionSnapshot(
    Guid Id,
    string AppKey,
    string Title,
    string StateJson,
    string? NavigationJson,
    double? ScrollOffset,
    Guid? GroupId,
    bool IsPinned,
    bool IsProtected,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TabGroupSnapshot(
    Guid Id,
    string Name,
    bool IsCollapsed,
    IReadOnlyList<Guid> OrderedTabIds);

public sealed record WorkspacePaneSnapshot(Guid Id, Guid TabId, int Order);

public sealed record WorkspaceLayoutSnapshot(
    Guid Id,
    WorkspaceLayoutKind Kind,
    SplitOrientation Orientation,
    double PrimaryRatio,
    IReadOnlyList<WorkspacePaneSnapshot> Panes)
{
    public const double MinimumPaneRatio = 0.2;
    public const double MaximumPaneRatio = 0.8;
}

public sealed record WorkspaceWindowSnapshot(
    Guid Id,
    WorkspaceWindowKind Kind,
    WorkspaceLayoutSnapshot Layout,
    IReadOnlyList<Guid> OrderedTabIds,
    Guid? SelectedTabId,
    string? BoundsJson,
    DateTimeOffset UpdatedAt);

public sealed record WorkspaceSessionSnapshot(
    int SchemaVersion,
    IReadOnlyList<TabSessionSnapshot> Tabs,
    IReadOnlyList<TabGroupSnapshot> Groups,
    IReadOnlyList<WorkspaceWindowSnapshot> Windows,
    DateTimeOffset SavedAt)
{
    public const int CurrentSchemaVersion = 1;
}
