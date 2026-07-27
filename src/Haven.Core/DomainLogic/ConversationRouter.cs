namespace Haven.Core;

/// <summary>
/// The surface from which a user started the current request.
/// </summary>
public enum HavenRoutingOrigin
{
    Go,
    Chat,
    Mode,
    Project
}

public enum HavenRoutingDestinationKind
{
    Chat,
    Mode,
    Project,
    ProjectCreator,
    Clarify
}

/// <summary>
/// Preserves the source conversation and relevant context when Go or Chat activates another surface.
/// Destination and transfer reason are stamped by <see cref="How=HavenConversationRouter"/> so receivers
/// do not have to infer why the hand-off occurred.
/// </summary>
public sealed record HavenContextTransfer(
    Guid? SourceConversationId,
    string UserRequest,
    IReadOnlyList<Guid> MessageIds,
    IReadOnlyList<Guid> AttachmentIds,
    IReadOnlyList<Guid> AgentIds,
    IReadOnlyList<Guid> PluginIds,
    IReadOnlyList<string> Instructions,
    string? ContextSummary,
    string? ReturnRoute,
    HavenRoutingDestinationKind Destination = HavenRoutingDestinationKind.Chat,
    string TransferReason = "");

public sealed record HavenRoutingRequest(
    HavenRoutingOrigin Origin,
    string UserRequest,
    bool IsExplicitNavigation,
    bool IsProjectRequest,
    bool HasStrongModeIntent,
    string? ModeKey,
    bool IsAmbiguous,
    HavenContextTransfer Transfer);

public sealed record HavenRoutingDecision(
    HavenRoutingDestinationKind Destination,
    string? TargetKey,
    bool KeepChatAsPrimarySurface,
    HavenContextTransfer Transfer);

/// <summary>
/// Enforces the product boundary: Go navigates, Chat converses and hosts capabilities,
/// and Projects own persistent workspaces.
/// </summary>
public static class HavenConversationRouter
{
    public static HavenRoutingDecision Route(HavenRoutingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Transfer);

        if (request.IsProjectRequest)
        {
            var destination = RequestsCreation(request.UserRequest)
                ? HavenRoutingDestinationKind.ProjectCreator
                : HavenRoutingDestinationKind.Project;

            return CreateDecision(
                request,
                destination,
                request.ModeKey,
                keepChatAsPrimarySurface: false,
                destination == HavenRoutingDestinationKind.ProjectCreator
                    ? "The request explicitly creates a project."
                    : "The request targets an existing project.");
        }

        if (request.Origin == HavenRoutingOrigin.Chat)
        {
            return CreateDecision(
                request,
                HavenRoutingDestinationKind.Chat,
                request.ModeKey,
                keepChatAsPrimarySurface: true,
                "Regular Chat keeps non-project capabilities in the current conversation.");
        }

        if (request.Origin == HavenRoutingOrigin.Go)
        {
            if (request.IsExplicitNavigation || request.HasStrongModeIntent)
            {
                return CreateDecision(
                    request,
                    HavenRoutingDestinationKind.Mode,
                    request.ModeKey,
                    keepChatAsPrimarySurface: false,
                    request.IsExplicitNavigation
                        ? "The request explicitly navigates to a mode."
                        : "The request has strong mode intent.");
            }

            if (request.IsAmbiguous)
            {
                return CreateDecision(
                    request,
                    HavenRoutingDestinationKind.Clarify,
                    targetKey: null,
                    keepChatAsPrimarySurface: false,
                    "The request is ambiguous and requires clarification before navigation.");
            }

            return CreateDecision(
                request,
                HavenRoutingDestinationKind.Chat,
                targetKey: null,
                keepChatAsPrimarySurface: true,
                "A generic Go request is handed to Chat for the response.");
        }

        var fallbackDestination = request.Origin == HavenRoutingOrigin.Project
            ? HavenRoutingDestinationKind.Project
            : HavenRoutingDestinationKind.Mode;

        return CreateDecision(
            request,
            fallbackDestination,
            request.ModeKey,
            keepChatAsPrimarySurface: false,
            request.Origin == HavenRoutingOrigin.Project
                ? "The request remains in its project workspace."
                : "The request remains in its active mode.");
    }

    private static HavenRoutingDecision CreateDecision(
        HavenRoutingRequest request,
        HavenRoutingDestinationKind destination,
        string? targetKey,
        bool keepChatAsPrimarySurface,
        string transferReason)
    {
        var transfer = request.Transfer with
        {
            Destination = destination,
            TransferReason = transferReason
        };

        return new HavenRoutingDecision(destination, targetKey, keepChatAsPrimarySurface, transfer);
    }

    private static bool RequestsCreation(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var text = value.Trim();
        return text.StartsWith("create a project", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("new project", StringComparison.OrdinalIgnoreCase);
    }
}
