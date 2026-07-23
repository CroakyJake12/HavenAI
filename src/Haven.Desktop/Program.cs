/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Program.cs, in the Desktop composition layer, which starts and wires the Avalonia application.
 * What: This file owns Program. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Avalonia;

namespace Haven.Desktop;

/// <summary>
/// Represents program and keeps its related state and behavior together.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Maximum thread pool threads to configure (up to 96 cores).
    /// </summary>
    private const int MaxThreadPoolThreads = 96;

    /// <summary>
    /// Performs the main step owned by this component.
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            ConfigureThreadPool();
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

    /// <summary>
    /// Configures the .NET thread pool to utilize up to 96 cores for parallel processing.
    /// </summary>
    private static void ConfigureThreadPool()
    {
        var coreCount = Environment.ProcessorCount;
        var threadCount = Math.Min(coreCount, MaxThreadPoolThreads);

        ThreadPool.SetMinThreads(threadCount, threadCount);
        ThreadPool.SetMaxThreads(threadCount, threadCount);

        Console.WriteLine($"[Haven] Thread pool configured: {threadCount} threads ( cores: {coreCount})");
    }

    /// <summary>
    /// Builds avalonia app from the currently available inputs.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    /// <summary>
    /// Performs the report bootstrap failure step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the set current process explicit app user model id step owned by this component.
    /// </summary>
    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    /// <summary>
    /// Performs the message box w step owned by this component.
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);
}
