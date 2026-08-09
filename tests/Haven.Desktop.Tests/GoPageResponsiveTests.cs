using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Haven.Desktop;
using Haven.Desktop.Events;
using Haven.Desktop.Views.Pages.Go;

namespace Haven.Desktop.Tests;

public sealed class GoPageResponsiveTests
{
    [AvaloniaFact]
    public void Main_window_centres_and_allows_high_dpi_compact_desktop_layouts()
    {
        var window = new MainWindow();
        try
        {
            Assert.Equal(WindowStartupLocation.CenterScreen, window.WindowStartupLocation);
            Assert.Equal(720, window.MinWidth);
            Assert.Equal(520, window.MinHeight);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Composer_and_primary_prompt_stay_inside_compact_desktop_viewport()
    {
        using var page = new GoPage(new HavenEventBus());
        var window = new Window { Width = 820, Height = 600, Content = page };
        try
        {
            window.Show();
            var instruction = page.FindControl<TextBox>("InstructionBox")!;
            var send = page.FindControl<Button>("SendButton")!;
            var prompt = page.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => text.Text == "How can I help?");

            AssertInside(page, instruction);
            AssertInside(page, send);
            AssertInside(page, prompt);
            Assert.True(instruction.Bounds.Width >= 280);
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertInside(Control page, Control child)
    {
        var topLeft = child.TranslatePoint(default, page);
        Assert.NotNull(topLeft);
        Assert.InRange(topLeft.Value.X, 0, page.Bounds.Width);
        Assert.InRange(topLeft.Value.Y, 0, page.Bounds.Height);
        Assert.True(topLeft.Value.X + child.Bounds.Width <= page.Bounds.Width + 0.5);
        Assert.True(topLeft.Value.Y + child.Bounds.Height <= page.Bounds.Height + 0.5);
    }
}
