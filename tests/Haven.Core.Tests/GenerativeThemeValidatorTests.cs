/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Core.Tests/GenerativeThemeValidatorTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns GenerativeThemeValidatorTests. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Core.Tests;

/// <summary>
/// Represents generative theme validator tests and keeps its related state and behavior together.
/// </summary>
public sealed class GenerativeThemeValidatorTests
{
    /// <summary>
    /// Stores validator locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly GenerativeThemeValidator _validator = new();

    /// <summary>
    /// Performs the complete dual variant theme with safe layout and pages is accepted step owned by this component.
    /// </summary>
    [Fact]
    public void CompleteDualVariantThemeWithSafeLayoutAndPagesIsAccepted()
    {
        var theme = ValidTheme() with
        {
            Layout = new GenerativeLayoutManifest(
            [
                new("chat.temporary", GenerativeUiCatalog.ChatComposerCenter, 10),
                new("chat.model", GenerativeUiCatalog.ChatComposerRight, 20),
                new("chat.effort", GenerativeUiCatalog.ChatComposerRight, 30, false),
                new("chat.context", GenerativeUiCatalog.ShellHeaderRight, 5, true, "compact")
            ],
            []),
            Pages =
            [
                new GeneratedPageDefinition(
                    "focus",
                    "Focus",
                    "A safe local focus page.",
                    "clock",
                    10,
                    [
                        new GeneratedWidgetDefinition("timer", GeneratedWidgetKind.Timer, "Focus timer", "Stay focused.", null, 1500, []),
                        new GeneratedWidgetDefinition("links", GeneratedWidgetKind.ShortcutGrid, "Shortcuts", null, null, 0, ["new-chat", "studio", "plan"])
                    ])
            ]
        };

        var result = _validator.Validate(theme);

        Assert.True(result.IsValid);
        Assert.NotNull(result.NormalizedTheme);
        Assert.Equal(4, result.NormalizedTheme!.Layout.Placements.Count);
        Assert.Equal(GenerativeUiCatalog.ShellHeaderRight,
            result.NormalizedTheme.Layout.Placements.Single(item => item.ItemId == "chat.context").Region);
        Assert.Single(result.NormalizedTheme.Pages);
        Assert.DoesNotContain(result.Issues, issue => issue.IsError);
    }

