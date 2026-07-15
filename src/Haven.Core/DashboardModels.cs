namespace Haven.Core;

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

public sealed record DashboardAgendaItem(
    string Id,
    string Kind,
    string Title,
    string Detail,
    DateTimeOffset? StartsAt,
    bool IsOverdue,
    string ActionKey);

public sealed record DashboardWorkItem(
    string Id,
    string Kind,
    string Title,
    string Detail,
    DateTimeOffset UpdatedAt,
    string IconKey,
    string ActionKey);

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

public sealed record DashboardTileData(
    string Primary,
    string Secondary,
    string? Badge = null,
    bool HasWarning = false);

public sealed record DashboardTileLayout(
    int Version,
    string Key,
    int Order,
    bool IsVisible,
    DashboardTileSize Size);

public sealed record DashboardPluginTileManifest(
    string Key,
    string Title,
    string Description,
    string IconKey,
    string ProviderKey,
    string ActionKey,
    string Size = "Standard");

public enum DashboardTileSize { Compact, Standard, Wide }

