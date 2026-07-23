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
    string? ReturnRoute);

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
    bool KeepChatAs PrimarySurface,
    HavenContextTransfer Transfer);

/// <summary>
/// Enforces the product boundary: Go navigates, Chat converses and hosts capabilities, and Projects own persistent workspaces.
/// </summary>
public static class HavenConversationRouter
{
    public static HavenRoutingDecision Route(HavenRoutingRequest request)
    {
        if (request.IsProjectRequest)
        {
            var destination = RequestsCreation(request.UserRequest)
                ? HavenRoutingDestinationKind.ProjectCreator
                : HavenRoutingDestinationKind.Project;
            return new(destination, request.ModeKey, false, request.Transfer);
        }

        if (request.Origin == HavenRoutingOrigin.Chat)
        {
            // Regular Chat keeps non-project capabilities inside the current conversation.
            return new(HavenRoutingDestinationKind.Chat, request.ModeKey, true, request.Transfer);
        }

        if (request.Origin == HavenRoutingOrigin.Go)
        {
            if (request.IsExplicitNavigation || request.HasStrongModeIntent)
                return new(HavenRoutingDestinationKind.Mode, request.ModeKey, false, request.Transfer);

            if (request.IsAmbiguous)
                return new(HavenRoutingDestinationKind.Clarify, null, false, request.Transfer);

            return new(HavenRoutingDestinationKind.Chat, null, true, request.Transfer);
        }

        return new(request.Origin == HavenRoutingOrigin.Project
            ? HavenRoutingDestinationKind.Project
            : HavenRoutingDestinationKind.Mode, request.ModeKey, false, request.Transfer);
    }

    private static bool RequestsCreation(string value)
    {
        var text = value.Trim();
        return text.StartsWith("create a project", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("new project", StringComparison.OrdinalIgnoreCase);
    }
}
