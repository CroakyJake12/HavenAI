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
var database = host.Services.GetRequiredService<IAppDatabase>();
await database.InitializeAsync(CancellationToken.None);
var migrator = host.Services.GetRequiredService<ILegacyStateMigrator>();
await migrator.MigrateIfNeededAsync(CancellationToken.None);
var runner = host.Services.GetRequiredService<AutomationRunner>();
var result = await runner.RunDueAsync(DateTimeOffset.UtcNow, CancellationToken.None);
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Haven.AutomationWorker");
logger.LogInformation("Automation pass complete. Due {Due}, started {Started}, succeeded {Succeeded}, failed {Failed}, skipped {Skipped}.", result.Due, result.Started, result.Succeeded, result.Failed, result.Skipped);
Environment.ExitCode = result.Failed == 0 ? 0 : 2;
