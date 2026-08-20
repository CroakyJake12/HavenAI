namespace Haven.Desktop.Services;

public enum GoRouteDestination
{
    Chat,
    App,
    Project,
    Clarify
}

public sealed record GoRoutingContext(
    IReadOnlyList<string> AttachmentPaths,
    IReadOnlyList<string> ProjectNames)
{
    public static GoRoutingContext Empty { get; } = new([], []);

    public bool HasImageAttachment => AttachmentPaths.Any(IsImagePath);

    private static bool IsImagePath(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".gif", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record GoRouteDecision(
    GoRouteDestination Destination,
    string Instruction,
    GoRoutingContext Context,
    string? TargetKey = null,
    string? ProjectName = null,
    string? Clarification = null);

/// <summary>
/// Deterministic first-pass routing for Go. Clear product intents are routed locally,
/// while ambiguous requests are returned to the caller for clarification instead of
/// silently losing the user's original instruction or attachments.
/// </summary>
public static class GoRouteIntentPolicy
{
    private static readonly string[] ProjectTerms = ["project", "repo", "repository", "codebase", "work on", "fix bug", "debug"];
    private static readonly string[] CodeTerms = ["write code", "fix code", "debug code", "compile", "build the app", "run tests", "refactor"];
    private static readonly string[] PresentationTerms = ["presentation", "slide deck", "slides", "powerpoint", "ppt", "pitch deck"];
    private static readonly string[] WriteTerms = ["letter", "email", "essay", "document", "report", "cv", "resume", "cover letter"];
    private static readonly string[] StudyTerms = ["study", "revise", "revision", "homework", "flashcards", "practice questions", "maths revision", "exam prep"];
    private static readonly string[] DashboardTerms = ["dashboard", "my overview", "personal overview"];
    private static readonly string[] ImageEditTerms = ["edit this image", "edit this photo", "edit this picture", "retouch", "remove the background", "crop this image", "change this image"];
    private static readonly string[] ImageCreateTerms = ["make an image", "create an image", "generate an image", "draw an image", "make a picture", "generate a picture"];
    private static readonly string[] VisionTerms = ["what is in this image", "what's in this image", "what is in this photo", "what's in this photo", "inspect this image", "analyse this image", "analyze this image", "read this image"];
    private static readonly string[] BrowseTerms = ["browse the web", "search the web", "search online", "look this up online", "open this website", "go to this website", "web search"];
    private static readonly string[] PlanTerms = ["plan my week", "plan my day", "schedule my", "calendar", "organise my week", "organize my week"];
    private static readonly string[] AutomationTerms = ["remind me", "set a reminder", "automate", "automation", "every day", "every week", "recurring task", "when this happens"];
    private static readonly string[] DataTerms = ["spreadsheet", "csv", "dataset", "analyse the data", "analyze the data", "data table", "sql"];
    private static readonly string[] TranslateTerms = ["translate", "translation"];
    private static readonly string[] CanvasTerms = ["whiteboard", "mind map", "canvas board", "infinite canvas"];
    private static readonly string[] PlayTerms = ["play a game", "launch a game", "interactive experience"];
    private static readonly string[] LauncherTerms = ["open calculator", "launch calculator", "open an app", "launch an app", "open application", "launch application"];
    private static readonly string[] TaskTerms = ["delegate this task", "delegate this", "run this as a task", "one-off task", "agent task"];

    public static GoRouteDecision Resolve(string instruction, GoRoutingContext? context = null)
    {
        context ??= GoRoutingContext.Empty;
        var normalized = Normalize(instruction);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Clarify(instruction, context, "Tell Haven what you want to do.");
        }

        if (ContainsAny(normalized, ImageEditTerms))
        {
            return context.HasImageAttachment
                ? App(instruction, context, "imagine")
                : Clarify(instruction, context, "Attach the image you want to edit, then try again.");
        }

        if (ContainsAny(normalized, VisionTerms))
        {
            return context.HasImageAttachment
                ? App(instruction, context, "vision")
                : Clarify(instruction, context, "Attach the image you want Haven to inspect.");
        }

        var namedProject = FindNamedProject(normalized, context.ProjectNames);
        if (namedProject is not null && ContainsAny(normalized, ProjectTerms))
        {
            return new GoRouteDecision(GoRouteDestination.Project, instruction, context, ProjectName: namedProject);
        }

        if (ContainsAny(normalized, PresentationTerms)) return App(instruction, context, "present");
        if (ContainsAny(normalized, WriteTerms)) return App(instruction, context, "write");
        if (ContainsAny(normalized, StudyTerms)) return App(instruction, context, "study");
        if (ContainsAny(normalized, DashboardTerms)) return App(instruction, context, "dashboard");
        if (ContainsAny(normalized, BrowseTerms)) return App(instruction, context, "browse");
        if (ContainsAny(normalized, PlanTerms)) return App(instruction, context, "plan");
        if (ContainsAny(normalized, AutomationTerms)) return App(instruction, context, "automations");
        if (ContainsAny(normalized, DataTerms)) return App(instruction, context, "data");
        if (ContainsAny(normalized, TranslateTerms)) return App(instruction, context, "translate");
        if (ContainsAny(normalized, CanvasTerms)) return App(instruction, context, "canvas");
        if (ContainsAny(normalized, PlayTerms)) return App(instruction, context, "play");
        if (ContainsAny(normalized, LauncherTerms)) return App(instruction, context, "launcher");
        if (ContainsAny(normalized, ImageCreateTerms)) return App(instruction, context, "imagine");
        if (ContainsAny(normalized, CodeTerms)) return App(instruction, context, "studio");
        if (ContainsAny(normalized, TaskTerms)) return App(instruction, context, "tasks");

        if (ContainsAny(normalized, ProjectTerms) && context.ProjectNames.Count > 0)
        {
            if (context.ProjectNames.Count == 1)
            {
                return new GoRouteDecision(GoRouteDestination.Project, instruction, context, ProjectName: context.ProjectNames[0]);
            }

            return Clarify(instruction, context, "Which project should Haven open?");
        }

        if (IsAmbiguousOpenRequest(normalized))
        {
            return Clarify(instruction, context, "What should Haven open?");
        }

        return new GoRouteDecision(GoRouteDestination.Chat, instruction, context, TargetKey: "chat");
    }

    private static GoRouteDecision App(string instruction, GoRoutingContext context, string targetKey)
        => new(GoRouteDestination.App, instruction, context, TargetKey: targetKey);

    private static GoRouteDecision Clarify(string instruction, GoRoutingContext context, string clarification)
        => new(GoRouteDestination.Clarify, instruction, context, Clarification: clarification);

    private static string Normalize(string value)
        => string.Join(' ', value.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static bool ContainsAny(string value, IEnumerable<string> terms)
        => terms.Any(term => value.Contains(term, StringComparison.Ordinal));

    private static string? FindNamedProject(string normalizedInstruction, IReadOnlyList<string> projectNames)
        => projectNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderByDescending(name => name.Length)
            .FirstOrDefault(name => normalizedInstruction.Contains(name.Trim().ToLowerInvariant(), StringComparison.Ordinal));

    private static bool IsAmbiguousOpenRequest(string normalized)
        => normalized is "open it" or "open this" or "go there" or "take me there";
}