    /// <summary>
    /// Performs the unknown ui item is rejected rather than becoming an arbitrary binding step owned by this component.
    /// </summary>
    [Fact]
    public void UnknownUiItemIsRejectedRatherThanBecomingAnArbitraryBinding()
    {
        var theme = ValidTheme() with
        {
            Layout = new GenerativeLayoutManifest(
            [new("chat.secret-admin-button", GenerativeUiCatalog.ShellHeaderRight, 1)],
            [])
        };

        var result = _validator.Validate(theme);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.IsError && issue.Message.Contains("Unknown UI item", StringComparison.Ordinal));
    }

    /// <summary>
    /// Performs the required model selector cannot be hidden step owned by this component.
    /// </summary>
    [Fact]
    public void RequiredModelSelectorCannotBeHidden()
    {
        var placements = GenerativeUiCatalog.DefaultLayout.Placements
            .Select(item => item.ItemId == "chat.model" ? item with { IsVisible = false } : item)
            .ToArray();

        var result = _validator.Validate(ValidTheme() with
        {
            Layout = new GenerativeLayoutManifest(placements, [])
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.IsError && issue.Path.Contains("chat.model", StringComparison.Ordinal));
    }

    /// <summary>
    /// Performs the moving control to unapproved region is rejected step owned by this component.
    /// </summary>
    [Fact]
    public void MovingControlToUnapprovedRegionIsRejected()
    {
        var placements = GenerativeUiCatalog.DefaultLayout.Placements
            .Select(item => item.ItemId == "chat.context" ? item with { Region = "settings.security.secrets" } : item)
            .ToArray();

        var result = _validator.Validate(ValidTheme() with
        {
            Layout = new GenerativeLayoutManifest(placements, [])
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.IsError && issue.Message.Contains("cannot be moved", StringComparison.Ordinal));
    }

    /// <summary>
    /// Performs the duplicate control placement is rejected step owned by this component.
    /// </summary>
    [Fact]
    public void DuplicateControlPlacementIsRejected()
    {
        var result = _validator.Validate(ValidTheme() with
        {
            Layout = new GenerativeLayoutManifest(
            [
                new("chat.model", GenerativeUiCatalog.ChatComposerRight, 1),
                new("chat.model", GenerativeUiCatalog.ShellHeaderRight, 2)
            ],
            [])
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.IsError && issue.Message.Contains("at most once", StringComparison.Ordinal));
    }

    /// <summary>
    /// Performs the generated command button cannot invoke unknown command step owned by this component.
    /// </summary>
    [Fact]
    public void GeneratedCommandButtonCannotInvokeUnknownCommand()
    {
        var theme = ValidTheme() with
        {
            Pages =
            [
                new GeneratedPageDefinition(
                    "unsafe",
                    "Unsafe",
                    "Must fail.",
                    "warning",
                    1,
                    [new GeneratedWidgetDefinition("run", GeneratedWidgetKind.CommandButton, "Run", null, "delete-all-files", 0, [])])
            ]
        };

        var result = _validator.Validate(theme);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.IsError && issue.Message.Contains("approved Haven command", StringComparison.Ordinal));
    }

    /// <summary>
    /// Performs the timer outside safe bounds is rejected step owned by this component.
    /// </summary>
    [Theory]
    [InlineData(4)]
    [InlineData(86401)]
    public void TimerOutsideSafeBoundsIsRejected(int durationSeconds)
    {
        var theme = ValidTheme() with
        {
            Pages =
            [
                new GeneratedPageDefinition(
                    "timer-page",
                    "Timer",
                    "Timer bounds.",
                    "clock",
                    1,
                    [new GeneratedWidgetDefinition("timer", GeneratedWidgetKind.Timer, "Timer", null, null, durationSeconds, [])])
            ]
        };

        var result = _validator.Validate(theme);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.IsError && issue.Message.Contains("24 hours", StringComparison.Ordinal));
    }

    /// <summary>
    /// Performs the missing light variant is rejected step owned by this component.
    /// </summary>
    [Fact]
    public void MissingLightVariantIsRejected()
    {
        var result = _validator.Validate(ValidTheme() with { Light = null! });

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.IsError && issue.Path == "light");
    }

    /// <summary>
    /// Performs the invalid colour is rejected before it can reach avalonia resources step owned by this component.
    /// </summary>
    [Fact]
    public void InvalidColourIsRejectedBeforeItCanReachAvaloniaResources()
    {
        var palette = Palette() with { Accent = "url(file:///secret)" };

        var result = _validator.Validate(ValidTheme() with { Dark = palette });

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.IsError && issue.Path == "dark.accent");
    }

    /// <summary>
    /// Performs the valid theme step owned by this component.
    /// </summary>
    private static GenerativeThemePack ValidTheme()
    {
        var now = DateTimeOffset.UtcNow;
        return new GenerativeThemePack(
            1,
            Guid.NewGuid(),
            "Validated theme",
            "A complete test theme.",
            "Tests",
            GenerativeThemeOrigin.Manual,
            false,
            now,
            now,
            Palette(),
            Palette(),
            new GenerativeThemeTypography("Segoe UI", 14, 1.35, 0),
            new GenerativeThemeShape(10, 14, 16, 1, true, true),
            GenerativeUiCatalog.DefaultLayout,
            []);
    }

    /// <summary>
    /// Performs the palette step owned by this component.
    /// </summary>
    private static GenerativeThemePalette Palette() => new(
        "#FF101010",
        "#FF111111",
        "#FF121212",
        "#FF131313",
        "#FF141414",
        "#FF151515",
        "#FFF0F0F0",
        "#FFE0E0E0",
        "#FFC0C0C0",
        "#FF909090",
        "#FF0078D4",
        "#FFFFFFFF",
        "#FF18344C",
        "#FF60CDFF",
        "#FF153245",
        "#FFFF99A4",
        "#FFFCE4A6",
        "#22FFFFFF",
        "#44FFFFFF",
        "#FF0078D4",
        "#FF182234",
        "#F2182234",
        "#2EFFFFFF",
        "#46FFFFFF",
        "#20FFFFFF",
        "#A060CDFF");
}
