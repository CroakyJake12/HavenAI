/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Desktop.Tests/ModelConfigurationControlTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns ModelConfigurationControlTests. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Haven.Desktop.Controls;

namespace Haven.Desktop.Tests;

/// <summary>
/// Represents model configuration control tests and keeps its related state and behavior together.
/// </summary>
public sealed class ModelConfigurationControlTests
{
    /// <summary>
    /// Performs the prompt bar removes model packaging fluff step owned by this component.
    /// </summary>
    [Theory]
    [InlineData("qwen3.6vl:latest", "Qwen 3.6")]
    [InlineData("openrouter:qwen3.6-vl-32b-instruct", "Qwen 3.6")]
    [InlineData("anthropic:claude-sonnet-4-preview", "claude sonnet 4")]
    [InlineData("llama3.3:70b-q4_k_m", "Llama 3.3")]
    public void PromptBarRemovesModelPackagingFluff(string raw, string expected)
    {
        Assert.Equal(expected, ModelConfigurationControl.SimplifyModelName(raw));
    }

    /// <summary>
    /// Performs the unified control constructs one compact button and one flyout step owned by this component.
    /// </summary>
    [AvaloniaFact]
    public void UnifiedControlConstructsOneCompactButtonAndOneFlyout()
    {
        using var control = new ModelConfigurationControl();
        var window = new Window { Content = control };
        try
        {
            window.Show();
            var button = control.GetVisualDescendants().OfType<Button>().Single();
            Assert.NotNull(button.Flyout);
            Assert.Contains("Choose model", button.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));
        }
        finally
        {
            window.Close();
        }
    }
}
