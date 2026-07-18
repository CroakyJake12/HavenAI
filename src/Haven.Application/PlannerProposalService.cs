/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/PlannerProposalService.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns PlannerProposalService. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Globalization;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Represents planner proposal service and keeps its related state and behavior together.
/// </summary>
public sealed class PlannerProposalService(IPlannerRepository repository) : IPlannerProposalService
{
    /// <summary>
    /// Gets or updates tool definition, the bindable or domain state represented by this property.
    /// </summary>
    public OllamaToolDefinition ToolDefinition { get; } = new(
        "planner_propose_changes",
        "Draft planner task or event changes for the user to review. Nothing is changed until the user applies the proposal.",
        new Dictionary<string, object>
        {
            ["summary"] = new { type = "string", description = "A short human-readable summary." },
            ["changes"] = new
            {
                type = "array",
                maxItems = 50,
                items = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["kind"] = new { type = "string", @enum = Enum.GetNames<PlannerChangeKind>() },
                        ["entityId"] = new { type = new[] { "string", "null" } },
                        ["description"] = new { type = "string" },
                        ["payload"] = new { type = "object" }
                    },
                    required = new[] { "kind", "description", "payload" }
                }
            }
        },
        ["summary", "changes"]);

    /// <summary>
    /// Performs the parse tool call step owned by this component.
    /// </summary>
    public PlannerChangeProposal ParseToolCall(IReadOnlyDictionary<string, JsonElement> arguments)
    {
        var summary = arguments.TryGetValue("summary", out var summaryElement) ? summaryElement.GetString()?.Trim() ?? string.Empty : string.Empty;
        if (!arguments.TryGetValue("changes", out var changesElement) || changesElement.ValueKind != JsonValueKind.Array)
            throw new JsonException("The planner proposal must contain a changes array.");

        var changes = new List<PlannerProposedChange>();
        foreach (var element in changesElement.EnumerateArray())
        {
            var kindText = element.GetProperty("kind").GetString();
            if (!Enum.TryParse<PlannerChangeKind>(kindText, true, out var kind))
                throw new JsonException($"Unknown planner change kind '{kindText}'.");
            Guid? entityId = null;
            if (element.TryGetProperty("entityId", out var idElement) && idElement.ValueKind == JsonValueKind.String)
            {
                if (!Guid.TryParse(idElement.GetString(), out var parsedId)) throw new JsonException("entityId must be a GUID.");
                entityId = parsedId;
            }
            var description = element.TryGetProperty("description", out var descriptionElement)
                ? descriptionElement.GetString()?.Trim() ?? string.Empty
                : string.Empty;
            var payload = element.TryGetProperty("payload", out var payloadElement) ? payloadElement.GetRawText() : "{}";
            changes.Add(new PlannerProposedChange(Guid.NewGuid(), kind, entityId, payload, description));
        }

        return new PlannerChangeProposal(Guid.NewGuid(), summary, changes, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Validates this member before it crosses the next trust or persistence boundary.
    /// </summary>
    public PlannerProposalValidation Validate(PlannerChangeProposal proposal)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(proposal.Summary)) errors.Add("A proposal summary is required.");
        if (proposal.Changes.Count == 0) errors.Add("The proposal contains no changes.");
        if (proposal.Changes.Count > 50) errors.Add("A proposal cannot contain more than 50 changes.");

        foreach (var change in proposal.Changes)
        {
            if (string.IsNullOrWhiteSpace(change.Description)) errors.Add($"{change.Kind} requires a description.");
            if (change.Kind is not PlannerChangeKind.CreateTask and not PlannerChangeKind.CreateEvent && change.EntityId is null)
                errors.Add($"{change.Kind} requires an entity ID.");
            try
            {
                using var document = JsonDocument.Parse(change.PayloadJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object) errors.Add($"{change.Kind} payload must be an object.");
                else ValidatePayload(change.Kind, document.RootElement, errors);
            }
            catch (JsonException)
            {
                errors.Add($"{change.Kind} payload is not valid JSON.");
            }
        }

        return errors.Count == 0 ? PlannerProposalValidation.Valid : new(false, errors);
    }

    /// <summary>
    /// Performs apply async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task ApplyAsync(PlannerChangeProposal proposal, CancellationToken cancellationToken)
    {
        var validation = Validate(proposal);
        if (!validation.IsValid) throw new InvalidOperationException(string.Join(" ", validation.Errors));
        await repository.ApplyProposalAsync(proposal, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Validates payload before it crosses the next trust or persistence boundary.
    /// </summary>
    private static void ValidatePayload(PlannerChangeKind kind, JsonElement payload, List<string> errors)
    {
        if (kind is PlannerChangeKind.CreateTask or PlannerChangeKind.UpdateTask)
        {
            if (kind == PlannerChangeKind.CreateTask && (!payload.TryGetProperty("collectionId", out var collection) || !Guid.TryParse(collection.GetString(), out _)))
                errors.Add("CreateTask requires a valid collectionId.");
            if (kind == PlannerChangeKind.CreateTask && (!payload.TryGetProperty("title", out var createTitle) || string.IsNullOrWhiteSpace(createTitle.GetString())))
                errors.Add("CreateTask requires a title.");
            else if (payload.TryGetProperty("title", out var title) && string.IsNullOrWhiteSpace(title.GetString())) errors.Add("Task title cannot be empty.");
            ValidateDate(payload, "startsAt", errors);
            ValidateDate(payload, "dueAt", errors);
            ValidateDate(payload, "reminderAt", errors);
        }
        else if (kind is PlannerChangeKind.CreateEvent or PlannerChangeKind.UpdateEvent)
        {
            if (kind == PlannerChangeKind.CreateEvent && (!payload.TryGetProperty("calendarId", out var calendar) || !Guid.TryParse(calendar.GetString(), out _)))
                errors.Add("CreateEvent requires a valid calendarId.");
            if (kind == PlannerChangeKind.CreateEvent && (!payload.TryGetProperty("title", out var createTitle) || string.IsNullOrWhiteSpace(createTitle.GetString())))
                errors.Add("CreateEvent requires a title.");
            else if (payload.TryGetProperty("title", out var title) && string.IsNullOrWhiteSpace(title.GetString())) errors.Add("Event title cannot be empty.");
            ValidateDate(payload, "startsAt", errors, required: kind == PlannerChangeKind.CreateEvent);
            ValidateDate(payload, "endsAt", errors, required: kind == PlannerChangeKind.CreateEvent);
            if (payload.TryGetProperty("startsAt", out var starts) && payload.TryGetProperty("endsAt", out var ends)
                && TryDate(starts, out var startsAt) && TryDate(ends, out var endsAt) && endsAt <= startsAt)
                errors.Add("An event must end after it starts.");
        }
    }

    /// <summary>
    /// Validates date before it crosses the next trust or persistence boundary.
    /// </summary>
    private static void ValidateDate(JsonElement payload, string name, List<string> errors, bool required = false)
    {
        if (!payload.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            if (required) errors.Add($"{name} is required.");
            return;
        }
        if (!TryDate(value, out _)) errors.Add($"{name} must be an ISO-8601 timestamp.");
    }

    /// <summary>
    /// Attempts to date and reports the result without using failure for normal control flow.
    /// </summary>
    private static bool TryDate(JsonElement value, out DateTimeOffset result) =>
        DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out result);
}
