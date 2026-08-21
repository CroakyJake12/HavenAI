using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Pages.Chat;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class ChatHavenSceneTests
{
    [Fact]
    public void Chat_scene_uses_canonical_chatbox_prefab_and_dynamic_message_host()
    {
        using var scene = new ChatHavenScene();

        Assert.Equal("Chatbox", scene.Chatbox.PrefabID);
        Assert.True(scene.Instruction.Multiline);
        Assert.True(scene.Instruction.SubmitOnEnter);
        Assert.Equal("Ask Haven anything", scene.Instruction.Accessibility.AccessibleName);
        Assert.Equal("Add to chat", scene.AddButton.Accessibility.AccessibleName);
        Assert.Equal("Send message", scene.SendButton.Accessibility.AccessibleName);
        Assert.Equal("Messages", scene.Messages.Name);
    }

    [AvaloniaFact]
    public void Sending_state_turns_primary_action_into_stop_and_restores_send()
    {
        using var scene = new ChatHavenScene();
        var host = new HavenSceneControl { Root = scene.Root };
        var window = new Window { Width = 1000, Height = 760, Content = host };
        window.Show();
        window.UpdateLayout();
        var router = new HavenInputRouter(scene.Root);
        try
        {
        var sends = 0;
        var stops = 0;
        scene.SendRequested += (_, _) => sends++;
        scene.StopRequested += (_, _) => stops++;

        scene.SetSending(true, modelAvailable: true);

        Assert.True(scene.Instruction.GetValue(HavenProperties.Enabled));
        Assert.True(scene.SendButton.GetValue(HavenProperties.Enabled));
        Assert.Equal("Stop response", scene.SendButton.Accessibility.AccessibleName);
        Assert.Equal("close", scene.SendIcon.Key);
        Click(router, scene.SendButton);
        Assert.Equal(0, sends);
        Assert.Equal(1, stops);

        scene.SetSending(false, modelAvailable: true);

        Assert.True(scene.Instruction.GetValue(HavenProperties.Enabled));
        Assert.True(scene.SendButton.GetValue(HavenProperties.Enabled));
        Assert.Equal("Send message", scene.SendButton.Accessibility.AccessibleName);
        Assert.Equal("arrow-up", scene.SendIcon.Key);
        Click(router, scene.SendButton);
        Assert.Equal(1, sends);
        Assert.Equal(1, stops);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [Fact]
    public void Tool_activity_rows_share_message_runtime_and_restore_in_order()
    {
        using var scene = new ChatHavenScene();
        var assistantId = Guid.NewGuid();
        var toolId = Guid.NewGuid();
        var activity = new ToolActivity(toolId, "Browser search", "Found 3 sources", true, TimeSpan.FromMilliseconds(850), DateTimeOffset.UtcNow, 3, 1);
        var assistant = new ChatSceneMessage(assistantId, MessageRole.Assistant, "Answer", "Haven", false, string.Empty, [activity]);
        var user = new ChatSceneMessage(Guid.NewGuid(), MessageRole.User, "Thanks", string.Empty, false, string.Empty);

        scene.SyncMessages([assistant, user]);

        Assert.Equal(3, scene.Messages.Items.Count);
        var tool = scene.Messages.Items[1];
        Assert.Equal("Completed", tool.GetComponent<Haven.UI.Components.Text>("Status").Content);
        Assert.Equal("Browser search", tool.GetComponent<Haven.UI.Components.Text>("Title").Content);
        Assert.Equal("Found 3 sources", tool.GetComponent<Haven.UI.Components.Text>("Detail").Content);
        Assert.Contains("0.9s", tool.GetComponent<Haven.UI.Components.Text>("Meta").Content);
        Assert.Contains("+3 -1", tool.GetComponent<Haven.UI.Components.Text>("Meta").Content);

        scene.SyncMessages([assistant with { ToolActivities = [] }, user]);

        Assert.Equal(2, scene.Messages.Items.Count);
    }

    [Fact]
    public void Streaming_assistant_update_preserves_dynamic_item_and_markdown_identity()
    {
        using var scene = new ChatHavenScene();
        var userId = Guid.NewGuid();
        var assistantId = Guid.NewGuid();
        scene.SyncMessages([
            new ChatSceneMessage(userId, MessageRole.User, "Hello", "", false, ""),
            new ChatSceneMessage(assistantId, MessageRole.Assistant, "Hi", "Haven", true, "Thinking")
        ]);

        Assert.Equal(2, scene.Messages.Items.Count);
        var assistantItem = scene.Messages.Items[1];
        var markdown = assistantItem.GetComponent<Markdown>("Body");
        Assert.Equal("Hi", markdown.Content);

        scene.UpdateMessage(new ChatSceneMessage(assistantId, MessageRole.Assistant, "Hi again", "Haven", true, "Thinking"));

        Assert.Same(assistantItem, scene.Messages.Items[1]);
        Assert.Same(markdown, assistantItem.GetComponent<Markdown>("Body"));
        Assert.Equal("Hi again", markdown.Content);
    }

    [Fact]
    public void Generated_content_mount_survives_streaming_message_updates_without_reparenting()
    {
        using var scene = new ChatHavenScene();
        var assistantId = Guid.NewGuid();
        scene.SyncMessages([new ChatSceneMessage(assistantId, MessageRole.Assistant, "Preparing", "Haven", true, string.Empty)]);
        var generated = new Container { Name = "GeneratedTestSurface" };
        generated.Add(new Text { Content = "Interactive result" });

        scene.SetGeneratedContent(assistantId, [generated]);
        var item = scene.Messages.GetItem(assistantId.ToString("N"));
        var host = item.GetComponent<Container>("GeneratedContent");
        Assert.Same(generated, Assert.Single(host.Children));

        scene.UpdateMessage(new ChatSceneMessage(assistantId, MessageRole.Assistant, "Done", "Haven", false, string.Empty));

        Assert.Same(generated, Assert.Single(host.Children));
        Assert.Same(host, item.GetComponent<Container>("GeneratedContent"));
    }

    [Fact]
    public void Chatbox_add_component_can_be_disabled_without_disabling_composer()
    {
        using var scene = new ChatHavenScene();

        scene.SetAddEnabled(false);
        Assert.False(scene.Chatbox.IsComponentEnabled("AddMenu"));
        Assert.Equal(HavenVisibility.Collapsed, scene.AddButton.GetValue(HavenProperties.Visibility));
        Assert.True(scene.Instruction.GetValue(HavenProperties.Enabled));
        Assert.True(scene.SendButton.GetValue(HavenProperties.Enabled));

        scene.SetAddEnabled(true);
        Assert.True(scene.Chatbox.IsComponentEnabled("AddMenu"));
    }

    [Fact]
    public void Chat_scene_uses_same_shared_add_menu_prefab_as_go()
    {
        using var scene = new ChatHavenScene();

        Assert.Equal("ChatAddMenu", scene.AddMenuPrefab.PrefabID);
        Assert.Equal(HavenVisibility.Collapsed, scene.AddOverlay.GetValue(HavenProperties.Visibility));

        scene.ShowAddMenu();
        Assert.Equal(HavenVisibility.Visible, scene.AddOverlay.GetValue(HavenProperties.Visibility));
        Assert.Equal("Manage Responses", scene.AddMenuPrefab.DescendantsAndSelf().OfType<Text>().First(text => text.Content == "Manage Responses").Content);

        scene.HideAddMenu();
        Assert.Equal(HavenVisibility.Collapsed, scene.AddOverlay.GetValue(HavenProperties.Visibility));
    }

    [AvaloniaFact]
    public void Chat_composer_stays_inside_viewport_and_plus_button_opens_shared_add_menu()
    {
        using var scene = new ChatHavenScene();
        var host = new HavenSceneControl { Root = scene.Root };
        var window = new Window { Width = 1000, Height = 760, Content = host };
        try
        {
            window.Show();
            window.UpdateLayout();

            Assert.True(scene.Chatbox.Bounds.Bottom <= host.SurfaceMetrics.Viewport.Height + 0.5);
            Assert.True(scene.Chatbox.Bounds.Y >= 0);
            Assert.Equal(HavenVisibility.Collapsed, scene.AddOverlay.GetValue(HavenProperties.Visibility));

            var router = new HavenInputRouter(scene.Root);
            Click(router, scene.AddButton);
            window.UpdateLayout();

            Assert.Equal(HavenVisibility.Visible, scene.AddOverlay.GetValue(HavenProperties.Visibility));
            Assert.True(scene.AddOverlay.Bounds.Bottom <= host.SurfaceMetrics.Viewport.Height + 0.5);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Chat_message_action_controls_are_compact_more_icons_with_accessible_names()
    {
        using var scene = new ChatHavenScene();
        var userId = Guid.NewGuid();
        var assistantId = Guid.NewGuid();
        scene.SyncMessages([
            new ChatSceneMessage(userId, MessageRole.User, "Hello", "", false, ""),
            new ChatSceneMessage(assistantId, MessageRole.Assistant, "Hi", "Haven", false, "")
        ]);
        var host = new HavenSceneControl { Root = scene.Root };
        var window = new Window { Width = 1000, Height = 760, Content = host };
        try
        {
            window.Show();
            window.UpdateLayout();

            var userMenu = scene.Messages.GetItem(userId.ToString("N")).GetComponent<Haven.UI.Components.Button>("MessageMenu");
            var assistantMenu = scene.Messages.GetItem(assistantId.ToString("N")).GetComponent<Haven.UI.Components.Button>("MessageMenu");
            Assert.Equal(string.Empty, userMenu.Content);
            Assert.Equal(string.Empty, assistantMenu.Content);
            Assert.Equal("more", userMenu.IconKey);
            Assert.Equal("more", assistantMenu.IconKey);
            Assert.Equal("Message actions", userMenu.Accessibility.AccessibleName);
            Assert.Equal("Response actions", assistantMenu.Accessibility.AccessibleName);
            Assert.InRange(userMenu.Bounds.Width, 31.5, 32.5);
            Assert.InRange(assistantMenu.Bounds.Width, 31.5, 32.5);
            Assert.True(new HavenSceneRenderer().Render(scene.Root).OfType<HavenIconCommand>().Count(command => command.Key == "more") >= 2);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Choice_prompt_uses_bounded_raised_card_over_overlay_scrim()
    {
        using var scene = new ChatHavenScene();
        scene.ShowChoicePrompt("Resolve Problems", "Choose what is going wrong.",
        [
            ("Hallucinating", () => { }),
            ("Looping", () => { }),
            ("Something Else", () => { })
        ]);
        var host = new HavenSceneControl { Root = scene.Root };
        var window = new Window { Width = 1000, Height = 760, Content = host };
        try
        {
            window.Show();
            window.UpdateLayout();
            var overlay = scene.Root.DescendantsAndSelf().OfType<Container>().Single(element => element.Name == "ChatModalOverlay");
            var card = scene.Root.DescendantsAndSelf().OfType<Container>().Single(element => element.Name == "ChatModalCard");
            Assert.Equal("Overlay", overlay.GetValue(HavenProperties.Background));
            Assert.Equal("SurfaceRaised", card.GetValue(HavenProperties.Background));
            Assert.InRange(card.Bounds.Width, 300, 420.5);
            Assert.True(card.Bounds.Height < overlay.Bounds.Height * .84);
            Assert.Equal(1d, card.GetValue(HavenProperties.BorderWidth).Value, 3);
            Assert.Equal("Card", card.GetValue(HavenProperties.Shadow));
            Assert.Equal("Cancel", Assert.IsType<Haven.UI.Components.Button>(card.Children.Last()).Content);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [Fact]
    public void Manage_responses_shows_the_live_per_chat_response_state()
    {
        using var scene = new ChatHavenScene();
        scene.SetResponseState("Research Agent", ChatActionMode.AllowAllActions, GenerativeUiResponseMode.AlwaysVisual);
        scene.ShowAddMenu();

        var summary = scene.AddMenuPrefab.DescendantsAndSelf().OfType<Haven.UI.Components.Text>()
            .Single(text => text.Name == "CurrentResponseState");
        Assert.Equal("Research Agent · All actions · Always visual", summary.Content);
        Assert.Equal(
            "Current agent: Research Agent",
            scene.AddMenuPrefab.GetComponent<Haven.UI.Components.Button>("Agents").Accessibility.Description);
    }

    [AvaloniaFact]
    public void Message_three_dot_opens_detached_popup_without_changing_message_height()
    {
        using var scene = new ChatHavenScene();
        var userId = Guid.NewGuid();
        ChatMessageActionRequest? request = null;
        scene.MessageActionRequested += (_, value) => request = value;
        scene.SyncMessages([new ChatSceneMessage(userId, MessageRole.User, "Hello", string.Empty, false, string.Empty)]);
        var item = scene.Messages.GetItem(userId.ToString("N"));
        var menu = item.GetComponent<Haven.UI.Components.Button>("MessageMenu");
        Assert.DoesNotContain(item.DescendantsAndSelf(), element => element.Name == "Actions");
        var host = new HavenSceneControl { Root = scene.Root };
        var window = new Window { Width = 1000, Height = 760, Content = host };
        try
        {
            window.Show();
            window.UpdateLayout();
            var height = item.Bounds.Height;
            var router = new HavenInputRouter(scene.Root);
            Click(router, menu);
            window.UpdateLayout();
            Assert.Equal(height, item.Bounds.Height, 3);
            var popup = Assert.Single(scene.Root.Children.OfType<PopupMenu>());
            Assert.DoesNotContain(item.DescendantsAndSelf(), element => element is PopupMenu);
            var copy = popup.Card.DescendantsAndSelf().OfType<Haven.UI.Components.Button>().Single(button => button.Content == "Copy");
            Click(router, copy);
            window.UpdateLayout();
            Assert.NotNull(request);
            Assert.Equal(userId, request!.MessageId);
            Assert.Equal(ChatMessageAction.Copy, request.Action);
            Assert.Empty(scene.Root.Children.OfType<PopupMenu>());
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Resolve_problems_uses_detached_shared_popup_and_dismisses_after_selection()
    {
        using var scene = new ChatHavenScene();
        var selected = string.Empty;
        var host = new HavenSceneControl { Root = scene.Root };
        var window = new Window { Width = 1000, Height = 760, Content = host };
        try
        {
            window.Show();
            window.UpdateLayout();
            scene.ShowResolveProblemsMenu(
                "Resolve Problems",
                "Choose a recovery.",
                [("Hallucinating", () => selected = "hallucinating"), ("Looping", () => selected = "looping")]);
            window.UpdateLayout();

            var popup = Assert.Single(scene.Root.Children.OfType<PopupMenu>());
            Assert.DoesNotContain(scene.Root.Children, child => child.Name == "ChatModalOverlay");
            Assert.Equal("Resolve Problems", popup.Card.Accessibility.AccessibleName);
            var looping = popup.Card.DescendantsAndSelf().OfType<Haven.UI.Components.Button>().Single(button => button.Content == "Looping");
            var router = new HavenInputRouter(scene.Root);
            Click(router, looping);
            window.UpdateLayout();

            Assert.Equal("looping", selected);
            Assert.Empty(scene.Root.Children.OfType<PopupMenu>());
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Message_secondary_choices_use_detached_popup_anchored_to_the_message()
    {
        using var scene = new ChatHavenScene();
        var messageId = Guid.NewGuid();
        var selected = false;
        scene.SyncMessages([new ChatSceneMessage(messageId, MessageRole.User, "Hello", string.Empty, false, string.Empty)]);
        var host = new HavenSceneControl { Root = scene.Root };
        var window = new Window { Width = 1000, Height = 760, Content = host };
        try
        {
            window.Show();
            window.UpdateLayout();
            scene.ShowMessageChoiceMenu(messageId, "Branch message", [("Branch in new chat", () => selected = true)]);
            window.UpdateLayout();

            var popup = Assert.Single(scene.Root.Children.OfType<PopupMenu>());
            Assert.DoesNotContain(scene.Root.Children, child => child.Name == "ChatModalOverlay");
            Assert.Equal("Branch message", popup.Card.Accessibility.AccessibleName);
            var choice = popup.Card.DescendantsAndSelf().OfType<Haven.UI.Components.Button>().Single(button => button.Content == "Branch in new chat");
            var router = new HavenInputRouter(scene.Root);
            Click(router, choice);
            window.UpdateLayout();

            Assert.True(selected);
            Assert.Empty(scene.Root.Children.OfType<PopupMenu>());
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [Fact]
    public void Transcript_sync_handles_120_messages_without_losing_dynamic_item_identity()
    {
        using var scene = new ChatHavenScene();
        var messages = Enumerable.Range(0, 120)
            .Select(index => new ChatSceneMessage(
                Guid.NewGuid(),
                index % 2 == 0 ? MessageRole.User : MessageRole.Assistant,
                $"Message {index}",
                index % 2 == 0 ? string.Empty : "Haven",
                false,
                string.Empty))
            .ToArray();

        scene.SyncMessages(messages);

        Assert.Equal(120, scene.Messages.Items.Count);
        Assert.All(messages, message => Assert.NotNull(scene.Messages.GetItem(message.Id.ToString("N"))));

        var tracked = messages[61];
        var originalItem = scene.Messages.GetItem(tracked.Id.ToString("N"));
        scene.UpdateMessage(tracked with { Content = "Updated at scale" });

        Assert.Equal(120, scene.Messages.Items.Count);
        Assert.Same(originalItem, scene.Messages.GetItem(tracked.Id.ToString("N")));
        Assert.Equal("Updated at scale", originalItem.GetComponent<Markdown>("Body").Content);
    }

    [Fact]
    public void Safety_lock_disables_authoritative_composer_and_consequential_message_actions()
    {
        using var scene = new ChatHavenScene();
        scene.Instruction.Text = "unsafe continuation";
        scene.SetSending(false, modelAvailable: true);

        scene.SetSafetyLocked(true);

        Assert.False(scene.Instruction.GetValue(HavenProperties.Enabled));
        Assert.False(scene.SendButton.GetValue(HavenProperties.Enabled));
        Assert.False(scene.AddButton.GetValue(HavenProperties.Enabled));

        scene.SetSafetyLocked(false);
        scene.SetSending(false, modelAvailable: true);
        Assert.True(scene.Instruction.GetValue(HavenProperties.Enabled));
        Assert.True(scene.SendButton.GetValue(HavenProperties.Enabled));
        Assert.True(scene.AddButton.GetValue(HavenProperties.Enabled));
    }

    private static void Click(HavenInputRouter router, HavenElement element)
    {
        var point = new HavenPoint(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);
        router.PointerPressed(point);
        Assert.True(router.PointerReleased(point));
    }
}
