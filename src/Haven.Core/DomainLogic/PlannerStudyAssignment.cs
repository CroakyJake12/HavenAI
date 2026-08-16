/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Core/DomainLogic/PlannerStudyAssignment.cs, in the dependency-free Core layer.
 * What: Defines Study assignment links carried by canonical PlannerTask metadata and helpers for preserving user/system tags.
 * How: Stable reserved tags identify a Study subject/lesson while the PlannerTask remains the sole task/deadline/status record.
 * Why: Study assignments must reference real Plan state instead of creating a second assignment database or duplicated deadline state.
 * Maintenance: Keep reserved tags backward-compatible, preserve unrelated system tags, and never expose reserved tags as editable user tags.
 */

using System.Text.Json;

namespace Haven.Core;

public sealed record PlannerStudyLink(Guid SubjectId, Guid? LessonId);

public sealed record PlannerStudyAssignment(PlannerStudyLink Link, PlannerTask Task)
{
    public Guid PlanTaskId => Task.Id;
}

public static class PlannerStudyAssignmentTags
{
    public const string ReservedPrefix = "haven:";
    public const string AssignmentMarker = "haven:study:assignment";
    public const string SubjectPrefix = "haven:study:subject:";
    public const string LessonPrefix = "haven:study:lesson:";

    public static bool IsReserved(string? tag) =>
        !string.IsNullOrWhiteSpace(tag) && tag.StartsWith(ReservedPrefix, StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<string> GetUserTags(string? tagsJson) =>
        ReadTags(tagsJson).Where(tag => !IsReserved(tag)).ToArray();

    public static string ReplaceUserTags(string? existingTagsJson, IEnumerable<string> userTags)
    {
        ArgumentNullException.ThrowIfNull(userTags);
        var tags = ReadTags(existingTagsJson)
            .Where(IsReserved)
            .Concat(userTags
                .Select(tag => tag?.Trim() ?? string.Empty)
                .Where(tag => tag.Length > 0 && !IsReserved(tag)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return JsonSerializer.Serialize(tags);
    }

    public static string Attach(string? existingTagsJson, Guid subjectId, Guid? lessonId)
    {
        if (subjectId == Guid.Empty) throw new ArgumentException("Study subject ID is required.", nameof(subjectId));
        if (lessonId == Guid.Empty) throw new ArgumentException("Study lesson ID cannot be empty.", nameof(lessonId));

        var tags = ReadTags(existingTagsJson)
            .Where(tag => !IsStudyTag(tag))
            .ToList();
        tags.Add(AssignmentMarker);
        tags.Add(SubjectPrefix + subjectId.ToString("N"));
        if (lessonId is not null) tags.Add(LessonPrefix + lessonId.Value.ToString("N"));
        return JsonSerializer.Serialize(tags.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    public static string Detach(string? existingTagsJson) =>
        JsonSerializer.Serialize(ReadTags(existingTagsJson).Where(tag => !IsStudyTag(tag)));

    public static bool TryRead(string? tagsJson, out PlannerStudyLink link)
    {
        link = default!;
        var tags = ReadTags(tagsJson);
        if (!tags.Contains(AssignmentMarker, StringComparer.OrdinalIgnoreCase)) return false;

        var subjects = ParseIds(tags, SubjectPrefix);
        var lessons = ParseIds(tags, LessonPrefix);
        if (subjects.Count != 1 || lessons.Count > 1) return false;

        link = new PlannerStudyLink(subjects[0], lessons.Count == 0 ? null : lessons[0]);
        return true;
    }

    private static bool IsStudyTag(string tag) =>
        tag.Equals(AssignmentMarker, StringComparison.OrdinalIgnoreCase)
        || tag.StartsWith(SubjectPrefix, StringComparison.OrdinalIgnoreCase)
        || tag.StartsWith(LessonPrefix, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<Guid> ParseIds(IReadOnlyList<string> tags, string prefix) =>
        tags.Where(tag => tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(tag => tag[prefix.Length..])
            .Select(text => Guid.TryParseExact(text, "N", out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

    private static IReadOnlyList<string> ReadTags(string? tagsJson)
    {
        if (string.IsNullOrWhiteSpace(tagsJson)) return [];
        try
        {
            using var document = JsonDocument.Parse(tagsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return [];
            return document.RootElement.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()?.Trim() ?? string.Empty)
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
