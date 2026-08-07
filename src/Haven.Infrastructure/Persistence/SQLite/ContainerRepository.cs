/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/ContainerRepository.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns ContainerRepository. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

/// <summary>
/// Represents container repository and keeps its related state and behavior together.
/// </summary>
public sealed class ContainerRepository(ISqliteConnectionFactory factory, IAppPaths? paths = null) : IContainerRepository
{
    /// <summary>
    /// Retrieves by mode async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<ContainerDefinition>> GetByModeAsync(HavenMode mode, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM containers WHERE mode=$mode AND is_archived=0 ORDER BY name;";
        command.Parameters.AddWithValue("$mode", (int)mode);
        var result = new List<ContainerDefinition>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(Map(reader));
        return result;
    }

    /// <summary>
    /// Retrieves archived by mode async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<ContainerDefinition>> GetArchivedByModeAsync(HavenMode mode, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM containers WHERE mode=$mode AND is_archived=1 ORDER BY updated_at DESC;";
        command.Parameters.AddWithValue("$mode", (int)mode);
        var result = new List<ContainerDefinition>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(Map(reader));
        return result;
    }

    /// <summary>
    /// Performs upsert asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task UpsertAsync(ContainerDefinition item, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO containers(id,mode,name,root_path,context,instructions,created_at,updated_at,is_archived)
            VALUES($id,$mode,$name,$rootPath,$context,$instructions,$createdAt,$updatedAt,$isArchived)
            ON CONFLICT(id) DO UPDATE SET mode=excluded.mode,name=excluded.name,root_path=excluded.root_path,
              context=excluded.context,instructions=excluded.instructions,updated_at=excluded.updated_at,is_archived=excluded.is_archived;
            """;
        command.Parameters.AddWithValue("$id", item.Id.ToString());
        command.Parameters.AddWithValue("$mode", (int)item.Mode);
        command.Parameters.AddWithValue("$name", item.Name);
        command.Parameters.AddWithValue("$rootPath", (object?)item.RootPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$context", item.Context);
        command.Parameters.AddWithValue("$instructions", item.Instructions);
        command.Parameters.AddWithValue("$createdAt", item.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", item.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$isArchived", item.IsArchived ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates subject async with the invariants required by its callers.
    /// </summary>
    public async Task<Lesson> CreateSubjectAsync(ContainerDefinition subject, CancellationToken cancellationToken)
    {
        if (subject.Mode != HavenMode.Study)
            throw new ArgumentException("A subject must use Study mode.", nameof(subject));

        var now = DateTimeOffset.UtcNow;
        var general = new Lesson(Guid.NewGuid(), subject.Id, "General", "General", "{}", 0, now, now);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var subjectCommand = connection.CreateCommand())
        {
            subjectCommand.Transaction = transaction;
            subjectCommand.CommandText = """
                INSERT INTO containers(id,mode,name,root_path,context,instructions,created_at,updated_at,is_archived)
                VALUES($id,$mode,$name,$rootPath,$context,$instructions,$createdAt,$updatedAt,$isArchived);
                """;
            AddContainerParameters(subjectCommand, subject);
            await subjectCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var lessonCommand = connection.CreateCommand())
        {
            lessonCommand.Transaction = transaction;
            lessonCommand.CommandText = """
                INSERT INTO lessons(id,subject_id,topic_group,name,structure_json,sort_order,created_at,updated_at)
                VALUES($id,$subjectId,$topicGroup,$name,$structureJson,$sortOrder,$createdAt,$updatedAt);
                """;
            AddLessonParameters(lessonCommand, general);
            await lessonCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return general;
    }

    /// <summary>
    /// Performs delete asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => DeleteAndDetachConversationsAsync(id, cancellationToken);

    /// <summary>
    /// Performs delete and detach conversations asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task DeleteAndDetachConversationsAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        int? mode;
        await using (var modeCommand = connection.CreateCommand())
        {
            modeCommand.Transaction = transaction;
            modeCommand.CommandText = "SELECT mode FROM containers WHERE id=$id;";
            modeCommand.Parameters.AddWithValue("$id", id.ToString());
            var value = await modeCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            mode = value is null or DBNull ? null : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        if (mode is null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var detachedKind = (HavenMode)mode.Value switch
        {
            HavenMode.Study => ConversationKind.QuickChat,
            HavenMode.Tasks => ConversationKind.Task,
            HavenMode.Studio => ConversationKind.StudioChat,
            _ => ConversationKind.Chat
        };
        await using (var detachCommand = connection.CreateCommand())
        {
            detachCommand.Transaction = transaction;
            detachCommand.CommandText = """
                UPDATE conversations
                SET container_id=NULL, lesson_id=NULL, kind=$kind, updated_at=$updatedAt
                WHERE container_id=$id;
                """;
            detachCommand.Parameters.AddWithValue("$id", id.ToString());
            detachCommand.Parameters.AddWithValue("$kind", (int)detachedKind);
            detachCommand.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
            await detachCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM containers WHERE id=$id;";
            deleteCommand.Parameters.AddWithValue("$id", id.ToString());
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        DeleteResourceDirectory(id);
    }

    /// <summary>
    /// Retrieves lessons async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<Lesson>> GetLessonsAsync(Guid subjectId, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM lessons WHERE subject_id=$subjectId ORDER BY sort_order,name;";
        command.Parameters.AddWithValue("$subjectId", subjectId.ToString());
        var result = new List<Lesson>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new Lesson(reader.Guid("id"), reader.Guid("subject_id"), reader.String("topic_group"), reader.String("name"),
                reader.String("structure_json"), reader.Int32("sort_order"), reader.DateTimeOffset("created_at"), reader.DateTimeOffset("updated_at")));
        }
        return result;
    }

    /// <summary>
    /// Performs upsert lesson asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task UpsertLessonAsync(Lesson lesson, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO lessons(id,subject_id,topic_group,name,structure_json,sort_order,created_at,updated_at)
            VALUES($id,$subjectId,$topicGroup,$name,$structureJson,$sortOrder,$createdAt,$updatedAt)
            ON CONFLICT(id) DO UPDATE SET subject_id=excluded.subject_id,topic_group=excluded.topic_group,name=excluded.name,
              structure_json=excluded.structure_json,sort_order=excluded.sort_order,updated_at=excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$id", lesson.Id.ToString());
        command.Parameters.AddWithValue("$subjectId", lesson.SubjectId.ToString());
        command.Parameters.AddWithValue("$topicGroup", lesson.TopicGroup);
        command.Parameters.AddWithValue("$name", lesson.Name);
        command.Parameters.AddWithValue("$structureJson", lesson.StructureJson);
        command.Parameters.AddWithValue("$sortOrder", lesson.SortOrder);
        command.Parameters.AddWithValue("$createdAt", lesson.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", lesson.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs delete lesson asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task DeleteLessonAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var detachCommand = connection.CreateCommand())
        {
            detachCommand.Transaction = transaction;
            detachCommand.CommandText = """
                UPDATE conversations
                SET container_id=NULL, lesson_id=NULL, kind=$kind, updated_at=$updatedAt
                WHERE lesson_id=$id;
                """;
            detachCommand.Parameters.AddWithValue("$id", id.ToString());
            detachCommand.Parameters.AddWithValue("$kind", (int)ConversationKind.QuickChat);
            detachCommand.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
            await detachCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM lessons WHERE id=$id;";
            deleteCommand.Parameters.AddWithValue("$id", id.ToString());
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs the add container parameters step owned by this component.
    /// </summary>
    private static void AddContainerParameters(SqliteCommand command, ContainerDefinition item)
    {
        command.Parameters.AddWithValue("$id", item.Id.ToString());
        command.Parameters.AddWithValue("$mode", (int)item.Mode);
        command.Parameters.AddWithValue("$name", item.Name);
        command.Parameters.AddWithValue("$rootPath", (object?)item.RootPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$context", item.Context);
        command.Parameters.AddWithValue("$instructions", item.Instructions);
        command.Parameters.AddWithValue("$createdAt", item.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", item.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$isArchived", item.IsArchived ? 1 : 0);
    }

    /// <summary>
    /// Performs the add lesson parameters step owned by this component.
    /// </summary>
    private static void AddLessonParameters(SqliteCommand command, Lesson lesson)
    {
        command.Parameters.AddWithValue("$id", lesson.Id.ToString());
        command.Parameters.AddWithValue("$subjectId", lesson.SubjectId.ToString());
        command.Parameters.AddWithValue("$topicGroup", lesson.TopicGroup);
        command.Parameters.AddWithValue("$name", lesson.Name);
        command.Parameters.AddWithValue("$structureJson", lesson.StructureJson);
        command.Parameters.AddWithValue("$sortOrder", lesson.SortOrder);
        command.Parameters.AddWithValue("$createdAt", lesson.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", lesson.UpdatedAt.ToString("O"));
    }

    /// <summary>
    /// Performs the delete resource directory step owned by this component.
    /// </summary>
    private void DeleteResourceDirectory(Guid containerId)
    {
        if (paths is null) return;
        var root = Path.GetFullPath(Path.Combine(paths.DataDirectory, "container-resources"));
        var target = Path.GetFullPath(Path.Combine(root, containerId.ToString("N")));
        var rootedPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(rootedPrefix, StringComparison.OrdinalIgnoreCase)) return;
        try { if (Directory.Exists(target)) Directory.Delete(target, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Performs the map step owned by this component.
    /// </summary>
    private static ContainerDefinition Map(Microsoft.Data.Sqlite.SqliteDataReader reader) =>
        new(reader.Guid("id"), (HavenMode)reader.Int32("mode"), reader.String("name"), reader.NullableString("root_path"),
            reader.String("context"), reader.String("instructions"), reader.DateTimeOffset("created_at"), reader.DateTimeOffset("updated_at"),
            reader.Boolean("is_archived"));
}
