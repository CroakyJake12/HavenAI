using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Spaces;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private NativeSpacesPage? _spacesPage;
    private SpaceRegistry? _spaceRegistry;

    private SpaceRegistry SpacesRegistry => _spaceRegistry ??= new SpaceRegistry(_versionedSettings);

    private async Task OpenSpacesAsync()
    {
        _spacesPage ??= new NativeSpacesPage(
            SpacesRegistry,
            new SpaceGeneratedSurfaceRenderer(
                _genUiRouter,
                _genUiInstances,
                _checklistTemplate,
                _dataGridTemplate,
                _cardDeckTemplate,
                _dashboardTemplate,
                _assessmentTemplate,
                _workflowTemplate,
                _customTemplate),
            new SpaceEditPlanner(_ollama, () => _preferences.DefaultModel),
            LaunchSpaceAsync,
            OpenSpaceLayoutAsync);

        AddOrSelectTab("spaces", "Spaces", _spacesPage, false, HavenSurface.Spaces);
        await _spacesPage.ActivateAsync(CancellationToken.None);
        ApplyShellVisualState();
    }

    private async Task LaunchSpaceAsync(SpaceDefinition space)
    {
        var plan = SpaceLaunchPolicy.Resolve(space);
        if (plan.Destination == SpaceLaunchDestination.StudyProduct)
        {
            await OpenStudyHomeAsync();
            return;
        }

        var page = CreateNewChatPage();
        await ConfigureAddMenuAsync(page);
        await page.StartFreshConversationAsync(HavenMode.Chat, null);
        page.ConfigureRegisteredContext(plan.RegisteredContext, plan.EffortOverride);

        if (plan.Files.Count > 0)
            await page.AddFilesAsync(plan.Files.Select(file => file.Path));

        if (!string.IsNullOrWhiteSpace(plan.ModelName))
        {
            try
            {
                var models = await _ollama.GetModelsAsync(CancellationToken.None);
                var selected = models.FirstOrDefault(model => model.Name.Equals(plan.ModelName, StringComparison.OrdinalIgnoreCase));
                if (selected is not null) page.SelectModel(selected);
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException)
            {
                // The workspace remains usable with Chat's normal model fallback if the preferred model is unavailable.
            }
        }

        AddOrSelectTab(
            $"space-{space.Id:N}-{Guid.NewGuid():N}",
            plan.Title,
            page,
            false,
            HavenSurface.Spaces,
            forceNewTab: true);
        page.FocusComposer();
        ApplyShellVisualState();
    }

    private Task OpenSpaceLayoutAsync(SpaceDefinition space)
    {
        var page = new SpaceLayoutEditorPage(SpacesRegistry, space);
        AddOrSelectTab(
            $"space-layout-{space.Id:N}",
            $"{space.Name} layout",
            page,
            true,
            HavenSurface.Spaces,
            forceNewTab: true);
        ApplyShellVisualState();
        return Task.CompletedTask;
    }
}
