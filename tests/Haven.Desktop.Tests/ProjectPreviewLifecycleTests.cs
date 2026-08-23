using Haven.Application;
using Haven.Desktop.Views.Pages.ProjectPreview;

namespace Haven.Desktop.Tests;

public sealed class ProjectPreviewLifecycleTests
{
    [Fact]
    public async Task Hidden_preview_cancels_an_inflight_start()
    {
        var provider = new SlowPreviewProvider();
        using var page = new ProjectPreviewPage(provider, "test-root", TimeSpan.FromMilliseconds(20));

        var testToken = TestContext.Current.CancellationToken;
        var starting = page.StartProviderSessionAsync();
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2), testToken);
        await page.DisposeWhenHiddenAsync(_ => Task.FromResult(false), testToken);
        Assert.Null(await starting.WaitAsync(TimeSpan.FromSeconds(2), testToken));

        Assert.Equal(1, provider.CancelledStarts);
    }

    private sealed class SlowPreviewProvider : IProjectPreviewProvider
    {
        private int _cancelledStarts;

        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CancelledStarts => Volatile.Read(ref _cancelledStarts);
        public string Id => "test.preview";

        public bool CanPreview(string projectRoot) => true;

        public ProjectPreviewDescriptor Describe(string projectRoot) =>
            new(Id, ProjectPreviewKind.Website, "Test preview", projectRoot, "Test preview");

        public async Task<IProjectPreviewSession> StartAsync(string projectRoot, CancellationToken cancellationToken)
        {
            Started.TrySetResult(true);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("Unreachable.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Interlocked.Increment(ref _cancelledStarts);
                throw;
            }
        }
    }
}
