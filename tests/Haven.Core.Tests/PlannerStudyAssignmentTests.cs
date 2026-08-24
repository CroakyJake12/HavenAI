/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Core.Tests/PlannerStudyAssignmentTests.cs, in the Core automated test suite.
 * What: Protects Study assignment metadata carried by canonical Plan task tags.
 * How: Tests verify round-trip links, user-tag replacement, reserved metadata preservation and unlinking.
 * Why: Study links must survive ordinary Plan tag edits without exposing or duplicating internal relationship state.
 * Maintenance: Treat the reserved tag format as persisted compatibility data.
 */

using System.Text.Json;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class PlannerStudyAssignmentTests
{
    [Fact]
    public void AttachRoundTripsStudyContextAndPreservesExistingTags()
    {
        var subjectId = Guid.NewGuid();
        var lessonId = Guid.NewGuid();
        var attached = PlannerStudyAssignmentTags.Attach(
            JsonSerializer.Serialize(new[] { "revision", "haven:other:metadata" }),
            subjectId,
            lessonId);

        Assert.True(PlannerStudyAssignmentTags.TryRead(attached, out var link));
        Assert.Equal(subjectId, link.SubjectId);
        Assert.Equal(lessonId, link.LessonId);
        Assert.Equal(new[] { "revision" }, PlannerStudyAssignmentTags.GetUserTags(attached));
        Assert.Contains("haven:other:metadata", JsonSerializer.Deserialize<string[]>(attached)!);
    }

    [Fact]
    public void ReplaceUserTagsPreservesReservedStudyLinkAndRejectsReservedSpoofing()
    {
        var subjectId = Guid.NewGuid();
        var original = PlannerStudyAssignmentTags.Attach(JsonSerializer.Serialize(new[] { "old" }), subjectId, null);

        var updated = PlannerStudyAssignmentTags.ReplaceUserTags(original, new[] { "new", "haven:study:subject:ffffffffffffffffffffffffffffffff" });

        Assert.True(PlannerStudyAssignmentTags.TryRead(updated, out var link));
        Assert.Equal(subjectId, link.SubjectId);
        Assert.Equal(new[] { "new" }, PlannerStudyAssignmentTags.GetUserTags(updated));
    }

    [Fact]
    public void AttachReplacesPreviousStudyLinkWithoutDroppingOtherMetadata()
    {
        var firstSubjectId = Guid.NewGuid();
        var secondSubjectId = Guid.NewGuid();
        var secondLessonId = Guid.NewGuid();
        var first = PlannerStudyAssignmentTags.Attach(
            JsonSerializer.Serialize(new[] { "revision", "haven:other:metadata" }),
            firstSubjectId,
            null);

        var relinked = PlannerStudyAssignmentTags.Attach(first, secondSubjectId, secondLessonId);

        Assert.True(PlannerStudyAssignmentTags.TryRead(relinked, out var link));
        Assert.Equal(secondSubjectId, link.SubjectId);
        Assert.Equal(secondLessonId, link.LessonId);
        var tags = JsonSerializer.Deserialize<string[]>(relinked)!;
        Assert.Contains("revision", tags);
        Assert.Contains("haven:other:metadata", tags);
        Assert.Single(tags, tag => tag.StartsWith(PlannerStudyAssignmentTags.SubjectPrefix, StringComparison.OrdinalIgnoreCase));
        Assert.Single(tags, tag => tag.StartsWith(PlannerStudyAssignmentTags.LessonPrefix, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(PlannerStudyAssignmentTags.SubjectPrefix + firstSubjectId.ToString("N"), tags);
    }

    [Fact]
    public void DetachRemovesOnlyStudyMetadata()
    {
        var subjectId = Guid.NewGuid();
        var linked = PlannerStudyAssignmentTags.Attach(
            JsonSerializer.Serialize(new[] { "exam", "haven:other:metadata" }),
            subjectId,
            null);

        var detached = PlannerStudyAssignmentTags.Detach(linked);

        Assert.False(PlannerStudyAssignmentTags.TryRead(detached, out _));
        var tags = JsonSerializer.Deserialize<string[]>(detached)!;
        Assert.Contains("exam", tags);
        Assert.Contains("haven:other:metadata", tags);
    }
}
