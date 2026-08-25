using Haven.Desktop.Overlay;
using System.Reflection;
using System.Runtime.InteropServices;
namespace Haven.Desktop.Tests;
public sealed class OverlayCaptureShellTests
{
    [Fact]
    public void Scene_exposes_visual_capture_control()
    {
        using var scene = new OverlayShellHavenScene();
        Assert.Equal("Overlay.Capture", scene.CaptureButton.Name);
        Assert.Equal("AI Select", scene.CaptureButton.Content);
    }

    [Fact]
    public async Task Global_hotkey_listener_dispatches_registered_thread_message()
    {
        if (!OperatingSystem.IsWindows()) return;

        const int testHotkeyId = 0x4842;
        const uint controlShiftNoRepeat = 0x0002 | 0x0004 | 0x4000;
        const uint virtualKeyF12 = 0x7B;
        await using var hotkey = new OverlayGlobalHotkey(testHotkeyId, controlShiftNoRepeat, virtualKeyF12);
        var pressed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hotkey.Pressed += (_, _) => pressed.TrySetResult();

        Assert.True(hotkey.Start(), hotkey.UnavailableReason);

        var threadIdField = typeof(OverlayGlobalHotkey).GetField("_threadId", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(threadIdField);
        var threadId = Assert.IsType<uint>(threadIdField.GetValue(hotkey));
        Assert.NotEqual(0u, threadId);

        Assert.True(Native.PostThreadMessage(threadId, 0x0312, (nuint)testHotkeyId, (nint)0x007B0006));
        await pressed.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    private static class Native
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostThreadMessage(uint idThread, uint msg, nuint wParam, nint lParam);
    }
}
