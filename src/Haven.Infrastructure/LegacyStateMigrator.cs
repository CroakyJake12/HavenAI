/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/LegacyStateMigrator.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns LegacyStateMigrator. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Represents legacy state migrator and keeps its related state and behavior together.
/// </summary>
public sealed class LegacyStateMigrator(IAppPaths paths, ISqliteConnectionFactory factory, IConversationRepository conversations, IContainerRepository containers) : ILegacyStateMigrator
{
    /// <summary>
    /// Stores migration key locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const string MigrationKey = "legacy-localcode-state-v1";

    /// <summary>
    /// Performs migrate if needed asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<LegacyMigrationResult> MigrateIfNeededAsync(CancellationToken cancellationToken)
    {
        if (await HasCompletedAsync(cancellationToken).ConfigureAwait(false)) return new(false, false, 0, 0, "Migration was already completed.");
        if (!File.Exists(paths.LegacyStatePath))
        {
            await MarkCompletedAsync("No LocalCode state file was found.", cancellationToken).ConfigureAwait(false);
            return new(true, false, 0, 0, "No LocalCode state file was found.");
        }

        try
        {
            await using var stream = File.OpenRead(paths.LegacyStatePath);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            var projectMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            var conversationCount = 0;
            var messageCount = 0;

            if (root.TryGetProperty("projects", out var projects) && projects.ValueKind == JsonValueKind.Array)
            {
                foreach (var project in projects.EnumerateArray())
                {
                    var legacyId = GetString(project, "id") ?? Guid.NewGuid().ToString();
                    var id = ParseOrStable(legacyId, "project");
                    projectMap[legacyId] = id;
                    var now = DateTimeOffset.UtcNow;
                    var container = new ContainerDefinition(id, HavenMode.Studio, GetString(project, "name") ?? "Imported project", GetString(project, "workspacePath"), GetString(project, "context") ?? string.Empty, GetString(project, "instructions") ?? string.Empty, now, now);
                    await containers.UpsertAsync(container, cancellationToken).ConfigureAwait(false);
                }
            }

            if (root.TryGetProperty("chats", out var chats) && chats.ValueKind == JsonValueKind.Array)
            {
                foreach (var chat in chats.EnumerateArray())
                {
                    var id = ParseOrStable(GetString(chat, "id") ?? Guid.NewGuid().ToString(), "chat");
                    var product = GetString(chat, "product") ?? GetString(root, "product") ?? "haven-code";
                    var mode = product.Contains("chat", StringComparison.OrdinalIgnoreCase) ? HavenMode.Chat : HavenMode.Studio;
                    var projectIdText = GetString(chat, "projectId");
                    var containerId = projectIdText is not null && projectMap.TryGetValue(projectIdText, out var mapped) ? mapped : (Guid?)null;
                    var now = DateTimeOffset.UtcNow;
                    var conversation = new Conversation(id, mode, mode == HavenMode.Studio ? ConversationKind.StudioChat : ConversationKind.Chat, GetString(chat, "title") ?? "Imported chat", containerId, null, GetBoolean(chat, "pinned"), false, now, now);
                    await conversations.UpsertConversationAsync(conversation, cancellationToken).ConfigureAwait(false);
                    conversationCount++;

                    if (chat.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var message in messages.EnumerateArray())
                        {
                            var roleText = GetString(message, "role") ?? "assistant";
                            var role = roleText.Equals("user", StringComparison.OrdinalIgnoreCase) ? MessageRole.User : MessageRole.Assistant;
                            var content = GetString(message, "content") ?? string.Empty;
                            var item = new ChatMessage(Guid.NewGuid(), id, role, content, GetString(message, "agentName"), GetString(message, "model"), null, now.AddMilliseconds(messageCount));
                            await conversations.AddMessageAsync(item, cancellationToken).ConfigureAwait(false);
                            messageCount++;
                        }
                    }
                }
            }

            var note = $"Imported {conversationCount} conversations and {messageCount} messages from LocalCode.";
            await MarkCompletedAsync(note, cancellationToken).ConfigureAwait(false);
            return new(true, conversationCount > 0, conversationCount, messageCount, note);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return new(true, false, 0, 0, $"Legacy state was not imported: {exception.Message}");
        }
    }

    /// <summary>
    /// Reports whether completed async applies to the current state.
    /// </summary>
    private async Task<bool> HasCompletedAsync(CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM migration_log WHERE key=$key;";
        command.Parameters.AddWithValue("$key", MigrationKey);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    /// <summary>
    /// Performs mark completed asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task MarkCompletedAsync(string note, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR REPLACE INTO migration_log(key,completed_at,note) VALUES($key,$completedAt,$note);";
        command.Parameters.AddWithValue("$key", MigrationKey);
        command.Parameters.AddWithValue("$completedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$note", note);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs the parse or stable step owned by this component.
    /// </summary>
    private static Guid ParseOrStable(string value, string scope) => Guid.TryParse(value, out var id) ? id : GuidUtility.FromStableName($"legacy.{scope}.{value}");
    /// <summary>
    /// Retrieves string for the current operation.
    /// </summary>
    private static string? GetString(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    /// <summary>
    /// Retrieves boolean for the current operation.
    /// </summary>
    private static bool GetBoolean(JsonElement element, string property) => element.TryGetProperty(property, out var value) && (value.ValueKind is JsonValueKind.True or JsonValueKind.False) && value.GetBoolean();
}
