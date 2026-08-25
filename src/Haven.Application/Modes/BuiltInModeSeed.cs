/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/BuiltInModeSeed.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns BuiltInModeSeed. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Represents built in mode seed and keeps its related state and behavior together.
/// </summary>
public sealed class BuiltInModeSeed
{
    /// <summary>
    /// Gets or updates modes, the bindable or domain state represented by this property.
    /// </summary>
    public static IReadOnlyList<ModeDefinition> Modes { get; } =
    [
        new ModeDefinition(
            Guid.Parse("a0000000-0000-0000-0000-000000000001"),
            "chat", "Chat", "Private conversation with local models", "chat",
            HavenMode.Chat, "[\"Chat\"]", "[]", "[]", "[]", "",
            ModeSource.BuiltIn, ModeInstallState.BuiltIn, "Haven", "1.0.0", "[]",
            DateTimeOffset.MinValue, DateTimeOffset.MinValue),
        new ModeDefinition(
            Guid.Parse("a0000000-0000-0000-0000-000000000002"),
            "study", "Study", "Structured lessons and knowledge checks", "book",
            HavenMode.Study, "[\"Study\"]", "[]", "[]", "[]", "",
            ModeSource.BuiltIn, ModeInstallState.BuiltIn, "Haven", "1.0.0", "[]",
            DateTimeOffset.MinValue, DateTimeOffset.MinValue),
        new ModeDefinition(
            Guid.Parse("a0000000-0000-0000-0000-000000000003"),
            "automations", "Automations", "Reusable, scheduled, recurring and triggered workflows", "automation",
            HavenMode.Tasks, "[\"Automations\"]", "[\"write_file\",\"replace_in_file\",\"run_tests\",\"run_command\"]", "[]", "[\"Automate\"]", "",
            ModeSource.BuiltIn, ModeInstallState.BuiltIn, "Haven", "1.0.0", "[]",
            DateTimeOffset.MinValue, DateTimeOffset.MinValue),
        new ModeDefinition(
            Guid.Parse("a0000000-0000-0000-0000-000000000020"),
            "terminal", "Terminal", "Run local commands with visible permissions and real output", "commands",
            HavenMode.Tasks, "[\"Terminal\"]", "[\"run_command\",\"run_tests\"]", "[]", "[]", "",
            ModeSource.BuiltIn, ModeInstallState.BuiltIn, "Haven", "1.0.0", "[\"developer\",\"productivity\"]",
            DateTimeOffset.MinValue, DateTimeOffset.MinValue),
        new ModeDefinition(
            Guid.Parse("a0000000-0000-0000-0000-000000000019"),
            "tasks", "Tasks", "One-off delegated and agentic work", "tasks",
            HavenMode.Tasks, "[\"Tasks\"]", "[\"write_file\",\"replace_in_file\",\"run_tests\",\"run_command\"]", "[]", "[]", "",
            ModeSource.BuiltIn, ModeInstallState.BuiltIn, "Haven", "1.0.0", "[]",
            DateTimeOffset.MinValue, DateTimeOffset.MinValue),
        new ModeDefinition(
            Guid.Parse("a0000000-0000-0000-0000-000000000004"),
            "studio", "Studio", "Inspect, edit, test and repair local projects", "code",
            HavenMode.Studio, "[\"Studio\"]", "[\"write_file\",\"replace_in_file\",\"run_tests\",\"run_command\"]", "[]", "[\"Automate\",\"Test\"]", "",
            ModeSource.BuiltIn, ModeInstallState.BuiltIn, "Haven", "1.0.0", "[]",
            DateTimeOffset.MinValue, DateTimeOffset.MinValue),
        new ModeDefinition(
            Guid.Parse("a0000000-0000-0000-0000-000000000005"),
            "browse", "Browse", "Isolated tabbed browser with side assistant", "globe",
            HavenMode.Chat, "[\"Browse\"]", "[]", "[]", "[\"BrowserUse\",\"WebSearch\"]", "",
            ModeSource.BuiltIn, ModeInstallState.BuiltIn, "Haven", "1.0.0", "[]",
            DateTimeOffset.MinValue, DateTimeOffset.MinValue),
        new ModeDefinition(
            Guid.Parse("a0000000-0000-0000-0000-000000000006"),
            "plan", "Plan", "Task planning and calendar management", "calendar",
            HavenMode.Chat, "[\"Plan\"]", "[]", "[]", "[]", "",
            ModeSource.BuiltIn, ModeInstallState.BuiltIn, "Haven", "1.0.0", "[]",
            DateTimeOffset.MinValue, DateTimeOffset.MinValue),
        new ModeDefinition(
            Guid.Parse("a0000000-0000-0000-0000-000000000007"),
            "training", "Training", "Autonomous agent sessions with scoring", "target",
            HavenMode.Tasks, "[\"Training\"]", "[\"write_file\",\"replace_in_file\",\"run_tests\",\"run_command\"]", "[]", "[]", "",
            ModeSource.BuiltIn, ModeInstallState.BuiltIn, "Haven", "1.0.0", "[]",
            DateTimeOffset.MinValue, DateTimeOffset.MinValue),
        new ModeDefinition(
            Guid.Parse("a0000000-0000-0000-0000-000000000008"),
            "imagine", "Imagine", "Create and refine image concepts with local models and approved providers", "palette",
            HavenMode.Chat, "[\"Imagine\"]", "[]", "[]", "[]", "Help the user turn an idea into a production-ready visual brief. Ask only for missing essentials, preserve a clear prompt history, and be explicit when an image provider is not installed instead of claiming an image was generated.",
            ModeSource.BuiltIn, ModeInstallState.BuiltIn, "Haven", "1.0.0", "[\"media\",\"creative\"]",
            DateTimeOffset.MinValue, DateTimeOffset.MinValue),
          new ModeDefinition(
              Guid.Parse("a0000000-0000-0000-0000-000000000018"),
              "canvas", "Canvas", "Create and edit infinite whiteboards and study boards", "file",
              HavenMode.Chat, "[\"Canvas\"]", "[]", "[]", "[]", "Act as Haven Canvas. Help create and edit visual boards while preserving the user's existing canvas content and study-layer intent.",
              ModeSource.BuiltIn, ModeInstallState.BuiltIn, "Haven", "1.0.0", "[\"productivity\",\"creative\",\"study\"]",
              DateTimeOffset.MinValue, DateTimeOffset.MinValue),
        new ModeDefinition(
            Guid.Parse("a0000000-0000-0000-0000-000000000009"),
            "present", "Present", "Plan, draft and review presentations", "present",
            HavenMode.Chat, "[\"Present\"]", "[]", "[]", "[]", "Act as Haven Present. Develop an audience-aware narrative, slide outline, concise on-slide copy, speaker notes and an evidence checklist. Never claim a deck file was exported unless a file tool confirms it.",
            ModeSource.BuiltIn, ModeInstallState.BuiltIn, "Haven", "1.0.0", "[\"productivity\",\"creative\"]",
            DateTimeOffset.MinValue, DateTimeOffset.MinValue),
        new ModeDefinition(
            Guid.Parse("a0000000-0000-0000-0000-000000000010"),
            "data", "Data", "Import, inspect and analyse local data", "data",
            HavenMode.Tasks, "[\"Data\"]", "[]", "[]", "[]", "Act as Haven Data. Inspect attached CSV, JSON and text datasets carefully, state schema and data-quality assumptions, show calculations, and distinguish measured results from inference.",
            ModeSource.BuiltIn, ModeInstallState.BuiltIn, "Haven", "1.0.0", "[\"productivity\"]",
            DateTimeOffset.MinValue, DateTimeOffset.MinValue),
        new ModeDefinition(
            Guid.Parse("a0000000-0000-0000-0000-000000000011"),
            "vision", "Vision", "Inspect images with a compatible local vision model", "vision",
            HavenMode.Chat, "[\"Vision\"]", "[]", "[]", "[]", "Act as Haven Vision. Analyse only images actually attached to the current turn, describe uncertainty, and clearly report when the selected local model has no vision capability.",
            ModeSource.BuiltIn, ModeInstallState.BuiltIn, "Haven", "1.0.0", "[\"media\"]",
            DateTimeOffset.MinValue, DateTimeOffset.MinValue),
        new ModeDefinition(
            Guid.Parse("a0000000-0000-0000-0000-000000000012"),
            "play", "Play", "Launch and organise interactive local experiences", "play",
            HavenMode.Chat, "[\"Play\"]", "[]", "[]", "[]", "Act as Haven Play. Help the user discover, design and safely launch interactive local experiences. Do not claim an application or game launched unless a confirmed tool result says it did.",
            ModeSource.BuiltIn, ModeInstallState.BuiltIn, "Haven", "1.0.0", "[\"media\"]",
            DateTimeOffset.MinValue, DateTimeOffset.MinValue),
        new ModeDefinition(
            Guid.Parse("a0000000-0000-0000-0000-000000000013"),
            "translate", "Translate", "Translate text and documents with local models", "translate",
            HavenMode.Chat, "[\"Translate\"]", "[]", "[]", "[]", "Act as Haven Translate. Preserve meaning, tone, formatting, names and domain terminology. Identify the source language, translate into the requested target, and briefly flag genuinely ambiguous phrases.",
            ModeSource.BuiltIn, ModeInstallState.BuiltIn, "Haven", "1.0.0", "[\"productivity\"]",
            DateTimeOffset.MinValue, DateTimeOffset.MinValue),
        new ModeDefinition(
            Guid.Parse("a0000000-0000-0000-0000-000000000014"),
            "launcher", "Launcher", "Find and launch apps, projects and commands", "rocket",
            HavenMode.Chat, "[\"Launcher\"]", "[]", "[]", "[]", "Act as Haven Launcher. Find the most relevant installed Haven app, project, command or recent item and explain the destination before opening it. Use registered commands only and never invent an installed capability.",
            ModeSource.BuiltIn, ModeInstallState.BuiltIn, "Haven", "1.0.0", "[\"general\"]",
            DateTimeOffset.MinValue, DateTimeOffset.MinValue),
        new ModeDefinition(
            Guid.Parse("a0000000-0000-0000-0000-000000000015"),
            "go", "Go", "Universal contextual search and quick navigation", "search",
            HavenMode.Chat, "[\"Go\"]", "[]", "[]", "[]", "",
            ModeSource.BuiltIn, ModeInstallState.BuiltIn, "Haven", "1.0.0", "[\"general\"]",
            DateTimeOffset.MinValue, DateTimeOffset.MinValue),
        new ModeDefinition(
            Guid.Parse("a0000000-0000-0000-0000-000000000016"),
            "dashboard", "Dashboard", "Build and switch between personal dashboard pages", "dashboard",
            HavenMode.Chat, "[\"Dashboard\"]", "[]", "[]", "[]", "",
            ModeSource.BuiltIn, ModeInstallState.BuiltIn, "Haven", "1.0.0", "[\"general\"]",
            DateTimeOffset.MinValue, DateTimeOffset.MinValue),
        new ModeDefinition(
            Guid.Parse("a0000000-0000-0000-0000-000000000017"),
            "write", "Write", "Create, edit, import and export rich local documents", "file",
            HavenMode.Chat, "[]", "[]", "[]", "[]", "Act as Haven Write when AI document assistance is requested. Operate on the user's real document or selection, preserve structure and formatting, and make proposed edits explicit before applying them.",
            ModeSource.BuiltIn, ModeInstallState.BuiltIn, "Haven", "1.0.0", "[\"productivity\",\"documents\"]",
            DateTimeOffset.MinValue, DateTimeOffset.MinValue),
        new ModeDefinition(
            Guid.Parse("a0000000-0000-0000-0000-000000000021"),
            "mesh", "Mesh", "Connect trusted devices and run multi-device Work Mode teams", "plugin",
            HavenMode.Tasks, "[\"Mesh\",\"Work Mode\"]", "[]", "[]", "[]", "Use Haven Mesh to manage trusted devices, remote models and agents, and coordinated Work Mode teams. Never treat discovery as trust or claim a remote action ran without a confirmed receipt.",
            ModeSource.BuiltIn, ModeInstallState.BuiltIn, "Haven", "1.0.0", "[\"productivity\",\"devices\"]",
            DateTimeOffset.MinValue, DateTimeOffset.MinValue),
        new ModeDefinition(
            Guid.Parse("a0000000-0000-0000-0000-000000000022"),
            "spaces", "Spaces", "Organise chats, files and layouts into named Spaces", "folder",
            HavenMode.Chat, "[\"Spaces\"]", "[]", "[]", "[]", "Act as Haven Spaces. Help the user organise conversations, files and layouts into named Spaces. Opening a Space exposes its conversations; New Chat while scoped to a Space creates a Space-associated conversation. Never claim a conversation joined a Space without a confirmed membership change.",
            ModeSource.BuiltIn, ModeInstallState.BuiltIn, "Haven", "1.0.0", "[\"general\",\"productivity\"]",
            DateTimeOffset.MinValue, DateTimeOffset.MinValue),
        new ModeDefinition(
            Guid.Parse("a0000000-0000-0000-0000-000000000023"),
            "boards", "Boards", "Organise notebooks, pages, freeform ideas and live components", "notes",
            HavenMode.Chat, "[\"Boards\"]", "[]", "[]", "[]", "Act as Haven Boards. Preserve stable board, section, page, block, freeform-object and live-component identities. Keep structured content semantic, use real attachment references, and report unavailable live sources truthfully.",
            ModeSource.BuiltIn, ModeInstallState.BuiltIn, "Haven", "1.0.0", "[\"productivity\",\"documents\",\"study\"]",
            DateTimeOffset.MinValue, DateTimeOffset.MinValue),
        new ModeDefinition(
            Guid.Parse("a0000000-0000-0000-0000-000000000024"),
            "maps", "Maps", "Find places, view OpenStreetMap maps, get directions and save places", "map",
            HavenMode.Chat, "[\"Maps\"]", "[]", "[]", "[]", "Act as Haven Maps. Answer place, map and direction questions only from provider-backed OpenStreetMap and OSRM results, keep the required OpenStreetMap attribution visible on the map surface, and never invent places, coordinates or routes when the providers are unavailable.",
            ModeSource.BuiltIn, ModeInstallState.BuiltIn, "Haven", "1.0.0", "[\"general\",\"travel\"]",
            DateTimeOffset.MinValue, DateTimeOffset.MinValue)
    ];
}
