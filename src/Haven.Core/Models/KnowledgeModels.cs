namespace Haven.Core;

public enum KnowledgeCategory
{
    WorldKnowledge = 0,
    LearnMe = 1,
    Environment = 2,
    Project = 3,
    ApiBank = 4,
    Experience = 5,
    PreparedPack = 6,
    ErrorKnowledge = 7
}

public enum KnowledgePrivacyClass
{
    Normal = 0,
    Private = 1,
    Sensitive = 2,
    NeverLearn = 3
}

public enum BackgroundLearningMode
{
    Minimal = 0,
    Balanced = 1,
    Proactive = 2,
    Maximum = 3
}

public enum KnowledgeFreshnessClass
{
    Durable = 0,
    Changing = 1,
    ShortLived = 2
}

public enum KnowledgeRecordStatus
{
    Active = 0,
    Corrected = 1,
    Rejected = 2,
    Superseded = 3
}

public enum KnowledgeOrigin
{
    Inferred = 0,
    Explicit = 1,
    Imported = 2,
    Prepared = 3
}

public static class KnowledgeStorageLimits
{
    public const long BackgroundLearningBytes = 512L * 1024 * 1024;
    public const long ApiBankBytes = 1024L * 1024 * 1024;
}

public sealed record KnowledgeSource(
    string SourceId,
    string Title,
    string SourceType,
    string? Publisher,
    string? Url,
    DateTimeOffset ObtainedAt,
    DateTimeOffset? LastVerifiedAt,
    DateTimeOffset? ExpiresAt,
    string TrustLevel = "Unclassified");

public sealed record KnowledgeRecord(
    Guid Id,
    KnowledgeCategory Category,
    string Topic,
    string Title,
    string Summary,
    KnowledgePrivacyClass PrivacyClass,
    double Confidence,
    bool IsPinned,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ExpiresAt,
    string LearnedBecause,
    IReadOnlyList<KnowledgeSource> Sources,
    KnowledgeFreshnessClass Freshness = KnowledgeFreshnessClass.Durable,
    DateTimeOffset? LastConfirmedAt = null,
    string Scope = "global",
    KnowledgeRecordStatus Status = KnowledgeRecordStatus.Active,
    KnowledgeOrigin Origin = KnowledgeOrigin.Inferred,
    string? UserCorrection = null,
    Guid? SupersedesId = null);

public sealed record ApiBankRecord(
    Guid Id,
    string Application,
    string ApiName,
    string Version,
    string DocumentationUrl,
    string ActionsJson,
    string Authentication,
    bool RequiresInternet,
    bool RequiresCredentials,
    decimal? CostPerRequest,
    string AlternativesJson,
    string? Deprecation,
    DateTimeOffset LastCheckedAt,
    string DocumentationHash,
    string InputsJson = "[]",
    string OutputsJson = "[]",
    string ScopesJson = "[]",
    string RateLimits = "",
    string Pricing = "",
    string CapabilityNotes = "",
    string Limitations = "",
    string OfflineQueuePolicy = "",
    bool IsPinned = false,
    string SourceUrl = "");

public sealed record KnowledgeStorageSnapshot(
    long KnowledgeBytes,
    long KnowledgeLimitBytes,
    int KnowledgeCount,
    int KnowledgePinnedCount,
    long ApiBankBytes,
    long ApiBankLimitBytes,
    int ApiBankCount,
    int ApiBankPinnedCount);

public sealed record KnowledgeCleanupResult(
    int KnowledgeRemoved,
    int ApiBankRemoved,
    long KnowledgeBytesFreed,
    long ApiBankBytesFreed,
    string Summary);
