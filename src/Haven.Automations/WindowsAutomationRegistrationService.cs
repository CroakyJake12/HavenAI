/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Automations/WindowsAutomationRegistrationService.cs, in the Automations layer, which parses schedules and runs durable background actions.
 * What: This file owns AutomationRegistrationResult, WindowsAutomationRegistrationService. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Diagnostics;

namespace Haven.Automations;

/// <summary>
/// Represents automation registration result and keeps its related state and behavior together.
/// </summary>
public sealed record AutomationRegistrationResult(bool Succeeded, string Message);

/// <summary>
/// Represents windows automation registration service and keeps its related state and behavior together.
/// </summary>
public sealed class WindowsAutomationRegistrationService
{
    /// <summary>
    /// Stores task name locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public const string TaskName = "Haven Background Automations";

    /// <summary>
    /// Performs register async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs unregister async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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
