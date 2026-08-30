using Haven.Core;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Tests;

public sealed class WorkspaceTabViewModelLifecycleTests
{
    [Fact]
    public void Navigating_after_back_disposes_only_abandoned_forward_pages()
    {
        var pageA = new DisposablePage();
        var pageB = new DisposablePage();
        var pageC = new DisposablePage();
        var pageD = new DisposablePage();
        using var tab = new WorkspaceTabViewModel("a", "A", pageA, true, HavenSurface.Home);

        tab.NavigateTo("b", "B", pageB, true, HavenSurface.Home);
        tab.NavigateTo("c", "C", pageC, true, HavenSurface.Home);
        Assert.True(tab.TryGoBack());
        Assert.Same(pageB, tab.Page);

        tab.NavigateTo("d", "D", pageD, true, HavenSurface.Home);

        Assert.Equal(1, pageC.DisposeCount);
        Assert.Equal(0, pageA.DisposeCount);
        Assert.Equal(0, pageB.DisposeCount);
        Assert.Equal(0, pageD.DisposeCount);
    }

    private sealed class DisposablePage : IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }
}
