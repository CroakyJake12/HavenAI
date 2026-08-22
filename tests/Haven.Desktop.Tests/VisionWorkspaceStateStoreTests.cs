using Haven.Desktop.Views.Pages.Imagine;

namespace Haven.Desktop.Tests;

public sealed class VisionWorkspaceStateStoreTests
{
    [Fact]
    public async Task RoundTrip_PreservesVisionState()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "haven-vision-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "state.json");
            var store = new VisionWorkspaceStateStore(path);
            var expected = new VisionWorkspaceState("image.png", "question", "answer", "vision-model", "ABC");
            await store.SaveAsync(expected, cancellationToken);
            Assert.Equal(expected, await store.LoadAsync(cancellationToken));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task AnalysisKey_ChangesWithPromptOrImage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "haven-vision-key-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var first = Path.Combine(root, "first.bin");
            var second = Path.Combine(root, "second.bin");
            await File.WriteAllBytesAsync(first, [1, 2, 3], cancellationToken);
            await File.WriteAllBytesAsync(second, [1, 2, 4], cancellationToken);
            var a = await VisionWorkspaceStateStore.BuildAnalysisKeyAsync(first, "model", "prompt", cancellationToken);
            var same = await VisionWorkspaceStateStore.BuildAnalysisKeyAsync(first, "model", "prompt", cancellationToken);
            var promptChanged = await VisionWorkspaceStateStore.BuildAnalysisKeyAsync(first, "model", "different", cancellationToken);
            var imageChanged = await VisionWorkspaceStateStore.BuildAnalysisKeyAsync(second, "model", "prompt", cancellationToken);
            Assert.Equal(a, same);
            Assert.NotEqual(a, promptChanged);
            Assert.NotEqual(a, imageChanged);
        }
        finally { Directory.Delete(root, true); }
    }
}
