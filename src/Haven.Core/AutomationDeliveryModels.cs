/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Core/AutomationDeliveryModels.cs, in the dependency-free Core layer, where shared domain models and rules live.
 * What: This file owns AutomationDeliveryKind, AutomationDelivery. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: This code stays free of UI and storage dependencies so the same rule or data shape can be reused and tested everywhere.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

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
