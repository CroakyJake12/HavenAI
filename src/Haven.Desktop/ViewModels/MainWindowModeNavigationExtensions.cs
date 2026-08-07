/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/ViewModels/MainWindowModeNavigationExtensions.cs, in the Desktop presentation-model layer, exposing bindable state and commands to Avalonia views.
 * What: This file owns MainWindowModeNavigationExtensions, ModeActivationSnapshot. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Keeping UI state here makes the XAML declarative and keeps behavior testable without recreating the full window.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Core;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Views.Shell;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Represents main window mode navigation extensions and keeps its related state and behavior together.
/// </summary>
public static class MainWindowModeNavigationExtensions
{
    /// <summary>
    /// Stores mode profile prefix locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const string ModeProfilePrefix = "App profile · ";
    /// <summary>
    /// Stores snapshots locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly ConditionalWeakTable<ChatPage, ModeActivationSnapshot> Snapshots = new();

    /// <summary>
    /// Performs open mode definition asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public static async Task OpenModeDefinitionAsync(this MainView shell, ModeDefinition mode)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(mode);

        switch (mode.Key.ToLowerInvariant())
        {
            case "chat":
                await shell.NavigateChatCommand.ExecuteAsync();
                ClearModeProfile(shell.CurrentChat);
                return;
            case "study":
            case "teach": // Compatibility route for saved layouts created before Study.
                await shell.NavigateStudyCommand.ExecuteAsync();
                ClearModeProfile(shell.CurrentChat);
                return;
            case "tasks":
            case "do": // Compatibility route for saved layouts created before Tasks.
            case "research": // Compatibility route for the short-lived Research label.
                await shell.NavigateTasksCommand.ExecuteAsync();
                ClearModeProfile(shell.CurrentChat);
                return;
            case "studio":
                await shell.NavigateStudioCommand.ExecuteAsync();
                ClearModeProfile(shell.CurrentChat);
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
        }

        await NavigateToBaseWorkspaceAsync(shell, mode.BaseMode);
        ApplyModeProfile(shell.CurrentChat, mode);
        await shell.NavigateCurrentChatCommand.ExecuteAsync();
    }

    /// <summary>
    /// Performs the apply mode profile step owned by this component.
    /// </summary>
    internal static void ApplyModeProfile(ChatPage chat, ModeDefinition mode)
    {
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(mode);
        if (chat.Mode != mode.BaseMode)
            throw new InvalidOperationException($"Mode '{mode.Name}' requires the {mode.BaseMode} workspace, but {chat.Mode} is active.");

        ClearModeProfile(chat);
        Snapshots.Add(chat, new ModeActivationSnapshot(
            chat.Plugins.Where(plugin => plugin.IsActive).Select(plugin => plugin.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)));
        chat.NewChatCommand.Execute(null);

        var instruction = string.IsNullOrWhiteSpace(mode.SystemPromptSuffix)
            ? $"The user selected the {mode.Name} mode. Follow the mode's enabled capabilities and the user's instructions without assuming permissions that were not granted."
            : mode.SystemPromptSuffix.Trim();
        var prompt = new PromptItemViewModel(
            new PromptDefinition(
                Guid.NewGuid(),
                ModeProfilePrefix + mode.Name,
                $"Runtime instructions supplied by the {mode.Name} mode.",
                mode.IconKey,
                instruction,
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

        var requestedPlugins = ParseNames(mode.PluginsJson);
        foreach (var plugin in chat.Plugins)
            plugin.IsActive = requestedPlugins.Contains(plugin.Name) && plugin.IsAvailableInMode && plugin.IsRuntimeAvailable;
    }

    /// <summary>
    /// Performs the clear mode profile step owned by this component.
    /// </summary>
    internal static void ClearModeProfile(ChatPage chat)
    {
        foreach (var oldProfile in chat.Prompts
                     .Where(prompt => prompt.Name.StartsWith(ModeProfilePrefix, StringComparison.OrdinalIgnoreCase))
                     .ToArray())
            chat.Prompts.Remove(oldProfile);

        if (!Snapshots.TryGetValue(chat, out var snapshot)) return;
        foreach (var plugin in chat.Plugins)
            plugin.IsActive = snapshot.ActivePlugins.Contains(plugin.Name) && plugin.IsAvailableInMode && plugin.IsRuntimeAvailable;
        Snapshots.Remove(chat);
    }

    /// <summary>
    /// Performs navigate to base workspace asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task NavigateToBaseWorkspaceAsync(MainView shell, HavenMode baseMode)
    {
        switch (baseMode)
        {
            case HavenMode.Chat:
                await shell.NavigateChatCommand.ExecuteAsync();
                break;
            case HavenMode.Study:
                await shell.NavigateStudyCommand.ExecuteAsync();
                break;
            case HavenMode.Tasks:
                await shell.NavigateTasksCommand.ExecuteAsync();
                break;
            case HavenMode.Studio:
                await shell.NavigateStudioCommand.ExecuteAsync();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(baseMode), baseMode, "Unsupported mode workspace.");
        }
    }

    /// <summary>
    /// Performs the parse names step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Represents mode activation snapshot and keeps its related state and behavior together.
    /// </summary>
    private sealed record ModeActivationSnapshot(HashSet<string> ActivePlugins);
}
