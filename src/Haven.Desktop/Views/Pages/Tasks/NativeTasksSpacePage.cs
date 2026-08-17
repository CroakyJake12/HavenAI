using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views.Pages.Tasks;

/// <summary>
/// Platform host for the Haven-native Tasks Space. Visible product UI is entirely Haven.UI; existing task-chat runtime executes delegated work.
/// </summary>
public sealed class NativeTasksSpacePage : UserControl, IActivatablePage, IDisposable
{
    private readonly IConversationRepository _conversations;
    private readonly Func<Task> _startBlankTask;
    private readonly Func<string, Task> _invokeTask;
    private readonly Func<Conversation, Task> _openConversation;
    private readonly TasksSpaceHavenScene _scene;
    private CancellationTokenSource? _refreshCancellation;
    private IReadOnlyList<Conversation> _recent = [];
    private bool _disposed;

    public NativeTasksSpacePage(
        IConversationRepository conversations,
        Func<Task> startBlankTask,
        Func<string, Task> invokeTask,
        Func<Conversation, Task> openConversation)
    {
        _conversations = conversations ?? throw new ArgumentNullException(nameof(conversations));
        _startBlankTask = startBlankTask ?? throw new ArgumentNullException(nameof(startBlankTask));
        _invokeTask = invokeTask ?? throw new ArgumentNullException(nameof(invokeTask));
        _openConversation = openConversation ?? throw new ArgumentNullException(nameof(openConversation));
        _scene = new TasksSpaceHavenScene();
        Scene = new HavenSceneControl { Root = _scene.Root };
        AutomationProperties.SetAutomationId(this, "HavenNativeTasksSpacePage");
        AutomationProperties.SetName(this, "Haven Tasks Space");
        AutomationProperties.SetAutomationId(Scene, "HavenNativeTasksSpaceScene");
        AutomationProperties.SetName(Scene, "One-off delegated tasks");
        Content = Scene;
        _scene.DelegateRequested += OnDelegateRequested;
        _scene.NewBlankTaskRequested += OnNewBlankTaskRequested;
        _scene.RecentTaskRequested += OnRecentTaskRequested;
    }

    public HavenSceneControl Scene { get; }

    public Task ActivateAsync(CancellationToken cancellationToken) => RefreshAsync(cancellationToken);

    public void Deactivate() => Interlocked.Exchange(ref _refreshCancellation, null)?.Cancel();

    internal Task RefreshNowAsync(CancellationToken cancellationToken = default) => RefreshAsync(cancellationToken);

    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;
        var refresh = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var previous = Interlocked.Exchange(ref _refreshCancellation, refresh);
        previous?.Cancel();
        var token = refresh.Token;
        try
        {
            var recent = await _conversations.GetRecentAsync(HavenMode.Tasks, 40, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            _recent = recent
                .Where(item => !item.IsArchived && item.Kind != ConversationKind.Call)
                .OrderByDescending(item => item.UpdatedAt)
                .ToArray();
            var rows = _recent
                .Select(item => new TasksSpaceRecentItem(item.Id, item.Title, $"Updated {item.UpdatedAt.LocalDateTime:g}"))
                .ToArray();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _scene.SetRecent(rows);
                _scene.SetStatus(null);
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => _scene.SetStatus($"Tasks could not refresh: {exception.Message}"));
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _refreshCancellation, null, refresh), refresh)) refresh.Dispose();
            else refresh.Dispose();
        }
    }

    private async void OnDelegateRequested(object? sender, string instruction)
    {
        _scene.SetBusy(true);
        try
        {
            await _invokeTask(instruction);
            _scene.Instruction.Text = string.Empty;
            _scene.SetStatus(null);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            _scene.SetStatus($"Could not start task: {exception.Message}");
        }
        finally
        {
            _scene.SetBusy(false);
        }
    }

    private async void OnNewBlankTaskRequested(object? sender, EventArgs e)
    {
        _scene.SetBusy(true);
        try { await _startBlankTask(); }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            _scene.SetStatus($"Could not open task: {exception.Message}");
        }
        finally { _scene.SetBusy(false); }
    }

    private async void OnRecentTaskRequested(object? sender, Guid conversationId)
    {
        var conversation = _recent.FirstOrDefault(item => item.Id == conversationId);
        if (conversation is null) return;
        try { await _openConversation(conversation); }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            _scene.SetStatus($"Could not open task: {exception.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Interlocked.Exchange(ref _refreshCancellation, null)?.Cancel();
        _scene.DelegateRequested -= OnDelegateRequested;
        _scene.NewBlankTaskRequested -= OnNewBlankTaskRequested;
        _scene.RecentTaskRequested -= OnRecentTaskRequested;
        _scene.Dispose();
    }
}
