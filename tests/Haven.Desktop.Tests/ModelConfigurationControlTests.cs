using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Haven.Desktop.Controls;

namespace Haven.Desktop.Tests;

public sealed class ModelConfigurationControlTests
{
    [Theory]
    [InlineData("qwen3.6vl:latest", "Qwen 3.6")]
    [InlineData("openrouter:qwen3.6-vl-32b-instruct", "Qwen 3.6")]
    [InlineData("anthropic:claude-sonnet-4-preview", "claude sonnet 4")]
    [InlineData("llama3.3:70b-q4_k_m", "Llama 3.3")]
    public void PromptBarRemovesModelPackagingFluff(string raw, string expected)
    {
        Assert.Equal(expected, ModelConfigurationControl.SimplifyModelName(raw));
    }

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
