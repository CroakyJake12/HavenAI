using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Haven.Desktop;

[assembly: AvaloniaTestApplication(typeof(Haven.Desktop.Tests.TestAppBuilder))]

namespace Haven.Desktop.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
