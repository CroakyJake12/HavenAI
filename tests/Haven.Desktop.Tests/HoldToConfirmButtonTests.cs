using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Desktop.HavenUI.Components.Buttons;
using Haven.Desktop.HavenUI.Tokens;

namespace Haven.Desktop.Tests;

public sealed class HoldToConfirmButtonTests
{
    [AvaloniaFact]
    public void Destructive_button_uses_the_mandatory_five_second_default()
    {
        var button = new HoldToConfirmButton { Content = "Delete" };

        Assert.Equal(HavenUiMotion.HoldToConfirm, button.HoldDuration);
        Assert.Contains("danger", button.Classes);
        Assert.Contains("destructive", button.Classes);
    }

    [AvaloniaFact]
    public async Task Completed_hold_invokes_once_and_restores_the_original_label()
    {
        var invoked = 0;
        var button = new HoldToConfirmButton
        {
            Content = "Delete model",
            ActionLabel = "delete model",
            HoldDuration = TimeSpan.FromMilliseconds(60)
        };
        button.Click += (_, _) => invoked++;
        var window = new Window { Content = button };
        try
        {
            window.Show();
            button.BeginHold();
            await Task.Delay(180);

            Assert.Equal(1, invoked);
            Assert.Equal("Delete model", button.Content);
            Assert.Equal(0, button.HoldProgress);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Interrupted_hold_winds_down_without_invoking()
    {
        var invoked = 0;
        var button = new HoldToConfirmButton
        {
            Content = "Delete",
            HoldDuration = TimeSpan.FromMilliseconds(300)
        };
        button.Click += (_, _) => invoked++;
        var window = new Window { Content = button };
        try
        {
            window.Show();
            button.BeginHold();
            await Task.Delay(100);
            Assert.True(button.HoldProgress > 0);
            button.BeginWindDown();
            await Task.Delay(200);

            Assert.Equal(0, invoked);
            Assert.Equal("Delete", button.Content);
            Assert.Equal(0, button.HoldProgress);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Disabling_during_a_hold_cancels_without_invoking()
    {
        var invoked = 0;
        var button = new HoldToConfirmButton
        {
            Content = "Delete",
            HoldDuration = TimeSpan.FromMilliseconds(120)
        };
        button.Click += (_, _) => invoked++;
        var window = new Window { Content = button };
        try
        {
            window.Show();
            button.BeginHold();
            await Task.Delay(45);
            button.IsEnabled = false;
            await Task.Delay(160);

            Assert.Equal(0, invoked);
            Assert.Equal("Delete", button.Content);
            Assert.Equal(0, button.HoldProgress);
        }
        finally
        {
            window.Close();
        }
    }
}
