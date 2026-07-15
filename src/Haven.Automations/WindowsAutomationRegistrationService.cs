using System.Diagnostics;

namespace Haven.Automations;

public sealed record AutomationRegistrationResult(bool Succeeded, string Message);

public sealed class WindowsAutomationRegistrationService
{
    public const string TaskName = "Haven Background Automations";

    public async Task<AutomationRegistrationResult> RegisterAsync(string workerExecutablePath, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return new(false, "Windows Task Scheduler is only available on Windows.");
        if (!File.Exists(workerExecutablePath)) return new(false, "The Haven automation worker executable was not found.");

        var quotedWorker = $"\\\"{workerExecutablePath}\\\"";
        var arguments = $"/Create /F /SC MINUTE /MO 5 /TN \"{TaskName}\" /TR \"{quotedWorker}\"";
        var result = await RunAsync(arguments, cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0
            ? new(true, "Background automations are registered to check every five minutes.")
            : new(false, string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError);
    }

    public async Task<AutomationRegistrationResult> UnregisterAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return new(false, "Windows Task Scheduler is only available on Windows.");
        var result = await RunAsync($"/Delete /F /TN \"{TaskName}\"", cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0
            ? new(true, "Background automation task removed.")
            : new(false, string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError);
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunAsync(string arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return (process.ExitCode, await outputTask.ConfigureAwait(false), await errorTask.ConfigureAwait(false));
    }
}
