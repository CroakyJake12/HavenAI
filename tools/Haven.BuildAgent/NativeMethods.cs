using System.Runtime.InteropServices;

namespace Haven.BuildAgent;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

internal static partial class NativeMethods
{
    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int GetWindowRect(nint windowHandle, out NativeRect rectangle);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int PrintWindow(nint windowHandle, nint deviceContext, uint flags);
}
