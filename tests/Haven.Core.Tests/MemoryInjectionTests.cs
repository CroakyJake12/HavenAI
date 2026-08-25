using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class MemoryInjectionTests
{
    private static KnowledgeRecord Record(
        string title,
        bool pinned = false,
        string summary = "summary text",
        KnowledgeCategory category = KnowledgeCategory.LearnMe) =>
        new(Guid.NewGuid(), category, "topic", title, summary,
            KnowledgePrivacyClass.Private, 0.8, pinned,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            ExpiresAt: null, LearnedBecause: "user said so", Sources: []);

    private static string Bullet(string text) => "• " + text;

    [Fact]
    public void EmptyRecordsProduceEmptyDirective()
    {
        Assert.Equal(string.Empty, MemoryInjection.BuildDirective([]));
    }

    [Fact]
    public void DirectiveRendersHeaderAndOneBulletPerRecord()
    {
        var directive = MemoryInjection.BuildDirective([Record("alpha"), Record("beta")]);

        Assert.StartsWith(MemoryInjection.DirectiveHeader + "\n", directive);
        Assert.Contains(Bullet("alpha"), directive);
        Assert.Contains(Bullet("beta"), directive);
        Assert.Equal(3, directive.Split('\n').Length);
    }

    [Fact]
    public void LongContentIsTrimmedTo160Characters()
    {
        var longTitle = new string('x', 300);

        var directive = MemoryInjection.BuildDirective([Record(longTitle)]);

        Assert.Contains(Bullet(longTitle[..160]), directive);
        Assert.DoesNotContain(longTitle[..161], directive);
    }

    [Fact]
    public void FallsBackToSummaryWhenTitleIsBlank()
    {
        var directive = MemoryInjection.BuildDirective([Record("   ", summary: "remembered detail")]);

        Assert.Contains(Bullet("remembered detail"), directive);
    }

    [Fact]
    public void BlankEntriesAreSkippedWithoutBreakingHeader()
    {
        var directive = MemoryInjection.BuildDirective([Record("  ", summary: " "), Record("real note")]);

        Assert.Equal(MemoryInjection.DirectiveHeader + "\n" + Bullet("real note"), directive);
    }

    [Fact]
    public void DirectiveCapsAtEightRecords()
    {
        var records = Enumerable.Range(0, 12).Select(index => Record($"note {index}")).ToArray();

        var directive = MemoryInjection.BuildDirective(records);

        Assert.Equal(9, directive.Split('\n').Length);
        Assert.DoesNotContain("note 8", directive);
        Assert.DoesNotContain("note 11", directive);
    }

    [Fact]
    public void VeryLowInjectsPinnedOnlyWithMaximumOfTwo()
    {
        var records = new[]
        {
            Record("pinned one", pinned: true),
            Record("unpinned one"),
            Record("pinned two", pinned: true),
            Record("pinned three", pinned: true)
        };

        var selected = MemoryInjection.SelectForLevel(PersonalityLevel.VeryLow, records);

        Assert.Equal(["pinned one", "pinned two"], selected.Select(record => record.Title).ToArray());
        Assert.True(MemoryInjection.ShouldInclude(PersonalityLevel.VeryLow, selected.Count));

        var directive = MemoryInjection.BuildDirective(selected);
        Assert.Contains(Bullet("pinned one"), directive);
        Assert.DoesNotContain("unpinned one", directive);
        Assert.DoesNotContain("pinned three", directive);
    }

    [Fact]
    public void VeryLowAndLowStaySilentWithoutPinnedRecords()
    {
        var records = new[] { Record("one"), Record("two"), Record("three") };

        foreach (var level in new[] { PersonalityLevel.VeryLow, PersonalityLevel.Low })
        {
            var selected = MemoryInjection.SelectForLevel(level, records);

            Assert.Empty(selected);
            Assert.False(MemoryInjection.ShouldInclude(level, selected.Count));
            Assert.Equal(string.Empty, MemoryInjection.BuildDirective(selected));
        }
    }

    [Fact]
    public void ModerateCapsAtFiveIncludingPinned()
    {
        var records = new[]
        {
            Record("pinned", pinned: true),
            Record("n1"), Record("n2"), Record("n3"), Record("n4"), Record("n5"), Record("n6")
        };

        var selected = MemoryInjection.SelectForLevel(PersonalityLevel.Moderate, records);

        Assert.Equal(5, selected.Count);
        Assert.Equal("pinned", selected[0].Title);
        Assert.True(MemoryInjection.ShouldInclude(PersonalityLevel.Moderate, selected.Count));
    }

    [Fact]
    public void HighAndVeryHighCapAtEight()
    {
        var records = Enumerable.Range(0, 12).Select(index => Record($"note {index}")).ToArray();

        foreach (var level in new[] { PersonalityLevel.High, PersonalityLevel.VeryHigh })
        {
            var selected = MemoryInjection.SelectForLevel(level, records);

            Assert.Equal(8, selected.Count);
            Assert.True(MemoryInjection.ShouldInclude(level, selected.Count));
        }
    }

    [Fact]
    public void ShouldIncludeMatrixFollowsSurvivingRecordCount()
    {
        foreach (var level in Enum.GetValues<PersonalityLevel>())
        {
            Assert.False(MemoryInjection.ShouldInclude(level, 0));
            Assert.False(MemoryInjection.ShouldInclude(level, -1));
            Assert.True(MemoryInjection.ShouldInclude(level, 1));
            Assert.True(MemoryInjection.ShouldInclude(level, 8));
        }
    }

    [Fact]
    public void NonLearnMeRecordsAreNotSpecialCasedBySelection()
    {
        var worldRecord = Record("world fact", category: KnowledgeCategory.WorldKnowledge);

        Assert.Equal([worldRecord], MemoryInjection.SelectForLevel(PersonalityLevel.VeryHigh, [worldRecord]));
        Assert.Empty(MemoryInjection.SelectForLevel(PersonalityLevel.Low, [worldRecord]));
    }
}
