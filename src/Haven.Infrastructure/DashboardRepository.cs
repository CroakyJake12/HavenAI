using System.Globalization;
using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

/// <summary>
/// Produces the Home surface snapshot with a bounded set of aggregate queries.
/// The dashboard never walks individual activity rows to compute counters.
/// </summary>
public sealed class DashboardRepository(ISqliteConnectionFactory factory) : IDashboardRepository
{
    public async Task<DashboardSnapshot> GetSnapshotAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var localDayStart = new DateTimeOffset(now.Date, now.Offset);
        var localDayEnd = localDayStart.AddDays(1);
        var weekStart = localDayStart.AddDays(-(((int)localDayStart.DayOfWeek + 6) % 7));
        var sevenDays = localDayEnd.AddDays(7);

        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var counters = await ReadCountersAsync(connection, localDayStart, localDayEnd, weekStart, sevenDays, cancellationToken).ConfigureAwait(false);
        var agenda = await ReadAgendaAsync(connection, now, localDayEnd, cancellationToken).ConfigureAwait(false);
        var recent = await ReadRecentAsync(connection, cancellationToken).ConfigureAwait(false);

        return new DashboardSnapshot(
            DateTimeOffset.UtcNow,
            counters.ConversationsToday,
            counters.MessagesThisWeek,
            counters.ActiveProjects,
            counters.ChatGroups,
            counters.TeachingSubjects,
            counters.TasksDueToday,
            counters.OverdueTasks,
            counters.TasksCompletedThisWeek,
            counters.UpcomingEvents,
            counters.EnabledAutomations,
            counters.CallsThisWeek,
            TimeSpan.FromSeconds(counters.CallDurationSeconds),
            agenda,
            recent);
    }

    private static async Task<Counters> ReadCountersAsync(
        SqliteConnection connection,
        DateTimeOffset dayStart,
        DateTimeOffset dayEnd,
        DateTimeOffset weekStart,
        DateTimeOffset sevenDays,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
              (SELECT COUNT(*) FROM conversations WHERE is_archived=0 AND created_at >= $dayStart AND created_at < $dayEnd),
              (SELECT COUNT(*) FROM messages WHERE created_at >= $weekStart),
              (SELECT COUNT(*) FROM containers WHERE mode=3 AND is_archived=0),
              (SELECT COUNT(*) FROM containers WHERE mode=0 AND is_archived=0),
              (SELECT COUNT(*) FROM containers WHERE mode=1 AND is_archived=0),
              (SELECT COUNT(*) FROM planner_tasks WHERE status NOT IN (3,4) AND due_at >= $dayStart AND due_at < $dayEnd),
              (SELECT COUNT(*) FROM planner_tasks WHERE status NOT IN (3,4) AND due_at IS NOT NULL AND due_at < $dayStart),
              (SELECT COUNT(*) FROM planner_task_completions WHERE completed_at >= $weekStart),
              (SELECT COUNT(*) FROM planner_events WHERE deleted_at IS NULL AND ends_at >= $dayStart AND starts_at < $sevenDays),
              (SELECT COUNT(*) FROM automations WHERE is_enabled=1),
              (SELECT COUNT(*) FROM call_sessions WHERE started_at >= $weekStart),
              (SELECT COALESCE(SUM(MAX(0, CAST((julianday(COALESCE(ended_at,$dayEnd))-julianday(started_at))*86400 AS INTEGER))),0)
                 FROM call_sessions WHERE started_at >= $weekStart);
            """;
        Add(command, "$dayStart", dayStart);
        Add(command, "$dayEnd", dayEnd);
        Add(command, "$weekStart", weekStart);
        Add(command, "$sevenDays", sevenDays);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return default;
        return new Counters(
            reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4),
            reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8), reader.GetInt32(9),
            reader.GetInt32(10), reader.GetInt64(11));
    }

    private static async Task<IReadOnlyList<DashboardAgendaItem>> ReadAgendaAsync(
        SqliteConnection connection,
        DateTimeOffset now,
        DateTimeOffset dayEnd,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,'task',title,
                   CASE WHEN due_at < $now THEN 'Overdue' ELSE COALESCE(NULLIF(notes,''),'Task') END,
                   due_at,CASE WHEN due_at < $now THEN 1 ELSE 0 END,'plan'
              FROM planner_tasks
             WHERE status NOT IN (3,4) AND due_at IS NOT NULL AND due_at < $dayEnd
            UNION ALL
            SELECT id,'event',title,COALESCE(NULLIF(location,''),'Calendar event'),starts_at,0,'plan'
              FROM planner_events
             WHERE deleted_at IS NULL AND ends_at >= $now AND starts_at < $dayEnd
             ORDER BY 6 DESC, 5
             LIMIT 12;
            """;
        Add(command, "$now", now);
        Add(command, "$dayEnd", dayEnd);
        var result = new List<DashboardAgendaItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new DashboardAgendaItem(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                ParseDate(reader.GetString(4)), reader.GetInt32(5) != 0, reader.GetString(6)));
        }
        return result;
    }

    private static async Task<IReadOnlyList<DashboardWorkItem>> ReadRecentAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,'chat',title,
                   CASE mode WHEN 1 THEN 'Teaching' WHEN 2 THEN 'Do' WHEN 3 THEN 'Studio' ELSE 'Chat' END,
                   updated_at,'chat','chat'
              FROM conversations WHERE is_archived=0
            UNION ALL
            SELECT id,
                   CASE mode WHEN 0 THEN 'group' WHEN 1 THEN 'subject' ELSE 'project' END,
                   name,
                   CASE mode WHEN 0 THEN 'Chat Group' WHEN 1 THEN 'Teaching subject' ELSE 'Studio project' END,
                   updated_at,
                   CASE mode WHEN 0 THEN 'folder' WHEN 1 THEN 'teach' ELSE 'studio' END,
                   CASE mode WHEN 0 THEN 'chat' WHEN 1 THEN 'teach' ELSE 'studio' END
              FROM containers WHERE is_archived=0 AND mode IN (0,1,3)
            UNION ALL
            SELECT id,'call','Call',COALESCE(NULLIF(model_name,''),'Local call'),started_at,'call','call'
              FROM call_sessions
             ORDER BY 5 DESC
             LIMIT 12;
            """;
        var result = new List<DashboardWorkItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new DashboardWorkItem(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                ParseDate(reader.GetString(4)), reader.GetString(5), reader.GetString(6)));
        }
        return result;
    }

    private static void Add(SqliteCommand command, string name, DateTimeOffset value) =>
        command.Parameters.AddWithValue(name, value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private readonly record struct Counters(
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
        long CallDurationSeconds);
}
