using System.Text.Json;
using System.Globalization;
using System.Text.Json.Nodes;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Study;

internal sealed record StudySessionEntry(DateTimeOffset StartedAt, int Minutes);

internal sealed record StudyLessonState(
    string Rag,
    DateTimeOffset? LastReviewedAt,
    IReadOnlyList<StudySessionEntry> Sessions,
    string? Paper,
    string? Section)
{
    public int ProgressPercent => Rag switch
    {
        "green" => 100,
        "amber" => 60,
        "red" => 25,
        _ => 0
    };

    public int TotalMinutes => Sessions.Sum(item => Math.Max(0, item.Minutes));
}

internal static class StudyLessonMetadata
{
    private const string HavenKey = "havenStudy";

    public static StudyLessonState Read(Lesson lesson)
    {
        ArgumentNullException.ThrowIfNull(lesson);
        var root = ParseRoot(lesson.StructureJson);
        var state = root[HavenKey] as JsonObject;
        var rag = NormalizeRag(state?["rag"]?.GetValue<string>());

        DateTimeOffset? reviewed = null;
        var reviewedText = state?["lastReviewedAt"]?.GetValue<string>();
        if (DateTimeOffset.TryParse(reviewedText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            reviewed = parsed;

        var sessions = new List<StudySessionEntry>();
        if (state?["sessions"] is JsonArray sessionArray)
        {
            foreach (var node in sessionArray.OfType<JsonObject>())
            {
                var startedText = node["startedAt"]?.GetValue<string>();
                var minutes = node["minutes"]?.GetValue<int>() ?? 0;
                if (minutes <= 0 ||
                    !DateTimeOffset.TryParse(startedText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var started))
                    continue;
                sessions.Add(new StudySessionEntry(started, minutes));
            }
        }

        return new StudyLessonState(
            rag,
            reviewed,
            sessions.OrderBy(item => item.StartedAt).ToArray(),
            state?["paper"]?.GetValue<string>(),
            state?["section"]?.GetValue<string>());
    }

    public static Lesson WithRag(Lesson lesson, string rag, DateTimeOffset now)
    {
        var root = ParseRoot(lesson.StructureJson);
        var state = EnsureState(root);
        state["rag"] = NormalizeRag(rag);
        state["lastReviewedAt"] = now.ToString("O", CultureInfo.InvariantCulture);
        return lesson with { StructureJson = root.ToJsonString(), UpdatedAt = now };
    }

    public static Lesson WithPaperMetadata(Lesson lesson, string? paper, string? section, DateTimeOffset now)
    {
        var root = ParseRoot(lesson.StructureJson);
        var state = EnsureState(root);
        if (string.IsNullOrWhiteSpace(paper)) state.Remove("paper");
        else state["paper"] = paper.Trim();
        if (string.IsNullOrWhiteSpace(section)) state.Remove("section");
        else state["section"] = section.Trim();
        return lesson with { StructureJson = root.ToJsonString(), UpdatedAt = now };
    }

    public static Lesson AddSession(Lesson lesson, DateTimeOffset startedAt, int minutes, DateTimeOffset now)
    {
        if (minutes <= 0) return lesson;
        var root = ParseRoot(lesson.StructureJson);
        var state = EnsureState(root);
        var sessions = state["sessions"] as JsonArray ?? [];
        state["sessions"] = sessions;
        sessions.Add(new JsonObject
        {
            ["startedAt"] = startedAt.ToString("O", CultureInfo.InvariantCulture),
            ["minutes"] = minutes
        });
        while (sessions.Count > 500) sessions.RemoveAt(0);
        return lesson with { StructureJson = root.ToJsonString(), UpdatedAt = now };
    }

    public static (int CurrentWeekMinutes, int WeeklyAverageMinutes, int TotalMinutes) StudyMinutes(
        IEnumerable<Lesson> lessons,
        DateTimeOffset now)
    {
        var states = lessons.Select(Read).ToArray();
        var sessions = states.SelectMany(item => item.Sessions).ToArray();
        var weekStart = StartOfWeek(now);
        var current = sessions.Where(item => item.StartedAt >= weekStart && item.StartedAt < weekStart.AddDays(7))
            .Sum(item => item.Minutes);

        var priorTotals = new List<int>();
        for (var weeksBack = 1; weeksBack <= 4; weeksBack++)
        {
            var start = weekStart.AddDays(-7 * weeksBack);
            var end = start.AddDays(7);
            priorTotals.Add(sessions.Where(item => item.StartedAt >= start && item.StartedAt < end).Sum(item => item.Minutes));
        }

        var average = priorTotals.Count == 0 ? 0 : (int)Math.Round(priorTotals.Average());
        return (current, average, states.Sum(item => item.TotalMinutes));
    }

    public static (int Points, int Level, int PointsToNext) LearningLevel(
        IEnumerable<Lesson> lessons,
        int completedAssignments)
    {
        var states = lessons.Select(Read).ToArray();
        var points = states.Sum(item => item.TotalMinutes * 2 + (item.Rag switch
        {
            "green" => 120,
            "amber" => 60,
            "red" => 20,
            _ => 0
        })) + Math.Max(0, completedAssignments) * 50;

        var level = Math.Max(1, 1 + points / 1000);
        var remainder = points % 1000;
        return (points, level, remainder == 0 && points > 0 ? 1000 : 1000 - remainder);
    }

    public static string RecommendationReason(StudyLessonState state, DateTimeOffset now)
    {
        if (state.Rag == "red") return "You Found This Hard";
        if (state.LastReviewedAt is { } reviewed && reviewed < now.AddDays(-7)) return "Due a Review";
        if (state.Rag == "none") return "Start Something New";
        if (state.Rag == "amber") return "Build Confidence";
        return "Keep It Fresh";
    }

    public static string RagLabel(string rag) => NormalizeRag(rag) switch
    {
        "red" => "Red",
        "amber" => "Amber",
        "green" => "Green",
        _ => "Not rated"
    };

    public static string NextRag(string rag) => NormalizeRag(rag) switch
    {
        "none" => "red",
        "red" => "amber",
        "amber" => "green",
        _ => "none"
    };

    private static JsonObject ParseRoot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new JsonObject();
        try
        {
            if (JsonNode.Parse(json) is JsonObject root) return root;
        }
        catch (JsonException)
        {
        }

        return new JsonObject { ["legacyStructure"] = json };
    }

    private static JsonObject EnsureState(JsonObject root)
    {
        if (root[HavenKey] is JsonObject state) return state;
        state = new JsonObject();
        root[HavenKey] = state;
        return state;
    }

    private static string NormalizeRag(string? value) =>
        value?.Trim().ToLowerInvariant() is "red" or "amber" or "green"
            ? value.Trim().ToLowerInvariant()
            : "none";

    private static DateTimeOffset StartOfWeek(DateTimeOffset value)
    {
        var local = value.ToLocalTime();
        var delta = ((int)local.DayOfWeek + 6) % 7;
        var monday = local.Date.AddDays(-delta);
        return new DateTimeOffset(monday, local.Offset);
    }
}
