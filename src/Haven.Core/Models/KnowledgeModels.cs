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
    IReadOnlyList<KnowledgeSource> Sources);

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
    string DocumentationHash);
