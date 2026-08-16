namespace Haven.Desktop.Views.Shell.NativePresentation;

/// <summary>
/// Identifies the New Haven destination that owns a selected legacy presentation surface.
/// The policy is pure so route regressions can be tested without creating Avalonia controls.
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
/// Maps concrete legacy page and view-model names to their native New Haven presentation.
/// Project detail pages intentionally remain outside this policy.
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

        if (surfaceName.Equals("WorkspaceHomeView", StringComparison.OrdinalIgnoreCase) ||
            surfaceName.Equals("WorkspaceHomePageViewModel", StringComparison.OrdinalIgnoreCase) ||
            surfaceName.Equals("StudioHomePage", StringComparison.OrdinalIgnoreCase) ||
            surfaceName.Equals("ProjectsPage", StringComparison.OrdinalIgnoreCase) ||
            surfaceName.Equals("ProjectBrowserView", StringComparison.OrdinalIgnoreCase) ||
            dataContextName.Equals("WorkspaceHomeViewModel", StringComparison.OrdinalIgnoreCase) ||
            dataContextName.Equals("WorkspaceHomePageViewModel", StringComparison.OrdinalIgnoreCase) ||
            dataContextName.Equals("StudioHomePageViewModel", StringComparison.OrdinalIgnoreCase) ||
            dataContextName.Equals("ProjectBrowserViewModel", StringComparison.OrdinalIgnoreCase))
        {
            return NativePresentationDestination.Projects;
        }

        if (surfaceName.Equals("NewChatPage", StringComparison.OrdinalIgnoreCase))
        {
            return NativePresentationDestination.None;
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
