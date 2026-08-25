using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>
/// Shared LIVE STEER + QUEUE runtime: follow-up classification through the
/// coordinator, backed by the migration-24 durable execution store.
/// </summary>
public sealed class TaskExecutionCoordinatorTests
{
    [Theory]
    [InlineData("after that, book the restaurant", TaskFollowUpMode.Queue)]
    [InlineData("then summarise everything", TaskFollowUpMode.Queue)]
    [InlineData("once you're done, export the report", TaskFollowUpMode.Queue)]
    [InlineData("actually use vegan options instead", TaskFollowUpMode.Steer)]
    [InlineData("instead search Manchester", TaskFollowUpMode.Steer)]
    [InlineData("don't include archived tabs", TaskFollowUpMode.Steer)]
    public void Follow_up_instructions_are_classified_into_steer_and_queue(string instruction, TaskFollowUpMode expected)
    {
        var dataDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "haven-steer-tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dataDirectory);
        try
        {
            var coordinator = CreateCoordinator(dataDirectory);

            Assert.Equal(expected, coordinator.InferMode(instruction));
        }
        finally
        {
            try { System.IO.Directory.Delete(dataDirectory, true); } catch (System.IO.IOException) { }
        }
    }

    private static TaskExecutionCoordinator CreateCoordinator(string dataDirectory)
    {
        var database = new SqliteDatabase(new FixedPaths(dataDirectory));
        database.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        return new TaskExecutionCoordinator(new TaskExecutionRepository(database), new NullSink());
    }

    private sealed class NullSink : IExecutionEventSink
    {
        public bool TryPublish(ExecutionEvent executionEvent) => true;
    }

    private sealed class FixedPaths(string dataDirectory) : IAppPaths
    {
        public string DataDirectory { get; } = dataDirectory;
        public string DatabasePath => System.IO.Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => System.IO.Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => System.IO.Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => System.IO.Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => System.IO.Path.Combine(DataDirectory, "legacy.json");
    }
}
