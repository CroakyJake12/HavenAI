using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class VoiceReactionRuntimeTests
{
    [Fact]
    public void LessonVoiceTracksPhaseFromPartialAndFinalSpeech()
    {
        var profile = VoiceProfileCatalog.BuiltIns.Single(profile => profile.Id == "lesson");
        var runtime = new VoiceReactionRuntime(profile);

        var partial = runtime.ObservePartial("Today we are learning about mens rea");
        var final = runtime.ObserveFinal("Today we are learning about mens rea");

        Assert.NotNull(partial);
        Assert.Equal(VoiceReactionKind.LessonPhaseChanged, partial!.Kind);
        Assert.Equal(LessonVoicePhase.Explanation, runtime.LessonPhase);
        Assert.Contains(final, reaction => reaction.Kind == VoiceReactionKind.FinalUnderstanding);
        Assert.Equal(LessonVoicePhase.Explanation, final.Last().LessonPhase);
    }

    [Fact]
    public void RepeatedPartialAndFinalEventsAreSuppressed()
    {
        var runtime = new VoiceReactionRuntime(new VoiceProfile(
            "test-live", "Test", "", "", RequiresRealtimeProcessing: true, ContinuousListening: true));

        Assert.NotNull(runtime.ObservePartial("search for the periodic table"));
        Assert.Null(runtime.ObservePartial("search for the periodic table"));

        var firstFinal = runtime.ObserveFinal("search for the periodic table");
        var duplicateFinal = runtime.ObserveFinal("search for the periodic table");

        Assert.NotEmpty(firstFinal);
        Assert.Empty(duplicateFinal);
        Assert.True(firstFinal.Select(reaction => reaction.Sequence).SequenceEqual(
            firstFinal.Select(reaction => reaction.Sequence).OrderBy(sequence => sequence)));
    }

    [Fact]
    public void RealtimeFlagDisablesPartialInterpretation()
    {
        var runtime = new VoiceReactionRuntime(new VoiceProfile(
            "final-only", "Final only", "", "", RequiresRealtimeProcessing: false));

        Assert.Null(runtime.ObservePartial("look this up while I am talking"));
        Assert.NotEmpty(runtime.ObserveFinal("look this up while I am talking"));
    }

    [Fact]
    public void ActionsAreBoundedAndConsequentialPlanningRequiresConfirmation()
    {
        var runtime = new VoiceReactionRuntime(new VoiceProfile(
            "actions", "Actions", "", "", AllowAutomaticActions: true, ContinuousListening: true));

        var browse = runtime.ObserveFinal("look this up about photosynthesis")
            .Single(reaction => reaction.Kind == VoiceReactionKind.ActionSuggested).Action!;
        var planning = runtime.ObserveFinal("remind me that homework is due tomorrow")
            .Single(reaction => reaction.Kind == VoiceReactionKind.ActionSuggested).Action!;

        Assert.Equal(HavenSurface.Browse, browse.TargetSurface);
        Assert.False(browse.RequiresConfirmation);
        Assert.True(browse.Confidence >= 0.92);
        Assert.Equal(HavenSurface.Plan, planning.TargetSurface);
        Assert.True(planning.RequiresConfirmation);
    }

    [Fact]
    public void NonContinuousProfilesDropPhaseAfterTurnWhileLessonRetainsIt()
    {
        var nonContinuous = new VoiceReactionRuntime(new VoiceProfile(
            "lesson-once", "Lesson once", "", "", ContinuousListening: false));
        var lesson = new VoiceReactionRuntime(VoiceProfileCatalog.BuiltIns.Single(profile => profile.Id == "lesson"));

        nonContinuous.ObserveFinal("Today we are learning about atoms");
        lesson.ObserveFinal("Today we are learning about atoms");
        nonContinuous.ResetTransientAfterTurn();
        lesson.ResetTransientAfterTurn();

        Assert.Equal(LessonVoicePhase.None, nonContinuous.LessonPhase);
        Assert.Equal(LessonVoicePhase.Explanation, lesson.LessonPhase);
    }

    [Fact]
    public void DeliveryStylesMakeLessonAndCommentatorMateriallyDifferent()
    {
        var general = VoiceProfileCatalog.BuiltIns.Single(profile => profile.Id == "general");
        var lesson = VoiceProfileCatalog.BuiltIns.Single(profile => profile.Id == "lesson");
        var commentator = VoiceProfileCatalog.BuiltIns.Single(profile => profile.Id == "commentator");

        var generalStyle = VoiceDeliveryStylePolicy.Resolve(general, null, "Here is the update.");
        var lessonStyle = VoiceDeliveryStylePolicy.Resolve(lesson, null, "Here is the update.");
        var commentatorStyle = VoiceDeliveryStylePolicy.Resolve(commentator, null, "Here is the update!");

        Assert.True(lessonStyle.Pace < generalStyle.Pace);
        Assert.True(lessonStyle.Emphasis > generalStyle.Emphasis);
        Assert.True(commentatorStyle.Pace > generalStyle.Pace + 0.08f);
        Assert.True(commentatorStyle.Energy > lessonStyle.Energy + 0.25f);
        Assert.Equal("Commentary", commentatorStyle.Label);
    }

    [Fact]
    public void LessonKnowledgeCheckSlowsDeliveryAndRaisesEmphasis()
    {
        var lesson = VoiceProfileCatalog.BuiltIns.Single(profile => profile.Id == "lesson");
        var reaction = new VoiceReaction(
            1,
            VoiceReactionKind.LessonPhaseChanged,
            "Lesson phase · Knowledge check",
            0.96,
            DateTimeOffset.UtcNow,
            LessonVoicePhase.KnowledgeCheck);

        var baseline = VoiceDeliveryStylePolicy.Resolve(lesson, null, "What is mens rea?");
        var knowledgeCheck = VoiceDeliveryStylePolicy.Resolve(lesson, reaction, "What is mens rea?");

        Assert.True(knowledgeCheck.Pace < baseline.Pace);
        Assert.True(knowledgeCheck.Emphasis >= 0.72f);
    }

    [Fact]
    public void ProgressivePartialHypothesesAreThrottled()
    {
        var runtime = new VoiceReactionRuntime(new VoiceProfile(
            "live", "Live", "", "", RequiresRealtimeProcessing: true, ContinuousListening: true));

        var first = runtime.ObservePartial("search for the");
        var immediateGrowth = runtime.ObservePartial("search for the periodic table");

        Assert.NotNull(first);
        Assert.Null(immediateGrowth);
    }
}
