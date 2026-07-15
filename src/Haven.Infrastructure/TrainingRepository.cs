using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class TrainingRepository(ISqliteConnectionFactory factory) : ITrainingRepository
{
    public async Task UpsertRunAsync(TrainingRun run, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO training_runs(id, task_prompt, workspace_path, snapshot_path, model_name,
                max_attempts, duration_minutes, file_permission, command_permission, browser_permission,
                allow_desktop_tools, allow_file_system_writes, created_at, completed_at)
            VALUES($id, $taskPrompt, $workspacePath, $snapshotPath, $modelName,
                $maxAttempts, $durationMinutes, $filePermission, $commandPermission, $browserPermission,
                $allowDesktopTools, $allowFileSystemWrites, $createdAt, $completedAt)
            ON CONFLICT(id) DO UPDATE SET
                completed_at=$completedAt;
            """;
        command.Parameters.AddWithValue("$id", run.Id.ToString());
        command.Parameters.AddWithValue("$taskPrompt", run.TaskPrompt);
        command.Parameters.AddWithValue("$workspacePath", run.WorkspacePath);
        command.Parameters.AddWithValue("$snapshotPath", run.SnapshotPath);
        command.Parameters.AddWithValue("$modelName", run.ModelName);
        command.Parameters.AddWithValue("$maxAttempts", run.MaxAttempts);
        command.Parameters.AddWithValue("$durationMinutes", run.DurationMinutes);
        command.Parameters.AddWithValue("$filePermission", (int)run.FilePermission);
        command.Parameters.AddWithValue("$commandPermission", (int)run.CommandPermission);
        command.Parameters.AddWithValue("$browserPermission", (int)run.BrowserPermission);
        command.Parameters.AddWithValue("$allowDesktopTools", run.AllowDesktopTools ? 1 : 0);
        command.Parameters.AddWithValue("$allowFileSystemWrites", run.AllowFileSystemWrites ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", run.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$completedAt", (object?)run.CompletedAt?.ToString("O") ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TrainingRun?> GetRunAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM training_runs WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadRun(reader) : null;
    }

    public async Task<IReadOnlyList<TrainingRun>> GetRecentRunsAsync(int limit, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM training_runs ORDER BY created_at DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", limit);
        var results = new List<TrainingRun>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            results.Add(ReadRun(reader));
        return results;
    }

    public async Task DeleteRunAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM training_runs WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpsertAttemptAsync(TrainingAttempt attempt, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO training_attempts(id, training_run_id, attempt_number, report_markdown, feedback,
                action_log, succeeded, duration_ms, created_at)
            VALUES($id, $runId, $attemptNumber, $report, $feedback,
                $actionLog, $succeeded, $durationMs, $createdAt);
            """;
        command.Parameters.AddWithValue("$id", attempt.Id.ToString());
        command.Parameters.AddWithValue("$runId", attempt.TrainingRunId.ToString());
        command.Parameters.AddWithValue("$attemptNumber", attempt.AttemptNumber);
        command.Parameters.AddWithValue("$report", attempt.ReportMarkdown);
        command.Parameters.AddWithValue("$feedback", (object?)attempt.Feedback ?? DBNull.Value);
        command.Parameters.AddWithValue("$actionLog", attempt.ActionLog);
        command.Parameters.AddWithValue("$succeeded", attempt.Succeeded ? 1 : 0);
        command.Parameters.AddWithValue("$durationMs", (long)attempt.Duration.TotalMilliseconds);
        command.Parameters.AddWithValue("$createdAt", attempt.CreatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TrainingAttempt>> GetAttemptsAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM training_attempts WHERE training_run_id=$runId ORDER BY attempt_number;";
        command.Parameters.AddWithValue("$runId", runId.ToString());
        var results = new List<TrainingAttempt>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            results.Add(ReadAttempt(reader));
        return results;
    }

    private static TrainingRun ReadRun(Microsoft.Data.Sqlite.SqliteDataReader reader) => new(
        Guid.Parse(reader.String("id")),
        reader.String("task_prompt"),
        reader.String("workspace_path"),
        reader.String("snapshot_path"),
        reader.String("model_name"),
        reader.Int32("max_attempts"),
        reader.Int32("duration_minutes"),
        (PermissionMode)reader.Int32("file_permission"),
        (PermissionMode)reader.Int32("command_permission"),
        (PermissionMode)reader.Int32("browser_permission"),
        reader.Int32("allow_desktop_tools") == 1,
        reader.Int32("allow_file_system_writes") == 1,
        reader.DateTimeOffset("created_at"),
        reader.NullableDateTimeOffset("completed_at"));

    private static TrainingAttempt ReadAttempt(Microsoft.Data.Sqlite.SqliteDataReader reader) => new(
        Guid.Parse(reader.String("id")),
        Guid.Parse(reader.String("training_run_id")),
        reader.Int32("attempt_number"),
        reader.String("report_markdown"),
        reader.NullableString("feedback"),
        reader.String("action_log"),
        reader.Int32("succeeded") == 1,
        TimeSpan.FromMilliseconds(reader.GetInt64(reader.GetOrdinal("duration_ms"))),
        reader.DateTimeOffset("created_at"));
}
