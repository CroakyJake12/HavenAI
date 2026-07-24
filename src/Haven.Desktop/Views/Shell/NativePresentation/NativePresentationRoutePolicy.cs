namespace Haven.Desktop.Views.Shell.NativePresentation;

/// <summary>
/// Identifies the New Haven destination that must own a selected legacy
/// surface. The policy is pure so route regressions can be tested without
/// creating Avalonia controls or starting platform services.
/// </summary>
public enum NativePresentationDestination
{
    None = 0,
    Chat = 1,
    ChatCallWidget = 2,
    Projects = 3,
    ProjectCreator = 4
}

/// <summary>
/// Maps the existing shell's selected surface to the native New Haven
/// presentation. Matching is intentionally limited to concrete page and
/// view-model names so unrelated controls are never replaced.
/// </summary>
public static class NativePresentationRoutePolicy
{
    public static NativePresentationDestination Classify(
        string? surfaceName,
        string? dataContextName)
    {
        surfaceName ??= string.Empty;
        dataContextName ??= string.Empty;

        if (surfaceName.Equals("CallPage", StringComparison.OrdinalIgnoreCase) ||
            surfaceName.Equals("CallView", StringComparison.OrdinalIgnoreCase) ||
            surfaceName.Contains("StandaloneCall", StringComparison.OrdinalIgnoreCase) ||
            dataContextName.Equals("CallPageViewModel", StringComparison.OrdinalIgnoreCase))
        {
            return NativePresentationDestination.ChatCallWidget;
        }

        if (surfaceName.Contains("ProjectCreator", StringComparison.OrdinalIgnoreCase) ||
            dataContextName.Contains("ProjectCreator", StringComparison.OrdinalIgnoreCase))
        {
            return NativePresentationDestination.ProjectCreator;
        }

        if (surfaceName.Equals("StudioProjectPage", StringComparison.OrdinalIgnoreCase) ||
            surfaceName.Equals("WorkspaceHomeView", StringComparison.OrdinalIgnoreCase) ||
            surfaceName.Contains("ProjectBrowser", StringComparison.OrdinalIgnoreCase) ||
            dataContextName.Contains("WorkspaceHome", StringComparison.OrdinalIgnoreCase) ||
            dataContextName.Contains("StudioHome", StringComparison.OrdinalIgnoreCase) ||
            dataContextName.Contains("ProjectBrowser", StringComparison.OrdinalIgnoreCase))
        {
            return NativePresentationDestination.Projects;
        }

        if (surfaceName.Equals("ChatView", StringComparison.OrdinalIgnoreCase) ||
            surfaceName.Contains("ChatPage", StringComparison.OrdinalIgnoreCase) ||
            dataContextName.Contains("ChatPage", StringComparison.OrdinalIgnoreCase) ||
            dataContextName.Contains("Conversation", StringComparison.OrdinalIgnoreCase))
        {
            return NativePresentationDestination.Chat;
        }

        return NativePresentationDestination.None;
    }
}
