/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Desktop.Tests/ExperienceAutomationAndCallTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns ExperienceAutomationAndCallTests. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Haven.Application.Tasks;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Tests;

/// <summary>
/// Represents experience automation and call tests and keeps its related state and behavior together.
/// </summary>
public sealed class ExperienceAutomationAndCallTests
{
    /// <summary>
    /// Performs the experience host keeps home fixed and provides full mode viewport entry step owned by this component.
    /// </summary>
    [AvaloniaFact]
    public void ExperienceHostKeepsHomeFixedAndProvidesFullModeViewportEntry()
    {
        var existingShell = new Grid();
        using var host = new ExperienceShellHost(existingShell);
        var window = new Window { Content = host };
        try
        {
            window.Show();
            Assert.Equal(2, host.ColumnDefinitions.Count);
            Assert.Equal(1, Grid.GetColumn(existingShell));
            Assert.Equal(6, ExperienceShellHost.MaximumPinnedModes);
            var buttons = host.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.Contains(buttons, button => button.Name == "ExperienceHomeButton");
            Assert.Contains(buttons, button => button.Name == "ExperienceAllModesButton");
            Assert.Equal("ExperienceHomeButton", buttons.First(button => button.Name?.StartsWith("Experience", StringComparison.Ordinal) == true).Name);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Performs the plan automation builder constructs one production flyout step owned by this component.
    /// </summary>
    [AvaloniaFact]
    public void PlanAutomationBuilderConstructsOneProductionFlyout()
    {
        using var control = new PlanScheduledTaskControl();
        var window = new Window { Content = control };
        try
        {
            window.Show();
            Assert.NotNull(control.Content);
            Assert.Contains(control.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Scheduled tasks"));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Performs the weekly schedule round trips through friendly draft step owned by this component.
    /// </summary>
    [Fact]
    public void WeeklyScheduleRoundTripsThroughFriendlyDraft()
    {
        var draft = new ScheduledTaskScheduleDraft(
            new DateTimeOffset(2026, 7, 20, 9, 30, 0, TimeSpan.Zero),
            new TimeOnly(17, 45),
            DayOfWeek.Thursday,
            3,
            180);

        var json = ScheduledTaskScheduleComposer.Compose(AutomationScheduleKind.Weekly, draft);
        var parsed = ScheduledTaskScheduleComposer.Parse(
            AutomationScheduleKind.Weekly,
            json,
            DateTimeOffset.UtcNow);

        Assert.Equal(DayOfWeek.Thursday, parsed.DayOfWeek);
        Assert.Equal(new TimeOnly(17, 45), parsed.Time);
        Assert.Contains("Thursday", ScheduledTaskScheduleComposer.Describe(AutomationScheduleKind.Weekly, parsed));
    }

    /// <summary>
    /// Performs the condition schedule clamps to worker supported minimum step owned by this component.
    /// </summary>
    [Fact]
    public void ConditionScheduleClampsToWorkerSupportedMinimum()
    {
        var json = ScheduledTaskScheduleComposer.Compose(
            AutomationScheduleKind.ConditionWatch,
            new ScheduledTaskScheduleDraft(DateTimeOffset.UtcNow, new TimeOnly(8, 0), DayOfWeek.Monday, 1, 5));
        var parsed = ScheduledTaskScheduleComposer.Parse(
            AutomationScheduleKind.ConditionWatch,
            json,
            DateTimeOffset.UtcNow);

        Assert.Equal(60, parsed.ConditionIntervalMinutes);
    }

    /// <summary>
    /// Performs the condition parser accepts structured evidence and fails closed on free text step owned by this component.
    /// </summary>
    [Fact]
    public void ConditionParserAcceptsStructuredEvidenceAndFailsClosedOnFreeText()
    {
        var met = ScheduledTaskConditionParser.Parse("{\"conditionMet\":true,\"report\":\"The release is available.\"}");
        var malformed = ScheduledTaskConditionParser.Parse("It probably happened, but I am not sure.");

        Assert.True(met.ConditionMet);
        Assert.Contains("release", met.Report, StringComparison.OrdinalIgnoreCase);
        Assert.False(malformed.ConditionMet);
        Assert.Contains("treated as not met", malformed.Report, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Performs the call transcript export marks interrupted and partial turns step owned by this component.
    /// </summary>
    [Fact]
    public void CallTranscriptExportMarksInterruptedAndPartialTurns()
    {
        var user = new CallTranscriptItemViewModel(
            Guid.NewGuid(), MessageRole.User, "Please check this.", false,
            new DateTimeOffset(2026, 7, 16, 18, 0, 0, TimeSpan.Zero));
        var assistant = new CallTranscriptItemViewModel(
            Guid.NewGuid(), MessageRole.Assistant, "I started checking it.", true,
            new DateTimeOffset(2026, 7, 16, 18, 1, 0, TimeSpan.Zero))
        {
            WasInterrupted = true
        };

        var markdown = CallTranscriptExportFormatter.ToMarkdown(
            [user, assistant],
            new DateTimeOffset(2026, 7, 16, 18, 2, 0, TimeSpan.Zero));

        Assert.Contains("# Haven Call Transcript", markdown);
        Assert.Contains("## You", markdown);
        Assert.Contains("## Haven", markdown);
        Assert.Contains("Response was interrupted", markdown);
        Assert.Contains("still partial", markdown);
    }
}
