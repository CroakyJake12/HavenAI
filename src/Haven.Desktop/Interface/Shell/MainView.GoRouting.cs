using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Views.Pages.Go;
using Haven.Desktop.Views.Shell.TopRail;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private async Task RouteGoSubmissionAsync(GoPage page, string instruction)
    {
        var taskContext = page.TakeTaskSnapshot();
        try
        {
            var projects = (await _containers.GetByModeAsync(HavenMode.Studio, CancellationToken.None))
                .Where(project => !project.IsArchived)
                .ToArray();
            var decision = GoRouteIntentPolicy.Resolve(
                instruction,
                new GoRoutingContext(taskContext.Attachments.Files, projects.Select(project => project.Name).ToArray()));

            switch (decision.Destination)
            {
                case GoRouteDestination.Chat:
                    var chat = await OpenScopedNewChatPageAsync(
                        HavenMode.Chat,
                        null,
                        $"go-chat-{Guid.NewGuid():N}",
                        "Chat",
                        HavenSurface.Chat);
                    ApplyGoTaskContext(chat, taskContext);
                    chat.Submit(decision.Instruction);
                    return;

                case GoRouteDestination.Project:
                    await RouteGoProjectAsync(page, decision, taskContext, projects);
                    return;

                case GoRouteDestination.App:
                    await RouteGoAppAsync(page, decision, taskContext);
                    return;

                default:
                    RestoreGoTask(page, instruction, taskContext, decision.Clarification ?? "Tell Haven where you want this task to go.");
                    return;
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            RestoreGoTask(page, instruction, taskContext, "Haven could not route that task: " + exception.Message);
        }
    }

    private async Task RouteGoProjectAsync(
        GoPage page,
        GoRouteDecision decision,
        GoTaskSnapshot taskContext,
        IReadOnlyList<ContainerDefinition> projects)
    {
        var project = projects.FirstOrDefault(item =>
            item.Name.Equals(decision.ProjectName, StringComparison.OrdinalIgnoreCase));
        if (project is null)
        {
            RestoreGoTask(page, decision.Instruction, taskContext, "That project is not available anymore. Choose another project and try again.");
            return;
        }

        ActivateProject(project);
        var chat = await OpenScopedNewChatPageAsync(
            HavenMode.Studio,
            project.Id,
            $"go-project-{project.Id:N}-{Guid.NewGuid():N}",
            project.Name + " chat",
            HavenSurface.Studio);
        ApplyGoTaskContext(chat, taskContext);
        chat.Submit(decision.Instruction);
    }

    private async Task RouteGoAppAsync(GoPage page, GoRouteDecision decision, GoTaskSnapshot taskContext)
    {
        if (string.IsNullOrWhiteSpace(decision.TargetKey))
        {
            RestoreGoTask(page, decision.Instruction, taskContext, "Haven could not identify the destination App.");
            return;
        }

        if (decision.TargetKey.Equals("spaces", StringComparison.OrdinalIgnoreCase))
        {
            await OpenSpacesAsync();
            if (taskContext.Files.Count > 0 || taskContext.Apps.Count > 0 || taskContext.Capabilities.Count > 0)
            {
                page.RestorePendingTask(decision.Instruction, taskContext);
                _notifications.Show("Go", "Opened Spaces. Your attached Go context is still available when you return.", ToastKind.Info, TimeSpan.FromSeconds(5));
            }
            return;
        }

        var app = await _modeRegistry.GetModeByKeyAsync(decision.TargetKey, CancellationToken.None);
        if (app is null)
        {
            RestoreGoTask(page, decision.Instruction, taskContext, $"The {decision.TargetKey} App is not registered in this profile.");
            return;
        }

        var route = HavenAppRoutePolicy.Resolve(app);
        if (route.Kind == HavenAppRouteKind.ModeWorkspace && !IsDocumentWorkspace(app.Key))
        {
            var chat = CreateNewChatPage();
            chat.ConfigureMode(app);
            await ConfigureAddMenuAsync(chat);
            ApplyGoTaskContext(chat, taskContext);
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

        if (route.Kind == HavenAppRouteKind.Translate)
        {
            await OpenTranslateAsync(true, decision.Instruction, taskContext.Files);
            await _modeUsage.RecordUsageAsync(app.Id, DateOnly.FromDateTime(DateTime.Today), CancellationToken.None);
            return;
        }

        if (route.Kind == HavenAppRouteKind.BaseMode && app.BaseMode == HavenMode.Chat)
        {
            var chat = await OpenScopedNewChatPageAsync(
                HavenMode.Chat,
                null,
                $"go-app-chat-{Guid.NewGuid():N}",
                app.Name,
                HavenSurface.Chat);
            ApplyGoTaskContext(chat, taskContext);
            chat.Submit(decision.Instruction);
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
            ApplyGoTaskContext(task, taskContext);
            task.Submit(decision.Instruction);
            await _modeUsage.RecordUsageAsync(app.Id, DateOnly.FromDateTime(DateTime.Today), CancellationToken.None);
            return;
        }

        await LaunchAppAsync(app, false);
        RestoreGoTask(
            page,
            decision.Instruction,
            taskContext,
            $"Opened {app.Name}. This App does not expose direct Go task handoff yet, so your instruction and context remain in Go.");
    }

    private static void ApplyGoTaskContext(NewChatPage chat, GoTaskSnapshot taskContext)
    {
        chat.AttachSnapshot(taskContext.Attachments);
        if (taskContext.Agent is not null)
            chat.ApplyAddSelection(new AddMenuSelection(AddMenu.AddMenuAction.Agent, taskContext.Agent));
        foreach (var instruction in taskContext.Instructions)
            chat.ApplyAddSelection(new AddMenuSelection(AddMenu.AddMenuAction.Instruction, instruction));
        if (taskContext.ActionMode is ChatActionMode actionMode)
            chat.ApplyAddSelection(new AddMenuSelection(AddMenu.AddMenuAction.AllowActions, actionMode));
        if (taskContext.VisualResponseMode is GenerativeUiResponseMode visualMode)
            chat.ApplyAddSelection(new AddMenuSelection(AddMenu.AddMenuAction.VisualResponses, visualMode));
    }

    private void RestoreGoTask(GoPage page, string instruction, GoTaskSnapshot taskContext, string message)
    {
        page.RestorePendingTask(instruction, taskContext);
        _notifications.Show("Go", message, ToastKind.Warning, TimeSpan.FromSeconds(6));
    }
}
