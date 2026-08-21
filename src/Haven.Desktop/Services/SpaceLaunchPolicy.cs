using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Services;

internal enum SpaceLaunchDestination
{
    StudyProduct = 0,
    ConfiguredWorkspace = 1
}

internal sealed record SpaceLaunchPlan(
    SpaceLaunchDestination Destination,
    HavenMode Mode,
    string Title,
    string? ModelName,
    SpaceThinkingMode ThinkingMode,
    string RegisteredContext,
    IReadOnlyList<SpaceFileReference> Files,
    SpaceGeneratedSurface? GeneratedSurface,
    SpaceLayoutDocument? LayoutDocument);

internal static class SpaceLaunchPolicy
{
    public static SpaceLaunchPlan Resolve(SpaceDefinition space)
    {
        ArgumentNullException.ThrowIfNull(space);
        var destination = space.Kind == SpaceKind.Study
            ? SpaceLaunchDestination.StudyProduct
            : SpaceLaunchDestination.ConfiguredWorkspace;
        var mode = destination == SpaceLaunchDestination.StudyProduct ? HavenMode.Study : HavenMode.Chat;
        return new SpaceLaunchPlan(
            destination,
            mode,
            space.Name,
            space.ModelName,
            space.ThinkingMode,
            BuildRegisteredContext(space),
            space.Files.ToArray(),
            space.GeneratedSurface,
            space.LayoutDocument);
    }

    internal static string BuildRegisteredContext(SpaceDefinition space)
    {
        var sections = new List<string>
        {
            $"Active Haven Space: {space.Name}.",
            $"Space kind: {space.Kind}."
        };
        if (!string.IsNullOrWhiteSpace(space.Description)) sections.Add("Purpose: " + space.Description.Trim());
        if (!string.IsNullOrWhiteSpace(space.Instructions)) sections.Add("Space instructions:\n" + space.Instructions.Trim());
        if (space.ExamplePairs.Count > 0)
        {
            sections.Add("Space examples:\n" + string.Join("\n", space.ExamplePairs.Select(pair =>
                $"User: {pair.User}\nHaven: {pair.Assistant}")));
        }
        if (space.Files.Count > 0)
        {
            sections.Add("Space files and declared permissions:\n" + string.Join("\n", space.Files.Select(file =>
                $"- {file.DisplayName}: {(file.Permission == SpaceFilePermission.ReadWrite ? "read/write" : "read-only")}")));
        }
        return string.Join("\n\n", sections);
    }
}
