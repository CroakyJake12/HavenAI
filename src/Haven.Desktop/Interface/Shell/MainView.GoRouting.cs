using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Go;
using Haven.Desktop.Views.Shell.TopRail;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private async Task RouteGoSubmissionAsync(GoPage page, string instruction)
    {
        var snapshot = page.TakeAttachments();
        try
        {
            var projects = (await _containers.GetByModeAsync(HavenMode.Studio, CancellationToken.None))
                .Where(project => !project.IsArchived)
                .ToArray();
            var decision = GoRouteIntentPolicy.Resolve(
                instruction,
                new GoRoutingContext(snapshot.Files, projects.Select(project => project.Name).ToArray()));

            switch (decision.Destination)
            {
                case GoRouteDestination.Chat:
                    await OpenNewChatAsync(decision.Instruction, initialAttachments: snapshot);
                    return;

                case GoRouteDestination.Project:
                    await RouteGoProjectAsync(page, decision, snapshot, projects);
                    return;

                case GoRouteDestination.App:
                    await RouteGoAppAsync(page, decision, snapshot);
                    return;

                default:
                    RestoreGoTask(page, instruction, snapshot, decision.Clarification ?? "Tell Haven where you want this task to go.");
                    return;
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            RestoreGoTask(page, instruction, snapshot, "Haven could not route that task: " + exception.Message);
        }
    }

    private async Task RouteGoProjectAsync(
        GoPage page,
        GoRouteDecision decision,
        TaskAttachmentSnapshot snapshot,
        IReadOnlyList<ContainerDefinition> projects)
    {
        var project = projects.FirstOrDefault(item =>
            item.Name.Equals(decision.ProjectName, StringComparison.OrdinalIgnoreCase));
        if (project is null)
        {
            RestoreGoTask(page, decision.Instruction, snapshot, "That project is not available anymore. Choose another project and try again.");
            return;
        }

        ActivateProject(project);
        var chat = await OpenScopedNewChatPageAsync(
            HavenMode.Studio,
            project.Id,
            $"go-project-{project.Id:N}-{Guid.NewGuid():N}",
            project.Name + " chat",
            HavenSurface.Studio);
        chat.AttachSnapshot(snapshot);
        chat.Submit(decision.Instruction);
    }

    private async Task RouteGoAppAsync(GoPage page, GoRouteDecision decision, TaskAttachmentSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(decision.TargetKey))
        {
            RestoreGoTask(page, decision.Instruction, snapshot, "Haven could not identify the destination App.");
            return;
        }

        if (decision.TargetKey.Equals("spaces", StringComparison.OrdinalIgnoreCase))
        {
            await OpenSpacesAsync();
            if (snapshot.Files.Count > 0 || snapshot.Apps.Count > 0 || snapshot.Capabilities.Count > 0)
            {
                page.RestorePendingTask(decision.Instruction, snapshot);
                _notifications.Show("Go", "Opened Spaces. Your attached Go context is still available when you return.", ToastKind.Info, TimeSpan.FromSeconds(5));
            }
            return;
        }

        var app = await _modeRegistry.GetModeByKeyAsync(decision.TargetKey, CancellationToken.None);
        if (app is null)
        {
            RestoreGoTask(page, decision.Instruction, snapshot, $"The {decision.TargetKey} App is not registered in this profile.");
            return;
        }

        var route = HavenAppRoutePolicy.Resolve(app);
        if (route.Kind == HavenAppRouteKind.ModeWorkspace && !IsDocumentWorkspace(app.Key))
        {
            var chat = CreateNewChatPage();
            chat.ConfigureMode(app);
            await ConfigureAddMenuAsync(chat);
            chat.AttachSnapshot(snapshot);
            AddOrSelectTab(
                $"go-app-{app.Key}-{Guid.NewGuid():N}",
                app.Name,
                chat,
                true,
                route.Surface,
                forceNewTab: true);
            ApplyShellVisualState();
            chat.Submit(decision.Instruction);
            await _modeUsage.RecordUsageAsync(app.Id, DateOnly.FromDateTime(DateTime.Today), CancellationToken.None);
            return;
        }

        if (route.Kind == HavenAppRouteKind.BaseMode && app.BaseMode == HavenMode.Chat)
        {
            await OpenNewChatAsync(decision.Instruction, forceNewTab: true, initialAttachments: snapshot);
            await _modeUsage.RecordUsageAsync(app.Id, DateOnly.FromDateTime(DateTime.Today), CancellationToken.None);
            return;
        }

        if (route.Kind == HavenAppRouteKind.BaseMode && app.BaseMode == HavenMode.Tasks)
        {
            var task = await OpenScopedNewChatPageAsync(
                HavenMode.Tasks,
                null,
                $"go-task-{Guid.NewGuid():N}",
                "Task",
                HavenSurface.Tasks);
            task.AttachSnapshot(snapshot);
            task.Submit(decision.Instruction);
            await _modeUsage.RecordUsageAsync(app.Id, DateOnly.FromDateTime(DateTime.Today), CancellationToken.None);
            return;
        }

        await LaunchAppAsync(app, false);
        RestoreGoTask(
            page,
            decision.Instruction,
            snapshot,
            $"Opened {app.Name}. This App does not expose direct Go task handoff yet, so your instruction and attachments remain in Go.");
    }

    private void RestoreGoTask(GoPage page, string instruction, TaskAttachmentSnapshot snapshot, string message)
    {
        page.RestorePendingTask(instruction, snapshot);
        _notifications.Show("Go", message, ToastKind.Warning, TimeSpan.FromSeconds(6));
    }
}
