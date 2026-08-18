using Haven.Application;
using Haven.Desktop.Views.Shell.NativePresentation;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private NativeChatSidebar? _nativeChatSidebar;
    private StudyAssignmentsSidebarCoordinator? _studyAssignmentsSidebar;

    private void InitialiseNativeChatSidebar()
    {
        _nativeChatSidebar = new NativeChatSidebar(
            _conversations,
            _containers,
            OpenNativeConversationAsync,
            StartNativeConversationAsync,
            OpenChatGroupAsync,
            SwitchNativeChatModeAsync);

        var services = App.Services ?? throw new InvalidOperationException("Haven services are unavailable while constructing the Study sidebar.");
        _studyAssignmentsSidebar = new StudyAssignmentsSidebarCoordinator(
            _nativeChatSidebar,
            _containers,
            services.GetRequiredService<IStudyPlannerService>(),
            OpenPlan);
        NativeSidebarHost.Content = _nativeChatSidebar;
    }
}
