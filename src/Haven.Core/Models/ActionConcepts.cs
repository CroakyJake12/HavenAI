// User-facing action concepts: default provider categories, chat plan approval artifacts and suggested actions.

using System.Text.RegularExpressions;

namespace Haven.Core;

/// <summary>Categories a user can assign a default provider App for (or Always Ask).</summary>
public enum ProviderCategory
{
    Email = 0,
    Calendar = 1,
    Reminders = 2,
    Documents = 3,
    Notes = 4,
    Browser = 5,
    Automation = 6,
    ImageGeneration = 7,
    VideoGeneration = 8,
    AudioSpeech = 9,
    Maps = 10,
    CloudFiles = 11,
    CodingWorkspace = 12
}

/// <summary>Human-readable names for provider categories.</summary>
public static class ProviderCategoryNames
{
    public static string For(ProviderCategory category) => category switch
    {
        ProviderCategory.Email => "Email",
        ProviderCategory.Calendar => "Calendar",
        ProviderCategory.Reminders => "Reminders",
        ProviderCategory.Documents => "Documents",
        ProviderCategory.Notes => "Notes",
        ProviderCategory.Browser => "Browser",
        ProviderCategory.Automation => "Automation / Macro",
        ProviderCategory.ImageGeneration => "Image Generation",
        ProviderCategory.VideoGeneration => "Video Generation",
        ProviderCategory.AudioSpeech => "Audio / TTS",
        ProviderCategory.Maps => "Maps",
        ProviderCategory.CloudFiles => "Cloud Files",
        _ => "Coding Workspace"
    };
}

/// <summary>Reserved assignment meaning Haven must ask the user instead of guessing.</summary>
public static class DefaultProviderAssignments
{
    public const string AlwaysAsk = "ask";
}

/// <summary>
/// A parsed plan-approval artifact from an assistant message that used the Plan instruction.
/// The tag is removed from the user-visible content; the remainder is the plan body.
/// </summary>
public sealed record ChatPlanArtifact(string Title, string CleanedContent)
{
    private static readonly Regex TagRegex = new(
        @"<haven-plan>(?<title>[\s\S]*?)</haven-plan>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ChatPlanArtifact? TryParse(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        var match = TagRegex.Match(content);
        if (!match.Success) return null;
        var title = match.Groups["title"].Value.Trim();
        if (title.Length is 0 or > 400) return null;
        var cleaned = content.Remove(match.Index, match.Length).TrimEnd();
        return new ChatPlanArtifact(title, cleaned);
    }

    /// <summary>Canonical option labels rendered for the three plan decisions.</summary>
    public static class Options
    {
        public const string Follow = "Follow this Plan";
        public const string Tweak = "Tweak this Plan";
        public const string Reject = "Reject this Plan";
    }
}

/// <summary>One optional, non-modal next-step suggestion shown after an assistant response.</summary>
public sealed record SuggestedAction(string Label, string ComposerText, string Reason);

/// <summary>
/// Conservative heuristic engine producing at most two suggested actions per turn.
/// Suggestions never interrupt conversation; ignoring them simply continues the chat.
/// </summary>
public static class SuggestedActionEngine
{
    public static IReadOnlyList<SuggestedAction> ForTurn(
        string prompt,
        string assistantContent,
        bool workspaceAttached,
        bool studyMode)
    {
        if (string.IsNullOrWhiteSpace(prompt) || string.IsNullOrWhiteSpace(assistantContent)) return [];
        // Purely conversational turns get no suggestions.
        if (prompt.Trim().Length < 12) return [];

        var suggestions = new List<SuggestedAction>(2);

        if (!workspaceAttached && Regex.IsMatch(prompt, @"\b(remind|reminder|schedule|calendar|appointment|meeting|forget)\b", RegexOptions.IgnoreCase)
            && !Regex.IsMatch(prompt, @"\b(already|did you|i reminded|cancelled)\b", RegexOptions.IgnoreCase))
        {
            suggestions.Add(new SuggestedAction(
                "Add to Planner",
                "Add this to my planner.",
                "Creating a planner entry could be useful."));
        }

        if (workspaceAttached && assistantContent.Contains("```", StringComparison.Ordinal)
            && Regex.IsMatch(assistantContent, @"\b(test|tests|build|compile)\b", RegexOptions.IgnoreCase))
        {
            suggestions.Add(new SuggestedAction(
                "Run tests now",
                "Run the relevant tests in the workspace and report the results.",
                "Validating the change just produced."));
        }

        if (studyMode && Regex.IsMatch(assistantContent, @"\b(step \d|first|second|third|definition|theorem|formula)\b", RegexOptions.IgnoreCase)
            && assistantContent.Length > 600)
        {
            suggestions.Add(new SuggestedAction(
                "Create flashcards",
                "Create flashcards from the key points above.",
                "This explanation looks like revision material."));
        }

        return suggestions.Take(2).ToArray();
    }
}
