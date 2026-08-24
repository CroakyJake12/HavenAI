using System.Text.Json.Serialization;

namespace Haven.Core;

public enum HavenTaskStatus
{
    Draft = 0,
    AwaitingAgent = 1,
    Claimed = 2,
    InProgress = 3,
    Blocked = 4,
    AwaitingUser = 5,
    Completed = 6,
    Failed = 7,
    Cancelled = 8,
    Expired = 9
}

/// <summary>An opaque display locator. It is deliberately not an authentication credential.</summary>
public readonly record struct HavenTaskLocator
{
    [JsonConstructor]
    public HavenTaskLocator(string value)
    {
        if (!IsValid(value))
            throw new ArgumentException("Task locator must use the HAV- format.", nameof(value));
        Value = value.ToUpperInvariant();
    }

    public string Value { get; }
    public override string ToString() => Value;

    public static bool TryParse(string? value, out HavenTaskLocator locator)
    {
        locator = default;
        if (!IsValid(value)) return false;
        var normalized = value!.ToUpperInvariant();
        if (normalized.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '-'))) return false;
        try { locator = new HavenTaskLocator(normalized); return true; }
        catch (ArgumentException) { return false; }
    }

    private static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.ToUpperInvariant().Split('-');
        return parts is ["HAV", { Length: 4 }, { Length: 4 }, { Length: 4 }]
               && parts.Skip(1).SelectMany(part => part).All(character => character is >= 'A' and <= 'Z' or >= '2' and <= '9');
    }
}

public sealed record ExternalAgentTask(
    Guid Id,
    HavenTaskLocator Locator,
    Guid OwnerUserId,
    Guid? WorkspaceId,
    Guid? ProjectId,
    Guid? SourceTabId,
    Guid? ExecutionId,
    string AgentPluginId,
    string Title,
    string Instruction,
    string ContextReferenceJson,
    string ExpectedOutput,
    HavenTaskStatus Status,
    string? ClaimedBy,
    string? LeaseTokenHash,
    DateTimeOffset? LeaseExpiresAt,
    string? IdempotencyKey,
    string? SafeProgress,
    string? SafeResult,
    string? SafeError,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ExpiresAt);

public sealed record ExternalAgentPrincipal(
    Guid UserId,
    IReadOnlySet<Guid> WorkspaceIds,
    IReadOnlySet<Guid> ProjectIds,
    string ClientId);

public sealed record ExternalTaskClaim(
    ExternalAgentTask Task,
    string LeaseToken,
    DateTimeOffset LeaseExpiresAt);
