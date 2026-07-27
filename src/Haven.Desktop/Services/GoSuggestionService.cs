using System.Text.Json;
using System.Text.RegularExpressions;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;

namespace Haven.Desktop.Services;

/// <summary>
/// Produces the four context-aware Go actions after the shell is interactive.
/// The model authors the visible label, full instruction, semantic icon and
/// readable accent colour as one unit; invalid output never reaches the UI.
/// </summary>
public sealed class GoSuggestionService(
    IConversationRepository conversations,
    IOllamaClient models,
    UserPreferencesService preferences)
{
    private const int SuggestionCount = 4;
    private static readonly Regex HexColour = new("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> AllowedIcons = new(StringComparer.OrdinalIgnoreCase)
    {
        "chat", "teach", "folder", "refresh", "search", "build", "test", "notes",
        "plan", "browse", "tasks", "studio", "rapid", "experiment", "bookmark", "file"
    };

    public static IReadOnlyList<GoSuggestion> ImmediateDefaults { get; } =
    [
        new("Show my Recent Chats", "Show my recent chats and help me choose where to continue.", "chat", "#913C00"),
        new("Continue teaching me Algebra", "Continue teaching me Algebra from where we last stopped.", "teach", "#07539B"),
        new("Work on Haven", "Help me continue my recent work on Haven in Studio.", "folder", "#5B00A8"),
        new("Recap this Week's Work", "Recap what I worked on this week and suggest the best next step.", "refresh", "#176425")
    ];

    public async Task<IReadOnlyList<GoSuggestion>> GenerateAsync(
        string currentActivity,
        CancellationToken cancellationToken)
    {
        try
        {
            var recentTask = conversations.GetRecentAsync(null, 12, cancellationToken);
            var modelTask = models.GetModelsAsync(cancellationToken);
            await Task.WhenAll(recentTask, modelTask).ConfigureAwait(false);

            var availableModels = await modelTask.ConfigureAwait(false);
            var selectedModel = availableModels.FirstOrDefault(item =>
                                    item.Name.Equals(preferences.DefaultModel, StringComparison.OrdinalIgnoreCase))
                                ?? availableModels.FirstOrDefault();
            if (selectedModel is null) return ImmediateDefaults;

            var recent = await recentTask.ConfigureAwait(false);
            var activity = recent.Count == 0
                ? "No saved activity is available yet."
                : string.Join('\n', recent.Select(item =>
                    $"- {item.UpdatedAt.LocalDateTime:yyyy-MM-dd HH:mm} | {item.Mode} | {item.Title}"));

            var response = await models.CompleteAsync(
                new OllamaChatRequest(
                    selectedModel.Name,
                    [new OllamaMessage("user", $"""
                        Current workspace context: {currentActivity}

                        Recent Haven activity:
                        {activity}

                        Create exactly four useful next-action suggestions for this user. Return only a JSON array.
                        Every object must have these string fields:
                        - label: concise visible text, 3 to 7 words
                        - instruction: the complete instruction Haven should run when clicked
                        - iconKey: one of {string.Join(", ", AllowedIcons.Order())}
                        - colour: a dark six-digit hex colour readable on a pale mint/white button

                        Vary the purpose and colour. Prefer actions connected to current or recent work. Do not mention this generation task.
                        """)],
                    EffortLevel.Low,
                    "You create safe, concise launcher actions. Output strict JSON only, with no Markdown fences or commentary.",
                    Options: new GenerationOptions(0.55, 4096, 0)),
                cancellationToken).ConfigureAwait(false);

            return Parse(response) ?? ImmediateDefaults;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or JsonException or InvalidOperationException)
        {
            return ImmediateDefaults;
        }
    }

    private static IReadOnlyList<GoSuggestion>? Parse(string response)
    {
        var start = response.IndexOf('[', StringComparison.Ordinal);
        var end = response.LastIndexOf(']');
        if (start < 0 || end <= start) return null;

        var payload = JsonSerializer.Deserialize<List<SuggestionPayload>>(
            response[start..(end + 1)],
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (payload is null || payload.Count != SuggestionCount) return null;

        var result = new List<GoSuggestion>(SuggestionCount);
        foreach (var item in payload)
        {
            var label = item.Label?.Trim();
            var instruction = item.Instruction?.Trim();
            var icon = item.IconKey?.Trim();
            var colour = item.Colour?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(label) || label.Length > 54 ||
                string.IsNullOrWhiteSpace(instruction) || instruction.Length > 500 ||
                string.IsNullOrWhiteSpace(icon) || !AllowedIcons.Contains(icon) || !HavenIcon.IsKnown(icon) ||
                string.IsNullOrWhiteSpace(colour) || !HexColour.IsMatch(colour) || !HasReadableLuminance(colour))
                return null;

            result.Add(new GoSuggestion(label, instruction, icon.ToLowerInvariant(), colour));
        }

        return result;
    }

    private static bool HasReadableLuminance(string colour)
    {
        var red = Convert.ToInt32(colour.Substring(1, 2), 16) / 255d;
        var green = Convert.ToInt32(colour.Substring(3, 2), 16) / 255d;
        var blue = Convert.ToInt32(colour.Substring(5, 2), 16) / 255d;
        var luminance = (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
        return luminance <= 0.48;
    }

    private sealed record SuggestionPayload(string? Label, string? Instruction, string? IconKey, string? Colour);
}

public sealed record GoSuggestion(string Label, string Instruction, string IconKey, string Colour);
