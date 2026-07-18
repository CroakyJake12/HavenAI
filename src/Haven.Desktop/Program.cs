using System.Runtime.InteropServices;
using System.Text;
using Avalonia;

namespace Haven.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                SetCurrentProcessExplicitAppUserModelID("Haven.LocalAI.Desktop");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            ReportBootstrapFailure(ex);
            Environment.ExitCode = 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void ReportBootstrapFailure(Exception exception)
    {
        string? logPath = null;
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var logDirectory = Path.Combine(appData, "Haven", "Logs");
            Directory.CreateDirectory(logDirectory);
            logPath = Path.Combine(logDirectory, "startup-bootstrap-failure.log");
            var entry = $"[{DateTimeOffset.Now:O}] Haven bootstrap failed.{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}";
            File.AppendAllText(logPath, entry, new UTF8Encoding(false));
        }
        catch
        {
            // Reporting must never hide the original startup failure.
        }

        try
        {
            Console.Error.WriteLine(exception);
        }
        catch
        {
        }

        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var detail = exception.GetBaseException().Message;
            var location = string.IsNullOrWhiteSpace(logPath)
                ? "The startup log could not be written."
                : $"Full details were written to:{Environment.NewLine}{logPath}";
            MessageBoxW(
                IntPtr.Zero,
                $"Haven could not start.{Environment.NewLine}{Environment.NewLine}{detail}{Environment.NewLine}{Environment.NewLine}{location}",
                "Haven startup failed",
                0x00000010);
        }
        catch
        {
        }
    }

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);
}
