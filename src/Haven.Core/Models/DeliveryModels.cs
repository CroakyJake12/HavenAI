// Automation delivery records.

namespace Haven.Core;

/// <summary>
/// Lists the supported automation delivery kind values used to make state explicit and type-safe.
/// </summary>
public enum AutomationDeliveryKind
{
    ConditionMet = 0,
    Failed = 1
}

/// <summary>
/// Represents automation delivery and keeps its related state and behavior together.
/// </summary>
public sealed record AutomationDelivery(
    Guid Id,
    Guid AutomationId,
    string AutomationName,
    AutomationDeliveryKind Kind,
    string Title,
    string Message,
    DateTimeOffset CreatedAt);
