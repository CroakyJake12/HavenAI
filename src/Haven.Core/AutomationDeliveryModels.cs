namespace Haven.Core;

public enum AutomationDeliveryKind
{
    ConditionMet = 0,
    Failed = 1
}

public sealed record AutomationDelivery(
    Guid Id,
    Guid AutomationId,
    string AutomationName,
    AutomationDeliveryKind Kind,
    string Title,
    string Message,
    DateTimeOffset CreatedAt);
