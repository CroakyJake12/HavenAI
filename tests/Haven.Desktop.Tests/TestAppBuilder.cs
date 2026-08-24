/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Desktop.Tests/TestAppBuilder.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns TestAppBuilder. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Skia;
using Haven.Desktop;

[assembly: AvaloniaTestApplication(typeof(Haven.Desktop.Tests.TestAppBuilder))]

namespace Haven.Desktop.Tests;

/// <summary>
/// Represents test app builder and keeps its related state and behavior together.
/// </summary>
public static class TestAppBuilder
{
    /// <summary>
    /// Builds avalonia app from the currently available inputs.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp()
    {
        var captureFrames = !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<App>();
        if (captureFrames)
            builder = builder.UseSkia();

        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions
        {
            // Pixel capture needs the Skia-backed renderer; normal behavioural
            // tests retain the faster headless drawing implementation.
            UseHeadlessDrawing = !captureFrames
        });
    }
}
