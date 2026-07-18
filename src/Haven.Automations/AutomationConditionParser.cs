/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Automations/AutomationConditionParser.cs, in the Automations layer, which parses schedules and runs durable background actions.
 * What: This file owns AutomationConditionParser. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

namespace Haven.Automations;

/// <summary>
/// Represents automation condition parser and keeps its related state and behavior together.
/// </summary>
public static class AutomationConditionParser
{
    /// <summary>
    /// Performs the parse step owned by this component.
    /// </summary>
    public static AutomationConditionResult Parse(string? response) =>
        AutomationRunner.ParseConditionResult(response);
}
