using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Pages.Chat;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class ChatGeneratedContentRecoveryTests
{
    [AvaloniaFact]
    public void Retry_action_is_canonical_accessible_and_invokes_once()
    {
        var invocations = 0;
        var retry = ChatGeneratedContentRecovery.CreateRetryButton(() => invocations++);
        var root = new Haven.UI.Components.Page { Layout = HavenLayout.Vertical };
        root.Add(retry);
        var host = new HavenSceneControl { Root = root };
        var window = new Window { Width = 600, Height = 400, Content = host };
        try
        {
            window.Show();
            window.UpdateLayout();
            Assert.Equal(ChatGeneratedContentRecovery.RetryLabel, retry.Content);
            Assert.Equal(ButtonVariant.Secondary, retry.Variant);
            Assert.Equal(ChatGeneratedContentRecovery.RetryLabel, retry.Accessibility.AccessibleName);

            var router = new HavenInputRouter(root);
            var point = new HavenPoint(retry.Bounds.X + retry.Bounds.Width / 2, retry.Bounds.Y + retry.Bounds.Height / 2);
            router.PointerPressed(point);
            Assert.True(router.PointerReleased(point));
            Assert.Equal(1, invocations);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }
}
