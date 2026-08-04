using AndroidApplication = global::Android.App.Application;
using AndroidActivity = global::Android.App.Activity;
using AndroidAlertDialog = global::Android.App.AlertDialog;
using AndroidContext = global::Android.Content.Context;
using AndroidIntent = global::Android.Content.Intent;
using AndroidClipData = global::Android.Content.ClipData;
using AndroidClipboardManager = global::Android.Content.ClipboardManager;
using AndroidBuild = global::Android.OS.Build;
using AndroidEnvironment = global::Android.Runtime.AndroidEnvironment;
using AndroidLog = global::Android.Util.Log;
using AndroidToast = global::Android.Widget.Toast;
using AndroidToastLength = global::Android.Widget.ToastLength;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Haven.Android;

internal static class AndroidRuntimeDiagnostics
{
    private const string LogTag = "Haven";
    private const string ReportFileName = "haven-runtime-errors.log";
    private const int MaxReportCharacters = 131_072;
    private const int MaxDetailCharacters = 20_000;

    private static readonly object Sync = new();
    private static string? _reportPath;
    private static WeakReference<AndroidActivity>? _activity;
    private static int _initialized;
    private static int _reportPresented;

    public static void Initialize(AndroidApplication application)
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
            return;

        try
        {
            var directory = application.FilesDir?.AbsolutePath;
            if (string.IsNullOrWhiteSpace(directory))
                directory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            Directory.CreateDirectory(directory);
            _reportPath = Path.Combine(directory, ReportFileName);
        }
        catch (Exception exception)
        {
            AndroidLog.Error(LogTag, "Could not initialise Haven's private runtime-error file: " + exception.Message);
        }

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var exception = args.ExceptionObject as Exception
                ?? new InvalidOperationException("The runtime raised a non-Exception fatal error.");
            Record(exception, "Unhandled managed exception", showDialog: false);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
            Record(args.Exception, "Unobserved task exception", showDialog: true);

