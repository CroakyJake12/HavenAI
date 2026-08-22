using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Shell.NativePresentation;
using Haven.UI;

namespace Haven.Desktop.Tests;

public sealed class NativeChatSidebarNewChatTests
{
    [AvaloniaFact]
    public async Task Rapid_new_chat_activation_starts_only_one_conversation_until_creation_finishes()
    {
        var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var startCount = 0;

        using var sidebar = new NativeChatSidebar(
            new EmptyConversationRepository(),
            new EmptyContainerRepository(),
            _ => Task.CompletedTask,
            async (_, _) =>
            {
                var count = Interlocked.Increment(ref startCount);
                if (count == 1)
                {
                    firstStarted.TrySetResult(true);
                    await releaseFirst.Task;
                }
            },
            _ => Task.CompletedTask);

        var window = new Window { Width = 420, Height = 700, Content = sidebar };
        try
        {
            window.Show();
            window.UpdateLayout();
            var router = new HavenInputRouter(sidebar.Scene.Root);
            var button = sidebar.Scene.NewChat;
            var point = new HavenPoint(button.Bounds.X + button.Bounds.Width / 2, button.Bounds.Y + button.Bounds.Height / 2);

            router.PointerPressed(point);
            Assert.True(router.PointerReleased(point));
            await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(button.GetValue(HavenProperties.Enabled));

            router.PointerPressed(point);
            router.PointerReleased(point);
            await Task.Delay(50);
            Assert.Equal(1, Volatile.Read(ref startCount));

            releaseFirst.TrySetResult(true);
            await WaitUntilAsync(() => button.GetValue(HavenProperties.Enabled));

            router.PointerPressed(point);
            Assert.True(router.PointerReleased(point));
            await WaitUntilAsync(() => Volatile.Read(ref startCount) == 2);
            Assert.Equal(2, Volatile.Read(ref startCount));
        }
        finally
        {
            releaseFirst.TrySetResult(true);
            window.Content = null;
            window.Close();
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition()) return;
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private sealed class EmptyConversationRepository : IConversationRepository
    {
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Conversation>>([]);
        public Task<Conversation?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Conversation?>(null);
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ChatMessage>>([]);
        public Task UpsertConversationAsync(Conversation conversation, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteConversationAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class EmptyContainerRepository : IContainerRepository
    {
        public Task<IReadOnlyList<ContainerDefinition>> GetByModeAsync(HavenMode mode, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ContainerDefinition>>([]);
        public Task<IReadOnlyList<Lesson>> GetLessonsAsync(Guid subjectId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Lesson>>([]);
        public Task UpsertAsync(ContainerDefinition item, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Lesson> CreateSubjectAsync(ContainerDefinition item, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteAndDetachConversationsAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpsertLessonAsync(Lesson item, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteLessonAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
