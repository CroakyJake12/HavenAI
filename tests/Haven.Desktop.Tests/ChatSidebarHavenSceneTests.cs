using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Shell.NativePresentation;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;

namespace Haven.Desktop.Tests;

public sealed class ChatSidebarHavenSceneTests
{
    [AvaloniaFact]
    public void Sidebar_three_dot_opens_detached_popup_without_changing_row_height()
    {
        using var scene = new ChatSidebarHavenScene();
        var chatId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        ChatSidebarConversationRequest? request = null;
        scene.ConversationActionRequested += (_, value) => request = value;
        scene.SetRows(
            [new ChatSidebarEntry(ChatSidebarEntryKind.Conversation, chatId, "Pinned chat", false, false, true)],
            [],
            [new ChatSidebarEntry(ChatSidebarEntryKind.Group, groupId, "Project", true, true, false, true)],
            []);

        var chat = Assert.Single(scene.PinnedRows.Items);
        var group = Assert.Single(scene.GroupRows.Items);
        var more = chat.GetComponent<HavenButton>("More");
        Assert.Equal("more", more.IconKey);
        Assert.Equal(string.Empty, more.Content);
        Assert.Equal("Manage Pinned chat", more.Accessibility.AccessibleName);
        Assert.Equal("chevron-down", group.GetComponent<HavenButton>("Toggle").IconKey);
        Assert.DoesNotContain(chat.DescendantsAndSelf(), element => element.Name == "Actions");

        var host = new HavenSceneControl { Root = scene.Root };
        var window = new Window { Width = 420, Height = 700, Content = host };
        try
        {
            window.Show();
            window.UpdateLayout();
            var rowHeight = chat.Bounds.Height;
            var router = new HavenInputRouter(scene.Root);
            Click(router, more);
            window.UpdateLayout();

            Assert.Equal(rowHeight, chat.Bounds.Height, 3);
            var popup = Assert.Single(scene.Root.Children.OfType<PopupMenu>());
            Assert.Same(scene.Root, popup.Parent);
            Assert.DoesNotContain(chat.DescendantsAndSelf(), element => element is PopupMenu);
            var rename = popup.Card.DescendantsAndSelf().OfType<HavenButton>().Single(button => button.Content == "Rename");
            Click(router, rename);
            window.UpdateLayout();

            Assert.NotNull(request);
            Assert.Equal(chatId, request!.ConversationId);
            Assert.Equal(ChatSidebarConversationAction.Rename, request.Action);
            Assert.Empty(scene.Root.Children.OfType<PopupMenu>());
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [Fact]
    public void Sidebar_existing_rows_can_refresh_after_inline_action_variables_are_removed()
    {
        using var scene = new ChatSidebarHavenScene();
        var chatId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        scene.SetRows(
            [],
            [],
            [new ChatSidebarEntry(ChatSidebarEntryKind.Group, groupId, "Project", true, true, false, true)],
            [new ChatSidebarEntry(ChatSidebarEntryKind.Conversation, chatId, "Travel", false, false, false)]);

        var group = Assert.Single(scene.GroupRows.Items);
        var chat = Assert.Single(scene.ChatRows.Items);

        scene.SetRows(
            [],
            [],
            [new ChatSidebarEntry(ChatSidebarEntryKind.Group, groupId, "Project renamed", false, false, true, false)],
            [new ChatSidebarEntry(ChatSidebarEntryKind.Conversation, chatId, "Travel renamed", Active: true, Unread: true, Pinned: true)]);

        Assert.Same(group, Assert.Single(scene.GroupRows.Items));
        Assert.Same(chat, Assert.Single(scene.ChatRows.Items));
        Assert.Equal("Project renamed", group.GetComponent<HavenButton>("Open").Content);
        Assert.Equal("Travel renamed •", chat.GetComponent<HavenButton>("Open").Content);
        Assert.Equal("chevron-right", group.GetComponent<HavenButton>("Toggle").IconKey);
    }

    [AvaloniaFact]
    public void Sidebar_search_uses_haven_input_text_changes_and_row_actions_emit_real_ids()
    {
        using var scene = new ChatSidebarHavenScene();
        var chatId = Guid.NewGuid();
        string? query = null;
        ChatSidebarConversationRequest? request = null;
        scene.SearchChanged += (_, value) => query = value;
        scene.ConversationActionRequested += (_, value) => request = value;
        scene.SetRows([], [], [], [new ChatSidebarEntry(ChatSidebarEntryKind.Conversation, chatId, "Travel", false, false, false)]);

        scene.Search.Text = "travel";
        Assert.Equal("travel", query);

        Click(scene, scene.ChatRows.Items[0].GetComponent<HavenButton>("Open"));
        Assert.NotNull(request);
        Assert.Equal(chatId, request!.ConversationId);
        Assert.Equal(ChatSidebarConversationAction.Open, request.Action);
    }

    [Fact]
    public void Sidebar_sync_handles_150_chats_without_losing_dynamic_row_identity()
    {
        using var scene = new ChatSidebarHavenScene();
        var chats = Enumerable.Range(0, 150)
            .Select(index => new ChatSidebarEntry(
                ChatSidebarEntryKind.Conversation,
                Guid.NewGuid(),
                $"Chat {index:000}",
                false,
                false,
                false))
            .ToArray();

        scene.SetRows([], [], [], chats);

        Assert.Equal(150, scene.ChatRows.Items.Count);
        var trackedId = chats[87].Id;
        var trackedItem = scene.ChatRows.Items[87];
        var refreshed = chats
            .Select(entry => entry.Id == trackedId ? entry with { Title = "Updated at scale" } : entry)
            .ToArray();

        scene.SetRows([], [], [], refreshed);

        Assert.Equal(150, scene.ChatRows.Items.Count);
        var refreshedItem = scene.ChatRows.Items[87];
        Assert.Same(trackedItem, refreshedItem);
        Assert.Equal("Updated at scale", refreshedItem.GetComponent<HavenButton>("Open").Content);
    }

    private static void Click(HavenInputRouter router, HavenElement element)
    {
        var point = new HavenPoint(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);
        router.PointerPressed(point);
        Assert.True(router.PointerReleased(point));
    }

    private static void Click(ChatSidebarHavenScene scene, HavenElement element)
    {
        var host = new HavenSceneControl { Root = scene.Root };
        var window = new Window { Width = 420, Height = 700, Content = host };
        try
        {
            window.Show();
            window.UpdateLayout();
            var router = new HavenInputRouter(scene.Root);
            var point = new HavenPoint(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);
            router.PointerPressed(point);
            Assert.True(router.PointerReleased(point));
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [Fact]
    public void Sidebar_scene_contains_only_haven_elements_for_visible_composition()
    {
        using var scene = new ChatSidebarHavenScene();

        Assert.IsType<Haven.UI.Components.Page>(scene.Root);
        Assert.All(scene.Root.DescendantsAndSelf(), element => Assert.IsAssignableFrom<HavenElement>(element));
        Assert.Contains(scene.Root.DescendantsAndSelf(), element => element is Input);
        Assert.Contains(scene.Root.DescendantsAndSelf(), element => element is DynamicUIRuntime);
    }
}
