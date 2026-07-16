using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Haven.Automations;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Tests;

public sealed class ExperienceAutomationAndCallTests
{
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

    [AvaloniaFact]
    public void PlanAutomationBuilderConstructsOneProductionFlyout()
    {
        using var control = new PlanAutomationControl();
        var window = new Window { Content = control };
        try
        {
            window.Show();
            Assert.Equal("Automations", control.Content);
            Assert.NotNull(control.Flyout);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void WeeklyScheduleRoundTripsThroughFriendlyDraft()
    {
        var draft = new AutomationScheduleDraft(
            new DateTimeOffset(2026, 7, 20, 9, 30, 0, TimeSpan.Zero),
            new TimeOnly(17, 45),
            DayOfWeek.Thursday,
            3,
            180);

        var json = AutomationScheduleComposer.Compose(AutomationScheduleKind.Weekly, draft);
        var parsed = AutomationScheduleComposer.Parse(
            AutomationScheduleKind.Weekly,
            json,
            DateTimeOffset.UtcNow);

        Assert.Equal(DayOfWeek.Thursday, parsed.DayOfWeek);
        Assert.Equal(new TimeOnly(17, 45), parsed.Time);
        Assert.Contains("Thursday", AutomationScheduleComposer.Describe(AutomationScheduleKind.Weekly, parsed));
    }

    [Fact]
    public void ConditionScheduleClampsToWorkerSupportedMinimum()
    {
        var json = AutomationScheduleComposer.Compose(
            AutomationScheduleKind.ConditionWatch,
            new AutomationScheduleDraft(DateTimeOffset.UtcNow, new TimeOnly(8, 0), DayOfWeek.Monday, 1, 5));
        var parsed = AutomationScheduleComposer.Parse(
            AutomationScheduleKind.ConditionWatch,
            json,
            DateTimeOffset.UtcNow);

        Assert.Equal(60, parsed.ConditionIntervalMinutes);
    }

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
