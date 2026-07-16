using System.Text.Json;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

public static class MainWindowModeNavigationExtensions
{
    private const string ModeProfilePrefix = "Mode profile · ";

    public static async Task OpenModeDefinitionAsync(this MainWindowViewModel shell, ModeDefinition mode)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(mode);

        switch (mode.Key.ToLowerInvariant())
        {
            case "chat":
                await shell.NavigateChatCommand.ExecuteAsync();
                return;
            case "teach":
                await shell.NavigateTeachCommand.ExecuteAsync();
                return;
            case "do":
                await shell.NavigateDoCommand.ExecuteAsync();
                return;
            case "studio":
                await shell.NavigateStudioCommand.ExecuteAsync();
                return;
            case "browse":
                shell.NavigateBrowserCommand.Execute(null);
                return;
            case "plan":
                shell.NavigatePlanCommand.Execute(null);
                return;
            case "training":
                shell.NavigateTrainingCommand.Execute(null);
                return;
            case "call":
                await shell.NavigateCallCommand.ExecuteAsync();
                return;
        }

        await NavigateToBaseWorkspaceAsync(shell, mode.BaseMode);
        ApplyModeProfile(shell.CurrentChat, mode);
        await shell.NavigateCurrentChatCommand.ExecuteAsync();
    }

    internal static void ApplyModeProfile(ChatPageViewModel chat, ModeDefinition mode)
    {
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(mode);
        if (chat.Mode != mode.BaseMode)
            throw new InvalidOperationException($"Mode '{mode.Name}' requires the {mode.BaseMode} workspace, but {chat.Mode} is active.");

        chat.NewChatCommand.Execute(null);

        foreach (var oldProfile in chat.Prompts
                     .Where(prompt => prompt.Name.StartsWith(ModeProfilePrefix, StringComparison.OrdinalIgnoreCase))
                     .ToArray())
            chat.Prompts.Remove(oldProfile);

        if (!string.IsNullOrWhiteSpace(mode.SystemPromptSuffix))
        {
            var prompt = new PromptItemViewModel(
                new PromptDefinition(
                    Guid.NewGuid(),
                    ModeProfilePrefix + mode.Name,
                    $"Runtime instructions supplied by the {mode.Name} mode.",
                    mode.IconKey,
                    mode.SystemPromptSuffix.Trim(),
                    true,
                    false,
                    true,
                    DateTimeOffset.UtcNow,
                    false,
                    JsonSerializer.Serialize(new[] { mode.BaseMode.ToString() })),
                mode.BaseMode,
                true)
            {
                IsActive = true
            };
            chat.Prompts.Insert(0, prompt);
        }

        var requestedPlugins = ParseNames(mode.PluginsJson);
        foreach (var plugin in chat.Plugins)
            plugin.IsActive = requestedPlugins.Contains(plugin.Name) && plugin.IsAvailableInMode && plugin.IsRuntimeAvailable;
    }

    private static async Task NavigateToBaseWorkspaceAsync(MainWindowViewModel shell, HavenMode baseMode)
    {
        switch (baseMode)
        {
            case HavenMode.Chat:
                await shell.NavigateChatCommand.ExecuteAsync();
                break;
            case HavenMode.Teach:
                await shell.NavigateTeachCommand.ExecuteAsync();
                break;
            case HavenMode.Do:
                await shell.NavigateDoCommand.ExecuteAsync();
                break;
            case HavenMode.Studio:
                await shell.NavigateStudioCommand.ExecuteAsync();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(baseMode), baseMode, "Unsupported mode workspace.");
        }
    }

    private static HashSet<string> ParseNames(string? json)
    {
        try
        {
            return (JsonSerializer.Deserialize<string[]>(string.IsNullOrWhiteSpace(json) ? "[]" : json) ?? [])
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
