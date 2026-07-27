/*
 * FILE DOCUMENTATION
 * Where: src/Haven.AutomationWorker/Program.cs, in the automation worker executable, which hosts background automation processing.
 * What: This file owns top-level declarations and support code. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Automations;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddHavenInfrastructure();
builder.Services.AddHavenAutomations();

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Haven.AutomationWorker");
var diagnostics = host.Services.GetRequiredService<IProductionDiagnostics>();

try
{
    var recovery = await host.Services.GetRequiredService<IRecoverySafetyProbe>()
        .AssessAsync(CancellationToken.None);
    if (recovery.IsSafeMode)
    {
        await diagnostics.WriteAsync(
            ReliabilitySeverity.Warning,
            "automation-worker",
            "recovery-blocked",
            "The scheduled automation pass was skipped because Haven is in crash-loop recovery safe mode.",
            new Dictionary<string, string>
            {
                ["stateReadable"] = recovery.StateWasReadable.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["recentUncleanStarts"] = recovery.RecentUncleanStarts.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["reason"] = recovery.Reason
            },
            cancellationToken: CancellationToken.None);
        logger.LogWarning(
            "Automation pass skipped by Haven recovery safe mode. State readable {StateReadable}. Recent unclean starts {RecentUncleanStarts}. Reason: {Reason}",
            recovery.StateWasReadable,
            recovery.RecentUncleanStarts,
            recovery.Reason);
        // A safety skip is intentional, not a failed scheduled task. Returning success
        // prevents Task Scheduler from creating a noisy failure/retry loop.
        Environment.ExitCode = 0;
        return;
    }

    var database = host.Services.GetRequiredService<IAppDatabase>();
    await database.InitializeAsync(CancellationToken.None);
    var migrator = host.Services.GetRequiredService<ILegacyStateMigrator>();
    await migrator.MigrateIfNeededAsync(CancellationToken.None);
    var runner = host.Services.GetRequiredService<AutomationRunner>();
    var result = await runner.RunDueAsync(DateTimeOffset.UtcNow, CancellationToken.None);
    logger.LogInformation(
        "Automation pass complete. Due {Due}, started {Started}, succeeded {Succeeded}, failed {Failed}, skipped {Skipped}.",
        result.Due,
        result.Started,
        result.Succeeded,
        result.Failed,
        result.Skipped);
    await diagnostics.WriteAsync(
        result.Failed == 0 ? ReliabilitySeverity.Information : ReliabilitySeverity.Error,
        "automation-worker",
        "pass-complete",
        "The scheduled automation pass completed.",
        new Dictionary<string, string>
        {
            ["due"] = result.Due.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["started"] = result.Started.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["succeeded"] = result.Succeeded.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["failed"] = result.Failed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["skipped"] = result.Skipped.ToString(System.Globalization.CultureInfo.InvariantCulture)
        },
        cancellationToken: CancellationToken.None);
    Environment.ExitCode = result.Failed == 0 ? 0 : 2;
}
catch (Exception ex)
{
    try
    {
        await diagnostics.WriteAsync(
            ReliabilitySeverity.Critical,
            "automation-worker",
            "pass-failed",
            ex.ToString(),
            new Dictionary<string, string>
            {
                ["exceptionType"] = ex.GetType().FullName ?? ex.GetType().Name,
                ["hResult"] = ex.HResult.ToString(System.Globalization.CultureInfo.InvariantCulture)
            },
            cancellationToken: CancellationToken.None);
    }
    catch { }
    logger.LogError(ex, "Haven Automation Worker failed before completing its scheduled pass.");
    Environment.ExitCode = 2;
}
