// Dashboard snapshots, agenda items, work items, tile definitions, and layouts.

namespace Haven.Core;

/// <summary>
/// Represents dashboard snapshot and keeps its related state and behavior together.
/// </summary>
public sealed record DashboardSnapshot(
    DateTimeOffset CapturedAt,
    int ConversationsToday,
    int MessagesThisWeek,
    int ActiveProjects,
    int ChatGroups,
    int TeachingSubjects,
    int TasksDueToday,
    int OverdueTasks,
    int TasksCompletedThisWeek,
    int UpcomingEvents,
    int EnabledAutomations,
    int CallsThisWeek,
    TimeSpan CallDurationThisWeek,
    IReadOnlyList<DashboardAgendaItem> Agenda,
    IReadOnlyList<DashboardWorkItem> RecentWork);

/// <summary>
/// Represents dashboard agenda item and keeps its related state and behavior together.
/// </summary>
public sealed record DashboardAgendaItem(
    string Id,
    string Kind,
    string Title,
    string Detail,
    DateTimeOffset? StartsAt,
    bool IsOverdue,
    string ActionKey);

/// <summary>
/// Represents dashboard work item and keeps its related state and behavior together.
/// </summary>
public sealed record DashboardWorkItem(
    string Id,
    string Kind,
    string Title,
    string Detail,
    DateTimeOffset UpdatedAt,
    string IconKey,
    string ActionKey);

/// <summary>
/// Represents dashboard tile definition and keeps its related state and behavior together.
/// </summary>
public sealed record DashboardTileDefinition(
    string Key,
    string Title,
    string Description,
    string IconKey,
    string ProviderKey,
    string ActionKey,
    DashboardTileSize DefaultSize = DashboardTileSize.Standard,
    int DefaultOrder = 0,
    bool IsBuiltIn = true);

/// <summary>
/// Represents dashboard tile data and keeps its related state and behavior together.
/// </summary>
public sealed record DashboardTileData(
    string Primary,
    string Secondary,
    string? Badge = null,
    bool HasWarning = false);

/// <summary>
/// Represents dashboard tile layout and keeps its related state and behavior together.
/// </summary>
public sealed record DashboardTileLayout(
    int Version,
    string Key,
    int Order,
    bool IsVisible,
    DashboardTileSize Size);

/// <summary>
/// Represents dashboard plugin tile manifest and keeps its related state and behavior together.
/// </summary>
public sealed record DashboardPluginTileManifest(
    string Key,
    string Title,
    string Description,
    string IconKey,
    string ProviderKey,
    string ActionKey,
    string Size = "Standard");

/// <summary>
/// Lists the supported dashboard tile size values used to make state explicit and type-safe.
/// </summary>
public enum DashboardTileSize { Compact, Standard, Wide }