        AndroidEnvironment.UnhandledExceptionRaiser += (_, args) =>
            Record(args.Exception, "Unhandled Android runtime exception", showDialog: false);
    }

    public static void Attach(AndroidActivity activity)
    {
        _activity = new WeakReference<AndroidActivity>(activity);
        ShowPendingReport(activity);
    }

    public static void Detach(AndroidActivity activity)
    {
        if (_activity?.TryGetTarget(out var current) == true && ReferenceEquals(current, activity))
            _activity = null;
    }

    public static void Record(Exception exception, string context, bool showDialog)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var report = BuildReport(exception, context);
        WriteReport(report);
        AndroidLog.Error(LogTag, report);

        if (!showDialog || !TryGetActivity(out var activity))
            return;

        Interlocked.Exchange(ref _reportPresented, 0);
        ShowPendingReport(activity);
    }

    public static void ShowStartupToast(AndroidContext context)
    {
        try
        {
            AndroidToast.MakeText(
                context,
                "Haven could not start. A technical error report was saved.",
                AndroidToastLength.Long)?.Show();
        }
        catch (Exception exception)
        {
            AndroidLog.Error(LogTag, "Could not show Haven's startup error message: " + exception.Message);
        }
    }

    private static void ShowPendingReport(AndroidActivity activity)
    {
        if (!TryReadReport(out var report)
            || Interlocked.CompareExchange(ref _reportPresented, 1, 0) != 0)
        {
            return;
        }

        activity.RunOnUiThread(() =>
        {
            try
            {
                if (activity.IsFinishing || activity.IsDestroyed)
                {
                    Interlocked.Exchange(ref _reportPresented, 0);
                    return;
                }

                new AndroidAlertDialog.Builder(activity)
                    .SetTitle("Haven encountered an error")
                    .SetMessage(BuildDialogMessage(report))
                    .SetPositiveButton("Copy details", (_, _) => CopyReport(activity, report))
                    .SetNeutralButton("Share report", (_, _) => ShareReport(activity, report))
                    .SetNegativeButton("Clear report", (_, _) => ClearReport())
                    .Show();
            }
            catch (Exception exception)
            {
                Interlocked.Exchange(ref _reportPresented, 0);
                AndroidLog.Error(LogTag, "Could not display Haven's runtime-error dialog: " + exception.Message);
            }
        });
    }

    private static bool TryGetActivity(out AndroidActivity activity)
    {
        if (_activity?.TryGetTarget(out var current) == true
            && !current.IsFinishing
            && !current.IsDestroyed)
        {
            activity = current;
            return true;
        }

        activity = null!;
        return false;
    }

    private static string BuildReport(Exception exception, string context)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Haven Android runtime report");
        builder.AppendLine("UTC: " + DateTimeOffset.UtcNow.ToString("O"));
        builder.AppendLine("Context: " + Sanitize(context));
        builder.AppendLine("Haven version: " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown"));
        builder.AppendLine("Android: " + (AndroidBuild.VERSION.Release ?? "unknown")
            + " (API " + (int)AndroidBuild.VERSION.SdkInt + ")");
        builder.AppendLine("Device: " + Sanitize(AndroidBuild.Manufacturer) + " " + Sanitize(AndroidBuild.Model));

        var current = exception;
        for (var depth = 0; current is not null && depth < 6; depth++)
        {
            builder.AppendLine();
            builder.AppendLine(depth == 0 ? "Exception:" : "Inner exception " + depth + ":");
            builder.AppendLine("Type: " + current.GetType().FullName);
            builder.AppendLine("Message: " + Sanitize(current.Message));

            if (!string.IsNullOrWhiteSpace(current.StackTrace))
            {
                builder.AppendLine("Stack trace:");
                builder.AppendLine(Sanitize(current.StackTrace));
            }

            current = current.InnerException;
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildDialogMessage(string report)
    {
        var context = FindLastLine(report, "Context:") ?? "A runtime error was recorded.";
        var message = FindLastLine(report, "Message:") ?? "No additional message was available.";

        return "Haven recorded a technical runtime error.\n\n"
            + context + "\n"
            + message + "\n\n"
            + "Copy or share the report to provide diagnostic details. "
            + "Review it before sharing, then clear it after the problem has been recorded.";
    }

    private static string? FindLastLine(string report, string prefix)
    {
        return report
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "(not provided)";

        var sanitized = Regex.Replace(
            value,
            @"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]+",
            "Bearer [redacted]");
        sanitized = Regex.Replace(
            sanitized,
            @"(?i)\b(api[_-]?key|access[_-]?token|refresh[_-]?token|token|password|secret|authorization)\b\s*[:=]\s*[^\s,;]+",
            "$1=[redacted]");
        sanitized = Regex.Replace(
            sanitized,
            @"/data/(?:user/\d+|data)/com\.cakemods\.haven",
            "<app-data>");
        sanitized = Regex.Replace(
            sanitized,
            @"(?im)(?:[A-Z]:\\|/home/|/Users/)[^\r\n]*",
            "<source-path>");

        return sanitized.Length <= MaxDetailCharacters
            ? sanitized
            : sanitized[..MaxDetailCharacters] + "\n[details truncated]";
    }

    private static void WriteReport(string report)
    {
        lock (Sync)
        {
            if (string.IsNullOrWhiteSpace(_reportPath))
                return;

            try
            {
                var existing = File.Exists(_reportPath)
                    ? File.ReadAllText(_reportPath)
                    : string.Empty;
                var combined = string.IsNullOrEmpty(existing)
                    ? report
                    : existing
                        + Environment.NewLine
                        + Environment.NewLine
                        + "----------------------------------------"
                        + Environment.NewLine
                        + report;

                if (combined.Length > MaxReportCharacters)
                    combined = combined[^MaxReportCharacters..];

                File.WriteAllText(_reportPath, combined);
            }
            catch (Exception exception)
            {
                AndroidLog.Error(LogTag, "Could not write Haven's runtime-error report: " + exception.Message);
            }
        }
    }

    private static bool TryReadReport(out string report)
    {
        lock (Sync)
        {
            report = string.Empty;
            if (string.IsNullOrWhiteSpace(_reportPath) || !File.Exists(_reportPath))
                return false;

            try
            {
                report = File.ReadAllText(_reportPath);
                return !string.IsNullOrWhiteSpace(report);
            }
            catch (Exception exception)
            {
                AndroidLog.Error(LogTag, "Could not read Haven's runtime-error report: " + exception.Message);
                return false;
            }
        }
    }

    private static void ClearReport()
    {
        lock (Sync)
        {
            if (string.IsNullOrWhiteSpace(_reportPath))
                return;

            try
            {
                File.Delete(_reportPath);
            }
            catch (Exception exception)
            {
                AndroidLog.Error(LogTag, "Could not clear Haven's runtime-error report: " + exception.Message);
            }
        }
    }

    private static void CopyReport(AndroidActivity activity, string report)
    {
        try
        {
            if (activity.GetSystemService(AndroidContext.ClipboardService) is AndroidClipboardManager clipboard)
            {
                clipboard.PrimaryClip = AndroidClipData.NewPlainText("Haven runtime report", report);
                AndroidToast.MakeText(
                    activity,
                    "Haven error details copied.",
                    AndroidToastLength.Short)?.Show();
            }
        }
        catch (Exception exception)
        {
            AndroidLog.Error(LogTag, "Could not copy Haven's runtime-error report: " + exception.Message);
        }
    }

    private static void ShareReport(AndroidActivity activity, string report)
    {
        try
        {
            var intent = new AndroidIntent(AndroidIntent.ActionSend);
            intent.SetType("text/plain");
            intent.PutExtra(AndroidIntent.ExtraSubject, "Haven Android runtime report");
            intent.PutExtra(AndroidIntent.ExtraText, report);
            activity.StartActivity(AndroidIntent.CreateChooser(intent, "Share Haven error report"));
        }
        catch (Exception exception)
        {
            AndroidLog.Error(LogTag, "Could not share Haven's runtime-error report: " + exception.Message);
        }
    }
}
